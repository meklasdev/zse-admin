# ZSE-Admin

## Cel produktu

ZSE-Admin ma być wspólnym punktem pracy dla administratora IT i — w zakresie
przyznanych uprawnień — nauczyciela. Zamiast szukać informacji osobno w
narzędziach Active Directory, na serwerze i przy poszczególnych stacjach,
użytkownik ma szybko zobaczyć, co dzieje się w szkolnych pracowniach:
jakie są tam komputery, kto i kiedy się na nich logował oraz czy stacje są
dostępne w sieci.

Serwer działa na maszynie z Active Directory (Windows Server 2016) i komunikuje
się z klientem przez WebSocket. Aplikacja ma wspierać codzienną administrację
oraz prowadzenie lekcji; nie powinna sugerować, że dane są aktualne lub
kompletne, jeśli serwer nie może ich potwierdzić.

## Kiedy narzędzie daje największą wartość

- **Przed lekcją w pracowni:** nauczyciel lub administrator sprawdza, które
  komputery są widoczne w sieci i czy ich stan pozwala rozpocząć zajęcia.
- **Problem z kontem ucznia:** administrator odnajduje konto i jego
  przynależność do grup, a następnie może sprawdzić dostępne informacje o
  logowaniach. Każda zmiana w AD powinna być dostępna tylko osobom z
  odpowiednimi uprawnieniami.
- **Ustalenie, kto korzystał ze stacji:** administrator filtruje logowania
  według komputera, użytkownika i czasu, aby wyjaśnić problem lub odtworzyć
  przebieg zajęć.
- **Diagnoza niedostępnego komputera:** administrator sprawdza jego pracownię
  lub OU, ostatnio znany stan i adres IP, a następnie porównuje je z historią
  logowań. Brak bieżącego połączenia nie oznacza, że komputer nie istnieje ani
  że nie logowano się na nim wcześniej.

## Główne byty i przepływ informacji

1. **Pracownia ↔ OU:** OU opisuje położenie obiektu w Active Directory, a
   pracownia — szkolny, fizyczny kontekst. To nie musi być relacja jeden do
   jednego; przypisanie OU do pracowni wymaga jawnej reguły lub konfiguracji.
2. **OU / pracownia → komputer:** komputery należą do struktury AD i mogą być
   grupowane w widoku według pracowni. Warto rozróżniać lokalizację wynikającą
   z OU od faktycznej lokalizacji urządzenia.
3. **Komputer ↔ log logowania ↔ użytkownik AD:** zdarzenie logowania łączy
   rozpoznane konto z komputerem i czasem. Historia jest zdarzeniowa — opisuje
   zarejestrowane logowania, a niekoniecznie wszystkie sesje ani to, kto
   aktualnie siedzi przy komputerze.
4. **Komputer → stan / IP:** stan dostępności i adres IP są informacjami o
   komputerze, zwykle bieżącymi lub ostatnio znanymi. Należy pokazać czas
   ostatniej aktualizacji i odróżnić brak danych od stanu offline.
5. **Klient ↔ serwer:** serwer pobiera lub udostępnia dane AD, logi i informacje
   o stacjach; klient prezentuje je w kontekście wybranej pracowni, komputera
   lub użytkownika. WebSocket zapewnia kanał komunikacji, ale sam w sobie nie
   gwarantuje aktualności ani dostarczenia każdego zdarzenia.

Praktyczny przepływ: **pracownia / OU → komputer → zdarzenia logowania →
użytkownik AD**, z równoległym podglądem **stanu i IP komputera**. Użytkownik
powinien móc przejść od pracowni do stacji i jej logowań, a od logowania do
konta — bez utożsamiania bieżącego stanu urządzenia z jego historią.

## Priorytety i założenia

1. **Szybka orientacja w pracowniach:** łatwo znaleźć komputer i ocenić, czy
   można na nim polegać przed zajęciami.
2. **Wiarygodna historia:** przy logach widoczne są zakres czasu, źródło,
   kompletność oraz rozróżnienie między brakiem zdarzeń a brakiem dostępu do
   danych.
3. **Bezpieczne działania:** odczyt, modyfikacja AD i akcje na stacjach mają
   odrębne, jawne uprawnienia; interfejs nie pokazuje niedostępnej akcji jako
   wykonalnej.
4. **Czytelny status danych:** stan/IP mają oznaczenie świeżości, a awaria
   połączenia klient–serwer jest odróżniona od offline komputera.
5. **Użyteczność w czasie lekcji:** typowe informacje można znaleźć szybko,
   bez konieczności rozumienia całej struktury AD.

## Co doprecyzować przed projektowaniem ekranów

- Jak definiuje się pracownię i mapuje ją na jedną lub wiele jednostek OU?
  Co z komputerami wspólnymi, przeniesionymi lub poza znanym OU?
- Z jakiego źródła i jak długo dostępne są logi? Jakie typy logowań obejmują,
  jak często są pobierane i jak oznaczać luki, opóźnienie lub brak uprawnień?
- Czy historia ma opisywać udane logowania, nieudane próby, wylogowania czy
  aktywne sesje? Jak rozwiązywać zmiany nazw kont i komputerów?
- Jak często odświeżane są stan i IP? Czy IP jest bieżące, czy ostatnio
  zaobserwowane; jaki jest próg uznania urządzenia za offline?
- Jakie role będą korzystać z klienta i które dane oraz działania są dla nich
  dozwolone? Jak potwierdzać i audytować operacje zmieniające stan AD lub
  komputerów?
- Jak serwer sygnalizuje zerwane połączenie WebSocket, błąd pobierania danych
  i ponowne połączenie? Czy klient może bezpiecznie wznowić synchronizację?
- Jak ograniczać widoczność i retencję historii logowań, która może zawierać
  dane o aktywności uczniów i pracowników?

Te odpowiedzi powinny ustalić granice zaufania do danych oraz podstawowe
przypadki błędów, zanim zostanie zaprojektowana nawigacja i szczegóły widoków.
