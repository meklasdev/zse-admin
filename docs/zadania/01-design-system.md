# ZAD-01 — System projektowy UI: tokeny i refactor „zero glow”

**Etap:** M6 · **Priorytet:** P0 (fundament dla wszystkich widoków) · **Zależy od:** —
**Blokuje:** ZAD-02, ZAD-03, ZAD-05, ZAD-08, ZAD-10
**Etykiety:** `enhancement`, `documentation` · **Szacunek:** 2–3 dni
**Specyfikacja:** [`../ui-design-system.md`](../ui-design-system.md)

## Kontekst

Zamawiający wymaga: „całe UI ma być spójne, zero glow” oraz płaskiego, minimalistycznego stylu
z 1px obramowaniami ([`../notatki-zamawiajacego-2026-11-26.md`](../notatki-zamawiajacego-2026-11-26.md) F7).
Obecny klient ma style rozproszone w `App.xaml` i dziesiątki kolorów wpisanych wprost w
`MainWindow.xaml` (`#F3F4F6`, `#1F2937`, `#E5E7EB`, `Background="White"` itd.), w tym ciemny pasek
nagłówka, który kłóci się z lekkim, jasnym kierunkiem. Dopóki nie ma jednego zestawu tokenów,
każdy nowy widok (topologia, pliki) będzie wyglądał inaczej.

## Zakres

1. `src/ZseAdmin.Client/Styles/Theme.xaml` (nowy `ResourceDictionary`):
   - brushy z tabeli [`../ui-design-system.md`](../ui-design-system.md) §2.1,
   - `Thickness.Border1 = 1`, promienie `6 / 8 / 999`, odstępy `4/8/12/16/24/32`, `Size.FormLabel = 260`,
   - skala typografii `Text.Title/Section/Body/Label/Meta`, jeden `FontFamily` z fallbackiem
     `Segoe UI` (bundling fontu dopiero po decyzji **D9**).
2. `src/ZseAdmin.Client/Styles/Controls.xaml` (nowy): style niejawne dla `Button`, `TextBox`,
   `PasswordBox`, `ComboBox`, `ListBox`, `ListBoxItem`, `TabControl`/`TabItem`, `DataGrid`,
   `TextBlock` (`Text.*`) oraz style jawne `TagPill`, `SectionSeparator`, `DropZone`, `CardSurface`.
   Stany: `IsMouseOver`, `IsPressed`, `IsKeyboardFocusWithin`, `IsEnabled=false`, walidacja —
   wyłącznie zmiana koloru obramowania/tła, bez efektów.
3. `App.xaml`: `ResourceDictionary.MergedDictionaries` wskazuje `Theme.xaml` + `Controls.xaml`;
   usunąć stare style `SectionTitle`, `Hint`, `Button`, `TextBox` (lub zastąpić aliasami).
4. `MainWindow.xaml`: wszystkie kolory, `BorderThickness`, `CornerRadius` i `FontFamily` przez
   `StaticResource`; ciemny pasek nagłówka → `Brush.Surface` + dolna linia 1px `Brush.Border`;
   panele boczne → `Brush.SurfaceMuted`; karty → `Brush.Surface` z 1px `Brush.Border` i promieniem 8.
5. Usunięcie ewentualnych `DropShadowEffect` / `BlurEffect` / gradientów z całego XAML.
6. `docs/ui-design-system.md` — doprecyzować nazwy tokenów, jeśli implementacja wybierze inne.

## Poza zakresem

- Ikony i zależność `SvgIcons` (ZAD-02), formularze dwukolumnowe (ZAD-03), nowe widoki (ZAD-05,
  ZAD-08), kontrola w CI (ZAD-10 — ale ten PR nie może wprowadzać zakazanych efektów).
- Logika `MainViewModel`, protokół, serwer, uprawnienia, konfiguracja produkcyjna.
- Dołączanie fontów Inter / Plus Jakarta Sans przed decyzją **D9**.

## Kryteria akceptacji (Given / When / Then)

1. **Given** klient zbudowany, **when** przeszukam pliki `src/ZseAdmin.Client/**/*.xaml`,
   **then** nie znajdę żadnego literału koloru (`#...`, `White`, `Black`) poza `Styles/Theme.xaml`
   ani `DropShadowEffect`, `BlurEffect`, `*GradientBrush`.
2. **Given** okno główne, **when** zmienię wartość `Brush.Border` w `Theme.xaml` i zrestartuję klienta,
   **then** obramowania wszystkich paneli, pól, przycisków i separatów zmienią się spójnie bez
   edycji któregokolwiek widoku.
3. **Given** nagłówek aplikacji, **when** uruchomię klienta, **then** jest jasną powierzchnią z 1px
   linią podziału (bez ciemnego tła), a tekst tożsamości, świeżości i połączenia ma kontrast ≥ 4.5:1.
4. **Given** fokus klawiatury, **when** przechodzę `Tab` po przyciskach i polach, **then** aktywna
   kontrolka ma 1px obramowanie `Brush.Accent` i tło `Brush.AccentSubtle`, bez cienia i bez zmiany
   grubości krawędzi.
5. **Given** minimalne okno `1100 × 620` przy 100% / 125% / 150% DPI, **when** porównam zrzuty,
   **then** nie ma ucięć etykiet ani rozjechanych kolumn, a `dotnet build -c Release` na
   `windows-latest` jest zielony.
6. **Given** istniejący ekran logowań, **when** dane są `unknown` albo `partial`, **then** noty
   świeżości/kompletności pozostają widoczne i nie są oznaczone wyłącznie kolorem.

## Testy

- Brak testów jednostkowych dla XAML; weryfikacja: `dotnet build -c Release`, `dotnet test`
  (regresja logiki M0–M4), statyczny skan XAML (docelowo zautomatyzowany w ZAD-10) oraz zrzuty
  ekranu 100/125/150% DPI dołączone do PR.
- `tests/ZseAdmin.Tests` nie wymagają zmian — zadanie nie dotyka `Client.Core`.

## Definicja ukończenia

- [ ] `Theme.xaml` i `Controls.xaml` istnieją i są jedynym źródłem wartości wizualnych.
- [ ] Checklista [`../ui-design-system.md`](../ui-design-system.md) §5 odhaczona dla `MainWindow`.
- [ ] PR zawiera zrzuty „przed / po” dla 3 skal DPI.
- [ ] CI zielone; brak nowych paczek NuGet (ADR-0001 nienaruszony).
- [ ] Status w [`README.md`](README.md) uzupełniony.

## Ryzyka i decyzje

- **D9 (font):** bez zgody na bundling używamy `Segoe UI`; zmiana później to jedna linijka w
  `Theme.xaml`, o ile widoki nie mają własnych `FontFamily`.
- Styl niejawny (`TargetType` bez `x:Key`) może „przebić” style w kontrolkach zewnętrznych —
  unikać globalnych styli dla `Border`/`Grid`.
- Wysoki kontrast Windows: po zmianie tokenów sprawdzić, czy `Brush.AccentSubtle` nie zlewa się
  z `Brush.Surface` (fallback opisany w §4 specyfikacji).
