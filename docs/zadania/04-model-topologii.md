# ZAD-04 — Model topologii: sale, pracownie, węzły i łącza

**Etap:** M7 · **Priorytet:** P1 · **Zależy od:** — (**D1** = lista sal od admina; **D7** = wszystkie role — podjęte)
**Blokuje:** ZAD-05
**Etykiety:** `enhancement`, `documentation` · **Szacunek:** 3–4 dni
**Źródło:** [`../notatki-zamawiajacego-2026-11-26.md`](../notatki-zamawiajacego-2026-11-26.md) F1, F2

## Kontekst

W szkole jest kilka sal, a w każdej pracownie robocze; potrzebny jest diagram „jak z Cisco Packet
Tracer” pokazujący, jak stacje są podpięte. Istniejący `ClassroomDto` opisuje **pracownię jako
kontekst szkolny mapowany na OU** ([`../model-danych.md`](../model-danych.md) §1,
[`../architektura.md`](../architektura.md) §6) — to nie jest sala i nie wolno tych pojęć mieszać.
Tak jak `classrooms.json`, opis fizyczny ma być **konfiguracją walidowaną przy starcie**, nie
odkrywaniem sieci (SNMP/LLDP) ani danymi wyprowadzonymi z DN w AD.

## Zakres

1. `src/ZseAdmin.Server/topology.json` (nowy, wzorzec konfiguracji):
   ```jsonc
   [
     {
       "id": "sala-101",
       "name": "Sala 101",
       "building": "Budynek A",
       "classroomIds": ["prac-101"],
       "nodes": [
         { "id": "sw-101", "kind": "switch", "name": "SW-101", "ports": 24 },
         { "id": "pp-101", "kind": "patchPanel", "name": "PP-101", "ports": 24 },
         { "id": "st-101-01", "kind": "station", "name": "ST101-01", "computerName": "ST101-01" }
       ],
       "links": [
         { "id": "l-1", "from": { "nodeId": "st-101-01", "port": "NIC" },
           "to": { "nodeId": "pp-101", "port": "P01" }, "medium": "ethernet" },
         { "id": "l-2", "from": { "nodeId": "pp-101", "port": "P01" },
           "to": { "nodeId": "sw-101", "port": "Gi0/1" }, "medium": "ethernet" },
         { "id": "l-3", "from": { "nodeId": "sw-101", "port": "Gi0/24" },
           "to": { "nodeId": "uplink-A", "port": "core" }, "medium": "fiber" }
       ]
     }
   ]
   ```
   `kind`: `station | switch | patchPanel | router | uplink | other`. Węzły współdzielone między
   salami (np. `uplink-A`) zdefiniować w jednym miejscu i referencjonować — bez duplikowania.
2. `ZseAdmin.Contracts`: nowe rekordy `RoomDto`, `NetworkNodeDto`, `NetworkLinkDto`,
   `TopologyDto`, enum `NetworkNodeKind`; opcjonalne pole `roomId` w `ClassroomDto`
   (kompatybilne wstecz). Opcjonalny `imageRef` na węźle wskazuje grafikę urządzenia (**D2**);
   domyślnie używany jest `IconKey` z ZAD-02. Każda odpowiedź ma `DataFreshnessDto`
   z `source = "topology.json"`.
3. `src/ZseAdmin.Server/Infrastructure/Topology/TopologyMap.cs`: wczytanie, walidacja i mapowanie
   (na wzór `ClassroomMap`), `Resolve(roomId)`. **D7 = C:** brak filtrowania po `classroomScope`
   dla topologii — wszystkie zalogowane role widzą wszystkie sale.
4. Protokół: `MessageTypes.TopologyGet = "topology.get"`, `TopologyGetRequest` (opcjonalny
   `roomId`), `TopologyResponse`; `Capability.TopologyRead`; wpis w `RoleCatalog`
   (**D7 = C:** Administrator, Teacher i ReadOnly); router + `CapabilityGuard`.
5. Dokumentacja: [`../model-danych.md`](../model-danych.md) (nowe byty + semantyka braków),
   [`../api-websocket.md`](../api-websocket.md) §3 i §7.1 (nowy typ w wersji 1),
   [`../architektura.md`](../architektura.md) §6 (warstwa „sala”),
   [`../uruchomienie.md`](../uruchomienie.md) (plik konfiguracyjny).

## Poza zakresem

- Rysowanie diagramów i grafik urządzeń (ZAD-05), fotografie szaf/patch paneli (**D2** = gotowe
  rysunki, nie foto), automatyczne wykrywanie topologii (LLDP/SNMP/ARP), edycja topologii z poziomu
  UI, zapis do AD.
- Zmiana `protocolVersion` (nowy typ + opcjonalne pole są zgodne wstecz), zmiana `classrooms.json`,
  nowa baza danych (ADR-0003).

## Kryteria akceptacji (Given / When / Then)

1. **Given** poprawny `topology.json` z 2 salami, **when** serwer startuje i klient wyśle
   `topology.get`, **then** dostaje `TopologyResponse` z salami, węzłami, łączami i
   `freshness.completeness = complete` oraz `source = "topology.json"`.
2. **Given** łącze wskazuje na nieistniejący `nodeId` albo `classroomId` nie istnieje w
   `classrooms.json`, **when** serwer startuje, **then** nie startuje i loguje błąd konfiguracji
   z identyfikatorem węzła/sali (tak jak kolizja OU w M1).
3. **Given** komputer z AD, którego nie ma w `topology.json`, **when** pobiorę topologię,
   **then** jest zwrócony jako węzeł `station` z `linkState = "unknown"` / „niepodpięty — brak
   danych”, a nie znika z odpowiedzi (semantyka [`../model-danych.md`](../model-danych.md) §4).
4. **Given** brak pliku `topology.json` albo błąd JSON, **when** wyślę `topology.get`, **then**
   dostaję `error: upstreamUnavailable` z nazwą źródła, a nie pustą listę sal.
5. **Given** klient w wersji M4 (bez obsługi `topology.get`), **when** połączy się z nowym serwerem,
   **then** `welcome.protocolVersion` nadal wynosi `1`, a klient ignoruje nowe `capability` i działa.
6. **Given** nauczyciel z `classroomScope = ["prac-101"]` i **D7 = C**, **when** wyśle
   `topology.get` bez `roomId`, **then** serwer zwraca **wszystkie** sale (bez filtra `classroomScope`
   dla topologii), a rolę bez `topology.read` i tak odrzuca `forbidden`. To świadome odstępstwo od
   ADR-0007, dotyczące wyłącznie nowego widoku.
7. **Given** rola bez `topology.read`, **when** wyśle `topology.get`, **then** `forbidden` i wpis
   audytu `denied` (spójnie z ZAD-06).

## Testy

- `tests/ZseAdmin.Tests/TopologyMapTests.cs` (nowy): poprawna mapa, duplikat `id`, nieistniejący
  endpoint łącza, kolizja `classroomId`, węzeł współdzielony, komputer „niepodpięty”,
  błędny `kind`.
- `tests/ZseAdmin.Tests/RoleCatalogTests.cs` / protokolarne: `topology.read` we wszystkich trzech
  rolach aplikacji (D7 = C); `MessageRouter` odrzuca rolę bez uprawnienia i ucznia.
- `tests/ZseAdmin.Tests/ProtocolSerializationTests.cs`: nowe DTO serializują się zgodnie z
  konwencją `camelCase`, a starszy klient ignoruje dodatkowe pole.

## Definicja ukończenia

- [ ] `topology.json` jest jedynym źródłem opisu fizycznego i jest walidowany przy starcie.
- [ ] CI kopiuje `topology.json` do artefaktu (rozszerzyć kontrolę w
  [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml) o ten plik, jak dla `classrooms.json`).
- [ ] Dokumentacja protokołu i modelu zaktualizowana; `protocolVersion = 1`.
- [ ] Decyzje **D1**, **D7** zapisane w notatkach §4 (podjęte); status w [`README.md`](README.md).

## Ryzyka i decyzje

- **D1 = C (podjęte):** administrator przygotowuje listę sal i `classroomIds` w `topology.json`.
  Relacja pozostaje jawna i walidowana, bez wyprowadzania z OU; przykładowe wpisy są tylko wzorcem.
- **D7 = C (podjęte):** `topology.read` dla Administrator, Teacher i ReadOnly, bez filtra sal.
  Uczniowie są odrzucani wcześniej przez ZAD-06; konfiguracja roli zostaje w jednym miejscu.
- Nadmiarowy opis portów może szybko się zdezaktualizować — `freshness` musi mówić, skąd dane
  i kiedy wczytane; nie wolno sugerować, że diagram odzwierciedla bieżący stan łączy.
- **D2 = B (podjęte):** model ma opcjonalny `imageRef` na węźle; domyślnie grafiki pochodzą
  z `IconKey` (ZAD-02/05). Źródło ewentualnych własnych plików szkoły doprecyzowuje §4.1 notatek.
