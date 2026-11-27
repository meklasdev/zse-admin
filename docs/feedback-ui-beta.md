# Uwagi do interfejsu — ZSE-Admin beta

Wpisuj tutaj rzeczy, które w interfejsie są nieczytelne, niewygodne albo powinny działać inaczej. Możesz wypełnić jeden blok na każdą uwagę i skopiować go tyle razy, ile potrzeba. Nie musisz używać terminów technicznych — opisz po prostu, co widzisz i czego oczekujesz.

**Wydanie:** ZSE-Admin Beta 0.1.0-beta.1  
**Ekran:** klient WPF  
**Urządzenie / rozdzielczość (opcjonalnie):** 1920x1080, Windows 11  

## Ogólne wrażenia

- Co jest wygodne albo działa dobrze?
  - Ogólny podział przestrzeni na główne strefy robocze daje bazę pod dalszą rozbudowę, ale sam układ i brak diagramu wymagają zmian.
- Co najbardziej przeszkadza?
  - Zamiast graficznego diagramu sieci/pracowni jak z Cisco Packet Tracer pojawiły się zwykłe listy/tabele.
  - W dolnym pasku widnieje błąd połączenia z adresem WebSocket (`Only Uris starting with 'ws://' or 'wss://' are supported`).
  - W prawym panelu nakładają się zakładki i ucięte są napisy.
- Czego brakuje?
  - Wizualnego diagramu połączeń i urządzeń w pracowniach z czytelnym podglądem topologii.

## Uwagi do poprawy

### Uwaga 1

- **Ekran / miejsce w aplikacji:** Pracownie i komputery (główny obszar roboczy)
- **Co mi nie pasuje albo co się dzieje:** Pracownie nie są przedstawione w formie graficznego diagramu z węzłami i połączeniami (tak jak na zrzucie referencyjnym z Packet Tracera), tylko jako zwykły opis i płaska tabela komputerów.
- **Jak chciałbym/chciałabym, żeby to wyglądało lub działało:** Pracownie i przypisane do nich komputery powinny być ułożone w formie czytelnego diagramu topologii sieciowego/stanowiskowego (węzły, linie połączeń, podpisy/adresy/stany przy urządzeniach).
- **Dlaczego / w jakiej sytuacji to przeszkadza:** W diagramie od razu widać strukturę i powiązania stanowisk oraz sieci, czego nie daje sama tabela.
- **Ważność:** duża
- **Zrzut ekranu (jeśli jest):** image_89cd84.png, image_89ce20.png

---

### Uwaga 2

- **Ekran / miejsce w aplikacji:** Prawy panel — zakładki ("Logowania" / "Akcje i audyt")
- **Co mi nie pasuje albo co się dzieje:** Teksty zakładek nachodzą na siebie, a nazwy są ucięte i otoczone ramką fokusu.
- **Jak chciałbym/chciałabym, żeby to wyglądało lub działało:** Zakładki powinny mieć odpowiednią szerokość i odstępy, żeby napisy były w pełni czytelne i nie nachodziły na siebie ani na inne elementy.
- **Dlaczego / w jakiej sytuacji to przeszkadza:** Utrudnia przełączanie widoków i psuje czytelność prawego panelu.
- **Ważność:** duża
- **Zrzut ekranu (jeśli jest):** image_89cd84.png

---

### Uwaga 3

- **Ekran / miejsce w aplikacji:** Dolny pasek stanu
- **Co mi nie pasuje albo co się dzieje:** Wyświetla się błąd: `Brak połączenia: Only Uris starting with 'ws://' or 'wss://' are supported. (Parameter 'uri')`.
- **Jak chciałbym/chciałabym, żeby to wyglądało lub działało:** Adres połączenia powinien być poprawnie budowany z prefiksem `ws://` lub `wss://`, a komunikat błędu powinien być przyjazny i zrozumiały dla użytkownika.
- **Dlaczego / w jakiej sytuacji to przeszkadza:** Blokuje połączenie na żywo i pokazuje techniczny komunikat wyjątku na dole ekranu.
- **Ważność:** duża
- **Zrzut ekranu (jeśli jest):** image_89cd84.png

---

### Uwaga 4

- **Ekran / miejsce w aplikacji:** Górny pasek aplikacji
- **Co mi nie pasuje albo co się dzieje:** Widać status `niezidentyfikowany`, `Dane: —` oraz ciągłe `ponowne łączenie...`.
- **Jak chciałbym/chciałabym, żeby to wyglądało lub działało:** Status połączenia i użytkownika powinien jasno informować, w jakim trybie działa aplikacja i czy próba połączenia trwa, czy się nie powiodła.
- **Dlaczego / w jakiej sytuacji to przeszkadza:** Nie wiadomo, czy aplikacja po prostu nie ma połączenia z serwerem, czy trwa oczekiwanie na dane.
- **Ważność:** średnia
- **Zrzut ekranu (jeśli jest):** image_89cd84.png

---

## Jak przekazać uwagi

Zapisz uzupełniony plik albo skopiuj jego treść i odeślij ją w rozmowie. Jeśli dodajesz zrzut ekranu, dopisz nazwę pliku przy odpowiedniej uwadze.

**Prywatność:** beta używa danych fikcyjnych. Nie dodawaj prawdziwych haseł, tokenów ani zrzutów zawierających dane osobowe uczniów lub pracowników.
```[cite: 1, 2, 3]
