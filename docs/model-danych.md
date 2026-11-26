# Model danych

Wszystkie typy żyją w `ZseAdmin.Contracts` i są współdzielone przez serwer i klienta — to gwarantuje, że obie strony mówią tym samym językiem. Czasy są w UTC (`*Utc`), a klient formatuje je lokalnie.

## 1. Byty

### ClassroomDto — pracownia

| Pole | Typ | Znaczenie |
|---|---|---|
| `id` | string | stabilny identyfikator konfiguracyjny (`prac-101`), nie nazwa |
| `name` | string | nazwa widoczna dla człowieka |
| `ouDistinguishedName` | string? | OU przypisane jawną regułą; `null` = pracownia bez OU |
| `computerCount` | int | liczba komputerów w pracowni (z inwentarza) |

Pracownia **nie jest** OU. To kontekst szkolny, mapowany na jedno lub wiele OU w konfiguracji.

### ComputerDto — komputer

| Pole | Typ | Znaczenie |
|---|---|---|
| `name` | string | nazwa NetBIOS/AD, klucz komputera |
| `classroomId`, `classroomName` | string? | przypisanie do pracowni; `null` = poza znanym OU |
| `ouDistinguishedName` | string? | faktyczne położenie w AD |
| `ipAddress` | string? | ostatnio zaobserwowany adres; `null` = brak danych |
| `status` | `online` \| `offline` \| `unknown` | wynik ostatniego sprawdzenia |
| `statusObservedAtUtc` | DateTimeOffset? | kiedy stan został zaobserwowany; `null` dla `unknown` |
| `lastLogonAtUtc` | DateTimeOffset? | ostatnie znane logowanie (z AD lub zdarzeń) |
| `operatingSystem` | string? | z AD (`operatingSystem`) |

**Reguła:** `status = unknown` i `statusObservedAtUtc = null` to „brak danych”, a nie „offline”. UI nie może ich utożsamiać.

### LoginEventDto — zdarzenie logowania

| Pole | Typ | Znaczenie |
|---|---|---|
| `id` | Guid | identyfikator zdarzenia (docelowo rekord z dziennika) |
| `userSamAccountName` | string | konto, którego dotyczy zdarzenie |
| `userDisplayName` | string? | nazwa wyświetlana (rozwiązana z AD) |
| `computerName` | string | stacja, na której zarejestrowano zdarzenie |
| `occurredAtUtc` | DateTimeOffset | czas zdarzenia |
| `type` | `logon` \| `logoff` \| `failedLogon` | rodzaj |
| `source` | string | np. `Security log (4624)` — audytowalne pochodzenie |

Model jest **zdarzeniowy**: brak wylogowania nie znaczy, że użytkownik nadal pracuje (stacja mogła zostać wyłączona). Lista zdarzeń nie jest listą aktywnych sesji.

### AdUserDto — konto AD

| Pole | Typ | Znaczenie |
|---|---|---|
| `samAccountName` | string | login |
| `displayName` | string? | nazwa wyświetlana |
| `email` | string? | adres |
| `enabled` | bool | czy konto jest włączone |
| `groups` | string[] | nazwy grup (docelowo `tokenGroups`, z rozwiązanymi nazwami) |
| `ouDistinguishedName` | string? | jednostka organizacyjna konta |
| `lastLogonAtUtc` | DateTimeOffset? | `lastLogonTimestamp` — **może być opóźniony do 14 dni** |

### DataFreshnessDto — zaufanie do danych

| Pole | Typ | Znaczenie |
|---|---|---|
| `source` | string | źródło (`Active Directory`, `Dzienniki zabezpieczeń stacji`…) |
| `observedAtUtc` | DateTimeOffset? | czas obserwacji danych po stronie serwera |
| `isStale` | bool | czy wiek przekroczył próg z konfiguracji |
| `completeness` | `complete` \| `partial` \| `unavailable` | czy dane są pełne |
| `note` | string? | ludzkie wyjaśnienie („źródło pokrywa ostatnie 14 dni”) |

Ten typ jest obowiązkowy w każdej odpowiedzi — to on pozwala UI napisać „dane z 10:42, częściowe” zamiast sugerować aktualność.

## 2. Progi świeżości

| Źródło | Klucz konfiguracji | Domyślnie | Skutek przekroczenia |
|---|---|---|---|
| Stan komputerów | `ZseAdmin:Freshness:ComputersSeconds` | 120 s | `isStale = true`, pasek ostrzeżenia |
| Logowania | `ZseAdmin:Freshness:LoginsSeconds` | 900 s | `isStale = true`, informacja o zakresie |
| Katalog AD / konta | `ZseAdmin:Freshness:DirectorySeconds` | 3600 s | `isStale = true` |

Progi są celowo krótkie: lepiej pokazać „nieaktualne” niż przekonać nauczyciela, że stan jest bieżący.

## 3. Mapowanie na Active Directory

| Element aplikacji | Źródło AD / systemu | Uwagi |
|---|---|---|
| Pracownia | konfiguracja (`classrooms.json`) | nie wynika z AD; relacja jawna, walidowana przy starcie (ADR-0005) |
| Komputer | `objectCategory=computer`, `cn`, `dNSHostName`, `operatingSystem`, `lastLogonTimestamp` | filtr po OU z konfiguracji |
| Użytkownik | `objectCategory=person` + `objectClass=user`, `sAMAccountName`, `displayName`, `mail`, `userAccountControl` | `enabled` = brak `ACCOUNTDISABLE` (0x2); `lastLogonAtUtc` z `lastLogonTimestamp` (FILETIME) |
| Grupy | `tokenGroups` lub `memberOf` | `tokenGroups` uwzględnia zagnieżdżenie; SID-y rozwiązywane jednym zapytaniem, nierozwiązane wracają jako `S-1-5-…` |
| Stan / IP | ICMP, SMB 445, WMI `Win32_PingStatus`, ARP/DNS | zapisać metodę i czas obserwacji |
| Logowania | dziennik zabezpieczeń stacji: 4624, 4634, 4625 (opcjonalnie 4648) | wymaga „Event Log Readers” na stacji; etap M2 — do tego czasu `unavailable` |

**Ograniczenia, które muszą być widoczne w UI:**

- `lastLogonTimestamp` jest replikowany leniwie (do 14 dni) — nie jest „ostatnim logowaniem” w sensie co do minuty.
- `lastLogon` jest dokładny tylko na tym DC, na którym nastąpiło logowanie.
- Dzienniki stacji przewijają się po osiągnięciu limitu rozmiaru — starsze zdarzenia przepadają.
- ICMP bywa blokowany; „offline” może znaczyć „zablokowany ping”.

## 4. Semantyka braków

| Sytuacja | Reprezentacja | Czego UI nie może napisać |
|---|---|---|
| Nie sprawdzono stanu | `status=unknown`, `statusObservedAtUtc=null`, `isStale=true` | „offline” |
| Sprawdzono, brak odpowiedzi | `status=offline`, `statusObservedAtUtc=<czas>` | „komputer nie istnieje” |
| Brak dostępu do dziennika stacji | `completeness=unavailable`, `note` | „brak logowań” |
| Dzienniki pokrywają krótszy zakres | `completeness=partial`, `note` z zakresem | „pełna historia” |
| Zerwane połączenie klient–serwer | stan sesji w UI | „dane aktualne” |

## 5. Retencja

| Dane | Retencja docelowa | Uzasadnienie |
|---|---|---|
| Zdarzenia logowań w aplikacji | 30 dni (do potwierdzenia przez DPO) | wystarcza do wyjaśnienia sprawy, ogranicza profilowanie |
| Cache stanu komputerów | w pamięci, nadpisywany | dane techniczne |
| Logi operacji administracyjnych | 12 miesięcy | rozliczalność |

Szczegóły prawne: `docs/uprawnienia-i-dane-osobowe.md`.
