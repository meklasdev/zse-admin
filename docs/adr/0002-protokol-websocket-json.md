# ADR-0002: JSON w kopercie przez WebSocket

**Status:** przyjęte
**Data:** 2026-11-26

## Kontekst

README wymaga komunikacji klient–serwer przez WebSocket i podkreśla, że kanał sam nie gwarantuje aktualności. Potrzebne są zarówno żądania z odpowiedzią, jak i zdarzenia push (zmiana stanu komputera).

## Decyzja

Jedno połączenie WebSocket z tekstowymi ramkami JSON w kopercie:

```json
{ "type": "computers.list", "requestId": "…", "payload": { } }
```

- Żądanie ma `requestId`; odpowiedź ma ten sam `type` i `requestId`.
- Błędy: `type = "error"` z `ErrorDto` (kod + komunikat).
- Zdarzenia push: `type = "event.*"`, bez `requestId`.
- Enumy jako stringi, camelCase, pomijanie `null`.

## Alternatywy

| Alternatywa | Dlaczego odrzucona |
|---|---|
| REST + polling | brak push, gorsze opóźnienia, więcej narzutu; README wprost mówi o WebSocket |
| gRPC | świetny kontrakt, ale generowanie klienta i konfiguracja HTTP/2 w IIS to nadmiar dla 30 sesji |
| SignalR | wygodny, ale ukrywa semantykę dostarczenia i utrudnia testowanie protokołu w `debug.html` |
| Własny protokół binarny | brak realnej korzyści przy małych wiadomościach, gorsza debugowalność |

## Konsekwencje

**Pozytywne:** prosty, czytelny, testowalny ręcznie (konsola w `debug.html`); łatwo dodać nowy `type`; testy serializacji pilnują zgodności.

**Negatywne:** brak gwarancji dostarczenia — po reconnect trzeba pobrać pełny stan (świadoma decyzja: prostsze niż kolejka `since`); brak paginacji kursorowej; brak kompresji.

**Neutralne:** rozszerzenie o subskrypcje tematyczne wymaga nowego pola (`topics`) i jest zgodne wstecz.
