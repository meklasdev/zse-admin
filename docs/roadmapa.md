# Roadmapa

Każdy etap ma kryteria akceptacji w formacie Given/When/Then — bez nich nie da się obiektywnie zamknąć zadania (to była największa luka ocenianej specyfikacji).

## M0 — Szkielet (zrobione w tym commicie)

**Zakres:** kontrakty protokołu, serwer WebSocket z danymi przykładowymi, klient WPF z trzema listami, metadane świeżości, role i uprawnienia, testy jednostkowe, CI, dokumentacja.

**Kryteria akceptacji:**

- Mając uruchomiony serwer, gdy otworzę `/healthz`, to widzę `status: ok` i liczbę sesji.
- Mając konsolę `debug.html`, gdy wyślę `computers.list` z tokenem administratora, to dostaję listę 17 komputerów z `freshness`.
- Mając rolę Nauczyciel, gdy wyślę `users.get`, to dostaję odpowiedź (uprawnienie `users.read`), a gdy wyślę nieznany typ — `invalidRequest`.
- Mając rolę ReadOnly, gdy wyślę `users.get`, to dostaję `forbidden`.
- Gdy serwer działa 30 s, to klient otrzymuje co najmniej jedno `event.computer.status` (o ile stan się zmienił).
- Gdy zerwę połączenie, to klient przechodzi w „ponowne łączenie”, a po powrocie pobiera pełny stan.

## M1 — Prawdziwe AD (odczyt)

**Zakres:** adapter LDAP (`System.DirectoryServices.Protocols`), `classrooms.json`, mapowanie OU → pracownia, komputery i konta z AD, grupy przez `tokenGroups`, paginacja i limity.

**Kryteria akceptacji:**

- Gdy w AD jest nowy komputer w OU pracowni, to pojawia się na liście po odświeżeniu, bez restartu serwera.
- Gdy komputer jest poza OU z konfiguracji, to widzę go w „poza pracownią”, a nie znika.
- Gdy OU jest przypisane do dwóch pracowni, to serwer nie startuje i loguje błąd konfiguracji.
- Gdy konto ma 300 grup, to widzę wszystkie (bez ucięcia), a odpowiedź trwa < 2 s.
- Gdy konto AD nie istnieje, to `users.get` zwraca `notFound`.

**Ryzyka:** uprawnienia konta usługi, opóźnienie `lastLogonTimestamp`, rozmiar odpowiedzi LDAP.

## M2 — Historia logowań

**Zakres:** zbieranie zdarzeń 4624/4634/4625 (WEF lub odczyt zdalny), bufor lokalny, filtry komputer/użytkownik/czas, kompletność i zakres, retencja 30 dni.

**Kryteria akceptacji:**

- Gdy uczeń zaloguje się na stacji, to zdarzenie jest widoczne w aplikacji w ciągu 2 minut.
- Gdy 2 z 6 stacji nie mają dostępu do dziennika, to odpowiedź ma `completeness = partial` i notę z liczbą stacji.
- Gdy pytam o zakres poza retencją, to widzę jawny komunikat o zakresie, nie pustą listę bez wyjaśnienia.
- Gdy minie 30 dni, to zdarzenia są usuwane (test na podstawie znaczników czasu).

**Ryzyka:** przewijanie dzienników, obciążenie stacji, RODO (podstawa prawna, DPIA).

## M3 — Bezpieczeństwo produkcyjne

**Zakres:** uwierzytelnianie Windows/Kerberos lub mapowanie grup AD, `wss`, ograniczenie widoczności historii dla nauczycieli, audyt operacji, minimalne prawa konta usługi.

**Kryteria akceptacji:**

- Gdy użytkownik nie należy do grupy `ZSE-Admin-Nauczyciele`, to nie może się połączyć.
- Gdy nauczyciel prosi o logowania z pracowni, w której nie ma zajęć, to dostaje `forbidden`.
- Gdy zmieniono dane (etap M4), to w audycie jest wpis z aktorem, celem i wartością przed/po.
- Tokeny z `appsettings.json` są wyłączone, a połączenie bez `wss` odrzucone.

## M4 — Akcje na stacjach i zapis do AD

**Zakres:** restart/blokada stacji, reset hasła, dodanie do grupy — każde z potwierdzeniem i audytem.

**Kryteria akceptacji:**

- Gdy rola nie ma `computers.control`, to akcja jest niewidoczna w UI i odrzucona serwerowo.
- Gdy akcja się nie powiedzie, to UI pokazuje błąd źródła, a nie „sukces”.
- Każda zmiana ma wpis audytowy i jest widoczna w szczegółach obiektu.

## M5 — Wdrożenie

**Zakres:** publikacja jako usługa Windows, certyfikaty, firewall, dokumentacja dla szkoły, szkolenie, pilotaż w jednej pracowni.

**Kryteria akceptacji:**

- Serwer startuje automatycznie po restarcie maszyny.
- Pilotaż w jednej pracowni przez tydzień bez utraty danych (kompletność ≥ 95%).
- Użytkownicy końcowi (1 nauczyciel, 1 administrator) wykonują zadania z listy bez pomocy.

## Backlog decyzji (z README, sekcja „Co doprecyzować”)

| Decyzja | Etap | Właściciel |
|---|---|---|
| Definicja pracowni i mapowanie OU | M1 | administrator IT |
| Źródło i retencja logów | M2 | administrator IT + DPO |
| Typy logowań i aktywne sesje | M2 | dyrekcja |
| Częstotliwość stanu i próg offline | M1 | administrator IT |
| Role i widoczność danych | M3 | dyrekcja + DPO |
| Sposób sygnalizowania reconnectu | M0 (zrobione) | — |
| Ograniczenie retencji i widoczności historii | M2/M3 | DPO |
| Skala: liczba stacji i klientów | M1 | administrator IT |
