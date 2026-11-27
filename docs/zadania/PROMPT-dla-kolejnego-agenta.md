# Prompt do przekazania kolejnemu agentowi (implementacja M6–M8)

**Jak użyć:** skopiuj cały blok poniżej do nowej sesji/agenta. Jest samowystarczalny — nie wymaga
znajomości poprzedniej rozmowy. Zastąp `<<ZADANIE>>` numerem zadania (np. `ZAD-01`) albo napisz
`kolejno ZAD-01, ZAD-06, ZAD-02 ...`.

```text
Pracujesz w repozytorium meklasdev/zse-admin (.NET 8: serwer ASP.NET Core + WebSocket,
klient WPF, wspólne kontrakty w ZseAdmin.Contracts, testy xUnit, CI na windows-latest).
Etapy M0–M4 są gotowe i zmergowane; M5 (wdrożenie) jest w toku. Pracuj WYŁĄCZNIE na bieżącej
gałęzi, nie zmieniaj gałęzi, nie otwieraj PR bez pytania.

Zanim zaczniesz, przeczytaj w tej kolejności:
1. README.md (status, mapa dokumentów),
2. docs/notatki-zamawiajacego-2026-11-26.md — zwłaszcza §4 (odpowiedzi zamawiającego D1–D10)
   i §4.1 (trzy drobne doprecyzowania),
3. docs/ui-design-system.md — obowiązujący styl UI: spójny, flat, 1px, ZERO GLOW,
4. docs/zadania/README.md oraz docs/zadania/<<ZADANIE>>.md — pełna specyfikacja,
5. docs/roadmapa.md (M6–M8), docs/api-websocket.md §7 (wersjonowanie), docs/model-danych.md §4,
   docs/uprawnienia-i-dane-osobowe.md, ADR-y w docs/adr/.

DECYZJE ZAMAWIAJĄCEGO (podjęte 2026-11-26, nie zgaduj, nie pytaj ponownie):
- D1: listę sal i przypisanie classroomIds przygotowuje administrator w topology.json;
  przykładowe wpisy w repo są tylko wzorcem.
- D2: „zdj jak z Cisco" = GOTOWE RYSUNKI/OBRAZKI URZĄDZEŃ w stylu Packet Tracer
  (nie fotografie szafy) + diagram UŁOŻENIA komputerów w sali + diagram POŁĄCZEŃ z portami.
- D3: /pepsi to DYSK SIECIOWY na serwerze. Root w konfiguracji musi być ścieżką UNC
  (np. \\serwer\pepsi), NIE literą mapowanego dysku — mapowanie jest per użytkownik i nie jest
  widoczne dla usługi Windows. Konto usługi potrzebuje praw odczytu do udziału.
- D4: listing /pepsi widzą WSZYSTKIE role aplikacji (Administrator, Teacher, ReadOnly).
  Uczniowie są odrzucani wcześniej. Wydzielone prawa ZAPISU na udziale to sprawa NTFS
  POZA aplikacją — v1 implementuje wyłącznie odczyt metadanych, bez zapisu i bez treści plików.
- D5: logowanie do domeny tworzy PROFIL LOKALNY na tej stacji; pliki nie przenoszą się między
  stacjami (brak profili mobilnych). LoginEventDto.accountScope = domain/local/unknown,
  wyznaczany z dziennika 4624, bez zgadywania.
- D6: uczeń NIE MA DOSTĘPU DO APLIKACJI. UI nie pokazuje żadnych danych o plikach ucznia ani
  powiązania plik ↔ konto. Notatka techniczna o braku profili mobilnych jest dla administratora.
- D7: topologię widzi KAŻDY ZALOGOWANY (wszystkie trzy role), BEZ filtrowania classroomScope
  dla tego widoku. Świadome odstępstwo od ADR-0007 dotyczy TYLKO nowego widoku topologii —
  historia logowań i konta nadal są filtrowane jak w M3.
- D8: DODAJ pakiet `dotnet add package SvgIcons --version 0.1.10` do ZseAdmin.Client.csproj.
  Bramka 7 sprawdzeń z docs/adr/0009-ikony-svg.md jest obowiązkowa: `dotnet restore` i
  `dotnet build -c Release` muszą być zielone w CI. Jeśli paczka nie istnieje / nie wspiera WPF,
  użyj fallbacku z lokalnymi geometriami Path — bez ponownego pytania zamawiającego.
- D9: krój systemowy Segoe UI, BEZ dołączania Inter / Plus Jakarta Sans.
- D10: deny overrides — uczeń jest blokowany zawsze, nawet jeśli przypadkiem dostał grupę
  ZSE-Admin-*. Odmowa 401 przed upgrade WebSocket + audyt auth.rejected.

TWARDE ZASADY (nie zmieniaj bez nowej decyzji):
- Całe UI: płaskie, 1px obramowania, ZERO glow — zakaz DropShadowEffect, BlurEffect,
  gradientów, neonów, kolorów wpisanych wprost w XAML; tylko StaticResource z Theme.xaml.
  Formularze dwukolumnowe, przerywane strefy upuszczania, dostępność (kontrast, fokus, DPI).
- protocolVersion pozostaje 1: nowe typy wiadomości i opcjonalne pola DTO są OK, zmiana
  znaczenia / usunięcie pola — NIE. Stary klient musi ignorować nieznane pola.
- ADR-0003: brak własnej bazy danych. ADR-0007: produkcja = wss + Windows/Kerberos,
  bramka produkcyjna bez tokenów. ADR-0008: akcje M4 z potwierdzeniem i audytem.
- Serwer jest jedynym miejscem egzekwowania uprawnień (CapabilityGuard w MessageRouter);
  ukrycie przycisku w UI nie jest zabezpieczeniem. Brak mapowania capability = forbidden.
- Semantyka braków z docs/model-danych.md §4: unknown ≠ offline, unavailable ≠ „pusto",
  partial zawsze z notą. Żadna odpowiedź nie może sugerować aktualności bez freshness.
- Uczniowie: zero dostępu do aplikacji i danych. Nie twórz roli „Uczeń" z uprawnieniami.
- Nie hardkoduj /pepsi, nie implementuj zapisu na udział, nie pobieraj treści plików,
  nie mapuj plików na uczniów (RODO/DPIA w M5).
- Nie dodawaj sekretów/tokenów. Nie przywracaj zamkniętych zgłoszeń GitHub #5–#15.

ZAKRES: zrealizuj <<ZADANIE>> dokładnie według jego pliku w docs/zadania/ — łącznie z sekcjami
Zakres, Poza zakresem, Kryteria akceptacji Given/When/Then, Testy i Definicja ukończenia.
Kolejność sugerowana: ZAD-01 + ZAD-06 → ZAD-02 / ZAD-03 / ZAD-04 → ZAD-05 / ZAD-09 →
ZAD-07 → ZAD-08 → ZAD-10. Jedno zadanie = jeden PR.

Zrobione: ZAD-01 (PR #18), ZAD-06 (PR #19) i ZAD-11 (PR #21 — automatyczne zrzuty UI z realnego
klienta WPF w `screenshots` job). Zrzuty 100/125/150% DPI są artefaktem CI, nie plikami w repo.

WYMAGANIA KOŃCOWE:
- `dotnet build -c Release` i `dotnet test` zielone na windows-latest (CI jest wiążące;
  lokalnie może nie być .NET SDK — wtedy napisz to wprost w opisie PR).
- Testy jednostkowe dla nowej logiki, najlepiej w ZseAdmin.Client.Core / testach bez WPF tam,
  gdzie to możliwe (TopologyLayout, IconCatalog, FileBrowserViewModel, AccountScope).
- W tym samym PR aktualizuj docs/api-websocket.md, docs/model-danych.md,
  docs/uprawnienia-i-dane-osobowe.md, docs/uruchomienie.md, docs/roadmapa.md, README.md
  i status w docs/zadania/README.md.
- Załącz zrzuty ekranu 100/125/150% DPI dla zmian w UI i odhacz checklistę
  docs/ui-design-system.md §5.
- Jeśli coś w specyfikacji jest sprzeczne z decyzjami D1–D10 wyżej, decyzje mają pierwszeństwo
  i popraw dokumentację w tym samym PR.
- Na koniec napisz krótko: co zrobiłeś, które kryteria Given/When/Then spełniłeś, jaki jest
  wynik CI i co zostało otwarte (np. dokładny UNC, lista sal, pliki grafik).
```

**Uwaga:** prompt opisuje stan na 2026-11-26. Jeśli któryś z trzech punktów z §4.1 notatek
zostanie doprecyzowany, zaktualizuj też ten plik.
