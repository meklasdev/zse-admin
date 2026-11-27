# ZSE-Admin

Repozytorium zawiera **ocenę specyfikacji** (`docs/ocena.md`) oraz **działającą aplikację**:
serwer WebSocket na .NET 8 (dane przykładowe albo prawdziwe Active Directory przez LDAP)
i klient WPF. Zrealizowano etapy **M0–M4**: odczyt AD, historia logowań z retencją,
uwierzytelnianie Windows/grupami AD z `wss`, zakres danych nauczyciela oraz akcje
na stacjach i zapis do AD z potwierdzeniem i audytem. Specyfikacja produktu (poniżej)
pozostaje źródłem wymagań.

| Chcesz… | Zajrzyj do |
|---|---|
| poznać ocenę wymagań, ryzyka i luki | [`docs/ocena.md`](docs/ocena.md) |
| zrozumieć komponenty i wdrożenie | [`docs/architektura.md`](docs/architektura.md) |
| zobaczyć kontrakt klient–serwer | [`docs/api-websocket.md`](docs/api-websocket.md) |
| sprawdzić role i RODO | [`docs/uprawnienia-i-dane-osobowe.md`](docs/uprawnienia-i-dane-osobowe.md) |
| uruchomić projekt | [`docs/uruchomienie.md`](docs/uruchomienie.md) |
| zobaczyć plan etapów i kryteria akceptacji | [`docs/roadmapa.md`](docs/roadmapa.md) |
| uruchomić M2/M3/M4 (logi, Kerberos, akcje) | [`docs/uruchomienie.md`](docs/uruchomienie.md) §5b–5d |
| poznać decyzje architektoniczne M2–M4 | [`docs/adr/`](docs/adr/) — ADR 0006, 0007, 0008 |
| zobaczyć notatki zamawiającego (sale, topologia, `/pepsi`, uczniowie) i podjęte decyzje D1–D10 | [`docs/notatki-zamawiajacego-2026-11-26.md`](docs/notatki-zamawiajacego-2026-11-26.md) |
| poznać obowiązujący styl UI (spójny, flat, zero glow) | [`docs/ui-design-system.md`](docs/ui-design-system.md) |
| przejrzeć backlog zadań dla deweloperów (M6–M8) | [`docs/zadania/`](docs/zadania/) |
| zobaczyć zrzuty UI 100/125/150% DPI bez Windows | artefakt `ui-screenshots` w jobie `screenshots` CI — [`docs/zadania/11-zrzuty-ui-ci.md`](docs/zadania/11-zrzuty-ui-ci.md) |
| sprawdzić decyzję o ikonach i pakiecie `SvgIcons` | [`docs/adr/0009-ikony-svg.md`](docs/adr/0009-ikony-svg.md) — przyjęte warunkowo (D8 = B) |
| przekazać implementację M6–M8 kolejnemu agentowi | [`docs/zadania/PROMPT-dla-kolejnego-agenta.md`](docs/zadania/PROMPT-dla-kolejnego-agenta.md) |

## Cel produktu

ZSE-Admin ma być wspólnym punktem pracy dla administratora IT i — w zakresie
przyznanych uprawnień — nauczyciela. Zamiast szukać informacji osobno w
narzędziach Active Directory, na serwerze i przy poszczególnych stacjach,
użytkownik ma szybko zobaczyć, co dzieje się w szkolnych pracowniach:
jakie są tam komputery, kto i kiedy się na nich logował oraz czy stacje są
dostępne w sieci.

Serwer działa na maszynie z Active Directory (Windows Server 2016) i komunikuje
się z klientem przez WebSocket. Aplikacja ma wspierać codzienną administrację
oraz prowadzenie lekcji; nie powinna sugerować, że dane są aktualne lub
kompletne, jeśli serwer nie może ich potwierdzić.

## Kiedy narzędzie daje największą wartość

- **Przed lekcją w pracowni:** nauczyciel lub administrator sprawdza, które
  komputery są widoczne w sieci i czy ich stan pozwala rozpocząć zajęcia.
- **Problem z kontem ucznia:** administrator odnajduje konto i jego
  przynależność do grup, a następnie może sprawdzić dostępne informacje o
  logowaniach. Każda zmiana w AD powinna być dostępna tylko osobom z
  odpowiednimi uprawnieniami.
- **Ustalenie, kto korzystał ze stacji:** administrator filtruje logowania
  według komputera, użytkownika i czasu, aby wyjaśnić problem lub odtworzyć
  przebieg zajęć.
- **Diagnoza niedostępnego komputera:** administrator sprawdza jego pracownię
  lub OU, ostatnio znany stan i adres IP, a następnie porównuje je z historią
  logowań. Brak bieżącego połączenia nie oznacza, że komputer nie istnieje ani
  że nie logowano się na nim wcześniej.

## Główne byty i przepływ informacji

1. **Pracownia ↔ OU:** OU opisuje położenie obiektu w Active Directory, a
   pracownia — szkolny, fizyczny kontekst. To nie musi być relacja jeden do
   jednego; przypisanie OU do pracowni wymaga jawnej reguły lub konfiguracji.
2. **OU / pracownia → komputer:** komputery należą do struktury AD i mogą być
   grupowane w widoku według pracowni. Warto rozróżniać lokalizację wynikającą
   z OU od faktycznej lokalizacji urządzenia.
3. **Komputer ↔ log logowania ↔ użytkownik AD:** zdarzenie logowania łączy
   rozpoznane konto z komputerem i czasem. Historia jest zdarzeniowa — opisuje
   zarejestrowane logowania, a niekoniecznie wszystkie sesje ani to, kto
   aktualnie siedzi przy komputerze.
4. **Komputer → stan / IP:** stan dostępności i adres IP są informacjami o
   komputerze, zwykle bieżącymi lub ostatnio znanymi. Należy pokazać czas
   ostatniej aktualizacji i odróżnić brak danych od stanu offline.
5. **Klient ↔ serwer:** serwer pobiera lub udostępnia dane AD, logi i informacje
   o stacjach; klient prezentuje je w kontekście wybranej pracowni, komputera
   lub użytkownika. WebSocket zapewnia kanał komunikacji, ale sam w sobie nie
   gwarantuje aktualności ani dostarczenia każdego zdarzenia.

Praktyczny przepływ: **pracownia / OU → komputer → zdarzenia logowania →
użytkownik AD**, z równoległym podglądem **stanu i IP komputera**. Użytkownik
powinien móc przejść od pracowni do stacji i jej logowań, a od logowania do
konta — bez utożsamiania bieżącego stanu urządzenia z jego historią.

## Priorytety i założenia

1. **Szybka orientacja w pracowniach:** łatwo znaleźć komputer i ocenić, czy
   można na nim polegać przed zajęciami.
2. **Wiarygodna historia:** przy logach widoczne są zakres czasu, źródło,
   kompletność oraz rozróżnienie między brakiem zdarzeń a brakiem dostępu do
   danych.
3. **Bezpieczne działania:** odczyt, modyfikacja AD i akcje na stacjach mają
   odrębne, jawne uprawnienia; interfejs nie pokazuje niedostępnej akcji jako
   wykonalnej.
4. **Czytelny status danych:** stan/IP mają oznaczenie świeżości, a awaria
   połączenia klient–serwer jest odróżniona od offline komputera.
5. **Użyteczność w czasie lekcji:** typowe informacje można znaleźć szybko,
   bez konieczności rozumienia całej struktury AD.

## Co doprecyzować przed projektowaniem ekranów

- Jak definiuje się pracownię i mapuje ją na jedną lub wiele jednostek OU?
  Co z komputerami wspólnymi, przeniesionymi lub poza znanym OU?
- Z jakiego źródła i jak długo dostępne są logi? Jakie typy logowań obejmują,
  jak często są pobierane i jak oznaczać luki, opóźnienie lub brak uprawnień?
- Czy historia ma opisywać udane logowania, nieudane próby, wylogowania czy
  aktywne sesje? Jak rozwiązywać zmiany nazw kont i komputerów?
- Jak często odświeżane są stan i IP? Czy IP jest bieżące, czy ostatnio
  zaobserwowane; jaki jest próg uznania urządzenia za offline?
- Jakie role będą korzystać z klienta i które dane oraz działania są dla nich
  dozwolone? Jak potwierdzać i audytować operacje zmieniające stan AD lub
  komputerów?
- Jak serwer sygnalizuje zerwane połączenie WebSocket, błąd pobierania danych
  i ponowne połączenie? Czy klient może bezpiecznie wznowić synchronizację?
- Jak ograniczać widoczność i retencję historii logowań, która może zawierać
  dane o aktywności uczniów i pracowników?

Te odpowiedzi powinny ustalić granice zaufania do danych oraz podstawowe
przypadki błędów, zanim zostanie zaprojektowana nawigacja i szczegóły widoków.

**Stan na dziś:** odpowiedzi domyślne (do potwierdzenia) są zapisane w
[`docs/ocena.md`](docs/ocena.md) §7 i [`docs/roadmapa.md`](docs/roadmapa.md).

---

## Struktura repozytorium

```
├── docs/                        ocena, architektura, protokół, RODO, roadmapa, ADR-y
│   ├── adr/                     decyzje architektoniczne z alternatywami
│   ├── zadania/                 backlog zadań M6–M8 z kryteriami Given/When/Then
│   ├── ui-design-system.md      tokeny, kontrolki, zasada „zero glow”
│   └── notatki-zamawiajacego-2026-11-26.md  surowe notatki + interpretacja + decyzje
├── src/
│   ├── ZseAdmin.Contracts/      DTO i protokół (wspólne dla serwera i klienta; wersja 1)
│   ├── ZseAdmin.Server/         ASP.NET Core: WebSocket /ws, routing, uprawnienia, AD/LDAP
│   │   ├── Infrastructure/Logins/  bufor historii, kolektor, WEF/EventLog, makieta (M2)
│   │   ├── Infrastructure/Audit/   rejestr audytu 12 miesięcy (M3/M4)
│   │   ├── Infrastructure/Windows/ akcje na stacjach (M4)
│   │   ├── Security/              grupy AD, wss, zakres pracowni (M3)
│   │   ├── classrooms.json      definicja pracowni i mapowanie OU (konfiguracja, nie kod)
│   │   ├── appsettings.Production.json  bramka produkcyjna (bez tokenów, z wss)
│   │   └── wwwroot/debug.html   konsola protokołu do testów ręcznych
│   ├── ZseAdmin.Client.Core/    klient WS, API typowane, ViewModele (bez WPF)
│   └── ZseAdmin.Client/         aplikacja WPF (XAML)
├── tests/ZseAdmin.Tests/        testy jednostkowe (xUnit)
├── tools/ZseAdmin.Screenshots/  render realnego okna WPF do PNG (CI, 100/125/150% DPI)
├── scripts/                     uruchomienie i publikacja
└── .github/workflows/ci.yml     build + testy na Windows
```

## Szybki start

```bash
dotnet restore
dotnet run --project src/ZseAdmin.Server      # http://localhost:5080/debug.html
dotnet run --project src/ZseAdmin.Client      # tylko Windows (WPF)
dotnet test
```

Tokeny deweloperskie: `dev-admin-token` (Administrator), `dev-teacher-token` (Nauczyciel).
Szczegóły, konfiguracja i rozwiązywanie problemów: [`docs/uruchomienie.md`](docs/uruchomienie.md).

## Status

- **M0 (szkielet): gotowe.** Protokół, serwer, klient, testy, CI, dokumentacja.
- **M1 (prawdziwe AD): gotowe.** Adapter LDAP (`System.DirectoryServices.Protocols`), `classrooms.json`
  z walidacją przy starcie, mapowanie OU → pracownia, `tokenGroups`, ICMP, limity i cache.
- **M2 (historia logowań): gotowe.** Zdalny odczyt 4624/4634/4625 lub WEF, bufor z deduplikacją,
  retencja 30 dni, `completeness = partial` z liczbą niedostępnych stacji, push `event.logins.new`.
- **M3 (bezpieczeństwo): gotowe.** `Security:Mode = Windows` (Negotiate/Kerberos), role z grup AD,
  wymuszone `wss`, zakres pracowni nauczyciela z planem lekcji, audyt i bramka produkcyjna.
- **M4 (akcje i zapis): gotowe.** Restart/blokada stacji, reset hasła i dodanie do grupy —
  dwukrokowe potwierdzenie, `upstreamUnavailable` przy awarii, wpis audytowy przed/po,
  przyciski widoczne tylko dla ról z uprawnieniem.
- **Następny etap: M5 (wdrożenie).** Usługa Windows, certyfikaty, firewall, DPIA, pilotaż.
- **Nowy backlog (2026-11-26): M6–M8 — w realizacji.** ZAD-01 (system projektowy UI: `Styles/Theme.xaml`
  + `Styles/Controls.xaml`, zero glow, `Segoe UI`) **zrobione** — PR #18; ZAD-06 (uczniowie bez dostępu:
  `Security:DeniedGroups`, deny overrides 401 + `auth.rejected`, fail-close `MessageCapabilities`,
  ekran „Brak dostępu”) **zrobione** — PR #19; ZAD-11 (automatyczne zrzuty UI z realnego klienta WPF
  w GitHub Actions, artefakt `ui-screenshots`) **zrobione** — PR #21; pozostałe zadania w kolejności
  z [`docs/zadania/README.md`](docs/zadania/README.md). Notatki zamawiającego
  (sale i diagram topologii w stylu Packet Tracer, brak dostępu uczniów, hosting plików `/pepsi`,
  konto AD → profil lokalny na stacji, ikony, jednolite UI bez poświaty) oraz 10 zadań dla
  deweloperów z kryteriami Given/When/Then: [`docs/zadania/`](docs/zadania/). Decyzje D1–D10
  **podjęte 2026-11-26** (odpowiedzi ABC + interpretacja): [`docs/notatki-zamawiajacego-2026-11-26.md`](docs/notatki-zamawiajacego-2026-11-26.md) §4.
- **Uwaga:** ADR-0003 („MVP bez bazy”) obowiązuje nadal — bufor logowań i audyt są w pamięci
  procesu, z opcjonalnym plikiem JSONL dla audytu. Nie ma własnej bazy danych.
- Tokeny w `appsettings.json` są wyłącznie deweloperskie; produkcja ich nie czyta i nie startuje,
  jeśli bramka wykryje niebezpieczną konfigurację.

Szczegóły i kryteria akceptacji: [`docs/roadmapa.md`](docs/roadmapa.md).

### Tryby danych i uwierzytelniania

```jsonc
// appsettings.json — development
"ZseAdmin": { "UseMockData": true, "LoginHistory": { "Enabled": true } },
"Security": { "Mode": "Token", "RequireSecureTransport": false }

// appsettings.Production.json — produkcja
"ZseAdmin": { "UseMockData": false, "Directory": { "UseSsl": true },
              "LoginHistory": { "Source": "WindowsEventLog" } },
"Security": { "Mode": "Windows", "RequireSecureTransport": true, "Tokens": [] }
```

W trybie `UseMockData = true` źródło logowań jest zawsze makiety (bez względu na
`LoginHistory:Source`). Klient w produkcji: `wss://…` + `UseWindowsAuth = true`, bez tokenu;
publikuj go przez `scripts/publish-client.ps1`, bo zwykły `dotnet publish` kopiuje
deweloperski token. Instrukcja krok po kroku: [`docs/uruchomienie.md`](docs/uruchomienie.md) §5a–5d.
