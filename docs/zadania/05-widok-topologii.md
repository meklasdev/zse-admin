# ZAD-05 — Widok topologii w stylu Cisco Packet Tracer

**Etap:** M7 · **Priorytet:** P1 · **Zależy od:** ZAD-01, ZAD-02, ZAD-04
**Etykiety:** `enhancement`, `accessibility` · **Szacunek:** 4–5 dni
**Źródło:** [`../notatki-zamawiajacego-2026-11-26.md`](../notatki-zamawiajacego-2026-11-26.md) F2 · **Decyzje:** D2 = gotowe grafiki urządzeń + diagram ułożenia + diagram połączeń; D7 = wszystkie zalogowane role

## Kontekst

„trzeba mieć zdj jak z cisco pakiet tracer takie diagramy jak są podpinane” — administrator ma
widzieć, w której sali jest stacja, do którego portu patch panelu i switcha jest podpięta i skąd
przychodzi uplink. Dane pochodzą z `topology.json` (ZAD-04), stan stacji z `computers.list`.
Widok musi być płaski: prostokątne węzły z 1px obramowaniem, łącza jako linie 1px, status
oznaczony **kolorem obramowania + tekstem**, nigdy poświatą. Obowiązuje
[`../ui-design-system.md`](../ui-design-system.md).

**D2 (doprecyzowane przez zamawiającego):** „zdj” oznacza **gotowe rysunki komputerów/urządzeń
w stylu Packet Tracer** (nie zdjęcia z serwerowni), z których składamy **dwa** widoki:
(1) **układ/ułożenie komputerów w sali** (rzędy, stanowiska) oraz (2) **diagram połączeń** z portami.
Źródło grafik: własny zestaw wektorowy w repo, chyba że szkoła dostarczy pliki (§4.1 notatek).

## Zakres

1. `src/ZseAdmin.Client.Core/Topology/TopologyLayout.cs` (nowy, **bez zależności od WPF**):
   deterministyczny układ węzłów w rzędach (sala → rząd stacji → patch panel → switch → uplink)
   oraz obliczenie ortogonalnych łączy z etykietami portów. Czysta funkcja:
   `IReadOnlyList<TopologyNodeLayout> Compute(TopologyDto dto, LayoutOptions options)` — do
   przetestowania w xUnit bez UI.
2. `src/ZseAdmin.Client.Core/ViewModels/TopologyViewModel.cs` (nowy): pobranie `topology.get`
   przez `ZseAdminApiClient` (dodać metodę + subskrypcję błędów), połączenie z aktualnym stanem
   komputerów, wybór węzła, zoom, filtr sali, stany: ładowanie / brak danych / `forbidden` /
   `upstreamUnavailable` / `partial`.
3. `src/ZseAdmin.Client/Views/TopologyView.xaml` (nowy): `ItemsControl` + `Canvas`, szablony węzłów
   wg `NetworkNodeKind`, łącza jako `Polyline`/`Path` 1px, etykiety portów, legenda, breadcrumb
   sal, zoom 50–200% i pan (bez efektów „glow”), klik węzeł stacji → szczegóły komputera
   w istniejącym prawym panelu (`SelectedComputer` w `MainViewModel`).
4. **Grafiki urządzeń i układ sali (D2):** wektorowy zestaw rysunków w
   `src/ZseAdmin.Client/Assets/Topology/` (stacja, switch, patch panel, router/uplink) w konwencji
   Packet Tracer — 1px, jeden kolor, bez cieni i gradientów; mapowanie
   `NetworkNodeKind → IconKey` i opcjonalny `imageRef` z `topology.json`. Osobny, płaski **diagram
   ułożenia stanowisk w sali** (rzędy/stanowiska) obok diagramu połączeń z portami.
5. Integracja: zakładka „Topologia” w `MainWindow.xaml`, style w `Styles/Topology.xaml` wyłącznie
   z tokenów ZAD-01, ikony wyłącznie z `IconKey` (ZAD-02).
6. Dokumentacja: [`../uruchomienie.md`](../uruchomienie.md) (jak uzupełnić `topology.json`),
   [`../ui-design-system.md`](../ui-design-system.md) (jeśli pojawi się nowy wzorzec kontrolki),
   zrzut ekranu w `docs/` tylko jeśli nie jest duży (preferować PR).

## Poza zakresem

- Edycja topologii w UI, zapis `topology.json` z aplikacji, automatyczne wykrywanie łączy
  (LLDP/SNMP), monitoring stanu portów w czasie rzeczywistym.
- Fotografie prawdziwej szafy/patch paneli (**D2** = gotowe rysunki, nie foto); wczytywanie
  własnych plików graficznych szkoły — do doprecyzowania w §4.1 notatek.
- Zmiana protokołu, nowe uprawnienia, nowa baza danych, zmiana `MainViewModel` w zakresie akcji M4.

## Kryteria akceptacji (Given / When / Then)

1. **Given** topologia 3 sale / 24 stacje / 2 switche, **when** otworzę zakładkę „Topologia”,
   **then** wszystkie węzły są widoczne, nie nachodzą na siebie, a każde łącze zaczyna i kończy się
   na właściwym porcie z etykietą.
2. **Given** stacja z `status = unknown` i `statusObservedAtUtc = null`, **when** renderuję węzeł,
   **then** ma 1px obramowanie `Brush.StatusInfo` i tekst „brak danych”, a nie kolor/etykietę
   „offline”.
3. **Given** kliknięcie węzła stacji, **when** wybiorę go, **then** prawy panel pokazuje szczegóły
   tego komputera wraz z `freshness` (bez ręcznego odświeżania).
4. **Given** zoom 50% i 200% oraz przesuwanie, **when** korzystam z diagramu, **then** linie
   pozostają 1px, etykiety są czytelne, a w UI nie pojawia się cień, rozmycie ani animacja
   poświaty.
5. **Given** Nauczyciel lub ReadOnly (**D7 = C**), **when** otworzy widok, **then** widzi wszystkie
   sale (bez filtra `classroomScope` dla topologii); **given** rola bez `topology.read` albo uczeń,
   **then** ekran „Brak dostępu”, nie puste płótno.
6. **Given** `error: upstreamUnavailable` dla `topology.json`, **when** ładuję widok, **then**
   widzę nazwę źródła i przyczynę, a nie pusty diagram sugerujący „brak połączeń”.
7. **Given** `TopologyLayout.Compute` z tym samym DTO i opcjami, **when** wywołam ją dwukrotnie,
   **then** wynik jest identyczny (determinizm), a testy nie wymagają WPF ani wątku UI.
8. **Given** sala z 12 stanowiskami, **when** otworzę widok, **then** widzę gotowe rysunki urządzeń
   (1px, bez cieni) oraz diagram ułożenia komputerów w rzędach obok diagramu połączeń z portami.

## Testy

- `tests/ZseAdmin.Tests/TopologyLayoutTests.cs` (nowy): brak kolizji węzłów, końce łączy,
  determinizm, pusta topologia, węzeł „niepodpięty”, 100 węzłów (wydajność).
- `tests/ZseAdmin.Tests/TopologyViewModelTests.cs` (nowy, jeśli możliwe bez WPF): stany błędu,
  `forbidden`, wybór węzła, zoom w granicach.
- Regresja: `dotnet test`, `dotnet build -c Release`; manualne zrzuty 100/125/150% DPI.

## Definicja ukończenia

- [ ] `TopologyLayout` żyje w `Client.Core` i nie referencjonuje WPF (zasada z ADR-0001).
- [ ] Nowy widok używa wyłącznie tokenów ZAD-01 i `IconKey` ZAD-02.
- [ ] Checklista [`../ui-design-system.md`](../ui-design-system.md) §5 odhaczona; zrzuty w PR.
- [ ] `docs/roadmapa.md` i [`README.md`](README.md) odnotowują M7; status w [`README.md`](README.md).
- [ ] D2: grafiki urządzeń + diagram ułożenia sali + diagram połączeń; D7: brak filtra sal dla ról aplikacji.

## Ryzyka i decyzje

- **D2 = B, D7 = C (podjęte):** realizujemy grafiki urządzeń, diagram ułożenia sali i diagram
  połączeń dla wszystkich zalogowanych ról. Jedynym punktem do doprecyzowania jest źródło plików
  graficznych (§4.1 notatek) — zostawiamy `imageRef` jako jawne rozszerzenie.
- Duża liczba węzłów w `Canvas` może zepsuć responsywność — rozważyć wirtualizację / grupowanie
  rzędów i limit widocznych etykiet; nie kosztem czytelności portów.
- Łatwo „udekorować” diagram kolorowymi kablami i cieniami węzłów: `medium` (ethernet/fiber)
  opisujemy **tekstem w legendzie**, nie efektem świetlnym.
- `TopologyDto` opisuje konfigurację, nie stan bieżący — w widoku musi być widoczna nota
  „opis wg topology.json z <czas>”, żeby nie sugerować live monitoringu portów.
