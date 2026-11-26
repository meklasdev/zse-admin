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
odczyt `dNSHostName`, historia logowań (M2). Wtedy `logins.query` zwracało
`unavailable` z powodem, chyba że włączono przejściowy `UseMockDataForLogins`.
Od M2 historia ma własne źródło; przełącznik M1.5 w produkcji jest blokowany przez bramkę M3.

**Ryzyka:** uprawnienia konta usługi, opóźnienie `lastLogonTimestamp`, rozmiar odpowiedzi LDAP.

## M2 — Historia logowań — zrobione

**Zakres:** zbieranie zdarzeń 4624/4634/4625 (WEF lub odczyt zdalny), bufor lokalny, filtry komputer/użytkownik/czas, kompletność i zakres, retencja 30 dni. Decyzje: [`docs/adr/0006-historia-logowan-bufor-i-retencja.md`](adr/0006-historia-logowan-bufor-i-retencja.md).

| Element | Gdzie |
|---|---|
| Port źródła + status stacji | `Abstractions/LoginSources.cs` |
| Bufor: deduplikacja, filtry, retencja, kompletność | `Infrastructure/Logins/LoginHistoryBuffer.cs` |
| Kolektor (pierwszy odczyt od razu, potem ≤ 120 s) + push | `Infrastructure/Logins/LoginCollectorService.cs` |
| Zdalny dziennik Security (EventLogReader/XPath) | `Infrastructure/Logins/RemoteSecurityEventSource.cs` |
| Wariant WEF (`*.jsonl` + `stations.json`) | `Infrastructure/Logins/WefFileEventSource.cs` |
| Źródło dev (2 z 6 stacji niedostępnych) | `Infrastructure/Logins/MockLoginEventSource.cs` |

**Kryteria akceptacji:**

- ✔ Nowe logowanie widać bez odświeżania: kolektor odpytuje co 60 s (twardy limit 120 s, `ClampInterval`), a `event.logins.new` trafia do klienta; test `Nowe_logowanie_jest_widoczne_po_jednym_cyklu_ingestu` + `Kolektor_gwarantuje_odczyt_nie_rzadziej_niz_co_2_minuty`.
- ✔ 2 z 6 stacji bez dziennika → `completeness = partial` i nota „Brak dostępu do dziennika na 2 z 6 stacji: …”; test `Dwie_z_szesciu_stacji_niedostepnych_daja_partial_z_liczba_stacji`.
- ✔ Zakres przed retencją → `partial` + nota z początkiem retencji, nie pusta lista; test `Zakres_przed_retencja_ma_jawny_komunikat_a_nie_pusta_liste_bez_note`.
- ✔ Po 30 dniach zdarzenia są fizycznie usuwane z bufora (zegar testowy + 31 dni); test `Po_30_dniach_zdarzenia_zostaja_fizycznie_usuniete`.

**Ryzyka:** przewijanie dzienników, obciążenie stacji, RODO (podstawa prawna, DPIA) — pozostają do M5.

## M3 — Bezpieczeństwo produkcyjne — zrobione

**Zakres:** uwierzytelnianie Windows/Kerberos z mapowaniem grup AD, `wss`, ograniczenie widoczności historii dla nauczycieli, audyt operacji, minimalne prawa konta usługi. Decyzje: [`docs/adr/0007-uwierzytelnianie-windows-i-zakres-nauczyciela.md`](adr/0007-uwierzytelnianie-windows-i-zakres-nauczyciela.md).

| Element | Gdzie |
|---|---|
| Tożsamość z grup AD, odmowa 401 bez grupy | `Security/WindowsAuthenticator.cs`, `WindowsGroupProvider.cs`, `RoleCatalog.RoleFromGroups` |
| Egzekwowanie `wss` przed upgrade'em | `Security/TransportSecurityGuard.cs`, `Realtime/WsEndpoint.cs`, `UseForwardedHeaders` |
| Bramka produkcyjna (fail-fast, czyszczenie tokenów) | `Configuration/ProductionConfigurationGuard.cs`, `appsettings.Production.json` |
| Zakres nauczyciela (plan lekcji + przypisania) | `Security/TeacherClassroomAccess.cs`, `LoginAccessPolicy.cs` |
| Rejestr audytu (12 miesięcy, opcjonalny JSONL) | `Infrastructure/Audit/AuditLog.cs`, `audit.query` |
| Klient bez tokenu: `wss` + Kerberos | `src/ZseAdmin.Client/appsettings.Production.json`, `scripts/publish-client.ps1`, kontrola artefaktu w CI |

**Kryteria akceptacji:**

- ✔ Użytkownik bez grup `ZSE-Admin-*` (w tym `ZSE-Admin-Nauczyciele`) nie łączy się; test `Uzytkownik_bez_grupy_ZSE_Admin_Nauczyciele_nie_moze_sie_polaczyc`.
- ✔ Nauczyciel proszący o logowania z obcej pracowni dostaje `forbidden` (także przy filtrze po komputerze i dla push); testy w `TeacherScopeTests.cs`.
- ✔ Każda zmiana M4 ma wpis z aktorem, celem, `before`/`after` (hasła maskowane); test `Kazda_zmiana_ma_wpis_audytowy_z_aktorem_celem_przed_i_po`.
- ✔ Produkcja startuje tylko z `Mode = Windows`, bez tokenów, z `RequireSecureTransport = true`, LDAPS i realnym źródłem logowań; odmowy bez `wss` → 403. Testy `Produkcja_startuje_tylko_z_bezpieczna_konfiguracja`, `Polaczenie_bez_wss_jest_odrzucone_przed_upgradem` oraz kontrola CI.

## M4 — Akcje na stacjach i zapis do AD — zrobione

**Zakres:** restart/blokada stacji, reset hasła, dodanie do grupy — każde z potwierdzeniem i audytem. Decyzje: [`docs/adr/0008-akcje-potwierdzenie-i-audyt.md`](adr/0008-akcje-potwierdzenie-i-audyt.md).

| Element | Gdzie |
|---|---|
| Protokół dwóch kroków + DTO | `Contracts/Protocol/{MessageTypes,Requests,Responses}.cs` |
| Jednorazowe potwierdzenie (aktor, czas, cel) | `Services/ConfirmationStore.cs` |
| Orkiestracja, uprawnienia, audyt sukcesu/porażki | `Services/AdminActionService.cs`, `Realtime/MessageRouter.cs` |
| Stacje: `shutdown.exe` / WinRM | `Infrastructure/Windows/{WindowsComputerControl,ProcessRunner}.cs` |
| Zapis AD: `unicodePwd`, `member` po LDAPS | `Infrastructure/Ldap/{LdapDirectoryWriter,LdapAdWriteService}.cs` |
| UI: akcje tylko dla uprawnionych + audyt obiektu | `src/ZseAdmin.Client/MainWindow.xaml`, `Client.Core/ViewModels/MainViewModel.cs` |

**Kryteria akceptacji:**

- ✔ Rola bez `computers.control` nie widzi przycisków (`welcome.capabilities` → `CanControlComputers`) i dostaje `forbidden` z `MessageRouter`; test `Rola_bez_computers_control_jest_odrzucona_serwerowo`.
- ✔ Awaria źródła → `error: upstreamUnavailable` ze szczegółem, wpis audytu `failed`, UI pokazuje błąd z `detail` (bez „sukces”); test `Niepowodzenie_akcji_to_upstream_unavailable_z_nazwa_zrodla_i_wpisem_o_porazce`.
- ✔ Każda zmiana ma wpis audytowy widoczny w `audit.query` i zakładce „Akcje i audyt” w szczegółach wybranego komputera/konta; `ActionExecuteResponse.AuditId` wskazuje ten wpis.

**Poza zakresem (M5):** pilotaż na kontrolerze domeny, DPIA, certyfikaty, firewall, szkolenie.

## M5 — Wdrożenie

**Zakres:** publikacja jako usługa Windows, certyfikaty, firewall, dokumentacja dla szkoły, szkolenie, pilotaż w jednej pracowni.

**Kryteria akceptacji:**

- Serwer startuje automatycznie po restarcie maszyny.
- Pilotaż w jednej pracowni przez tydzień bez utraty danych (kompletność ≥ 95%).
- Użytkownicy końcowi (1 nauczyciel, 1 administrator) wykonują zadania z listy bez pomocy.

## M6–M8 — Nowy backlog (2026-11-26, do zaplanowania)

**Status:** specyfikacja i zadania gotowe, **implementacja nie rozpoczęta**. Źródło:
[`notatki-zamawiajacego-2026-11-26.md`](notatki-zamawiajacego-2026-11-26.md). Szczegółowe zakresy,
pliki, testy i kryteria akceptacji dla każdego zadania: [`zadania/`](zadania/).
Zasady M0–M4 (ADR-0003, 0007, 0008) obowiązują; zmiany protokołu są rozszerzeniami wersji 1.

| Etap | Zakres | Zadania |
|---|---|---|
| **M6 — System projektowy UI** | spójne, płaskie UI: 1px obramowania, tokeny, zero cieni/poświaty, ikony, formularze dwukolumnowe, kontrola w CI | [ZAD-01](zadania/01-design-system.md), [ZAD-02](zadania/02-ikony-svg.md), [ZAD-03](zadania/03-formularze-dwukolumnowe.md), [ZAD-10](zadania/10-ci-dokumentacja-dostepnosc.md) |
| **M7 — Topologia pracowni** | fizyczne sale + `topology.json` + diagram połączeń w stylu Cisco Packet Tracer | [ZAD-04](zadania/04-model-topologii.md), [ZAD-05](zadania/05-widok-topologii.md) |
| **M8 — Dostęp, pliki i tożsamość** | uczniowie bez dostępu (fail-close), tylko odczyt `/pepsi`, konto AD vs profil lokalny na stacji | [ZAD-06](zadania/06-dostep-uczniow.md), [ZAD-07](zadania/07-pliki-pepsi-backend.md), [ZAD-08](zadania/08-pliki-pepsi-ui.md), [ZAD-09](zadania/09-konto-ad-vs-lokalne.md) |

**Kryteria akceptacji etapów (skrót; pełna lista w plikach zadań):**

- **Given** klient po M6, **when** przeszukam XAML, **then** nie ma efektów cienia/poświaty ani
  kolorów wpisanych w widokach, a fokus i stany danych są opisane zgodnie z
  [`ui-design-system.md`](ui-design-system.md).
- **Given** M7, **when** otworzę widok topologii, **then** widzę sale, węzły, porty i łącza z
  `topology.json`, a komputer bez wpisu jest oznaczony „brak danych”, nie znika.
- **Given** M8, **when** konto z grupy uczniowskiej spróbuje połączyć się lub wyśle dowolne
  żądanie, **then** dostaje odmowę z audytem i żadnych danych; `/pepsi` jest dostępne wyłącznie
  do odczytu metadanych i wyłącznie dla roli z `files.read`.
- **Given** dowolny etap, **when** uruchomię CI na `windows-latest`, **then** `build`, `test`
  i kontrola „zero glow” są zielone, a dokumentacja protokołu/modelu/uprawnień jest zgodna z kodem.

**Kolejność:** ZAD-01 i ZAD-06 najpierw (fundament UI i bezpieczeństwo), potem ZAD-02/03/04,
następnie ZAD-05/09, ZAD-07, ZAD-08, a ZAD-10 domyka dokumentację na końcu każdego etapu.
M5 (wdrożenie, DPIA) pozostaje warunkiem wypuszczenia któregokolwiek z nowych etapów na produkcję.

**Decyzje blokujące:** D1–D10 w [`notatki-zamawiajacego-2026-11-26.md`](notatki-zamawiajacego-2026-11-26.md) §4
(m.in. czym jest `/pepsi`, czy „zdj” oznacza też zdjęcia, kto widzi pliki i topologię, weryfikacja
`SvgIcons 0.1.10` przed dodaniem paczki).

## Backlog decyzji (z README, sekcja „Co doprecyzować”)

| Decyzja | Etap | Właściciel | Stan |
|---|---|---|---|
| Definicja pracowni i mapowanie OU | M1 | administrator IT | zrobione |
| Źródło i retencja logów | M2 | administrator IT + DPO | domyślne: odczyt zdalny / WEF, 30 dni — do potwierdzenia w M5 |
| Typy logowań i aktywne sesje | M2 | dyrekcja | 4624/4634/4625, model zdarzeniowy (bez aktywnych sesji) |
| Częstotliwość stanu i próg offline | M1 | administrator IT | zrobione |
| Role i widoczność danych | M3 | dyrekcja + DPO | grupy AD + zakres pracowni nauczyciela |
| Sposób sygnalizowania reconnectu | M0 (zrobione) | — | zrobione |
| Ograniczenie retencji i widoczności historii | M2/M3 | DPO | mechanizmy gotowe; klauzula/DPIA przed wdrożeniem |
| Skala: liczba stacji i klientów | M1 | administrator IT | zrobione |
| D1–D10: sale/topologia, `/pepsi`, uczniowie, konta AD, ikony, font, deny overrides | M6–M8 | administrator IT, dyrekcja, DPO, deweloper | **do podjęcia** — [`notatki-zamawiajacego-2026-11-26.md`](notatki-zamawiajacego-2026-11-26.md) §4, [`zadania/README.md`](zadania/README.md) |

## Weryfikacja M2/M3/M4

Implementacja jest sprawdzana przez CI [`.github/workflows/ci.yml`](../.github/workflows/ci.yml)
na `windows-latest`: `dotnet restore`, `dotnet build -c Release`, `dotnet test`
(w tym testy kryteriów M2/M3/M4), `dotnet publish` serwera i klienta oraz kontrolę
artefaktów (`classrooms.json`, `appsettings.Production.json`, bezpieczny `appsettings.json`
klienta). Ostatni przebieg dla tej gałęzi: zielony. W środowisku bez .NET SDK nie wykonano
lokalnej kompilacji — wiążący jest wynik GitHub Actions.
