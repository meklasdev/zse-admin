# Backlog zadań dla deweloperów (2026-11-26)

**Status:** implementacja rozpoczęta. ZAD-01 (PR #18), ZAD-06 (PR #19) i ZAD-11 (zrzuty UI w CI)
zrobione; pozostałe w kolejce —
ten katalog zawiera specyfikacje do implementacji, nie implementację.
**Materiał wejściowy:** [`../notatki-zamawiajacego-2026-11-26.md`](../notatki-zamawiajacego-2026-11-26.md)
**Obowiązujący styl:** [`../ui-design-system.md`](../ui-design-system.md) (spójne UI, zero glow)
**Plan etapów:** [`../roadmapa.md`](../roadmapa.md) (M6–M8)
**Decyzje:** D1–D10 **podjęte 2026-11-26** — [`../notatki-zamawiajacego-2026-11-26.md`](../notatki-zamawiajacego-2026-11-26.md) §4 (odpowiedzi ABC)
**Handoff:** [`PROMPT-dla-kolejnego-agenta.md`](PROMPT-dla-kolejnego-agenta.md) — gotowy, samowystarczalny prompt do przekazania implementacji dalej

Zadania powstały z notatki zamawiającego: kilka sal i pracowni, diagram połączeń w stylu Cisco
Packet Tracer, brak dostępu dla uczniów, hosting plików `/pepsi`, konto AD → profil lokalny na
stacji, ikony (`SvgIcons 0.1.10`) oraz jednolity, płaski system projektowy UI.

## Konwencja zadania

Każdy plik `NN-*.md` ma stałe sekcje: **Kontekst** (dlaczego), **Zakres** (konkretne pliki i zmiany),
**Poza zakresem**, **Kryteria akceptacji** w formacie **Given / When / Then** (zgodnie z
[`../roadmapa.md`](../roadmapa.md)), **Testy**, **Definicja ukończenia**, **Ryzyka i decyzje**.
Bez kryteriów nie zamykamy zadania. Zmiany w protokole są kompatybilne wstecz i zostają przy
`protocolVersion = 1` ([`../api-websocket.md`](../api-websocket.md) §7).

## Lista zadań

| ID | Zadanie | Etap | Priorytet | Zależy od | Plik | Status |
|---|---|---|---|---|---|---|
| ZAD-01 | System projektowy UI: tokeny, `ResourceDictionary`, refactor „zero glow” | M6 | P0 | — (D9: `Segoe UI`) | [`01-design-system.md`](01-design-system.md) | **zrobione** (PR #18) |
| ZAD-02 | Ikony: dodanie `SvgIcons 0.1.10`, weryfikacja w CI, jedno źródło ikon | M6 | P0 | ZAD-01 (D8 = B) | [`02-ikony-svg.md`](02-ikony-svg.md) | do zrobienia |
| ZAD-03 | Formularze dwukolumnowe, liczniki znaków, przerywana strefa upuszczania | M6 | P1 | ZAD-01, ZAD-02 | [`03-formularze-dwukolumnowe.md`](03-formularze-dwukolumnowe.md) | do zrobienia |
| ZAD-04 | Model topologii: sale wg listy admina → węzły/łącza, `topology.json`, DTO | M7 | P1 | D1: lista od admina; D7: wszystkie role | [`04-model-topologii.md`](04-model-topologii.md) | do zrobienia |
| ZAD-05 | Widok topologii: grafiki urządzeń + układ sali + diagram połączeń | M7 | P1 | ZAD-01, ZAD-02, ZAD-04 (D2, D7) | [`05-widok-topologii.md`](05-widok-topologii.md) | do zrobienia |
| ZAD-06 | Uczniowie bez dostępu: deny-by-default na serwerze + audyt odmów | M8 | P0 | — (D10 = A) | [`06-dostep-uczniow.md`](06-dostep-uczniow.md) | **zrobione** (PR #19) |
| ZAD-07 | `/pepsi` — backend tylko do odczytu, `Root` jako UNC (`files.list`) | M8 | P1 | ZAD-06 (D3: UNC, D4: wszystkie role) | [`07-pliki-pepsi-backend.md`](07-pliki-pepsi-backend.md) | do zrobienia |
| ZAD-08 | `/pepsi` — przeglądarka plików w UI i strefa upuszczania | M8 | P2 | ZAD-01, ZAD-03, ZAD-07 (D6) | [`08-pliki-pepsi-ui.md`](08-pliki-pepsi-ui.md) | do zrobienia |
| ZAD-09 | Konto AD vs profil/konto lokalne na stacji w historii logowań | M8 | P1 | — (D5 = A, D6) | [`09-konto-ad-vs-lokalne.md`](09-konto-ad-vs-lokalne.md) | do zrobienia |
| ZAD-10 | CI „zero glow”, dostępność, szablon zadania i domknięcie dokumentacji | M6–M8 | P1 | ZAD-01 | [`10-ci-dokumentacja-dostepnosc.md`](10-ci-dokumentacja-dostepnosc.md) | do zrobienia |
| ZAD-11 | Automatyczne zrzuty UI (100/125/150% DPI) z realnego klienta WPF w GitHub Actions | M6 | P1 | ZAD-01, ZAD-06 | [`11-zrzuty-ui-ci.md`](11-zrzuty-ui-ci.md) | **zrobione** |

**Kolejność sugerowana:** ZAD-01 + ZAD-06 (równolegle, bezpieczeństwo i fundament UI) →
ZAD-02 / ZAD-03 / ZAD-04 → ZAD-05 / ZAD-09 → ZAD-07 → ZAD-08 → ZAD-10 na koniec każdego etapu.

## Etapy

| Etap | Cel | Zadania |
|---|---|---|
| **M6 — System projektowy UI** | jedno, spójne, płaskie UI bez poświaty; ikony i formularze | ZAD-01, ZAD-02, ZAD-03, ZAD-10 (część) |
| **M7 — Topologia pracowni** | fizyczne sale + diagram połączeń w stylu Packet Tracer | ZAD-04, ZAD-05 |
| **M8 — Dostęp, pliki i tożsamość** | uczniowie bez dostępu, tylko odczyt `/pepsi`, konto AD vs profil lokalny | ZAD-06, ZAD-07, ZAD-08, ZAD-09 |

M6–M8 **nie zastępują M5 (wdrożenie)** z [`../roadmapa.md`](../roadmapa.md). M5 pozostaje
pierwszym krokiem przed produkcją; nowe etapy można realizować równolegle po M4, ale ich
wypuszczenie wymaga domknięcia kwestii RODO/DPIA z M5.

## Decyzje (podjęte 2026-11-26)

Wszystkie D1–D10 zostały podjęte przez zamawiającego w formie ABC. **Jedyne źródło prawdy:**
[`../notatki-zamawiajacego-2026-11-26.md`](../notatki-zamawiajacego-2026-11-26.md) §4
(odpowiedzi + interpretacja techniczna) oraz §4.1 (trzy drobne doprecyzowania).

| Decyzja | Odpowiedź | Skutek w zadaniach |
|---|---|---|
| D1 — sale | C: listę sal robi admin | ZAD-04: `topology.json` od admina, bez zgadywania |
| D2 — „zdj” | gotowe rysunki urządzeń + diagram ułożenia + diagram połączeń | ZAD-05: grafiki + dwa diagramy |
| D3 — `/pepsi` | dysk sieciowy → UNC | ZAD-07: `Root` jako UNC, prawa konta usługi |
| D4 — kto widzi pliki | wszystkie role aplikacji; zapis wydzielony poza aplikację | ZAD-06/07/08: `files.read` dla Administrator/Teacher/ReadOnly, v1 bez zapisu |
| D5 — konto AD | A: domena + profil lokalny na stacji | ZAD-09: `accountScope` bez zgadywania |
| D6 — pliki ucznia | uczeń nie ma dostępu do aplikacji | ZAD-08/09: brak danych o plikach ucznia w UI |
| D7 — topologia | C: każdy zalogowany | ZAD-04/05: `topology.read` dla wszystkich ról, bez filtra `classroomScope` |
| D8 — ikony | B: dodaj `SvgIcons 0.1.10` | ZAD-02: paczka + zielone CI, fallback `Path` przy porażce |
| D9 — font | A: `Segoe UI` | ZAD-01: krój systemowy, bez bundlingu |
| D10 — uczniowie | A: deny overrides | ZAD-06: odmowa ma pierwszeństwo nad rolą |

Trzy drobne doprecyzowania z §4.1 notatek (źródło grafik, dokładny UNC + konto usługi, lista sal)
**nie blokują** rozpoczęcia prac — zostawiamy po jednym, jawnym punkcie konfiguracji.

Zgłoszenia GitHub #5–#15 zostały zamknięte przez zamawiającego 2026-11-26; backlog żyje w
`docs/zadania/` i nie tworzymy nowych zgłoszeń bez wyraźnej prośby.

## Definicja ukończenia całego backlogu

- [ ] Wszystkie kryteria Given/When/Then z plików zadań spełnione, z dowodem (test lub zrzut ekranu).
- [ ] `dotnet build -c Release` i `dotnet test` zielone na `windows-latest`.
- [ ] `docs/api-websocket.md`, `docs/model-danych.md`, `docs/uprawnienia-i-dane-osobowe.md`,
  `docs/architektura.md` i `docs/roadmapa.md` zaktualizowane o nowe byty, komunikaty i uprawnienia.
- [ ] Brak nowych sekretów/tokenów w repo; produkcja nadal `wss` + Windows/Kerberos (ADR-0007).
- [ ] `docs/ui-design-system.md` §5 odhaczona dla każdego nowego widoku.
- [ ] Statusy zadań uzupełnione w tabeli powyżej (`zrobione` + link do PR).
