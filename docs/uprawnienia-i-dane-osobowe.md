# Uprawnienia, audyt i dane osobowe

## 1. Zasada nadrzędna

Serwer jest jedynym miejscem egzekwowania uprawnień. Klient może ukrywać akcje, ale **ukrycie w UI nie jest zabezpieczeniem** — każde żądanie jest sprawdzane w `MessageRouter` przez `CapabilityGuard`.

## 2. Role i uprawnienia

| Uprawnienie | Administrator | Nauczyciel | ReadOnly |
|---|---|---|---|
| `classrooms.read` | ✔ | ✔ | ✔ |
| `computers.read` | ✔ | ✔ | ✔ |
| `logins.read` | ✔ | ✔ | ✔ |
| `users.read` | ✔ | ✔ | ✘ |
| `users.write` | ✔ | ✘ | ✘ |
| `computers.control` | ✔ | ✘ | ✘ |
| `audit.read` | ✔ | ✘ | ✘ |

Definicje w kodzie: `src/ZseAdmin.Server/Security/Capability.cs` (stałe wskazują na `ZseAdmin.Contracts.Capabilities`) i `RoleCatalog.cs`. Testy pilnują, że Nauczyciel nie dostaje `users.write` ani `computers.control`.

**Po co trzy role, a nie dwie:** nauczyciel potrzebuje historii logowań (kto siedział przy stacji), ale nie powinien widzieć całej kartoteki kont ani niczego zmieniać. `ReadOnly` to profil dla dyżurki/sekretariatu — widok bez danych o kontach.

## 3. Uwierzytelnianie

| Etap | Mechanizm | Ryzyko |
|---|---|---|
| Development (ten kod) | tokeny w `appsettings.Development.json`; baza `appsettings.json` ich nie zawiera | tylko praca lokalna |
| Produkcja (zaimplementowane, etap M3) | Windows/Kerberos (`Negotiate`) na Kestrel albo IIS Windows Authentication + grupy AD → rola, świeże przy każdym połączeniu | wymaga SPN (Kestrel) albo roli IIS i zaufania domeny |

**Mapowanie grup → role (konfigurowalne w `Security:GroupMapping`):**

| Grupa AD | Rola |
|---|---|
| `ZSE-Admin-Administratorzy` | Administrator |
| `ZSE-Admin-Nauczyciele` | Nauczyciel |
| `ZSE-Admin-Podglad` | ReadOnly |

Użytkownik spoza wszystkich grup nie połączy się (401). Nieosiągalne AD przy sprawdzaniu grup → 503 (a nie 401 i nie „puste uprawnienia"). Konto usługi wymaga prawa czytania konstruowanych atrybutów (`tokenGroups`) — bez niego serwer wraca do `memberOf` (bez zagnieżdżenia) i loguje ostrzeżenie.

**Bramka wdrożeniowa (fail-fast):** `SecurityConfigurationValidator` zatrzymuje serwer przy starcie, gdy na produkcji (`Environment=Production`) wykryje: tryb inny niż `ActiveDirectory`, niepustą sekcję `Security:Tokens`, `RequireSecureTransport=false`, `UseMockData=true` albo LDAP bez SSL. Tokeny służą wyłącznie do pracy lokalnej.

Wymagania dodatkowe przy wdrożeniu:

- transport tylko `wss` z certyfikatem,
- konto usługi serwera: odczyt wybranych OU, „Event Log Readers” na stacjach, **bez** praw administratora domeny
  (etap M1: `ZseAdmin:Directory:ServiceAccountUser` lub konto procesu; `tokenGroups` wymaga prawa
  czytania konstruowanych atrybutów — bez niego serwer wraca do `memberOf` i traci zagnieżdżenie grup),
- logowanie do serwera tylko z podsieci administracyjnej.

## 4. Audyt operacji — zaimplementowane (etapy M3/M4)

Każda operacja zmieniająca stan zapisuje: kto, kiedy, na jakim obiekcie, jaka była wartość przed i po, z jakiego adresu IP, z jakim wynikiem. Dotyczy to: restartu i blokady stacji, resetu hasła, dodania do grupy, a także odrzuconych prób połączenia `/ws` (`auth.connect` / `denied`).

- Magazyn: `AuditEntry { Id, TimestampUtc, Actor, Role, Capability, Target, Before, After, Result, RemoteAddress }` w `data/audit.jsonl`, retencja 12 miesięcy, przycinanie przy starcie.
- Wpis powstaje zawsze — także przy nieudanej operacji (`result: failed`) i przy odmowie przed upgrade'em.
- **Hasła nigdy nie trafiają do audytu** (`Before`/`After` = `(hasło utajnione)` / `(hasło zmienione)`; komunikaty błędów są oczyszczane z hasła).
- Błąd zapisu audytu wywraca operację — serwer nie melduje „sukcesu” bez rozliczalności.
- Podgląd: zakładka „Audyt” w kliencie (Administrator, filtr po obiekcie) albo `audit.query` w `debug.html` (`target`, `capability`, `take` ≤ 200).

## 5. Dane osobowe (RODO)

Narzędzie przetwarza dane uczniów i pracowników: login, nazwę, przynależność do klas/grup oraz **historię logowań na stacjach**. To dane osobowe, a historia aktywności może być uznana za profilowanie — wymaga podstawy prawnej i oceny skutków.

**Do ustalenia przed wdrożeniem (poza kodem):**

1. **Podstawa prawna** — najpewniej art. 6 ust. 1 lit. c/e RODO (obowiązek ustawowy, zadanie publiczne — statut szkoły, nadzór pedagogiczny) oraz regulamin pracowni. Nie „zgoda ucznia”.
2. **Ocena skutków (DPIA)** — wskazana, bo monitoring korzystania ze sprzętu przez osoby, w tym niepełnoletnie, jest systematyczną obserwacją.
3. **Minimalizacja** — zbierać tylko typy zdarzeń potrzebne do celu (4624/4634/4625), bez procesów, bez treści, bez przechwytywania ekranu.
4. **Retencja** — 30 dni w aplikacji (wartość do potwierdzenia), potem automatyczne usunięcie; dzienniki systemowe zostają poza aplikacją.
5. **Dostęp** — nauczyciel widzi logowania w kontekście prowadzonych zajęć; nie powinien widzieć całej historii szkoły. Etap M3: ograniczenie zakresu do pracowni przypisanych nauczycielowi.
6. **Informacja** — regulamin pracowni i klauzula informacyjna muszą mówić, że logowania są rejestrowane i w jakim celu.
7. **Prawa osób** — możliwość wyjaśnienia wpisu (ktoś mógł użyć cudzego konta), ścieżka do inspektora danych.

**Czego aplikacja nie może robić bez nowej decyzji:** śledzić aktywności poza logowaniem (aplikacje, strony, czas bezczynności), udostępniać historii logowań osobom trzecim, wysyłać danych poza sieć szkolną.

## 6. Zasady bezpieczeństwa w kodzie

- Brak endpointów przyjmujących dowolne zapytania LDAP — filtry budowane z konkretnych, walidowanych parametrów (docelowo escaping wartości: `(`, `)`, `*`, `\`).
- Limity: `take` przycinane do 500, rozmiar wiadomości 1 MiB, paginacja LDAP z limitem.
- Token porównywany w czasie stałym (`CryptographicOperations.FixedTimeEquals`).
- Szczegóły błędów wewnętrznych nie trafiają do klienta (tylko `internal` + komunikat ogólny), pełny wyjątek idzie do logu serwera.
- Sekrety poza repozytorium: `appsettings.Local.json` w `.gitignore`, w produkcji zmienne środowiskowe lub magazyn sekretów.

## 7. Lista kontrolna przed produkcją

- [x] Uwierzytelnianie Windows/grupy AD zamiast tokenów (M3 — do włączenia na produkcji: `Authentication:Mode=ActiveDirectory`; w dev zostają tokeny).
- [x] `wss` z certyfikatem, firewall ograniczony do podsieci (`RequireSecureTransport=true` — kod odrzuca nie-`wss` już przed upgrade'em; firewall po stronie infrastruktury).
- [ ] DPIA i klauzula informacyjna zatwierdzone.
- [x] Retencja logowań wdrożona (30 dni, `FileLoginEventStore.PruneAsync` przy każdym cyklu) — do przetestowania na pilocie, że dane faktycznie znikają.
- [ ] Konto usługi z minimalnymi prawami, zweryfikowane testem negatywnym.
- [x] Ograniczenie widoczności historii dla nauczycieli (M3 — `teachers` w `classrooms.json`, odmowa `forbidden` poza zakresem).
- [x] Audyt operacji zapisu (M4 — `audit.jsonl` + podgląd w kliencie i przez `audit.query`).
- [ ] Plan na awarię DC: aplikacja pokazuje „dane z godziny X”, nie puste ekrany — mechanika jest (`freshness`), plan operacyjny do spisania.
