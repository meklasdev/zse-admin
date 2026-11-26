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

- `http://localhost:5080/healthz` — status, liczba sesji, tryb danych,
- `http://localhost:5080/debug.html` — konsola protokołu (ręczne wywołania `classrooms.list`, `computers.list`, `logins.query`, `users.get`, `ping`).

W konsoli użyj tokenu `dev-admin-token` (Administrator) lub `dev-teacher-token` (Nauczyciel) z `appsettings.json`.

## 3. Klient WPF

```powershell
dotnet run --project src/ZseAdmin.Client
```

Klient czyta `appsettings.json` z katalogu wyjściowego:

```json
{
  "ServerUrl": "ws://localhost:5080/ws",
  "Token": "dev-admin-token"
}
```

Po uruchomieniu łączy się automatycznie, ponawia połączenie z backoffem i po każdym połączeniu pobiera pełny stan.

## 4. Testy

```bash
dotnet test
```

Testy pokrywają: progi świeżości, filtrowanie komputerów i logowań, uprawnienia ról, routing wiadomości, serializację protokołu i formatowanie w UI.

## 5. Konfiguracja serwera

`src/ZseAdmin.Server/appsettings.json`:

| Klucz | Znaczenie | Domyślnie |
|---|---|---|
| `ZseAdmin:Environment` | nazwa środowiska w `/healthz` | `Development` |
| `ZseAdmin:UseMockData` | dane przykładowe zamiast AD | `true` |
| `ZseAdmin:StatusPollSeconds` | interwał sprawdzania stanu (min. 5) | `15` |
| `ZseAdmin:Freshness:ComputersSeconds` | próg starzenia stanu | `120` |
| `ZseAdmin:Freshness:LoginsSeconds` | próg starzenia logowań | `900` |
| `ZseAdmin:Freshness:DirectorySeconds` | próg starzenia katalogu AD | `3600` |
| `Security:RequireToken` | czy wymagać tokenu | `true` |
| `Security:Tokens[]` | lista `Name`/`Token`/`Role` | dwa tokeny dev |

Sekrety trzymaj poza repozytorium: `appsettings.Local.json` (ignorowany przez git) albo zmienne środowiskowe, np.

```powershell
$env:Security__Tokens__0__Token = "…"
```

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

Klienta publikuj osobno i skopiuj na stacje/komputer administratora:

```powershell
dotnet publish src/ZseAdmin.Client -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

## 7. Wariant IIS

1. Zainstaluj rolę IIS + „WebSocket Protocol” (funkcja systemu Windows).
2. Opublikuj aplikację do katalogu witryny (`dotnet publish -o C:\inetpub\zseadmin`).
3. Utwórz aplikację/pulę z „No Managed Code”, ustaw `web.config` z `AspNetCoreModuleV2` (generowany przy publikacji).
4. Wiązanie HTTPS z certyfikatem wewnętrznym; w kliencie ustaw `wss://…`.

## 8. Rozwiązywanie problemów

| Objaw | Prawdopodobna przyczyna | Co zrobić |
|---|---|---|
| Klient: „łączenie z serwerem…” bez końca | zły `ServerUrl` lub serwer nie działa | sprawdź `/healthz` w przeglądarce |
| Błąd 400 na `/ws` | żądanie nie jest upgrade'em WS | użyj klienta WS, nie zwykłego GET |
| Połączenie odrzucone (401) | brak/zły token | sprawdź `Security:Tokens` i `appsettings.json` klienta |
| Odpowiedzi `forbidden` | rola bez uprawnienia | użyj `dev-admin-token` lub zmień rolę |
| `welcome` nie przychodzi | serwer za proxy bez WebSocketów | włącz funkcję WebSocket w IIS, sprawdź `Upgrade` |
| Lista pusta, brak błędu | filtr pracowni bez komputerów | wybierz „Wszystkie pracownie” |
| Wszystko „brak danych” | `statusObservedAtUtc = null` (dane przykładowe) | poczekaj na cykl monitorowania (15 s) |

## 9. Status weryfikacji

Kod powstał w środowisku bez .NET SDK i bez dostępu do NuGet, więc **nie został tu skompilowany ani przetestowany**. Weryfikacja odbywa się przez:

- CI (`.github/workflows/ci.yml`) na `windows-latest`: restore, build, test, publish;
- lokalne `dotnet build` / `dotnet test` na maszynie z .NET 8.

Rzeczy do sprawdzenia przy pierwszym buildzie:

1. `Microsoft.Extensions.Logging` jest używany wyłącznie w projekcie serwera (framework ASP.NET Core) — warstwa `Client.Core` nie ma zależności zewnętrznych.
2. Wersje pakietów testowych (`Microsoft.NET.Test.Sdk` 17.10.0, `xunit` 2.8.1) — jeśli NuGet zgłosi brak, podnieś do najnowszych 2.x/17.x.
3. Uruchomienie klienta WPF wymaga Windows; na Linuksie działa tylko restore/build z `EnableWindowsTargeting`.
4. Pierwszy build na Windows Server 2016 nie wymaga dodatkowych pakietów poza .NET 8 Desktop Runtime dla klienta.
