# ADR-0004: Model świeżości i kompletności zamiast flagi online

**Status:** przyjęte
**Data:** 2026-11-26

## Kontekst

README wielokrotnie podkreśla, że aplikacja nie powinna sugerować aktualności danych, jeśli serwer nie może jej potwierdzić, oraz że brak połączenia z komputerem nie oznacza braku historii logowań. Typowy błąd takich narzędzi to pojedyncza flaga `isOnline` bez czasu i bez informacji o źródle.

## Decyzja

Każda odpowiedź zawierająca dane niesie `DataFreshnessDto`:

- `source` — ludzka nazwa źródła,
- `observedAtUtc` — kiedy dane zaobserwowano (może być `null`),
- `isStale` — czy wiek przekroczył próg konfiguracyjny,
- `completeness` — `complete` / `partial` / `unavailable`,
- `note` — wyjaśnienie dla człowieka.

Dodatkowo `ComputerDto.status` ma wartość `unknown` różną od `offline`, a `statusObservedAtUtc` jest osobnym polem.

## Alternatywy

| Alternatywa | Dlaczego odrzucona |
|---|---|
| `isOnline: bool` | nie odróżnia „nie sprawdzono” od „sprawdzono i nie odpowiada”; brak czasu obserwacji |
| Sam znacznik czasu bez kompletności | nie pokazuje, że część stacji nie była dostępna |
| Status wyliczany w kliencie z czasu | klient nie zna progów ani źródła; serwer jest jedynym miejscem, które wie |

## Konsekwencje

**Pozytywne:** UI może uczciwie napisać „dane z 10:42, częściowe”; testy pilnują, że `unknown ≠ offline`; ten sam model obsługuje AD, stan i logi.

**Negatywne:** więcej pól w protokole; każdy handler musi policzyć metadane (rozwiązane przez `FreshnessService`); klient musi formatować (rozwiązane przez `DisplayFormat`).

**Neutralne:** `completeness` jest rozszerzalne (np. `stale`), ale lepiej trzymać się trzech wartości i wyrażać szczegóły w `note`.

## Rozszerzenie — M2 (2026-11-26)

`completeness` i `note` stały się nośnikiem kompletności źródeł historii logowań:
brak dostępu do dziennika na 2 z 6 stacji → `partial` z liczbą stacji, zapytanie
przed początkiem 30-dniowej retencji → `partial` z jawną datą graniczną, wyłączony
kolektor → `unavailable` z powodem. To rozszerza pierwotną zasadę, ale nie zmienia
`DataFreshnessDto`: wciąż obowiązują `source`, `observedAtUtc`, `isStale`,
`completeness` i `note`. Szczegóły: ADR-0006.
