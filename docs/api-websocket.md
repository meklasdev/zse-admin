# Protokół WebSocket

Wersja protokołu: **1** (stała `ProtocolJson.ProtocolVersion`, wysyłana w `welcome`).

## 1. Transport i uwierzytelnianie

- Endpoint: `/ws` na serwerze ZSE-Admin (`ws://host:5080/ws`, w produkcji `wss://`).
- Podprotokół: brak (czysty tekst). Wiadomości binarne są ignorowane.
- Limit wiadomości: 1 MiB; przekroczenie zamyka sesję.
- Keep-alive: ping WebSocket co 20 s po obu stronach.

Uwierzytelnianie zależy od `Security:Mode`:

| Tryb | Mechanizm | Kiedy |
|---|---|---|
| `Token` (development) | token w nagłówku `X-ZseAdmin-Token: <token>` albo `?access_token=<token>` (konsola debug) | lokalnie, CI |
| `Windows` (produkcja) | Negotiate/Kerberos (`HttpContext.User` z IIS/HTTP.sys/Kestrel), rola z grup AD | wyłącznie `wss` |

Brak/błędny token lub brak grupy `ZSE-Admin-*` → HTTP 401, sesja nie powstaje.
Gdy `Security:RequireSecureTransport = true`, połączenie bez HTTPS → HTTP 403 z
`{"error":"Wymagane połączenie wss (HTTPS)."}` — również przed upgrade'em.
Odmowy są zapisywane w audycie (`auth.rejected`, `transport.rejected`).
Po upgrade'ie serwer nie przyjmuje już zmian tożsamości — reconnect wymaga nowego uwierzytelnienia.
W trybie Windows token z `appsettings.json` jest ignorowany, a produkcja nie startuje,
jeśli `Security:Tokens` nie jest puste.

> Uwaga: tokeny to rozwiązanie wyłącznie developerskie — patrz `docs/uprawnienia-i-dane-osobowe.md`.

## 2. Koperta

Każda wiadomość w obie strony ma ten sam kształt:

```json
{
  "type": "computers.list",
  "requestId": "a1b2c3",
  "payload": { }
}
```

| Pole | Wymagane | Znaczenie |
|---|---|---|
| `type` | tak | nazwa wiadomości (stałe w `MessageTypes`) |
| `requestId` | tylko żądania | korelacja odpowiedzi; klient generuje, serwer odsyła |
| `payload` | nie | treść zależna od `type`; pomijane, gdy brak |

**Reguła odpowiedzi:** serwer odpowiada tym samym `type`, co żądanie, z tym samym `requestId`. Wyjątki: `welcome`, `error` i zdarzenia `event.*` (bez `requestId`).

Serializacja: camelCase, enumy jako stringi camelCase (`"online"`, `"logon"`, `"partial"`), pola `null` pomijane.

## 3. Katalog wiadomości

### Serwer → klient

| `type` | Kiedy | Payload |
|---|---|---|
| `welcome` | natychmiast po połączeniu | `WelcomeResponse` |
| `error` | błąd żądania lub protokołu | `ErrorDto` |
| `event.computer.status` | zmiana stanu komputera (push) | `ComputerStatusChangedEvent` |
| `event.logins.new` | nowe zdarzenia logowań (M2, push) | `LoginEventsChangedEvent` |

`event.logins.new` nie ma `requestId`. Serwer filtruje listę per sesja: nauczyciel dostaje
wyłącznie zdarzenia ze swoich pracowni (`classroomScope`). Po reconnect klient i tak wykonuje
pełny `logins.query`.

`WelcomeResponse`:

```json
{
  "type": "welcome",
  "payload": {
    "serverVersion": "0.1.0",
    "protocolVersion": 1,
    "sessionId": "9f1c…",
    "userName": "k.mazur",
    "role": "Teacher",
    "capabilities": ["classrooms.read", "computers.read", "logins.read", "users.read"],
    "serverTimeUtc": "2026-11-25T18:12:03+00:00",
    "classroomScope": ["prac-101"]
  }
}
```

### Klient → serwer

| `type` | Uprawnienie | Payload żądania | Payload odpowiedzi |
|---|---|---|---|
| `ping` | — | brak | `PongResponse` |
| `classrooms.list` | `classrooms.read` | brak | `ClassroomsResponse` |
| `computers.list` | `computers.read` | `ComputerListRequest` | `ComputersResponse` |
| `logins.query` | `logins.read` | `LoginQueryRequest` | `LoginsResponse` |
| `users.get` | `users.read` | `UserLookupRequest` | `UserResponse` |
| `audit.query` | `audit.read` | `AuditQueryRequest` | `AuditQueryResponse` |
| `action.prepare` | `computers.control` lub `users.write` | `ActionPrepareRequest` | `ActionPrepareResponse` |
| `action.execute` | jw. + ważny token potwierdzenia | `ActionExecuteRequest` | `ActionExecuteResponse` |
| `topology.get` (ZAD-04) | `topology.read` — wszystkie role aplikacji (D7) | brak | `TopologyResponse` (po ZAD-04) |
| `files.list` (ZAD-07) | `files.read` — wszystkie role aplikacji (D4) | `FileListRequest` (po ZAD-07) | `FileListResponse` (po ZAD-07) |
| `files.stat` (ZAD-07) | `files.read` (D4) | `FileStatRequest` (po ZAD-07) | `FileStatResponse` (po ZAD-07) |

**Fail-close uprawnień (ZAD-06/D10):** serwer najpierw sprawdza, czy `type` jest w katalogu
protokołu, potem czy ma przypisane uprawnienie, a dopiero potem wykonuje obsługę. Typ z katalogu
bez wpisu w `MessageCapabilities` → `forbidden`; rola bez uprawnienia → `forbidden` bez żadnych
danych; typ spoza katalogu → `invalidRequest`. `topology.get`, `files.list` i `files.stat` są
zarezerwowane w wersji 1: do czasu ZAD-04/ZAD-07 rola uprawniona dostaje `invalidRequest`,
a rola bez uprawnienia (np. konto uczniowskie) `forbidden`.

`LoginQueryRequest` ma dodatkowe opcjonalne `classroomId` / `classroomIds`. Serwer **nadpisuje**
je dla roli Teacher — klient nie może poszerzyć zakresu, a filtr po komputerze spoza zakresu
kończy się `forbidden`. `LoginEventDto.classroomId` pochodzi z inwentarza komputerów.

`ActionPrepareRequest.Action`: `restartComputer`, `lockComputer`, `resetPassword`, `addToGroup`.
`action.prepare` nic nie zmienia: zwraca `before`, ostrzeżenia i jednorazowy `confirmationToken`
(ważny 120 s, związany z aktorem i celem). `action.execute` wymaga `confirm: true`; po udanym
wykonaniu zwraca `after` i `auditId`. Błąd źródła zawsze przychodzi jako `error`.
`temporaryPassword` jest obecne wyłącznie po `resetPassword` i tylko w tej jednej odpowiedzi.

`AuditQueryRequest.Target` zawęża wpisy do konta lub komputera — to samo zapytanie zasila
szczegóły obiektu w UI. `AuditEntryDto` zawiera `actor`, `role`, `capability`, `action`,
`target`, `before`, `after`, `result`, `remoteAddress` i `detail`; hasła są maskowane.

## 4. Przykłady

### 4.1 Lista komputerów w pracowni

```json
{ "type": "computers.list", "requestId": "r-1", "payload": { "classroomId": "prac-101", "includeOffline": true } }
```

```json
{
  "type": "computers.list",
  "requestId": "r-1",
  "payload": {
    "items": [
      {
        "name": "KS-101-01",
        "classroomId": "prac-101",
        "classroomName": "Pracownia 101",
        "ouDistinguishedName": "CN=KS-101-01,OU=PRAC101,OU=Pracownie,DC=zse,DC=local",
        "ipAddress": "10.20.101.11",
        "status": "online",
        "statusObservedAtUtc": "2026-11-25T18:11:40+00:00",
        "lastLogonAtUtc": "2026-11-25T07:42:00+00:00",
        "operatingSystem": "Windows 10 Pro 22H2"
      }
    ],
    "freshness": {
      "source": "Active Directory + sprawdzanie dostępności",
      "observedAtUtc": "2026-11-25T18:11:40+00:00",
      "isStale": false,
      "completeness": "complete"
    }
  }
}
```

### 4.2 Historia logowań

```json
{ "type": "logins.query", "requestId": "r-2", "payload": { "computerName": "KS-101-01", "take": 50 } }
```

```json
{
  "type": "logins.query",
  "requestId": "r-2",
  "payload": {
    "items": [
      {
        "id": "6b0f0f5e-1e1b-4b0d-9b1e-6f4d3b2a1c00",
        "userSamAccountName": "j.kowalski",
        "userDisplayName": "Jan Kowalski",
        "computerName": "KS-101-01",
        "occurredAtUtc": "2026-11-25T07:42:11+00:00",
        "type": "logon",
        "source": "Security log (4624)",
        "classroomId": "prac-101"
      }
    ],
    "totalMatched": 137,
    "freshness": {
      "source": "Dzienniki zabezpieczeń stacji",
      "observedAtUtc": "2026-11-25T18:12:00+00:00",
      "isStale": false,
      "completeness": "partial",
      "note": "Brak dostępu do dziennika na 2 z 6 stacji: KS-101-05, KS-101-06. Retencja: 30 dni."
    }
  }
}
```

### 4.3 Konto AD

```json
{ "type": "users.get", "requestId": "r-3", "payload": { "samAccountName": "j.kowalski" } }
```

```json
{
  "type": "users.get",
  "requestId": "r-3",
  "payload": {
    "user": {
      "samAccountName": "j.kowalski",
      "displayName": "Jan Kowalski",
      "email": "j.kowalski@zse.local",
      "enabled": true,
      "groups": ["Uczniowie", "Klasa-3TI"],
      "ouDistinguishedName": "OU=Uczniowie,OU=ZSE,DC=zse,DC=local",
      "lastLogonAtUtc": "2026-11-24T08:12:00+00:00"
    },
    "freshness": { "source": "Active Directory (użytkownicy)", "observedAtUtc": "2026-11-25T18:11:30+00:00", "isStale": false, "completeness": "complete" }
  }
}
```

### 4.4 Błąd

```json
{
  "type": "error",
  "requestId": "r-4",
  "payload": { "code": "forbidden", "message": "Brak uprawnienia 'users.read' dla roli 'ReadOnly'." }
}
```

### 4.5 Zdarzenie push

```json
{
  "type": "event.computer.status",
  "payload": { "computer": { "name": "KS-101-02", "status": "offline", "statusObservedAtUtc": "2026-11-25T18:13:05+00:00" } }
}
```

```json
{
  "type": "event.logins.new",
  "payload": {
    "items": [
      {
        "id": "c4d0b5cb-0a93-4d62-b7f1-ea8d03f5f2d1",
        "userSamAccountName": "j.kowalski",
        "computerName": "KS-101-01",
        "classroomId": "prac-101",
        "occurredAtUtc": "2026-11-25T18:13:11+00:00",
        "type": "logon",
        "source": "Security log (4624)"
      }
    ]
  }
}
```

### 4.6 Akcja M4 — przygotowanie, wykonanie, audyt

```json
{ "type": "action.prepare", "requestId": "r-5", "payload": { "action": "restartComputer", "target": "KS-101-01", "reason": "Zamrożony ekran" } }
```

```json
{
  "type": "action.prepare",
  "requestId": "r-5",
  "payload": {
    "confirmationToken": "…jednorazowy token…",
    "action": "restartComputer",
    "target": "KS-101-01",
    "capability": "computers.control",
    "description": "Restart stacji KS-101-01",
    "before": "stan: Online; IP: 10.20.101.11; blokada: nie; obserwacja: 2026-11-25 18:11 UTC",
    "warnings": [
      "Operacja zostanie zapisana w audycie z aktorem, celem i wartością przed/po.",
      "Stacja zrestartuje się za 30 s — upewnij się, że nikt nie pracuje."
    ],
    "expiresAtUtc": "2026-11-25T18:15:03+00:00"
  }
}
```

```json
{ "type": "action.execute", "requestId": "r-6", "payload": { "confirmationToken": "…jednorazowy token…", "confirm": true } }
```

```json
{
  "type": "action.execute",
  "requestId": "r-6",
  "payload": {
    "success": true,
    "action": "restartComputer",
    "target": "KS-101-01",
    "message": "Zaplanowano restart (dane przykładowe — stacja nie została fizycznie zrestartowana).",
    "before": "stan: Online; IP: 10.20.101.11; blokada: nie; obserwacja: 2026-11-25 18:11 UTC",
    "after": "restart zaplanowany za 30 s (makieta)",
    "auditId": "0d2b7adf-7fbd-4b24-a8ab-4d2a5ccc0910",
    "freshness": { "source": "Operacja serwera", "observedAtUtc": "2026-11-25T18:13:20+00:00", "isStale": false, "completeness": "complete" }
  }
}
```

```json
{ "type": "audit.query", "requestId": "r-7", "payload": { "target": "KS-101-01", "take": 10 } }
```

```json
{
  "type": "audit.query",
  "requestId": "r-7",
  "payload": {
    "items": [
      {
        "id": "0d2b7adf-7fbd-4b24-a8ab-4d2a5ccc0910",
        "timestampUtc": "2026-11-25T18:13:20+00:00",
        "actor": "a.nowak",
        "role": "Administrator",
        "capability": "computers.control",
        "action": "RestartComputer",
        "target": "KS-101-01",
        "before": "stan: Online; IP: 10.20.101.11; blokada: nie; obserwacja: 2026-11-25 18:11 UTC",
        "after": "restart zaplanowany za 30 s (makieta)",
        "result": "success",
        "remoteAddress": "10.20.1.35",
        "detail": "Powód: Zamrożony ekran · Zaplanowano restart."
      }
    ],
    "totalMatched": 1,
    "freshness": { "source": "Rejestr audytu (12 miesięcy)", "observedAtUtc": "2026-11-25T18:13:20+00:00", "isStale": false, "completeness": "complete" }
  }
}
```

## 5. Kody błędów

| Kod | Znaczenie | Reakcja klienta |
|---|---|---|
| `invalidRequest` | nieznany `type`, zły payload, nieczytelny JSON | błąd programisty — pokazać w logu |
| `unauthorized` | brak/zły token, brak grupy AD albo konto w grupie wykluczającej `Security:DeniedGroups` (HTTP 401 przed upgrade'em, wpis `auth.rejected`) | pokazać ekran „Brak dostępu”; nie ponawiać w pętli |
| `forbidden` | rola bez uprawnienia, nauczyciel poza zakresem, zapis wyłączony | ukryć akcję w UI, pokazać zakres z `welcome` |
| `notFound` | brak zasobu (np. konto/komputer akcji) | pusty stan z komunikatem |
| `upstreamUnavailable` | źródło (AD, LDAP, dziennik, WinRM, `shutdown.exe`) nieosiągalne; `detail` ma komunikat źródła | pokazać „dane niedostępne” + źródło, pozwolić na ponowienie — **nie** pokazywać pustej listy ani „sukcesu” |
| `internal` | błąd serwera | komunikat + ponowienie za chwilę |

### 5.1 Brak danych a błąd (M1–M4)

| Sytuacja | Co dostaje klient |
|---|---|
| AD nieosiągalne (bind/search poległ) | `error: upstreamUnavailable`, `detail` z komunikatem LDAP |
| Konto nie istnieje | `error: notFound` |
| Inwentarz obcięty limitem `MaxComputers` | poprawna lista + `freshness.completeness = partial` i nota z liczbami |
| Stan nie był jeszcze sprawdzony (brak adresu/timeout pingu) | lista z `status = unknown`, `freshness.completeness = unavailable` |
| `LoginHistory:Enabled = false` (tryb awaryjny) | `logins.query` z pustą listą, `completeness = unavailable` i notą „Historia logowań nie jest jeszcze podłączona (etap M2)” |
| Zakres zapytania przed retencją 30 dni | `completeness = partial` + nota z początkiem retencji |
| 2 z 6 stacji bez dziennika | poprawna lista + `completeness = partial` i nota z liczbą stacji |
| Nauczyciel prosi o obcą pracownię | `error: forbidden` z komunikatem o braku zajęć |
| Połączenie bez `wss` w produkcji | HTTP 403 + wpis `transport.rejected` w audycie |
| Konto w grupie `ZSE-Uczniowie` (także z grupą `ZSE-Admin-*`) | HTTP 401 **przed** upgrade'em, wpis `auth.rejected` z kodem przyczyny; brak `welcome` |
| Rola bez uprawnienia (np. `Student` z tokenu deweloperskiego) | `error: forbidden` dla każdego typu żądania — bez danych, także `topology.get` i `files.list` |
| Awaria `action.execute` | `error: upstreamUnavailable` z nazwą źródła + wpis audytu `result = failed` |

Zasada: **pusty wynik nigdy nie jest jedyną informacją** — zawsze towarzyszy mu
`freshness` mówiące, czy źródło w ogóle odpowiedziało.

## 6. Reconnect i resynchronizacja

1. Klient wykrywa zerwanie (błąd odbioru lub close) i przechodzi w stan `reconnecting` z backoffem 2, 4, 8, 16, 30 s.
2. Wszystkie oczekujące żądania kończą się błędem transportu — **nie** są cicho ponawiane.
3. Po ponownym połączeniu klient pobiera pełny stan (`classrooms.list`, `computers.list`, `logins.query`), bo zdarzenia push z przerwy przepadły.
4. UI w czasie przerwy pokazuje „dane mogą być nieaktualne” i nie zmienia stanu komputerów na offline.

Brak mechanizmu kolejki/`since` w wersji 1 — świadoma decyzja: pełny odczyt po reconnect jest prostszy i bezpieczniejszy niż odtwarzanie strumienia.

## 7. Zgodność i wersjonowanie

- Dodanie nowego `type` jest zmianą zgodną wstecz.
- Dodanie pola do `payload` jest zgodne (klient ignoruje nieznane pola).
- Zmiana znaczenia pola, usunięcie pola lub zmiana typu wymaga `protocolVersion` + 1 i okresu równoległej obsługi.
- Klient powinien odrzucić połączenie z `protocolVersion` większym niż obsługiwany i pokazać komunikat o aktualizacji.

### 7.1 Rozszerzenia M2/M3/M4 w wersji 1

Zgodnie z powyższą regułą są to **dodatkowe, opcjonalne pola i nowe typy** — `protocolVersion`
pozostaje `1`, a starszy klient może ignorować nieznane wiadomości:

| Dodane | Charakter |
|---|---|
| `event.logins.new`, `audit.query`, `action.prepare`, `action.execute` | nowe `type` (kompatybilne wstecz) |
| `welcome.classroomScope` | nowe opcjonalne pole |
| `LoginQueryRequest.classroomId` / `classroomIds` | nowe opcjonalne pola; dla Teacher nadpisywane przez serwer |
| `LoginEventDto.classroomId` | nowe opcjonalne pole |
| `topology.get`, `files.list`, `files.stat` | nowe `type` zarezerwowane w wersji 1 (ZAD-06); implementacja w ZAD-04/ZAD-07 |
| kody `auth.rejected`: `denied_group`, `role_group_missing`, `token_missing`, `token_invalid`, `not_authenticated` | wartości w `detail` wpisu audytu, nie w odpowiedzi HTTP |

Nie zmieniono znaczenia żadnego istniejącego pola. `logins.query` nadal zwraca `LoginsResponse`
z `items`, `totalMatched` i `freshness`; M2 zmienia jedynie źródło danych i treść noty.
Wymuszenie `wss` i zmiana trybu uwierzytelniania są konfiguracją wdrożenia, nie zmianą formatu
wiadomości. Gdyby w przyszłości trzeba było np. usunąć `access_token` albo zmienić `role`,
wymagałoby to `protocolVersion = 2`.

## 8. Ograniczenia wersji 1

- Brak subskrypcji tematycznych (klient dostaje zdarzenia stanu wszystkich pracowni, do których ma dostęp; `event.logins.new` jest filtrowany per sesja).
- Brak strumieniowania dużych list (paginacja po `take`, brak kursora).
- Brak kompresji ramek (przy 30 sesjach i małych wiadomościach niepotrzebna).
- Bufor logowań i rejestr audytu są w pamięci procesu (M2: 30 dni, M3/M4: 12 miesięcy); audyt można dodatkowo zapisywać do pliku JSONL. Po restarcie serwera stan jest odtwarzany ze źródeł, a wpisy audytu tylko z pliku — patrz ADR-0003.
