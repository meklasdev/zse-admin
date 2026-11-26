# ZAD-09 — Konto AD vs profil/konto lokalne na stacji w historii logowań

**Etap:** M8 · **Priorytet:** P1 · **Zależy od:** — (**D5 = A**: domena + profil lokalny; **D6**: brak danych o plikach ucznia — podjęte)
**Etykiety:** `enhancement`, `question`, `documentation` · **Szacunek:** 2–3 dni
**Źródło:** [`../notatki-zamawiajacego-2026-11-26.md`](../notatki-zamawiajacego-2026-11-26.md) F5, §3.4

## Kontekst

„Każdy uczeń ma swoje konto na AD ale nie przenoszą się pliki między stacjami czyli z AD robi się
konto lokalne na stacji”. **D5 = A (potwierdzone przez zamawiającego):** logowanie do domeny
**tworzy profil użytkownika na tej konkretnej stacji** (`C:\Users\<login>`), bo nie ma profili
mobilnych ani przekierowania folderów — pliki zostają na dysku tej stacji. Konto AD nie „zamienia
się” literalnie w konto lokalne; w dzienniku mogą jednak występować logowania do kont **lokalnych**
(np. serwisowych), więc `accountScope` nadal rozróżnia `domain` / `local` / `unknown` bez zgadywania.
**D6:** uczeń nie ma dostępu do aplikacji, więc UI nie pokazuje żadnych danych o plikach ucznia;
notatka „Pliki użytkownika pozostają na stacji — brak profili mobilnych” jest informacją techniczną
dla administratora, nie wskazaniem na konkretnego ucznia.

## Zakres

1. `ZseAdmin.Contracts`: `enum AccountScope { Domain, Local, Unknown }` i **opcjonalne** pole
   `LoginEventDto.AccountScope?` (domyślnie `null` → `Unknown` dla starszych danych). Zmiana
   kompatybilna wstecz, `protocolVersion = 1`
   ([`../api-websocket.md`](../api-websocket.md) §7).
2. Kolektor M2 (`src/ZseAdmin.Server/Infrastructure/Logins/`): przy odczycie zdarzenia 4624
   wyznaczać zakres z pól dziennika:
   - `TargetDomainName` == NetBIOS domeny (z konfiguracji `Directory:DomainNetBiosName`) → `Domain`,
   - `TargetDomainName` == nazwa komputera/stacji → `Local`,
   - brak pola / wartość nierozpoznana → `Unknown` (**bez zgadywania**),
   - uwzględnić `LogonType` (2 interaktywne, 10 zdalne, 11 buforowane domenowe) i zapisać
     źródło w `LoginEventDto.Source` zgodnie z dotychczasową konwencją.
   Wariant WEF (`WefFileEventSource`) i makieta (`MockLoginEventSource`) mają produkować to samo pole.
3. `src/ZseAdmin.Client.Core/Display/DisplayFormat.cs` + `ItemViewModels.cs`: etykiety
   „konto domenowe AD”, „konto lokalne na stacji”, „źródło konta nieznane”; kolumna/znacznik w
   liście logowań; stała, jawna notatka w widoku logowań:
   „Pliki użytkownika pozostają na stacji — brak profili mobilnych.”
4. `docs`: [`../model-danych.md`](../model-danych.md) (nowe pole + semantyka `Unknown`),
   [`../api-websocket.md`](../api-websocket.md) §7.1 (rozszerzenie v1),
   [`../notatki-zamawiajacego-2026-11-26.md`](../notatki-zamawiajacego-2026-11-26.md) §3.4, §4
   (D5, D6), [`../uprawnienia-i-dane-osobowe.md`](../uprawnienia-i-dane-osobowe.md) (krótka nota
   RODO: brak mapowania plików na ucznia).

## Poza zakresem

- Wdrożenie profili mobilnych, folderów przekierowanych, roamingowych kont, synchronizacji plików.
- Kopiowanie/odczyt plików użytkownika, wiązanie plików z `/pepsi` z kontem ucznia (**D6** podjęte).
- Zmiana retencji 30 dni (ADR-0006), aktywnych sesji, modelu zdarzeniowego, nowa baza danych.
- Zmiana `AdUserDto` — zadanie dotyczy **zdarzenia logowania na stacji**, nie kartoteki konta AD.

## Kryteria akceptacji (Given / When / Then)

1. **Given** zdarzenie 4624 z `TargetDomainName = "ZSE"` i `LogonType = 2`, **when** kolektor
   zinkestuje je, **then** `accountScope = "domain"`, a UI pokazuje „konto domenowe AD”.
2. **Given** zdarzenie 4624 z `TargetDomainName = "ST101-07"` (nazwa stacji), **when** kolektor je
   przetworzy, **then** `accountScope = "local"` i etykieta „konto lokalne na stacji”.
3. **Given** zdarzenie bez `TargetDomainName` albo z nieznaną domeną, **when** jest zwracane,
   **then** `accountScope = "unknown"` z etykietą „źródło konta nieznane” — bez domysłu „domenowe”.
4. **Given** ekran historii logowań, **when** go otworzę, **then** widoczna jest notatka o braku
   przenoszenia plików między stacjami, nie ma komunikatu typu „pliki ucznia na serwerze”
   ani żadnych danych o plikach ucznia (D6).
5. **Given** klient w wersji M4, **when** dostanie `LoginEventDto` z nowym polem, **then** ignoruje
   je i działa bez zmian (`protocolVersion = 1`).
6. **Given** historię mieszaną (konta domenowe i lokalne na jednej stacji), **when** filtruję po
   komputerze, **then** oba rodzaje są widoczne i wyraźnie oznaczone, bez scalania w jedno konto.
7. **Given** `logins.query` z zakresem nauczyciela (ADR-0007), **when** serwer zwraca zdarzenia,
   **then** nowe pole nie poszerza zakresu widoczności (filtr `classroomId` działa jak dotąd).

## Testy

- `tests/ZseAdmin.Tests/AccountScopeTests.cs` (nowy): domain / local / unknown / brak pola /
  `LogonType` 2, 10, 11 / nazwa domeny z inną wielkością liter.
- Rozszerzenie testów źródeł M2 (`LoginHistoryBufferTests`, testy WEF/makiety): pole obecne we
  wszystkich źródłach.
- `tests/ZseAdmin.Tests/DisplayFormatTests.cs`: etykiety dla trzech wartości + `null`.
- `tests/ZseAdmin.Tests/ProtocolSerializationTests.cs`: brak pola = `null`/`unknown`, brak podbicia
  wersji protokołu.

## Definicja ukończenia

- [ ] `AccountScope` jest opcjonalny i udokumentowany jako rozszerzenie v1.
- [ ] Wszystkie źródła logowań M2 ustalają pole w ten sam sposób; brak danych nie jest zgadywany.
- [ ] Notatka o plikach lokalnych widoczna w UI; brak mapowania plików na uczniów.
- [ ] Decyzje **D5 = A**, **D6** zapisane w notatkach §4; `dotnet test` zielone; status w [`README.md`](README.md).

## Ryzyka i decyzje

- **D5 = A (podjęte):** profil domenowy tworzony lokalnie. Jeśli admin zgłosi jednak osobne konta
  lokalne, regułę rozpoznawania zmieniamy w jednym miejscu (kolektor M2); dla przypadków
  niepewnych i tak zostajemy przy `Unknown`.
- `TargetDomainName` bywa pusty przy logowaniu buforowanym albo w WEF — nie traktować tego jako
  błąd źródła, tylko `Unknown` + nota.
- Etykiety nie mogą ujawniać dodatkowych danych osobowych; `Unknown` nie jest powodem do audytu
  odmowy ani do komunikatu „podejrzane konto”.
- `lastLogonTimestamp` z AD nadal może być opóźniony do 14 dni
  ([`../model-danych.md`](../model-danych.md) §3) — nie wolno go używać do wnioskowania o plikach.
