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

Definicje w kodzie: `src/ZseAdmin.Server/Security/Capability.cs` i `RoleCatalog.cs`. Testy pilnują, że Nauczyciel nie dostaje `users.write` ani `computers.control`.

**Po co trzy role, a nie dwie:** nauczyciel potrzebuje historii logowań (kto siedział przy stacji), ale nie powinien widzieć całej kartoteki kont ani niczego zmieniać. `ReadOnly` to profil dla dyżurki/sekretariatu — widok bez danych o kontach.

## 3. Uwierzytelnianie

| Etap | Mechanizm | Ryzyko |
|---|---|---|
| MVP (ten kod) | statyczne tokeny w `appsettings.json`, rola przypisana do tokenu | token = hasło bez wygasania, brak rozliczalności per osoba |
| Produkcja (wymagane) | Windows/Kerberos (`Negotiate`) albo mapowanie grup AD → rola | wymaga konfiguracji IIS/HTTP.sys i zaufania domeny |
| Docelowo | rola wyliczana z przynależności do grup AD (`ZSE-Admin-Administratorzy`, `ZSE-Admin-Nauczyciele`) | brak duplikacji kont |

**Bramka wdrożeniowa:** aplikacja z tokenami nie może trafić na produkcję. Tokeny służą wyłącznie do pracy lokalnej — token w `appsettings.json` jest widoczny dla każdego, kto ma dostęp do pliku.

Wymagania dodatkowe przy wdrożeniu:

- transport tylko `wss` z certyfikatem,
- konto usługi serwera: odczyt wybranych OU, „Event Log Readers” na stacjach, **bez** praw administratora domeny,
- logowanie do serwera tylko z podsieci administracyjnej.

## 4. Audyt operacji

Każda operacja zmieniająca stan (etap M3+) musi zapisać: kto, kiedy, na jakim obiekcie, jaka była wartość przed i po, z jakiego adresu IP, z jakim wynikiem. MVP nic nie zmienia, więc audyt dotyczy tylko zdarzeń bezpieczeństwa: nieudanego uwierzytelnienia, odmowy uprawnienia i zerwania sesji — te są już logowane.

Minimalny rejestr docelowy: tabela `AuditEntry { Id, TimestampUtc, Actor, Role, Capability, Target, Before, After, Result, RemoteAddress }`, retencja 12 miesięcy.

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

- [ ] Uwierzytelnianie Windows/grupy AD zamiast tokenów.
- [ ] `wss` z certyfikatem, firewall ograniczony do podsieci.
- [ ] DPIA i klauzula informacyjna zatwierdzone.
- [ ] Retencja wdrożona i przetestowana (dane faktycznie znikają).
- [ ] Konto usługi z minimalnymi prawami, zweryfikowane testem negatywnym.
- [ ] Ograniczenie widoczności historii dla nauczycieli.
- [ ] Audyt operacji zapisu (jeśli wdrożono M3).
- [ ] Plan na awarię DC: aplikacja pokazuje „dane z godziny X”, nie puste ekrany.
