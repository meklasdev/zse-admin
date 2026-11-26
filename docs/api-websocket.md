# Protokół WebSocket

Wersja protokołu: **1** (stała `ProtocolJson.ProtocolVersion`, wysyłana w `welcome`).

## 1. Transport i uwierzytelnianie

- Endpoint: `/ws` na serwerze ZSE-Admin (`ws://host:5080/ws`, w produkcji `wss://`).
- Podprotokół: brak (czysty tekst). Wiadomości binarne są ignorowane.
- Limit wiadomości: 1 MiB; przekroczenie zamyka sesję.
- Keep-alive: ping WebSocket co 20 s po obu stronach.

Token przekazywany jest **przed** upgrade'em:

| Sposób | Przykład | Kiedy |
|---|---|---|
| Nagłówek | `X-ZseAdmin-Token: <token>` | klient desktopowy |
| Parametr | `?access_token=<token>` | przeglądarka, konsola debug |

Brak lub błędny token → HTTP 401, sesja nie powstaje. Po upgrade'ie serwer nie przyjmuje już zmian tożsamości — reconnect wymaga nowego tokenu.

> Uwaga: tokeny to rozwiązanie MVP. Docelowo uwierzytelnianie Windows/Kerberos — `docs/uprawnienia-i-dane-osobowe.md`.

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
    "serverVersion": "0.1.0",
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
      "note": "Źródło pokrywa ostatnie 14 dni."
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

## 5. Kody błędów

| Kod | Znaczenie | Reakcja klienta |
|---|---|---|
| `invalidRequest` | nieznany `type`, zły payload, nieczytelny JSON | błąd programisty — pokazać w logu |
| `unauthorized` | brak/zły token | nie ponawiać w pętli; poprosić o konfigurację |
| `forbidden` | rola bez uprawnienia | ukryć akcję w UI |
| `notFound` | brak zasobu (np. konto) | pusty stan z komunikatem |
| `upstreamUnavailable` | źródło (AD, dziennik) nieosiągalne | pokazać „dane niedostępne”, pozwolić na ponowienie |
| `internal` | błąd serwera | komunikat + ponowienie za chwilę |

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
