# Notatki zamawiającego — 2026-11-26

**Status:** surowe notatki + interpretacja. To jest **materiał wejściowy**, nie decyzja architektoniczna.
**Źródło:** wiadomość zamawiającego w repozytorium (zachowana dosłownie w §1).
**Powiązane:** [`ui-design-system.md`](ui-design-system.md), [`adr/0009-ikony-svg.md`](adr/0009-ikony-svg.md),
[`zadania/`](zadania/), [`roadmapa.md`](roadmapa.md).

Dokument powstał, żeby nic nie zginęło między luźną notatką a zadaniami dla deweloperów.
§2–§3 to uporządkowana interpretacja; tam, gdzie notatka jest niejednoznaczna, zamiast zgadywać
pojawia się decyzja do podjęcia (§4). Do momentu jej podjęcia zadania zależne są oznaczone jako
zablokowane.

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
| F2 | „zdj jak z cisco pakiet tracer … jak są podpinane” | Widok **diagramu topologii**: węzły (stacja, switch, patch panel, uplink) i łącza z portami, w konwencji diagramu sieciowego. Nie wiadomo jeszcze, czy „zdj” oznacza też zdjęcia szafy/patch panelu — decyzja **D2**. |
| F3 | „uczniowie wgl nie mają do tego dostępu” | Uczeń nie ma dostępu **ani do aplikacji, ani do danych/plików**. To wymaganie bezpieczeństwa egzekwowane po stronie serwera (fail-close), nie tylko ukrycie przycisków w UI. |
| F4 | „hosing plików w /pepsi … pliki … foldery” | Istnieje zasób plików w katalogu `/pepsi`. W v1 proponujemy **tylko odczyt metadanych** (drzewo folderów + lista plików). Ścieżka musi być konfiguracją, nie literałem w kodzie — decyzja **D3**. |
| F5 | „każdy uczeń ma konto na AD, ale pliki nie przenoszą się między stacjami; z AD robi się konto lokalne na stacji” | Logowanie do domeny **tworzy profil lokalny na tej stacji**; brak profili mobilnych / przekierowania folderów. UI nie może sugerować, że pliki ucznia są na serwerze albo że „podążają” za kontem. Trzeba rozróżniać konto domenowe od konta lokalnego w historii logowań — decyzja **D5**. |
| F6 | `dotnet add package SvgIcons --version 0.1.10` | Kandydat na źródło ikon. Wymaga weryfikacji przed dodaniem do projektu (konflikt z ADR-0001 „WPF bez dodatkowych zależności NuGet”) — patrz [`adr/0009-ikony-svg.md`](adr/0009-ikony-svg.md) i **ZAD-02**. |
| F7 | „całe ui ma być spójne, zero glow” + style guide | Jednolity system projektowy dla całego klienta WPF: flat, 1px, bez cieni/poświaty/gradientów, dwukolumnowe formularze. Rozpisany w [`ui-design-system.md`](ui-design-system.md). |
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
   (`C:\Users\<login>`), a pliki zostają na dysku tej stacji. Jeśli w szkole działa jednak osobny
   mechanizm zakładania kont lokalnych, model trzeba skorygować — decyzja **D5**. Aplikacja nie
   powinna zakładać żadnego z wariantów „w cichości”.
5. **`/pepsi` nie jest jeszcze zdefiniowane.** Nie wiadomo, czy to katalog lokalny serwera, udział UNC,
   czy podmontowany zasób. Do czasu decyzji **D3** nie wolno hardkodować ścieżki, zakładać uprawnień
   konta usługi ani implementować zapisu. W v1 nie pobieramy treści plików.
6. **Zero glow to twarde ograniczenie, nie preferencja.** Dotyczy całego UI, także nowych widoków:
   zakaz `DropShadowEffect`, `BlurEffect`, gradientów, neonowych kolorów i „unoszenia” kart. Głębię
   dają wyłącznie 1px obramowania, różnica tła i odstępy. Kontrola w CI (ZAD-10).
7. **Zgodność protokołu.** Nowe typy wiadomości i opcjonalne pola DTO są zmianami kompatybilnymi
   wstecz — `protocolVersion` pozostaje `1` ( reguły w [`api-websocket.md`](api-websocket.md) §7 ).
   Stary klient musi ignorować nieznane pola.
8. **Nie zmieniamy ustaleń M0–M4.** ADR-0003 (brak własnej bazy), ADR-0007 (Windows/Kerberos, `wss`,
   zakres nauczyciela) i ADR-0008 (potwierdzenie + audyt akcji) obowiązują dalej. Nowe zadania są
   rozszerzeniem, nie przepisaniem produktu.

---

## 4. Decyzje do podjęcia (blokują część zadań)

| ID | Pytanie | Właściciel | Blokuje | Domyślna propozycja (do potwierdzenia) |
|---|---|---|---|---|
| D1 | Ile jest sal, jak się nazywają i jak łączą się z pracowniami z `classrooms.json`? Czy jedna sala może mieć kilka pracowni? | administrator IT | ZAD-04, ZAD-05 | `topology.json`: sala ma `id`, `name`, listę `classroomIds`; relacja wiele-do-wielu dopuszczona, walidowana |
| D2 | Czy „zdj” to diagram, czy też zdjęcia/foto szafy i patch paneli? Jeśli zdjęcia: gdzie są trzymane i kto je dodaje? | administrator IT / dyrekcja | ZAD-05 (załączniki) | v1: tylko diagram wektorowy; zdjęcia jako osobne zadanie po doprecyzowaniu |
| D3 | Czym jest `/pepsi`: ścieżka lokalna, UNC `\\serwer\pepsi`, czy mount? Jakie konto usługi ma do niego dostęp i z jakimi prawami? | administrator IT | ZAD-07, ZAD-08 | konfiguracja `ZseAdmin:FileHosting:Root`, tylko odczyt, bez hardkodowania |
| D4 | Kto widzi listing `/pepsi`: tylko Administrator, czy też Nauczyciel (i w jakim zakresie)? | dyrekcja + DPO | ZAD-06, ZAD-07 | tylko Administrator; `files.read` nie trafia do roli Teacher ani ReadOnly |
| D5 | Jak dokładnie działa logowanie ucznia: profil domenowy tworzony lokalnie, czy osobne konto lokalne zakładane na stacji? Czy są wyjątki (konta serwisowe, nauczyciele)? | administrator IT | ZAD-09 | `accountScope`: `domain` / `local` / `unknown` wyznaczany z dziennika 4624, bez zgadywania |
| D6 | Czy aplikacja ma w ogóle wskazywać, gdzie leżą pliki ucznia, czy tylko komunikować „pliki zostają na stacji”? | dyrekcja + DPO | ZAD-08, ZAD-09 | tylko jawna notatka w UI; brak mapowania plików na konta uczniów (RODO) |
| D7 | Czy nauczyciel widzi topologię (tylko swoje sale) czy wyłącznie administrator? | dyrekcja | ZAD-04, ZAD-05 | nauczyciel widzi topologię w `classroomScope`, serwer filtruje |
| D8 | Czy pakiet `SvgIcons 0.1.10` jest dostępny, wspiera `net8.0-windows`/WPF i ma akceptowalną licencję? Czy dopuszczamy zewnętrzną zależność NuGet w kliencie (zmiana ADR-0001)? | deweloper + administrator IT | ZAD-02 | bramka weryfikacji; fallback: lokalne geometrie `Path` bez zależności |
| D9 | Czy font Inter / Plus Jakarta Sans ma być dołączony do aplikacji (licencja OFL, rozmiar artefaktu), czy wystarczy natywny `Segoe UI`? | deweloper | ZAD-01 | fallback systemowy; bundling tylko po zatwierdzeniu licencji |
| D10 | Czy uczeń, który przypadkiem trafi też do grupy `ZSE-Admin-*`, ma być i tak odrzucony (deny overrides)? | administrator IT + DPO | ZAD-06 | tak: jawna grupa wykluczająca ma pierwszeństwo nad przypisaniem roli |

Odpowiedzi dopisuj w tym dokumencie i aktualizuj status zadania w [`zadania/README.md`](zadania/README.md).

---

## 5. Mapowanie na istniejące dokumenty

| Temat z notatki | Dokument w repo | Co trzeba zaktualizować przy realizacji |
|---|---|---|
| Pracownie / OU | `architektura.md` §6, `classrooms.json`, `model-danych.md` §1 | dodać warstwę „sala” i `topology.json` |
| Nowe byty (węzły, łącza, pliki, zakres konta) | `model-danych.md` | nowe sekcje DTO + semantyka braków |
| Nowe komunikaty | `api-websocket.md` §3, §7.1 | `topology.get`, `files.list`; dopisać do tabeli rozszerzeń v1 |
| Uprawnienia | `uprawnienia-i-dane-osobowe.md`, `adr/0007` | `topology.read`, `files.read`, jawna odmowa uczniów, audyt odmów |
| Styl UI | `ui-design-system.md` (nowy) | tokeny, kontrolki, checklista |
| Ikony | `adr/0009-ikony-svg.md` (nowy), `adr/0001` | status decyzji; ewentualna adnotacja o zależności NuGet |
| Plan i zadania | `roadmapa.md`, `zadania/` (nowy) | etapy M6–M8 + kryteria Given/When/Then |

---

## 6. Czego **nie** robimy w tym etapie

- Nie implementujemy funkcji — ten commit dodaje specyfikację i zadania, zgodnie z prośbą
  „add to my repository and create more tasks for developers”.
- Nie dodajemy `SvgIcons` do `*.csproj` bez weryfikacji (ZAD-02): paczka może nie istnieć, nie
  wspierać WPF albo nie przywrócić się w CI na `windows-latest`.
- Nie podbijamy `protocolVersion` i nie zmieniamy znaczenia istniejących pól.
- Nie dodajemy zapisu/plików do `/pepsi`, pobierania treści, ani dostępu dla uczniów.
- Nie wprowadzamy własnej bazy danych (ADR-0003) ani nowej roli „Uczeń” z uprawnieniami.
- Nie dodajemy cieni, gradientów, poświaty ani „kartowych” ozdobników do UI.
