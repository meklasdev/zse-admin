# ADR-0001: .NET 8 — ASP.NET Core (serwer) + WPF (klient)

**Status:** przyjęte
**Data:** 2026-11-26

## Kontekst

Serwer działa na maszynie z Active Directory (Windows Server 2016). Klient jest narzędziem administracyjnym używanym w sieci szkolnej. Potrzebny jest dostęp do AD, dzienników zdarzeń i WMI — czyli API typowo windowsowych — oraz stabilna komunikacja sieciowa.

## Decyzja

- Serwer: ASP.NET Core na .NET 8 (minimal API + WebSocket), publikowany jako usługa Windows lub aplikacja IIS.
- Klient: WPF na .NET 8 (`net8.0-windows`), z logiką w osobnym projekcie `ZseAdmin.Client.Core` bez zależności od WPF.
- Wspólne kontrakty protokołu w `ZseAdmin.Contracts`.

## Alternatywy

| Alternatywa | Dlaczego odrzucona |
|---|---|
| Node/TypeScript + React (klient web) | AD i dzienniki wymagają owijania PowerShell/WMI; gorsze wsparcie dla Kerberos w scenariuszu domenowym; aplikacja webowa zwiększa powierzchnię ataku |
| Python (FastAPI) + React | `pywin32`/`ldap3` działają, ale dystrybucja na Windows Server i serwisowanie są trudniejsze; brak jednego typu DTO po obu stronach |
| Avalonia zamiast WPF | technicznie lepsza przenośność, ale szkoła używa Windows, a WPF nie wymaga zewnętrznych pakietów ani licencji |
| Blazor Server / Electron | nadmiar jak na narzędzie wewnętrzne; Electron podnosi koszt aktualizacji |

## Konsekwencje

**Pozytywne:** jeden język i jeden typ DTO po obu stronach; natywny dostęp do AD/WMI/EventLog z poziomu serwera; WPF bez dodatkowych zależności NuGet; łatwe testy logiki bez UI.

**Negatywne:** klient tylko na Windows; WPF wymaga Desktop Experience (nie działa na Server Core); .NET 8 na Server 2016 wymaga instalacji runtime i jest zależny od cyklu życia systemu (wsparcie do 2027-01).

**Neutralne:** logika klienta jest oddzielona od WPF, więc ewentualna migracja na Avalonię nie dotknie protokołu ani ViewModeli.

## Adnotacja — 2026-11-26 (decyzja D8 = B)

Zamawiający zatwierdził **jedną** zewnętrzną zależność NuGet w kliencie: `SvgIcons 0.1.10`
([ADR-0009](0009-ikony-svg.md), [`zadania/02-ikony-svg.md`](../zadania/02-ikony-svg.md)).
Zdanie „WPF bez dodatkowych zależności NuGet” z sekcji *Konsekwencje* należy odtąd czytać jako
„bez zależności **poza jedną, jawnie zatwierdzoną i zweryfikowaną w CI**”. Do czasu zielonego
`dotnet restore`/buildu w ZAD-02 repozytorium nadal nie zawiera tej referencji. Pozostałe ustalenia
(.NET 8, WPF, wspólne kontrakty, brak własnej bazy) pozostają bez zmian.
