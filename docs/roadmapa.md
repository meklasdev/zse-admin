# Roadmapa

Każdy etap ma kryteria akceptacji w formacie Given/When/Then — bez nich nie da się obiektywnie zamknąć zadania (to była największa luka ocenianej specyfikacji).

## M0 — Szkielet (zrobione)

**Zakres:** kontrakty protokołu, serwer WebSocket z danymi przykładowymi, klient WPF z trzema listami, metadane świeżości, role i uprawnienia, testy jednostkowe, CI, dokumentacja.

**Kryteria akceptacji:**

- Mając uruchomiony serwer, gdy otworzę `/healthz`, to widzę `status: ok` i liczbę sesji.
- Mając konsolę `debug.html`, gdy wyślę `computers.list` z tokenem administratora, to dostaję listę 17 komputerów z `freshness`.
- Mając rolę Nauczyciel, gdy wyślę `users.get`, to dostaję odpowiedź (uprawnienie `users.read`), a gdy wyślę nieznany typ — `invalidRequest`.
- Mając rolę ReadOnly, gdy wyślę `users.get`, to dostaję `forbidden`.
- Gdy serwer działa 30 s, to klient otrzymuje co najmniej jedno `event.computer.status` (o ile stan się zmienił).
- Gdy zerwę połączenie, to klient przechodzi w „ponowne łączenie”, a po powrocie pobiera pełny stan.

## M1 — Prawdziwe AD (odczyt) — zrobione

**Zakres:** adapter LDAP (`System.DirectoryServices.Protocols`), `classrooms.json`, mapowanie OU → pracownia, komputery i konta z AD, grupy przez `tokenGroups`, paginacja i limity.

| Element | Gdzie |
|---|---|
| Odczyt LDAP (tylko to miejsce zna LDAP) | `Infrastructure/Ldap/LdapDirectoryReader.cs` |
| Interpretacja atrybutów AD, mapowanie na DTO | `Infrastructure/Ldap/LdapInventoryReader.cs` |
| Mapowanie OU → pracownia + walidacja konfiguracji | `Infrastructure/Classrooms/ClassroomMap.cs` |
| Komputery z AD + ICMP + cache ostatniej obserwacji | `Infrastructure/Ldap/AdComputerDirectory.cs`, `StatusProbe.cs` |
| Konta i pracownie nad AD | `Infrastructure/Ldap/AdDirectory.cs`, `AdClassroomDirectory.cs` |
| Definicja pracowni (konfiguracja, nie kod) | `src/ZseAdmin.Server/classrooms.json` |

**Kryteria akceptacji:**

- Gdy w AD jest nowy komputer w OU pracowni, to pojawia się na liście po odświeżeniu, bez restartu serwera. ✔ cache katalogu ma TTL (`Directory:CacheSeconds`), więc odświeżenie widać najpóźniej po tym czasie.
- Gdy komputer jest poza OU z konfiguracji, to widzę go w „poza pracownią”, a nie znika. ✔ `ClassroomMap.Resolve` zwraca `null`, a `AdComputerDirectory` nie filtruje po pracowni źródłowo (test: `Komputer_poza_znanym_OU_nie_dostaje_pracowni_ale_nie_rzuca`).
- Gdy OU jest przypisane do dwóch pracowni, to serwer nie startuje i loguje błąd konfiguracji. ✔ `ClassroomMap.Create` + `Program.cs` (kod wyjścia 1, komunikat na stderr; test: `Jedno_OU_w_dwoch_pracowniach_to_blad_konfiguracji`).
- Gdy konto ma 300 grup, to widzę wszystkie (bez ucięcia), a odpowiedź trwa < 2 s. ✔ grupy przez `tokenGroups` rozwiązywane **jednym** zapytaniem po `objectSid` (test: `Grupy_przez_tokenGroups_sa_rozwiazywane_jednym_zapytaniem`). SID-y spoza domeny nie znikają — wracają jako `S-1-5-…`.
- Gdy konto AD nie istnieje, to `users.get` zwraca `notFound`. ✔ `AdDirectory` zwraca `null`, router podnosi `NotFound`.
- Gdy AD jest nieosiągalne, to `users.get` zwraca `upstreamUnavailable` z nazwą źródła — a nie `notFound` ani pustą listę. ✔ (`SourceUnavailableException` → `ProtocolException`).
- Gdy inwentarz przekroczy `Directory:MaxComputers`, to odpowiedź ma `completeness = partial` i notę z liczbą komputerów. ✔

**Poza zakresem M1 (świadomie):** adres IP stacji (wymaga WMI/ARP/DNS — etap M1.1),
odczyt `dNSHostName`, historia logowań (M2). W trybie produkcyjnym `logins.query` zwraca
wtedy `unavailable` z powodem, chyba że włączono `UseMockDataForLogins`.

**Ryzyka:** uprawnienia konta usługi, opóźnienie `lastLogonTimestamp`, rozmiar odpowiedzi LDAP.

## M2 — Historia logowań — zaimplementowane (weryfikacja na pilocie)

**Zakres:** zbieranie zdarzeń 4624/4634/4625 (zdalny odczyt dziennika zabezpieczeń), bufor lokalny (JSONL), filtry komputer/użytkownik/pracownia/czas, kompletność i zakres, retencja 30 dni.

| Element | Gdzie |
|---|---|
| Zdalny odczyt dzienników (4624/4634/4625) per stacja, budżet czasu | `Infrastructure/Logins/WindowsSecurityEventSource.cs`, `WindowsSecurityEventReader.cs` |
| Mapowanie zdarzeń + pracownia z inwentarza | `SecurityEventMapper.cs` |
| Cykliczny zbiór + czyszczenie retencji | `LoginEventCollector.cs`, `LoginEventCollectorService.cs` (pierwszy cykl od razu, potem co `Logins:PollSeconds`) |
| Magazyn zdarzeń (append + retencja po znacznikach) | `FileLoginEventStore.cs` (`data/logins.jsonl`, 30 dni, deduplikacja po Id) |
| Odpowiedź z kompletnością i notą „N z M stacji bez dostępu” | `LoginLog.cs` |
| Makieta na dev (14 dni, partial) | `MockLoginLog.cs` (`UseMockDataForLogins=true`) |

**Kryteria akceptacji:**

- Gdy uczeń zaloguje się na stacji, to zdarzenie jest widoczne w aplikacji w ciągu 2 minut. — cykl co 60 s + pierwsze sprawdzenie po starcie; do potwierdzenia na pilocie (Windows, *Event Log Readers*).
- Gdy 2 z 6 stacji nie mają dostępu do dziennika, to odpowiedź ma `completeness = partial` i notę z liczbą stacji. ✔ `LoginLog` składa notę z `LoginCollectionState.Snapshot` (Stacje bez dostępu: N z M); brak dostępu nigdy nie wygląda jak „brak logowań”.
- Gdy pytam o zakres poza retencją, to widzę jawny komunikat o zakresie, nie pustą listę bez wyjaśnienia. ✔ `MessageRouter` (nota o początku retencji) + test `Zakres_przed_retencja_jest_jawnie_oznaczony`.
- Gdy minie 30 dni, to zdarzenia są usuwane (test na podstawie znaczników czasu). ✔ `FileLoginEventStore.PruneAsync` wywoływany przy każdym cyklu; retencja konfigurowalna (`Logins:RetentionDays`).

**Ryzyka:** przewijanie dzienników, obciążenie stacji, RODO (podstawa prawna, DPIA).

## M3 — Bezpieczeństwo produkcyjne — zaimplementowane (weryfikacja na pilocie)

**Zakres:** uwierzytelnianie Windows/Kerberos + mapowanie grup AD na role, `wss`, ograniczenie widoczności historii dla nauczycieli, audyt operacji, minimalne prawa konta usługi, bramka konfiguracji produkcyjnej.

| Element | Gdzie |
|---|---|
| Negotiate (Kerberos/NTLM) na Kestrel / za IIS | `Program.cs` (`AddNegotiate()`), `Security/ActiveDirectoryAuthenticator.cs` |
| Mapowanie grup AD → role (świeże przy każdym połączeniu) | `ActiveDirectoryAuthenticator` + `Security:GroupMapping` (`ZSE-Admin-Administratorzy` / `ZSE-Admin-Nauczyciele` / `ZSE-Admin-Podglad`) |
| Odmowa bez grupy (401), AD nieosiągalne (503) | `WsEndpoint.cs` (+ audyt `auth.connect`/`denied`) |
| `wss` wymagane — odmowa przed upgrade'em (400) | `WsEndpoint.cs` + `Security/SecureTransport.cs` (`RequireSecureTransport=true` na prod) |
| Bramka produkcyjna: tokeny/brak wss/mock/bez-LDAPS → serwer nie startuje | `Security/SecurityConfigurationValidator.cs` (kod wyjścia 1) |
| Widoczność historii nauczyciela ograniczona do jego pracowni (`teachers` w `classrooms.json`) | `MessageRouter` (`ClassroomIdsForTeacher`), `Forbidden` przy cudzej pracowni/stacji |
| Audyt: zapis + retencja 12 mies. + podgląd (`audit.query`) | `Infrastructure/Audit/FileAuditLog.cs` (`data/audit.jsonl`) |

**Kryteria akceptacji:**

- Gdy użytkownik nie należy do grupy `ZSE-Admin-Nauczyciele` (ani `Administratorzy`/`Podglad`), to nie może się połączyć. ✔ 401 z `WsEndpoint`, zanim powstanie sesja; testy `SecurityConfigurationValidatorTests` pilnują bramki.
- Gdy nauczyciel prosi o logowania z pracowni, w której nie ma zajęć, to dostaje `forbidden`. ✔ `MessageRouter` + testy `Nauczyciel_nie_moze_pytac_o_pracownie_spoza_swojego_przypisania` i `Nauczyciel_bez_przypisanej_pracowni_nie_widzi_historii_logowan`.
- Gdy zmieniono dane (etap M4), to w audycie jest wpis z aktorem, celem i wartością przed/po. ✔ `RecordAuditAsync` (M4) + `audit.query`; testy `Administrator_restartuje_stacje_i_tworzy_wpis_audytu`.
- Tokeny z `appsettings.json` są wyłączone, a połączenie bez `wss` odrzucone. ✔ baza nie ma tokenów (dev-tokeny w `appsettings.Development.json`), `RequireSecureTransport` + `SecurityConfigurationValidator` (test: `Brak_wss_na_produkcji_blokuje_start`).

**Uwaga:** uwierzytelnianie Negotiate na Kestrel wymaga SPN (patrz `docs/uruchomienie.md` §9) — za IIS z włączonym Windows Authentication SPN nie jest potrzebny.

## M4 — Akcje na stacjach i zapis do AD — zaimplementowane (weryfikacja na pilocie)

**Zakres:** restart/blokada stacji, reset hasła, dodanie do grupy — każde z potwierdzeniem i audytem.

| Element | Gdzie |
|---|---|
| Restart: `shutdown.exe /r /m \\STACJA /f /t 0` | `Infrastructure/Actions/WindowsComputerActionExecutor.cs` |
| Blokada ekranu: PsExec (`Actions:PsExecPath`); bez konfiguracji — jawny błąd, nie udawany sukces | ten sam plik |
| Reset hasła: LDAP `unicodePwd` (wymaga LDAPS), hasło nigdy nie trafia do audytu | `Infrastructure/Actions/LdapAdAccountWriter.cs` |
| Dodanie do grupy: LDAP `member`, operacja idempotentna (`changed=false`) | ten sam plik |
| Wymuszenie `confirm=true` + uprawnienie + audyt (zawsze, także przy błędzie) | `MessageRouter` (`computers.action`, `users.resetPassword`, `users.addToGroup`) |
| Ukrycie akcji w UI bez uprawnienia (`computers.control`/`users.write`) | `MainViewModel` + `MainWindow.xaml` (wpis `welcome.capabilities`) |
| Potwierdzenie w UI przed każdą akcją | `IUserInteraction` + dialogi WPF (`WpfUserInteraction`, `ResetPasswordWindow`, `PromptWindow`) |

**Kryteria akceptacji:**

- Gdy rola nie ma `computers.control`, to akcja jest niewidoczna w UI i odrzucona serwerowo. ✔ `CanControlComputers` z `welcome.capabilities` + `CapabilityGuard.Require` → `forbidden` (test: `Readonly_nie_moze_wykonac_akcji_na_stacji`).
- Gdy akcja się nie powiedzie, to UI pokazuje błąd źródła, a nie „sukces”. ✔ serwer zwraca `upstreamUnavailable` z komunikatem narzędzia/AD (także przy błędzie audytu — operacja nie jest meldowana jako udana); klient pokazuje `StatusMessage` z błędem zamiast „gotowe”.
- Każda zmiana ma wpis audytowy i jest widoczna w szczegółach obiektu. ✔ audyt zawsze (sukces i porażka); zakładka „Audyt” w kliencie (Administrator) i `audit.query` w `debug.html`; testy `…_zapisuje_wpis_audytu…`.

## M5 — Wdrożenie

**Zakres:** publikacja jako usługa Windows, certyfikaty, firewall, dokumentacja dla szkoły, szkolenie, pilotaż w jednej pracowni.

**Kryteria akceptacji:**

- Serwer startuje automatycznie po restarcie maszyny.
- Pilotaż w jednej pracowni przez tydzień bez utraty danych (kompletność ≥ 95%).
- Użytkownicy końcowi (1 nauczyciel, 1 administrator) wykonują zadania z listy bez pomocy.

## Backlog decyzji (z README, sekcja „Co doprecyzować”)

| Decyzja | Etap | Właściciel |
|---|---|---|
| Definicja pracowni i mapowanie OU | M1 | administrator IT |
| Źródło i retencja logów | M2 | administrator IT + DPO |
| Typy logowań i aktywne sesje | M2 | dyrekcja |
| Częstotliwość stanu i próg offline | M1 | administrator IT |
| Role i widoczność danych | M3 | dyrekcja + DPO |
| Sposób sygnalizowania reconnectu | M0 (zrobione) | — |
| Ograniczenie retencji i widoczności historii | M2/M3 | DPO |
| Skala: liczba stacji i klientów | M1 | administrator IT |
