# Opisy testów – PackageWorkflowTests (wersja pogrupowana wg diagramów sekwencji)

Schemat każdego opisu: przygotowanie → akcja → weryfikacja.

## Etap 0: Scenariusz przekrojowy (diagramy 01–05)

- **WF01 – Pełny proces do oceny**: Przygotowuje nadawcę, odbiorcę, kuriera, operatora, skrytki i bramkę płatności, a potem przeprowadza paczkę przez cały proces: zamówienie, płatność, nadanie, akceptację, przypisanie kuriera, wysyłkę, doręczenie, odbiór kodem i ocenę kuriera oraz nadawcy. Sprawdza że paczka jest odebrana, wszystkie daty są w dobrej kolejności, kod ma 6 cyfr, płatność jest zakończona, oceny trafiły do rankingów i poszły powiadomienia.

## Etap 1: Zamówienie i płatność (diagram 01)

- **WF02 – Brak skrytek przy zamówieniu**: Przygotowuje serwis zamówień, w którym najpierw brakuje wolnej skrytki w paczkomacie nadawczym, a potem w docelowym. Próbuje zamówić paczkę w obu przypadkach, sprawdza że za każdym razem jest błąd, a żadna paczka nie została zapisana.
- **WF10 – Płatność z ponowieniem**: Przygotowuje paczkę i bramkę płatności, która za pierwszym razem zwraca porażkę, a za drugim sukces. Płaci dwa razy, sprawdza że po pierwszej płatność jest nieudana, po drugiej zakończona, i że poszło powiadomienie o potwierdzeniu.
- **WF14 – Powtórne potwierdzenie płatności**: Tworzy paczkę z już zakończoną płatnością, potwierdza płatność jeszcze raz, sprawdza że nic się nie dzieje: bez pytania bramki, bez zapisu i bez powiadomień.
- **WF22 – Wykluczenie skrytki w tym samym paczkomacie**: Sprawdza, czy przy wyborze tego samego paczkomatu jako nadawczy i odbiorczy system wyklucza skrytkę źródłową (`exclude: originBox.Id`) przy rezerwacji skrytki docelowej.
- **WF23 – Blokada podwójnej płatności**: Próbuje zainicjować drugą płatność dla paczki, która została już opłacona, sprawdza że zgłaszany jest błąd.
- **WF29 – Anulowanie na etapie zamówienia**: Przygotowuje paczkę w statusie `Ordered`, wywołuje anulowanie w serwisie zamówień (`PackageOrderService.CancelPackageAsync`), sprawdza że status zmienia się na `Cancelled`, paczka została zapisana w repozytorium i poszły powiadomienia.

## Etap 2: Potwierdzenie depozytu (diagram 02)

- **WF03 – Pusta skrytka (brak odbioru przez kuriera)**: Przygotowuje paczkę i kuriera, kurier zgłasza że skrytka jest pusta, sprawdza że paczka została anulowana, zapisana i poszło powiadomienie.
- **WF04 – Nienadanie w 48 h**: Przygotowuje paczkę z minionym terminem nadania, uruchamia zadanie w tle, sprawdza że paczka została anulowana, zapisana i poszło powiadomienie.
- **WF24 – Próba nadania po terminie**: Próbuje potwierdzić włożenie paczki do skrytki po upływie 48-godzinnego okna depozytu, sprawdza że rzucany jest wyjątek o wygaśnięciu rezerwacji.

## Etap 3: Decyzja operatora (diagram 03)

- **WF05 – Odrzucenie przez operatora**: Przygotowuje opłaconą i nadaną paczkę, operator ją odrzuca, a następnie ktoś próbuje ją wysłać. Sprawdza że paczka jest odrzucona, zapisana, poszło powiadomienie, a wysyłka kończy się błędem.
- **WF13 – Akceptacja bez warunków**: Tworzy paczkę, która jest nieopłacona albo niewłożona do skrytki, próbuje ją zaakceptować, sprawdza że w obu przypadkach jest właściwy błąd, a paczka nadal jest zamówiona.
- **WF16 – Przypisanie za wcześnie**: Tworzy nową paczkę, próbuje przypisać do niej kuriera i operatora, sprawdza że obie próby kończą się błędem, a nikt nie został przypisany.
- **WF25 – Niezgodność skrytki lub gabarytu**: Sprawdza odmowę akceptacji przy błędnym ID skrytki oraz gdy deklarowany gabaryt paczki przewyższa rozmiar skrytki.
- **WF30 – Anulowanie na etapie nadania/akceptacji**: Przygotowuje paczkę w statusie `Accepted`, wywołuje anulowanie w serwisie zamówień (`PackageOrderService.CancelPackageAsync`), sprawdza że status zmienia się na `Cancelled`, paczka została zapisana i poszło powiadomienie.

## Etap 4: Transport i doręczenie kurierskie (diagram 04)

- **WF06 – Zgubienie przez kuriera w transporcie**: Przygotowuje wysłaną paczkę, której termin transportu już minął, uruchamia obsługę wygasłych paczek, sprawdza że paczka wygasła, została zapisana i poszło powiadomienie.
- **WF09 – Blokada anulowania po wysyłce**: Tworzy paczkę i doprowadza ją do statusu wysłana (`Shipped`), próbuje anulować przez serwis zamówień (`CancelPackageAsync`), sprawdza że rzucany jest błąd (wyjątek), a status i baza się nie zmieniły.
- **WF11 – Doręczenie za wcześnie**: Tworzy paczkę w statusie zaakceptowana, próbuje ją doręczyć, sprawdza że jest błąd, status się nie zmienił i nie powstał kod odbioru.
- **WF12 – Odrzucenie po wysyłce**: Tworzy paczkę w statusie wysłana, próbuje ją odrzucić, sprawdza że jest błąd, a status się nie zmienił.

## Etap 5: Odbiór i ocena (diagram 05)

- **WF07 – Błędy przy odbiorze**: Przygotowuje doręczoną paczkę, próbuje ją odebrać obca osoba, a potem prawdziwy odbiorca ze złym kodem. Sprawdza że obie próby kończą się właściwym błędem, a paczka nadal czeka na odbiór.
- **WF15 – Ocena kuriera**: Tworzy doręczoną paczkę i próbuje ocenić kuriera oceną za niską, za wysoką, przed odbiorem paczki i przez obcą osobę – sprawdza że każda próba daje błąd. Na końcu odbiera paczkę i sprawdza że poprawna ocena odbiorcy trafia do rankingu kuriera.
- **WF26 – Ocena bez przypisanego kuriera**: Próbuje ocenić kuriera dla przesyłki, do której kurier nie został przypisany, sprawdza że rzucany jest właściwy wyjątek.
- **WF31 – Nieodebranie paczki przez klienta (upłynięcie 48 h)**: Przygotowuje doręczoną paczkę w paczkomacie, której minął termin odbioru, wywołuje wygaśnięcie, sprawdza że paczka zmienia status na `Expired`.

## Etap 6: Proces zwrotu produktów (diagram 06)

- **WF08 – Zwrot częściowy**: Przygotowuje odebraną paczkę z trzema produktami, pobiera listę produktów do zwrotu, zwraca jeden z nich i pobiera listę ponownie. Sprawdza że lista skróciła się z 3 do 2 produktów, zwrócony produkt jest oznaczony, paczka zwrotna jest powiązana z oryginałem, nadawca z odbiorcą i paczkomaty są zamienione, obie paczki zapisano, a poszło powiadomienie.
- **WF17 – Zwrot przed odbiorem**: Tworzy paczki, które jeszcze nie zostały odebrane (zamówiona, zaakceptowana, w przygotowaniu, wysłana), próbuje zwrotu, sprawdza że w każdym przypadku jest błąd.
- **WF18 – Zwrot przez obcego**: Przygotowuje odebraną paczkę, próbuje zwrotu przez osobę, która nie jest odbiorcą, sprawdza że jest błąd.
- **WF19 – Podwójny zwrot**: Przygotowuje odebraną paczkę z produktem już oznaczonym jako zwrócony, próbuje zwrócić go ponownie, sprawdza że jest błąd.
- **WF20 – Zwroty etapami**: Przygotowuje odebraną paczkę, w której jeden produkt był już zwrócony, zwraca drugi, sprawdza że trafia do nowej paczki zwrotnej, a trzeci nadal zostaje u klienta.
- **WF21 – Brak skrytki na zwrot**: Przygotowuje odebraną paczkę z produktem i serwis zamówień, który przy tworzeniu paczki zwrotnej zgłasza brak wolnej skrytki. Próbuje zwrócić produkt, sprawdza że jest błąd, produkt nie został oznaczony jako zwrócony, a nic nie zostało zapisane.
- **WF27 – Pobranie zwracalnych pozycji przez nieuprawnionego**: Sprawdza blokadę pobierania listy produktów do zwrotu przez osobę inną niż odbiorca.
- **WF28 – Walidacja pozycji przy zwrocie**: Sprawdza blokadę utworzenia zwrotu z pustą listą pozycji oraz z pozycją nienależącą do danej paczki.
- **WF32 – Zwrot całkowity**: Przygotowuje odebraną paczkę z trzema produktami, zwraca wszystkie naraz, sprawdza że paczka jest oznaczona jako w całości zwrócona (`AreAllItemsReturned == true`).
