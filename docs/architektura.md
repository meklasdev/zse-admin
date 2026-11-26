# Architektura

## 1. Założenia

- Klient to aplikacja desktopowa na Windows (szkoła pracuje na Windows), serwer działa na maszynie z Active Directory.
- Komunikacja: WebSocket + JSON. Jeden kanał, wiele typów wiadomości, korelacja po `requestId`.
- Etapy M0–M1 były tylko do odczytu. Od M4 istnieją **jawnie kontrolowane operacje zapisu**: każda wymaga uprawnienia, dwukrokowego potwierdzenia i wpisu audytowego. Nie ma endpointu przyjmującego dowolny zapis/ldap.
- Dane muszą być „samo-opisujące się”: każda odpowiedź niesie źródło, czas obserwacji, kompletność i informację o starzeniu.
- Serwer jest jedynym miejscem egzekwowania uprawnień i zakresu danych; UI jedynie ukrywa to, czego serwer i tak odmówi.

## 2. Komponenty

```
┌─────────────────────────────┐            ┌──────────────────────────────────────────┐
│ ZseAdmin.Client (WPF)       │            │ ZseAdmin.Server (ASP.NET Core, net8.0)   │
│                             │            │                                          │
│ MainWindow.xaml             │  WebSocket │  /ws  → WsConnection → MessageRouter     │
│  ├─ pracownie (lista)       │◄──────────►│           │                              │
│  ├─ komputery (grid)        │   JSON     │           ├─ IClassroomDirectory          │
│  └─ logowania (lista)       │            │           ├─ IComputerDirectory           │
│                             │            │           ├─ ILoginLog                    │
│ Core:                       │            │           ├─ IAdDirectory                  │
│  WsClientConnection         │            │           └─ FreshnessService              │
│  ZseAdminApiClient          │            │                                          │
│ MainViewModel               │            │  StatusMonitorService (BackgroundService)│
│  └─ akcje + audyt (M4)      │            │  LoginCollectorService (M2, ≤ 2 min)    │
└─────────────────────────────┘            │  ConnectionRegistry (broadcast)          │
                                           │  LoginAccessPolicy (M3: zakres pracowni) │
                                           │  AdminActionService + ConfirmationStore  │
                                           │  AuditLog (12 miesięcy)                  │
                                           │  Windows/Token Authenticator             │
                                           └──────────────────────────────────────────┘
                                                     │            │            │
                                            ┌────────┘     ┌──────┘     ┌──────┘
                                            ▼              ▼            ▼
                                     Active Directory   Stacje (ICMP/  Dzienniki
                                     (LDAP + zapis)     WinRM)        stacji 4624/4634/4625
```

| Projekt | Rola | Zależności |
|---|---|---|
| `ZseAdmin.Contracts` | DTO i protokół, wspólne dla obu stron | brak |
| `ZseAdmin.Server` | Serwer WebSocket, źródła danych, uprawnienia | Contracts |
| `ZseAdmin.Client.Core` | Klient WS, API typowane, ViewModel, formatowanie | Contracts |
| `ZseAdmin.Client` | Warstwa WPF (XAML, dispatcher) | Client.Core, Contracts |
| `ZseAdmin.Tests` | Testy jednostkowe serwera i warstwy Core | Server, Client.Core |

Rozdzielenie `Client.Core` od `Client` jest celowe: logika jest testowalna bez WPF, a podmiana klienta na Avalonię lub aplikację webową nie wymaga przepisywania logiki.

## 3. Przepływ żądania

1. Klient łączy się z `/ws` — development: token w nagłówku `X-ZseAdmin-Token` lub `?access_token=`; produkcja: Negotiate/Kerberos i `wss` (brak tokenu).
2. `WsEndpoint` odrzuca połączenie bez `wss` (403) i bez ważnej tożsamości/grupy AD (401) **przed upgrade'em** — odmowy trafiają do audytu.
3. Serwer tworzy sesję i wysyła `welcome` z rolą, uprawnieniami oraz `classroomScope` (zakres pracowni nauczyciela).
4. Klient wysyła kopertę `{ type, requestId, payload }`.
5. `MessageRouter` sprawdza uprawnienie (`CapabilityGuard`), dla `logins.query` nakłada zakres (`LoginAccessPolicy`), pyta źródło i dokłada metadane świeżości. Akcje M4 przechodzą przez `AdminActionService`.
6. Serwer odpowiada tym samym `type`/`requestId`. Błąd → `type: "error"` + `ErrorDto`.
7. Równolegle `StatusMonitorService` rozgłasza `event.computer.status`, a `LoginCollectorService` — `event.logins.new` (z filtrem per sesja). Po reconnect klient i tak pobiera pełny stan.

## 4. Wątki i współbieżność

- Każde połączenie WebSocket ma własną pętlę odbioru. Wysyłka jest serializowana `SemaphoreSlim`, bo zdarzenia push idą z wątku usługi w tle.
- Repozytoria są singletonami; stan w pamięci chroniony jest `lock`.
- Klient wykonuje żądania z puli wątków; zmiany kolekcji do UI idą zawsze przez `IUiDispatcher` (w WPF `Dispatcher.BeginInvoke`).

## 5. Adaptery danych

Stan na dziś: **M0–M4 zaimplementowane** (ADR-0005..0008).

| Interfejs | Implementacja | Uwagi |
|---|---|---|
| `IClassroomDirectory` | `AdClassroomDirectory` / `MockClassroomDirectory` | konfiguracja + liczniki z AD |
| `IComputerDirectory` | `AdComputerDirectory` / `MockComputerDirectory` | paginacja LDAP, limit `MaxComputers`, cache TTL, „poza pracownią” bez filtra |
| `IComputerStatusProbe` | `StatusProbe` (ICMP) | brak odpowiedzi = `offline` z czasem, brak adresu = `unknown` |
| `ILoginEventSource` | `RemoteSecurityEventSource`, `WefFileEventSource`, `MockLoginEventSource` | 4624/4634/4625 + status dostępności każdej stacji |
| `ILoginLog` | `LoginHistoryBuffer` (retencja 30 dni) / `UnavailableLoginLog` | pusta lista nie udaje braku zdarzeń |
| `IAdDirectory` | `AdDirectory` / `MockAdDirectory` | `tokenGroups`; `lastLogonTimestamp` bywa opóźniony do 14 dni |
| `IComputerControl` | `WindowsComputerControl` / `MockComputerControl` | `shutdown.exe`, WinRM; błąd = `ActionFailedException` |
| `IAdWriteService` | `LdapAdWriteService` / `MockAdWriteService` | `unicodePwd` + `pwdLastSet`, `member` — tylko po LDAPS |
| `IAuditLog` | `AuditLog` | 12 miesięcy, opcjonalny JSONL, `audit.query` |
| `ITokenAuthenticator` | `TokenAuthenticator` / `WindowsAuthenticator` | wybór przez `Security:Mode` |

Porty pomocnicze: `ILdapDirectoryReader` (jedyna warstwa znająca LDAP), `IAdInventoryReader`
(wykrywanie komputerów/kont), `IComputerStatusProbe` (sieć). Dzięki temu całą interpretację
AD można testować bez kontrolera domeny.

### 5.1 Wybór źródła danych

```
ZseAdmin:UseMockData = true   → Mock* (development, CI, brak domeny)
ZseAdmin:UseMockData = false  → LDAP + ICMP; wymaga poprawnych classrooms.json i Directory:*
```

Historia logowań ma własne źródło (M2): w trybie `UseMockData=true` zawsze `Mock`,
w produkcji `WindowsEventLog` albo `WefFile`. `LoginHistory:Enabled=false` to tryb awaryjny
z jawną odpowiedzią `unavailable`. Produkcja (`appsettings.Production.json`) wymaga
`Security:Mode=Windows`, `RequireSecureTransport=true`, LDAPS i realnego źródła.

### 5.2 Zachowanie przy błędach źródła

| Sytuacja | Wynik |
|---|---|
| LDAP nieosiągalny / bind nieudany | `error: upstreamUnavailable` z nazwą źródła (`MessageRouter` zamienia `SourceUnavailableException`) |
| Komputer nie odpowiada na ICMP | `status = offline` + `statusObservedAtUtc` (nie znaczy „nie istnieje”) |
| Brak adresu do sprawdzenia | `status = unknown`, poprzednia obserwacja zachowana |
| Inwentarz obcięty limitem | `completeness = partial` + nota z liczbami |
| Brak `classrooms.json` lub kolizja OU | serwer **nie startuje**, błąd na stderr + kod wyjścia 1 |
| Część stacji bez dostępu do dziennika | `completeness = partial` + nota z liczbą stacji |
| Niebezpieczna konfiguracja produkcyjna | serwer **nie startuje** (`ProductionConfigurationGuard`) |
| Akcja odrzucona przez stację/AD | `error: upstreamUnavailable` + wpis audytu `failed` |

Zasada: **adapter nie ukrywa problemów źródła**. Jeśli nie ma dostępu do dziennika stacji, zwraca `Unavailable` z powodem, a nie pustą listę udającą „nikt się nie logował”.

## 6. Definicja pracowni

```json
// src/ZseAdmin.Server/classrooms.json — wczytywany przy starcie (M1)
[
  {
    "id": "prac-101",
    "name": "Pracownia 101",
    "ous": ["OU=PRAC101,OU=Pracownie,DC=zse,DC=local"]
  },
  {
    "id": "prac-102",
    "name": "Pracownia 102",
    "ous": ["OU=PRAC102,OU=Pracownie,DC=zse,DC=local"]
  }
]
```

Reguły:

1. Komputer dopasowany do OU z listy trafia do tej pracowni (także gdy OU jest kilka).
2. Komputer poza listą **nie znika** — trafia do grupy „poza pracownią”.
3. Jedno OU może należeć tylko do jednej pracowni; kolizja to błąd konfiguracji zgłaszany przy starcie.
4. Nazwa pracowni nie jest wyprowadzana z OU — OU bywa techniczne (`OU=KLIENTY-2020`).
5. Przy zagnieżdżonych OU wygrywa najbardziej szczegółowe dopasowanie (np. `OU=PRAC101` przed `OU=Pracownie`).
6. Konfiguracja jest walidowana przy starcie: brak `id`, brak `name`, pusta lista OU i DN
   niebędący distinguishedName to błędy. Serwer nie startuje z błędną mapą, bo „cicha”
   pracownia bez komputerów jest gorsza niż widoczny błąd.

Implementacja: `Infrastructure/Classrooms/ClassroomMap.cs` (`Create` waliduje, `Resolve` mapuje).
Testy: `tests/ZseAdmin.Tests/ClassroomMapTests.cs`.

## 7. Odporność i stany błędów

| Sytuacja | Zachowanie serwera | Zachowanie UI |
|---|---|---|
| Brak połączenia klient↔serwer | — | „brak połączenia”, dane oznaczone jako nieaktualne, automatyczne ponawianie z backoffem |
| Źródło danych nieosiągalne | odpowiedź z `Completeness = Unavailable` lub błąd `UpstreamUnavailable` | komunikat o źródle, nie „pusto” |
| Dane starsze niż próg | `isStale = true` + nota | żółty pasek „dane z 10:42” |
| Brak uprawnienia | `error: Forbidden` | akcja nie jest pokazywana jako wykonalna |
| Nauczyciel poza swoją pracownią | `error: Forbidden` | `welcome.classroomScope` zawęża listę pracowni |
| Awaria akcji M4 | `error: UpstreamUnavailable` ze źródłem | komunikat z `detail`, wpis audytu `failed` |
| Zerwanie w trakcie żądania | klient dostaje wyjątek transportu | retry po ponownym połączeniu, bez cichego „sukcesu” |

## 8. Wdrożenie

**Wariant A (zalecany): usługa Windows + Kestrel.**

- Serwer publikowany jako `dotnet publish -c Release -r win-x64 --self-contained false`.
- Rejestracja przez `sc.exe create` lub NSSM; uruchomienie jako konto usługi z minimalnymi prawami (odczyt OU, Event Log Readers na stacjach).
- Port 5080 w sieci szkolnej, firewall ograniczony do podsieci pracowni.

**Wariant B: IIS.**

- Włączone „WebSocket Protocol” w funkcjach IIS, aplikacja jako podrzędna witryna, binding HTTPS.
- Za reverse proxy trzeba przekazać nagłówek autoryzacji i włączyć `wss`.

**Bezpieczeństwo transportu i tożsamości (M3):** w produkcji wyłącznie `wss` + certyfikat wewnętrzny, `Security:Mode = Windows` (Negotiate/Kerberos lub Windows Auth z IIS), role z grup AD, brak tokenów. `ProductionConfigurationGuard` zatrzymuje serwer przy błędnej konfiguracji. Patrz `docs/uprawnienia-i-dane-osobowe.md` i ADR-0007.

## 9. Obserwowalność

- `/healthz` zwraca status, czas serwera, liczbę sesji, tryb danych, tryb uwierzytelniania, źródło/interwał/retencję/liczbę zdarzeń bufora, stacje, audyt i akcje.
- Logi strukturalne: identyfikator sesji, użytkownik, rola, odrzucone wiadomości z kodem błędu, cykle kolektora logowań.
- Rejestr audytu (`audit.query`): aktor, rola, uprawnienie, cel, przed/po, wynik, adres IP — dla zmian M4 i odmów uwierzytelnienia/transportu.
- Docelowo (M5): licznik czasu odpowiedzi źródeł i alert na liczbę stacji bez dostępu do dziennika.

## 10. Punkty rozszerzeń

- Nowe źródło danych = nowa implementacja istniejącego interfejsu, bez zmian w protokole.
- Nowy typ zapytania = stała w `MessageTypes` + gałąź w `MessageRouter` + DTO w `Contracts`.
- Nowa rola = wpis w `RoleCatalog`, bez zmian w handlerach.
