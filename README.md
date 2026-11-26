# ZSE-Admin

Repozytorium zawiera **ocenę specyfikacji** (`docs/ocena.md`) oraz **działającą aplikację**:
serwer WebSocket na .NET 8 (dane przykładowe albo prawdziwe Active Directory przez LDAP)
i klient WPF. Specyfikacja produktu (poniżej) pozostaje źródłem wymagań.

| Chcesz… | Zajrzyj do |
|---|---|
| poznać ocenę wymagań, ryzyka i luki | [`docs/ocena.md`](docs/ocena.md) |
| zrozumieć komponenty i wdrożenie | [`docs/architektura.md`](docs/architektura.md) |
| zobaczyć kontrakt klient–serwer | [`docs/api-websocket.md`](docs/api-websocket.md) |
| sprawdzić role i RODO | [`docs/uprawnienia-i-dane-osobowe.md`](docs/uprawnienia-i-dane-osobowe.md) |
| uruchomić projekt | [`docs/uruchomienie.md`](docs/uruchomienie.md) |
| zobaczyć plan etapów i kryteria akceptacji | [`docs/roadmapa.md`](docs/roadmapa.md) |

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
│   └── adr/                     decyzje architektoniczne z alternatywami
├── src/
│   ├── ZseAdmin.Contracts/      DTO i protokół (wspólne dla serwera i klienta)
│   ├── ZseAdmin.Server/         ASP.NET Core: WebSocket /ws, routing, uprawnienia, AD/LDAP
│   │   ├── classrooms.json      definicja pracowni i mapowanie OU (konfiguracja, nie kod)
│   │   └── wwwroot/debug.html   konsola protokołu do testów ręcznych
│   ├── ZseAdmin.Client.Core/    klient WS, API typowane, ViewModele (bez WPF)
│   └── ZseAdmin.Client/         aplikacja WPF (XAML)
├── tests/ZseAdmin.Tests/        testy jednostkowe (xUnit)
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

Tokeny deweloperskie (tylko `appsettings.Development.json`): `dev-admin-token` (Administrator), `dev-teacher-token` (Nauczyciel).
Szczegóły, konfiguracja i rozwiązywanie problemów: [`docs/uruchomienie.md`](docs/uruchomienie.md).

## Status

- **M0 (szkielet): gotowe.** Protokół, serwer, klient, testy, CI, dokumentacja.
- **M1 (prawdziwe AD): gotowe.** Adapter LDAP (`System.DirectoryServices.Protocols`), `classrooms.json`
  z walidacją przy starcie, mapowanie OU → pracownia („poza pracownią” zamiast znikania),
  komputery i konta z AD, grupy przez `tokenGroups` jednym zapytaniem, sprawdzanie dostępności
  stacji (ICMP), limity i cache, `upstreamUnavailable` zamiast pustych list. Szczegóły:
  [`docs/adr/0005-adaptery-ad.md`](docs/adr/0005-adaptery-ad.md) i `docs/roadmapa.md`.
- **M2 (historia logowań): zaimplementowane.** Zdalny odczyt dzienników 4624/4634/4625, bufor JSONL
  (`data/logins.jsonl`), retencja 30 dni, kompletność `partial` z notą „N z M stacji bez dostępu”
  zamiast cichej pustej listy. Werifikacja na pilocie (Windows + *Event Log Readers*).
- **M3 (bezpieczeństwo produkcyjne): zaimplementowane.** Windows/Kerberos (Negotiate) + grupy AD → role,
  `wss` wymuszane przed upgrade'em (HTTP 400), nauczyciel widzi wyłącznie history swoich pracowni,
  audyt operacji (`data/audit.jsonl`, 12 mies.), bramka startu blokująca tokeny/brak wss/mock/LDAP bez TLS.
- **M4 (akcje): zaimplementowane.** Restart stacji (`shutdown.exe /r /m`), blokada ekranu (PsExec),
  reset hasła (LDAPS `unicodePwd`), dodanie do grupy — wszystkie z `confirm=true` i wpisem audytowym;
  UI ukrywa akcje bez uprawnienia, nieudana akcja pokazuje błąd źródła, nie „sukces”.
- Tokeny w konfiguracji są rozwiązaniem deweloperskim i **nie mogą** trafić na produkcję
  (`SecurityConfigurationValidator` zatrzymuje serwer, jeśli tak się stanie).
- Szczegóły i stan weryfikacji: [`docs/roadmapa.md`](docs/roadmapa.md) i [`docs/uruchomienie.md`](docs/uruchomienie.md) §9.

### Dwa tryby danych

```jsonc
// appsettings.json
"ZseAdmin": {
  "UseMockData": true,          // development/CI: dane przykładowe, bez domeny
  "UseMockDataForLogins": true  // tryb przejściowy M1.5: prawdziwe AD, logowania z makiety
}
```

Przejście na AD: `UseMockData = false` + uzupełniony `src/ZseAdmin.Server/classrooms.json`
(w tym `teachers` per pracownia) i sekcje `ZseAdmin:Directory`, `Security` (tryb `ActiveDirectory`).
Instrukcja krok po kroku: [`docs/uruchomienie.md`](docs/uruchomienie.md) §5a–5c.
