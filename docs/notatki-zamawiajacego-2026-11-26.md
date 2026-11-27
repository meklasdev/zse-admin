# Notatki zamawiającego — 2026-11-26

**Status:** surowe notatki + interpretacja + **odpowiedzi zamawiającego (D1–D10 podjęte 2026-11-26)**.
To jest **materiał wejściowy**, nie decyzja architektoniczna.
**Źródło:** wiadomość zamawiającego w repozytorium (zachowana dosłownie w §1).
**Powiązane:** [`ui-design-system.md`](ui-design-system.md), [`adr/0009-ikony-svg.md`](adr/0009-ikony-svg.md),
[`zadania/`](zadania/), [`roadmapa.md`](roadmapa.md).

Dokument powstał, żeby nic nie zginęło między luźną notatką a zadaniami dla deweloperów.
§2–§3 to uporządkowana interpretacja; tam, gdzie notatka była niejednoznaczna, pojawiła się
decyzja (§4). Decyzje D1–D10 zostały podjęte przez zamawiającego 2026-11-26 w formie odpowiedzi
ABC — §4 zawiera odpowiedzi i ich techniczną interpretację. Zadania M6–M8 nie są już blokowane,
z wyjątkiem trzech drobnych doprecyzowań w §4.1.

---

## 1. Oryginalna notatka (dosłownie)

```text
W szkole jest tak są sale kilka i w.kazdej są pracownie robocze czyli trzeba mieć zdj jak z cisco
pakiet tracer takie diagramy jak są podpinane itp itd

Są uczniowie oni wgl nie mają do tego dostępu mamy też hosing plików w /pepsi są tam pliki są
jakieś tam foldery itp itd

Każdy uczeń ma swoje konto na Ad ale nie przenoszą się pliki miedzy stacjami czyli z ad robi się
konto lokalne na stacji

Do ikonek możemy użyć

dotnet add package SvgIcons --version 0.1.10

Cale ui ma być spunjnr zero glow itp

**Styl UI dla Agenta (.NET)**

**1. Kierunek i ogólny charakter**
- Estetyka: lekki, czysty minimalizm w duchu współczesnego SaaS i subtelnego neo-brutalizmu —
  nacisk na strukturę, linie i przestrzeń zamiast ozdobników.
- Głębia: płaska (flat), oparta na obramowaniach i odstępach; bez rozmytych cieni ani ciężkich
  efektów wizualnych.
- Oddech: dużo wolnej przestrzeni wokół elementów, czytelne marginesy i przejrzysty rytm
  pionowy oraz poziomy.

**2. Geometria, krawędzie i powierzchnie**
- Kontury: cienkie, jednolite obramowania (1px) definiujące granice elementów i rozdzielające sekcje.
- Zaokrąglenia: umiarkowane dla standardowych kontrolek i powierzchni; pełne (pill) dla drobnych
  liczników/znaczników oraz wybranych elementów akcyjnych; przerywane dla stref upuszczania plików.
- Zróżnicowanie warstw: lekkie rozróżnienie tła głównego i stref pomocniczych/nawigacyjnych oraz
  wyraźne zaznaczenie stanu aktywnego lub fokusu poprzez zmianę obramowania/kontrastu.
- Separacja sekcji: poziome linie podziału między grupami ustawień zamiast zamykania każdego pola
  w osobnej, ciężkiej karcie.

**3. Typografia i hierarchia**
- Krój pisma: czysty, nowoczesny sans-serif (np. Inter, Plus Jakarta Sans lub natywne kroje
  systemowe), z lekko swobodnym, przystępnym charakterem.
- Hierarchia: wyraźna różnica między tytułem widoku, nagłówkami grup, etykietami pól, treścią
  wprowadzaną przez użytkownika oraz drobnymi opisami/licznikami — budowana głównie rozmiarem
  i wagą fontu (Regular, Medium, Bold).

**4. Kompozycja i zachowanie kontrolek**
- Układ formularzy: dwukolumnowy w obrębie wiersza — po jednej stronie opis i kontekst pola,
  po drugiej właściwa kontrolka.
- Kontrolki wejściowe: przejrzyste pola jedno- i wieloliniowe z czytelnym wypełnieniem wewnętrznym,
  opcjonalnymi prefiksami/ikonami oraz dyskretnymi metadanymi (np. licznik znaków) wkomponowanymi
  w obręb pola.

i have this for add to my reposidory and create a more task for devoloper to vrite
```

---

## 2. Fakty wynikające z notatki

| ID | Fakt | Co to znaczy dla produktu |
|---|---|---|
| F1 | W szkole jest kilka **sal**, a w każdej **pracownie robocze** (stanowiska) | Potrzebna fizyczna hierarchia: budynek → sala → pracownia/stanowisko. Istniejący `ClassroomDto` opisuje pracownię jako kontekst szkolny mapowany na OU — **nie jest** tym samym co sala. |
| F2 | „zdj jak z cisco pakiet tracer … jak są podpinane” | **D2 = B:** gotowe rysunki/grafiki komputerów i urządzeń w stylu Packet Tracer **oraz** diagramy ułożenia komputerów w sali **i** diagramy połączeń (porty, kable). To rysunki wektorowe, nie fotografie szafy — źródło grafik doprecyzowuje §4.1. |
| F3 | „uczniowie wgl nie mają do tego dostępu” | Uczeń nie ma dostępu **ani do aplikacji, ani do danych/plików**. To wymaganie bezpieczeństwa egzekwowane po stronie serwera (fail-close), nie tylko ukrycie przycisków w UI. |
| F4 | „hosing plików w /pepsi … pliki … foldery” | **D3:** to **dysk sieciowy na serwerze** — w konfiguracji zapisujemy ścieżkę **UNC**, nie literę mapowanego dysku. **D4:** listing widzą wszystkie role aplikacji; zapis na udziale jest wydzielony i **poza aplikacją** (v1 tylko odczyt metadanych). |
| F5 | „każdy uczeń ma konto na AD, ale pliki nie przenoszą się między stacjami; z AD robi się konto lokalne na stacji” | **D5 = A (potwierdzone):** logowanie do domeny **tworzy profil lokalny na tej stacji**; brak profili mobilnych / przekierowania folderów. UI nie sugeruje, że pliki ucznia są na serwerze. `accountScope` rozróżnia konto domenowe od lokalnego bez zgadywania. |
| F6 | `dotnet add package SvgIcons --version 0.1.10` | **D8 = B:** paczka **ma zostać dodana** w ZAD-02. Bramka weryfikacji (`restore` + build w CI) pozostaje warunkiem; przy porażce fallback z lokalnymi `Path`. Patrz [`adr/0009-ikony-svg.md`](adr/0009-ikony-svg.md) (przyjęte warunkowo) i **ZAD-02**. |
| F7 | „całe ui ma być spójne, zero glow” + style guide | Jednolity system projektowy dla całego klienta WPF: flat, 1px, bez cieni/poświaty/gradientów, dwukolumnowe formularze. Rozpisany w [`ui-design-system.md`](ui-design-system.md). **D9 = A:** krój systemowy `Segoe UI`, bez bundlingu. |
| F8 | „add to my repository and create more tasks for developers” | Dodanie specyfikacji do repozytorium i utworzenie **backlogu zadań** z kryteriami akceptacji — [`zadania/`](zadania/). |

---

## 3. Interpretacja i granice (żeby nie zbudować czegoś złego)

1. **Sala ≠ pracownia ≠ OU.** `classrooms.json` mapuje OU na pracownię (reguły w
   [`architektura.md`](architektura.md) §6). Nowa warstwa „sala” jest fizyczna i konfiguracyjna;
   relacja sala ↔ pracownia jest jawna i nie musi być 1:1. Nie wolno wyprowadzać sali z nazwy OU.
2. **Topologia to konfiguracja, nie odkrywanie sieci.** Podobnie jak mapa OU, opis węzłów i łączy
   ma być plikiem konfiguracyjnym walidowanym przy starcie. Komputer z AD bez wpisu w topologii
   ma dostać stan „brak danych / niepodpięty”, a nie zniknąć (zgodnie z `docs/model-danych.md` §4).
3. **Brak dostępu uczniów = odmowa na serwerze.** `docs/uprawnienia-i-dane-osobowe.md` §1 już
   ustala, że ukrycie w UI nie jest zabezpieczeniem. Nowe typy wiadomości (topologia, pliki) muszą
   przechodzić przez `CapabilityGuard`, a odmowy mają być audytowane. UI ma pokazywać „brak dostępu”,
   a nie pustą listę, która wygląda jak „brak danych”.
4. **Konto AD nie „zamienia się” w konto lokalne.** Dosłowna konwersja konta domenowego na lokalne
   nie zachodzi przy zwykłym logowaniu; powstaje profil użytkownika na konkretnej stacji
   (`C:\Users\<login>`), a pliki zostają na dysku tej stacji. **D5 = A (potwierdzone):** logowanie
   do domeny, profil i pliki zostają na tej stacji. Konta lokalne mogą nadal wystąpić w dzienniku
   (np. serwisowe) — rozróżniamy je przez `accountScope`, bez zgadywania.
5. **`/pepsi` to dysk sieciowy (D3).** Ścieżka jest **konfiguracją** i musi być wpisana jako **UNC**,
   nie litera mapowanego dysku: mapowanie `P:\` jest per użytkownik i usługa Windows działająca na
   koncie serwisowym może go nie widzieć. Konto usługi potrzebuje praw odczytu do udziału. W v1 nie
   pobieramy treści plików ani nie zapisujemy na udziale; wydzielone prawa zapisu na `/pepsi`
   pozostają sprawą NTFS poza aplikacją (D4).
6. **Zero glow to twarde ograniczenie, nie preferencja.** Dotyczy całego UI, także nowych widoków:
   zakaz `DropShadowEffect`, `BlurEffect`, gradientów, neonowych kolorów i „unoszenia” kart. Głębię
   dają wyłącznie 1px obramowania, różnica tła i odstępy. Kontrola w CI (ZAD-10).
7. **Zgodność protokołu.** Nowe typy wiadomości i opcjonalne pola DTO są zmianami kompatybilnymi
   wstecz — `protocolVersion` pozostaje `1` ( reguły w [`api-websocket.md`](api-websocket.md) §7 ).
   Stary klient musi ignorować nieznane pola.
8. **Nie zmieniamy ustaleń M0–M4.** ADR-0003 (brak własnej bazy), ADR-0007 (Windows/Kerberos, `wss`,
   zakres nauczyciela) i ADR-0008 (potwierdzenie + audyt akcji) obowiązują dalej. Nowe zadania są
   rozszerzeniem, nie przepisaniem produktu.
9. **D7 = C: topologia bez zawężania do sal nauczyciela.** Każda zalogowana rola aplikacji
   (Administrator, Nauczyciel, ReadOnly) widzi całą topologię. To świadome odstępstwo od zakresu
   `classroomScope` z ADR-0007 — dotyczy **wyłącznie** nowego widoku topologii; historia logowań
   i dane kont nadal są filtrowane tak jak w M3. Uczniowie i tak nie mają dostępu do aplikacji (D10).

---

## 4. Decyzje — odpowiedzi zamawiającego (2026-11-26)

**Status: D1–D10 podjęte.** Zamawiający odpowiedział na pytania w formie A/B/C. Poniżej numeracja
z rozmowy, odpowiedź i jej techniczna interpretacja — to **jedyne źródło prawdy** dla zadań M6–M8.
Zadania nie są już blokowane; trzy drobne doprecyzowania (§4.1) nie wstrzymują startu prac.

| # / ID | Pytanie | Odpowiedź | Interpretacja dla dewelopera |
|---|---|---|---|
| 1 / D3 | Czym jest `/pepsi`? | **Dysk sieciowy na serwerze** („pewnie A, ale to dysk sieciowy”) | `ZseAdmin:FileHosting:Root` przyjmuje **UNC** (`\\serwer\pepsi`), nigdy litery mapowanego dysku. Konto usługi musi mieć prawa odczytu do udziału; mapowanie `P:\` jest per użytkownik i nie jest widoczne dla usługi Windows. |
| 2 / D2 | Co oznacza „zdj jak z Cisco”? | **Gotowe obrazki/rysunki komputerów jak w Packet Tracer + diagramy jak są poukładane komputery** | ZAD-05 dostarcza: (a) wektorowy zestaw grafik urządzeń (stacja, switch, patch panel, uplink), (b) diagram **ułożenia stanowisk w sali**, (c) diagram **połączeń** z portami. To rysunki, nie fotografie szafy. |
| 3 / D1 | Sale i pracownie | **C — „sam sobie robi admin”** | Administrator przygotowuje listę sal i przypisanie `classroomIds` w `topology.json`. Aplikacja nie zgaduje relacji; walidacja przy starcie zostaje. |
| 4 / D5 | Konto ucznia na stacji | **A** | Logowanie do domeny tworzy profil lokalny na tej stacji; pliki nie przenoszą się między stacjami (brak profili mobilnych). `accountScope = domain/local/unknown` bez zgadywania. |
| 5 / D10 | Uczeń z grupą nauczycielską | **A** | Deny overrides: grupa wykluczająca ma pierwszeństwo nad przypisaniem roli; 401 + audyt `auth.rejected`. |
| 6 / D4 | Kto widzi pliki z `/pepsi` | **„Wszyscy, ale uczniowie nie mają permisji do zapisania — jest tam wydzielone”** | `files.read` dla **Administrator, Teacher, ReadOnly** (każdej roli, która w ogóle loguje się do aplikacji). Uczniowie są odrzucani wcześniej (D10). Zapis na udziale to osobne uprawnienia NTFS **poza aplikacją** — v1 pozostaje wyłącznie odczytem metadanych. |
| 7 / D7 | Kto widzi topologię | **C — każdy zalogowany** | `topology.read` dla wszystkich trzech ról; **brak filtrowania po `classroomScope` dla topologii**. Świadome odstępstwo od ADR-0007 dotyczy tylko nowego widoku — historia logowań i konta nadal są filtrowane. |
| 8 / D6 | Pliki ucznia w aplikacji | **„Uczeń nie ma dostępu do aplikacji”** | UI nie pokazuje żadnych danych o plikach ucznia ani powiązania plik ↔ konto. Notatka techniczna o braku profili mobilnych (ZAD-09) zostaje jako informacja dla administratora, bez wskazania na osobę. |
| 9 / D8 | Ikony / `SvgIcons 0.1.10` | **B — dodaj paczkę** | ZAD-02 dodaje `dotnet add package SvgIcons --version 0.1.10` do klienta. Bramka 7 sprawdzeń z ADR-0009 zostaje obowiązkowa: zielony `dotnet restore` + build w CI. Przy porażce fallback `Path` (bez eskalacji). ADR-0009: „przyjęte warunkowo”, ADR-0001: adnotacja o jednej zależności. |
| 10 / D9 | Czcionka | **A** | `Segoe UI` (krój systemowy) w `Theme.xaml`; nie dołączamy Inter / Plus Jakarta Sans. |

**Zmiana względem pierwotnych propozycji domyślnych:** D2 (B zamiast „tylko diagram”), D4 (wszystkie
role zamiast tylko Administrator), D7 (wszystkie sale z zakresu nauczyciela), D8 (dodaj paczkę
zamiast „nie dodawaj bez weryfikacji”), D1 (lista od admina). D5, D9, D10 pozostały przy propozycji.

**Realizacja decyzji w kodzie (2026-11-27):** D10 → ZAD-06 (PR #19: `Security:DeniedGroups`,
deny overrides przed rolą, 401 + `auth.rejected`, `MessageCapabilities` fail-close, ekran
„Brak dostępu”); D9 → ZAD-01 (PR #18). D1–D4, D6–D8 czekają na ZAD-04/05/07/08; ich
konfiguracja (`topology.json`, UNC udziału, pliki grafik) pozostaje po stronie administratora IT.

### 4.1 Trzy drobne doprecyzowania (nie blokują startu)

1. **D2 — źródło grafik:** czy szkoła ma już własne pliki rysunków urządzeń? Jeśli nie, ZAD-05
   tworzy własny, wektorowy zestaw w `Assets/Topology/` (1px, jeden kolor, zero glow).
2. **D3 — dokładny UNC i konto usługi:** administrator podaje pełną ścieżkę udziału i potwierdza,
   że konto usługi ma do niego odczyt. Do tego czasu `Enabled = false`.
3. **D1 — lista sal:** administrator podmienia przykładowe `sala-101` / `classroomIds` w
   `topology.json` na rzeczywiste sale. Walidacja (unikalne `id`, istniejące `classroomIds`) działa
   od pierwszego uruchomienia.

**Uwaga organizacyjna:** zgłoszenia GitHub #5–#15 (zadania + pytania D1–D10) zostały zamknięte przez
zamawiającego 2026-11-26. Treść zadań i odpowiedzi pozostaje w tym dokumencie i w [`zadania/`](zadania/);
nie tworzymy nowych zgłoszeń bez wyraźnej prośby.

---

## 5. Mapowanie na istniejące dokumenty

| Temat z notatki | Dokument w repo | Co trzeba zaktualizować przy realizacji |
|---|---|---|
| Pracownie / OU | `architektura.md` §6, `classrooms.json`, `model-danych.md` §1 | dodać warstwę „sala” i `topology.json` |
| Nowe byty (węzły, łącza, pliki, zakres konta) | `model-danych.md` | nowe sekcje DTO + semantyka braków |
| Nowe komunikaty | `api-websocket.md` §3, §7.1 | `topology.get`, `files.list`; dopisać do tabeli rozszerzeń v1 |
| Uprawnienia | `uprawnienia-i-dane-osobowe.md`, `adr/0007` | `topology.read` i `files.read` dla wszystkich ról aplikacji (D4, D7), jawna odmowa uczniów (D10), audyt odmów |
| Styl UI | `ui-design-system.md` (nowy) | tokeny, kontrolki, checklista |
| Ikony | `adr/0009-ikony-svg.md`, `adr/0001` | D8 = B: dodać `SvgIcons 0.1.10` po zielonym CI; adnotacja o jednej zależności NuGet |
| Plan i zadania | `roadmapa.md`, `zadania/` (nowy) | etapy M6–M8 + kryteria Given/When/Then |

---

## 6. Czego **nie** robimy w tym etapie

- Nie implementujemy funkcji — ten commit dodaje specyfikację i zadania, zgodnie z prośbą
  „add to my repository and create more tasks for developers”.
- **D8 = B:** `SvgIcons 0.1.10` ma zostać dodana w ZAD-02, ale dopiero po pomyślnym
  `dotnet restore` + buildzie w CI; przy porażce obowiązuje fallback z lokalnymi geometriami `Path`.
  Ten commit jest wyłącznie dokumentacyjny — paczki jeszcze nie ma w `*.csproj`.
- Nie podbijamy `protocolVersion` i nie zmieniamy znaczenia istniejących pól.
- Nie dodajemy zapisu/plików do `/pepsi`, pobierania treści, ani dostępu dla uczniów.
- Nie wprowadzamy własnej bazy danych (ADR-0003) ani nowej roli „Uczeń” z uprawnieniami.
- Nie dodajemy cieni, gradientów, poświaty ani „kartowych” ozdobników do UI.
