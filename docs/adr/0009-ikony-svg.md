# ADR-0009: Ikony SVG w kliencie WPF — bramka weryfikacji dla `SvgIcons 0.1.10`

**Status:** przyjęte warunkowo — decyzja **D8 = B** z 2026-11-26; domknięcie po zielonym `dotnet restore` + buildzie w CI (ZAD-02)
**Data:** 2026-11-26
**Źródło:** [`notatki-zamawiajacego-2026-11-26.md`](../notatki-zamawiajacego-2026-11-26.md) F6, decyzja **D8**
**Powiązane:** [`../ui-design-system.md`](../ui-design-system.md) §3.3, [`../zadania/02-ikony-svg.md`](../zadania/02-ikony-svg.md), [ADR-0001](0001-stack-dotnet.md)

## Kontekst

Zamawiający wskazał kandydata na źródło ikon:

```bash
dotnet add package SvgIcons --version 0.1.10
```

Potrzebujemy spójnego, monochromatycznego zestawu ikon dla widoków: sale, pracownie, stacje,
switch, patch panel, uplink, foldery, pliki, konta AD i konta lokalne, akcje, stany danych.
System projektowy ([`../ui-design-system.md`](../ui-design-system.md)) wymaga ikon wektorowych
w rozmiarach 16/20/24 px, bez cieni, gradientów i poświaty.

Dwa fakty utrudniają bezpośrednie dodanie paczki:

1. **ADR-0001 przyjął „WPF bez dodatkowych zależności NuGet”** jako jedną z konsekwencji wyboru
   stosu. Dodanie zewnętrznej paczki jest zmianą tej decyzji i wymaga jawnej adnotacji.
2. **WPF nie renderuje natywnie SVG.** `Image`/`DrawingImage` operują na bitmapach, `Path` na
   geometriach XAML. Paczka musi więc albo dostarczać geometrie/`Drawing`, albo konwertować SVG,
   albo rysować przez własną kontrolkę. Samo „ma ikony SVG” nie gwarantuje działania w WPF.

W środowisku, w którym powstaje ten ADR, nie ma dostępu do NuGet, więc **nie można potwierdzić
istnienia, zgodności TFM ani licencji paczki**. Dodanie referencji „na wiarę” zepsułoby
`dotnet restore` w CI (`windows-latest`, [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml)).

## Decyzja

**D8 = B (podjęte przez zamawiającego 2026-11-26): dodajemy `SvgIcons 0.1.10` do klienta WPF.**
W repozytorium nie ma jeszcze referencji — ten etap to dokumentacja. ZAD-02 wykonuje
`dotnet add src/ZseAdmin.Client/ZseAdmin.Client.csproj package SvgIcons --version 0.1.10`.

Bramka weryfikacji **pozostaje obowiązkowym krokiem** (chroni CI, nie zmienia decyzji):

| # | Sprawdzenie | Warunek przejścia |
|---|---|---|
| 1 | Pakiet `SvgIcons` w wersji `0.1.10` istnieje na NuGet | `dotnet add package SvgIcons --version 0.1.10` kończy się powodzeniem |
| 2 | Zgodność z `net8.0-windows` / WPF | paczka deklaruje kompatybilny TFM i nie wymaga innego UI frameworku |
| 3 | Sposób renderowania w WPF | dostępne `Geometry`/`Drawing`/kontrolka możliwa do użycia w `StaticResource` i `Style` |
| 4 | Licencja i pochodzenie | licencja akceptowalna dla projektu szkolnego, wpisana do ADR; brak wymogu klucza/online |
| 5 | Restore w CI | `dotnet restore` + `dotnet build -c Release` zielone na `windows-latest` bez zmian w workflow |
| 6 | Styl | ikony dają się ustawić w jednym kolorze, 1–1.5 px, bez wbudowanych cieni/gradientów |
| 7 | Utrzymanie | rozmiar paczki, zależności tranzytywne, aktywność projektu — wpisane do „Konsekwencji” |

Wynik:

- **Bramka przechodzi** → status *przyjęte*, paczka wyłącznie w `ZseAdmin.Client.csproj`, adnotacja
  w ADR-0001 o jednej kontrolowanej zależności, `Styles/Icons.xaml` mapuje `IconKey` na zasoby paczki.
- **Bramka nie przechodzi** → status *odrzucone z powodem*, paczki nie ma w repozytorium, ikony są
  lokalnymi geometriami `Path` w `Styles/Icons.xaml`; ADR-0001 bez zmian. **Nie wymaga to ponownej
  decyzji zamawiającego** — fallback jest wprost dopuszczony w D8 = B w celu utrzymania zielonego CI.

W obu wariantach obowiązuje jedno źródło prawdy: `IconKey` w `ZseAdmin.Client.Core` (bez WPF)
i jedno mapowanie w kliencie. Widoki nie znają nazw plików ani API paczki.

## Alternatywy

| Alternatywa | Dlaczego odrzucona / odłożona |
|---|---|
| Dodać `SvgIcons 0.1.10` bez weryfikacji w CI | **odrzucone:** niezweryfikowana zależność może nie istnieć, nie wspierać WPF i zepsuć `dotnet restore`; decyzja D8 = B zachowuje bramkę |
| Lokalne `Path`/`Geometry` w `ResourceDictionary` | **przyjęte jako plan B** (D8 = B + bramka): wymaga konwersji SVG → XAML, ale daje zero zależności, pełną kontrolę nad stylem i zielone CI |
| Krok builda konwertujący SVG → XAML | dobry kompromis przy większej liczbie ikon; dodaje narzędzie do CI — rozważyć, gdy zestaw urośnie |
| `Segoe MDL2 Assets` / Segoe Fluent Icons | natywne, ale zależne od wersji Windows i ubogie w ikony sieciowe (switch, patch panel) |
| Bitmapy PNG z zestawu zewnętrznego | rozmycie przy 125/150% DPI, brak kontroli koloru, łamie zasadę wektorowości i „zero glow” |

## Konsekwencje

**Pozytywne:** brak ryzyka dla CI; jedno miejsce decyzji o ikonach; styl (rozmiar, kolor, brak efektów)
jest egzekwowany niezależnie od dostawcy ikon; logika `IconKey` pozostaje poza WPF i jest testowalna.

**Negatywne:** w wariancie fallback trzeba utrzymywać własny, niewielki zestaw geometrii; przyjęcie
paczki oznacza jedną zależność tranzytywną do audytowania przy aktualizacjach .NET.

**Neutralne:** `protocolVersion`, DTO i uprawnienia nie zmieniają się — ikony są wyłącznie warstwą
prezentacji. Minimalny zestaw ikon i wymóg `AutomationProperties.Name` opisuje
[`../ui-design-system.md`](../ui-design-system.md) §3.3–§4.

## Do uzupełnienia po weryfikacji (ZAD-02)

- **Decyzja D8 = B jest już wpisana** (odpowiedź zamawiającego, 2026-11-26) — nie pytamy ponownie.
- Wyniki punktów 1–7 z datą i wersją SDK.
- Status końcowy: `przyjęte` / `odrzucone z powodem`, nazwa wariantu (`SvgIcons` albo `Path`).
- Jeśli przyjęte: adnotacja w ADR-0001 + wpis do [`../uruchomienie.md`](../uruchomienie.md) o restore.
