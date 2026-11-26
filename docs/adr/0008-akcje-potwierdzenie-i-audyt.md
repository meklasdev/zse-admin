# ADR-0008: Akcje na stacjach i zapis do AD przez dwukrokowe potwierdzenie z audytem

**Status:** przyjęte
**Data:** 2026-11-26
**Etap:** M4

## Kontekst

M0–M1 były wyłącznie do odczytu (ADR-0003). M4 dodaje cztery operacje zmieniające stan:
restart i blokadę stacji, reset hasła oraz dodanie konta do grupy AD. Kryteria wymagają,
by rola bez `computers.control` nie widziała akcji w UI **i** była odrzucona serwerowo,
by awaria źródła pokazywała błąd źródła zamiast „sukces”, oraz by każda zmiana miała wpis
audytowy widoczny w szczegółach obiektu.

`docs/uprawnienia-i-dane-osobowe.md` wymaga audytu z aktorem, celem, wartością przed/po
i wynikiem (retencja 12 miesięcy). Protokół w wersji 1 pozwala dodać nowe typy bez
podnoszenia `ProtocolVersion` (ADR-0002).

## Decyzja

1. **Dwa kroki, zawsze po stronie serwera.**
   - `action.prepare` sprawdza `CapabilityGuard`, waliduje `target` (1–256 znaków),
     opisuje stan „przed”, zwraca ostrzeżenia i jednorazowy `confirmationToken`
     (`ConfirmationStore`, 120 s, przypisany do aktora i celu). Nic nie zmienia.
   - `action.execute` wymaga `confirm: true`, konsumuje token jednorazowo i **ponownie**
     sprawdza uprawnienie w chwili wykonania. `ZseAdmin:Actions:AllowWrites = false`
     jest globalnym wyłącznikiem bezpieczeństwa.
2. **Serwer jest jedynym miejscem autoryzacji.** `MessageRouter` i `AdminActionService`
   odrzucają akcje ról bez `computers.control` / `users.write`. Klient pokazuje przyciski
   tylko na podstawie `welcome.capabilities`, ale to wyłącznie ergonomia, nie zabezpieczenie.
   `ResetPassword` dodatkowo wymaga bezpiecznego transportu (`wss`).
3. **Porty izolują źródła:** `IComputerControl` (`shutdown.exe` i WinRM/`LockWorkStation`)
   oraz `IAdWriteService`/`ILdapDirectoryWriter` (`unicodePwd` + `pwdLastSet = 0`,
   atrybut `member`). `MockComputerControl` i `MockAdWriteService` pozwalają testować
   cały przepływ bez domeny i bez fizycznego restartu stacji.
4. **Błąd źródła nie jest sukcesem.** `ActionFailedException` niesie `SourceName`,
   `ActionTargetNotFoundException` mapuje na `notFound`, a `SourceUnavailableException`
   na `upstreamUnavailable`. `ActionExecuteResponse` powstaje wyłącznie po udanym
   wykonaniu; przy awarii przychodzi `error` z kodem, komunikatem źródła i wpisem audytu
   `result = failed`. `MainViewModel` pokazuje `detail` zamiast komunikatu o powodzeniu.
5. **Audyt jest obowiązkowy dla sukcesu i porażki.** `AdminActionService` zapisuje
   `AuditRecord` z aktorem, rolą, uprawnieniem, akcją, celem, `before`, `after`, wynikiem,
   adresem IP i powodem. `ActionExecuteResponse.AuditId` wskazuje wpis; `audit.query`
   (`audit.read`) filtruje po `target`, a zakładka „Akcje i audyt” w WPF pokazuje te same
   dane w szczegółach komputera/konta. Hasło tymczasowe jest generowane przez serwer,
   zwracane jednorazowo i **nigdy nie trafia do audytu ani logu**.
6. **Protokół pozostaje w wersji 1.** `action.prepare`, `action.execute`, `audit.query`,
   `AdminActionKind`, `AuditEntryDto` i odpowiedzi to nowe typy/pola addytywne; żadne
   istniejące pole nie zmieniło znaczenia.

## Alternatywy

| Alternatywa | Dlaczego odrzucona |
|---|---|
| Jedno żądanie `action.run` wykonujące od razu | brak punktu, w którym użytkownik widzi cel, stan „przed” i ostrzeżenie; łatwy przypadkowy restart |
| Potwierdzenie tylko w oknie WPF | UI nie jest zabezpieczeniem; ktoś mógłby wysłać `action.execute` bezpośrednio do `/ws` |
| Token potwierdzenia bezwiązany z aktorem | przejęty token pozwoliłby innej sesji wykonać cudzą akcję |
| Ogólny endpoint LDAP / dowolne polecenie PowerShell od klienta | prawo do zapisu byłoby nieograniczone; trudny audyt before/po i eskalacja uprawnień |
| Zwracanie `success: false` w `ActionExecuteResponse` | kryterium M4 wymaga jawnego błędu źródła; osobny `error` nie daje się pomylić z sukcesem |
| Audyt tylko udanych operacji | awaria też jest zdarzeniem bezpieczeństwa i musi być rozliczalna |
| Zapisywanie hasła tymczasowego w `detail` audytu | sekret w dodatkowym magazynie danych; naruszenie minimalizacji |

## Konsekwencje

**Pozytywne:** spełnione trzy kryteria M4, jednakowy audyt dla makiety i realnych adapterów,
serwerowe odrzucenie prób obejścia UI, jawne źródło błędu, brak sekretów w audycie.
Testy pokrywają odmowę uprawnienia, awarię źródła, `before`/`after`, jednorazowość
potwierdzenia, niezerowy kod procesu i brak hasła w audycie.

**Negatywne:** zapisy LDAP i polecenia na stacjach nie są transakcyjne — `unicodePwd`
i `pwdLastSet` to jedno `ModifyRequest`, ale awaria między wykonaniem a audytem nadal
może zostawić zmianę bez wpisu. `LockWorkStation` wymaga WinRM na stacji, a `shutdown.exe`
praw do zdalnego zarządzania; brak tych uprawnień jest raportowany jako błąd źródła.
Potwierdzenia i audyt domyślnie żyją w pamięci procesu (ADR-0003), więc produkcyjnie
należy włączyć `ZseAdmin:Audit:FilePath` i chronić plik uprawnieniami NTFS.

**Neutralne:** `classroomId` z M2 pozwala w przyszłości ograniczyć akcje do pracowni
nauczyciela, ale M4 celowo przyznaje `computers.control`/`users.write` wyłącznie
Administratorowi. Realne prawa konta usługi, certyfikaty i pilotaż na stacjach są zadaniami M5.
