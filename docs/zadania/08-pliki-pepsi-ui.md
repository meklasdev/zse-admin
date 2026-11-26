# ZAD-08 — Przeglądarka `/pepsi` w UI: drzewo, lista, strefa upuszczania

**Etap:** M8 · **Priorytet:** P2 · **Zależy od:** ZAD-01, ZAD-03, ZAD-07
**Etykiety:** `enhancement`, `accessibility` · **Szacunek:** 2–3 dni
**Styl:** [`../ui-design-system.md`](../ui-design-system.md) §3.1–§3.2, §3.4
**Decyzje:** D4 = pliki widzą wszystkie role aplikacji; D6 = brak danych o plikach ucznia

## Kontekst

Backend ZAD-07 udostępnia tylko metadane plików z `/pepsi`. Potrzebny jest widok zgodny ze stylem
zamawiającego: drzewo folderów + lista plików z 1px separatorami, liczniki w pillach,
**przerywana strefa upuszczania plików** i jawne stany `forbidden` / `unavailable` / `partial`.
W v1 nie ma zapisu: upuszczenie pliku może co najwyżej pokazać jego metadane albo komunikat,
że zapis nie jest dostępny — UI nie może obiecać funkcji, której serwer nie ma (zasada z
[`../notatki-zamawiajacego-2026-11-26.md`](../notatki-zamawiajacego-2026-11-26.md) §3.5).

## Zakres

1. `src/ZseAdmin.Client.Core/ViewModels/FileBrowserViewModel.cs` (nowy): `files.list` przez
   `ZseAdminApiClient`, breadcrumb ze ścieżki względnej, wybór folderu, sortowanie (foldery
   pierwsze, potem nazwa), licznik wpisów, stany: ładowanie / pusto / `forbidden` /
   `upstreamUnavailable` / `partial` / wyłączone źródło.
2. `src/ZseAdmin.Client/Views/FileBrowserView.xaml` (nowy):
   - lewa kolumna `SurfaceMuted`: drzewo folderów (1px separatory, ikony `IconKey.Folder`),
   - prawa kolumna: lista plików z `name`, `size`, `modifiedAtUtc`, `extension`, nagłówek
     `SurfaceMuted`, tylko poziome linie 1px, pill z liczbą wpisów,
   - breadcrumb + nota `freshness` (źródło, czas obserwacji, `completeness`),
   - `DropZone` z ZAD-03: przerywane 1px obramowanie, tekst
     „Tylko podgląd — zapis plików nie jest dostępny”, obsługa `DragOver`/`Drop` bez zapisu,
   - ekran „Brak dostępu” dla `forbidden` (ZAD-06), „Źródło niedostępne” dla
     `upstreamUnavailable` — z nazwą źródła i przyczyną, nigdy pusta lista.
3. `MainViewModel` / `MainWindow.xaml`: zakładka „Pliki” widoczna, gdy `welcome.capabilities`
   zawiera `files.read` (**D4:** wszystkie trzy role aplikacji, więc widzi ją każdy zalogowany).
   Ukrycie w UI **nie jest** zabezpieczeniem — serwer i tak odmawia uczniom (ZAD-06/07).
4. Wyłącznie tokeny z ZAD-01 i `IconKey` z ZAD-02; żadnych nowych kolorów, cieni, gradientów.

## Poza zakresem

- Pobieranie treści, podgląd dokumentów/zdjęć, upload, usuwanie, drag & drop zapisujący na dysk,
  wyszukiwarka, sortowanie po stronie serwera.
- Wskazywanie, do którego ucznia należy plik (**D6** = uczeń nie ma dostępu do aplikacji),
  mapowanie plików na konta, dane osobowe w tooltipach.
- Zmiana protokołu, ról, modelu danych.

## Kryteria akceptacji (Given / When / Then)

1. **Given** `files.list` zwraca 3 foldery i 12 plików, **when** otworzę zakładkę „Pliki”,
   **then** widzę drzewo, listę z rozmiarem i datą, breadcrumb, pill z liczbą 15 oraz notę
   `freshness` z czasem obserwacji.
2. **Given** kliknięcie folderu w drzewie, **when** wyślę `files.list` z jego ścieżką względną,
   **then** breadcrumb i lista aktualizują się, a ścieżka fizyczna serwera nigdzie nie jest
   widoczna.
3. **Given** `error: forbidden`, **when** ładuję widok, **then** pokazuję ekran „Brak dostępu do
   plików”, bez pustej tabeli i bez przycisków sugerujących akcje.
4. **Given** `completeness = partial` albo `unavailable`, **when** renderuję listę, **then** pełna
   nota z limitem / powodem jest widoczna nad listą, a lista nie jest opisana jako „kompletna”.
5. **Given** upuszczę plik na przerywaną strefę, **when** obsłużę `Drop`, **then** nie wykonuję
   żadnego zapisu, pokazuję metadane (jeśli ścieżka leży w `Root`) albo komunikat
   „Plik spoza /pepsi — brak zapisu”, a obramowanie zmienia kontrast bez cienia i animacji glow.
6. **Given** 125% i 150% DPI oraz minimalne okno, **when** wyświetlę długie nazwy plików,
   **then** są skracane z `TextTrimming` i pełna nazwa jest dostępna w `ToolTip` /
   `AutomationProperties.Name`, bez rozjechania kolumn.
7. **Given** konto bez `files.read` (uczeń odrzucony przez ZAD-06 albo starszy serwer), **when**
   otworzę klienta, **then** zakładka „Pliki” nie jest widoczna, a ręczne wysłanie `files.list`
   i tak kończy się `forbidden`; **given** Nauczyciel/ReadOnly, **then** zakładka jest widoczna (D4).

## Testy

- `tests/ZseAdmin.Tests/FileBrowserViewModelTests.cs` (nowy, bez WPF): breadcrumb, sortowanie,
  stany błędów, licznik, brak pełnych ścieżek w modelu widoku.
- Regresja: `dotnet test`, `dotnet build -c Release`.
- Manualna: zrzuty 100/125/150% DPI, stany `forbidden`/`partial`/`unavailable`, drag-over.

## Definicja ukończenia

- [ ] Checklista [`../ui-design-system.md`](../ui-design-system.md) §5 odhaczona dla widoku plików.
- [ ] `DropZone` pochodzi z ZAD-03 (bez duplikowania stylu); ikony z `IconKey`.
- [ ] Żaden komunikat nie sugeruje zapisu ani „przenoszenia plików między stacjami”.
- [ ] Decyzje **D4**, **D6** zapisane w notatkach §4; status w [`README.md`](README.md).

## Ryzyka i decyzje

- **D6 (podjęte):** uczeń nie ma dostępu do aplikacji, więc UI nie pokazuje żadnych danych
  o plikach ucznia ani nie łączy plików z kontami — nawet w tooltipie. Zapis na udziale jest
  wydzielony (D4), dlatego `DropZone` nadal mówi wprost „Tylko podgląd”.
- `ToolTip` z pełną nazwą to wciąż dane z serwera: nie dodawać ścieżek bezwzględnych ani treści.
- `DragOver`/`Drop` w WPF lubi domalować domyślny efekt — jawnie ustawić `DragDropEffects.None`
  i własny, 1px stan obramowania.
- Długie listy: paginacja po `take` z ZAD-07; UI ma pokazać, że lista jest ucięta, zanim użytkownik
  uzna ją za pełną.
