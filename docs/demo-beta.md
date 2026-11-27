# Demo beta — ZSE-Admin

Demo beta to przeglądarkowy, responsywny podgląd interfejsu z fikcyjnymi danymi. Pozwala obejrzeć pulpit, pracownie, komputery, historię logowań, konta oraz przykładowy audyt bez domeny Active Directory i bez instalowania klienta WPF.

> **Bezpieczeństwo:** ten podgląd jest całkowicie statyczny i nie łączy się z serwerem, WebSocketem ani AD. Wszystkie nazwiska, konta, adresy i zdarzenia są przykładowe. Przyciski akcji pokazują symulację — niczego nie restartują, nie blokują ani nie zmieniają. To nie jest paczka produkcyjna ani rzeczywiste środowisko beta do podłączania danych szkolnych.

## Wersja przeglądarkowa

Podgląd w przeglądarce jest osobnym, responsywnym prototypem — **nie jest zrzutem 1:1 klienta WPF**. Dane są generowane lokalnie przez `demo.js`, a ekran ma własny układ HTML/CSS.

## Rzeczywisty klient WPF jako EXE

Do obejrzenia interfejsu aplikacji z repozytorium użyj publikacji beta dla Windows:

```powershell
./scripts/publish-beta-exe.ps1 -Runtime win-x64
```

Wynik: `artifacts/beta-exe/ZseAdmin.Client.exe`. Uruchom go w Windows. To istniejący klient WPF, a serwer WebSocket z mockami uruchamia się **wewnątrz tego samego procesu**, na losowym porcie `127.0.0.1`. Po zamknięciu okna serwer jest zatrzymywany. W tej konfiguracji wymuszone są dane przykładowe i uwierzytelnianie lokalne; aplikacja nie łączy się z AD. Publikacja jest samodzielna (`win-x64`, .NET dołączony), ale plik `appsettings.json` pozostaje obok EXE jako ustawienie uruchomienia.

Nie uruchamiaj tej komendy na Linux/macOS — wymaga Windows SDK i toolchainu WPF. Wydania beta można publikować jako GitHub Release tagiem w formacie `v*-beta*`; workflow `.github/workflows/release-beta.yml` buduje EXE na Windows i dołącza ZIP z EXE oraz `appsettings.json`.

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
