using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Moq;
using PackageDeliverySystem.Core;
using PackageDeliverySystem.Infrastructure;
using PackageDeliverySystem.Infrastructure.Data;
using Xunit;

namespace PackageDeliverySystem.Tests;

/// <summary>
/// Contains tests identified as missing from the original test suite:
/// - PackageReturnService query methods and validation edge cases
/// - PackageFulfillmentService orchestration methods (Assign, Ship, Deliver, Collect)
/// - PackageIntakeService physical deposit confirmation and payment edge cases
/// - PackageOrderService input validation and same-locker box collision avoidance
/// - RatingService sender rating and missing role guards
/// - SupportContactService support dispatch and invalid person handling
/// - Domain invariants (ConfirmDeposit deadline, MarkAsReturned, Payment transitions, ParcelLocker box management)
/// </summary>
public class MissingTests
{
    // SECTION 1: PackageReturnService - Query Methods & Validation Guards
    [Fact]
    public async Task ReturnService_GetAvailableItemsForReturnAsync_ReturnsOnlyUnreturnedItems()
    {
        var packageRepo = new Mock<IPackageRepository>();
        var boxRepo = new Mock<IBoxRepository>();
        var orderService = new Mock<IPackageOrderService>();
        var notificationService = new Mock<INotificationService>();

        var service = new PackageReturnService(
            packageRepo.Object, boxRepo.Object, orderService.Object, notificationService.Object);

        var package = new Package { Id = 1, RecipientId = 42 };
        package.Items.Add(new PackageItem { Id = 10, PackageId = 1, Name = "Item 1", ReturnPackageId = null });
        package.Items.Add(new PackageItem { Id = 11, PackageId = 1, Name = "Item 2", ReturnPackageId = 99 }); // already returned
        package.Items.Add(new PackageItem { Id = 12, PackageId = 1, Name = "Item 3", ReturnPackageId = null });

        packageRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);

        var available = await service.GetAvailableItemsForReturnAsync(1, 42);

        Assert.Equal(2, available.Count);
        Assert.Contains(available, i => i.Id == 10);
        Assert.Contains(available, i => i.Id == 12);
        Assert.DoesNotContain(available, i => i.Id == 11);
    }

    [Fact]
    public async Task ReturnService_GetAvailableItemsForReturnAsync_NonRecipient_ThrowsInvalidOperationException()
    {
        var packageRepo = new Mock<IPackageRepository>();
        var service = new PackageReturnService(
            packageRepo.Object, Mock.Of<IBoxRepository>(), Mock.Of<IPackageOrderService>(), Mock.Of<INotificationService>());

        var package = new Package { Id = 1, RecipientId = 42 };
        packageRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetAvailableItemsForReturnAsync(1, customerId: 999));

        Assert.Equal("Only the package recipient can query returnable items.", ex.Message);
    }

    [Fact]
    public async Task ReturnService_GetAvailableItemsForReturnAsync_PackageNotFound_ThrowsArgumentException()
    {
        var packageRepo = new Mock<IPackageRepository>();
        var service = new PackageReturnService(
            packageRepo.Object, Mock.Of<IBoxRepository>(), Mock.Of<IPackageOrderService>(), Mock.Of<INotificationService>());

        packageRepo.Setup(r => r.GetByIdAsync(404)).ReturnsAsync((Package?)null);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.GetAvailableItemsForReturnAsync(404, customerId: 42));
    }

    [Fact]
    public async Task ReturnService_GetReturnDeliveriesForPackageAsync_ReturnsMatchingDeliveries()
    {
        var packageRepo = new Mock<IPackageRepository>();
        var service = new PackageReturnService(
            packageRepo.Object, Mock.Of<IBoxRepository>(), Mock.Of<IPackageOrderService>(), Mock.Of<INotificationService>());

        var allPackages = new List<Package>
        {
            new Package { Id = 1 },
            new Package { Id = 100, OriginalPackageId = 1 },
            new Package { Id = 101, OriginalPackageId = 1 },
            new Package { Id = 102, OriginalPackageId = 2 }
        };

        packageRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(allPackages);

        var returns = await service.GetReturnDeliveriesForPackageAsync(1);

        Assert.Equal(2, returns.Count);
        Assert.All(returns, r => Assert.Equal(1, r.OriginalPackageId));
    }

    [Fact]
    public async Task ReturnService_CreateReturnDeliveryAsync_EmptyItemIds_ThrowsArgumentException()
    {
        var service = new PackageReturnService(
            Mock.Of<IPackageRepository>(), Mock.Of<IBoxRepository>(), Mock.Of<IPackageOrderService>(), Mock.Of<INotificationService>());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateReturnDeliveryAsync(1, customerId: 42, itemIds: Array.Empty<int>()));
    }

    [Fact]
    public async Task ReturnService_CreateReturnDeliveryAsync_ItemNotBelongingToPackage_ThrowsArgumentException()
    {
        var packageRepo = new Mock<IPackageRepository>();
        var service = new PackageReturnService(
            packageRepo.Object, Mock.Of<IBoxRepository>(), Mock.Of<IPackageOrderService>(), Mock.Of<INotificationService>());

        var package = new Package
        {
            Id = 1,
            RecipientId = 42,
            BoxId = 10,
            DestinationBoxId = 20
        };
        package.Items.Add(new PackageItem { Id = 1, PackageId = 1, Name = "Valid Item" });
        package.ConfirmDeposit();
        package.Payment = new Payment { PackageId = 1, PayerEmail = "test@ex.com", Amount = 10 };
        package.Payment.Complete();
        package.Accept();
        package.Prepare();
        package.Ship();
        package.Deliver();
        package.Collect(package.PickupCode!);

        packageRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateReturnDeliveryAsync(1, 42, new[] { 999 })); // 999 does not exist in package

        Assert.Equal("One or more selected items do not belong to the specified package.", ex.Message);
    }

    // SECTION 2: PackageFulfillmentService - Lifecycle Orchestration

    [Fact]
    public async Task FulfillmentService_AssignCourierAsync_HappyPath_UpdatesPackageAndNotifies()
    {
        var packageRepo = new Mock<IPackageRepository>();
        var notificationService = new Mock<INotificationService>();
        var peopleService = new Mock<IPeopleService>();

        var service = new PackageFulfillmentService(packageRepo.Object, notificationService.Object, peopleService.Object);

        var package = new Package { Id = 1 };
        package.ConfirmDeposit();
        package.Payment = new Payment { PackageId = 1, PayerEmail = "test@ex.com", Amount = 10 };
        package.Payment.Complete();
        package.Accept();

        var courier = new Courier { Id = 7, Name = "Jan", Surname = "Kowalski" };

        packageRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);
        peopleService.Setup(p => p.GetCourierAsync(7)).ReturnsAsync(courier);

        await service.AssignCourierAsync(1, 7);

        Assert.Equal(courier, package.Courier);
        packageRepo.Verify(r => r.UpdateAsync(package), Times.Once);
        notificationService.Verify(n => n.NotifyAsync("system",
            "Package 1 has been assigned to courier Jan."), Times.Once);
    }

    [Fact]
    public async Task FulfillmentService_AssignCourierAsync_CourierNotFound_ThrowsArgumentException()
    {
        var packageRepo = new Mock<IPackageRepository>();
        var peopleService = new Mock<IPeopleService>();
        var service = new PackageFulfillmentService(packageRepo.Object, Mock.Of<INotificationService>(), peopleService.Object);

        packageRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new Package { Id = 1 });
        peopleService.Setup(p => p.GetCourierAsync(99)).ReturnsAsync((Courier?)null);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.AssignCourierAsync(1, 99));
        Assert.Equal("Courier not found", ex.Message);
    }

    [Fact]
    public async Task FulfillmentService_ShipPackageAsync_HappyPath_TransitionsToShippedAndNotifies()
    {
        var packageRepo = new Mock<IPackageRepository>();
        var notificationService = new Mock<INotificationService>();
        var service = new PackageFulfillmentService(packageRepo.Object, notificationService.Object, Mock.Of<IPeopleService>());

        var package = new Package { Id = 1 };
        package.ConfirmDeposit();
        package.Payment = new Payment { PackageId = 1, PayerEmail = "test@ex.com", Amount = 10 };
        package.Payment.Complete();
        package.Accept();

        packageRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);

        await service.ShipPackageAsync(1);

        Assert.Equal(PackageStatus.Shipped, package.Status);
        Assert.NotNull(package.PreparedAt);
        Assert.NotNull(package.ShippedAt);
        packageRepo.Verify(r => r.UpdateAsync(package), Times.Once);
        notificationService.Verify(n => n.NotifyAsync("system", "Package 1 has been shipped."), Times.Once);
    }

    [Fact]
    public async Task FulfillmentService_DeliverPackageAsync_HappyPath_GeneratesPickupCodeAndNotifies()
    {
        var packageRepo = new Mock<IPackageRepository>();
        var notificationService = new Mock<INotificationService>();
        var service = new PackageFulfillmentService(packageRepo.Object, notificationService.Object, Mock.Of<IPeopleService>());

        var package = new Package { Id = 1 };
        package.ConfirmDeposit();
        package.Payment = new Payment { PackageId = 1, PayerEmail = "test@ex.com", Amount = 10 };
        package.Payment.Complete();
        package.Accept();
        package.Prepare();
        package.Ship();

        packageRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);

        await service.DeliverPackageAsync(1);

        Assert.Equal(PackageStatus.Delivered, package.Status);
        Assert.NotNull(package.DeliveredAt);
        Assert.NotNull(package.PickupCode);
        packageRepo.Verify(r => r.UpdateAsync(package), Times.Once);
        notificationService.Verify(n => n.NotifyAsync("system",
            $"Package 1 has been delivered. Pickup code: {package.PickupCode}"), Times.Once);
    }

    [Fact]
    public async Task FulfillmentService_CollectPackageAsync_HappyPath_CompletesCollectionAndNotifies()
    {
        var packageRepo = new Mock<IPackageRepository>();
        var notificationService = new Mock<INotificationService>();
        var service = new PackageFulfillmentService(packageRepo.Object, notificationService.Object, Mock.Of<IPeopleService>());

        var package = new Package { Id = 1, RecipientId = 42 };
        package.ConfirmDeposit();
        package.Payment = new Payment { PackageId = 1, PayerEmail = "test@ex.com", Amount = 10 };
        package.Payment.Complete();
        package.Accept();
        package.Prepare();
        package.Ship();
        package.Deliver();

        packageRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);

        await service.CollectPackageAsync(1, 42, package.PickupCode!);

        Assert.Equal(PackageStatus.Collected, package.Status);
        Assert.NotNull(package.CollectedAt);
        packageRepo.Verify(r => r.UpdateAsync(package), Times.Once);
        notificationService.Verify(n => n.NotifyAsync("system", "Package 1 has been collected."), Times.Once);
    }

    [Fact]
    public async Task FulfillmentService_CollectPackageAsync_InvalidPickupCode_ThrowsInvalidOperationException()
    {
        var packageRepo = new Mock<IPackageRepository>();
        var service = new PackageFulfillmentService(packageRepo.Object, Mock.Of<INotificationService>(), Mock.Of<IPeopleService>());

        var package = new Package { Id = 1, RecipientId = 42 };
        package.ConfirmDeposit();
        package.Payment = new Payment { PackageId = 1, PayerEmail = "test@ex.com", Amount = 10 };
        package.Payment.Complete();
        package.Accept();
        package.Prepare();
        package.Ship();
        package.Deliver();

        packageRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);

        var wrongCode = package.PickupCode == "111111" ? "222222" : "111111";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CollectPackageAsync(1, 42, wrongCode));
    }

    // SECTION 3: PackageIntakeService - Physical Deposit & Payment Guarding

    [Fact]
    public async Task IntakeService_ConfirmDepositAsync_HappyPath_SetsDepositConfirmedAndNotifies()
    {
        var packageRepo = new Mock<IPackageRepository>();
        var notificationService = new Mock<INotificationService>();
        var peopleService = new Mock<IPeopleService>();
        var service = new PackageIntakeService(
            packageRepo.Object, Mock.Of<IPaymentGateway>(), notificationService.Object, peopleService.Object);

        var package = new Package { Id = 10 };
        var courier = new Courier { Id = 5, Name = "Piotr" };

        packageRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(package);
        peopleService.Setup(p => p.GetCourierAsync(5)).ReturnsAsync(courier);

        await service.ConfirmDepositAsync(10, 5);

        Assert.NotNull(package.DepositConfirmedAt);
        packageRepo.Verify(r => r.UpdateAsync(package), Times.Once);
        notificationService.Verify(n => n.NotifyAsync("system",
            "Courier Piotr confirmed package 10 is in its box."), Times.Once);
    }

    [Fact]
    public async Task IntakeService_ConfirmDepositAsync_CourierNotFound_ThrowsArgumentException()
    {
        var packageRepo = new Mock<IPackageRepository>();
        var peopleService = new Mock<IPeopleService>();
        var service = new PackageIntakeService(
            packageRepo.Object, Mock.Of<IPaymentGateway>(), Mock.Of<INotificationService>(), peopleService.Object);

        packageRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(new Package { Id = 10 });
        peopleService.Setup(p => p.GetCourierAsync(99)).ReturnsAsync((Courier?)null);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.ConfirmDepositAsync(10, 99));
        Assert.Equal("Courier not found", ex.Message);
    }

    [Fact]
    public async Task IntakeService_RequestPaymentAsync_WhenAlreadyCompleted_ThrowsInvalidOperationException()
    {
        var packageRepo = new Mock<IPackageRepository>();
        var service = new PackageIntakeService(
            packageRepo.Object, Mock.Of<IPaymentGateway>(), Mock.Of<INotificationService>(), Mock.Of<IPeopleService>());

        var package = new Package { Id = 1 };
        package.Payment = new Payment { PackageId = 1, PayerEmail = "paid@ex.com", Amount = 15m };
        package.Payment.Complete();

        packageRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RequestPaymentAsync(1, "another@ex.com", 15m));

        Assert.Equal("This package has already been paid for.", ex.Message);
    }

    [Fact]
    public async Task IntakeService_ConfirmPaymentAsync_WhenNoPaymentRequested_ThrowsInvalidOperationException()
    {
        var packageRepo = new Mock<IPackageRepository>();
        var service = new PackageIntakeService(
            packageRepo.Object, Mock.Of<IPaymentGateway>(), Mock.Of<INotificationService>(), Mock.Of<IPeopleService>());

        var package = new Package { Id = 1, Payment = null };
        packageRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ConfirmPaymentAsync(1));

        Assert.Equal("No payment was requested for this package.", ex.Message);
    }

    // SECTION 4: PackageOrderService - Validation & Same-Locker Collision

    [Fact]
    public async Task OrderService_CreatePackageAsync_SenderNotFound_ThrowsArgumentException()
    {
        var peopleService = new Mock<IPeopleService>();
        peopleService.Setup(p => p.GetUserAsync(1)).ReturnsAsync((User?)null);

        var service = new PackageOrderService(
            Mock.Of<IPackageRepository>(), Mock.Of<IBoxRepository>(), peopleService.Object);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreatePackageAsync(1, 2, 10, 20, BoxSize.Small));

        Assert.Equal("Sender not found", ex.Message);
    }

    [Fact]
    public async Task OrderService_CreatePackageAsync_RecipientNotFound_ThrowsArgumentException()
    {
        var peopleService = new Mock<IPeopleService>();
        peopleService.Setup(p => p.GetUserAsync(1)).ReturnsAsync(new User { Id = 1 });
        peopleService.Setup(p => p.GetUserAsync(2)).ReturnsAsync((User?)null);

        var service = new PackageOrderService(
            Mock.Of<IPackageRepository>(), Mock.Of<IBoxRepository>(), peopleService.Object);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreatePackageAsync(1, 2, 10, 20, BoxSize.Small));

        Assert.Equal("Recipient not found", ex.Message);
    }

    [Fact]
    public async Task OrderService_CreatePackageAsync_NoFreeBoxAtOrigin_ThrowsInvalidOperationException()
    {
        var peopleService = new Mock<IPeopleService>();
        var boxRepo = new Mock<IBoxRepository>();

        peopleService.Setup(p => p.GetUserAsync(1)).ReturnsAsync(new User { Id = 1 });
        peopleService.Setup(p => p.GetUserAsync(2)).ReturnsAsync(new User { Id = 2 });
        boxRepo.Setup(b => b.FindFreeBoxAsync(10, BoxSize.Small, null)).ReturnsAsync((Box?)null);

        var service = new PackageOrderService(Mock.Of<IPackageRepository>(), boxRepo.Object, peopleService.Object);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreatePackageAsync(1, 2, 10, 20, BoxSize.Small));

        Assert.Contains("No free Small box at the chosen origin locker", ex.Message);
    }

    [Fact]
    public async Task OrderService_CreatePackageAsync_SameLocker_PassesOriginBoxIdToExcludeFromDestination()
    {
        var peopleService = new Mock<IPeopleService>();
        var boxRepo = new Mock<IBoxRepository>();
        var packageRepo = new Mock<IPackageRepository>();

        const int sameLockerId = 5;
        var originBox = new Box { Id = 101, ParcelLockerId = sameLockerId, Size = BoxSize.Medium };
        var destBox = new Box { Id = 102, ParcelLockerId = sameLockerId, Size = BoxSize.Medium };

        peopleService.Setup(p => p.GetUserAsync(1)).ReturnsAsync(new User { Id = 1 });
        peopleService.Setup(p => p.GetUserAsync(2)).ReturnsAsync(new User { Id = 2 });

        boxRepo.Setup(b => b.FindFreeBoxAsync(sameLockerId, BoxSize.Medium, null))
            .ReturnsAsync(originBox);
        boxRepo.Setup(b => b.FindFreeBoxAsync(sameLockerId, BoxSize.Medium, originBox.Id))
            .ReturnsAsync(destBox);

        var service = new PackageOrderService(packageRepo.Object, boxRepo.Object, peopleService.Object);

        var package = await service.CreatePackageAsync(1, 2, sameLockerId, sameLockerId, BoxSize.Medium);

        Assert.Equal(101, package.BoxId);
        Assert.Equal(102, package.DestinationBoxId);
        Assert.NotEqual(package.BoxId, package.DestinationBoxId);
        boxRepo.Verify(b => b.FindFreeBoxAsync(sameLockerId, BoxSize.Medium, originBox.Id), Times.Once);
    }

    // SECTION 5: RatingService - RateSenderAsync & Missing Role Guards

    [Fact]
    public async Task RatingService_RateSenderAsync_HappyPath_UpdatesSenderRank()
    {
        var packageRepo = new Mock<IPackageRepository>();
        var peopleService = new Mock<IPeopleService>();
        var service = new RatingService(packageRepo.Object, peopleService.Object);

        var package = new Package
        {
            Id = 1,
            SenderId = 15,
            RecipientId = 25
        };
        package.ConfirmDeposit();
        package.Payment = new Payment { PackageId = 1, PayerEmail = "test@ex.com", Amount = 10 };
        package.Payment.Complete();
        package.Accept();
        package.Prepare();
        package.Ship();
        package.Deliver();
        package.Collect(package.PickupCode!);

        packageRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);

        await service.RateSenderAsync(1, raterUserId: 25, score: 5);

        peopleService.Verify(p => p.UpdateRankAsync(15, 5), Times.Once);
    }

    [Fact]
    public async Task RatingService_RateCourierAsync_PackageHasNoCourier_ThrowsInvalidOperationException()
    {
        var packageRepo = new Mock<IPackageRepository>();
        var service = new RatingService(packageRepo.Object, Mock.Of<IPeopleService>());

        var package = new Package
        {
            Id = 1,
            RecipientId = 25,
            CourierId = null
        };
        package.ConfirmDeposit();
        package.Payment = new Payment { PackageId = 1, PayerEmail = "test@ex.com", Amount = 10 };
        package.Payment.Complete();
        package.Accept();
        package.Prepare();
        package.Ship();
        package.Deliver();
        package.Collect(package.PickupCode!);

        packageRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RateCourierAsync(1, raterUserId: 25, score: 4));

        Assert.Equal("This package has no courier to rate", ex.Message);
    }

    [Fact]
    public async Task RatingService_RatePersonAsync_PackageNotFound_ThrowsArgumentException()
    {
        var packageRepo = new Mock<IPackageRepository>();
        var service = new RatingService(packageRepo.Object, Mock.Of<IPeopleService>());

        packageRepo.Setup(r => r.GetByIdAsync(404)).ReturnsAsync((Package?)null);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.RateSenderAsync(404, raterUserId: 25, score: 5));

        Assert.Equal("Package not found", ex.Message);
    }

    // SECTION 6: SupportContactService - Direct Support Dispatch

    [Fact]
    public async Task SupportContactService_ContactSupportAsync_UserFound_DispatchesSupportNotification()
    {
        var notificationService = new Mock<INotificationService>();
        var user = new User
        {
            Id = 10,
            Name = "Marek",
            Surname = "Nowak",
            Email = "marek@example.com"
        };

        var mockDbSet = CreateMockDbSet(new List<AbstractPerson> { user });
        var dbContext = new DeliveryDbContext(new DbContextOptions<DeliveryDbContext>());
        dbContext.Users = mockDbSet.Object;

        var service = new SupportContactService(dbContext, notificationService.Object);

        await service.ContactSupportAsync(10, "Uszkodzona paczka", "Skrytka byla zalana.");

        notificationService.Verify(n => n.NotifyAsync(
            "support",
            "[Marek Nowak <marek@example.com>] Uszkodzona paczka: Skrytka byla zalana."),
            Times.Once);
    }

    [Fact]
    public async Task SupportContactService_ContactSupportAsync_PersonNotFound_ThrowsArgumentException()
    {
        var mockDbSet = CreateMockDbSet(new List<AbstractPerson>());
        var dbContext = new DeliveryDbContext(new DbContextOptions<DeliveryDbContext>());
        dbContext.Users = mockDbSet.Object;

        var service = new SupportContactService(dbContext, Mock.Of<INotificationService>());

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ContactSupportAsync(999, "Problem", "Opis"));

        Assert.Equal("Person not found", ex.Message);
    }

    // SECTION 7: Domain Invariants - Package, Payment & ParcelLocker

    [Fact]
    public void Package_ConfirmDeposit_PastDeadline_ThrowsInvalidOperationException()
    {
        var package = new Package
        {
            Id = 1,
            DepositDeadline = DateTime.UtcNow.AddMinutes(-5) // Expired deposit window
        };

        var ex = Assert.Throws<InvalidOperationException>(() => package.ConfirmDeposit());
        Assert.Equal("Deposit window has expired; the order must be re-created", ex.Message);
    }

    [Fact]
    public void Package_ConfirmDeposit_WhenNotInOrderedStatus_ThrowsInvalidOperationException()
    {
        var package = new Package { Id = 1 };
        package.ConfirmDeposit();
        package.Payment = new Payment { PackageId = 1, PayerEmail = "payer@ex.com", Amount = 10 };
        package.Payment.Complete();
        package.Accept();

        var ex = Assert.Throws<InvalidOperationException>(() => package.ConfirmDeposit());
        Assert.Equal("Cannot confirm deposit for package in status Accepted", ex.Message);
    }

    [Fact]
    public void Package_MarkAsReturned_FromCollected_TransitionsToReturnedToSender()
    {
        var package = new Package { Id = 1 };
        package.ConfirmDeposit();
        package.Payment = new Payment { PackageId = 1, PayerEmail = "payer@ex.com", Amount = 10 };
        package.Payment.Complete();
        package.Accept();
        package.Prepare();
        package.Ship();
        package.Deliver();
        package.Collect(package.PickupCode!);

        package.MarkAsReturned();

        Assert.Equal(PackageStatus.ReturnedToSender, package.Status);
    }

    [Theory]
    [InlineData(PackageStatus.Delivered)]
    [InlineData(PackageStatus.Cancelled)]
    [InlineData(PackageStatus.Rejected)]
    public void Package_MarkAsReturned_FromInvalidStates_ThrowsInvalidOperationException(PackageStatus status)
    {
        var package = new Package { Id = 1 };
        if (status == PackageStatus.Delivered)
        {
            package.ConfirmDeposit();
            package.Payment = new Payment { PackageId = 1, PayerEmail = "payer@ex.com", Amount = 10 };
            package.Payment.Complete();
            package.Accept();
            package.Prepare();
            package.Ship();
            package.Deliver();
        }
        else if (status == PackageStatus.Cancelled)
        {
            package.Cancel();
        }
        else if (status == PackageStatus.Rejected)
        {
            package.Reject();
        }

        var ex = Assert.Throws<InvalidOperationException>(() => package.MarkAsReturned());
        Assert.Equal($"Cannot return package in status {status}", ex.Message);
    }

    [Fact]
    public void Payment_CompleteAndFail_WhenNotPending_ThrowsInvalidOperationException()
    {
        var payment = new Payment { PackageId = 1, PayerEmail = "a@b.com", Amount = 20 };
        payment.Complete();

        var exComplete = Assert.Throws<InvalidOperationException>(() => payment.Complete());
        Assert.Equal("Cannot complete payment in status Completed", exComplete.Message);

        var exFail = Assert.Throws<InvalidOperationException>(() => payment.Fail());
        Assert.Equal("Cannot fail payment in status Completed", exFail.Message);
    }

    [Fact]
    public void ParcelLocker_CreateBoxAndRemoveBox_CorrectlyMaintainsBoxesList()
    {
        var locker = new ParcelLocker
        {
            City = "Warszawa",
            Street = "Zlota",
            Number = "44"
        };

        var box1 = locker.CreateBox(BoxSize.Small);
        var box2 = locker.CreateBox(BoxSize.Large);

        Assert.Equal(2, locker.Boxes.Count);
        Assert.Contains(box1, locker.Boxes);
        Assert.Contains(box2, locker.Boxes);
        Assert.Same(locker, box1.ParcelLocker);

        locker.RemoveBox(box1.Id);
        Assert.Single(locker.Boxes);
        Assert.DoesNotContain(box1, locker.Boxes);
        Assert.Contains(box2, locker.Boxes);
    }

    // EF Core Async Query Mocking Helpers (for in-memory DbSet support)

    private static Mock<DbSet<T>> CreateMockDbSet<T>(IEnumerable<T> data) where T : class
    {
        var queryable = new TestAsyncEnumerable<T>(data);
        var mockSet = new Mock<DbSet<T>>();

        mockSet.As<IAsyncEnumerable<T>>()
            .Setup(m => m.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns(new TestAsyncEnumerator<T>(data.GetEnumerator()));

        mockSet.As<IQueryable<T>>()
            .Setup(m => m.Provider)
            .Returns(new TestAsyncQueryProvider<T>(queryable));

        mockSet.As<IQueryable<T>>()
            .Setup(m => m.Expression)
            .Returns(((IQueryable)queryable).Expression);

        mockSet.As<IQueryable<T>>()
            .Setup(m => m.ElementType)
            .Returns(((IQueryable)queryable).ElementType);

        mockSet.As<IQueryable<T>>()
            .Setup(m => m.GetEnumerator())
            .Returns(data.GetEnumerator());

        return mockSet;
    }

    private class TestAsyncQueryProvider<TEntity> : IAsyncQueryProvider
    {
        private readonly IQueryProvider _inner;

        internal TestAsyncQueryProvider(IQueryProvider inner) => _inner = inner;

        public IQueryable CreateQuery(Expression expression) => new TestAsyncEnumerable<TEntity>(expression);

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) => new TestAsyncEnumerable<TElement>(expression);

        public object? Execute(Expression expression) => _inner.Execute(expression);

        public TResult Execute<TResult>(Expression expression) => _inner.Execute<TResult>(expression);

        public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
        {
            var resultType = typeof(TResult).GetGenericArguments()[0];
            var executionResult = typeof(IQueryProvider)
                .GetMethods()
                .First(m => m.Name == nameof(IQueryProvider.Execute) && m.IsGenericMethod)
                .MakeGenericMethod(resultType)
                .Invoke(_inner, new object[] { expression });

            return (TResult)typeof(Task).GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(resultType)
                .Invoke(null, new[] { executionResult })!;
        }
    }

    private class TestAsyncEnumerable<T> : EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryable<T>
    {
        public TestAsyncEnumerable(IEnumerable<T> enumerable) : base(enumerable) { }
        public TestAsyncEnumerable(Expression expression) : base(expression) { }

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            => new TestAsyncEnumerator<T>(this.AsEnumerable().GetEnumerator());

        IQueryProvider IQueryable.Provider => new TestAsyncQueryProvider<T>(this);
    }

    private class TestAsyncEnumerator<T> : IAsyncEnumerator<T>
    {
        private readonly IEnumerator<T> _inner;
        public TestAsyncEnumerator(IEnumerator<T> inner) => _inner = inner;
        public ValueTask DisposeAsync() { _inner.Dispose(); return ValueTask.CompletedTask; }
        public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(_inner.MoveNext());
        public T Current => _inner.Current;
    }
}
