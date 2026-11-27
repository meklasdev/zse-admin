# ZAD-11 — Automatyczne zrzuty UI z realnego klienta WPF w GitHub Actions

**Etap:** M6 (domknięcie ZAD-01) · **Priorytet:** P1 · **Zależy od:** ZAD-01, ZAD-06
**Blokuje:** domknięcie checklisty `ui-design-system.md` §5 (zrzuty 100/125/150% DPI)
**Etykiety:** `enhancement`, `documentation`, `ci` · **Szacunek:** 1 dzień
**Status:** **zrobione** (PR w tym zadaniu, 2026-11-27)

## Kontekst

ZAD-01 wymaga zrzutów „przed / po” przy 100%, 125% i 150% DPI, ale środowisko agenta nie ma
pulpitu Windows ani .NET SDK, więc jedyny otwarty punkt zadania to właśnie zrzuty. Ręczne
klikanie po trzech skalach na stacji Windows jest powtarzalne tylko w teorii: zapomina się
o stanie „brak dostępu”, o zakładce akcji i o świeżości danych.

Rozwiązanie: **osobny job CI na `windows-latest`**, który uruchamia serwer z danymi przykładowymi,
łączy się z nim prawdziwym klientem (`MainWindow.xaml`, `MainViewModel`, `WsClientConnection`)
i renderuje widok do PNG — bez makiety HTML i bez Selenium.

### Dlaczego render off-screen, a nie `Show()`

GitHub-hosted runnerzy działają w sesji 0 (brak interaktywnego pulpitu). `RenderTargetBitmap`
nie potrzebuje HWND: mierzy i układa to samo drzewo wizualne, a potem rasteryzuje je w zadanym
DPI. Render przy 120/144 DPI odpowiada skalowaniu systemowemu 125%/150% — układ pozostaje
w DIP, rośnie wyłącznie gęstość pikseli. To dokładnie ta klasa problemu, którą chcemy łapać
(ucięcia etykiet, rozjechane kolumny).

## Zakres

1. **`tools/ZseAdmin.Screenshots`** (nowy projekt `net8.0-windows`, `UseWPF`, konsolowy `Exe`):
   - `Program.cs` — ładuje `Styles/Theme.xaml` + `Styles/Controls.xaml` z biblioteki klienta przez
     `pack://application:,,,/ZseAdmin.Client;component/...` i rejestruje `BoolToVisibility`
     (MainWindow wiąże nim widoczność), startuje `MainViewModel` na `ws://127.0.0.1:5080/ws`
     z tokenem deweloperskim, czeka na `welcome` + pracownie + komputery + `FreshnessText`
     zaczynający się od „Dane”, potem renderuje 8 plików: `main-{100,125,150}.png` oraz
     `akcje-i-audyt-{100,125,150}.png`.
   - `UiSnapshot.cs` — `RenderTargetBitmap` + `PngBitmapEncoder`, walidacja wymiarów oraz
     wyszukiwanie `TabControl` w drzewie wizualnym.
   - `ScreenshotOptions.cs` — `--server`, `--token`, `--out`, `--wait`, `--help`; kody wyjścia
     `0/2/3/4/5` (sukces / brak danych / błąd / brak dostępu / pusty zrzut).
2. **`.github/workflows/ci.yml`** — job `screenshots` (`needs: build-and-test`,
   `if: pull_request || workflow_dispatch`): build, serwer w tle (`ASPNETCORE_ENVIRONMENT=Development`,
   `UseMockData=true`, `/healthz` jako gotowość), zrzuty, log serwera przy błędzie,
   `actions/upload-artifact@v4` (`ui-screenshots`), podsumowanie w runie i komentarz w PR
   z linkiem do artefaktu (`continue-on-error`).
3. **`ZseAdmin.sln`** — projekt w solution (build w CI), `.gitignore` — `screenshots/`.
4. **Dokumentacja** — ten plik, `README.md`, `docs/roadmapa.md`, `docs/zadania/README.md`,
   `docs/uruchomienie.md` (uruchomienie lokalne), `docs/ui-design-system.md` §5.1 (DPI odhaczone,
   link do artefaktu).

## Poza zakresem

- Zrzuty stanu „Brak dostępu” i „serwer niedostępny”: wymagałyby tokenu z rolą uczniowską albo
  zatrzymania serwera w trakcie — D10 zabrania tworzenia roli „Uczeń”, a sztuczne stany w UI
  są testowane jednostkowo (`AccessDenialTests`).
- Windows High Contrast, rzeczywisty monitor i skalowanie per-monitor (CI renderuje off-screen).
- Publikowanie zrzutów do repo lub komentowanie obrazków inline (GitHub wymaga uwierzytelnionego
  linku; artefakt + komentarz z linkiem wystarcza).
- Wizualna regresja pikselowa (baseline + diff) — możliwa w kolejnym kroku na bazie tych plików.
- Zmiany w XAML, logice serwera i protokole.

## Kryteria akceptacji (Given / When / Then)

1. **Given** PR do `main`, **when** CI na `windows-latest` wykona job `screenshots`, **then**
   w artefakcie `ui-screenshots` jest 6 plików PNG (`main-100/125/150`, `akcje-i-audyt-100/125/150`)
   o wymiarach odpowiednio 1460×800, 1825×1000 i 2190×1200 px.
2. **Given** uruchomiony job, **when** porównam zrzuty, **then** widok zawiera dane z serwera
   (`UseMockData = true`): pracownie, komputery z IP/stanem, logowania, świeżość i tożsamość
   **Administrator (dev-admin)** — nie „Pobieranie danych…” i nie puste listy.
3. **Given** brak danych (serwer nie odpowiada albo `welcome` nie przychodzi), **when** mija
   `--wait`, **then** narzędzie kończy się kodem `2`, wypisuje status i świeżość, a job jest
   czerwony — „pusty” zrzut nie udaje sukcesu.
4. **Given** odmowa dostępu serwera, **when** narzędzie dostanie `IsAccessDenied`, **then**
   kończy się kodem `4` z komunikatem z `AccessDenial` (bez zapisywania zrzutów).
5. **Given** zrzut, **when** sprawdzę jego rozmiar, **then** plik ma ≥ 10 kB (heurystyka pustej
   klatki); mniejszy plik kończy job kodem `5`.
6. **Given** push na dowolną gałąź bez PR, **when** CI się uruchomi, **then** job `screenshots`
   jest pomijany (koszt), a `build-and-test` działa jak dotąd.
7. **Given** lokalna stacja Windows z serwerem w trybie mock, **when** uruchomię
   `dotnet run --project tools/ZseAdmin.Screenshots`, **then** pliki pojawią się w `screenshots/`
   (katalog ignorowany przez git).

## Testy

- CI jest testem integracyjnym tego zadania (build + uruchomienie serwera + render + artefakt).
- Kontrole jakości w kodzie: wymiary bitmapy, minimalny rozmiar PNG, kody wyjścia, fail-fast na
  `IsAccessDenied`, brak danych → kod `2`.
- `dotnet test` bez zmian: narzędzie nie dotyka `Client.Core` ani serwera; testy jednostkowe
  (w tym `AccessDenialTests`) pozostają zielone.

## Definicja ukończenia

- [x] Job `screenshots` w `.github/workflows/ci.yml`, artefakt `ui-screenshots`, podsumowanie,
  komentarz w PR z linkiem.
- [x] `tools/ZseAdmin.Screenshots` w solution; brak nowych paczek NuGet.
- [x] Brak HTML/makiety — renderowany jest `MainWindow.xaml` z prawdziwym `MainViewModel`.
- [x] Dokumentacja: `uruchomienie.md` (uruchomienie lokalne), `ui-design-system.md` §5.1,
  `README.md`, `roadmapa.md`, `zadania/README.md`.
- [x] `dotnet build -c Release` i `dotnet test` zielone na `windows-latest`; zrzuty w artefakcie.

## Ryzyka i decyzje

- **Sesja 0 / brak pulpitu:** dlatego render off-screen, nie `Show()`. Jeśli GitHub zmieni model
  sesji, narzędzie nadal działa — nie zależy od HWND.
- **Różnice antyaliasingu tekstu** między `RenderTargetBitmap` a monitorem: akceptowalne; celem
  jest układ i kompletność danych, nie pikselowy baseline.
- **Koszt CI:** osobny job tylko dla PR-ów i `workflow_dispatch`; ~2 min, po `build-and-test`.
- **Token deweloperski** w jobie to `dev-admin-token` z `appsettings.json` — istniejący, jawny
  token dev (nie sekret). Produkcja nadal nie zawiera tokenów; artefakt nie zawiera konfiguracji.
- **Zakładka „Akcje i audyt”** jest wybierana przez `TabControl.SelectedIndex = 1` w drzewie
  wizualnym; gdyby UI zmieniło kolejność zakładek, zrzut pokaże inną — test wymiarów i rozmiaru
  nadal przejdzie, więc przy zmianie kolejności trzeba zaktualizować indeks w `Program.cs`.
