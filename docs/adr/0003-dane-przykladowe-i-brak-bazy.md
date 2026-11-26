# ADR-0003: Dane przykładowe w MVP, bez bazy danych

**Status:** przyjęte
**Data:** 2026-11-26

## Kontekst

Specyfikacja nie rozstrzyga, czy aplikacja przechowuje własne dane, czy jest wyłącznie warstwą prezentacji nad AD, stacjami i dziennikami. Wdrożenie bazy przed ustaleniem retencji i modelu uprawnień byłoby przedwczesne — a RODO sugeruje raczej minimalizację niż kopiowanie historii logowań.

## Decyzja

- MVP działa bez własnej bazy: dane pobierane są z interfejsów `IClassroomDirectory`, `IComputerDirectory`, `ILoginLog`, `IAdDirectory`.
- W tej wersji jedyne implementacje to `Mock*` (dane w pamięci, deterministyczne), przełączane przez DI.
- Stan, który musi przetrwać restart (cache ostatniej obserwacji), będzie plikiem/relacyjną bazą dopiero w M2, gdy poznamy wolumen i retencję.

## Alternatywy

| Alternatywa | Dlaczego odrzucona na tym etapie |
|---|---|
| SQLite od razu | wymusza schemat przed decyzjami o retencji i zakresie danych; trudniejsze testy |
| PostgreSQL/SQL Server | nadmiar dla narzędzia czytającego AD; dodatkowa infrastruktura do utrzymania w szkole |
| Trwały cache w pamięci procesu | tracone dane po awarii; akceptowalne tylko do pilotażu |

## Konsekwencje

**Pozytywne:** szybki start, brak migracji, brak danych osobowych w nowym miejscu, testy bez bazy, łatwa podmiana źródła.

**Negatywne:** po restarcie serwera historia logowań jest odtwarzana od nowa ze źródła (jeśli źródło jej nie ma — jest niedostępna); wydajność zależy od LDAP i dzienników; brak możliwości zapytań analitycznych.

**Neutralne:** interfejsy są zaprojektowane pod docelowe źródła, więc dodanie magazynu w M2 nie wymaga zmian w protokole.
