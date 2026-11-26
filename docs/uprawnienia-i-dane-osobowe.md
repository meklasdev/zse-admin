# Uprawnienia, audyt i dane osobowe

## 1. Zasada nadrzędna

Serwer jest jedynym miejscem egzekwowania uprawnień. Klient może ukrywać akcje, ale **ukrycie w UI nie jest zabezpieczeniem** — każde żądanie jest sprawdzane w `MessageRouter` przez `CapabilityGuard`, a akcje M4 dodatkowo ponownie przy `action.execute`. Od M3 serwer odrzuca też nieuwierzytelnione połączenia i ruch bez `wss` **przed** upgrade'em WebSocket.

## 2. Role i uprawnienia

| Uprawnienie | Administrator | Nauczyciel | ReadOnly |
|---|---|---|---|
| `classrooms.read` | ✔ | ✔ | ✔ |
| `computers.read` | ✔ | ✔ | ✔ |
| `logins.read` | ✔ | ✔ | ✔ |
| `users.read` | ✔ | ✔ | ✘ |
| `users.write` | ✔ | ✘ | ✘ |
| `computers.control` | ✔ | ✘ | ✘ |
| `audit.read` | ✔ | ✘ | ✘ |

Definicje w kodzie: `src/ZseAdmin.Server/Security/Capability.cs` i `RoleCatalog.cs`. Testy pilnują, że Nauczyciel nie dostaje `users.write` ani `computers.control`, a `MessageRouter` i `AdminActionService` odrzucają próby obejścia UI (kryterium M4). `audit.read` jest widoczne wyłącznie w `welcome.capabilities` Administratora.

**Po co trzy role, a nie dwie:** nauczyciel potrzebuje historii logowań (kto siedział przy stacji), ale nie powinien widzieć całej kartoteki kont ani niczego zmieniać. `ReadOnly` to profil dla dyżurki/sekretariatu — widok bez danych o kontach.

## 3. Uwierzytelnianie

| Tryb | Mechanizm | Status |
|---|---|---|
| `Security:Mode = Token` (development) | statyczne tokeny w `appsettings.json`, rola przypisana do tokenu | wyłącznie praca lokalna/CI |
| `Security:Mode = Windows` (produkcja) | Negotiate/Kerberos z `HttpContext.User`, rola z grup AD (`ZSE-Admin-Administratorzy`, `ZSE-Admin-Nauczyciele`, `ZSE-Admin-ReadOnly`) | zaimplementowane (M3) |

`WindowsAuthenticator` fail-close: brak którejkolwiek skonfigurowanej grupy `ZSE-Admin-*`
oznacza HTTP 401 — sesja nie powstaje, a odmowa (`auth.rejected`) trafia do audytu.
Przynależność jest normalizowana z postaci `DOMENA\Grupa`, a kolejność ról przy wielu
grupach: Administrator → Nauczyciel → ReadOnly. Token jest porównywany w czasie stałym.

**Bramka wdrożeniowa (M3):** `ProductionConfigurationGuard` zatrzymuje start serwera, gdy
produkcja ma choć jeden z błędów: tryb inny niż Windows, niepuste `Security:Tokens`,
`RequireSecureTransport = false`, dane przykładowe, wyłączona historia logowań / źródło Mock,
brak LDAPS albo brak mapowania grupy nauczycieli. `appsettings.Production.json` jest
kontrolowany przez CI. Tokeny z `appsettings.json` są w produkcji ignorowane i czyszczone.

Wymagania dodatkowe przy wdrożeniu:

- transport tylko `wss` z certyfikatem; bez HTTPS → HTTP 403 z `transport.rejected` w audycie,
- konto usługi serwera: odczyt wybranych OU, „Event Log Readers” na stacjach, prawo resetu
  `unicodePwd`/`pwdLastSet` i modyfikacji `member` tylko we wskazanych OU/grupach, WinRM
  na stacjach do blokady, **bez** praw administratora domeny
  (`ZseAdmin:Directory:ServiceAccountUser` lub konto procesu; `tokenGroups` wymaga prawa
  czytania konstruowanych atrybutów — bez niego serwer wraca do `memberOf` i traci zagnieżdżenie grup),
- logowanie do serwera tylko z podsieci administracyjnej,
- `UseForwardedHeaders` wyłącznie za zaufanym reverse proxy przekazującym `X-Forwarded-Proto`.

## 4. Audyt operacji

Każda operacja zmieniająca stan (M4) zapisuje: kto, kiedy, na jakim obiekcie, jaka była
wartość przed i po, z jakiego adresu IP i z jakim wynikiem. Audyt obejmuje również
zdarzenia bezpieczeństwa M3: nieudane uwierzytelnienie, odrzucenie transportu bez `wss`
i odmowę uprawnienia.

Zaimplementowany rejestr: `AuditEntryDto { Id, TimestampUtc, Actor, Role, Capability,
Action, Target, Before, After, Result, RemoteAddress, Detail }`, retencja 12 miesięcy,
`Infrastructure/Audit/AuditLog.cs`, zapytanie `audit.query` i lista w szczegółach obiektu.
Wpisy `success` i `failed` powstają w `AdminActionService`; `denied` — przy odmowach
transportu/uwierzytelnienia. `result = failed` zawiera nazwę źródła w `detail`, a hasła
tymczasowe są z audytu wyłączone (pojawiają się jednorazowo tylko w `action.execute`).
Plik `ZseAdmin:Audit:FilePath` (JSONL, append-only) jest opcjonalny — bez niego audyt
jest w pamięci procesu i znika po restarcie, co jest świadomym ograniczeniem ADR-0003.

## 5. Dane osobowe (RODO)

Narzędzie przetwarza dane uczniów i pracowników: login, nazwę, przynależność do klas/grup oraz **historię logowań na stacjach**. To dane osobowe, a historia aktywności może być uznana za profilowanie — wymaga podstawy prawnej i oceny skutków.

**Do ustalenia przed wdrożeniem (poza kodem):**

1. **Podstawa prawna** — najpewniej art. 6 ust. 1 lit. c/e RODO (obowiązek ustawowy, zadanie publiczne — statut szkoły, nadzór pedagogiczny) oraz regulamin pracowni. Nie „zgoda ucznia”.
2. **Ocena skutków (DPIA)** — wskazana, bo monitoring korzystania ze sprzętu przez osoby, w tym niepełnoletnie, jest systematyczną obserwacją.
3. **Minimalizacja** — zaimplementowane: tylko 4624/4634/4625, pola konta/stacji/czasu/typu/źródła; bez procesów, bez treści, bez przechwytywania ekranu. `RemoteSecurityEventSource` czyta wybrane pola XML zdarzenia, `LoginEventDto` nie ma danych biometrycznych ani aktywności aplikacji.
4. **Retencja** — zaimplementowane: 30 dni w `LoginHistoryBuffer` (fizyczne usuwanie starszych wpisów, test zegarowy); audyt 12 miesięcy. Wartość 30 dni wciąż wymaga formalnego potwierdzenia przez DPO; dzienniki systemowe zostają poza aplikacją.
5. **Dostęp** — zaimplementowane (M3): nauczyciel widzi logowania tylko z pracowni przypisanych w `Security:TeacherClassrooms` albo aktywnych w `Security:TeachingSlots`. `LoginAccessPolicy` nadpisuje filtr zapytania, zwraca `forbidden` dla obcej pracowni i filtruje `event.logins.new`; `welcome.classroomScope` zawęża UI. Dane planu lekcji muszą być dostarczone w M5.
6. **Informacja** — regulamin pracowni i klauzula informacyjna muszą mówić, że logowania są rejestrowane i w jakim celu.
7. **Prawa osób** — możliwość wyjaśnienia wpisu (ktoś mógł użyć cudzego konta), ścieżka do inspektora danych.

**Czego aplikacja nie może robić bez nowej decyzji:** śledzić aktywności poza logowaniem (aplikacje, strony, czas bezczynności), udostępniać historii logowań osobom trzecim, wysyłać danych poza sieć szkolną.

## 6. Zasady bezpieczeństwa w kodzie

- Brak endpointów przyjmujących dowolne zapytania LDAP — filtry budowane z konkretnych, walidowanych parametrów. M4 wystawia tylko dwie operacje zapisu (`unicodePwd`, `member`), nie ogólny `ModifyRequest` od klienta.
- Limity: `take` przycinane do 500, rozmiar wiadomości 1 MiB, paginacja LDAP z limitem, długość `target` akcji 1–256 znaków bez znaków sterujących.
- Token porównywany w czasie stałym (`CryptographicOperations.FixedTimeEquals`); w produkcji `Security:Mode = Windows` i brak tokenów.
- Potwierdzenie akcji: jednorazowy token ważny 120 s, przypisany do aktora i celu; bez `confirm: true` nie ma zapisu.
- Szczegóły błędów wewnętrznych nie trafiają do klienta (tylko `internal` + komunikat ogólny), pełny wyjątek idzie do logu serwera. Błąd źródła akcji jest jednak jawny (`upstreamUnavailable` + `detail`), bo UI nie może pokazać „sukces” po awarii.
- Sekrety poza repozytorium: `appsettings.Local.json` w `.gitignore`, w produkcji zmienne środowiskowe lub magazyn sekretów. Hasło tymczasowe nie jest logowane ani audytowane.

## 7. Lista kontrolna przed produkcją

**Zrobione w kodzie i testach (M2–M4, CI):**

- [x] Uwierzytelnianie Windows/grupy AD zamiast tokenów + czyszczenie tokenów w produkcji.
- [x] Artefakt klienta publikowany przez `scripts/publish-client.ps1` bez tokenu, z `wss` i `UseWindowsAuth = true`; CI to sprawdza.
- [x] Odrzucenie połączenia bez `wss` (`RequireSecureTransport`, 403 przed upgrade'em; klient WPF też nie startuje na `ws://` w trybie Windows Auth).
- [x] Retencja logowań 30 dni i audytu 12 miesięcy — dane faktycznie znikają (testy z zegarem).
- [x] Ograniczenie widoczności historii dla nauczycieli (plan lekcji/przypisania, `forbidden`).
- [x] Audyt operacji zapisu: aktor, cel, przed/po, wynik, IP; awaria = `failed`.
- [x] Serwerowe odrzucenie akcji bez `computers.control` / `users.write`, także przy wykonaniu.
- [x] Plan na awarię źródła: „dane z godziny X”, `partial`, `upstreamUnavailable`, nie puste ekrany.

**Do wykonania w M5 (wymaga środowiska szkoły):**

- [ ] Certyfikat dla `wss` i LDAPS, firewall ograniczony do podsieci, zaufany reverse proxy.
- [ ] DPIA, podstawa prawna i klauzula informacyjna zatwierdzone przez DPO/dyrekcję.
- [ ] Konto usługi z minimalnymi prawami, zweryfikowane testem negatywnym na DC.
- [ ] Prawdziwy plan lekcji w `Security:TeachingSlots` i przegląd przypisań nauczycieli.
- [ ] `ZseAdmin:Audit:FilePath` na udziale z uprawnieniami NTFS + polityka rotacji.
- [ ] Pilotaż: Event Log Readers, WinRM, `unicodePwd` po LDAPS i `shutdown.exe` na 6 stacjach.
