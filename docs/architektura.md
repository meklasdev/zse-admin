# Architektura

## 1. Założenia

- Klient to aplikacja desktopowa na Windows (szkoła pracuje na Windows), serwer działa na maszynie z Active Directory.
- Komunikacja: WebSocket + JSON. Jeden kanał, wiele typów wiadomości, korelacja po `requestId`.
- MVP jest **tylko do odczytu**. Żadna operacja zapisu do AD ani akcja na stacji nie istnieje w pierwszej wersji.
- Dane muszą być „samo-opisujące się”: każda odpowiedź niesie źródło, czas obserwacji, kompletność i informację o starzeniu.

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
│  MainViewModel              │            │  StatusMonitorService (BackgroundService)│
└─────────────────────────────┘            │  ConnectionRegistry (broadcast)          │
                                           │  TokenAuthenticator → ClientIdentity     │
                                           └──────────────────────────────────────────┘
                                                     │            │            │
                                            ┌────────┘     ┌──────┘     ┌──────┘
                                            ▼              ▼            ▼
                                     Active Directory   Stacje (ICMP/  Dzienniki
                                     (LDAP/Win32 API)    SMB/WMI)      stacji
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

1. Klient łączy się z `/ws` z tokenem (nagłówek `X-ZseAdmin-Token` lub `?access_token=`).
2. Serwer uwierzytelnia, tworzy sesję i wysyła `welcome` z rolą i listą uprawnień.
3. Klient wysyła kopertę `{ type, requestId, payload }`.
4. `MessageRouter` sprawdza uprawnienie (`CapabilityGuard`), pyta odpowiednie źródło i dokłada metadane świeżości.
5. Serwer odpowiada tą samą wartością `type` i tym samym `requestId`. Błąd → `type: "error"` + `ErrorDto`.
6. Równolegle `StatusMonitorService` odświeża stan komputerów i rozgłasza `event.computer.status` do sesji z `computers.read`.

## 4. Wątki i współbieżność

- Każde połączenie WebSocket ma własną pętlę odbioru. Wysyłka jest serializowana `SemaphoreSlim`, bo zdarzenia push idą z wątku usługi w tle.
- Repozytoria są singletonami; stan w pamięci chroniony jest `lock`.
- Klient wykonuje żądania z puli wątków; zmiany kolekcji do UI idą zawsze przez `IUiDispatcher` (w WPF `Dispatcher.BeginInvoke`).

## 5. Adaptery danych

Stan na dziś: **M1 zaimplementowane** (ADR-0005), M2 w planie.

| Interfejs | Implementacja M1 | Uwagi |
|---|---|---|
| `IClassroomDirectory` | `AdClassroomDirectory` (konfiguracja + liczniki z AD) | pracownia istnieje także wtedy, gdy OU jest puste |
| `IComputerDirectory` | `AdComputerDirectory` (AD + `IComputerStatusProbe`) | paginacja LDAP, limit `MaxComputers`, cache TTL, „poza pracownią” bez filtra |
| `IComputerDirectory.RefreshStatusesAsync` | ICMP (`StatusProbe`) | równoległość ograniczona; brak odpowiedzi = `offline` z czasem, brak adresu = `unknown` |
| `ILoginLog` | `MockLoginLog` (dev) / `UnavailableLoginLog` (produkcja do M2) | pusta lista nie udaje braku zdarzeń |
| `IAdDirectory` | `AdDirectory` (LDAP `tokenGroups`) | `lastLogonTimestamp` bywa opóźniony do 14 dni |

Porty pomocnicze: `ILdapDirectoryReader` (jedyna warstwa znająca LDAP), `IAdInventoryReader`
(wykrywanie komputerów/kont), `IComputerStatusProbe` (sieć). Dzięki temu całą interpretację
AD można testować bez kontrolera domeny.

### 5.1 Wybór źródła danych

```
ZseAdmin:UseMockData = true   → Mock* (development, CI, brak domeny)
ZseAdmin:UseMockData = false  → LDAP + ICMP; wymaga poprawnych classrooms.json i Directory:*
```

Tryb przejściowy M1.5: `UseMockData=false` + `UseMockDataForLogins=true` — prawdziwe AD,
przykładowe logowania, żeby dopracować UI przed M2.

### 5.2 Zachowanie przy błędach źródła

| Sytuacja | Wynik |
|---|---|
| LDAP nieosiągalny / bind nieudany | `error: upstreamUnavailable` z nazwą źródła (`MessageRouter` zamienia `SourceUnavailableException`) |
| Komputer nie odpowiada na ICMP | `status = offline` + `statusObservedAtUtc` (nie znaczy „nie istnieje”) |
| Brak adresu do sprawdzenia | `status = unknown`, poprzednia obserwacja zachowana |
| Inwentarz obcięty limitem | `completeness = partial` + nota z liczbami |
| Brak `classrooms.json` lub kolizja OU | serwer **nie startuje**, błąd na stderr + kod wyjścia 1 |

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
| Zerwanie w trakcie żądania | klient dostaje wyjątek transportu | retry po ponownym połączeniu, bez cichego „sukcesu” |

## 8. Wdrożenie

**Wariant A (zalecany): usługa Windows + Kestrel.**

- Serwer publikowany jako `dotnet publish -c Release -r win-x64 --self-contained false`.
- Rejestracja przez `sc.exe create` lub NSSM; uruchomienie jako konto usługi z minimalnymi prawami (odczyt OU, Event Log Readers na stacjach).
- Port 5080 w sieci szkolnej, firewall ograniczony do podsieci pracowni.

**Wariant B: IIS.**

- Włączone „WebSocket Protocol” w funkcjach IIS, aplikacja jako podrzędna witryna, binding HTTPS.
- Za reverse proxy trzeba przekazać nagłówek autoryzacji i włączyć `wss`.

**Bezpieczeństwo transportu:** w produkcji wyłącznie `wss` + certyfikat wewnętrzny. Tokeny MVP są rozwiązaniem deweloperskim — patrz `docs/uprawnienia-i-dane-osobowe.md`.

## 9. Obserwowalność

- `/healthz` zwraca status, czas serwera, liczbę aktywnych sesji i informację, czy działa na danych przykładowych.
- Logi strukturalne: identyfikator sesji, użytkownik, rola, odrzucone wiadomości z kodem błędu.
- Docelowo: licznik czasu odpowiedzi źródeł i liczba stacji bez dostępu — to jedyne metryki, które realnie ostrzegą przed „cichą” utratą danych.

## 10. Punkty rozszerzeń

- Nowe źródło danych = nowa implementacja istniejącego interfejsu, bez zmian w protokole.
- Nowy typ zapytania = stała w `MessageTypes` + gałąź w `MessageRouter` + DTO w `Contracts`.
- Nowa rola = wpis w `RoleCatalog`, bez zmian w handlerach.
