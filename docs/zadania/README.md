# Backlog zadań dla deweloperów (2026-11-26)

**Status:** propozycja do zaplanowania. Żadne zadanie nie jest jeszcze rozpoczęte — ten katalog
zawiera specyfikacje do implementacji, nie implementację.
**Materiał wejściowy:** [`../notatki-zamawiajacego-2026-11-26.md`](../notatki-zamawiajacego-2026-11-26.md)
**Obowiązujący styl:** [`../ui-design-system.md`](../ui-design-system.md) (spójne UI, zero glow)
**Plan etapów:** [`../roadmapa.md`](../roadmapa.md) (M6–M8)

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

| ID | Zadanie | Etap | Priorytet | Zależy od | Plik |
|---|---|---|---|---|---|
| ZAD-01 | System projektowy UI: tokeny, `ResourceDictionary`, refactor „zero glow” | M6 | P0 | — | [`01-design-system.md`](01-design-system.md) |
| ZAD-02 | Ikony: weryfikacja `SvgIcons 0.1.10` + ADR-0009 + fallback `Path` | M6 | P0 | ZAD-01 | [`02-ikony-svg.md`](02-ikony-svg.md) |
| ZAD-03 | Formularze dwukolumnowe, liczniki znaków, przerywana strefa upuszczania | M6 | P1 | ZAD-01, ZAD-02 | [`03-formularze-dwukolumnowe.md`](03-formularze-dwukolumnowe.md) |
| ZAD-04 | Model topologii: sale → pracownie → węzły/łącza, `topology.json`, DTO | M7 | P1 | — | [`04-model-topologii.md`](04-model-topologii.md) |
| ZAD-05 | Widok topologii (diagram w stylu Packet Tracer) | M7 | P1 | ZAD-01, ZAD-02, ZAD-04 | [`05-widok-topologii.md`](05-widok-topologii.md) |
| ZAD-06 | Uczniowie bez dostępu: deny-by-default na serwerze + audyt odmów | M8 | P0 | — | [`06-dostep-uczniow.md`](06-dostep-uczniow.md) |
| ZAD-07 | `/pepsi` — backend tylko do odczytu (`files.list`, `files.read`) | M8 | P1 | ZAD-06, D3, D4 | [`07-pliki-pepsi-backend.md`](07-pliki-pepsi-backend.md) |
| ZAD-08 | `/pepsi` — przeglądarka plików w UI i strefa upuszczania | M8 | P2 | ZAD-01, ZAD-03, ZAD-07 | [`08-pliki-pepsi-ui.md`](08-pliki-pepsi-ui.md) |
| ZAD-09 | Konto AD vs profil/konto lokalne na stacji w historii logowań | M8 | P1 | D5, D6 | [`09-konto-ad-vs-lokalne.md`](09-konto-ad-vs-lokalne.md) |
| ZAD-10 | CI „zero glow”, dostępność, szablon zadania i domknięcie dokumentacji | M6–M8 | P1 | ZAD-01 | [`10-ci-dokumentacja-dostepnosc.md`](10-ci-dokumentacja-dostepnosc.md) |

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

## Decyzje blokujące

Z [`../notatki-zamawiajacego-2026-11-26.md`](../notatki-zamawiajacego-2026-11-26.md) §4:

| Decyzja | Blokuje | Krótko |
|---|---|---|
| D1 — sale i ich powiązanie z pracowniami | ZAD-04, ZAD-05 | kształt `topology.json` |
| D2 — diagram czy też zdjęcia szaf/patch paneli | ZAD-05 (załączniki) | zakres widoku topologii |
| D3 — czym jest `/pepsi` (lokalnie / UNC / mount) | ZAD-07, ZAD-08 | konfiguracja i uprawnienia FS |
| D4 — kto widzi listing plików | ZAD-06, ZAD-07 | `files.read` w `RoleCatalog` |
| D5 — profil domenowy czy konto lokalne na stacji | ZAD-09 | `accountScope` |
| D6 — czy UI wskazuje pliki ucznia | ZAD-08, ZAD-09 | RODO, brak mapowania |
| D7 — czy nauczyciel widzi topologię | ZAD-04, ZAD-05 | `topology.read` + `classroomScope` |
| D8 — weryfikacja `SvgIcons` i zgoda na zależność NuGet | ZAD-02 | ADR-0009, ADR-0001 |
| D9 — bundling fontu Inter / Plus Jakarta Sans | ZAD-01 | licencja i rozmiar artefaktu |
| D10 — deny overrides dla grup uczniowskich | ZAD-06 | pierwszeństwo odmowy |

Dopóki decyzja nie jest podjęta, zadanie realizujemy w wariancie „propozycja domyślna” z §4
notatek i zostawiamy jawny punkt do zmiany (konfiguracja / jeden adapter), bez zgadywania w kodzie.

## Definicja ukończenia całego backlogu

- [ ] Wszystkie kryteria Given/When/Then z plików zadań spełnione, z dowodem (test lub zrzut ekranu).
- [ ] `dotnet build -c Release` i `dotnet test` zielone na `windows-latest`.
- [ ] `docs/api-websocket.md`, `docs/model-danych.md`, `docs/uprawnienia-i-dane-osobowe.md`,
  `docs/architektura.md` i `docs/roadmapa.md` zaktualizowane o nowe byty, komunikaty i uprawnienia.
- [ ] Brak nowych sekretów/tokenów w repo; produkcja nadal `wss` + Windows/Kerberos (ADR-0007).
- [ ] `docs/ui-design-system.md` §5 odhaczona dla każdego nowego widoku.
- [ ] Statusy zadań uzupełnione w tabeli powyżej (`zrobione` + link do PR).
