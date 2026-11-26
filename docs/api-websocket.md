# Protokół WebSocket

Wersja protokołu: **1** (stała `ProtocolJson.ProtocolVersion`, wysyłana w `welcome`).

## 1. Transport i uwierzytelnianie

- Endpoint: `/ws` na serwerze ZSE-Admin (`ws://host:5080/ws`).
- **Produkcja: wyłącznie `wss://`.** Przy `Security:RequireSecureTransport=true` serwer odrzuca
  niezaszyfrowane `ws://` odpowiedzią HTTP 400, zanim powstanie jakakolwiek sesja.
- Podprotokół: brak (czysty tekst). Wiadomości binarne są ignorowane.
- Limit wiadomości: 1 MiB; przekroczenie zamyka sesję.
- Keep-alive: ping WebSocket co 20 s po obu stronach.

### 1.1 Produkcja (M3): Windows/Kerberos + grupy AD

Konfiguracja: `Security:Authentication:Mode = ActiveDirectory`.

1. Żądanie `/ws` przechodzi przez Negotiate (IIS z uwierzytelnianiem Windows albo handler
   `Microsoft.AspNetCore.Authentication.Negotiate` na Kestrel). Brak tożsamości → HTTP 401.
2. Serwer mapuje grupy AD użytkownika na rolę — świeżo przy każdym połączeniu:
   `ZSE-Admin-Administratorzy` → `Administrator`, `ZSE-Admin-Nauczyciele` → `Teacher`,
   `ZSE-Admin-Podglad` → `ReadOnly` (nazwy konfigurowalne w `Security:GroupMapping`).
3. Użytkownik spoza wszystkich grup → HTTP 401, sesja nie powstaje.
4. Nieosiągalne AD (grup się nie da odczytać) → HTTP 503, a nie 401 — klient powinien ponowić później.
5. Próby odrzucone (bez tożsamości) trafiają do audytu jako `auth.connect` / `denied`.

### 1.2 Development: tokeny

Konfiguracja: `Security:Authentication:Mode = DevelopmentTokens` (domyślnie w `appsettings.Development.json`).

Token przekazywany jest **przed** upgrade'em:

| Sposób | Przykład | Kiedy |
|---|---|---|
| Nagłówek | `X-ZseAdmin-Token: <token>` | klient desktopowy |
| Parametr | `?access_token=<token>` | przeglądarka, konsola debug |

Brak lub błędny token → HTTP 401, sesja nie powstaje. Po upgrade'ie serwer nie przyjmuje już zmian tożsamości — reconnect wymaga ponownej autentykacji.

> Tokeny są zabronione na produkcji — `SecurityConfigurationValidator` zatrzymuje serwer przy starcie, jeśli `Security:Tokens` nie jest pusta albo wymuszono je w trybie `ActiveDirectory`.

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

`WelcomeResponse`:

```json
{
  "type": "welcome",
  "payload": {
    "serverVersion": "0.4.0",
    "protocolVersion": 1,
    "sessionId": "9f1c…",
    "userName": "k.mazur",
    "role": "Teacher",
    "capabilities": ["classrooms.read", "computers.read", "logins.read", "users.read"],
    "serverTimeUtc": "2026-11-25T18:12:03+00:00"
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
| `computers.action` | `computers.control` | `ComputerActionRequest` | `ComputerActionResponse` |
| `users.resetPassword` | `users.write` | `ResetPasswordRequest` | `ResetPasswordResponse` |
| `users.addToGroup` | `users.write` | `AddToGroupRequest` | `AddToGroupResponse` |
| `audit.query` | `audit.read` | `AuditQueryRequest` | `AuditResponse` |

**Akcje zmieniające stan (M4) — zasady wspólne:**

- Każda wymaga `confirm = true`; brak potwierdzenia → `invalidRequest`.
- Każda kończy się wpisem audytowym (`accepted`/`failed`/`ok`/`nochange`) z aktorem, celem, wartością przed/po i adresem.
- Hasło nigdy nie trafia do audytu (`(hasło utajnione)`).
- Błąd zapisu audytu = operacja **nie** jest meldowana jako udana (`internal`).
- Błąd wykonania akcji → wyjątek `upstreamUnavailable`, a wpis audytowy zapisuje wynik `failed`. Klient nigdy nie dostaje „sukcesu” przy nieudanej akcji.

**Zakres `logins.query` dla nauczyciela (M3):** rola `Teacher` widzi wyłącznie logowania ze stacji w pracowniach przypisanych do jego konta (`teachers` w `classrooms.json` / mapie `ClassroomMap`). Żądanie o inną pracownię lub stację spoza przypisania → `forbidden`. Konto bez przypisanej pracowni → `forbidden`. Administrator i ReadOnly bez ograniczeń.

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
        "source": "Security log (4624)"
      }
    ],
    "totalMatched": 137,
    "freshness": {
      "source": "Dzienniki zabezpieczeń stacji",
      "observedAtUtc": "2026-11-25T18:12:00+00:00",
      "isStale": false,
      "completeness": "partial",
      "note": "Pytanie dotyczy ostatnich 14 dni (dane przykładowe); dziennik docelowy: retencja 30 dni (M2)."
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

### 4.4 Akcja na stacji (M4)

```json
{ "type": "computers.action", "requestId": "r-10", "payload": { "computerName": "KS-101-01", "action": "restart", "confirm": true } }
```

```json
{
  "type": "computers.action",
  "requestId": "r-10",
  "payload": {
    "computerName": "KS-101-01",
    "action": "restart",
    "accepted": true,
    "message": "Restart komputera KS-101-01 został zlecony.",
    "auditId": "5c3d…"
  }
}
```

`action`: `restart` (przez `shutdown.exe /r /m \\<komputer> /f /t 0`) albo `lock`
(zdalna blokada ekranu — wymaga `Actions:PsExecPath`; bez niej jawny błąd, nie udawany sukces).

### 4.5 Reset hasła (M4)

```json
{ "type": "users.resetPassword", "requestId": "r-11", "payload": { "samAccountName": "j.kowalski", "newPassword": "NoweHaslo!2026", "confirm": true } }
```

```json
{
  "type": "users.resetPassword",
  "requestId": "r-11",
  "payload": {
    "samAccountName": "j.kowalski",
    "changed": true,
    "message": "Hasło zostało zmienione.",
    "auditId": "8a71…"
  }
}
```

Hasło musi mieć min. 8 znaków. Nigdy nie trafia do audytu ani logów.

### 4.6 Dodanie do grupy (M4)

```json
{ "type": "users.addToGroup", "requestId": "r-12", "payload": { "samAccountName": "j.kowalski", "groupName": "Klasa-3TI", "confirm": true } }
```

```json
{
  "type": "users.addToGroup",
  "requestId": "r-12",
  "payload": {
    "samAccountName": "j.kowalski",
    "groupName": "Klasa-3TI",
    "changed": true,
    "message": "Dodano konto do grupy.",
    "auditId": "c21b…"
  }
}
```

Operacja jest idempotentna: konto już w grupie → `changed: false`, audyt z wynikiem `nochange`.

### 4.7 Zapytanie o audyt (M3/M4)

```json
{ "type": "audit.query", "requestId": "r-13", "payload": { "target": "KS-101-01", "take": 20 } }
```

```json
{
  "type": "audit.query",
  "requestId": "r-13",
  "payload": {
    "items": [
      {
        "id": "5c3d…",
        "timestampUtc": "2026-11-26T09:14:02+00:00",
        "actor": "admin.it",
        "role": "Administrator",
        "capability": "computers.control",
        "target": "KS-101-01",
        "before": "status=online (obserwacja 2026-11-26 09:13 UTC)",
        "after": "Wydano: restart",
        "result": "accepted",
        "remoteAddress": "192.168.1.10"
      }
    ],
    "freshness": { "source": "Dziennik audytu (lokalny)", "observedAtUtc": "2026-11-26T09:14:05+00:00", "isStale": false, "completeness": "complete" }
  }
}
```

Filtry: `target` (podciąg, np. komputer albo `sam → grupa`), `capability` (np. `computers.control`), `take` ≤ 200. Wymaga `audit.read`. Wynik posortowany malejąco po czasie.

### 4.8 Błąd

```json
{
  "type": "error",
  "requestId": "r-4",
  "payload": { "code": "forbidden", "message": "Brak uprawnienia 'users.read' dla roli 'ReadOnly'." }
}
```

### 4.9 Zdarzenie push

```json
{
  "type": "event.computer.status",
  "payload": { "computer": { "name": "KS-101-02", "status": "offline", "statusObservedAtUtc": "2026-11-25T18:13:05+00:00" } }
}
```

## 5. Kody błędów

| Kod | Znaczenie | Reakcja klienta |
|---|---|---|
| `invalidRequest` | nieznany `type`, zły payload, nieczytelny JSON | błąd programisty — pokazać w logu |
| `unauthorized` | brak/zły token | nie ponawiać w pętli; poprosić o konfigurację |
| `forbidden` | rola bez uprawnienia | ukryć akcję w UI |
| `notFound` | brak zasobu (np. konto) | pusty stan z komunikatem |
| `upstreamUnavailable` | źródło (AD, LDAP, dziennik) nieosiągalne; `detail` ma komunikat źródła | pokazać „dane niedostępne” + źródło, pozwolić na ponowienie — **nie** pokazywać pustej listy |
| `internal` | błąd serwera | komunikat + ponowienie za chwilę |

### 5.1 Brak danych a błąd (etap M1)

| Sytuacja | Co dostaje klient |
|---|---|
| AD nieosiągalne (bind/search poległ) | `error: upstreamUnavailable`, `detail` z komunikatem LDAP |
| Konto nie istnieje | `error: notFound` |
| Inwentarz obcięty limitem `MaxComputers` | poprawna lista + `freshness.completeness = partial` i nota z liczbami |
| Stan nie był jeszcze sprawdzony (brak adresu/timeout pingu) | lista z `status = unknown`, `freshness.completeness = unavailable` |
| Historia: stacje bez dostępu do dziennika (M2) | `completeness = partial` + nota z liczbą stacji bez dostępu |
| Historia: tryb AD bez skonfigurowanego zbioru (M1.5) | `logins.query` z pustą listą, ale `completeness = unavailable` (gdy `UseMockDataForLogins=false` i brak magazynu) |
| Zakres zapytania przed retencją | `completeness = partial` + nota z początkiem retencji |
| Nauczyciel pyta o cudzą pracownię/stację (M3) | `error: forbidden` — nigdy cicha pusta lista |

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

## 8. Ograniczenia wersji 1

- Brak subskrypcji tematycznych (klient dostaje wszystkie zdarzenia stanu).
- Brak strumieniowania dużych list (paginacja po `take`, brak kursora).
- Brak kompresji ramek (przy 30 sesjach i małych wiadomościach niepotrzebna).
- Retencja historii logowań: 30 dni (M2; wartość do potwierdzenia przez DPO). Retencja audytu: 12 miesięcy.
