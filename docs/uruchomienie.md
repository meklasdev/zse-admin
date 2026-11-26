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
| `ZseAdmin:UseMockDataForLogins` | historia logowań z makiety w trybie AD (M1.5) | `true` |
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
| `Security:RequireToken` | czy wymagać tokenu | `true` |
| `Security:Tokens[]` | lista `Name`/`Token`/`Role` | dwa tokeny dev |

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
   | `logins.query` bez M2 | `unavailable` z powodem (albo makieta, gdy `UseMockDataForLogins`) |

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

Kod powstał w środowisku bez .NET SDK i bez dostępu do NuGet, więc **nie został tu skompilowany ani przetestowany**. To samo dotyczy etapu M1 (adapter LDAP). Weryfikacja odbywa się przez:

- CI (`.github/workflows/ci.yml`) na `windows-latest`: restore, build, test, publish;
- lokalne `dotnet build` / `dotnet test` na maszynie z .NET 8.

Rzeczy do sprawdzenia przy pierwszym buildzie:

1. `Microsoft.Extensions.Logging` jest używany wyłącznie w projekcie serwera (framework ASP.NET Core) — warstwa `Client.Core` nie ma zależności zewnętrznych.
2. Wersje pakietów testowych (`Microsoft.NET.Test.Sdk` 17.10.0, `xunit` 2.8.1) — jeśli NuGet zgłosi brak, podnieś do najnowszych 2.x/17.x.
3. Uruchomienie klienta WPF wymaga Windows; na Linuksie działa tylko restore/build z `EnableWindowsTargeting`.
4. M1: pakiet `System.DirectoryServices.Protocols` 8.0.0 — jeśli NuGet zgłosi brak, użyj wersji
   zgodnej z zainstalowanym SDK (8.0.x).
5. M1: `LdapDirectoryReader` używa `PageResultRequestControl` i `SearchResultEntry.Attributes` —
   warto potwierdzić na kontrolerze testowym, że stronicowanie zwraca wszystkie komputery
   (limit 1000 obiektów na stronę) i że `tokenGroups` jest czytane przy uprawnieniach konta usługi.
4. Pierwszy build na Windows Server 2016 nie wymaga dodatkowych pakietów poza .NET 8 Desktop Runtime dla klienta.
