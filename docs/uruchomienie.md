# Uruchomienie i praca z repozytorium

## 1. Wymagania

| Element | Wersja | Uwagi |
|---|---|---|
| .NET SDK | 8.0 | `global.json` dopuszcza nowsze (`latestMajor`) |
| Windows | 10/11 lub Server 2016+ | klient WPF wymaga pulpitu (Desktop Experience) |
| Przeglądarka | dowolna nowoczesna | tylko do konsoli `debug.html` |
| Sieć | dostęp do maszyny z AD | w etapie M1+ |

Serwer działa również na Linuksie (bez adapterów AD), co ułatwia development bez domeny.

## 2. Szybki start (serwer z danymi przykładowymi)

```bash
dotnet restore
dotnet build -c Debug
dotnet run --project src/ZseAdmin.Server
```

Serwer nasłuchuje na `http://0.0.0.0:5080`:

- `http://localhost:5080/healthz` — status, liczba sesji, tryb danych, źródło i wiek bufora logowań, tryb uwierzytelniania,
- `http://localhost:5080/debug.html` — konsola protokołu (M0–M4: `classrooms.list`, `computers.list`, `logins.query`, `users.get`, `audit.query`, `action.prepare`, `action.execute`, `ping`).

W konsoli użyj tokenu `dev-admin-token` (Administrator) lub `dev-teacher-token` (Nauczyciel) z `appsettings.json`.
Kolektor logowań startuje od razu i co 60 s dopisuje nowe zdarzenia (`event.logins.new`); w danych przykładowych
2 z 6 stacji symulują brak dostępu do dziennika, więc `freshness.completeness = partial`.

## 3. Klient WPF

```powershell
dotnet run --project src/ZseAdmin.Client
```

Klient czyta `appsettings.json` z katalogu wyjściowego:

```json
{
  "ServerUrl": "ws://localhost:5080/ws",
  "Token": "dev-admin-token",
  "UseWindowsAuth": false
}
```

Produkcja (M3): ustaw `"ServerUrl": "wss://zse-srv.zse.local:5080/ws"`, `"UseWindowsAuth": true`
i usuń `Token` — klient użyje `UseDefaultCredentials` (Negotiate/Kerberos), a serwer wyliczy rolę z grup AD.

Po uruchomieniu klient łączy się automatycznie, ponawia połączenie z backoffem, pobiera pełny stan,
a z `welcome` odczytuje uprawnienia: akcje i audyt są widoczne tylko dla ról, które je mają.

## 4. Testy

```bash
dotnet test
```

Testy pokrywają: progi świeżości, filtrowanie komputerów i logowań, uprawnienia ról, routing wiadomości, serializację protokołu, formatowanie w UI oraz kryteria M2/M3/M4 (retencja 30 dni, kompletność 2 z 6 stacji, grupy AD i `wss`, zakres pracowni nauczyciela, potwierdzenie akcji, audyt przed/po, błąd źródła).

CI na `windows-latest` wykonuje restore, build, testy, publish i kontrolę `appsettings.Production.json`.
Gdy test nie przejdzie, krok „Szczegóły nieudanych testów” wypisuje nazwę i komunikat jako adnotację GitHub.

## 5. Konfiguracja serwera

`src/ZseAdmin.Server/appsettings.json`:

| Klucz | Znaczenie | Domyślnie |
|---|---|---|
| `ZseAdmin:Environment` | nazwa środowiska w `/healthz` | `Development` |
| `ZseAdmin:UseMockData` | dane przykładowe zamiast AD | `true` |
| `ZseAdmin:UseMockDataForLogins` | przestarzały przełącznik M1.5; produkcja wymaga `false` | `false` |
| `ZseAdmin:Classrooms:Path` | plik z definicją pracowni i OU | `classrooms.json` |
| `ZseAdmin:Directory:Host` | kontroler domeny (puste = wybór z `Domain`) | `zse-dc01.zse.local` |
| `ZseAdmin:Directory:Domain` | domena do wyboru DC i logowania konta usługi | `zse.local` |
| `ZseAdmin:Directory:SearchBase` | baza wyszukiwania LDAP | `DC=zse,DC=local` |
| `ZseAdmin:Directory:UseSsl` | LDAPS (636) — wymagane na produkcji | `false` |
| `ZseAdmin:Directory:ServiceAccountUser/Password` | konto usługi; puste = konto procesu | puste |
| `ZseAdmin:Directory:PageSize` | stronicowanie LDAP (limit 1000) | `500` |
| `ZseAdmin:Directory:MaxComputers` | limit komputerów z AD | `2000` |
| `ZseAdmin:Directory:CacheSeconds` | TTL cache katalogu (czas do pojawienia się nowego komputera) | `60` |
| `ZseAdmin:StatusProbe:Enabled` | sprawdzanie dostępności (ICMP) | `true` |
| `ZseAdmin:StatusProbe:TimeoutMilliseconds` | timeout pingu | `1000` |
| `ZseAdmin:StatusProbe:MaxParallelism` | ile stacji pingować równolegle | `32` |
| `ZseAdmin:StatusPollSeconds` | interwał sprawdzania stanu (min. 5) | `15` |
| `ZseAdmin:Freshness:ComputersSeconds` | próg starzenia stanu | `120` |
| `ZseAdmin:Freshness:LoginsSeconds` | próg starzenia logowań | `900` |
| `ZseAdmin:Freshness:DirectorySeconds` | próg starzenia katalogu AD | `3600` |
| `ZseAdmin:Freshness:AuditSeconds` | próg starzenia rejestru audytu | `3600` |
| `ZseAdmin:LoginHistory:Enabled` | kolektor + bufor M2 (`false` = jawnie `unavailable`) | `true` |
| `ZseAdmin:LoginHistory:Source` | `WindowsEventLog`, `WefFile`, `Mock` (tylko dev) | `WindowsEventLog` |
| `ZseAdmin:LoginHistory:PollSeconds` | interwał odczytu, przycinany do 10–120 s (≤ 2 min) | `60` |
| `ZseAdmin:LoginHistory:RetentionDays` | retencja zdarzeń w buforze | `30` |
| `ZseAdmin:LoginHistory:MaxEvents` | limit bufora | `20000` |
| `ZseAdmin:LoginHistory:OverlapMinutes` | nakładanie odczytów (deduplikacja w buforze) | `5` |
| `ZseAdmin:LoginHistory:WefDirectory` | katalog `*.jsonl` + `stations.json` dla WEF | `wef` |
| `ZseAdmin:Audit:RetentionMonths` | retencja wpisów audytu | `12` |
| `ZseAdmin:Audit:FilePath` | opcjonalny plik JSONL; pusty = tylko pamięć | pusty (prod.: `C:\ProgramData\ZseAdmin\audit.jsonl`) |
| `ZseAdmin:Actions:ConfirmationSeconds` | ważność jednorazowego potwierdzenia | `120` |
| `ZseAdmin:Actions:AllowWrites` | wyłącznik bezpieczeństwa dla wszystkich zapisów | `true` |
| `Security:Mode` | `Token` (dev) albo `Windows` (produkcja) | `Token` |
| `Security:RequireSecureTransport` | odrzuć połączenia bez HTTPS/`wss` | `false` (dev), `true` (prod) |
| `Security:RequireToken` | czy wymagać tokenu w trybie `Token` | `true` |
| `Security:Tokens[]` | lista `Name`/`Token`/`Role` (ignorowana w trybie Windows) | dwa tokeny dev |
| `Security:RoleGroups` | mapowanie grup AD → rola | 3 grupy ZSE-Admin-* |
| `Security:DeniedGroups` | grupy wykluczające (D10: deny overrides) — członek nigdy nie dostaje sesji, nawet z grupą `ZSE-Admin-*`; w produkcji wymagana, bez grup szerokich | `["ZSE-Uczniowie"]` |
| `Security:TeacherClassrooms` | stałe przypisanie nauczyciela do pracowni | mapa dev |
| `Security:TeachingSlots` | okna zajęć (dni i godziny UTC) | pusta |
| `Security:EnforceTeacherClassroomScope` | ogranicz historię do pracowni z zajęciami | `true` |

`Security:DeniedGroups` w produkcji: podmień wzorzec `ZSE-Uczniowie` na rzeczywistą, **wąską** grupę
uczniowską w domenie. `ProductionConfigurationGuard` nie wystartuje, gdy lista jest pusta albo zawiera
`Domain Users`, `Users`, `Everyone`, `Authenticated Users` lub `Domain Computers` (zablokowałyby
konta usługowe). Odmowa trafia do audytu jako `auth.rejected` (`detail` = `denied_group`), a klient
pokazuje ekran „Brak dostępu” — nie puste tabele.

Sekrety trzymaj poza repozytorium: `appsettings.Local.json` (ignorowany przez git) albo zmienne środowiskowe, np.

```powershell
$env:Security__Tokens__0__Token = "…"
```

## 5a. Tryb z prawdziwym Active Directory (etap M1)

1. Uzupełnij `classrooms.json` (obok pliku exe lub w katalogu publikacji):

   ```json
   [
     { "id": "prac-101", "name": "Pracownia 101", "ous": ["OU=PRAC101,OU=Pracownie,DC=zse,DC=local"] }
   ]
   ```

   Jedno OU może należeć tylko do jednej pracowni. Kolizja, brak `id`/`name` albo DN
   w złym formacie **zatrzymuje serwer** z komunikatem na stderr — to celowe (kryterium M1).

2. Wyłącz dane przykładowe i wskaż katalog:

   ```powershell
   $env:ZseAdmin__UseMockData = "false"
   $env:ZseAdmin__Directory__SearchBase = "DC=zse,DC=local"
   $env:ZseAdmin__Directory__Domain = "zse.local"
   # konto usługi: tylko odczyt OU i Event Log Readers, bez admina domeny
   $env:ZseAdmin__Directory__ServiceAccountUser = "svc-zseadmin"
   $env:ZseAdmin__Directory__ServiceAccountPassword = "…"   # nie do repozytorium
   dotnet run --project src/ZseAdmin.Server
   ```

   Albo prościej: konto usługi Windows uruchamiające serwer i puste
   `ServiceAccountUser` (uwierzytelnianie kontem procesu).

3. Sprawdź `/healthz` — sekcja `ldap` pokazuje host, bazę i czy LDAP idzie po SSL,
   a `classrooms` liczbę wczytanych pracowni.

4. Co sprawdzić ręcznie (kryteria M1):

   | Sprawdzenie | Oczekiwany wynik |
   |---|---|
   | nowy komputer w OU pracowni | pojawia się po `CacheSeconds`, bez restartu |
   | komputer poza OU | widoczny jako „poza pracownią” |
   | to samo OU w dwóch pracowniach | serwer nie startuje, czytelny błąd |
   | konto z wieloma grupami | wszystkie grupy, bez ucięcia |
   | nieistniejące konto | `notFound` |
   | zły host DC | `upstreamUnavailable`, nie pusta lista |
   | `logins.query` (M2) | zdarzenia z bufora + `completeness` i nota o niedostępnych stacjach |

## 5b. Historia logowań (M2)

Domyślne źródło `WindowsEventLog` czyta zdalnie dzienniki Security stacji (4624/4634/4625)
przez `EventLogReader`. Konto usługi musi należeć do **Event Log Readers** na stacjach;
brak dostępu do części stacji daje `completeness = partial` z liczbą stacji.

Wariant WEF: ustaw `ZseAdmin:LoginHistory:Source = "WefFile"` i zapisuj w katalogu
`WefDirectory` pliki `*.jsonl`:

```json
{"computerName":"KS-101-01","userSamAccountName":"j.kowalski","occurredAtUtc":"2026-11-25T07:42:11Z","eventId":4624,"classroomId":"prac-101"}
```

oraz `stations.json` z heartbeatem: `[{"computerName":"KS-101-01","lastSeenUtc":"2026-11-25T10:00:00Z"}]`.
Stacja bez świeżego heartbeatu jest raportowana jako niedostępna. `classroomId` jest dokładane
z inwentarza komputerów i używane przez politykę zakresu (M3).

Bufor: retencja 30 dni, deduplikacja, limit `MaxEvents`, push `event.logins.new` do sesji
z `logins.read`. Gdy `LoginHistory:Enabled = false`, serwer zwraca jawnie `unavailable`.

## 5c. Produkcja: Windows/Kerberos, wss, zakres nauczyciela (M3)

Uruchom z `ASPNETCORE_ENVIRONMENT=Production` (albo `ZseAdmin:Environment = Production`).
`appsettings.Production.json` wyłącza dane przykładowe i tokeny, wymusza `Mode = Windows`,
`RequireSecureTransport = true`, LDAPS oraz realne źródło logowań. `Program.cs` czyści
odziedziczone tokeny i **nie startuje**, jeśli bramka `ProductionConfigurationGuard` znajdzie błąd.

Grupy AD (konfiguracja `Security:RoleGroups`): `ZSE-Admin-Administratorzy`,
`ZSE-Admin-Nauczyciele`, `ZSE-Admin-ReadOnly`. Użytkownik spoza nich dostaje 401 przed
upgrade'em WebSocket. Kestrel jako usługa Windows używa `AddNegotiate()`; w IIS/HTTP.sys
tożsamość dostarcza host.

Zakres nauczyciela — `Security:TeacherClassrooms` albo okna z planu lekcji:

```json
{
  "Security": {
    "TeacherClassrooms": { "k.mazur": ["prac-101"] },
    "TeachingSlots": [
      { "teacher": "k.mazur", "classroomId": "prac-205", "days": ["Monday"], "startUtc": "09:00", "endUtc": "11:00" }
    ]
  }
}
```

Dni i godziny są w UTC. Brak aktywnych zajęć = `forbidden` dla `logins.query`;
`welcome.classroomScope` informuje UI, które pracownie pokazać. Zdarzenia push są filtrowane
per sesja. Odmowy uwierzytelnienia i transportu trafiają do audytu.

## 5d. Akcje i audyt (M4)

Wszystkie zmiany przechodzą przez dwa kroki: `action.prepare` (opis, stan „przed”,
jednorazowy token ważny 120 s, ostrzeżenia) → `action.execute` (`confirm: true`).
Serwer sprawdza `computers.control` / `users.write` w obu krokach oraz ponownie przy wykonaniu.

| Akcja | Uprawnienie | Realna implementacja |
|---|---|---|
| `restartComputer` | `computers.control` | `shutdown.exe /r /m \\stacja /t 30 /f` |
| `lockComputer` | `computers.control` | WinRM + `LockWorkStation` (wymaga WinRM na stacji) |
| `resetPassword` | `users.write` | LDAPS `unicodePwd` + `pwdLastSet = 0`; hasło tymczasowe generuje serwer |
| `addToGroup` | `users.write` | LDAP `member` na grupie; operacja idempotentna |

Błąd źródła (niezerowy kod `shutdown.exe`, odmowa WinRM/LDAP) wraca jako
`error: upstreamUnavailable` z nazwą źródła i **wpisem audytu o porażce** — nigdy jako sukces.
`audit.query` (tylko `audit.read`) pokazuje aktora, rolę, cel, wartość przed/po, wynik i adres IP;
te same wpisy są widoczne w zakładce „Akcje i audyt” klienta WPF. Hasła są maskowane.
Retencja audytu: 12 miesięcy, opcjonalny plik `ZseAdmin:Audit:FilePath` (JSONL, append-only).

## 5e. Zrzuty ekranu UI (ZAD-11)

Zrzuty generuje narzędzie renderujące **realne** `MainWindow.xaml` (bez makiety HTML) na danych
z serwera w trybie `UseMockData = true`. Wymaga Windows (WPF) i uruchomionego serwera:

```powershell
# 1) serwer z danymi przykładowymi (osobne okno)
dotnet run --project src/ZseAdmin.Server -c Release

# 2) zrzuty 100 / 125 / 150% DPI do katalogu screenshots/
dotnet run --project tools/ZseAdmin.Screenshots -c Release -- `
  --server ws://127.0.0.1:5080/ws --token dev-admin-token --out screenshots
```

Pliki: `main-100/125/150.png` (zakładka „Logowania”) i `akcje-i-audyt-100/125/150.png`.
Katalog `screenshots/` jest ignorowany przez git. Kody wyjścia: `0` sukces, `2` brak danych
w czasie `--wait`, `3` błąd renderowania, `4` odmowa dostępu, `5` podejrzanie pusty zrzut.
W CI robi to job `screenshots` (`pull_request` / `workflow_dispatch`) i publikuje artefakt
`ui-screenshots` — szczegóły: [`zadania/11-zrzuty-ui-ci.md`](zadania/11-zrzuty-ui-ci.md).

## 6. Publikacja na Windows Server 2016

```powershell
./scripts/publish-server.ps1 -Configuration Release -Output C:\Apps\ZseAdmin
```

Skrypt publikuje serwer i wypisuje komendę rejestracji usługi. Ręcznie:

```powershell
sc.exe create ZseAdmin binPath= "C:\Program Files\dotnet\dotnet.exe C:\Apps\ZseAdmin\ZseAdmin.Server.dll" start= auto
sc.exe description ZseAdmin "ZSE-Admin — serwer WebSocket dla pracowni"
sc.exe start ZseAdmin
```

Klienta produkcyjnego publikuj skryptem — podmienia on `appsettings.json` na
`wss` + `UseWindowsAuth = true` i **bez tokenu**:

```powershell
./scripts/publish-client.ps1 -Configuration Release -Output C:\Apps\ZseAdminClient
# wariant jednego pliku: -Runtime win-x64 -SingleFile
# po publikacji uzupełnij tylko ServerUrl w C:\Apps\ZseAdminClient\appsettings.json
```

Zwykły `dotnet publish src/ZseAdmin.Client` kopiuje deweloperski `dev-admin-token` —
używaj go tylko lokalnie. CI wykonuje tę samą podmianę i sprawdza artefakt.

Przed startem usługi ustaw zmienne procesu (M3):

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:ZseAdmin__Directory__ServiceAccountUser = "svc-zseadmin"
$env:ZseAdmin__Directory__ServiceAccountPassword = "…"   # magazyn sekretów, nie repozytorium
```

Konto usługi: odczyt OU, **Event Log Readers** na stacjach, prawo resetu haseł i modyfikacji
`member` tylko na wyznaczonych OU/grupach, WinRM na stacjach do blokady — bez praw administratora
domeny. Wymagany certyfikat dla `wss` i LDAPS (`Directory:UseSsl = true`).

## 7. Wariant IIS

1. Zainstaluj rolę IIS + „WebSocket Protocol” (funkcja systemu Windows).
2. Opublikuj aplikację do katalogu witryny (`dotnet publish -o C:\inetpub\zseadmin`).
3. Utwórz aplikację/pulę z „No Managed Code”, ustaw `web.config` z `AspNetCoreModuleV2` (generowany przy publikacji).
4. Wiązanie HTTPS z certyfikatem wewnętrznym; w kliencie ustaw `wss://…` i `UseWindowsAuth = true`.
5. Włącz Windows Authentication (Negotiate/Kerberos) i wyłącz Anonymous. `UseForwardedHeaders`
   w `Program.cs` przepisuje `X-Forwarded-Proto` od IIS, więc `Security:RequireSecureTransport`
   egzekwuje `wss` także za proxy.

## 8. Rozwiązywanie problemów

| Objaw | Prawdopodobna przyczyna | Co zrobić |
|---|---|---|
| Klient: „łączenie z serwerem…” bez końca | zły `ServerUrl` lub serwer nie działa | sprawdź `/healthz` w przeglądarce |
| Błąd 400 na `/ws` | żądanie nie jest upgrade'em WS | użyj klienta WS, nie zwykłego GET |
| Połączenie odrzucone (401) | brak/zły token albo brak grupy AD | sprawdź `Security:Tokens`/`Security:RoleGroups` i `appsettings.json` klienta |
| Połączenie odrzucone (403) | połączenie bez `wss` przy `RequireSecureTransport` | użyj `wss://` i certyfikatu; za proxy włącz `UseForwardedHeaders` |
| Serwer nie startuje w produkcji | `ProductionConfigurationGuard` znalazł błąd | przeczytaj komunikaty na stderr: tokeny, `UseMockData`, brak LDAPS/wss, źródło Mock |
| `logins.query` zwraca `forbidden` | nauczyciel bez zajęć w tej pracowni | uzupełnij `TeacherClassrooms`/`TeachingSlots` albo wybierz własną pracownię |
| `completeness = partial` w historii | część stacji bez dostępu do dziennika | dodaj konto usługi do Event Log Readers; nota podaje liczbę stacji |
| `action.execute` zwraca `upstreamUnavailable` | stacja/AD odmówiły wykonania | sprawdź WinRM, LDAPS i prawa konta; wpis audytu ma `result = failed` |
| Odpowiedzi `forbidden` | rola bez uprawnienia | użyj `dev-admin-token` lub zmień rolę |
| `welcome` nie przychodzi | serwer za proxy bez WebSocketów | włącz funkcję WebSocket w IIS, sprawdź `Upgrade` |
| Lista pusta, brak błędu | filtr pracowni bez komputerów | wybierz „Wszystkie pracownie” |
| Wszystko „brak danych” | `statusObservedAtUtc = null` (dane przykładowe) | poczekaj na cykl monitorowania (15 s) |

## 9. Status weryfikacji

Kod M0–M4 jest weryfikowany przez CI (`.github/workflows/ci.yml`) na `windows-latest`:
restore, build, testy jednostkowe, publish serwera i klienta oraz kontrolę artefaktu
(`classrooms.json`, `appsettings.Production.json`). W środowisku developerskim bez SDK
kompilacji nie wykonano lokalnie — wynik CI jest wiążący. Lokalnie: `dotnet build` / `dotnet test`.

Pakiety serwera (wszystkie 8.0.x, restore w CI):

| Pakiet | Etap | Po co |
|---|---|---|
| `System.DirectoryServices.Protocols` | M1 / M4 | odczyt i zapis LDAP (`unicodePwd`, `member`) |
| `System.Diagnostics.EventLog` | M2 | `EventLogReader` do dzienników 4624/4634/4625 |
| `Microsoft.AspNetCore.Authentication.Negotiate` | M3 | Kerberos dla Kestrel jako usługi Windows |

Rzeczy do potwierdzenia w pilotażu (M5, wymagają domeny):

1. `EventLogReader` z kontem usługi w Event Log Readers czyta zdalne dzienniki i stronicowanie
   LDAP zwraca wszystkie komputery (`tokenGroups` bez ucięcia).
2. Zapis `unicodePwd` działa wyłącznie po LDAPS, a `pwdLastSet = 0` wymusza zmianę hasła.
3. `shutdown.exe /r /m` i blokada przez WinRM działają na stacjach w pracowni; brak uprawnień
   daje `upstreamUnavailable`, nie sukces.
4. IIS/HTTP.sys przekazuje tożsamość Windows i `X-Forwarded-Proto`, więc `wss` jest egzekwowane.
5. Plan lekcji (`TeachingSlots`) jest uzupełniony, a DPIA i klauzula informacyjna zatwierdzone.
