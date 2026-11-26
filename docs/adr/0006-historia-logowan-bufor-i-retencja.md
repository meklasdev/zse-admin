# ADR-0006: Historia logowań — bufor w pamięci, 30 dni i jawna kompletność

**Status:** przyjęte
**Data:** 2026-11-26
**Etap:** M2

## Kontekst

M1 kończyło się odpowiedzią `unavailable` dla `logins.query`. M2 wymaga, aby logowanie
ucznia było widoczne w aplikacji w ciągu 2 minut, aby brak dostępu do dziennika na 2 z 6
stacji dawał `completeness = partial` z liczbą stacji, a zapytanie poza retencją zwracało
wyjaśnienie zamiast pustej listy. Po 30 dniach zdarzenia muszą fizycznie zniknąć.

ADR-0003 zakłada brak własnej bazy danych. Szkoła ma już dzienniki zabezpieczeń Windows,
a docelowo może dostarczać pliki z subskrypcji WEF. Duplikowanie historii w relacyjnej bazie
tworzyłoby kolejny zbiór danych osobowych i wymagałoby osobnych uprawnień, backupów i usuwania.

## Decyzja

1. **Źródło jest za portem `ILoginEventSource`**, zwracającym partię zdarzeń oraz stan
   dostępności każdej stacji. Dwie implementacje produkcyjne:
   - `RemoteSecurityEventSource` — `EventLogReader` z XPath dla 4624/4634/4625,
     odczyt zdalny (konto usługi w Event Log Readers),
   - `WefFileEventSource` — pliki `*.jsonl` + heartbeat `stations.json`.
   `MockLoginEventSource` (2 z 6 stacji niedostępnych) zasila development, testy i CI.
2. **`LoginCollectorService`** wykonuje pierwszy odczyt przy starcie, potem cyklicznie.
   `ZseAdmin:LoginHistory:PollSeconds` jest przycinany do 10–120 s, więc nawet błędna
   konfiguracja nie złamie kryterium 2 minut. Nowe zdarzenia są rozgłaszane jako
   `event.logins.new`; po reconnect klient wykonuje pełne `logins.query`.
3. **`LoginHistoryBuffer`** jest jedynym magazynem aplikacji: deduplikacja po
   (komputer, użytkownik, czas, typ), filtry komputer/użytkownik/czas, twardy limit
   `MaxEvents`, usuwanie wpisów starszych niż `RetentionDays = 30` przy każdym cyklu
   (test z wstrzykiwanym zegarem i skokiem o 31 dni).
4. **`DataFreshnessDto` (ADR-0004) opisuje zakres i kompletność**, nie tylko wiek danych:
   - brak odpowiedzi z części stacji → `partial` + nota „Brak dostępu do dziennika na
     2 z 6 stacji: …”,
   - `fromUtc` starsze niż początek retencji → `partial` + nota z datą graniczną,
   - wyłączony kolektor → `unavailable` z jawnym powodem.
   Nigdy nie zwracamy pustej listy bez wyjaśnienia.
5. **Minimalizacja danych:** zapisujemy konto, nazwę stacji, czas, typ i źródło.
   `classroomId` jest dokładany z `IComputerDirectory` wyłącznie po to, by M3 mogło
   ograniczyć widoczność. Nie zbieramy procesów, treści, adresów URL ani aktywnych sesji.
6. `ProtocolVersion` pozostaje **1**: nowy typ `event.logins.new` oraz opcjonalne pola
   `LoginQueryRequest.classroomId(s)` i `LoginEventDto.classroomId` są zgodne wstecz
   zgodnie z ADR-0002 i `docs/api-websocket.md`.

## Alternatywy

| Alternatywa | Dlaczego odrzucona |
|---|---|
| SQLite / SQL Server na zdarzenia | nowy magazyn danych osobowych, migracje i backup do utrzymania w szkole; sprzeczne z ADR-0003 przed pilotażem |
| Bez bufora: odpytywanie 6 stacji przy każdym `logins.query` | obciąża stacje, opóźnia odpowiedź, trudno zagwarantować 2 minuty; brak stabilnej deduplikacji |
| Wewnętrzna baza + kursor „od ostatniego ID” | przewijanie dzienników Windows i brak gwarancji dostarczenia push wymagałyby reconciliacji; zbędna złożoność |
| Tylko WEF | wymusza infrastrukturę kolektora WEF jeszcze przed pilotażem; odczyt zdalny jest prostszy na starcie |
| Tylko zdalny EventLog | szkoła może już mieć WEF; pozostawienie obu adapterów za jednym portem nie zmienia protokołu |
| Cicha pusta lista przy braku dostępu | narusza kryterium M2 i ADR-0004: sugeruje, że nikt się nie logował |

## Konsekwencje

**Pozytywne:** spełnione cztery kryteria M2, testowalne bez domeny (zegar, makieta 2/6),
jedno miejsce egzekwowania retencji, brak nowej bazy, `completeness` informuje o lukach.

**Negatywne:** bufor jest w pamięci procesu — po restarcie odtwarzamy tylko to, co źródło
jeszcze ma; przy przewinięciu dziennika starsze zdarzenia przepadają. Odczyt EventLog działa
tylko na Windows i daje ostrzeżenia CA1416 (świadomie wyciszone w adapterze). Pliki WEF
wymagają poprawnego `stations.json`, inaczej stacja jest raportowana jako niedostępna.

**Neutralne:** `ZseAdmin:LoginHistory:Source` wybiera adapter w DI. Decyzja DPO o 30 dniach,
podstawa prawna, DPIA i klauzula informacyjna pozostają zadaniami M5 — kod nie przesądza,
że konkretna podstawa przetwarzania została już zatwierdzona.
