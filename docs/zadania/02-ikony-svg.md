# ZAD-02 — Ikony: weryfikacja `SvgIcons 0.1.10` i jedno źródło ikon

**Etap:** M6 · **Priorytet:** P0 · **Zależy od:** ZAD-01 · **Blokuje:** ZAD-03, ZAD-05, ZAD-08
**Etykiety:** `enhancement`, `documentation`, `help wanted` · **Szacunek:** 1–2 dni
**Decyzja:** [`../adr/0009-ikony-svg.md`](../adr/0009-ikony-svg.md) · **Styl:** [`../ui-design-system.md`](../ui-design-system.md) §3.3

## Kontekst

Zamawiający zaproponował: `dotnet add package SvgIcons --version 0.1.10`. ADR-0001 przyjął jednak
„WPF bez dodatkowych zależności NuGet”, a WPF nie renderuje SVG natywnie. W tym środowisku nie ma
dostępu do NuGet, więc **paczkę trzeba zweryfikować przed dodaniem** — inaczej `dotnet restore` w CI
(`windows-latest`) może przestać działać. ADR-0009 opisuje bramkę 7 sprawdzeń; to zadanie ją
wykonuje i domyka decyzję **D8**.

## Zakres

1. **Bramka weryfikacji** (na maszynie z dostępem do NuGet):
   ```bash
   dotnet add src/ZseAdmin.Client/ZseAdmin.Client.csproj package SvgIcons --version 0.1.10
   dotnet restore ZseAdmin.sln
   dotnet build ZseAdmin.sln -c Release
   ```
   Sprawdzić: istnienie wersji, TFM (`net8.0-windows`), sposób otrzymania geometrii/`Drawing`
   dla WPF, licencję, zależności tranzytywne, możliwość jednolitego koloru i rozmiaru, wynik CI.
   Wyniki punkt po punkcie dopisać do [`../adr/0009-ikony-svg.md`](../adr/0009-ikony-svg.md)
   i ustawić status `przyjęte` albo `odrzucone`.
2. **Wariant przyjęty:** referencja tylko w `ZseAdmin.Client.csproj`; adnotacja w
   [`../adr/0001-stack-dotnet.md`](../adr/0001-stack-dotnet.md) o jednej kontrolowanej zależności;
   wpis o restore w [`../uruchomienie.md`](../uruchomienie.md).
3. **Wariant odrzucony (fallback):** lokalne geometrie `Path` w `Styles/Icons.xaml`, źródłowe pliki
   SVG (jeśli użyte) w `src/ZseAdmin.Client/Assets/Icons/` z informacją o licencji; `*.csproj`
   pozostaje bez zmian.
4. **Wspólne dla obu wariantów:**
   - `IconKey` (enum) + `IconCatalog` w `ZseAdmin.Client.Core` — bez referencji do WPF, testowalne;
   - `src/ZseAdmin.Client/Styles/Icons.xaml`: mapowanie `IconKey` → zasób geometryczny, rozmiary
     16 / 20 / 24, kolor dziedziczony z `Brush.Text` / `Brush.Accent`, grubość linii 1–1.5;
   - minimalny zestaw: `Room`, `Classroom`, `Station`, `Switch`, `PatchPanel`, `Uplink`, `Folder`,
     `File`, `AdAccount`, `LocalAccount`, `Refresh`, `Warning`, `NoData`, `Action`, `Denied`;
   - podmiana ikon w `MainWindow.xaml` (odśwież, akcje, stany) na `IconKey`;
   - `AutomationProperties.Name` dla każdego przycisku z samą ikoną.

## Poza zakresem

- Zmiana protokołu, DTO, ról; rysowanie diagramu topologii (ZAD-05) i przeglądarki plików (ZAD-08) —
  te zadania tylko **konsumują** `IconKey`.
- Dodawanie innych paczek NuGet, konwerterów SVG w CI, zestawów bitmap.
- Zmiana ADR-0001 bez zapisanej zgody na zależność.

## Kryteria akceptacji (Given / When / Then)

1. **Given** dostęp do NuGet, **when** wykonam bramkę z ADR-0009, **then** w ADR-0009 są wpisane
   wyniki wszystkich 7 punktów z datą i wersją SDK oraz jawny status `przyjęte` / `odrzucone`.
2. **Given** paczka nie spełnia choć jednego warunku, **when** zbuduję rozwiązanie, **then**
   `ZseAdmin.Client.csproj` nie zawiera `SvgIcons`, a ikony pochodzą z `Styles/Icons.xaml`
   (`Path`) — CI zielone bez zmian w workflow.
3. **Given** przyjęta paczka, **when** uruchomię `dotnet restore` + `dotnet build -c Release` na
   `windows-latest`, **then** oba polecenia kończą się powodzeniem, a ADR-0001 ma adnotację.
4. **Given** dowolny `IconKey` z katalogu, **when** poproszę o zasób w 16/20/24 px, **then**
   dostaję jeden, monochromatyczny zasób o spójnej grubości linii, bez cienia, gradientu i poświaty.
5. **Given** przycisk tylko z ikoną, **when** włączę Narrator / sprawdzę drzewo automatyzacji,
   **then** ma niepustą `AutomationProperties.Name` opisującą akcję.
6. **Given** `ZseAdmin.Client.Core`, **when** zbuduję projekt, **then** nie ma referencji do
   `PresentationFramework`/WPF (ikony są danymi, nie kontrolkami).

## Testy

- `tests/ZseAdmin.Tests/IconCatalogTests.cs` (nowy): każdy `IconKey` ma mapowanie, brak duplikatów,
  katalog nie zależy od WPF, zestaw zawiera ikony wymagane w §Zakres.
- Regresja: `dotnet test` (M0–M4) zielone; `dotnet build -c Release` na Windows.
- Jeśli przyjęto paczkę: przebieg CI z `dotnet restore` dołączony do PR.

## Definicja ukończenia

- [ ] ADR-0009 ma status i wypełnioną sekcję „Do uzupełnienia”.
- [ ] Decyzja **D8** odnotowana w [`../notatki-zamawiajacego-2026-11-26.md`](../notatki-zamawiajacego-2026-11-26.md) §4.
- [ ] `IconKey` + `Icons.xaml` są jedynym źródłem ikon; widoki nie znają API paczki.
- [ ] Checklista [`../ui-design-system.md`](../ui-design-system.md) §3.3 spełniona; CI zielone.

## Ryzyka i decyzje

- **D8** jest jedyną decyzją blokującą. Jeśli paczka nie istnieje / nie wspiera WPF, nie eskaluj:
  fallback `Path` jest pełnoprawnym rozwiązaniem, nie prowizorką.
- Paczka z wbudowanymi kolorami/gradientami łamie „zero glow” — weryfikacja punktu 6 jest
  warunkiem przejścia, nie miłym dodatkiem.
- Przy aktualizacji .NET sprawdzać zgodność zależności tranzytywnych (wpisać do ADR-0009).
