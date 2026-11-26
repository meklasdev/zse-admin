# ADR-0007: Windows/Kerberos, `wss`, grupy AD i zakres pracowni nauczyciela

**Status:** przyjęte
**Data:** 2026-11-26
**Etap:** M3

## Kontekst

Tokeny developerskie z `appsettings.json` nie dają rozliczalności per osoba i nie wygasają.
M3 wymaga, by użytkownik spoza grupy `ZSE-Admin-Nauczyciele` nie mógł się połączyć, by
połączenie bez `wss` było odrzucone, a nauczyciel proszący o logowania z pracowni, w której
nie ma zajęć, dostawał `forbidden`. Trzeba też zapewnić audyt operacji zmieniających dane (M4)
i bramkę, która nie pozwoli uruchomić produkcji na tokenach.

Protokół w wersji 1 dopuszcza dodawanie nowych typów i opcjonalnych pól bez zmiany
`protocolVersion` (ADR-0002). Zmiana sposobu uwierzytelniania jest konfiguracją wdrożenia,
nie zmianą znaczenia pól wiadomości.

## Decyzja

1. **`Security:Mode` wybiera `TokenAuthenticator` albo `WindowsAuthenticator`.**
   W produkcji obowiązuje `Windows`: `HttpContext.User` z Negotiate/Kerberos (Kestrel
   `AddNegotiate` lub IIS/HTTP.sys), a `WindowsGroupProvider` rozwiązuje grupy do nazw.
   `RoleCatalog.RoleFromGroups` mapuje `ZSE-Admin-Administratorzy`,
   `ZSE-Admin-Nauczyciele` i `ZSE-Admin-ReadOnly` na role w kolejności
   Administrator → Nauczyciel → ReadOnly. Brak grupy = brak tożsamości = 401 przed
   upgrade'em WebSocket (fail-close).
2. **`TransportSecurityGuard` sprawdza wyłącznie `request.IsHttps`.** Za IIS/odwrotnym
   proxy `UseForwardedHeaders(XForwardedProto)` ustawia schemat z zaufanego proxy; nagłówek
   podsunięty bezpośrednio przez klienta nie jest dowodem bezpieczeństwa. Gdy
   `Security:RequireSecureTransport = true`, zwykłe `ws://` dostaje 403 z komunikatem
   „Wymagane połączenie wss (HTTPS).” przed utworzeniem sesji.
3. **`ProductionConfigurationGuard` zatrzymuje start serwera**, jeśli produkcja ma tryb inny
   niż Windows, niepuste `Security:Tokens`, wyłączone `RequireSecureTransport`, dane
   przykładowe, wyłączoną historię / źródło Mock, brak LDAPS albo brak mapowania grupy
   nauczycieli. `appsettings.Production.json` czyści tokeny, a CI kontroluje artefakt.
4. **`LoginAccessPolicy` jest jedynym miejscem zakresu danych.** Łączy stałe przypisania
   `Security:TeacherClassrooms` z oknami `Security:TeachingSlots` (dzień i godziny UTC).
   Dla roli Teacher nadpisuje `classroomId`/`classroomIds` w `LoginQueryRequest`, zwraca
   `forbidden` dla pracowni bez zajęć (także przy filtrze po komputerze) i filtruje
   `event.logins.new` per sesja. Administrator nie ma ograniczeń. `WelcomeResponse`
   dostaje opcjonalne `classroomScope`, żeby UI mogło zawęzić listę pracowni.
5. **Odmowy są audytowane.** `WsEndpoint` zapisuje `auth.rejected` i `transport.rejected`
   z aktorem, adresem IP i wynikiem `denied`. `AuditLog` (12 miesięcy, opcjonalny JSONL)
   jest współdzielony z akcjami M4, a `audit.query` wymaga `audit.read`.
6. **Konto usługi pozostaje najmniej uprzywilejowane:** odczyt wskazanych OU,
   Event Log Readers na stacjach, prawo zapisu `unicodePwd`/`member` tylko na wyznaczonych
   obiektach i WinRM do blokady; bez administratora domeny. Sekret wyłącznie ze zmiennych
   środowiskowych/magazynu sekretów.
7. `ProtocolVersion = 1` zostaje bez zmian: `welcome.classroomScope` i pola filtra są
   opcjonalne, `audit.query` jest nowym typem. Wyłączenie tokenów i odrzucenie `ws://`
   nie zmieniają formatu koperty.

## Alternatywy

| Alternatywa | Dlaczego odrzucona |
|---|---|
| Tokeny JWT z wygasaniem | osobny system tożsamości, rotacja kluczy i duplikacja kont AD; M3 wprost wskazuje Windows/grupy |
| Basic + hasło domenowe | wysyła hasło do aplikacji, brak Kerberos i SSO, gorszy audyt |
| ufanie nagłówkowi `X-Forwarded-Proto` od dowolnego klienta | klient mógłby sam „udawać” `wss`; sprawdzamy wynik middleware'u dla zaufanego proxy |
| Filtrowanie historii wyłącznie w kliencie | UI nie jest zabezpieczeniem; `logins.query` i push muszą być filtrowane serwerowo |
| Jeden statyczny przydział nauczyciel → pracownia | nie obsługuje zastępstw i planu lekcji; dlatego doszły okna `TeachingSlots` |
| Odrzucenie nieznanej grupy po upgrade'ie | wtedy powstaje sesja i zdarzenia mogą wyciec przed odmową; odrzucamy przed `AcceptWebSocketAsync` |

## Konsekwencje

**Pozytywne:** rozliczalność per konto Windows, bramka fail-fast, brak sekretów w repo,
egzekwowanie `wss` i zakresu nauczyciela po stronie serwera, protokół wciąż w wersji 1.

**Negatywne:** produkcja wymaga poprawnej konfiguracji IIS/Kestrel, SPN i certyfikatu;
wymagane jest dostarczenie rzeczywistego planu lekcji — puste `TeachingSlots` oznacza
`forbidden` dla nauczyciela bez stałego przypisania. `WindowsIdentity`/`SecurityIdentifier`
to API Windows (ostrzeżenia CA1416 wyciszone świadomie w adapterze). `UseForwardedHeaders`
musi być użyty tylko za zaufanym proxy; w M5 trzeba jawnie zweryfikować jego listę zaufania.

**Neutralne:** `scripts/publish-client.ps1` podmienia `appsettings.json` artefaktu na
`wss` + `UseWindowsAuth = true` i pusty `Token` (CI sprawdza te trzy warunki). `App.xaml.cs`
nie startuje w trybie Windows Auth na `ws://`, a `WsClientConnection` ustawia
`UseDefaultCredentials`, gdy nie dostaje tokenu. Testy zastępują `IWindowsGroupProvider`
fake'iem, więc CI działa bez domeny. DPIA, klauzula informacyjna i przegląd grup AD
pozostają zadaniami wdrożeniowymi M5.
