# ZAD-04 — Model topologii: sale, pracownie, węzły i łącza

**Etap:** M7 · **Priorytet:** P1 · **Zależy od:** decyzja **D1** (i **D7** dla uprawnień)
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
   (kompatybilne wstecz). Każda odpowiedź zawiera `DataFreshnessDto` z `source = "topology.json"`.
3. `src/ZseAdmin.Server/Infrastructure/Topology/TopologyMap.cs`: wczytanie, walidacja i mapowanie
   (na wzór `ClassroomMap`), `Resolve(roomId)`, filtrowanie po `classroomScope` dla nauczyciela
   (**D7**).
4. Protokół: `MessageTypes.TopologyGet = "topology.get"`, `TopologyGetRequest` (opcjonalny
   `roomId`), `TopologyResponse`; `Capability.TopologyRead`; wpis w `RoleCatalog`
   (Administrator + ewentualnie Teacher/ReadOnly wg **D7**); router + `CapabilityGuard`.
5. Dokumentacja: [`../model-danych.md`](../model-danych.md) (nowe byty + semantyka braków),
   [`../api-websocket.md`](../api-websocket.md) §3 i §7.1 (nowy typ w wersji 1),
   [`../architektura.md`](../architektura.md) §6 (warstwa „sala”),
   [`../uruchomienie.md`](../uruchomienie.md) (plik konfiguracyjny).

## Poza zakresem

- Rysowanie diagramu (ZAD-05), zdjęcia szaf/patch paneli (**D2**), automatyczne wykrywanie
  topologii (LLDP/SNMP/ARP), edycja topologii z poziomu UI, zapis do AD.
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
6. **Given** nauczyciel z `classroomScope = ["prac-101"]` i decyzja **D7** = „widzi swoje sale”,
   **when** wyśle `topology.get` bez `roomId`, **then** serwer zwraca wyłącznie sale powiązane z
   `prac-101` — filtrowanie po stronie serwera, nie w UI.
7. **Given** rola bez `topology.read`, **when** wyśle `topology.get`, **then** `forbidden` i wpis
   audytu `denied` (spójnie z ZAD-06).

## Testy

- `tests/ZseAdmin.Tests/TopologyMapTests.cs` (nowy): poprawna mapa, duplikat `id`, nieistniejący
  endpoint łącza, kolizja `classroomId`, węzeł współdzielony, komputer „niepodpięty”,
  błędny `kind`.
- `tests/ZseAdmin.Tests/RoleCatalogTests.cs` / protokolarne: `topology.read` tylko w dozwolonych
  rolach; `MessageRouter` odrzuca żądanie bez uprawnienia.
- `tests/ZseAdmin.Tests/ProtocolSerializationTests.cs`: nowe DTO serializują się zgodnie z
  konwencją `camelCase`, a starszy klient ignoruje dodatkowe pole.

## Definicja ukończenia

- [ ] `topology.json` jest jedynym źródłem opisu fizycznego i jest walidowany przy starcie.
- [ ] CI kopiuje `topology.json` do artefaktu (rozszerzyć kontrolę w
  [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml) o ten plik, jak dla `classrooms.json`).
- [ ] Dokumentacja protokołu i modelu zaktualizowana; `protocolVersion = 1`.
- [ ] Decyzje **D1**, **D7** zapisane w notatkach zamawiającego; status w [`README.md`](README.md).

## Ryzyka i decyzje

- **D1** decyduje, czy sala może mieć kilka pracowni i odwrotnie — od tego zależy kształt walidacji.
  Domyślna propozycja: relacja jawna, wiele-do-wielu dopuszczone, bez wyprowadzania z OU.
- **D7** decyduje o `topology.read` dla Nauczyciela; do czasu decyzji przyjąć wariant „tylko
  Administrator” (fail-close) i zostawić konfigurację roli w jednym miejscu.
- Nadmiarowy opis portów może szybko się zdezaktualizować — `freshness` musi mówić, skąd dane
  i kiedy wczytane; nie wolno sugerować, że diagram odzwierciedla bieżący stan łączy.
- **D2** (zdjęcia) nie blokuje tego zadania: model ma pole na opcjonalny `attachmentRef`, ale
  bez podjętej decyzji nie jest ono wypełniane.
