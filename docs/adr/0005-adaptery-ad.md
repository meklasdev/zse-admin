# ADR-0005: Adaptery AD przez LDAP za portem `IAdInventoryReader`

**Status:** przyjęte
**Data:** 2026-11-26

## Kontekst

Etap M1 wymaga prawdziwych danych z Active Directory: komputerów z OU, kont z grupami
i mapowania OU → pracownia z konfiguracji. Kod musi działać bez domeny (development,
CI, testy jednostkowe) — poprzednie M0 nie miało żadnej zależności zewnętrznej w warstwie
danych, a `Mock*` były jedynymi implementacjami.

Do wyboru były trzy drogi: `System.DirectoryServices` (tylko Windows, prostsze API),
`System.DirectoryServices.Protocols` (LDAP, działa też poza Windows) albo własny klient LDAP.

## Decyzja

1. **`System.DirectoryServices.Protocols`** (pakiet NuGet 8.0.0) jako transporter — to jedyne
   miejsce, które zna LDAP (`Infrastructure/Ldap/LdapDirectoryReader.cs`).
2. Wynik wyszukiwania jest mapowany na **`LdapEntry`** (DN + słownik atrybutów jako bajty).
   Dzięki temu interpretacja AD (FILETIME, `userAccountControl`, `tokenGroups`, OU) jest
   zwykłym kodem, testowalnym bez kontrolera domeny.
3. Serwer zna tylko **`IAdInventoryReader`** (komputery + konta) i **`IComputerStatusProbe`**
   (ICMP). `IClassroomDirectory`, `IComputerDirectory`, `IAdDirectory` pozostają bez zmian,
   więc protokół i klient nie wiedzą, czy dane są z AD, czy z makiety.
4. **Sprawdzanie dostępności jest osobnym portem**, bo to inna technologia i inny harmonogram
   niż odczyt katalogu.
5. Wybór źródła odbywa się przez `ZseAdmin:UseMockData` w `Program.cs` (DI).
   `UseMockDataForLogins` pozwalało przejściowo mieć prawdziwe AD i przykładowe logowania (M1.5;
   zastąpione w M2 — patrz rozszerzenie na końcu dokumentu).

## Alternatywy

| Alternatywa | Dlaczego odrzucona |
|---|---|
| `System.DirectoryServices.DirectorySearcher` | wymaga Windows i COM-owego `ADSI`; trudne do uruchomienia w CI i na Linuksie; mniej przewidywalne błędy |
| Bezpośredni `LdapConnection` w handlerach routera | logika AD przecieka do protokołu; nie da się testować bez domeny |
| Własna implementacja BER/LDAP | nieuzasadniony koszt, gorsze bezpieczeństwo (TLS, referencje, stronicowanie) |
| Cache w bazie zamiast w pamięci | ADR-0003: brak bazy do M2; cache w pamięci wystarcza, a `observedAtUtc` i tak ujawnia wiek danych |

## Konsekwencje

**Pozytywne:** testy M1 (mapowanie OU, grupy, FILETIME, kompletność) działają w CI bez domeny;
podmiana źródła to jedna flaga; fail-fast przy błędnej konfiguracji pracowni.

**Negatywne:** trzeba utrzymywać własną interpretację atrybutów; `tokenGroups` wymaga
dodatkowego zapytania (rozwiązane: jedno zapytanie na wszystkie SID-y, nie N zapytań);
`dNSHostName` nie daje adresu IP — do M1.1 pole `ipAddress` może być puste i UI musi to pokazać
jako „brak danych”.

**Neutralne:** pakiet jest częścią .NET 8, więc nie zmienia wymagań wdrożeniowych.

## Rozszerzenie — M2/M4 (2026-11-26)

Przejściowy przełącznik `UseMockDataForLogins` (M1.5) został zastąpiony osobnym
portem `ILoginEventSource` i konfiguracją `ZseAdmin:LoginHistory` (ADR-0006).
W produkcji `ProductionConfigurationGuard` wymaga `UseMockDataForLogins = false`.
Ten sam transporter LDAP (`LdapConnectionFactory`) zasila teraz również dwie
dozwolone operacje zapisu `ILdapDirectoryWriter`: `unicodePwd` + `pwdLastSet`
oraz `member` (ADR-0008). Nie dodano ogólnego endpointu LDAP — port nadal
ogranicza powierzchnię zależności od AD.
