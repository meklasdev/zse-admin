# Demo beta — ZSE-Admin

Demo beta to przeglądarkowy, responsywny podgląd interfejsu z fikcyjnymi danymi. Pozwala obejrzeć pulpit, pracownie, komputery, historię logowań, konta oraz przykładowy audyt bez domeny Active Directory i bez instalowania klienta WPF.

> **Bezpieczeństwo:** ten podgląd jest całkowicie statyczny i nie łączy się z serwerem, WebSocketem ani AD. Wszystkie nazwiska, konta, adresy i zdarzenia są przykładowe. Przyciski akcji pokazują symulację — niczego nie restartują, nie blokują ani nie zmieniają. To nie jest paczka produkcyjna ani rzeczywiste środowisko beta do podłączania danych szkolnych.

## Uruchomienie

### Z serwerem aplikacji

Wymagany .NET SDK 8:

```bash
dotnet run --project src/ZseAdmin.Server
```

Otwórz `http://localhost:5080/` lub `http://localhost:5080/demo.html`. Strona główna przekierowuje do demo; techniczna konsola WebSocket pozostaje dostępna pod `/debug.html`.

### Sam podgląd UI

Wystarczy dowolny statyczny serwer HTTP — backend .NET nie jest wymagany. Przykład z Pythonem:

```bash
python3 -m http.server 8080 --directory src/ZseAdmin.Server/wwwroot
```

Następnie otwórz `http://localhost:8080/`.

## Co można przetestować

- wybór pracowni i przegląd stanu komputerów;
- wyszukiwanie oraz filtrowanie po stanie;
- widoki historii logowań, użytkowników i audytu;
- szczegóły wybranego komputera oraz symulację podglądu operacji;
- eksport przykładowej listy komputerów do CSV;
- układ responsywny na wąskich ekranach.

Dane interfejsu są celowo niezależne od `SchoolSeed` serwera: demo nie potrzebuje działającego backendu, a każda interakcja pozostaje lokalna w przeglądarce.
