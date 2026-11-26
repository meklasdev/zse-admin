# ADR-0009: Ikony SVG w kliencie WPF — bramka weryfikacji dla `SvgIcons 0.1.10`

**Status:** propozycja (do zatwierdzenia w ramach ZAD-02)
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

**Nie dodajemy paczki do `*.csproj`, dopóki nie przejdzie bramki weryfikacji (ZAD-02).**

Bramka sprawdza i zapisuje wynik w tym ADR:

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

- **Bramka przechodzi** → ADR otrzymuje status *przyjęte*, paczka trafia wyłącznie do
  `src/ZseAdmin.Client/ZseAdmin.Client.csproj`, w ADR-0001 dopisujemy adnotację o jednej
  kontrolowanej zależności, a `Styles/Icons.xaml` mapuje `IconKey` na zasoby paczki.
- **Bramka nie przechodzi** → status *odrzucone*, paczki nie ma w repozytorium; ikony są lokalnymi
  geometriami `Path` w `Styles/Icons.xaml` (źródło SVG → XAML w repo, bez zależności). ADR-0001
  pozostaje bez zmian.

W obu wariantach obowiązuje jedno źródło prawdy: `IconKey` w `ZseAdmin.Client.Core` (bez WPF)
i jedno mapowanie w kliencie. Widoki nie znają nazw plików ani API paczki.

## Alternatywy

| Alternatywa | Dlaczego odrzucona / odłożona |
|---|---|
| Dodać `SvgIcons 0.1.10` od razu | niezweryfikowana zależność może nie istnieć, nie wspierać WPF i zepsuć CI; narusza ADR-0001 bez decyzji |
| Lokalne `Path`/`Geometry` w `ResourceDictionary` (fallback) | przyjęte jako wariant bezpieczny; wymaga ręcznej konwersji SVG → XAML, ale zero zależności i pełna kontrola nad stylem |
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

- Wyniki punktów 1–7 z datą i wersją SDK.
- Status: `przyjęte` / `odrzucone`, nazwa wariantu (`SvgIcons` albo `Path`).
- Jeśli przyjęte: adnotacja w ADR-0001 + wpis do [`../uruchomienie.md`](../uruchomienie.md) o restore.
