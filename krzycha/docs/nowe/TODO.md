# Plan Działań (TODO): Diagramy oraz Szkielet Prezentacji (7 minut)

Ten dokument zawiera:
1. **Szczegółową listę zmian do naniesienia w diagramach i dokumentacji projektowej**, aby odzwierciedlały w 100% aktualny stan kodu.
2. **Gotowy szkielet 7-minutowej prezentacji** (z podziałem na slajdy, czas co do minuty, talking points i sugerowane grafiki).

---

# Część 1: TODO – Aktualizacja Diagramów i Dokumentacji

Po ostatnich zmianach w kodzie (usunięcie martwego pola `Price`, dodanie serwisu anulowania `CancelPackageAsync` oraz naprawa wygaszania nieodebranych paczek `Delivered`), należy zaktualizować poniższe pliki w katalogu `docs/`:

### 1. Diagram Klas (`docs/nowe/class/class-diagram.puml`)
- [ ] **Klasa `Package`**:
  - Zmienić sygnaturę metody:
    - Było: `+PackageItem AddItem(string name, decimal price)`
    - Jest: `+PackageItem AddItem(string name)`
  - Upewnić się, że metoda `Deliver()` w opisie/notatce aktualizuje czas wygaśnięcia `ExpiredAt = DeliveredAt + 48h`.
  - W metodzie `Expire()` zaktualizować warunek (dozwolona dla `Accepted`, `Shipped`, `Delivered` po upływie deadline).
- [ ] **Klasa `PackageItem`**:
  - Usunąć atrybut `+decimal Price`. Klasa ma mieć wyłącznie: `Id`, `PackageId`, `Package`, `Name`, `ReturnPackageId`, `ReturnPackage`, `IsReturned`.
- [ ] **Interfejs `IPackageOrderService` i klasa `PackageOrderService`**:
  - Dodać nową metodę:
    ```plantuml
    +Task CancelPackageAsync(int packageId, int? requestingUserId = null)
    ```
  - Dodać relację asocjacji/zależności `PackageOrderService` do `INotificationService` (wstrzyknięcie notyfikacji).
- [ ] **Interfejs `IPackageRepository` i klasa `PackageRepository`**:
  - W metodzie `GetPackagesRequiringExpirationAsync()` dodać w opisie/stereotypie uwzględnienie statusu `Delivered` (obok `Accepted` i `Shipped`).
- [ ] **Ponowne wygenerowanie pliku PNG**:
  - Wygenerować zaktualizowany `class-diagram.png` z pliku `.puml`.

---

### 2. Diagramy ERD (`docs/nowe/er/`)
- [ ] **Plik PlantUML (`docs/nowe/er/er-diagram.puml`)**:
  - W encji `PackageItem` usunąć wiersz:
    ```plantuml
    * Price : DECIMAL(10,2)
    ```
- [ ] **Plik Draw.io (`docs/nowe/er/IO - diagram ER - rozszerzony.drawio` oraz `.drawio.xml`)**:
  - Otworzyć diagram w narzędziu Draw.io (lub rozszerzeniu VS Code).
  - W tabeli `PackageItem` usunąć wiersz z atrybutem `Price` (`DECIMAL(10,2)`).
  - Skorygować wysokość ramki tabeli po usunięciu wiersza.
  - Wyeksportować zaktualizowany plik graficzny `IO - diagram ER - rozszerzony-2.png`.

---

### 3. Diagramy Sekwencji (`docs/nowe/sekwencje/sequence-diagrams.puml`)
- [ ] **Diagram 01 – Zamówienie (`01-order-and-payment`)**:
  - W komunikacie od Klienta:
    - Było: `Sender -> Web: ... and define items list [name, price]`
    - Powinno być: `Sender -> Web: ... and define items list [name]`
  - W zapytaniu do bazy:
    - Było: `OrderSvc -> DB: INSERT Package(...) + INSERT PackageItems(PackageId, Name, Price)`
    - Powinno być: `OrderSvc -> DB: INSERT Package(...) + INSERT PackageItems(PackageId, Name)`
- [ ] **Sekwencja Anulowania (nowy fragment / alternatywa w Diagramie 01 lub 03)**:
  - Dodać interakcję anulowania przez serwis:
    ```plantuml
    Sender -> Web: Anuluj zamówienie (Cancel)
    Web -> OrderSvc: CancelPackageAsync(packageId, senderId)
    OrderSvc -> PackageRepo: GetByIdAsync(packageId)
    OrderSvc -> Package: Cancel()
    note right: Sprawdza czy status to Ordered/Accepted.\nZmienia status na Cancelled.
    OrderSvc -> PackageRepo: UpdateAsync(package)
    note right: Zwolnienie rezerwacji skrytek w bazie
    OrderSvc -> NotificationSvc: NotifyAsync("system", ...)\ni opcjonalnie NotifyAsync(sender.Email, ...)
    Web <-- OrderSvc: Sukces anulowania
    ```
- [ ] **Diagram 05 – Wygaszanie nieodebranej paczki po 48 h**:
  - Dodać lub uzupełnić ścieżkę tła:
    `Hangfire -> AcceptanceSvc: ProcessExpiredPackagesAsync()`
    `AcceptanceSvc -> PackageRepo: GetPackagesRequiringExpirationAsync()` (wybiera paczki `Delivered`, dla których `ExpiredAt < now`)
    `AcceptanceSvc -> Package: Expire()`
    `AcceptanceSvc -> PackageRepo: UpdateAsync(package)`
    `AcceptanceSvc -> NotificationSvc: NotifyAsync("system", "Package expired...")`
- [ ] **Ponowne wygenerowanie grafik sekwencji**:
  - Zaktualizować odpowiednie pliki PNG w `docs/nowe/sekwencje/`.

---

### 4. Dokumentacja LaTeX (`docs/dokumentacja_zmian.tex`)
- [ ] **Rozdział 2 (Wprowadzone Zmiany)**:
  - W tabeli plików:
    - W wierszu `PackageItem.cs` usunąć `\texttt{Price}`.
    - W wierszu `IPackageOrderService.cs` dopisać informację o dodaniu metody `CancelPackageAsync`.
    - W wierszu `PackageRepository.cs` dopisać informację o uwzględnieniu statusu `Delivered` w wygaszaniu.
- [ ] **Rozdział 3 (Aktualizacja Diagramów)**:
  - Zaktualizować opis diagramu 01 (zamiana `[name, price]` na `[name]`).
  - Dodać krótki podpunkt o obsłudze przypadku użycia anulowania przez `CancelPackageAsync`.
- [ ] **Kompilacja PDF**:
  - Przekompilować `dokumentacja_zmian.tex` do `dokumentacja_zmian.pdf` za pomocą `pdflatex` / TeXStudio.

---

# Część 2: Szkielet i Scenariusz Prezentacji (7 Minut)

Prezentacja powinna skupić się na inżynierskiej jakości rozwiązania, eliminacji długu technicznego oraz eleganckim rozszerzeniu architektury o proces zwrotów.

### Harmonogram czasowy:
- **0:00 – 1:00 (1 min)**: Wstęp, domena i architektura projektu.
- **1:00 – 2:30 (1.5 min)**: Zadanie 1 – Kompleksowa weryfikacja i testy workflow (WF01–WF32).
- **2:30 – 3:30 (1 min)**: Zmiany i eliminacja błędów w zastanym kodzie (Refactoring & Bugfixes).
- **3:30 – 5:30 (2 min)**: Zadanie 2 – Nowa funkcjonalność: Zwrot częściowy i wieloetapowy.
- **5:30 – 6:30 (1 min)**: Prezentacja UI (Blazor) i wyniki testów.
- **6:30 – 7:00 (0.5 min)**: Podsumowanie, spełnienie wymagań i wnioski.

---

### Szczegółowy konspekt slajd po slajdzie:

#### Slajd 1: Tytuł i Wprowadzenie (Czas: 0:00 – 0:45)
- **Tytuł**: System Obsługi Paczkomatów – Weryfikacja Cyklu Logistycznego oraz Moduł Zwrotów Towarów
- **Treść slajdu**:
  - Skład zespołu i rola w projekcie.
  - Cel: weryfikacja poprawności przepływów logistycznych (workflow) oraz rozbudowa o model handlowy i obsługę zwrotów.
  - Stack technologiczny: .NET 8, C#, Clean Architecture / DDD, EF Core + MySQL, Blazor Server, Hangfire.
- **Co mówić (Talking Points)**:
  - *„Dzień dobry. Nasz projekt dotyczy systemu logistycznego dla sieci paczkomatów. W ramach drugiego etapu skupiliśmy się na dwóch filarach: po pierwsze, na rygorystycznym przetestowaniu pełnego cyklu życia przesyłki i usunięciu błędów architektonicznych, a po drugie – na implementacji nowej funkcjonalności: bezpiecznych, elastycznych zwrotów produktów przez klientów.”*

---

#### Slajd 2: Zadanie 1 – Kompleksowe Testy Workflow (Czas: 0:45 – 2:00)
- **Tytuł**: Strategia Testowania Procesów Biznesowych (WF01–WF32)
- **Treść slajdu**:
  - 32 rygorystyczne scenariusze testowe zorganizowane wg diagramów sekwencji (etapy 0–6).
  - Podejście: testy przepływu w architekturze czystej (Arrange-Act-Assert) z wykorzystaniem biblioteki Moq.
  - Testowanie całych przypadków użycia w serwisach aplikacyjnych, a nie tylko izolowanych pól.
  - Pokrycie sytuacji awaryjnych:
    - brak wolnych skrytek (transakcyjność zamówienia),
    - pusta skrytka przy odbiorze przez kuriera (błąd nadawcy),
    - nieodebranie paczki w 48 h (wygaszanie Hangfire),
    - nieautoryzowane próby odbioru lub anulowania.
- **Grafika na slajdzie**:
  - Schemat `method-call-flow.png` lub tabela zestawiająca etapy testów.
- **Co mówić (Talking Points)**:
  - *„Zamiast pisać trywialne testy getterów i setterów, opracowaliśmy 32 scenariusze workflow, które weryfikują współpracę serwisów, repozytoriów, bramki płatności i powiadomień. Każdy test weryfikuje pełne przejście stanu: od zamówienia, przez weryfikację skrytek i płatność, po transport i doręczenie. Szczególny nacisk położyliśmy na sytuacje awaryjne – co się dzieje, gdy w paczkomacie brakuje skrytki o danym gabarycie, gdy kurier zastanie pustą skrzynkę, albo gdy klient nie odbierze paczki w 48 godzin.”*

---

#### Slajd 3: Poprawki i Refaktoryzacja Kodu (Czas: 2:00 – 3:15)
- **Tytuł**: Wykryte Problemy i Podniesienie Jakości Kodu
- **Treść slajdu**:
  - **1. Pełny serwis anulowania (`CancelPackageAsync`)**:
    - Problem: testy wołały tylko domenowe `package.Cancel()`, brakowało obsługi w serwisie.
    - Rozwiązanie: dodano `CancelPackageAsync` do `PackageOrderService` z walidacją uprawnień nadawcy/operatora, zwolnieniem skrytek w repozytorium i notyfikacją Hangfire.
  - **2. Naprawa wygaszania nieodebranych paczek (`Delivered` -> `Expired`)**:
    - Problem: metoda `Deliver()` nie przestawiała zegara na 48 h, a repozytorium pomijało paczki w statusie `Delivered`.
    - Rozwiązanie: `Deliver()` odnawia okno 48 h, a zapytanie repozytorium wyłapuje przeterminowane paczki ze skrytek.
  - **3. Uporządkowanie modelu dziedzinowego (Zasada YAGNI)**:
    - Bezpieczne usunięcie martwego pola `Price` z `PackageItem` (system transportowy nie rozlicza ubezpieczeń ani faktur handlowych – opłata transportowa jest stała w `Payment`).
- **Co mówić (Talking Points)**:
  - *„Podczas audytu kodu zidentyfikowaliśmy istotne braki, które od razu poprawiliśmy. Klient nie miał w serwisach metody do anulowania zamówienia – stworzyliśmy `CancelPackageAsync`, która dba o autoryzację, zwolnienie skrytki i powiadomienia. Naprawiliśmy również krytyczny błąd wygaszania: paczki doręczone do paczkomatu nigdy nie wygasały, ponieważ repozytorium ignorowało status `Delivered`. Dodatkowo oczyściliśmy model ze zbędnego pola `Price`, które nie brało udziału w żadnej logice logistycznej.”*

---

#### Slajd 4: Zadanie 2 – Nowa Funkcjonalność: Zwrot Produktów (Czas: 3:15 – 4:30)
- **Tytuł**: Architektura Zwrotów – Rozróżnienie Paczki od Towaru
- **Treść slajdu**:
  - **Rozróżnienie pojęć (DDD)**:
    - `Package` – jednostka transportowa (gabaryt S/M/L, kod odbioru, transport kurierski).
    - `PackageItem` – jednostka handlowa (towar wewnątrz paczki: np. buty, spodnie).
  - **Zasada DRY (Brak redundancji logistycznej)**:
    - Zwrot nie tworzy „specjalnego serwisu kurierskiego dla zwrotów”.
    - Przesyłka zwrotna to zwykła instancja `Package` przechodząca przez ten sam pipeline logistyczny!
  - **Relacja rekurencyjna (`Self-Referencing Relationship`)**:
    - `OriginalPackageId` wskazuje na pierwotną przesyłkę.
    - Towar posiada klucz obcy `ReturnPackageId`.
- **Grafika na slajdzie**:
  - Fragment diagramu klas pokazujący asocjację `Package` -> `PackageItem` oraz rekurencyjne `ReturnDeliveries`.
- **Co mówić (Talking Points)**:
  - *„Drugim zadaniem było zaprojektowanie procesu zwrotów. Kluczową decyzją inżynierską było rozróżnienie jednostki transportowej od handlowej. Klient nie zwraca połowy kartonu, lecz konkretne przedmioty z zamówienia. Zamiast tworzyć redundantne serwisy i zdublowane tabele dla zwrotów, zastosowaliśmy zasadę DRY. Zwrot tworzy nową przesyłkę `Package`, która korzysta z istniejącej logistyki, rezerwacji skrytek i transportu kurierskiego.”*

---

#### Slajd 5: Proces Zwrotu Częściowego i Etapowego (Czas: 4:30 – 5:30)
- **Tytuł**: Obsługa Zwrotów Częściowych i Wieloetapowych
- **Treść slajdu**:
  - **Faza 1 – Autoryzacja i wybór**:
    - `GetAvailableItemsForReturnAsync(pkgId, customerId)` weryfikuje tożsamość odbiorcy i zwraca tylko produkty, dla których `IsReturned == false`.
  - **Faza 2 – Utworzenie przesyłki powrotnej**:
    - `CreateReturnDeliveryAsync(...)` automatycznie odwraca routing (skrytka nadawcza staje się docelową dla sklepu).
    - Przypisuje `ReturnPackageId` tylko do zaznaczonych towarów.
  - **Niezależne zwroty wieloetapowe**:
    - Klient może zwrócić 1 z 3 produktów dzisiaj, a za tydzień drugi produkt w kolejnej, osobnej przesyłce zwrotnej.
  - **Statusy pomocnicze**: `HasReturns`, `AreAllItemsReturned` – bez naruszania pierwotnego statusu `Collected`.
- **Grafika na slajdzie**:
  - Fragment diagramu sekwencji zwrotu (`06-return-process.png`).
- **Co mówić (Talking Points)**:
  - *„Nasz moduł w pełni obsługuje zwroty częściowe i etapowe. Przykładowo, jeśli paczka zawierała 3 produkty, klient może zwrócić najpierw klawiaturę, a system utworzy dla niej przesyłkę zwrotną. Pozostałe 2 produkty nadal pozostają u klienta i za kilka dni mogą zostać zwrócone w kolejnej przesyłce. Metoda automatycznie zamienia paczkomaty rolami – klient nadaje zwrot w swoim paczkomacie, a sklep odbiera go w swoim.”*

---

#### Slajd 6: Demo Interfejsu i Wyniki Testów (Czas: 5:30 – 6:30)
- **Tytuł**: Prezentacja w UI (Blazor) oraz Wyniki Weryfikacji
- **Treść slajdu**:
  - Interfejs Blazor Server:
    - dynamiczne dodawanie pozycji przy zamówieniu paczki,
    - panel odbioru z generowaniem 6-cyfrowego kodu PIN,
    - formularz selekcji pozycji do zwrotu z natychmiastowym feedbackiem statusu (`U klienta` / `Zwrócony w #ID`).
  - **Wyniki testów xUnit**:
    - 32/32 scenariusze zaliczone (100% sukcesu),
    - czas wykonania wszystkich testów: **< 1 sekunda** (pełna izolacja in-memory, brak potrzeby zewnętrznych baz danych).
- **Grafika na slajdzie**:
  - Zrzut ekranu z aplikacji webowej (widok tabeli z pozycjami i przyciskami zwrotu) + zrzut zielonego paska testów `dotnet test`.
- **Co mówić (Talking Points)**:
  - *„Wszystkie te mechanizmy zintegrowaliśmy z interfejsem w Blazorze. Widzimy tutaj listę zadeklarowanych produktów, ich aktualny status oraz dynamiczny formularz zwrotu. Co najważniejsze: cały zestaw 32 scenariuszy wykonuje się w pamięci RAM w ułamku sekundy, co gwarantuje natychmiastowy feedback w pipeline CI/CD.”*

---

#### Slajd 7: Podsumowanie (Czas: 6:30 – 7:00)
- **Tytuł**: Podsumowanie i Wnioski Inżynierskie
- **Treść slajdu**:
  - Zrealizowano 100% wymagań obu zadań projektowych.
  - Czysta, modułowa architektura zgodna z zasadami DDD i SOLID.
  - Wyeliminowano dług techniczny i poprawiono błędy brzegowe w wygaszaniu oraz anulowaniu.
  - System jest w pełni gotowy na produkcyjne podpięcie fizycznych sterowników paczkomatów i bramek SMS/Email.
- **Co mówić (Talking Points)**:
  - *„Podsumowując: dostarczyliśmy stabilny, dokładnie przetestowany rdzeń logistyczny sieci paczkomatów. Dzięki rozróżnieniu paczki od towaru i ponownemu użyciu cyklu logistycznego, system jest elastyczny, łatwy w utrzymaniu i odporny na błędy. Dziękujemy za uwagę i zapraszamy do pytań.”*

---

### Wskazówki praktyczne do wystąpienia:
1. **Pilnowanie stopera**: Nie poświęcaj więcej niż 1 minuty na wstęp. Najważniejsze punkty dla oceniających to **Zadanie 1 (znalezione błędy i workflow testów)** oraz **Zadanie 2 (jak elegancko zrobiono zwrot częściowy)**.
2. **Płynne przejścia**: Podkreślaj, że poprawki (anulowanie, wygaszanie) wynikały bezpośrednio z rzetelnego podejścia do testów workflow.
3. **Pewność terminologii**: Używaj pojęć takich jak *Domain-Driven Design*, *zasada DRY*, *reverse logistics*, *rezerwacja atomowa*, *in-memory testing*.


### INFORMACJA DLA AI
Przeanalizuj cały projekt, edytuj TYLKO pliki zawarte w docs/nowe . Załóż, że aktualna wersja projektu jest w kodzie, a diagramy należy poprawić. Nie zmieniaj formatowania ani stylu/wyglądu diagramów. jeśli tak będzie łatwiej, generuj kod puml który użytkownik wklei do edytora i wyeksportuje diagram. poinformuj wtedy użytkownika o zastosowaniu takiego rozwiązania.