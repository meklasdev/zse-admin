# ZAD-03 — Formularze dwukolumnowe, metadane w polu i strefa upuszczania

**Etap:** M6 · **Priorytet:** P1 · **Zależy od:** ZAD-01, ZAD-02
**Blokuje:** ZAD-08 (wygląd przeglądarki plików)
**Etykiety:** `enhancement`, `accessibility` · **Szacunek:** 2 dni
**Specyfikacja:** [`../ui-design-system.md`](../ui-design-system.md) §3.1–§3.2

## Kontekst

Style guide zamawiającego wymaga układu dwukolumnowego: po lewej opis i kontekst pola, po prawej
kontrolka, z poziomymi liniami podziału zamiast osobnych kart. Pola mają mieć czytelne
wypełnienie, opcjonalny prefiks/ikonę oraz dyskretne metadane (np. licznik znaków) **w obrębie
pola**. Wymieniono też przerywane obramowanie dla stref upuszczania plików. Dziś formularz akcji
M4 w prawym panelu `MainWindow.xaml` to zwykły stos `TextBox` + `Button`, bez etykiet
kontekstowych i bez licznika.

## Zakres

1. `src/ZseAdmin.Client/Styles/Forms.xaml` (nowy, wpięty do `App.xaml`):
   - `FormRow` — `Grid` o kolumnach `Size.FormLabel` / `*`, z 1px linią podziału na dole,
   - `FormLabel`, `FormHint`, `FormControl`, `FormValidation`, `CharCounter`,
   - `DropZone` — 1px obramowanie **przerywane** (`Stroke.Dash` = `4,3`), promień 8,
     `Brush.SurfaceMuted`; stan drag-over = `Brush.Accent` bez animacji poświaty,
   - `PrefixIcon` — miejsce na `IconKey` 16 px wewnątrz pola.
2. `src/ZseAdmin.Client/Controls/FormRow.xaml(.cs)` albo równoważny `DataTemplate`/styl
   (decyzja dewelopera; wymóg: brak logiki biznesowej w kontrolce, tylko układ i przekazanie
   `Label`, `Hint`, `Content`, `Validation`, `Counter`).
3. `MainWindow.xaml` — przerobienie panelu akcji M4 (`resetPassword`, `addToGroup`, pole `reason`)
   na wiersze `FormRow`: etykieta + opis po lewej, kontrolka po prawej; licznik znaków powodu
   (limit ustalić w konfiguracji/const, np. 200) w obrębie pola; komunikat walidacji pod polem.
4. `Styles/Controls.xaml` — rozszerzenie `TextBox` o wariant z prefiksem i licznikiem, bez
   zmiany zachowania `Binding` w `MainViewModel`.
5. `docs/ui-design-system.md` §3.1 — uzupełnić o przyjęty wariant implementacyjny.

## Poza zakresem

- Obsługa upuszczania plików i jakiejkolwiek operacji na `/pepsi` (ZAD-07, ZAD-08) — tutaj
  powstaje wyłącznie **styl** `DropZone` i pusty stan wizualny.
- Zmiana logiki akcji M4, protokołu `action.prepare` / `action.execute`, audytu (ADR-0008).
- Nowe okna ustawień; walidacja po stronie serwera.

## Kryteria akceptacji (Given / When / Then)

1. **Given** panel akcji M4, **when** otworzę go, **then** każdy wiersz ma po lewej etykietę i krótki
   opis, po prawej kontrolkę, a wiersze rozdzielają poziome linie 1px — bez osobnych kart i cieni.
2. **Given** pole „Powód”, **when** wpiszę 1 znak i 200 znaków, **then** licznik wewnątrz pola
   pokazuje bieżącą wartość i limit, a po przekroczeniu pojawia się komunikat tekstowy +
   1px obramowanie `Brush.StatusError` (bez zmiany wyłącznie koloru i bez poświaty).
3. **Given** okno o szerokości `1100` (minimum) przy 150% DPI, **when** wyświetlę formularz,
   **then** lewa i prawa kolumna nie nachodzą na siebie, opis zawija się, a kontrolka zachowuje
   min. `Size.RowMinHeight`.
4. **Given** styl `DropZone`, **when** przeciągnę plik nad strefę, **then** obramowanie jest
   przerywane 1px i zmienia `BorderBrush` na `Brush.Accent` bez żadnego cienia, rozmycia ani
   animacji „glow”.
5. **Given** klawiatura, **when** przechodzę `Tab` przez formularz, **then** kolejność odpowiada
   kolejności wizualnej, a fokus jest widoczny zgodnie z [`../ui-design-system.md`](../ui-design-system.md) §4.
6. **Given** `dotnet test`, **when** uruchomię istniejące testy M4, **then** przechodzą bez zmian
   (zadanie nie dotyka `Client.Core` ani serwera).

## Testy

- Regresja: `dotnet test` (M0–M4), `dotnet build -c Release` na `windows-latest`.
- Manualna: zrzuty formularza przy 100/125/150% DPI + stan błędu + stan drag-over, dołączone do PR.
- Opcjonalnie `tests/ZseAdmin.Tests/DisplayFormatTests.cs` — tylko jeśli licznik wymaga
  formatowania po stronie `Client.Core` (preferować XAML/`Binding`).

## Definicja ukończenia

- [ ] `Forms.xaml` jest jedynym miejscem definicji układu formularzy.
- [ ] Panel akcji M4 używa `FormRow`; żaden widok nie duplikuje układu dwukolumnowego „ręcznie”.
- [ ] Checklista [`../ui-design-system.md`](../ui-design-system.md) §5 odhaczona dla formularza.
- [ ] Zrzuty stanów w PR; CI zielone.

## Ryzyka i decyzje

- `UserControl` z `Content` w WPF łatwo przypadkiem doda tło/cień — trzymać się tokenów z ZAD-01.
- Limit znaków powodu musi być spójny z tym, co serwer przyjmuje w `ActionPrepareRequest.Reason`;
  jeśli serwer ma własny limit, **użyć tego samego**, a nie zgadywać w UI.
- `DropZone` bez zachowania może wyglądać na działającą — w v1 (przed ZAD-08) dodać jawny tekst
  „Tylko podgląd — zapis nie jest dostępny”, żeby UI nie obiecywało funkcji, której nie ma.
