# ZAD-10 — CI „zero glow”, dostępność, szablon zadań i domknięcie dokumentacji

**Etap:** M6–M8 · **Priorytet:** P1 · **Zależy od:** ZAD-01 (uruchamiany na końcu każdego etapu)
**Etykiety:** `enhancement`, `documentation`, `accessibility` · **Szacunek:** 2 dni
**Specyfikacja:** [`../ui-design-system.md`](../ui-design-system.md) §1.1, §4, §5

## Kontekst

„Całe UI ma być spójne, zero glow” nie przetrwa bez automatycznej kontroli: wystarczy jeden PR z
`DropShadowEffect` albo kolorem wpisanym wprost w XAML i system projektowy (ZAD-01) zacznie się
rozjeżdżać. Potrzebna jest też stała checklista dostępności oraz domknięcie dokumentacji po nowych
etapach M6–M8, żeby `README.md`, `roadmapa.md`, protokół, model danych i uprawnienia opisywały to,
co faktycznie działa.

## Zakres

1. **Kontrola „zero glow” w CI** — krok w
   [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml) (PowerShell, po `Build`):
   - skan `src/ZseAdmin.Client/**/*.xaml` na `DropShadowEffect`, `BlurEffect`,
     `DropShadowBitmapEffect`, `OuterGlowBitmapEffect`, `LinearGradientBrush`,
     `RadialGradientBrush` → błąd z komunikatem „zero glow: ...”,
   - skan widoków (wszystko poza `Styles/`) na literały kolorów (`#[0-9A-Fa-f]{6}` oraz
     `Background="White"`, `Foreground="Black"` itp.) → błąd „użyj zasobu z Theme.xaml”,
   - wyjątki wyłącznie przez jawną listę allowlist w skrypcie, z komentarzem dlaczego.
   Skrypt w `scripts/check-ui-style.ps1`, wywoływany z workflow; działa na `windows-latest`.
2. **Dostępność** — `docs/ui-design-system.md` §4 doprecyzować o checklistę PR:
   kontrast ≥ 4.5:1 (z wynikiem pomiaru dla `Text.Meta` na `SurfaceMuted`), fokus na każdej
   kontrolce, `Tab` zgodny z kolejnością wizualną, `AutomationProperties.Name` dla ikon,
   min. 32 px celu kliknięcia, zrzuty 100/125/150% DPI, tryb wysokiego kontrastu.
   Dodać krótką sekcję do [`../uruchomienie.md`](../uruchomienie.md): jak uruchomić
   `scripts/check-ui-style.ps1` lokalnie.
3. **Szablon zadania** — `.github/ISSUE_TEMPLATE/zadanie.md` z sekcjami: Kontekst, Zakres,
   Poza zakresem, Kryteria akceptacji (Given/When/Then), Testy, Definicja ukończenia, Ryzyka i
   decyzje — identycznie jak pliki w `docs/zadania/`.
4. **Domknięcie dokumentacji** (po realizacji M6–M8, aktualizować przy każdym zamkniętym zadaniu):
   - `README.md`: tabela dokumentów, struktura repo, status etapów,
   - [`../roadmapa.md`](../roadmapa.md): status M6–M8 + linki do zadań i kryteria,
   - [`../api-websocket.md`](../api-websocket.md) §7.1: dopisać `topology.get`, `files.list`,
     `LoginEventDto.accountScope`, `ClassroomDto.roomId` jako rozszerzenia v1,
   - [`../model-danych.md`](../model-danych.md), [`../architektura.md`](../architektura.md),
     [`../uprawnienia-i-dane-osobowe.md`](../uprawnienia-i-dane-osobowe.md): spójność z kodem,
   - [`README.md`](README.md) w `docs/zadania/`: statusy zadań (`zrobione` + link do PR).
5. `docs/ocena.md` — krótka adnotacja, że zakres produktu rozszerzono o notatki z 2026-11-26
   (bez przepisywania oceny).

## Poza zakresem

- Zmiana logiki aplikacji, protokołu, ról; nowe zależności NuGet; nowe testy funkcjonalne M0–M4.
- Automatyczne testy wizualne/screenshot diff (może być osobne zadanie, gdy widoków przybędzie).
- Tłumaczenie UI na inne języki.

## Kryteria akceptacji (Given / When / Then)

1. **Given** PR dodający `DropShadowEffect` do dowolnego XAML klienta, **when** uruchomi się CI,
   **then** krok „Kontrola stylu UI” kończy się błędem z nazwą pliku i komunikatem „zero glow”.
2. **Given** PR z `Background="#FFFFFF"` w widoku, **when** uruchomi się CI, **then** krok kończy
   się błędem „użyj zasobu z Theme.xaml”.
3. **Given** poprawny kod z tokenami ZAD-01, **when** uruchomię `pwsh scripts/check-ui-style.ps1`
   lokalnie i w CI, **then** skrypt kończy się kodem 0, a `dotnet build`/`dotnet test` są zielone.
4. **Given** nowe zgłoszenie w GitHub, **when** wybiorę szablon „Zadanie”, **then** formularz ma
   wszystkie sekcje z konwencji `docs/zadania/` wraz z przykładem Given/When/Then.
5. **Given** zamknięte zadanie M6–M8, **when** sprawdzę `README.md`, `docs/roadmapa.md`,
   `docs/api-websocket.md` i `docs/zadania/README.md`, **then** opisują one nowe byty, komunikaty,
   uprawnienia i status bez rozjazdu z kodem.
6. **Given** checklista PR z `docs/ui-design-system.md` §4, **when** recenzent ją wypełni,
   **then** zawiera wynik pomiaru kontrastu, zrzuty 100/125/150% DPI i potwierdzenie fokusu
   klawiaturowego.

## Testy

- `scripts/check-ui-style.ps1`: testy negatywowe na tymczasowych plikach XAML (efekt, gradient,
  literał koloru) i pozytywne na obecnym kodzie — uruchamiane w CI.
- Pełny `dotnet restore` + `dotnet build -c Release` + `dotnet test` na `windows-latest`.
- Manualny przegląd: `gh issue list`, linki w dokumentacji, `git diff --check`.

## Definicja ukończenia

- [ ] Skrypt i krok w CI istnieją, są udokumentowane i działają na czystym checkoutcie.
- [ ] Szablon issue opublikowany; `docs/zadania/README.md` ma aktualne statusy.
- [ ] Wszystkie dokumenty z listy w Zakresie punkcie 4 zaktualizowane; `git diff --check` czysty.
- [ ] Ostatni przebieg CI dla gałęzi zielony, wynik odnotowany w [`../roadmapa.md`](../roadmapa.md).
- [ ] Decyzje D1–D10 i statusy zadań widoczne w notatkach §4 oraz [`README.md`](README.md).

## Ryzyka i decyzje

- Zbyt agresywny skan kolorów może blokować uzasadnione wyjątki (np. kolor w `DrawingImage`
  ikon) — przewidzieć jawną, krótką allowlist z komentarzem, bez wyłączania całej kontroli.
- Skrypt musi działać w PowerShell na `windows-latest` i nie może wymagać .NET SDK ani paczek
  zewnętrznych.
- Dokumentacja „na koniec” łatwo się starzeje: aktualizować ją w **tym samym PR** co zadanie,
  a ZAD-10 domyka tylko spójność całości.
