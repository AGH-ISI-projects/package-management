Zmiany w diagramach sekwencji:

01-order-and-payment:
Dodano definiowanie produktów zawartych w paczce
CreatePackageAsync przyjmuje items
Baza danych insertuje do PackageItems
Zwracany Package ma Items

Nowy diagram 06-customer-return-items

Zmiany w diagramie klas:

Nowa klasa PackageItem
Nowe pola w Package: OriginalPackageId, ReturnDeliveries, Items, addItem(), IsReturn, HasReturns, AreAllItemsReturned

Code flow:

Dodano opcję zwrotu (7. krok)

Diagram ER:

Dodano PackageItem

---

Oto zwięzłe podsumowanie architektoniczne i implementacyjne dodanej funkcji zwrotów:

---

### 1. Koncepcja architektoniczna
Zamiast tworzyć sztuczną, osobną tabelę `Returns` i dublować logikę logistyczną (osobne serwisy dla kurierów i skrytek), zrealizowaliśmy zwrot poprzez **relację rekurencyjną w modelu `Package`**. Przesyłka powrotna jest pełnoprawną paczką (`Package`) ze statusem `Ordered`, powiązaną z paczką pierwotną kluczem `OriginalPackageId`. Dzięki temu od momentu utworzenia zwrotu cały proces fizyczny (włożenie do skrytki, zatwierdzenie przez operatora, transport kuriera i odbiór przez sklep) realizowany jest przez **istniejący, standardowy potok logistyczny systemu**.

---

### 2. Rozszerzenie struktury danych
- **Nowa encja [`PackageItem`](file:///Users/marcelkosmala/Studia/io/package-management/krzycha/src/PackageDeliverySystem.Core/PackageItem.cs)**:
  Reprezentuje pojedynczy towar w paczce. Posiada klucz główny `Id`, dane produktu (`Name`, `Price`), klucz obcy do paczki pierwotnej (`PackageId`, relacja 1:N z kaskadowym usuwaniem) oraz opcjonalny klucz obcy do przesyłki zwrotnej (`ReturnPackageId`, relacja `0..1` z `SetNull`) wraz z właściwością obliczaną `IsReturned => ReturnPackageId.HasValue`.
- **Rozszerzenie encji [`Package`](file:///Users/marcelkosmala/Studia/io/package-management/krzycha/src/PackageDeliverySystem.Core/Package.cs)**:
  - Dodano kolekcję pozycji towarowych: `ICollection<PackageItem> Items`.
  - Dodano powiązanie rekurencyjne: `int? OriginalPackageId`, referencję `Package? OriginalPackage` oraz kolekcję przesyłek zwrotnych `ICollection<Package> ReturnDeliveries`.
  - Dodano właściwości pomocnicze: `IsReturn`, `HasReturns`, `AreAllItemsReturned` oraz metodę domenową `AddItem(name, price)`.
- **Warstwa bazy danych ([`DeliveryDbContext`](file:///Users/marcelkosmala/Studia/io/package-management/krzycha/src/PackageDeliverySystem.Infrastructure/Data/DeliveryDbContext.cs) / Migracje)**:
  Wprowadzono tabelę `PackageItems`, zmapowano relacje dwukierunkowe i zachowanie kluczy obcych (`Cascade` dla paczki pierwotnej, `SetNull` dla paczki zwrotnej) oraz eager loading dla pozycji w repozytoriach.

---

### 3. Nowe i zmodyfikowane komponenty / metody
- **Nowy serwis zwrotów ([`IPackageReturnService`](file:///Users/marcelkosmala/Studia/io/package-management/krzycha/src/PackageDeliverySystem.Core/IPackageReturnService.cs) / [`PackageReturnService`](file:///Users/marcelkosmala/Studia/io/package-management/krzycha/src/PackageDeliverySystem.Infrastructure/PackageReturnService.cs))**:
  - `GetAvailableItemsForReturnAsync(originalPackageId, customerId)`: Zwraca listę pozycji, które nie zostały jeszcze zwrócone (`!IsReturned`), weryfikując uprawnienia odbiorcy.
  - `CreateReturnDeliveryAsync(originalPackageId, customerId, itemIds, ...)`: 
    1. Waliduje uprawnienia klienta oraz status przesyłki (zwrot możliwy **tylko** po statusie `Collected`).
    2. Sprawdza, czy pozycje nie były wcześniej zwrócone.
    3. Automatycznie odwraca trasę i role (nadawcą staje się klient, odbiorcą sklep, paczkomatem nadawczym paczkomat klienta, a docelowym paczkomat sklepu).
    4. Wywołuje `_packageOrderService.CreatePackageAsync(...)` w celu rezerwacji wolnych skrytek (origin i destination).
    5. Powiązuje wybrane pozycje z nową paczką (`item.ReturnPackageId = returnPackage.Id`) i wysyła powiadomienie.
- **Modyfikacja serwisu zamówień ([`IPackageOrderService`](file:///Users/marcelkosmala/Studia/io/package-management/krzycha/src/PackageDeliverySystem.Core/IPackageOrderService.cs) / [`PackageOrderService`](file:///Users/marcelkosmala/Studia/io/package-management/krzycha/src/PackageDeliverySystem.Infrastructure/PackageOrderService.cs))**:
  - Rozszerzono metodę `CreatePackageAsync` o opcjonalny parametr `IEnumerable<PackageItem>? items = null`, co umożliwiło deklarowanie listy produktów zarówno przy standardowym nadaniu, jak i przy tworzeniu zwrotu.

---

### 4. Weryfikacja testowa
Mechanizm został w pełni przetestowany zestawem **10 dedykowanych scenariuszy jednostkowych i integracyjnych** ([`PackageReturnTests.cs`](file:///Users/marcelkosmala/Studia/io/package-management/krzycha/tests/PackageDeliverySystem.Tests/PackageReturnTests.cs) — scenariusze R01–R10, dające 13 przypadków xUnit), które gwarantują m.in. brak możliwości zwrotu przed odbiorem, blokadę ponownego zwrotu tego samego towaru, poprawność zwrotów częściowych i sekwencyjnych w czasie oraz nienaruszalność stanu w przypadku braku wolnych skrytek w paczkomacie. Cały pakiet 43 testów przechodzi w 100% pomyślnie.

# Opisy testów

Schemat każdego opisu: przygotowanie → akcja → weryfikacja.

## PackageReturnTests

- **R01 – Zwrot częściowy**: Przygotowuje odebraną paczkę z trzema produktami, zwraca jeden z nich, sprawdza że powstała paczka zwrotna, zwrócony produkt jest oznaczony, pozostałe nie, a oryginalna paczka nadal jest odebrana. Sprawdza też zapis i powiadomienie.
- **R02 – Zwrot przed odbiorem**: Tworzy paczki, które jeszcze nie zostały odebrane (zamówiona, zaakceptowana, w przygotowaniu, wysłana), próbuje zwrotu, sprawdza że w każdym przypadku jest błąd.
- **R03 – Zwrot przez obcego**: Przygotowuje odebraną paczkę, próbuje zwrotu przez osobę, która nie jest odbiorcą, sprawdza że jest błąd.
- **R04 – Podwójny zwrot**: Przygotowuje odebraną paczkę z produktem już oznaczonym jako zwrócony, próbuje zwrócić go ponownie, sprawdza że jest błąd.
- **R05 – Zwroty etapami**: Przygotowuje odebraną paczkę, w której jeden produkt był już zwrócony, zwraca drugi, sprawdza że trafia do nowej paczki zwrotnej, a trzeci nadal zostaje u klienta.
- **R06 – Odwrócone paczkomaty**: Przygotowuje odebraną paczkę z określonymi paczkomatami, zwraca produkt, sprawdza że paczka zwrotna jedzie od paczkomatu klienta do paczkomatu sklepu (odwrotnie niż oryginał).
- **R07 – Zwrot całości**: Przygotowuje odebraną paczkę, zwraca wszystkie trzy produkty naraz, sprawdza że paczka jest oznaczona jako w całości zwrócona.
- **R08 – Paczka z produktami**: Przygotowuje serwis zamówień i dwa produkty, tworzy paczkę z tą listą, sprawdza że produkty są w paczce (z nazwami i cenami) i że paczka została zapisana.
- **R09 – Dodanie produktu**: Tworzy pustą paczkę, dodaje do niej produkt, sprawdza że jest na liście z poprawną nazwą, ceną i przypisaniem do paczki.
- **R10 – Brak wolnej skrytki**: Przygotowuje odebraną paczkę z produktem i serwis zamówień, który przy tworzeniu paczki zwrotnej zgłasza brak wolnej skrytki w paczkomacie. Próbuje zwrócić produkt, sprawdza że jest błąd, produkt nie został oznaczony jako zwrócony, a nic nie zostało zapisane.

## PackageLifecycleTests

**Grupa A – cykl życia, anulowanie, płatność**

- **T01 – Pełny cykl życia**: Tworzy paczkę, opłaca ją i przeprowadza przez wszystkie etapy aż do odbioru, sprawdza że jest odebrana, wszystkie daty są ustawione w dobrej kolejności, a kod odbioru ma 6 cyfr.
- **T02 – Anulowanie zamówionej**: Tworzy nową paczkę, anuluje ją, sprawdza że ma status anulowana.
- **T03 – Anulowanie zaakceptowanej**: Tworzy paczkę i doprowadza ją do statusu zaakceptowana, anuluje ją, sprawdza że jest anulowana.
- **T04 – Anulowanie po wysyłce**: Tworzy paczkę i doprowadza ją do statusu wysłana, próbuje anulować, sprawdza że jest błąd, a status się nie zmienił.
- **T05 – Płatność z ponowieniem**: Przygotowuje paczkę i bramkę płatności, która za pierwszym razem zwraca porażkę, a za drugim sukces. Płaci dwa razy, sprawdza że pierwsza płatność jest nieudana, druga zakończona, i że poszło powiadomienie o potwierdzeniu.

**Grupa B – problemy po stronie kuriera i systemu**

- **T06 – Pusta skrytka**: Przygotowuje paczkę i kuriera, kurier zgłasza pustą skrytkę, sprawdza że paczka została anulowana i wysłano odpowiednie powiadomienie.
- **T07 – Nienadane paczki**: Przygotowuje dwie paczki z minionym terminem nadania, uruchamia zadanie w tle, sprawdza że obie zostały anulowane i dla każdej poszło powiadomienie.
- **T08 – Wygaśnięcie w transporcie**: Tworzy paczkę w statusie wysłana, ustawia termin transportu na przeszłość, wywołuje wygaśnięcie, sprawdza że paczka wygasła.
- **T09 – Doręczenie za wcześnie**: Tworzy paczkę w statusie zaakceptowana, próbuje ją doręczyć, sprawdza że jest błąd, status się nie zmienił i nie powstał kod odbioru.

**Grupa C – problemy po stronie odbiorcy**

- **T10 – Zły kod odbioru**: Tworzy nową paczkę, ustawia jej status na dostarczony, sprawdza czy próba odbioru ze złym kodem rzuca odpowiedni błąd.
- **T11 – Nieodebrana paczka**: Tworzy paczkę w statusie dostarczona, ustawia termin odbioru na przeszłość (minęło 48 h), wywołuje wygaśnięcie, sprawdza że paczka wygasła.
- **T12 – Odbiór przez obcego**: Tworzy dostarczoną paczkę, próbuje ją odebrać osoba, która nie jest odbiorcą, sprawdza że jest błąd, paczka dalej czeka na odbiór, a nic nie zostało zapisane ani wysłane.

**Grupa D – odrzucenia przez operatora**

- **T13 – Odrzucenie zamówionej**: Tworzy nową paczkę, operator ją odrzuca, sprawdza że paczka ma status odrzucona, została zapisana i poszło powiadomienie z operatorem.
- **T14 – Odrzucenie zaakceptowanej**: To samo co T13, ale dla paczki, która była już zaakceptowana.
- **T15 – Odrzucenie po wysyłce**: Tworzy paczkę w statusie wysłana, próbuje ją odrzucić, sprawdza że jest błąd, a status się nie zmienił.

**Grupa E – warunki wstępne i role**

- **T16 – Akceptacja bez nadania**: Tworzy opłaconą paczkę, która nie została nadana do skrytki, próbuje ją zaakceptować, sprawdza że jest błąd.
- **T17 – Akceptacja bez płatności**: Tworzy paczkę nadaną do skrytki, ale nieopłaconą, próbuje ją zaakceptować, sprawdza że jest błąd.
- **T18 – Powtórne potwierdzenie płatności**: Tworzy paczkę z już zakończoną płatnością, potwierdza płatność jeszcze raz, sprawdza że nic się nie dzieje: bez pytania bramki, bez zapisu i bez powiadomień.
- **T19 – Ocena kuriera**: Tworzy dostarczoną paczkę i próbuje ocenić kuriera: oceną za niską, za wysoką, przed odbiorem paczki i przez obcą osobę – sprawdza że każda próba daje błąd. Na końcu odbiera paczkę i sprawdza że poprawna ocena odbiorcy aktualizuje ranking kuriera.
- **T20 – Przypisanie za wcześnie**: Tworzy nową paczkę, próbuje przypisać do niej kuriera i operatora, sprawdza że obie próby kończą się błędem, a nikt nie został przypisany.
