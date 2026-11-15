using Moq;
using PackageDeliverySystem.Core;
using PackageDeliverySystem.Infrastructure;
using Xunit;

namespace PackageDeliverySystem.Tests;

public class PackageReturnTests
{
    private static Package CreateCollectedPackageWithItems(
        int packageId = 1,
        int senderId = 10,
        int recipientId = 20,
        int originBoxId = 101,
        int destBoxId = 202)
    {
        var package = new Package
        {
            Id = packageId,
            SenderId = senderId,
            RecipientId = recipientId,
            BoxId = originBoxId,
            DestinationBoxId = destBoxId,
            Size = BoxSize.Medium
        };

        package.Items.Add(new PackageItem { Id = 1, PackageId = packageId, Name = "Buty sportowe", Price = 199.99m });
        package.Items.Add(new PackageItem { Id = 2, PackageId = packageId, Name = "Spodnie jeansowe", Price = 129.99m });
        package.Items.Add(new PackageItem { Id = 3, PackageId = packageId, Name = "Czapka zimowa", Price = 49.99m });

        package.ConfirmDeposit();
        package.Payment = new Payment
        {
            PackageId = package.Id,
            PayerEmail = "customer@example.com",
            Amount = 14.99m
        };
        package.Payment.Complete();

        package.Accept();
        package.Prepare();
        package.Ship();
        package.Deliver();
        package.Collect(package.PickupCode!);

        return package;
    }

    private static (
        Mock<IPackageRepository> PackageRepo,
        Mock<IBoxRepository> BoxRepo,
        Mock<IPackageOrderService> OrderService,
        Mock<INotificationService> NotificationService,
        PackageReturnService Service) CreateReturnServiceContext()
    {
        var packageRepo = new Mock<IPackageRepository>();
        var boxRepo = new Mock<IBoxRepository>();
        var orderService = new Mock<IPackageOrderService>();
        var notificationService = new Mock<INotificationService>();

        var service = new PackageReturnService(
            packageRepo.Object,
            boxRepo.Object,
            orderService.Object,
            notificationService.Object);

        return (packageRepo, boxRepo, orderService, notificationService, service);
    }

    // =========================================================================
    // SCENARIUSZ R01: Zwrot częściowy (1 z 3 produktów) - Happy Path
    // =========================================================================
    [Fact]
    public async Task R01_PartialReturn_SingleItem_CreatesNewReturnPackageAndSetsReturnPackageId()
    {
        // 1. Arrange (Przygotowanie stanu systemu)
        var (packageRepo, boxRepo, orderService, notificationService, returnService) = CreateReturnServiceContext();
        var package = CreateCollectedPackageWithItems(packageId: 1, senderId: 10, recipientId: 20);

        boxRepo.Setup(b => b.GetByIdAsync(202)).ReturnsAsync(new Box { Id = 202, ParcelLockerId = 2 });
        boxRepo.Setup(b => b.GetByIdAsync(101)).ReturnsAsync(new Box { Id = 101, ParcelLockerId = 1 });
        packageRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);

        var createdReturnPackage = new Package
        {
            Id = 99,
            SenderId = 20, // klient zwracający
            RecipientId = 10, // pierwotny nadawca (sklep)
            BoxId = 301,
            DestinationBoxId = 302,
            Size = BoxSize.Medium
        };

        orderService.Setup(o => o.CreatePackageAsync(20, 10, 2, 1, BoxSize.Medium, It.IsAny<IEnumerable<PackageItem>?>()))
            .ReturnsAsync(createdReturnPackage);

        // 2. Act (Właściwy test - klient zwraca tylko buty o Id = 1)
        var result = await returnService.CreateReturnDeliveryAsync(
            originalPackageId: 1,
            customerId: 20,
            itemIds: new[] { 1 });

        // 3. Assert (Weryfikacja rezultatu)
        Assert.NotNull(result);
        Assert.Equal(99, result.Id);
        Assert.Equal(1, result.OriginalPackageId);
        Assert.True(result.IsReturn);
        Assert.Equal(PackageStatus.Ordered, result.Status);

        // Sprawdzenie stanu produktów w pierwotnej paczce
        var returnedItem = package.Items.First(i => i.Id == 1);
        var unreturnedItem = package.Items.First(i => i.Id == 2);

        Assert.True(returnedItem.IsReturned);
        Assert.Equal(99, returnedItem.ReturnPackageId);
        Assert.False(unreturnedItem.IsReturned);
        Assert.Null(unreturnedItem.ReturnPackageId);

        // Pierwotna paczka pozostaje w statusie Collected, ale ma zarejestrowany zwrot częściowy
        Assert.Equal(PackageStatus.Collected, package.Status);
        Assert.True(package.HasReturns);
        Assert.False(package.AreAllItemsReturned);

        packageRepo.Verify(r => r.UpdateAsync(package), Times.Once);
        packageRepo.Verify(r => r.UpdateAsync(result), Times.Once);
        notificationService.Verify(n => n.NotifyAsync("system", It.Is<string>(s => s.Contains("Return delivery 99"))), Times.Once);
    }

    // =========================================================================
    // SCENARIUSZ R02: Blokada zwrotu przed odbiorem paczki
    // =========================================================================
    [Theory]
    [InlineData(PackageStatus.Ordered)]
    [InlineData(PackageStatus.Accepted)]
    [InlineData(PackageStatus.Preparing)]
    [InlineData(PackageStatus.Shipped)]
    public async Task R02_CreateReturnDelivery_BeforeCollection_ThrowsInvalidOperationException(PackageStatus nonCollectedStatus)
    {
        // 1. Arrange
        var (packageRepo, _, _, _, returnService) = CreateReturnServiceContext();
        var package = new Package
        {
            Id = 1,
            RecipientId = 20,
            SenderId = 10,
            BoxId = 101,
            DestinationBoxId = 202
        };
        package.Items.Add(new PackageItem { Id = 1, PackageId = 1, Name = "Produkt" });

        // Ustawienie statusu
        if (nonCollectedStatus != PackageStatus.Ordered)
        {
            package.ConfirmDeposit();
            package.Payment = new Payment { PackageId = 1, PayerEmail = "test@ex.com", Amount = 10 };
            package.Payment.Complete();
            package.Accept();
        }
        if (nonCollectedStatus == PackageStatus.Preparing || nonCollectedStatus == PackageStatus.Shipped)
        {
            package.Prepare();
        }
        if (nonCollectedStatus == PackageStatus.Shipped)
        {
            package.Ship();
        }

        packageRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);

        // 2. Act & 3. Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            returnService.CreateReturnDeliveryAsync(1, 20, new[] { 1 }));

        Assert.Contains("Only collected packages can be returned", ex.Message);
    }

    // =========================================================================
    // SCENARIUSZ R03: Blokada zwrotu przez nieuprawnioną osobę (nie-odbiorcę)
    // =========================================================================
    [Fact]
    public async Task R03_CreateReturnDelivery_ByNonRecipient_ThrowsInvalidOperationException()
    {
        // 1. Arrange
        var (packageRepo, _, _, _, returnService) = CreateReturnServiceContext();
        var package = CreateCollectedPackageWithItems(packageId: 1, recipientId: 20);
        packageRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);

        const int unauthorizedUserId = 999;

        // 2. Act & 3. Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            returnService.CreateReturnDeliveryAsync(1, customerId: unauthorizedUserId, new[] { 1 }));

        Assert.Equal("Only the package recipient can request a return.", ex.Message);
    }

    // =========================================================================
    // SCENARIUSZ R04: Blokada ponownego zwrotu tego samego przedmiotu
    // =========================================================================
    [Fact]
    public async Task R04_CreateReturnDelivery_ItemAlreadyReturned_ThrowsInvalidOperationException()
    {
        // 1. Arrange
        var (packageRepo, _, _, _, returnService) = CreateReturnServiceContext();
        var package = CreateCollectedPackageWithItems(packageId: 1, recipientId: 20);

        // Oznaczamy produkt 1 jako już zwrócony w innej paczce zwrotnej
        package.Items.First(i => i.Id == 1).ReturnPackageId = 55;

        packageRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);

        // 2. Act & 3. Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            returnService.CreateReturnDeliveryAsync(1, 20, new[] { 1 }));

        Assert.Equal("One or more selected items have already been included in a return delivery.", ex.Message);
    }

    // =========================================================================
    // SCENARIUSZ R05: Obsługa wielu zwrotów w czasie (zwrot kolejnego produktu później)
    // =========================================================================
    [Fact]
    public async Task R05_CreateReturnDelivery_SequentialReturns_AllowsReturningSecondItemInSeparatePackage()
    {
        // 1. Arrange
        var (packageRepo, boxRepo, orderService, _, returnService) = CreateReturnServiceContext();
        var package = CreateCollectedPackageWithItems(packageId: 1, senderId: 10, recipientId: 20);

        // Produkt 1 został już wcześniej zwrócony w paczce 101
        package.Items.First(i => i.Id == 1).ReturnPackageId = 101;

        boxRepo.Setup(b => b.GetByIdAsync(202)).ReturnsAsync(new Box { Id = 202, ParcelLockerId = 2 });
        boxRepo.Setup(b => b.GetByIdAsync(101)).ReturnsAsync(new Box { Id = 101, ParcelLockerId = 1 });
        packageRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);

        var secondReturnPackage = new Package
        {
            Id = 102,
            SenderId = 20,
            RecipientId = 10,
            Size = BoxSize.Medium
        };

        orderService.Setup(o => o.CreatePackageAsync(20, 10, 2, 1, BoxSize.Medium, It.IsAny<IEnumerable<PackageItem>?>()))
            .ReturnsAsync(secondReturnPackage);

        // 2. Act: Klient zwraca produkt 2 (Spodnie)
        var result = await returnService.CreateReturnDeliveryAsync(1, 20, new[] { 2 });

        // 3. Assert
        Assert.NotNull(result);
        Assert.Equal(102, result.Id);
        Assert.Equal(102, package.Items.First(i => i.Id == 2).ReturnPackageId);
        Assert.False(package.Items.First(i => i.Id == 3).IsReturned); // Czapka nadal u klienta
    }

    // =========================================================================
    // SCENARIUSZ R06: Poprawne odwrócenie paczkomatów (origin -> destination)
    // =========================================================================
    [Fact]
    public async Task R06_CreateReturnDelivery_ReversesLockers_OriginLockerIsCustomerPickupLocker()
    {
        // 1. Arrange
        var (packageRepo, boxRepo, orderService, _, returnService) = CreateReturnServiceContext();
        var package = CreateCollectedPackageWithItems(packageId: 5, senderId: 100, recipientId: 200, originBoxId: 10, destBoxId: 20);

        // Locker 1: paczkomat sklepu (nadawczy pierwotnie)
        // Locker 2: paczkomat klienta (odbiorczy pierwotnie)
        boxRepo.Setup(b => b.GetByIdAsync(20)).ReturnsAsync(new Box { Id = 20, ParcelLockerId = 2 });
        boxRepo.Setup(b => b.GetByIdAsync(10)).ReturnsAsync(new Box { Id = 10, ParcelLockerId = 1 });
        packageRepo.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(package);

        orderService.Setup(o => o.CreatePackageAsync(200, 100, 2, 1, BoxSize.Medium, It.IsAny<IEnumerable<PackageItem>?>()))
            .ReturnsAsync(new Package { Id = 77, SenderId = 200, RecipientId = 100 });

        // 2. Act
        await returnService.CreateReturnDeliveryAsync(5, 200, new[] { 1 });

        // 3. Assert: Zwrot jest nadawany w Lockerze 2 (klient) i kierowany do Lockera 1 (sklep)
        orderService.Verify(o => o.CreatePackageAsync(200, 100, 2, 1, BoxSize.Medium, It.IsAny<IEnumerable<PackageItem>?>()), Times.Once);
    }

    // =========================================================================
    // SCENARIUSZ R07: Zwrot wszystkich pozycji oznacza paczkę jako AreAllItemsReturned
    // =========================================================================
    [Fact]
    public async Task R07_CreateReturnDelivery_WhenAllItemsReturned_AreAllItemsReturnedBecomesTrue()
    {
        // 1. Arrange
        var (packageRepo, boxRepo, orderService, _, returnService) = CreateReturnServiceContext();
        var package = CreateCollectedPackageWithItems(packageId: 1, senderId: 10, recipientId: 20);

        boxRepo.Setup(b => b.GetByIdAsync(202)).ReturnsAsync(new Box { Id = 202, ParcelLockerId = 2 });
        boxRepo.Setup(b => b.GetByIdAsync(101)).ReturnsAsync(new Box { Id = 101, ParcelLockerId = 1 });
        packageRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);

        var returnPkg = new Package { Id = 88, SenderId = 20, RecipientId = 10 };
        orderService.Setup(o => o.CreatePackageAsync(20, 10, 2, 1, BoxSize.Medium, It.IsAny<IEnumerable<PackageItem>?>())).ReturnsAsync(returnPkg);

        // 2. Act: Zwrot wszystkich 3 produktów jednocześnie
        await returnService.CreateReturnDeliveryAsync(1, 20, new[] { 1, 2, 3 });

        // 3. Assert
        Assert.True(package.HasReturns);
        Assert.True(package.AreAllItemsReturned);
    }

    // =========================================================================
    // SCENARIUSZ R08: Tworzenie nowej przesyłki z wyznaczoną listą produktów
    // =========================================================================
    [Fact]
    public async Task R08_PackageOrderService_CreatePackageAsync_WithItems_SavesItemsInPackage()
    {
        // 1. Arrange
        var packageRepo = new Mock<IPackageRepository>();
        var boxRepo = new Mock<IBoxRepository>();
        var peopleService = new Mock<IPeopleService>();

        peopleService.Setup(p => p.GetUserAsync(10)).ReturnsAsync(new User { Id = 10, Name = "Sklep" });
        peopleService.Setup(p => p.GetUserAsync(20)).ReturnsAsync(new User { Id = 20, Name = "Klient" });

        boxRepo.Setup(b => b.FindFreeBoxAsync(1, BoxSize.Medium, null))
            .ReturnsAsync(new Box { Id = 101, ParcelLockerId = 1 });
        boxRepo.Setup(b => b.FindFreeBoxAsync(2, BoxSize.Medium, 101))
            .ReturnsAsync(new Box { Id = 202, ParcelLockerId = 2 });

        var orderService = new PackageOrderService(packageRepo.Object, boxRepo.Object, peopleService.Object);

        var declaredItems = new[]
        {
            new PackageItem { Name = "Klawiatura mechaniczna", Price = 349.99m },
            new PackageItem { Name = "Podkładka pod mysz", Price = 59.99m }
        };

        // 2. Act
        var package = await orderService.CreatePackageAsync(
            senderId: 10,
            recipientId: 20,
            originLockerId: 1,
            destinationLockerId: 2,
            size: BoxSize.Medium,
            items: declaredItems);

        // 3. Assert
        Assert.NotNull(package);
        Assert.Equal(2, package.Items.Count);
        Assert.Contains(package.Items, i => i.Name == "Klawiatura mechaniczna" && i.Price == 349.99m);
        Assert.Contains(package.Items, i => i.Name == "Podkładka pod mysz" && i.Price == 59.99m);
        packageRepo.Verify(r => r.AddAsync(package), Times.Once);
    }

    // =========================================================================
    // SCENARIUSZ R09: Dodawanie produktu do paczki metodą pomocniczą AddItem
    // =========================================================================
    [Fact]
    public void R09_Package_AddItem_CorrectlyAppendsItem()
    {
        // Arrange
        var package = new Package { Id = 1 };

        // Act
        var item = package.AddItem("Monitor 27 cali", 999.00m);

        // Assert
        Assert.Single(package.Items);
        Assert.Same(item, package.Items.First());
        Assert.Equal("Monitor 27 cali", item.Name);
        Assert.Equal(999.00m, item.Price);
        Assert.Equal(1, item.PackageId);
    }
}
