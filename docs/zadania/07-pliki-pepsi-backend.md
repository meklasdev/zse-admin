# ZAD-07 — Hosting plików `/pepsi`: backend tylko do odczytu

**Etap:** M8 · **Priorytet:** P1 · **Zależy od:** ZAD-06 (**D3** = UNC, **D4** = wszystkie role — podjęte)
**Blokuje:** ZAD-08
**Etykiety:** `enhancement`, `help wanted`, `documentation` · **Szacunek:** 3 dni
**Źródło:** [`../notatki-zamawiajacego-2026-11-26.md`](../notatki-zamawiajacego-2026-11-26.md) F4, §3.5

## Kontekst

„mamy też hosing plików w /pepsi są tam pliki są jakieś tam foldery”. Aplikacja ma umieć pokazać
drzewo folderów i listę plików. **D3 (podjęte):** to **dysk sieciowy na serwerze**, więc `Root`
jest ścieżką **UNC**, a nie literą mapowanego dysku (mapowanie jest per użytkownik i nie jest
widoczne dla usługi Windows). **D4 (podjęte):** listing widzą wszystkie role aplikacji, a wydzielone
prawa zapisu na udziale pozostają sprawą NTFS **poza aplikacją**. Ścieżka jest **konfiguracją**, adapter
jedynym miejscem znającym FS; v1 to wyłącznie odczyt **metadanych** — bez treści, bez zapisu,
bez dostępu dla uczniów (ZAD-06).

## Zakres

1. Konfiguracja `ZseAdmin:FileHosting`:
   ```jsonc
   "FileHosting": {
     "Root": "",              // UNC, np. \\serwer\pepsi (D3); NIE litera mapowanego dysku
     "Enabled": false,
     "MaxEntries": 500,
     "MaxDepth": 8,
     "AllowedExtensions": []  // pusta = bez filtra rozszerzeń (w v1 i tak nie zwracamy treści)
   }
   ```
   Wartość domyślna wyłączona; produkcja nie startuje z włączonym źródłem bez ustawionego `Root`.
   **D3:** konto usługi musi mieć prawa odczytu do UNC; **D4:** `files.read` dla wszystkich ról,
   a zapis na udziale jest poza aplikacją (v1 go nie implementuje).
2. `src/ZseAdmin.Server/Infrastructure/Files/` (nowy):
   - `IFileHostingSource` (port): `ListAsync(relativePath, take, ct)` → wpisy + `DataFreshnessDto`,
   - `FileSystemHostingSource` (adapter): `Path.GetFullPath` + sprawdzenie, że wynik zaczyna się od
     kanonicznego `Root` (porównanie z uwzględnieniem separatorów i wielkości liter systemu plików),
     odrzucenie `..`, ścieżek absolutnych, dowiązań symbolicznych / junction / reparse point,
     odrzucenie ścieżek z `:` i znaków sterujących; `MaxDepth`, `MaxEntries`, timeout,
     `CancellationToken`; brak uprawnień → `SourceUnavailableException` z nazwą źródła.
   - `FileEntryDto` w `ZseAdmin.Contracts`: `name`, `relativePath`, `kind` (`folder|file`),
     `sizeBytes?`, `modifiedAtUtc?`, `extension?` — **bez zawartości i bez pełnej ścieżki serwera**.
3. Protokół (v1, kompatybilnie wstecz): `MessageTypes.FilesList = "files.list"`,
   `FilesListRequest` (`path?`, `take`), `FilesListResponse` (`items`, `totalMatched`, `freshness`).
   `Capability.FilesRead` w `RoleCatalog` — **D4:** Administrator, Teacher i ReadOnly;
   `MessageRouter` + `CapabilityGuard`, `forbidden` z audytem `denied` dla uczniów i ról bez
   uprawnienia (ZAD-06).
4. Semantyka braków z [`../model-danych.md`](../model-danych.md) §4: `Root` niedostępny →
   `error: upstreamUnavailable` z nazwą źródła (nie pusta lista); przekroczenie `MaxEntries` →
   `completeness = partial` + nota z limitem; źródło wyłączone → jawny `unavailable` z powodem.
5. Dokumentacja: [`../api-websocket.md`](../api-websocket.md) §3 i §7.1 (nowy typ w v1),
   [`../model-danych.md`](../model-danych.md), [`../uprawnienia-i-dane-osobowe.md`](../uprawnienia-i-dane-osobowe.md)
   (`files.read`), [`../uruchomienie.md`](../uruchomienie.md) (konfiguracja `Root`, prawa konta usługi).

## Poza zakresem

- Pobieranie/streamowanie treści plików, upload, usuwanie, przenoszenie, zmiana uprawnień NTFS,
  indeksowanie pełnotekstowe, miniaturki, „ostatnio otwarte”.
- Mapowanie plików na konta uczniów (**D6**), wyciąganie danych osobowych z nazw plików.
- Zmiana `protocolVersion`, nowa baza danych, zmiana ról M0–M4.

## Kryteria akceptacji (Given / When / Then)

1. **Given** `Root` (UNC) wskazuje na udział z 3 folderami i 12 plikami, **when** dowolna rola aplikacji wyśle
   `files.list` bez `path`, **then** dostaje wyłącznie wpisy bezpośrednie z `kind`, rozmiarem,
   datą, ścieżką **względną** oraz `freshness` z `source = "/pepsi"` (bez ścieżki fizycznej serwera).
2. **Given** `path = "../../Windows/System32"`, `path = "C:\\Windows"` albo `path` z dowiązaniem
   wychodzącym poza `Root`, **when** wyślę `files.list`, **then** dostaję `invalidRequest` i adapter
   nie wykonuje żadnego odczytu poza `Root` (test z tymczasowym katalogiem).
3. **Given** `Root` nie istnieje albo konto usługi nie ma do niego dostępu, **when** wyślę
   `files.list`, **then** dostaję `error: upstreamUnavailable` z nazwą źródła i powodem — nie pustą
   listę ani `notFound`.
4. **Given** konto z grupy uczniowskiej albo rola bez `files.read`, **when** wyśle `files.list`,
   **then** `forbidden` + audyt `denied`, bez jakichkolwiek nazw plików; **given** Nauczyciel lub
   ReadOnly (**D4**), **then** dostaje listing (v1 tylko odczyt metadanych).
5. **Given** katalog z 10 000 wpisów i `MaxEntries = 500`, **when** wyślę `files.list`, **then**
   odpowiedź ma ≤ 500 wpisów, `completeness = partial` i notę „Pokazano 500 z …” — bez cichego
   ucięcia.
6. **Given** `Enabled = false`, **when** wyślę `files.list`, **then** odpowiedź ma
   `completeness = unavailable` z jawnym powodem „Źródło plików wyłączone w konfiguracji”.
7. **Given** klient w wersji M4, **when** połączy się z serwerem obsługującym `files.list`,
   **then** `protocolVersion = 1`, a nieznany typ/capability jest ignorowany.

## Testy

- `tests/ZseAdmin.Tests/FileHostingSourceTests.cs` (nowy, tymczasowy katalog w teście): listing,
  path traversal (kilka wariantów), reparse point, limit wpisów, głębokość, brak `Root`, brak
  uprawnień (jeśli możliwe do symulacji), anulowanie.
- `tests/ZseAdmin.Tests/MessageRouterTests.cs` / `SecurityM3Tests.cs`: `files.read` u
  Administratora, Teacher i ReadOnly (D4); `forbidden` z audytem dla odrzuconego ucznia i roli
  bez uprawnienia.
- `tests/ZseAdmin.Tests/ProtocolSerializationTests.cs`: `FileEntryDto` nie zawiera pól treści ani
  ścieżki bezwzględnej.

## Definicja ukończenia

- [ ] `Root` nigdy nie jest hardkodowany; `FileSystemHostingSource` to jedyny kod dotykający FS.
- [ ] `Root` to UNC (D3), nie litera dysku; `appsettings.Production.json` ma źródło wyłączone
  do czasu podania ścieżki i praw konta usługi; brak sekretów.
- [ ] Dokumentacja protokołu, modelu i uprawnień zaktualizowana; `protocolVersion = 1`.
- [ ] CI zielone; decyzje **D3**, **D4** zapisane w notatkach §4; status w [`README.md`](README.md).

## Ryzyka i decyzje

- **D3 = dysk sieciowy / UNC (podjęte):** konto usługi musi mieć odczyt do udziału, a `Root`
  nie może być literą mapowanego dysku; przy UNC dodać jawne sprawdzenie prefiksu udziału
  i test, że `Path.GetFullPath` nie wychodzi poza `Root`.
- **D4 = wszystkie role aplikacji (podjęte):** `files.read` dla Administrator/Teacher/ReadOnly;
  uczniowie są odrzucani wcześniej (ZAD-06). Zapis na udziale pozostaje poza aplikacją — v1 nie
  implementuje żadnej operacji zapisu.
- Nazwy plików mogą zawierać dane osobowe uczniów (RODO, DPIA w M5): nie logować pełnych list,
  nie zwracać treści, nie indeksować, rozważyć audyt samego faktu odczytu listy.
- Reparse points / junction pod UNC potrafią „wyjść” poza `Root` mimo poprawnego prefiksu —
  test na to jest warunkiem zamknięcia zadania, nie opcją.
