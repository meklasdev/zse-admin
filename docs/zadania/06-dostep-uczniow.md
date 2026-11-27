# ZAD-06 — Uczniowie bez dostępu: deny-by-default na serwerze

**Etap:** M8 · **Priorytet:** P0 (bezpieczeństwo, przed `/pepsi` i topologią dla nauczycieli)
**Zależy od:** — (**D10 = A**: deny overrides, podjęte) · **Blokuje:** ZAD-07, ZAD-08
**Status:** **zrobione** (PR #19, 2026-11-27) — patrz „Realizacja” na końcu dokumentu
**Etykiety:** `enhancement`, `help wanted` · **Szacunek:** 2 dni
**Źródło:** [`../notatki-zamawiajacego-2026-11-26.md`](../notatki-zamawiajacego-2026-11-26.md) F3

## Kontekst

„Są uczniowie oni wgl nie mają do tego dostępu”. Obecny model (M3) jest już blisko: `RoleCatalog`
dla nieznanej roli zwraca pustą listę uprawnień, a `WindowsAuthenticator` fail-close odrzuca
połączenie bez grupy `ZSE-Admin-*`
([`../uprawnienia-i-dane-osobowe.md`](../uprawnienia-i-dane-osobowe.md) §3,
[`../adr/0007-uwierzytelnianie-windows-i-zakres-nauczyciela.md`](../adr/0007-uwierzytelnianie-windows-i-zakres-nauczyciela.md)).
Brakuje jednak **jawnego, przetestowanego wymagania** dla grup uczniowskich, odmów z nowych typów
wiadomości (`topology.get`, `files.list`) i komunikatu w UI, który nie wygląda jak „brak danych”.
Zasada z [`../uprawnienia-i-dane-osobowe.md`](../uprawnienia-i-dane-osobowe.md) §1: ukrycie w UI
nie jest zabezpieczeniem — każde żądanie sprawdza `CapabilityGuard` w `MessageRouter`.

## Zakres

1. Konfiguracja `Security:DeniedGroups` (wąska grupa uczniowska, np. `ZSE-Uczniowie`; **D10 = A**) w
   `appsettings*.json` (produkcja bez tokenów!), z walidacją w `ProductionConfigurationGuard`.
2. `src/ZseAdmin.Server/Security/WindowsAuthenticator.cs` + `RoleCatalog.cs`:
   - jawna odmowa dla konta w grupie wykluczającej **przed** wyliczeniem roli (**D10 = A**: deny
     overrides, czyli uczeń dodany przez pomyłkę do `ZSE-Admin-Nauczyciele` i tak nie wchodzi),
   - odmowa = 401 przed upgrade'em WebSocket + wpis audytu `auth.rejected` z `actor`, `role = none`,
     `detail` bez danych osobowych wykraczających poza nazwę konta,
   - brak nowej roli „Uczeń” z uprawnieniami: nieznana rola → `Array.Empty<string>()` (bez zmiany
     semantyki, ale z jawnym testem).
3. `MessageRouter` / `CapabilityGuard`: każdy **nowy** typ (`topology.get`, `files.list`,
   `files.stat`) ma przypisane capability i jest sprawdzany po stronie serwera; brak mapowania =
   `forbidden` (fail-close), nie „brak obsługi = domyślnie allow”.
4. `src/ZseAdmin.Client.Core/ViewModels/MainViewModel.cs` + `MainWindow.xaml`: stan
   „Brak dostępu” (odmowa połączenia / `welcome.capabilities` puste / `forbidden`) jako jawny ekran
   z tekstem „Brak dostępu do ZSE-Admin. Skontaktuj się z administratorem.”, bez pustych list,
   bez przycisków akcji, bez danych.
5. Dokumentacja: [`../uprawnienia-i-dane-osobowe.md`](../uprawnienia-i-dane-osobowe.md) (sekcja
   o uczniach, tabela ról, audyt odmów), aneks w ADR-0007,
   [`../notatki-zamawiajacego-2026-11-26.md`](../notatki-zamawiajacego-2026-11-26.md) §4 (D10).

## Poza zakresem

- Przyznawanie uczniom **jakiegokolwiek** dostępu (nawet tylko do swoich logowań), portal ucznia,
  dane osobowe uczniów w UI.
- Zmiana mechanizmu Windows/Kerberos, `wss`, `classroomScope` dla **historii logowań**, retencji,
  ADR-0003/0008. (D7 = C znosi filtr sal wyłącznie dla nowego widoku topologii.)
- DPIA/klauzule RODO (M5) — zadanie pilnuje tylko technicznej odmowy.

## Kryteria akceptacji (Given / When / Then)

1. **Given** konto w grupie `ZSE-Uczniowie` w trybie `Security:Mode = Windows`, **when** próbuje
   połączyć się z `/ws`, **then** serwer zwraca 401 **przed** upgrade'em WebSocket, nie wysyła
   `welcome` i zapisuje wpis audytu `auth.rejected`.
2. **Given** konto jednocześnie w grupie uczniowskiej i `ZSE-Admin-Nauczyciele` (**D10 = A** — deny
   overrides), **when** uwierzytelniam, **then** połączenie jest odrzucone — odmowa ma pierwszeństwo
   nad przypisaniem roli.
3. **Given** deweloperski token z rolą nieznaną/`Student`, **when** wyślę kolejno
   `classrooms.list`, `computers.list`, `logins.query`, `users.get`, `topology.get`, `files.list`,
   `audit.query`, `action.prepare`, **then** za każdym razem dostaję `forbidden` i żadnych danych.
4. **Given** nowy typ wiadomości bez wpisanego capability, **when** `MessageRouter` go obsłuży,
   **then** zwraca `forbidden` (fail-close), a test pilnuje, że katalog typów i katalog
   uprawnień są kompletne.
5. **Given** klient, który dostał `forbidden` albo odmowę połączenia, **when** renderuje widok,
   **then** pokazuje ekran „Brak dostępu”, a nie puste tabele, „brak danych” ani przyciski akcji M4.
6. **Given** produkcja, **when** `ProductionConfigurationGuard` sprawdza konfigurację, **then**
   `Security:DeniedGroups` jest skonfigurowane, a brak wpisu jest traktowany jako błąd
   (bez cichego „uczniowie nie są blokowani”).

## Testy

- `tests/ZseAdmin.Tests/SecurityM3Tests.cs` (rozszerzyć): odmowa grupy uczniowskiej, deny
  overrides, audyt `auth.rejected`, brak `welcome`.
- `tests/ZseAdmin.Tests/RoleCatalogTests.cs` (rozszerzyć): nieznana rola → zero uprawnień;
  `Student` nie istnieje jako rola z prawami.
- `tests/ZseAdmin.Tests/MessageRouterTests.cs` (nowe przypadki): każdy typ wiadomości ma
  capability; nieznany typ bez mapowania → `forbidden`; `files.*` i `topology.get` dla ReadOnly
  → **dozwolone** (D4, D7), a dla ucznia → `forbidden`.
- `tests/ZseAdmin.Tests/DisplayFormatTests.cs` / test ViewModelu: tekst „Brak dostępu” nie jest
  mylony z `completeness = unavailable`.

## Definicja ukończenia

- [ ] `DeniedGroups` udokumentowane w [`../uruchomienie.md`](../uruchomienie.md) i sprawdzane w
  produkcji; w repo nie ma sekretów.
- [ ] Tabela ról w [`../uprawnienia-i-dane-osobowe.md`](../uprawnienia-i-dane-osobowe.md) zawiera
  wiersz „Uczeń / grupa wykluczająca → brak dostępu”.
- [ ] `dotnet test` zielone; przebieg CI dołączony do PR.
- [ ] Decyzja **D10 = A** zapisana w notatkach §4; status w [`README.md`](README.md).

## Realizacja (2026-11-27, PR #19)

| Element | Zmiana |
|---|---|
| Konfiguracja | `Security:DeniedGroups` (wzorzec `ZSE-Uczniowie`) w `appsettings.json` i `appsettings.Production.json`; `SecurityOptions.DeniedGroups` z bezpieczną wartością domyślną |
| Deny overrides | `RoleCatalog.FindDeniedGroup` + `WindowsAuthenticator` — sprawdzenie **przed** `RoleFromGroups`; odmowa `AuthenticationResult.Reject("denied_group")` |
| 401 przed upgrade'em + audyt | `WsEndpoint` zapisuje `auth.rejected` z kodem przyczyny (`denied_group`, `role_group_missing`, `token_missing`, `token_invalid`, `not_authenticated`), nie akceptuje socketu i nie wysyła `welcome` |
| Fail-close katalogu | nowy `Security/MessageCapabilities.cs`; `MessageRouter` sprawdza typ → uprawnienie **przed** obsługą, brak wpisu = `forbidden`; `topology.get`, `files.list`, `files.stat` zarezerwowane z `topology.read` / `files.read` |
| Uprawnienia nowych typów | `Capability.TopologyRead`, `Capability.FilesRead` dla Administrator/Teacher/ReadOnly (D7, D4); brak roli „Uczeń” |
| Brama produkcyjna | `ProductionConfigurationGuard` wymaga niepustego `DeniedGroups` i odrzuca grupy szerokie |
| UI | `Client.Core/ViewModels/AccessDenial.cs` + stan `IsAccessDenied`/`HasAccess` w `MainViewModel`; w `MainWindow.xaml` jawny ekran „Brak dostępu do ZSE-Admin…” zamiast pustych tabel/przycisków akcji |
| Testy | `SecurityM3Tests` (deny overrides, audyt, brama), `RoleCatalogTests`, `MessageRouterTests` (kompletność mapy, `forbidden` dla `Student`, uprawnienia ReadOnly), `WsEndpointTests` (401 + audyt + brak upgrade'u, 403 dla `ws`), `AccessDenialTests` („brak dostępu” ≠ `unavailable`) |

**Spełnione kryteria:** 1 (401 + audyt + brak `welcome` — `WsEndpointTests`), 2 (deny overrides —
test z uczniem w grupie nauczycielskiej), 3 (`Student` → `forbidden` dla każdego typu żądania),
4 (fail-close kompletu typów — test `UnmappedProtocolRequests` pusty), 5 (ekran „Brak dostępu”,
`AccessDenialTests`), 6 (brama produkcyjna wymaga `DeniedGroups`).

**Otwarte:** rzeczywista nazwa grupy uczniowskiej w domenie (wzorzec `ZSE-Uczniowie`) — konfiguracja
administratora IT; DPIA/klauzula pozostają w M5. Klient przy odmowie rozpoznaje HTTP 401 po komunikacie
transportu (`ClientWebSocket` nie wystawia statusu) — jeśli biblioteka zmieni tekst, trzeba poprawić
`AccessDenial.IsAuthenticationFailure` (test to pilnuje).

## Ryzyka i decyzje

- **D10 = A (podjęte):** deny overrides. Użycie `Domain Users` jako grupy wykluczającej mogłoby
  zablokować też konta serwisowe — wybrać wąską grupę uczniowską i udokumentować wyjątki.
- `RoleCatalog.RolePriority` daje dziś pierwszeństwo Administratorowi; wprowadzenie deny overrides
  zmienia tę regułę — wymagany aneks do ADR-0007 i test regresji dla administratora, który **nie**
  jest uczniem.
- Ekran „Brak dostępu” nie może ujawniać, czy konto/grupa istnieje — komunikat ma być ogólny,
  a szczegóły tylko w audycie po stronie serwera.
