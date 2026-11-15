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

    // R01: Zwrot częściowy (1 z 3 produktów) - Happy Path
    [Fact]
    public async Task R01_PartialReturn_SingleItem_CreatesNewReturnPackageAndSetsReturnPackageId()
    {
        var (packageRepo, boxRepo, orderService, notificationService, returnService) = CreateReturnServiceContext();
        var package = CreateCollectedPackageWithItems(packageId: 1, senderId: 10, recipientId: 20);

        boxRepo.Setup(b => b.GetByIdAsync(202)).ReturnsAsync(new Box { Id = 202, ParcelLockerId = 2 });
        boxRepo.Setup(b => b.GetByIdAsync(101)).ReturnsAsync(new Box { Id = 101, ParcelLockerId = 1 });
        packageRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);

        var createdReturnPackage = new Package
        {
            Id = 99,
            SenderId = 20,
            RecipientId = 10,
            BoxId = 301,
            DestinationBoxId = 302,
            Size = BoxSize.Medium
        };

        orderService.Setup(o => o.CreatePackageAsync(20, 10, 2, 1, BoxSize.Medium, It.IsAny<IEnumerable<PackageItem>?>()))
            .ReturnsAsync(createdReturnPackage);

        var result = await returnService.CreateReturnDeliveryAsync(
            originalPackageId: 1,
            customerId: 20,
            itemIds: new[] { 1 });

        Assert.NotNull(result);
        Assert.Equal(99, result.Id);
        Assert.Equal(1, result.OriginalPackageId);
        Assert.True(result.IsReturn);
        Assert.Equal(PackageStatus.Ordered, result.Status);

        var returnedItem = package.Items.First(i => i.Id == 1);
        var unreturnedItem = package.Items.First(i => i.Id == 2);

        Assert.True(returnedItem.IsReturned);
        Assert.Equal(99, returnedItem.ReturnPackageId);
        Assert.False(unreturnedItem.IsReturned);
        Assert.Null(unreturnedItem.ReturnPackageId);

        Assert.Equal(PackageStatus.Collected, package.Status);
        Assert.True(package.HasReturns);
        Assert.False(package.AreAllItemsReturned);

        packageRepo.Verify(r => r.UpdateAsync(package), Times.Once);
        packageRepo.Verify(r => r.UpdateAsync(result), Times.Once);
        notificationService.Verify(n => n.NotifyAsync("system", It.Is<string>(s => s.Contains("Return delivery 99"))), Times.Once);
    }

    // R02: Blokada zwrotu przed odbiorem paczki
    [Theory]
    [InlineData(PackageStatus.Ordered)]
    [InlineData(PackageStatus.Accepted)]
    [InlineData(PackageStatus.Preparing)]
    [InlineData(PackageStatus.Shipped)]
    public async Task R02_CreateReturnDelivery_BeforeCollection_ThrowsInvalidOperationException(PackageStatus nonCollectedStatus)
    {
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

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            returnService.CreateReturnDeliveryAsync(1, 20, new[] { 1 }));

        Assert.Contains("Only collected packages can be returned", ex.Message);
    }

    // R03: Blokada zwrotu przez nieuprawnioną osobę (nie-odbiorcę)
    [Fact]
    public async Task R03_CreateReturnDelivery_ByNonRecipient_ThrowsInvalidOperationException()
    {
        var (packageRepo, _, _, _, returnService) = CreateReturnServiceContext();
        var package = CreateCollectedPackageWithItems(packageId: 1, recipientId: 20);
        packageRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);

        const int unauthorizedUserId = 999;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            returnService.CreateReturnDeliveryAsync(1, customerId: unauthorizedUserId, new[] { 1 }));

        Assert.Equal("Only the package recipient can request a return.", ex.Message);
    }

    // R04: Blokada ponownego zwrotu tego samego przedmiotu
    [Fact]
    public async Task R04_CreateReturnDelivery_ItemAlreadyReturned_ThrowsInvalidOperationException()
    {
        var (packageRepo, _, _, _, returnService) = CreateReturnServiceContext();
        var package = CreateCollectedPackageWithItems(packageId: 1, recipientId: 20);

        package.Items.First(i => i.Id == 1).ReturnPackageId = 55;

        packageRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            returnService.CreateReturnDeliveryAsync(1, 20, new[] { 1 }));

        Assert.Equal("One or more selected items have already been included in a return delivery.", ex.Message);
    }

    // R05: Obsługa wielu zwrotów w czasie (zwrot kolejnego produktu później)
    [Fact]
    public async Task R05_CreateReturnDelivery_SequentialReturns_AllowsReturningSecondItemInSeparatePackage()
    {
        var (packageRepo, boxRepo, orderService, _, returnService) = CreateReturnServiceContext();
        var package = CreateCollectedPackageWithItems(packageId: 1, senderId: 10, recipientId: 20);

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

        var result = await returnService.CreateReturnDeliveryAsync(1, 20, new[] { 2 });

        Assert.NotNull(result);
        Assert.Equal(102, result.Id);
        Assert.Equal(102, package.Items.First(i => i.Id == 2).ReturnPackageId);
        Assert.False(package.Items.First(i => i.Id == 3).IsReturned);

    // R06: Poprawne odwrócenie paczkomatów (origin -> destination)
    [Fact]
    public async Task R06_CreateReturnDelivery_ReversesLockers_OriginLockerIsCustomerPickupLocker()
    {
        var (packageRepo, boxRepo, orderService, _, returnService) = CreateReturnServiceContext();
        var package = CreateCollectedPackageWithItems(packageId: 5, senderId: 100, recipientId: 200, originBoxId: 10, destBoxId: 20);

        boxRepo.Setup(b => b.GetByIdAsync(20)).ReturnsAsync(new Box { Id = 20, ParcelLockerId = 2 });
        boxRepo.Setup(b => b.GetByIdAsync(10)).ReturnsAsync(new Box { Id = 10, ParcelLockerId = 1 });
        packageRepo.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(package);

        orderService.Setup(o => o.CreatePackageAsync(200, 100, 2, 1, BoxSize.Medium, It.IsAny<IEnumerable<PackageItem>?>()))
            .ReturnsAsync(new Package { Id = 77, SenderId = 200, RecipientId = 100 });

        await returnService.CreateReturnDeliveryAsync(5, 200, new[] { 1 });

        orderService.Verify(o => o.CreatePackageAsync(200, 100, 2, 1, BoxSize.Medium, It.IsAny<IEnumerable<PackageItem>?>()), Times.Once);
    }

    // R07: Zwrot wszystkich pozycji oznacza paczkę jako AreAllItemsReturned
    [Fact]
    public async Task R07_CreateReturnDelivery_WhenAllItemsReturned_AreAllItemsReturnedBecomesTrue()
    {
        var (packageRepo, boxRepo, orderService, _, returnService) = CreateReturnServiceContext();
        var package = CreateCollectedPackageWithItems(packageId: 1, senderId: 10, recipientId: 20);

        boxRepo.Setup(b => b.GetByIdAsync(202)).ReturnsAsync(new Box { Id = 202, ParcelLockerId = 2 });
        boxRepo.Setup(b => b.GetByIdAsync(101)).ReturnsAsync(new Box { Id = 101, ParcelLockerId = 1 });
        packageRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);

        var returnPkg = new Package { Id = 88, SenderId = 20, RecipientId = 10 };
        orderService.Setup(o => o.CreatePackageAsync(20, 10, 2, 1, BoxSize.Medium, It.IsAny<IEnumerable<PackageItem>?>())).ReturnsAsync(returnPkg);

        await returnService.CreateReturnDeliveryAsync(1, 20, new[] { 1, 2, 3 });

        Assert.True(package.HasReturns);
        Assert.True(package.AreAllItemsReturned);
    }

    // R08: Tworzenie nowej przesyłki z wyznaczoną listą produktów
    [Fact]
    public async Task R08_PackageOrderService_CreatePackageAsync_WithItems_SavesItemsInPackage()
    {
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

        var package = await orderService.CreatePackageAsync(
            senderId: 10,
            recipientId: 20,
            originLockerId: 1,
            destinationLockerId: 2,
            size: BoxSize.Medium,
            items: declaredItems);

        Assert.NotNull(package);
        Assert.Equal(2, package.Items.Count);
        Assert.Contains(package.Items, i => i.Name == "Klawiatura mechaniczna" && i.Price == 349.99m);
        Assert.Contains(package.Items, i => i.Name == "Podkładka pod mysz" && i.Price == 59.99m);
        packageRepo.Verify(r => r.AddAsync(package), Times.Once);
    }

    // R09: Dodawanie produktu do paczki metodą pomocniczą AddItem
    [Fact]
    public void R09_Package_AddItem_CorrectlyAppendsItem()
    {
        var package = new Package { Id = 1 };

        var item = package.AddItem("Monitor 27 cali", 999.00m);

        Assert.Single(package.Items);
        Assert.Same(item, package.Items.First());
        Assert.Equal("Monitor 27 cali", item.Name);
        Assert.Equal(999.00m, item.Price);
        Assert.Equal(1, item.PackageId);
    }

    // R10: Brak wolnej skrytki w paczkomacie podczas tworzenia zwrotu
    [Fact]
    public async Task R10_CreateReturnDelivery_WhenNoFreeBoxAvailable_ThrowsInvalidOperationExceptionAndItemsRemainUnreturned()
    {
        var (packageRepo, boxRepo, orderService, _, returnService) = CreateReturnServiceContext();
        var package = CreateCollectedPackageWithItems(1, 10, 20, 101, 202);

        packageRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);
        boxRepo.Setup(b => b.GetByIdAsync(202)).ReturnsAsync(new Box { Id = 202, ParcelLockerId = 2 });
        boxRepo.Setup(b => b.GetByIdAsync(101)).ReturnsAsync(new Box { Id = 101, ParcelLockerId = 1 });

        orderService.Setup(o => o.CreatePackageAsync(20, 10, 2, 1, BoxSize.Medium, null))
            .ThrowsAsync(new InvalidOperationException("No free Medium box at the chosen origin locker - pick another locker or size"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => returnService.CreateReturnDeliveryAsync(1, 20, new[] { 1 }));

        Assert.Contains("No free Medium box", ex.Message);

        var item1 = package.Items.First(i => i.Id == 1);
        Assert.False(item1.IsReturned);
        Assert.Null(item1.ReturnPackageId);
        packageRepo.Verify(r => r.UpdateAsync(It.IsAny<Package>()), Times.Never);
    }
}
