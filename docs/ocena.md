# Ocena specyfikacji ZSE-Admin

**Materiał oceniany:** `README.md` (93 linie) z commita `ef75632`, jedyny plik w repozytorium przed tą zmianą.
**Data oceny:** 2026-11-26
**Charakter:** ocena treści produktowej + ocena wykonalności technicznej (Windows Server 2016 + Active Directory), nie ocena kodu — kodu wcześniej nie było.

---

## 1. Werdykt w jednym akapicie

Specyfikacja jest **dobra na poziomie produktowym, a słaba na poziomie wykonawczym**. Autor rozumie domenę: wie, że pracownia to nie OU, że historia logowań to nie lista aktywnych sesji, że brak danych to nie to samo co offline i że WebSocket nie gwarantuje dostarczenia zdarzeń. To rzadka i cenna świadomość. Brakuje jednak wszystkiego, co pozwala zacząć implementację bez zgadywania: definicji pracowni, źródła logów, skali, modelu uprawnień w postaci macierzy, kryteriów akceptacji i decyzji o tym, co jest w MVP. Bez tego dokument jest raczej opisem wizji niż specyfikacją.

**Ocena łączna: 6/10** — dobry fundament, niewystarczający do wdrożenia.

---

## 2. Ocena punktowa

| Obszar | Ocena | Uzasadnienie |
|---|---|---|
| Jasność celu produktu | 8/10 | „Wspólny punkt pracy administratora i nauczyciela” plus cztery konkretne sytuacje użycia. Wiadomo, po co narzędzie istnieje. |
| Model domeny i przepływ | 7/10 | Poprawnie rozdzielone byty i kierunek przepływu „pracownia → komputer → logowanie → konto”. Otwarte pozostaje mapowanie pracowni na OU. |
| Priorytety | 7/10 | Pięć priorytetów ułożonych sensownie, z wyróżnieniem wiarygodności danych. |
| Wymagania funkcjonalne | 4/10 | Brak listy ekranów, pól, filtrów i zachowań brzegowych. „Szybko zobaczyć” nie jest wymaganiem mierzalnym. |
| Wymagania niefunkcjonalne | 3/10 | Brak skali (ile stacji, klas, użytkowników), czasu odpowiedzi, dostępności, liczby równoczesnych klientów, wymagań sprzętowych. |
| Bezpieczeństwo i prywatność | 6/10 | Świadomość problemu (osobne uprawnienia, retencja, audyt, dane uczniów) bez rozstrzygnięć. |
| Wykonalność techniczna | 7/10 | Stack da się zrealizować na Windows Server 2016, ale trzy źródła danych (AD, dzienniki, ping) mają różne ograniczenia — patrz sekcja 5. |
| Testowalność / akceptacja | 2/10 | Ani jednego kryterium akceptacji, więc nie da się obiektywnie stwierdzić, że funkcja „działa”. |

---

## 3. Co jest mocne

1. **Rozdzielenie pracowni i OU** (README, sekcja „Główne byty”, pkt 1). Autor nie zakłada, że struktura AD odwzorowuje rzeczywistość szkoły. To najczęstsze źródło błędów w takich narzędziach i tutaj jest nazwane wprost.
2. **Rozdzielenie stanu bieżącego od historii** (pkt 3–4). Komunikat „komputer offline” nie jest utożsamiany z „nikt się nie logował”. To przekłada się wprost na model `DataFreshnessDto` w kodzie.
3. **Świadomość ograniczeń WebSocket** (pkt 5, ostatnie zdanie). Autor wie, że kanał to nie gwarancja dostarczenia, więc potrzebny jest mechanizm ponownej synchronizacji — w szablonie zrealizowany jako `requestId` + pełne odświeżanie po ponownym połączeniu.
4. **Priorytet „bezpieczne działania”** z jawnym rozdziałem odczytu, modyfikacji AD i akcji na stacjach. W kodzie jest to `Capability.ComputersControl` vs `Capability.UsersWrite`.
5. **Lista pytań przed projektowaniem ekranów** — rzadko spotykana samoświadomość braków. To gotowy backlog decyzji (patrz sekcja 7).

---

## 4. Główne luki

| Luka | Dlaczego to blokuje | Rekomendacja | Gdzie rozwiązane |
|---|---|---|---|
| Brak definicji pracowni | Bez tego nie da się pogrupować komputerów ani zbudować nawigacji | Plik konfiguracyjny `classrooms.json` (id, nazwa, reguły OU), walidowany przy starcie | `docs/architektura.md` §6, `docs/model-danych.md` §3 |
| Brak źródła i zakresu logów | Decyduje o uprawnieniach, wydajności, retencji i o tym, co UI może obiecać | Etap 2: odczyt 4624/4634/4625 z dzienników stacji, zakres 14 dni, jawna kompletność | `docs/architektura.md` §5, `docs/api-websocket.md` |
| Brak skali | Nie da się ocenić wydajności ani dobrać infrastruktury | Założenie robocze: ≤ 120 stacji, ≤ 30 równoczesnych klientów, 4 pracownie | `docs/roadmapa.md`, ten dokument §6 |
| Brak macierzy uprawnień | „Odpowiednie uprawnienia” to nie wymaganie | Macierz rola × operacja, egzekwowana po stronie serwera | `docs/uprawnienia-i-dane-osobowe.md` |
| Brak kryteriów akceptacji | Nie da się zamknąć zadania ani napisać testów | Kryteria per funkcja w roadmapie (Given/When/Then) | `docs/roadmapa.md` |
| Brak decyzji: czy MVP zapisuje do AD | Zmienia cały model zagrożeń i potrzebę audytu | MVP tylko czyta; zapis w etapie 3 z audytem i potwierdzeniem | ADR-0003, `docs/roadmapa.md` |
| Brak definicji „świeżości” | Bez progów UI nie wie, kiedy pokazać ostrzeżenie | Progi konfigurowalne: stan 120 s, logi 900 s, katalog 3600 s | sekcja `ZseAdmin:Freshness` → `ZseAdminOptions.Freshness` |

---

## 5. Ocena wykonalności technicznej (Windows Server 2016 + AD)

**Środowisko.** Windows Server 2016 jest w **wsparciu rozszerzonym do 12 stycznia 2027**. To nie blokuje projektu, ale trzeba to wpisać do ryzyk: narzędzie może przeżyć swój system operacyjny. .NET 8 działa na Server 2016; klient WPF wymaga instalacji z pulpitem (Desktop Experience) — na Server Core nie wstanie.

**Active Directory.** Odczyt kont i grup przez LDAP (`System.DirectoryServices.Protocols`) jest tani i bezpieczny. Trzy pułapki:

- `lastLogonTimestamp` jest replikowany z opóźnieniem do ~14 dni i bywa mylony z „ostatnim logowaniem”. `lastLogon` jest dokładny, ale tylko na tym DC, na którym nastąpiło logowanie — bez pytania wszystkich DC nie ma prawdy.
- Zapytania LDAP muszą być paginowane i ograniczone (`sizeLimit`), inaczej przy większym OU dostaniemy ucięcie i błędne „kompletne”.
- Konto usługi serwera powinno mieć **minimum praw** (odczyt wybranych OU), a nie Domain Admin.

**Stan komputerów.** Ping/ICMP bywa blokowany przez zaporę na stacjach — „offline” może znaczyć „zakaz ICMP”. Realistyczny plan: ICMP + fallback na `Win32_PingStatus`/SMB (port 445) i jawne oznaczenie metody sprawdzenia oraz czasu obserwacji.

**Logi logowań.** Najdroższa część projektu. Zdarzenia 4624/4634/4625 są w dzienniku zabezpieczeń każdej stacji, więc serwer musi je zbierać (odczyt zdalny `Get-WinEvent -ComputerName`, WMI, albo Windows Event Forwarding). Odczyt zdalny wymaga uprawnień w grupie „Event Log Readers” na każdej stacji, a dzienniki mają ograniczoną retencję (domyślnie przewijają się po ~20 MB). Wniosek: **bez WEF albo własnego bufora historia będzie dziurawa** i UI musi to pokazywać jako `Partial`.

**WebSocket.** ASP.NET Core obsługuje go bez dodatkowej infrastruktury. Za reverse proxy (IIS) trzeba pamiętać o włączonym protokole WebSocket i o `X-Forwarded-*`. Klient WPF utrzymuje jedno połączenie na sesję — przy 30 klientach to 30 lekkich sesji, bez problemu.

**Czego brakuje w specyfikacji, a jest krytyczne:** ile stacji ma być monitorowanych, jak często (wpływ na ruch i CPU stacji) oraz czy szkoła ma zgodę/ocenę skutków dla monitorowania aktywności uczniów.

---

## 6. Ryzyka

| # | Ryzyko | Prawd. | Wpływ | Mitygacja |
|---|---|---|---|---|
| R1 | Server 2016 traci wsparcie 2027-01 | wysoka | średni | Zaplanować migrację; aplikacja nie może zależeć od API tylko-Windows-2016 |
| R2 | Dziury w historii logowań (przewijanie dzienników) | wysoka | wysoki | WEF lub bufor; `Completeness=Partial` i widoczny zakres |
| R3 | Fałszywe „offline” przez blokadę ICMP | średnia | wysoki | Wielometodowe sprawdzanie i opis metody w UI |
| R4 | Token w MVP trafia na produkcję | średnia | krytyczny | Bramka wdrożeniowa: Kerberos/Windows auth przed produkcją |
| R5 | RODO: monitoring uczniów bez podstawy i retencji | średnia | krytyczny | DPIA, minimalizacja, retencja 30 dni, ograniczenie do nauczycieli prowadzących zajęcia |
| R6 | Brak zdefiniowanej skali → przeciążenie stacji | średnia | średni | Interwał ≥ 60 s na stację, throttling, pomiar |
| R7 | `lastLogonTimestamp` pokazany jako „ostatnie logowanie” | wysoka | średni | Nazwać kolumnę „ostatnie logowanie (AD, może być nieaktualne)” lub użyć zdarzeń |
| R8 | Awaria DC lub serwera = brak danych | średnia | średni | Cache ostatnich danych + czytelny stan „dane z 10:42, brak odświeżenia” |

---

## 7. Decyzje do podjęcia (z pytania „co doprecyzować”)

Poniżej domyślne rozstrzygnięcia przyjęte w szablonie — każde wymaga potwierdzenia przez właściciela produktu (IT szkoły / dyrekcja / DPO).

| Pytanie ze specyfikacji | Decyzja domyślna w szablonie | Kto potwierdza |
|---|---|---|
| Jak zdefiniować pracownię i mapowanie na OU? | Konfiguracja `classrooms.json`: lista pracowni + reguła OU (jedno OU lub wiele). Komputer bez dopasowania trafia do „poza pracownią”, nigdy nie znika. | administrator IT |
| Źródło i retencja logów? | Dzienniki zabezpieczeń stacji, zakres 14 dni, kompletność oznaczana jawnie. | administrator IT |
| Jakie typy logowań? | 4624 (logowanie), 4634 (wylogowanie), 4625 (nieudane) — bez sesji aktywnych w MVP. | dyrekcja/IT |
| Jak często stan i IP? | Stan co 15 s (konfigurowalne), IP z ostatniej obserwacji; próg „stale” 120 s. | administrator IT |
| Role i uprawnienia? | Administrator / Nauczyciel / ReadOnly, egzekwowane serwerowo. | dyrekcja |
| Jak sygnalizować utratę połączenia? | Osobny stan sesji w UI + pełne odświeżenie po reconnect; brak danych ≠ offline. | — (zaimplementowane) |
| Ograniczenie widoczności i retencji? | Retencja 30 dni (do potwierdzenia), dostęp nauczycieli ograniczony do pracowni, w której mają zajęcia (etap 3). | DPO |

---

## 8. Zakres MVP (to, co powinno powstać najpierw)

**W zakresie:** lista pracowni z OU, lista komputerów z filtrem pracowni, stan + IP + czas obserwacji, ostatnie logowania z filtrem komputera i użytkownika, kartoteka konta AD (grupy, OU), jawna świeżość i kompletność, role i uprawnienia serwerowe, health check, konsola testowa protokołu.

**Poza zakresem MVP:** zapis do AD, zdalne akcje na stacjach (restart, blokada), aktywne sesje w czasie rzeczywistym, raporty i eksporty, powiadomienia, aplikacja webowa, integracja z dziennikiem elektronicznym.

**Definicja gotowości MVP:** administrator widzi wszystkie stacje z pracowni, poprawnie odróżnia offline od braku danych, znajduje konto i jego grupy, a nauczyciel nie może wykonać żadnej operacji zapisu — potwierdzone testem uprawnień.

---

## 9. Szacunek nakładu (orientacyjny, 1 osoba)

| Etap | Zakres | Nakład |
|---|---|---|
| M0 | Szkielet: kontrakty, serwer WS, klient WPF, dane przykładowe, testy, CI (zrobione) | 2–3 dni |
| M1 | Adapter AD (LDAP): komputery, konta, grupy, mapowanie pracowni z konfiguracji (**zrobione**) | 5–8 dni |
| M2 | Logi logowań: zbieranie, filtr, kompletność, retencja | 8–12 dni |
| M3 | Uprawnienia produkcyjne (Kerberos/grupy AD), audyt operacji | 4–6 dni |
| M4 | Akcje na stacjach (restart, blokada) + potwierdzenia | 5–10 dni |
| M5 | Wdrożenie, hardening, dokumentacja dla szkoły | 3–5 dni |

Razem: **ok. 27–44 dni roboczych** do wersji produkcyjnej bez akcji na stacjach; M0–M2 (czyli użyteczne narzędzie do odczytu) to około 15–23 dni.

---

## 10. Rekomendacje, w kolejności

1. **P0 — dopisać kryteria akceptacji** do każdej funkcji (roadmapa już je zawiera jako punkt wyjścia).
2. **P0 — ustalić definicję pracowni** i zapisać ją jako konfigurację, nie jako założenie w kodzie.
3. **P0 — rozstrzygnąć retencję i podstawę prawną** logowań, zanim zbierzemy pierwsze zdarzenie.
4. **P1 — zebrać logi przez WEF lub bufor**, zamiast polegać na zdalnym odczycie przewijanych dzienników.
5. **P1 — zmierzyć skalę** (liczba stacji, czas odpowiedzi) na jednej pracowni pilotażowo.
6. **P1 — nie wypuszczać tokenów MVP na produkcję**; bramka: uwierzytelnianie Windows.
7. **P2 — dodać „ostatnia znana obserwacja”** do każdego elementu UI, także po restarcie serwera (cache).

---

## 10a. Stan implementacji po etapach M2–M4

| Obszar | Stan | Gdzie |
|---|---|---|
| Protokół, serwer WS, klient WPF, świeżość, role | zrobione (M0) | `src/` |
| Adapter LDAP: komputery, konta, grupy `tokenGroups`, paginacja, limity | zrobione (M1) | `Infrastructure/Ldap/` |
| Definicja pracowni i mapowanie OU (walidacja przy starcie) | zrobione (M1) | `classrooms.json`, `Infrastructure/Classrooms/` |
| Sprawdzanie dostępności stacji (ICMP z budżetem czasu) | zrobione (M1) | `Infrastructure/Ldap/StatusProbe.cs` |
| `upstreamUnavailable` zamiast pustych list | zrobione (M1) | `MessageRouter`, `SourceUnavailableException` |
| Adres IP stacji (WMI/ARP/DNS) | do M1.1 | — |
| Historia logowań: zdalny odczyt 4624/4634/4625, bufor JSONL, retencja 30 dni, kompletność `partial` z notą „N z M stacji" | zaimplementowane (M2) — weryfikacja na pilocie (Windows + *Event Log Readers*) | `Infrastructure/Logins/` |
| Windows/Kerberos (Negotiate) + grupy AD → role; odmowa bez grupy; `wss` przed upgrade'em; bramka startu (tokeny/brak wss → exit 1) | zaimplementowane (M3) | `Program.cs`, `Security/` |
| Ograniczenie widoczności logowań nauczyciela do jego pracowni (`teachers` w `classrooms.json`) | zaimplementowane (M3) | `MessageRouter`, `ClassroomMap.ClassroomIdsForTeacher` |
| Audyt: `audit.jsonl`, retencja 12 mies., podgląd `audit.query`, hasła poza audytem | zaimplementowane (M3/M4) | `Infrastructure/Audit/` |
| Akcje na stacjach: restart (`shutdown.exe /r /m`), blokada (PsExec), reset hasła (LDAPS `unicodePwd`), dodanie do grupy (`member`), zawsze `confirm=true` i audyt | zaimplementowane (M4) — weryfikacja na pilocie | `Infrastructure/Actions/` |
| Przyciski akcji w UI ukryte bez uprawnienia; potwierdzenia; pasek statusu pokazuje błąd źródła, nie „sukces” | zaimplementowane (M4) | `MainViewModel`, `WpfUserInteraction`, `ResetPasswordWindow` |
| Konsola `debug.html` rozszerzona o M4 (`computers.action`, `users.resetPassword`, `users.addToGroup`, `audit.query`) | zrobione | `wwwroot/debug.html` |

Testy: `ClassroomMapTests` (+ przypisanie nauczycieli), `SecurityConfigurationValidatorTests`,
rozszerzone `MessageRouterTests` (scoping nauczyciela, confirm=true, audyt sukcesu/porażki),
`ProtocolSerializationTests` (M4 round-trip), `LdapInventoryTests`, `FreshnessServiceTests`
— wszystkie działają bez kontrolera domeny.

## 11. Co powstało w ramach tej oceny

Ten commit dodaje szkielet, który te luki **materializuje w kodzie i dokumentacji**, a nie tylko opisuje:

- `docs/architektura.md` — komponenty, przepływ, plan adapterów AD/WMI/EventLog, wdrożenie.
- `docs/model-danych.md` — byty, mapowanie na AD, semantyka świeżości.
- `docs/api-websocket.md` — pełny protokół z przykładami.
- `docs/uprawnienia-i-dane-osobowe.md` — macierz uprawnień, audyt, RODO.
- `docs/roadmapa.md` — etapy z kryteriami akceptacji.
- `docs/adr/` — cztery decyzje architektoniczne z alternatywami.
- `src/` — działający szkielet .NET 8: serwer WebSocket + klient WPF + kontrakty + testy.
- `src/ZseAdmin.Server/wwwroot/debug.html` — konsola protokołu do ręcznych testów bez klienta.

**Uczciwe zastrzeżenie:** kod nie został skompilowany w środowisku, w którym powstał (brak .NET SDK i dostępu do NuGet). Build i testy uruchamia CI na `windows-latest` oraz lokalny `dotnet build` na maszynie z .NET 8. Szczegóły i lista rzeczy do sprawdzenia: `docs/uruchomienie.md` §9.
