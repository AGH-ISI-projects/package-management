# Specyfikacja Architektury Systemu Przesyłek Kurierskich

Poniższa dokumentacja przedstawia strukturę klas, encji i modułów backendu (NestJS + TypeORM) dla systemu obsługi dostaw. Architektura opiera się na podziale na warstwę kontrolerów (obsługa zapytań HTTP/REST), warstwę serwisów (logika biznesowa) oraz warstwę encji bazodanowych, wspieranych przez bazę główną PostgreSQL (z rozszerzeniem PostGIS), bazę in-memory Redis oraz platformę Ably do komunikacji w czasie rzeczywistym.

## 1. Zmiany Architektoniczne i Dobre Praktyki (Zrewidowane)

1. **Ably zamiast WebSockets:** Backend (NestJS) działa w pełni bezstanowo. Serwis powiadomień publikuje zdarzenia do chmury Ably przez HTTP, a klienci łączą się bezpośrednio z Ably. Odciąża to serwer z konieczności utrzymywania tysięcy aktywnych socketów.
2. **Dual-Mode Telemetry (Geolokalizacja):**
   * **Tryb Wolny (Szukanie zlecenia):** Kurier wysyła ping co 10s do NestJS. Pozycja aktualizowana jest wyłącznie w Redisie (`GEOADD`) z 15-sekundowym TTL (tabela `courier_profiles` w PostgreSQL została całkowicie odciążona z kolumn lokalizacyjnych).
   * **Tryb Dostawy (Status `SHIPPING`):** Kurier publikuje pozycję GPS bezpośrednio na kanał Ably klienta (`tracking:order:{id}`). NestJS i Redis są całkowicie omijane, zapewniając płynne śledzenie na mapie.
3. **Konstrukcja Kontrolerów:** Metody kontrolerów rozróżniają pochodzenie danych. Identyfikatory przekazywane są w ścieżkach URL (`@Param`), obiekty w ciele żądania (`@Body`, np. DTO), filtry w query string (`@Query`), a tożsamość wyciągana jest z tokena JWT w nagłówku (`@CurrentUser`).

---

## 2. Moduły Główne (Kontrolery i Serwisy)

### Moduł Autoryzacji (Auth)
Odpowiada za rejestrację ról, logowanie i wydawanie tokenów JWT.

**`AuthController`**
* `register(dto: RegisterDto) -> Promise<AuthResponseDto>`: Rejestruje użytkownika i tworzy profil (zapis transakcyjny).
* `login(dto: LoginDto) -> Promise<AuthResponseDto>`: Weryfikuje dane logowania i zwraca tokeny (Access i Refresh).
* `refresh(dto: RefreshTokenDto) -> Promise<AuthTokensDto>`: Odświeża wygasły access token.

**`AuthService`**
* `registerUser(dto: RegisterDto) -> Promise<AuthResponseDto>`: Hashuje hasło i w razie potrzeby korzysta z geocodingu dla lokalizacji dostawcy.
* `validateUser(email: string, pass: string) -> Promise<UserEntity>`: Pobiera i weryfikuje użytkownika.
* `generateTokens(user: UserEntity) -> Promise<AuthTokensDto>`: Generuje nowe tokeny JWT z rolą (`role`) i identyfikatorem (`sub: user.id`) w payloadzie.

### Moduł Użytkowników (Users)
Zarządza danymi profilowymi i statusem kuriera.

**`UsersController`**
* `getMyProfile(currentUser: JwtPayload) -> Promise<UserProfileDto>`: Zwraca dane konta wraz z profilem VENDOR/COURIER.
* `updateProfile(currentUser: JwtPayload, dto: UpdateProfileDto) -> Promise<UserProfileDto>`: Aktualizuje m.in. numer telefonu, imię i nazwisko.
* `toggleCourierAvailability(currentUser: JwtPayload, dto: AvailabilityDto) -> Promise<CourierStatusDto>`: Aktywuje/dezaktywuje kuriera w systemie.

**`UsersService`**
* `findByIdWithProfile(userId: number) -> Promise<UserEntity>`: Pobiera użytkownika łącząc bazowe tabele (JOIN).
* `setCourierAvailability(courierId: number, isAvailable: boolean, initialLocation?: PointDto) -> Promise<void>`: Ustawia flagę w bazie PostgreSQL oraz w razie wyłączenia – usuwa z puli wolnych kurierów w Redisie.

### Moduł Katalogu (Products)
Obsługuje menu/ofertę dostawców.

**`ProductsController`**
* `getVendorsCatalog(query: SearchVendorsQueryDto) -> Promise<VendorSummaryDto[]>`: Wyszukiwanie dostawców, opcjonalnie uwzględniając odległość.
* `getVendorProducts(vendorId: number, includeInactive: boolean) -> Promise<ProductDto[]>`: Zwraca menu dostawcy.
* `createProduct(currentUser: JwtPayload, dto: CreateProductDto) -> Promise<ProductDto>`: Dodaje nowy produkt do oferty.
* `updateProduct(currentUser: JwtPayload, productId: number, dto: UpdateProductDto) -> Promise<ProductDto>`: Edytuje lub deaktywuje produkt (bez usuwania z bazy, by chronić historię).

**`ProductsService`**
* `create(vendorId: number, dto: CreateProductDto) -> Promise<ProductEntity>`
* `update(vendorId: number, productId: number, dto: UpdateProductDto) -> Promise<ProductEntity>`
* `validateAndLockProductsForOrder(vendorId: number, items: OrderItemInputDto[]) -> Promise<ValidatedOrderItem[]>`: Weryfikuje aktywność produktów i pobiera ich aktualną cenę na moment składania zamówienia.

### Moduł Zamówień (Orders)
Centralny automat stanów (State Machine) orkiestrujący cykl życia zlecenia.

**`OrdersController`**
* `createOrder(currentUser: JwtPayload, dto: CreateOrderDto) -> Promise<OrderCreatedResponseDto>`: Inicjuje proces, waliduje koszyk i zwraca URL do opłacenia.
* `getOrders(currentUser: JwtPayload, filter: OrderFilterDto) -> Promise<OrderDetailsDto[]>`: Zwraca listę zamówień per aktor.
* `getOrderById(currentUser: JwtPayload, orderId: number) -> Promise<OrderDetailsDto>`: Zwraca pełne szczegóły zamówienia (wymaga uprawnień udziałowca).
* `cancelOrderByClient(currentUser: JwtPayload, orderId: number) -> Promise<OrderDetailsDto>`: Stan `ORDERED` -> `CANCELLED`.
* `vendorAcceptOrder(currentUser: JwtPayload, orderId: number) -> Promise<OrderDetailsDto>`: Stan `ORDERED` -> `ACCEPTED`. Wyzwala szukanie kuriera.
* `vendorRejectOrder(currentUser: JwtPayload, orderId: number, dto: RejectReasonDto) -> Promise<OrderDetailsDto>`: Odrzucenie zamówienia (`REJECTED`) zlecające zwrot środków (Refund).
* `vendorStartPreparation(currentUser: JwtPayload, orderId: number) -> Promise<OrderDetailsDto>`: Stan `ACCEPTED` -> `PREPARATION`.
* `courierRespondToOffer(currentUser: JwtPayload, orderId: number, dto: CourierOfferResponseDto) -> Promise<void>`: Akceptacja/odrzucenie oferty w ramach 15s soft-locka z Redis.
* `courierPickupOrder(currentUser: JwtPayload, orderId: number) -> Promise<OrderDetailsDto>`: Stan `PREPARATION` -> `SHIPPING`.
* `courierDeliverOrder(currentUser: JwtPayload, orderId: number) -> Promise<OrderDetailsDto>`: Stan `SHIPPING` -> `DELIVERED`.
* `courierReportIssue(currentUser: JwtPayload, orderId: number, dto: IssueReportDto) -> Promise<void>`: Obsługa wyjątków (awaria/wypadek) – ponowne szukanie kuriera lub przejście w stan `DELIVERY_FAILED`.

**`OrdersService`**
* `create(...) -> Promise<{ order: OrderEntity, paymentUrl: string }>`
* `transitionState(...) -> Promise<OrderEntity>`: Sprawdza macierz przejść, zapisuje stan i triggeruje powiadomienia (Ably + DB).
* `initiateCourierMatching(orderId: number) -> Promise<void>`: Odpytuje Redis o kuriera i wysyła ofertę.
* `confirmCourierAssignment(orderId: number, courierId: number) -> Promise<void>`: Twardy zapis w PostgreSQL, usuwa wolnego kuriera z Redis.

### Moduł Geolokalizacji (Geolocation)
Integracja zewnętrzna (geokodowanie) i przestrzenna baza Redis (telemetria).

**`GeolocationController`**
* `pingLocation(currentUser: JwtPayload, dto: LocationPingDto) -> Promise<void>`: Odbiera ping GPS od wolnego kuriera (co 10s).
* `geocodePreview(query: AddressQueryDto) -> Promise<GeocodedAddressDto>`: Waliduje wpisany adres dla frontendu.

**`GeolocationService`**
* `geocodeAddress(address: string) -> Promise<PointDto>`
* `updateCourierLocationInRedis(courierId: number, point: PointDto) -> Promise<void>`: Komenda `GEOADD` + odświeżenie TTL `active`.
* `findAndLockNearestCourier(vendorLocation, orderId, excludedCouriers) -> Promise<number | null>`: Używa `GEOSEARCH` i skryptu LUA do założenia 15-sekundowego soft-locka na kandydata.
* `releaseCourierSoftLock(courierId: number, orderId: number) -> Promise<void>`
* `removeCourierFromActivePool(courierId: number) -> Promise<void>`
* `findNearbyVendors(clientPoint: PointDto, radiusMeters: number) -> Promise<VendorDistanceResult[]>`: Używa `ST_DWithin` oraz indeksu GIST `idx_vendor_location` w bazie PostGIS.

### Moduł Płatności (Payments)

**`PaymentsController`**
* `handleWebhook(signature: string, payload: PaymentWebhookPayloadDto) -> Promise<void>`: Asynchroniczne webhooki od operatora.
* `getPaymentStatus(currentUser: JwtPayload, orderId: number) -> Promise<PaymentDto>`

**`PaymentsService`**
* `createPaymentIntent(orderId: number, amount: number, clientEmail: string) -> Promise<string>`
* `processWebhookEvent(payload: PaymentWebhookPayloadDto) -> Promise<void>`: Aktualizuje status, a jeśli `COMPLETED` – informuje Dostawcę.
* `refundPayment(orderId: number) -> Promise<void>`: Zleca zwrot na kartę klienta.

### Moduł Ocen (Rating)

**`RatingController`**
* `createRating(currentUser: JwtPayload, orderId: number, dto: CreateRatingDto) -> Promise<RatingDto>`
* `getTargetRatings(targetUserId: number, query: PaginationDto) -> Promise<RatingListDto>`

**`RatingService`**
* `submitRating(clientId, orderId, dto) -> Promise<RatingEntity>`: Waliduje stan `DELIVERED`, pilnuje więzów unikalności (`uq_order_target_rating`) i przelicza średnią ocen (`AVG`) użytkownika docelowego.

### Moduł Powiadomień (Notifications)
Integracja Ably (wzorzec Pub/Sub) oraz trwała historia komunikatów.

**`NotificationController`**
* `getAblyToken(currentUser: JwtPayload) -> Promise<AblyTokenRequestDto>`: Podpisuje TokenRequest (zależny od roli) pozwalający klientowi łączyć się bezpiecznie z siecią Ably.
* `getMyNotifications(currentUser: JwtPayload, query: PaginationDto) -> Promise<NotificationDto[]>`: Historia alertów z bazy (do wglądu offline).
* `markAsRead(currentUser: JwtPayload, notificationId: number) -> Promise<void>`

**`NotificationService`**
* `createAblyTokenRequest(userId: number, role: UserRole) -> Promise<AblyTokenRequestDto>`
* `publishAndPersistMessage(...) -> Promise<void>`: Zapisuje rekord do `notifications` i wysyła asynchroniczny POST do API Ably.
* `sendCourierOfferAlert(...) -> Promise<void>`: Lotny komunikat dla kuriera (przydział), nietrafiający trwale do bazy.

---

## 3. Schematy Encji Bazodanowych (TypeORM)

Poniższe klasy reprezentują odwzorowanie relacyjnego schematu `schema.sql` na obiekty domenowe TypeScript (TypeORM). Współrzędne geograficzne `geography(Point, 4326)` są mapowane w kodzie na standardowy interfejs GeoJSON `Point` (`{ type: 'Point', coordinates: [longitude, latitude] }`).

### Typy wyliczeniowe (Enums)

* **`UserRole`**: `'CLIENT' | 'VENDOR' | 'COURIER'`
* **`OrderStatus`**: `'ORDERED' | 'ACCEPTED' | 'PREPARATION' | 'SHIPPING' | 'DELIVERED' | 'REJECTED' | 'CANCELLED' | 'DELIVERY_FAILED'`
* **`PaymentStatus`**: `'PENDING' | 'COMPLETED' | 'FAILED' | 'REFUNDED'` *(Uwaga: warto dopisać `'REFUNDED'` do więzów `CHECK` w tabeli `payments` w `schema.sql`)*
* **`TargetRole`**: `'VENDOR' | 'COURIER'`

---

### 3.1. `UserEntity` (Tabela: `users`)
Główna encja reprezentująca konto dowolnego użytkownika w systemie.

* **Pola:**
  * `id: number` – Klucz główny (`serial primary key`).
  * `email: string` – Unikalny adres e-mail (`varchar(100)`, `unique`, `not null`).
  * `passwordHash: string` – Hash hasła (`password_hash varchar(255)`, `not null`, kolumna z flagą `select: false` w ORM).
  * `role: UserRole` – Rola w systemie (`varchar(20)`, `not null`).
  * `firstName: string` – Imię (`first_name varchar(50)`, `not null`).
  * `lastName: string` – Nazwisko (`last_name varchar(50)`, `not null`).
  * `phone: string` – Numer telefonu (`varchar(20)`, `not null`).
  * `createdAt: Date` – Data utworzenia (`created_at timestamp`, `default current_timestamp`).
* **Relacje:**
  * `vendorProfile?: VendorProfileEntity` – `@OneToOne(() => VendorProfileEntity, vp => vp.user)`
  * `courierProfile?: CourierProfileEntity` – `@OneToOne(() => CourierProfileEntity, cp => cp.user)`
  * `clientOrders?: OrderEntity[]` – `@OneToMany(() => OrderEntity, o => o.client)`
  * `notifications?: NotificationEntity[]` – `@OneToMany(() => NotificationEntity, n => n.user)`

---

### 3.2. `VendorProfileEntity` (Tabela: `vendor_profiles`)
Profil rozszerzający konto użytkownika o roli `VENDOR` (współdzielony klucz główny 1:1). Posiada indeks przestrzenny GIST `idx_vendor_location`.

* **Pola:**
  * `userId: number` – Klucz główny i obcy (`user_id integer primary key`).
  * `companyName: string` – Nazwa lokalu/firmy (`company_name varchar(100)`, `not null`).
  * `location: Point` – Lokalizacja geograficzna lokalu (`geography(Point, 4326)`, `not null`).
  * `avgRating: number` – Średnia ocen (`avg_rating decimal(3,2)`, `default 0.00`).
* **Relacje:**
  * `user: UserEntity` – `@OneToOne(() => UserEntity, { onDelete: 'CASCADE' })`, `@JoinColumn({ name: 'user_id' })`
  * `products: ProductEntity[]` – `@OneToMany(() => ProductEntity, p => p.vendor)`
  * `orders: OrderEntity[]` – `@OneToMany(() => OrderEntity, o => o.vendor)`

---

### 3.3. `CourierProfileEntity` (Tabela: `courier_profiles`)
Profil rozszerzający konto użytkownika o roli `COURIER` (współdzielony klucz główny 1:1). Pozycja GPS na żywo jest przechowywana w bazie Redis, natomiast tabela przechowuje trwały stan konta.

* **Pola:**
  * `userId: number` – Klucz główny i obcy (`user_id integer primary key`).
  * `isAvailable: boolean` – Czy kurier jest gotowy do pracy (`is_available boolean`, `default false`).
  * `avgRating: number` – Średnia ocen (`avg_rating decimal(3,2)`, `default 0.00`).
* **Relacje:**
  * `user: UserEntity` – `@OneToOne(() => UserEntity, { onDelete: 'CASCADE' })`, `@JoinColumn({ name: 'user_id' })`
  * `orders: OrderEntity[]` – `@OneToMany(() => OrderEntity, o => o.courier)`

---

### 3.4. `ProductEntity` (Tabela: `products`)
Pozycja w katalogu (menu) danego dostawcy.

* **Pola:**
  * `id: number` – Klucz główny (`serial primary key`).
  * `vendorId: number` – ID dostawcy (`vendor_id integer`, `not null`).
  * `name: string` – Nazwa produktu (`varchar(100)`, `not null`).
  * `price: number` – Aktualna cena jednostkowa (`decimal(10,2)`, `check (price >= 0)`).
  * `isActive: boolean` – Flaga widoczności w ofercie (`is_active boolean`, `default true`).
* **Relacje:**
  * `vendor: VendorProfileEntity` – `@ManyToOne(() => VendorProfileEntity, vp => vp.products, { onDelete: 'CASCADE' })`, `@JoinColumn({ name: 'vendor_id' })`
  * `orderItems: OrderItemEntity[]` – `@OneToMany(() => OrderItemEntity, oi => oi.product)`

---

### 3.5. `OrderEntity` (Tabela: `orders`)
Centralna encja przechowująca stan zamówienia oraz dane adresowe dostawy.

* **Pola:**
  * `id: number` – Klucz główny (`serial primary key`).
  * `clientId: number` – ID klienta (`client_id integer`, `not null`).
  * `vendorId: number` – ID dostawcy (`vendor_id integer`, `not null`).
  * `courierId: number | null` – ID przypisanego kuriera (`courier_id integer`, `nullable`).
  * `status: OrderStatus` – Stan zamówienia (`order_status enum`, `default 'ORDERED'`).
  * `deliveryLocation: Point` – Punkt docelowy GPS (`delivery_location geography(Point, 4326)`, `not null`).
  * `deliveryAddress: string` – Tekstowy adres dostawy (`delivery_address varchar(100)`, `not null`).
  * `totalPrice: number` – Łączna kwota zamówienia (`total_price decimal(10,2)`, `check (total_price >= 0)`).
  * `createdAt: Date` – Czas złożenia zamówienia (`created_at timestamp`, `default current_timestamp`).
* **Relacje:**
  * `client: UserEntity` – `@ManyToOne(() => UserEntity, { onDelete: 'RESTRICT' })`, `@JoinColumn({ name: 'client_id' })`
  * `vendor: VendorProfileEntity` – `@ManyToOne(() => VendorProfileEntity, { onDelete: 'RESTRICT' })`, `@JoinColumn({ name: 'vendor_id' })`
  * `courier: CourierProfileEntity | null` – `@ManyToOne(() => CourierProfileEntity, { nullable: true, onDelete: 'SET NULL' })`, `@JoinColumn({ name: 'courier_id' })`
  * `items: OrderItemEntity[]` – `@OneToMany(() => OrderItemEntity, oi => oi.order, { cascade: true })`
  * `payment: PaymentEntity` – `@OneToOne(() => PaymentEntity, p => p.order)`
  * `ratings: RatingEntity[]` – `@OneToMany(() => RatingEntity, r => r.order)`
  * `notifications: NotificationEntity[]` – `@OneToMany(() => NotificationEntity, n => n.order)`

---

### 3.6. `OrderItemEntity` (Tabela: `order_items`)
Pozycja zamówienia zamrażająca cenę produktu w momencie zakupu.

* **Pola:**
  * `id: number` – Klucz główny (`serial primary key`).
  * `orderId: number` – ID zamówienia (`order_id integer`, `not null`).
  * `productId: number` – ID produktu (`product_id integer`, `not null`).
  * `quantity: number` – Liczba sztuk (`integer`, `check (quantity > 0)`).
  * `unitPrice: number` – Zamrożona cena jednostkowa (`unit_price decimal(10,2)`, `check (unit_price >= 0)`).
* **Relacje:**
  * `order: OrderEntity` – `@ManyToOne(() => OrderEntity, o => o.items, { onDelete: 'CASCADE' })`, `@JoinColumn({ name: 'order_id' })`
  * `product: ProductEntity` – `@ManyToOne(() => ProductEntity, { onDelete: 'RESTRICT' })`, `@JoinColumn({ name: 'product_id' })`

---

### 3.7. `PaymentEntity` (Tabela: `payments`)
Rejestr transakcji płatniczej powiązanej 1:1 z zamówieniem.

* **Pola:**
  * `id: number` – Klucz główny (`serial primary key`).
  * `orderId: number` – Unikalny klucz obcy zamówienia (`order_id integer`, `unique`, `not null`).
  * `amount: number` – Kwota transakcji (`decimal(10,2)`, `check (amount >= 0)`).
  * `status: PaymentStatus` – Status płatności (`varchar(20)`, `default 'PENDING'`).
  * `externalTxId: string | null` – Identyfikator z zewnętrznej bramki płatniczej (`external_tx_id varchar(100)`, `nullable`).
  * `updatedAt: Date` – Czas ostatniej zmiany statusu (`updated_at timestamp`, `default current_timestamp`).
* **Relacje:**
  * `order: OrderEntity` – `@OneToOne(() => OrderEntity, o => o.payment, { onDelete: 'CASCADE' })`, `@JoinColumn({ name: 'order_id' })`

---

### 3.8. `RatingEntity` (Tabela: `ratings`)
Ocena i komentarz wystawiony przez klienta dla dostawcy lub kuriera. Posiada ograniczenie unikalności `UNIQUE (order_id, target_role)`.

* **Pola:**
  * `id: number` – Klucz główny (`serial primary key`).
  * `orderId: number` – ID ocenianego zamówienia (`order_id integer`, `not null`).
  * `clientId: number` – ID wystawiającego klienta (`client_id integer`, `not null`).
  * `targetUserId: number` – ID ocenianego użytkownika (`target_user_id integer`, `not null`).
  * `targetRole: TargetRole` – Oceniana rola (`target_role varchar(20)`, `'VENDOR' | 'COURIER'`).
  * `score: number` – Ocena w skali 1–5 (`integer`, `check (score between 1 and 5)`).
  * `comment: string` – Treść komentarza (`varchar(50)`, `not null` – *zalecana zmiana w DDL na `varchar(500)`*).
  * `createdAt: Date` – Data wystawienia (`created_at timestamp`, `default current_timestamp`).
* **Relacje:**
  * `order: OrderEntity` – `@ManyToOne(() => OrderEntity, o => o.ratings, { onDelete: 'CASCADE' })`, `@JoinColumn({ name: 'order_id' })`
  * `client: UserEntity` – `@ManyToOne(() => UserEntity, { onDelete: 'CASCADE' })`, `@JoinColumn({ name: 'client_id' })`
  * `targetUser: UserEntity` – `@ManyToOne(() => UserEntity, { onDelete: 'CASCADE' })`, `@JoinColumn({ name: 'target_user_id' })`

---

### 3.9. `NotificationEntity` (Tabela: `notifications`)
Trwały zapis powiadomienia o zmianie statusu zamówienia, pozwalający na odczyt historii po zalogowaniu.

* **Pola:**
  * `id: number` – Klucz główny (`serial primary key`).
  * `userId: number` – ID adresata powiadomienia (`user_id integer`, `not null`).
  * `orderId: number` – ID powiązanego zamówienia (`order_id integer`, `not null`).
  * `message: string` – Treść komunikatu (`varchar(255)`, `not null`).
  * `isRead: boolean` – Czy odczytano (`is_read boolean`, `default false`).
  * `createdAt: Date` – Czas wygenerowania (`created_at timestamp`, `default current_timestamp`).
* **Relacje:**
  * `user: UserEntity` – `@ManyToOne(() => UserEntity, u => u.notifications, { onDelete: 'CASCADE' })`, `@JoinColumn({ name: 'user_id' })`
  * `order: OrderEntity` – `@ManyToOne(() => OrderEntity, o => o.notifications, { onDelete: 'CASCADE' })`, `@JoinColumn({ name: 'order_id' })`

---

## 4. Modele Danych Wejściowych (Request DTOs)

Obiekty te wykorzystują `class-validator` dla zachowania bezpieczeństwa i rygoru danych na wejściu API.

* **Auth & Użytkownicy:**
  * `RegisterDto`: `email`, `password`, `role`, `firstName`, `lastName`, `phone`, opcjonalnie: `companyName`, `address`.
  * `LoginDto`: `email`, `password`.
  * `RefreshTokenDto`: `refreshToken`.
  * `UpdateProfileDto`: Opcjonalne pola (imiona, telefon, firma).
  * `AvailabilityDto`: `isAvailable`, `currentLocation` (PointDto).
* **Produkty:**
  * `CreateProductDto` / `UpdateProductDto`: `name`, `price`, `isActive`.
* **Zamówienia:**
  * `CreateOrderDto`: `vendorId`, `deliveryAddress`, `items` (tablica obiektów `productId` i `quantity`).
  * `CourierOfferResponseDto`: `accepted` (boolean).
  * `RejectReasonDto`: `reason` (string).
  * `IssueReportDto`: `type` (enum awarii), `description`.
* **Geolokalizacja:**
  * `PointDto`: `longitude`, `latitude`.
  * `LocationPingDto`: Rozszerza PointDto.
  * `AddressQueryDto`: `address` (string wyszukiwania).
  * `SearchVendorsQueryDto`: `longitude`, `latitude`, `radiusKm`.
* **Webhooki:**
  * `PaymentWebhookPayloadDto`: `externalTxId`, `status`, `amount`.
* **Oceny:**
  * `CreateRatingDto`: `targetRole` (VENDOR / COURIER), `score` (1-5), `comment` (do 50 znaków wg obecnego DDL, lub do 500 po korekcie).
* **Filtry / Inne:**
  * `PaginationDto`: `limit`, `offset`.
  * `OrderFilterDto`: `status`, `fromDate`, `toDate`.

---

## 5. Middleware & Guardy (Zabezpieczenia HTTP)

Klasy i dekoratory chroniące aplikację przed nieuprawnionym dostępem:

1. **`JwtAuthGuard`:** Weryfikuje strukturę i ważność nagłówka `Authorization: Bearer <token>`. Dołącza obiekt użytkownika zdekodowany z tokenu (`sub`, `role`) do obiektu Request jako argument dla `@CurrentUser()`.
2. **`RolesGuard`:** Wymaga dodania dekoratora `@Roles('CLIENT' | 'VENDOR' | 'COURIER')` na endpoincie. Odrzuca żądania z kodem 403 Forbidden, jeśli aktor wykonuje zapytanie niezgodne z jego interfejsem (np. klient nie może tworzyć produktów).
3. **`OrderParticipantGuard`:** Ochrona typu IDOR (Insecure Direct Object Reference). Wykorzystuje parametr URL `orderId`, by sprawdzić, czy zalogowany użytkownik widnieje w bazie jako `client_id`, `vendor_id`, `courier_id` zamówienia (lub ma założony soft-lock w Redisie przy przydziale).
4. **`WebhookSignatureGuard`:** Weryfikuje kryptograficzny Hash z użyciem `WEBHOOK_SECRET`, aby uniemożliwić atakującym ręczne wywoływanie webhooka ze sfabrykowanym statusem płatności `COMPLETED`.