# ZAD-06 — Uczniowie bez dostępu: deny-by-default na serwerze

**Etap:** M8 · **Priorytet:** P0 (bezpieczeństwo, przed `/pepsi` i topologią dla nauczycieli)
**Zależy od:** decyzja **D10** · **Blokuje:** ZAD-07, ZAD-08
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

1. Konfiguracja `Security:DeniedGroups` (np. `ZSE-Uczniowie`, `Domain Users` wg **D10**) w
   `appsettings*.json` (produkcja bez tokenów!), z walidacją w `ProductionConfigurationGuard`.
2. `src/ZseAdmin.Server/Security/WindowsAuthenticator.cs` + `RoleCatalog.cs`:
   - jawna odmowa dla konta w grupie wykluczającej **przed** wyliczeniem roli (**D10**: deny
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
- Zmiana mechanizmu Windows/Kerberos, `wss`, `classroomScope`, retencji, ADR-0003/0008.
- DPIA/klauzule RODO (M5) — zadanie pilnuje tylko technicznej odmowy.

## Kryteria akceptacji (Given / When / Then)

1. **Given** konto w grupie `ZSE-Uczniowie` w trybie `Security:Mode = Windows`, **when** próbuje
   połączyć się z `/ws`, **then** serwer zwraca 401 **przed** upgrade'em WebSocket, nie wysyła
   `welcome` i zapisuje wpis audytu `auth.rejected`.
2. **Given** konto jednocześnie w grupie uczniowskiej i `ZSE-Admin-Nauczyciele` (**D10** = deny
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
  capability; nieznany typ bez mapowania → `forbidden`; `files.*` i `topology.get` dla ReadOnly →
  `forbidden`.
- `tests/ZseAdmin.Tests/DisplayFormatTests.cs` / test ViewModelu: tekst „Brak dostępu” nie jest
  mylony z `completeness = unavailable`.

## Definicja ukończenia

- [ ] `DeniedGroups` udokumentowane w [`../uruchomienie.md`](../uruchomienie.md) i sprawdzane w
  produkcji; w repo nie ma sekretów.
- [ ] Tabela ról w [`../uprawnienia-i-dane-osobowe.md`](../uprawnienia-i-dane-osobowe.md) zawiera
  wiersz „Uczeń / grupa wykluczająca → brak dostępu”.
- [ ] `dotnet test` zielone; przebieg CI dołączony do PR.
- [ ] Decyzja **D10** zapisana; status w [`README.md`](README.md).

## Ryzyka i decyzje

- **D10**: użycie `Domain Users` jako grupy wykluczającej może zablokować też konta serwisowe —
  wybrać wąską grupę uczniowską i udokumentować wyjątki.
- `RoleCatalog.RolePriority` daje dziś pierwszeństwo Administratorowi; wprowadzenie deny overrides
  zmienia tę regułę — wymagany aneks do ADR-0007 i test regresji dla administratora, który **nie**
  jest uczniem.
- Ekran „Brak dostępu” nie może ujawniać, czy konto/grupa istnieje — komunikat ma być ogólny,
  a szczegóły tylko w audycie po stronie serwera.
