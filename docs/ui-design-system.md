# System projektowy UI (ZSE-Admin)

**Status:** obowiązujący kierunek dla całego klienta WPF (źródło: notatki zamawiającego z 2026-11-26,
[`notatki-zamawiajacego-2026-11-26.md`](notatki-zamawiajacego-2026-11-26.md) §1).
**Zasada naczelna:** **całe UI jest spójne i ma zero glow**. Dokument jest specyfikacją; realizacja
to [`zadania/01-design-system.md`](zadania/01-design-system.md), a kontrola w CI to
[`zadania/10-ci-dokumentacja-dostepnosc.md`](zadania/10-ci-dokumentacja-dostepnosc.md).

Stosujemy go zarówno do istniejących ekranów (`MainWindow.xaml`), jak i do nowych widoków
(topologia, pliki). Wszystkie kolory, grubości, promienie i odstępy pochodzą z jednego zestawu
tokenów — **w widokach nie ma wartości „na sztywno”**.

---

## 1. Kierunek i ogólny charakter

- **Estetyka:** lekki, czysty minimalizm w duchu współczesnego SaaS z subtelnym neo-brutalizmem.
  Nacisk na strukturę, linie i przestrzeń, nie na ozdobniki.
- **Głębia:** płaska (*flat*) — budowana wyłącznie obramowaniami 1px, różnicą tła i odstępami.
  Bez rozmytych cieni, poświaty, gradientów i efektów „unoszenia się” elementów.
- **Oddech:** dużo wolnej przestrzeni, czytelne marginesy, przewidywalny rytm pionowy i poziomy
  (siatka 8 px). Sekcje dzielą poziome linie 1px, a nie stos ciężkich kart.
- **Spójność:** ten sam przycisk, pole, etykieta, ikona i komunikat wyglądają identycznie w każdym
  widoku. Wyjątki wymagają wpisu w tym dokumencie.

### 1.1 Twarde reguły „zero glow”

Zakazane w całym XAML i w kodzie rysującym:

| Zakaz | Dlaczego |
|---|---|
| `DropShadowEffect`, `BlurEffect`, `DropShadowBitmapEffect`, `OuterGlowBitmapEffect` | poświata i rozmyte cienie łamią płaskość |
| `LinearGradientBrush`, `RadialGradientBrush` jako tło kontrolek | udają wypukłość i głębię |
| Animacje cienia / skali „podnoszące” kartę, neonowe kolory | dekoracja zamiast struktury |
| Przezroczyste nakładki „dla efektu” | rozmywają hierarchię |
| Kolory wpisane literalnie w widokach (`#E5E7EB`, `White`, …) | brak jednego źródła prawdy → niespójność |

Dozwolone: `SolidColorBrush`, `Border` 1px, `Rectangle` 1px jako linia podziału, `StrokeDashArray`
dla stref upuszczania, zmiana `BorderBrush`/`Background` dla stanu aktywnego i fokusu.

---

## 2. Tokeny

Wartości docelowe dla `Styles/Theme.xaml` (ZAD-01). Nazwy są umowne; ważne, żeby widoki używały
wyłącznie `StaticResource`.

### 2.1 Kolory

| Token | Wartość | Zastosowanie |
|---|---|---|
| `Brush.AppBackground` | `#F7F8FA` | tło główne aplikacji |
| `Brush.Surface` | `#FFFFFF` | powierzchnie treści, pola formularzy |
| `Brush.SurfaceMuted` | `#F1F3F5` | nawigacja, panele pomocnicze, nagłówek tabeli |
| `Brush.Border` | `#DDE1E6` | standardowe obramowanie 1px, linie podziału |
| `Brush.BorderStrong` | `#B8BEC7` | hover, separatory wymagające kontrastu |
| `Brush.Text` | `#111827` | tekst główny, tytuły |
| `Brush.TextMuted` | `#5B6472` | opisy, liczniki, metadane |
| `Brush.Accent` | `#1D4ED8` | fokus, stan aktywny, przycisk pierwotny |
| `Brush.AccentSubtle` | `#EEF2FF` | wyróżnienie wyboru (tło, bez poświaty) |
| `Brush.OnAccent` | `#FFFFFF` | tekst na akcencie |
| `Brush.StatusOk` | `#15803D` | `online` — tylko tekst/obramowanie |
| `Brush.StatusWarn` | `#B45309` | `isStale`, `completeness = partial` |
| `Brush.StatusError` | `#B91C1C` | błąd, `upstreamUnavailable`, `denied` |
| `Brush.StatusInfo` | `#1D4ED8` | informacja, `unknown` |
| `Brush.Disabled` | `#9AA1AC` | kontrolki nieaktywne |

Kolory statusów **nie mogą być jedynym nośnikiem informacji** — zawsze towarzyszy im tekst
(„brak danych”, „dane z 10:42”, „częściowe”). Utrzymujemy semantykę z
[`model-danych.md`](model-danych.md) §4: `unknown` ≠ `offline`, `unavailable` ≠ „brak logowań”.

### 2.2 Geometria i odstępy

| Token | Wartość | Zastosowanie |
|---|---|---|
| `Thickness.Border1` | `1` | wszystkie obramowania i linie podziału |
| `Radius.Control` | `6` | pola, przyciski, powierzchnie standardowe |
| `Radius.Surface` | `8` | większe panele |
| `Radius.Pill` | `999` | liczniki, znaczniki, wybrane akcje |
| `Stroke.Dash` | `4,3` | wyłącznie przerywane strefy upuszczania plików |
| `Space.1` … `Space.6` | `4, 8, 12, 16, 24, 32` | paddingi i marginesy (siatka 8 px) |
| `Size.FormLabel` | `260` | lewa kolumna formularza |
| `Size.RowMinHeight` | `40` | minimalna wysokość wiersza / celu kliknięcia |

Warstwy: `AppBackground` → `SurfaceMuted` (nawigacja) → `Surface` (treść). Aktywność i fokus
oznaczamy zmianą `BorderBrush` na `Accent` + lekkim `AccentSubtle` w tle — **bez pogrubiania
obramowania i bez cienia**, żeby siatka 1px pozostała jednolita.

### 2.3 Typografia

| Poziom | Rozmiar | Waga | Użycie |
|---|---|---|---|
| `Text.Title` | 20 | Bold | tytuł widoku |
| `Text.Section` | 14 | SemiBold | nagłówek grupy ustawień |
| `Text.Body` | 13 | Regular | treść, wartości pól, wiersze tabel |
| `Text.Label` | 12 | Medium | etykiety pól |
| `Text.Meta` | 11 | Regular | opisy, liczniki, `freshness`, noty |

- Krój: nowoczesny sans-serif — preferowane **Inter** / **Plus Jakarta Sans**, fallback natywny
  **Segoe UI**. **D9 = A (podjęte 2026-11-26):** obowiązuje natywny `FontFamily` systemowy w jednym
  miejscu (`Theme.xaml`); nie dołączamy plików Inter / Plus Jakarta Sans (brak decyzji licencyjnej).
- Hierarchię budujemy rozmiarem i wagą, nie kolorem ani kursywą. Interlinia i odstępy z siatki 8 px.
- Tekst nie ma cienia, obrysu ani efektu „glow”.

---

## 3. Kompozycja i kontrolki

### 3.1 Układ formularzy (dwukolumnowy)

Każdy wiersz to `Grid` z dwiema kolumnami: **lewa** (etykieta + krótki opis/kontekst, stałe
`Size.FormLabel`) i **prawa** (kontrolka). Wiersze rozdziela pozioma linia 1px (`Brush.Border`),
a nie osobne karty. Grupa wierszy ma nagłówek `Text.Section`.

```xaml
<!-- Szkic docelowy (ZAD-03) — nie jest to jeszcze kod produkcyjny -->
<Grid>
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="260" />
        <ColumnDefinition Width="*" />
    </Grid.ColumnDefinitions>
    <StackPanel Grid.Column="0">
        <TextBlock Text="Powód operacji" Style="{StaticResource Text.Label}" />
        <TextBlock Text="Trafia do audytu. Bez danych osobowych."
                   Style="{StaticResource Text.Meta}" />
    </StackPanel>
    <Border Grid.Column="1" BorderBrush="{StaticResource Brush.Border}"
            BorderThickness="1" CornerRadius="6" Background="{StaticResource Brush.Surface}">
        <!-- TextBox + dyskretny licznik znaków w obrębie pola -->
    </Border>
</Grid>
```

Zasady: metadane (licznik znaków, prefiks/ikona, jednostka) są **wewnątrz** pola; opis i walidacja
są tekstem przy polu, nigdy samym kolorem; błąd to 1px obramowanie `StatusError` + komunikat
pod kontrolką. Formularze nie „pływają” — brak cieni i podniesionych kart.

### 3.2 Kontrolki wejściowe

| Kontrolka | Specyfikacja |
|---|---|
| `TextBox` / `PasswordBox` / `ComboBox` | tło `Surface`, 1px `Border`, promień 6, padding 8–10, `Text.Body`; focus → `BorderBrush = Accent`; disabled → `Disabled` + tekst `TextMuted`. Prefiks/ikona 16 px po lewej wewnątrz pola. |
| Wieloliniowy `TextBox` | jw., `AcceptsReturn`, `TextWrapping`, licznik znaków w prawym dolnym rogu pola. |
| `Button` pierwotny | wypełnienie `Accent`, tekst `OnAccent`, promień 6, bez cienia; hover = ciemniejszy akcent, pressed = `AccentSubtle` + 1px `Accent`. |
| `Button` wtórny / niebezpieczny | `Surface` + 1px `Border` (niebezpieczny: `StatusError` jako tekst i obramowanie), bez wypełnienia świetlistego. |
| `Tag` / licznik | pill (`Radius.Pill`), 1px `Border`, `Text.Meta`. |
| Zakładki | aktywna = 1px podkreślenie `Accent` + `Text` pogrubiony; brak „wypukłych” kart zakładek. |
| Listy / `DataGrid` | tylko poziome linie 1px `Border`, nagłówek `SurfaceMuted`, brak pionowych kratek, brak naprzemiennych cieni; wiersz aktywny = `AccentSubtle` + 1px `Accent`. |
| Strefa upuszczania plików | 1px obramowanie **przerywane** (`Stroke.Dash`), promień 8, `SurfaceMuted`; drag-over zmienia `BorderBrush` na `Accent` (bez animacji poświaty). |
| Separator sekcji | `Rectangle`/`Border` wysokości 1, `Brush.Border`, bez marginesów „dla efektu”. |

### 3.3 Ikony

- Jedno źródło: `IconKey` + mapowanie w `Client.Core` (logika bez WPF) i `Styles/Icons.xaml`
  po stronie klienta. Rozmiary 16 / 20 / 24 px, linia 1–1.5 px, kolor dziedziczony z `Text`/`Accent`,
  **bez cieni i gradientów**.
- **D8 = B (podjęte):** `dotnet add package SvgIcons --version 0.1.10` ma trafić do klienta w
  ZAD-02. Bramka weryfikacji (`restore` + CI) pozostaje warunkiem; przy porażce fallback `Path`.
  Decyzja i alternatywy w [`adr/0009-ikony-svg.md`](adr/0009-ikony-svg.md),
  realizacja w [`zadania/02-ikony-svg.md`](zadania/02-ikony-svg.md).
- Minimalny zestaw: sala, pracownia, stacja, switch, patch panel, uplink, folder, plik,
  konto AD, konto lokalne, odśwież, ostrzeżenie, brak danych, akcje M4.
- Przycisk z samą ikoną musi mieć `AutomationProperties.Name`.

### 3.4 Stany danych (nie zmieniamy semantyki M0–M4)

| Sytuacja | Reprezentacja w UI |
|---|---|
| `status = unknown`, brak `statusObservedAtUtc` | `StatusInfo` + tekst „brak danych”, nigdy „offline” |
| `completeness = partial` | `StatusWarn` + pełna nota z liczbą źródeł/stacji |
| `completeness = unavailable` / `upstreamUnavailable` | `StatusError` + nazwa źródła, nie pusta lista |
| `isStale = true` | `StatusWarn` + „dane z <czas>” w pasku świeżości |
| `forbidden` / odmowa połączenia | ekran „Brak dostępu”, nie puste tabele |
| zerwane połączenie WS | jawny stan „ponowne łączenie”, dane oznaczone jako nieaktualne |

---

## 4. Dostępność i responsywność

- Kontrast tekstu z tłem ≥ 4.5:1 (dla `Text.Meta` sprawdzić na `SurfaceMuted`), wielkość bazowa 13.
- Fokus klawiatury widoczny na każdej kontrolce: 1px `Accent` + `AccentSubtle`; kolejność `Tab`
  zgodna z kolejnością wizualną; brak „focusu tylko kolorem” (dodatkowo podkreślenie/grubość).
- Statusy zawsze opisane tekstem; ikony z `AutomationProperties.Name`; elementy klikalne ≥ 32 px
  wysokości (`Size.RowMinHeight`).
- Sprawdzenia przy 100% / 125% / 150% DPI, przy minimalnym oknie `1100 × 620` oraz w trybie
  wysokiego kontrastu Windows (tokeny nie mogą zniknąć).
- Brak animacji poświaty; dopuszczalne wyłącznie krótkie, dyskretne zmiany koloru stanu.

---

## 5. Definicja ukończenia (checklista dla każdego widoku)

- [ ] Wszystkie kolory, promienie, grubości i fonty przez `StaticResource` z `Theme.xaml`.
- [ ] Brak `DropShadowEffect`, `BlurEffect`, gradientów i dosłownych kolorów w XAML widoku
  (kontrola CI — ZAD-10).
- [ ] Obramowania 1px, promienie z tokenów, separacja sekcji poziomą linią, nie kartami.
- [ ] Formularz dwukolumnowy z opisem po lewej i kontrolką po prawej; metadane w obrębie pola.
- [ ] Stany: hover / focus / active / disabled / error / empty / forbidden / stale — bez glow.
- [ ] Ikony z jednego źródła, z nazwą dostępności, w jednym rozmiarze z zestawu 16/20/24.
- [ ] Noty `freshness` i `completeness` widoczne; `unknown` nie jest opisane jako `offline`.
- [ ] Zrzut ekranu przy 100% / 125% / 150% DPI dołączony do PR; brak ucięć.
- [ ] `dotnet build -c Release` i `dotnet test` zielone na `windows-latest`.

---

## 6. Czego ten dokument nie zmienia

Nie zmienia protokołu WebSocket, ról, uprawnień, modelu danych ani logiki serwera. Nowe widoki
(topologia, pliki) muszą korzystać z istniejących mechanizmów świeżości, audytu i `CapabilityGuard`.
**D8 = B:** dodanie jednej zależności NuGet jest zatwierdzone; ADR-0001 ma adnotację, a ADR-0009
status „przyjęte warunkowo” (domknięcie po zielonym CI).
