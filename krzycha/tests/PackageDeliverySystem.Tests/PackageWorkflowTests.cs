using Moq;
using PackageDeliverySystem.Core;
using PackageDeliverySystem.Infrastructure;
using Xunit;

namespace PackageDeliverySystem.Tests;

/// <summary>
/// Kompletny zestaw testów procesowych i reguł biznesowych systemu przesyłek kurierskich,
/// opracowany zgodnie z wymaganiami projektowymi (weryfikacja procesu nadania/dostarczenia,
/// sytuacje awaryjne oraz moduł zwrotów częściowych) i diagramami sekwencji:
/// - ETAP 0: Scenariusz przekrojowy E2E (Diagramy 01–05)
/// - ETAP 1: Zamówienie i płatność (Diagram 01: 01-order-and-payment)
/// - ETAP 2: Potwierdzenie depozytu w paczkomacie (Diagram 02: 02-deposit-confirmation)
/// - ETAP 3: Weryfikacja i decyzja operatora (Diagram 03: 03-operator-accept-reject)
/// - ETAP 4: Transport i doręczenie kurierskie (Diagram 04: 04-courier-pickup-delivery)
/// - ETAP 5: Odbiór przesyłki i ocena (Diagram 05: 05-customer-collect-and-rate)
/// - ETAP 6: Proces zwrotu produktów (Diagram 06: 06-return-process)
///
/// Każdy test zorganizowano w czytelnym podziale na trzy sekcje:
/// 1) Przygotowanie (Arrange) - ustawienie systemu w odpowiednim stanie
/// 2) Właściwy test (Act) - wołanie odpowiednich metod w ramach scenariusza testowego
/// 3) Weryfikacja rezultatu (Assert) - sprawdzenie stanu i rezultatów
/// </summary>
public class PackageWorkflowTests
{
    #region Pomocnicze metody fabrykujące dla testów

    private static void MakeReadyForAcceptance(Package package)
    {
        package.ConfirmDeposit();
        package.Payment = new Payment
        {
            PackageId = package.Id,
            PayerEmail = "payer@example.com",
            Amount = 14.99m
        };
        package.Payment.Complete();
    }

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

        package.Items.Add(new PackageItem { Id = 1, PackageId = packageId, Name = "Buty sportowe" });
        package.Items.Add(new PackageItem { Id = 2, PackageId = packageId, Name = "Spodnie jeansowe" });
        package.Items.Add(new PackageItem { Id = 3, PackageId = packageId, Name = "Czapka zimowa" });

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

    #endregion

    #region ETAP 0: Scenariusz przekrojowy E2E (Diagramy 01–05)

    [Fact]
    public async Task WF01_FullLifecycle_FromOrderToCollectionAndRating_OrchestratesAcrossAllServicesSuccessfully()
    {
        // 1. Przygotowanie (Arrange)
        var packageRepoMock = new Mock<IPackageRepository>();
        var boxRepoMock = new Mock<IBoxRepository>();
        var peopleServiceMock = new Mock<IPeopleService>();
        var paymentGatewayMock = new Mock<IPaymentGateway>();
        var notificationServiceMock = new Mock<INotificationService>();

        const int senderId = 10;
        const int recipientId = 20;
        const int originLockerId = 1;
        const int destinationLockerId = 2;
        const int originBoxId = 101;
        const int destinationBoxId = 202;
        const int courierId = 7;
        const int operatorId = 5;

        var sender = new User { Id = senderId, Name = "Sklep Elektronika", Email = "sklep@example.com" };
        var recipient = new User { Id = recipientId, Name = "Jan Kowalski", Email = "jan@example.com" };
        var courier = new Courier { Id = courierId, Name = "Dave", Surname = "Kurier" };
        var operatorUser = new Operator { Id = operatorId, Name = "Charlie", Surname = "Operator" };

        var originBox = new Box { Id = originBoxId, ParcelLockerId = originLockerId, Size = BoxSize.Medium };
        var destinationBox = new Box { Id = destinationBoxId, ParcelLockerId = destinationLockerId, Size = BoxSize.Medium };

        peopleServiceMock.Setup(p => p.GetUserAsync(senderId)).ReturnsAsync(sender);
        peopleServiceMock.Setup(p => p.GetUserAsync(recipientId)).ReturnsAsync(recipient);
        peopleServiceMock.Setup(p => p.GetCourierAsync(courierId)).ReturnsAsync(courier);
        peopleServiceMock.Setup(p => p.GetOperatorAsync(operatorId)).ReturnsAsync(operatorUser);

        boxRepoMock.Setup(b => b.FindFreeBoxAsync(originLockerId, BoxSize.Medium, null)).ReturnsAsync(originBox);
        boxRepoMock.Setup(b => b.FindFreeBoxAsync(destinationLockerId, BoxSize.Medium, originBoxId)).ReturnsAsync(destinationBox);
        boxRepoMock.Setup(b => b.GetByIdAsync(originBoxId)).ReturnsAsync(originBox);
        boxRepoMock.Setup(b => b.GetByIdAsync(destinationBoxId)).ReturnsAsync(destinationBox);

        paymentGatewayMock.Setup(g => g.InitiatePaymentAsync("sklep@example.com", 14.99m)).ReturnsAsync("PAY-EXT-999");
        paymentGatewayMock.Setup(g => g.GetPaymentStatusAsync("PAY-EXT-999")).ReturnsAsync(PaymentStatus.Completed);

        Package? trackedPackage = null;
        packageRepoMock.Setup(r => r.AddAsync(It.IsAny<Package>()))
            .Callback<Package>(p =>
            {
                p.Id = 1;
                p.SenderId = p.Sender.Id;
                p.RecipientId = p.Recipient.Id;
                trackedPackage = p;
            })
            .Returns(Task.CompletedTask);

        packageRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(() => trackedPackage);
        packageRepoMock.Setup(r => r.UpdateAsync(It.IsAny<Package>()))
            .Callback<Package>(p =>
            {
                if (p.Courier != null) p.CourierId = p.Courier.Id;
                if (p.Operator != null) p.OperatorId = p.Operator.Id;
            })
            .Returns(Task.CompletedTask);

        var orderService = new PackageOrderService(packageRepoMock.Object, boxRepoMock.Object, peopleServiceMock.Object);
        var intakeService = new PackageIntakeService(packageRepoMock.Object, paymentGatewayMock.Object, notificationServiceMock.Object, peopleServiceMock.Object);
        var acceptanceService = new PackageAcceptanceService(packageRepoMock.Object, boxRepoMock.Object, notificationServiceMock.Object, peopleServiceMock.Object);
        var fulfillmentService = new PackageFulfillmentService(packageRepoMock.Object, notificationServiceMock.Object, peopleServiceMock.Object);
        var ratingService = new RatingService(packageRepoMock.Object, peopleServiceMock.Object);

        // 2. Właściwy test (Act)
        // Krok 1: Złożenie zamówienia z produktami
        var items = new[] { new PackageItem { Name = "Słuchawki bezprzewodowe" } };
        var createdPackage = await orderService.CreatePackageAsync(senderId, recipientId, originLockerId, destinationLockerId, BoxSize.Medium, items);

        // Krok 2: Płatność
        var paymentRef = await intakeService.RequestPaymentAsync(createdPackage.Id, "sklep@example.com", 14.99m);
        await intakeService.ConfirmPaymentAsync(createdPackage.Id);

        // Krok 3: Potwierdzenie fizycznego włożenia do skrytki przez kuriera
        await intakeService.ConfirmDepositAsync(createdPackage.Id, courierId);

        // Krok 4: Akceptacja przesyłki przez operatora
        await acceptanceService.AcceptPackageAsync(createdPackage.Id, operatorId, originBoxId);

        // Krok 5: Przypisanie kuriera i rozpoczęcie transportu
        await fulfillmentService.AssignCourierAsync(createdPackage.Id, courierId);
        await fulfillmentService.ShipPackageAsync(createdPackage.Id);

        // Krok 6: Doręczenie do paczkomatu docelowego i wygenerowanie kodu odbioru
        await fulfillmentService.DeliverPackageAsync(createdPackage.Id);

        // Krok 7: Odbiór paczki przez klienta
        await fulfillmentService.CollectPackageAsync(createdPackage.Id, recipientId, createdPackage.PickupCode!);

        // Krok 8: Ocena kuriera i nadawcy
        await ratingService.RateCourierAsync(createdPackage.Id, recipientId, 5);
        await ratingService.RateSenderAsync(createdPackage.Id, recipientId, 5);

        // 3. Weryfikacja rezultatu (Assert)
        Assert.NotNull(createdPackage);
        Assert.Equal(PackageStatus.Collected, createdPackage.Status);
        Assert.Equal(originBoxId, createdPackage.BoxId);
        Assert.Equal(destinationBoxId, createdPackage.DestinationBoxId);
        Assert.Single(createdPackage.Items);
        Assert.Equal("PAY-EXT-999", paymentRef);
        Assert.NotNull(createdPackage.Payment);
        Assert.Equal(PaymentStatus.Completed, createdPackage.Payment.Status);
        Assert.NotNull(createdPackage.DepositConfirmedAt);
        Assert.Equal(operatorUser, createdPackage.Operator);
        Assert.NotNull(createdPackage.AcceptedAt);
        Assert.Equal(courier, createdPackage.Courier);
        Assert.NotNull(createdPackage.PreparedAt);
        Assert.NotNull(createdPackage.ShippedAt);
        Assert.NotNull(createdPackage.DeliveredAt);
        Assert.NotNull(createdPackage.PickupCode);
        Assert.Matches(@"^[0-9]{6}$", createdPackage.PickupCode);
        Assert.NotNull(createdPackage.CollectedAt);

        peopleServiceMock.Verify(p => p.UpdateRankAsync(courierId, 5), Times.Once);
        peopleServiceMock.Verify(p => p.UpdateRankAsync(senderId, 5), Times.Once);

        Assert.True(createdPackage.DepositConfirmedAt <= createdPackage.AcceptedAt);
        Assert.True(createdPackage.AcceptedAt <= createdPackage.PreparedAt);
        Assert.True(createdPackage.PreparedAt <= createdPackage.ShippedAt);
        Assert.True(createdPackage.ShippedAt <= createdPackage.DeliveredAt);
        Assert.True(createdPackage.DeliveredAt <= createdPackage.CollectedAt);

        packageRepoMock.Verify(r => r.AddAsync(createdPackage), Times.Once);
        notificationServiceMock.Verify(n => n.NotifyAsync("system", It.IsAny<string>()), Times.AtLeast(6));
    }

    #endregion

    #region ETAP 1: Zamówienie i płatność (Diagram 01: 01-order-and-payment)

    [Fact]
    public async Task WF02_OrderPhase_WhenNoFreeBoxAtOriginOrDestination_FailsAtomicallyWithoutCreatingPackage()
    {
        // 1. Przygotowanie (Arrange)
        var packageRepoMock = new Mock<IPackageRepository>();
        var boxRepoMock = new Mock<IBoxRepository>();
        var peopleServiceMock = new Mock<IPeopleService>();

        peopleServiceMock.Setup(p => p.GetUserAsync(10)).ReturnsAsync(new User { Id = 10 });
        peopleServiceMock.Setup(p => p.GetUserAsync(20)).ReturnsAsync(new User { Id = 20 });

        boxRepoMock.Setup(b => b.FindFreeBoxAsync(1, BoxSize.Large, null)).ReturnsAsync((Box?)null);

        var orderService = new PackageOrderService(packageRepoMock.Object, boxRepoMock.Object, peopleServiceMock.Object);

        // 2. Właściwy test (Act)
        // Brak skrytki w paczkomacie nadawczym
        var exOrigin = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orderService.CreatePackageAsync(10, 20, 1, 2, BoxSize.Large));

        boxRepoMock.Setup(b => b.FindFreeBoxAsync(1, BoxSize.Medium, null))
            .ReturnsAsync(new Box { Id = 101, ParcelLockerId = 1 });
        boxRepoMock.Setup(b => b.FindFreeBoxAsync(2, BoxSize.Medium, 101))
            .ReturnsAsync((Box?)null);

        // Brak skrytki w paczkomacie docelowym
        var exDestination = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orderService.CreatePackageAsync(10, 20, 1, 2, BoxSize.Medium));

        // 3. Weryfikacja rezultatu (Assert)
        Assert.Contains("No free Large box at the chosen origin locker", exOrigin.Message);
        Assert.Contains("No free Medium box at the chosen destination locker", exDestination.Message);
        packageRepoMock.Verify(r => r.AddAsync(It.IsAny<Package>()), Times.Never);
    }

    [Fact]
    public async Task WF10_RequestPaymentAndConfirm_FailureAndRetryFlow_CompletesSuccessfully()
    {
        // 1. Przygotowanie (Arrange)
        var packageRepoMock = new Mock<IPackageRepository>();
        var paymentGatewayMock = new Mock<IPaymentGateway>();
        var notificationMock = new Mock<INotificationService>();
        var peopleMock = new Mock<IPeopleService>();

        var intakeService = new PackageIntakeService(
            packageRepoMock.Object,
            paymentGatewayMock.Object,
            notificationMock.Object,
            peopleMock.Object);

        var package = new Package { Id = 1 };
        packageRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);

        paymentGatewayMock.SetupSequence(g => g.InitiatePaymentAsync("user@example.com", 25.00m))
            .ReturnsAsync("REF-FAIL-1")
            .ReturnsAsync("REF-SUCCESS-2");
        paymentGatewayMock.Setup(g => g.GetPaymentStatusAsync("REF-FAIL-1")).ReturnsAsync(PaymentStatus.Failed);
        paymentGatewayMock.Setup(g => g.GetPaymentStatusAsync("REF-SUCCESS-2")).ReturnsAsync(PaymentStatus.Completed);

        // 2. Właściwy test (Act)
        var ref1 = await intakeService.RequestPaymentAsync(1, "user@example.com", 25.00m);
        await intakeService.ConfirmPaymentAsync(1);
        var statusAfterFail = package.Payment?.Status;

        var ref2 = await intakeService.RequestPaymentAsync(1, "user@example.com", 25.00m);
        await intakeService.ConfirmPaymentAsync(1);

        // 3. Weryfikacja rezultatu (Assert)
        Assert.Equal("REF-FAIL-1", ref1);
        Assert.Equal(PaymentStatus.Failed, statusAfterFail);
        Assert.Equal("REF-SUCCESS-2", ref2);
        Assert.NotNull(package.Payment);
        Assert.Equal(PaymentStatus.Completed, package.Payment.Status);
        Assert.Equal("REF-SUCCESS-2", package.Payment.ExternalReference);
        Assert.NotNull(package.Payment.CompletedAt);

        notificationMock.Verify(n => n.NotifyAsync("system",
            It.Is<string>(s => s.Contains("Payment for package 1 has been confirmed."))), Times.Once);
    }

    [Fact]
    public async Task WF14_ConfirmPaymentAsync_WhenAlreadyCompleted_IsIdempotent()
    {
        // 1. Przygotowanie (Arrange)
        var package = new Package { Id = 1 };
        var payment = new Payment
        {
            PackageId = 1,
            PayerEmail = "payer@example.com",
            Amount = 25.00m,
            ExternalReference = "PAY-REF-COMPLETED"
        };
        payment.Complete();
        package.Payment = payment;

        var packageRepoMock = new Mock<IPackageRepository>();
        var paymentGatewayMock = new Mock<IPaymentGateway>();
        var notificationMock = new Mock<INotificationService>();
        var peopleMock = new Mock<IPeopleService>();

        packageRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);

        var intakeService = new PackageIntakeService(
            packageRepoMock.Object,
            paymentGatewayMock.Object,
            notificationMock.Object,
            peopleMock.Object);

        // 2. Właściwy test (Act)
        await intakeService.ConfirmPaymentAsync(1);

        // 3. Weryfikacja rezultatu (Assert)
        Assert.Equal(PaymentStatus.Completed, package.Payment.Status);
        paymentGatewayMock.Verify(g => g.GetPaymentStatusAsync(It.IsAny<string>()), Times.Never);
        packageRepoMock.Verify(r => r.UpdateAsync(It.IsAny<Package>()), Times.Never);
        notificationMock.Verify(n => n.NotifyAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task WF22_OrderPhase_SameOriginAndDestinationLocker_ExcludesOriginBoxFromDestinationSelection()
    {
        // 1. Przygotowanie (Arrange)
        var peopleServiceMock = new Mock<IPeopleService>();
        var boxRepoMock = new Mock<IBoxRepository>();
        var packageRepoMock = new Mock<IPackageRepository>();

        const int sameLockerId = 5;
        var originBox = new Box { Id = 101, ParcelLockerId = sameLockerId, Size = BoxSize.Medium };
        var destBox = new Box { Id = 102, ParcelLockerId = sameLockerId, Size = BoxSize.Medium };

        peopleServiceMock.Setup(p => p.GetUserAsync(1)).ReturnsAsync(new User { Id = 1 });
        peopleServiceMock.Setup(p => p.GetUserAsync(2)).ReturnsAsync(new User { Id = 2 });

        boxRepoMock.Setup(b => b.FindFreeBoxAsync(sameLockerId, BoxSize.Medium, null))
            .ReturnsAsync(originBox);
        boxRepoMock.Setup(b => b.FindFreeBoxAsync(sameLockerId, BoxSize.Medium, originBox.Id))
            .ReturnsAsync(destBox);

        var service = new PackageOrderService(packageRepoMock.Object, boxRepoMock.Object, peopleServiceMock.Object);

        // 2. Właściwy test (Act)
        var package = await service.CreatePackageAsync(1, 2, sameLockerId, sameLockerId, BoxSize.Medium);

        // 3. Weryfikacja rezultatu (Assert)
        Assert.Equal(101, package.BoxId);
        Assert.Equal(102, package.DestinationBoxId);
        Assert.NotEqual(package.BoxId, package.DestinationBoxId);
        boxRepoMock.Verify(b => b.FindFreeBoxAsync(sameLockerId, BoxSize.Medium, originBox.Id), Times.Once);
    }

    [Fact]
    public async Task WF23_PaymentPhase_RequestPayment_WhenAlreadyCompleted_ThrowsInvalidOperationException()
    {
        // 1. Przygotowanie (Arrange)
        var packageRepo = new Mock<IPackageRepository>();
        var service = new PackageIntakeService(
            packageRepo.Object, Mock.Of<IPaymentGateway>(), Mock.Of<INotificationService>(), Mock.Of<IPeopleService>());

        var package = new Package { Id = 1 };
        package.Payment = new Payment { PackageId = 1, PayerEmail = "paid@ex.com", Amount = 15m };
        package.Payment.Complete();

        packageRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);

        // 2. Właściwy test (Act) & 3. Weryfikacja (Assert)
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RequestPaymentAsync(1, "another@ex.com", 15m));

        Assert.Equal("This package has already been paid for.", ex.Message);
    }

    [Fact]
    public async Task WF29_OrderPhase_Cancel_FromOrderedStage_TransitionsToCancelled()
    {
        // 1. Przygotowanie (Arrange)
        var packageRepoMock = new Mock<IPackageRepository>();
        var boxRepoMock = new Mock<IBoxRepository>();
        var peopleServiceMock = new Mock<IPeopleService>();
        var notificationMock = new Mock<INotificationService>();

        const int packageId = 1;
        const int senderId = 10;
        var sender = new User { Id = senderId, Email = "sender@example.com" };
        var package = new Package
        {
            Id = packageId,
            SenderId = senderId,
            Sender = sender
        };
        Assert.Equal(PackageStatus.Ordered, package.Status);

        packageRepoMock.Setup(r => r.GetByIdAsync(packageId)).ReturnsAsync(package);

        var orderService = new PackageOrderService(
            packageRepoMock.Object,
            boxRepoMock.Object,
            peopleServiceMock.Object,
            notificationMock.Object);

        // 2. Właściwy test (Act)
        await orderService.CancelPackageAsync(packageId, senderId);

        // 3. Weryfikacja rezultatu (Assert)
        Assert.Equal(PackageStatus.Cancelled, package.Status);
        packageRepoMock.Verify(r => r.UpdateAsync(package), Times.Once);
        notificationMock.Verify(n => n.NotifyAsync("system", It.Is<string>(msg => msg.Contains("cancelled"))), Times.Once);
        notificationMock.Verify(n => n.NotifyAsync(sender.Email, It.Is<string>(msg => msg.Contains("cancelled"))), Times.Once);
    }

    #endregion

    #region ETAP 2: Potwierdzenie depozytu (Diagram 02: 02-deposit-confirmation)

    [Fact]
    public async Task WF03_DepositPhase_WhenCourierFindsBoxEmpty_CancelsPackageAndNotifiesSender()
    {
        // 1. Przygotowanie (Arrange)
        var packageRepoMock = new Mock<IPackageRepository>();
        var peopleServiceMock = new Mock<IPeopleService>();
        var notificationServiceMock = new Mock<INotificationService>();

        var package = new Package
        {
            Id = 15,
            SenderId = 10,
            RecipientId = 20,
            BoxId = 101,
            DestinationBoxId = 202
        };

        var courier = new Courier { Id = 7, Name = "Piotr" };

        packageRepoMock.Setup(r => r.GetByIdAsync(15)).ReturnsAsync(package);
        peopleServiceMock.Setup(p => p.GetCourierAsync(7)).ReturnsAsync(courier);

        var intakeService = new PackageIntakeService(
            packageRepoMock.Object,
            Mock.Of<IPaymentGateway>(),
            notificationServiceMock.Object,
            peopleServiceMock.Object);

        // 2. Właściwy test (Act)
        await intakeService.ReportMissingDepositAsync(packageId: 15, courierId: 7);

        // 3. Weryfikacja rezultatu (Assert)
        Assert.Equal(PackageStatus.Cancelled, package.Status);
        packageRepoMock.Verify(r => r.UpdateAsync(package), Times.Once);
        notificationServiceMock.Verify(n => n.NotifyAsync("system",
            It.Is<string>(msg => msg.Contains("cancelled") && msg.Contains("found the box empty"))),
            Times.Once);
    }

    [Fact]
    public async Task WF04_DepositPhase_WhenSenderMisses48hDeadline_BackgroundJobCancelsPackage()
    {
        // 1. Przygotowanie (Arrange)
        var packageRepoMock = new Mock<IPackageRepository>();
        var notificationServiceMock = new Mock<INotificationService>();

        var expiredPackage = new Package
        {
            Id = 22,
            DepositDeadline = DateTime.UtcNow.AddMinutes(-1)
        };

        packageRepoMock.Setup(r => r.GetPackagesWithMissedDepositAsync())
            .ReturnsAsync(new List<Package> { expiredPackage });

        var intakeService = new PackageIntakeService(
            packageRepoMock.Object,
            Mock.Of<IPaymentGateway>(),
            notificationServiceMock.Object,
            Mock.Of<IPeopleService>());

        // 2. Właściwy test (Act)
        await intakeService.ProcessMissedDepositsAsync();

        // 3. Weryfikacja rezultatu (Assert)
        Assert.Equal(PackageStatus.Cancelled, expiredPackage.Status);
        packageRepoMock.Verify(r => r.UpdateAsync(expiredPackage), Times.Once);
        notificationServiceMock.Verify(n => n.NotifyAsync("system",
            It.Is<string>(msg => msg.Contains("cancelled") && msg.Contains("nothing was deposited"))),
            Times.Once);
    }

    [Fact]
    public void WF24_DepositPhase_ConfirmDeposit_PastDeadline_ThrowsInvalidOperationException()
    {
        // 1. Przygotowanie (Arrange)
        var package = new Package
        {
            Id = 1,
            DepositDeadline = DateTime.UtcNow.AddMinutes(-5)
        };

        // 2. Właściwy test (Act) & 3. Weryfikacja (Assert)
        var ex = Assert.Throws<InvalidOperationException>(() => package.ConfirmDeposit());
        Assert.Equal("Deposit window has expired; the order must be re-created", ex.Message);
    }

    #endregion

    #region ETAP 3: Decyzja operatora (Diagram 03: 03-operator-accept-reject)

    [Fact]
    public async Task WF05_AcceptancePhase_OperatorRejectsPackage_TransitionsToRejectedAndBlocksDownstreamShipping()
    {
        // 1. Przygotowanie (Arrange)
        var packageRepoMock = new Mock<IPackageRepository>();
        var notificationServiceMock = new Mock<INotificationService>();
        var peopleServiceMock = new Mock<IPeopleService>();

        var package = new Package
        {
            Id = 30,
            SenderId = 10,
            RecipientId = 20,
            BoxId = 101,
            DestinationBoxId = 202
        };
        package.ConfirmDeposit();
        package.Payment = new Payment { PackageId = 30, PayerEmail = "payer@ex.com", Amount = 15m };
        package.Payment.Complete();

        var operatorUser = new Operator { Id = 5, Name = "Ewa", Surname = "Kontroler" };

        packageRepoMock.Setup(r => r.GetByIdAsync(30)).ReturnsAsync(package);
        peopleServiceMock.Setup(p => p.GetOperatorAsync(5)).ReturnsAsync(operatorUser);

        var acceptanceService = new PackageAcceptanceService(
            packageRepoMock.Object,
            Mock.Of<IBoxRepository>(),
            notificationServiceMock.Object,
            peopleServiceMock.Object);

        var fulfillmentService = new PackageFulfillmentService(
            packageRepoMock.Object,
            notificationServiceMock.Object,
            peopleServiceMock.Object);

        // 2. Właściwy test (Act)
        await acceptanceService.RejectPackageAsync(packageId: 30, operatorId: 5);
        var downstreamAction = () => fulfillmentService.ShipPackageAsync(30);

        // 3. Weryfikacja rezultatu (Assert)
        Assert.Equal(PackageStatus.Rejected, package.Status);
        packageRepoMock.Verify(r => r.UpdateAsync(package), Times.Once);
        notificationServiceMock.Verify(n => n.NotifyAsync("system",
            It.Is<string>(msg => msg.Contains("rejected by operator Ewa"))), Times.Once);
        await Assert.ThrowsAsync<InvalidOperationException>(downstreamAction);
    }

    [Theory]
    [InlineData(true, false, "Cannot accept a package that was never deposited into its box")]
    [InlineData(false, true, "Cannot accept a package that has not been paid for")]
    public void WF13_Accept_Preconditions_ThrowsWhenDepositOrPaymentMissing(bool paid, bool deposited, string expectedMessage)
    {
        // 1. Przygotowanie (Arrange)
        var package = new Package { Id = 1 };
        if (deposited) package.ConfirmDeposit();
        if (paid)
        {
            package.Payment = new Payment { PackageId = 1, PayerEmail = "p@ex.com", Amount = 10m };
            package.Payment.Complete();
        }

        // 2. Właściwy test (Act) & 3. Weryfikacja (Assert)
        var ex = Assert.Throws<InvalidOperationException>(() => package.Accept());

        Assert.Equal(expectedMessage, ex.Message);
        Assert.Equal(PackageStatus.Ordered, package.Status);
        Assert.Null(package.AcceptedAt);
    }

    [Fact]
    public void WF16_AssignCourierAndOperator_BeforeAcceptedStage_ThrowsInvalidOperationException()
    {
        // 1. Przygotowanie (Arrange)
        var package = new Package();
        var courier = new Courier { Id = 10, Name = "Jan", Surname = "Kowalski" };
        var op = new Operator { Id = 20, Name = "Anna", Surname = "Nowak" };

        // 2. Właściwy test (Act) & 3. Weryfikacja (Assert)
        var exCourier = Assert.Throws<InvalidOperationException>(() => package.AssignCourier(courier));
        var exOp = Assert.Throws<InvalidOperationException>(() => package.AssignOperator(op));

        Assert.Equal("Cannot assign courier to package in status Ordered", exCourier.Message);
        Assert.Equal("Cannot assign operator to package in status Ordered", exOp.Message);
        Assert.Null(package.Courier);
        Assert.Null(package.Operator);
        Assert.Equal(PackageStatus.Ordered, package.Status);
    }

    [Fact]
    public async Task WF25_AcceptancePhase_BoxMismatchOrSizeTooSmall_ThrowsException()
    {
        // 1. Przygotowanie (Arrange)
        var packageRepoMock = new Mock<IPackageRepository>();
        var boxRepoMock = new Mock<IBoxRepository>();
        var peopleMock = new Mock<IPeopleService>();

        var acceptanceService = new PackageAcceptanceService(
            packageRepoMock.Object,
            boxRepoMock.Object,
            Mock.Of<INotificationService>(),
            peopleMock.Object);

        var package = new Package { Id = 1, BoxId = 10, Size = BoxSize.Large };
        MakeReadyForAcceptance(package);
        var op = new Operator { Id = 5, Name = "Jan" };

        packageRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);
        peopleMock.Setup(p => p.GetOperatorAsync(5)).ReturnsAsync(op);
        boxRepoMock.Setup(b => b.GetByIdAsync(10)).ReturnsAsync(new Box { Id = 10, Size = BoxSize.Small });

        // 2. Właściwy test (Act) & 3. Weryfikacja (Assert)
        // Niezgodność ID skrytki
        await Assert.ThrowsAsync<ArgumentException>(() =>
            acceptanceService.AcceptPackageAsync(1, 5, 99));

        // Niezgodność rozmiaru: paczka Large w skrytce Small
        var exSize = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            acceptanceService.AcceptPackageAsync(1, 5, 10));

        Assert.Contains("cannot be accepted into a Small box", exSize.Message);
    }

    [Fact]
    public async Task WF30_AcceptancePhase_Cancel_FromAcceptedStage_TransitionsToCancelled()
    {
        // 1. Przygotowanie (Arrange)
        var packageRepoMock = new Mock<IPackageRepository>();
        var boxRepoMock = new Mock<IBoxRepository>();
        var peopleServiceMock = new Mock<IPeopleService>();
        var notificationMock = new Mock<INotificationService>();

        const int packageId = 1;
        const int senderId = 10;
        var sender = new User { Id = senderId, Email = "sender@example.com" };
        var package = new Package
        {
            Id = packageId,
            SenderId = senderId,
            Sender = sender
        };
        MakeReadyForAcceptance(package);
        package.Accept();
        Assert.Equal(PackageStatus.Accepted, package.Status);

        packageRepoMock.Setup(r => r.GetByIdAsync(packageId)).ReturnsAsync(package);

        var orderService = new PackageOrderService(
            packageRepoMock.Object,
            boxRepoMock.Object,
            peopleServiceMock.Object,
            notificationMock.Object);

        // 2. Właściwy test (Act)
        await orderService.CancelPackageAsync(packageId, senderId);

        // 3. Weryfikacja rezultatu (Assert)
        Assert.Equal(PackageStatus.Cancelled, package.Status);
        packageRepoMock.Verify(r => r.UpdateAsync(package), Times.Once);
        notificationMock.Verify(n => n.NotifyAsync("system", It.Is<string>(msg => msg.Contains("cancelled"))), Times.Once);
    }

    #endregion

    #region ETAP 4: Transport i doręczenie kurierskie (Diagram 04: 04-courier-pickup-delivery)

    [Fact]
    public async Task WF06_ShippingPhase_PackageLostInTransitExceedingSLA_MarkedAsExpiredAndAlertsSystem()
    {
        // 1. Przygotowanie (Arrange)
        var packageRepoMock = new Mock<IPackageRepository>();
        var notificationServiceMock = new Mock<INotificationService>();

        var lostInTransitPackage = new Package
        {
            Id = 44,
            SenderId = 10,
            RecipientId = 20,
            BoxId = 101,
            DestinationBoxId = 202
        };
        lostInTransitPackage.ConfirmDeposit();
        lostInTransitPackage.Payment = new Payment { PackageId = 44, PayerEmail = "p@ex.com", Amount = 12m };
        lostInTransitPackage.Payment.Complete();
        lostInTransitPackage.Accept();
        lostInTransitPackage.Prepare();
        lostInTransitPackage.Ship();

        typeof(Package).GetProperty(nameof(Package.ExpiredAt))?
            .SetValue(lostInTransitPackage, DateTime.UtcNow.AddHours(-1));

        packageRepoMock.Setup(r => r.GetPackagesRequiringExpirationAsync())
            .ReturnsAsync(new List<Package> { lostInTransitPackage });

        var acceptanceService = new PackageAcceptanceService(
            packageRepoMock.Object,
            Mock.Of<IBoxRepository>(),
            notificationServiceMock.Object,
            Mock.Of<IPeopleService>());

        // 2. Właściwy test (Act)
        await acceptanceService.ProcessExpiredPackagesAsync();

        // 3. Weryfikacja rezultatu (Assert)
        Assert.Equal(PackageStatus.Expired, lostInTransitPackage.Status);
        packageRepoMock.Verify(r => r.UpdateAsync(lostInTransitPackage), Times.Once);
        notificationServiceMock.Verify(n => n.NotifyAsync("system",
            It.Is<string>(msg => msg.Contains("EXPIRED due to inactivity"))), Times.Once);
    }

    [Fact]
    public async Task WF09_Cancel_AfterShipped_ThrowsInvalidOperationException()
    {
        // 1. Przygotowanie (Arrange)
        var packageRepoMock = new Mock<IPackageRepository>();
        var boxRepoMock = new Mock<IBoxRepository>();
        var peopleServiceMock = new Mock<IPeopleService>();
        var notificationMock = new Mock<INotificationService>();

        const int packageId = 1;
        const int senderId = 10;
        var package = new Package
        {
            Id = packageId,
            SenderId = senderId
        };
        MakeReadyForAcceptance(package);
        package.Accept();
        package.Prepare();
        package.Ship();

        packageRepoMock.Setup(r => r.GetByIdAsync(packageId)).ReturnsAsync(package);

        var orderService = new PackageOrderService(
            packageRepoMock.Object,
            boxRepoMock.Object,
            peopleServiceMock.Object,
            notificationMock.Object);

        // 2. Właściwy test (Act) & 3. Weryfikacja (Assert)
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orderService.CancelPackageAsync(packageId, senderId));

        Assert.Equal("Cannot cancel package in status Shipped", ex.Message);
        Assert.Equal(PackageStatus.Shipped, package.Status);
        packageRepoMock.Verify(r => r.UpdateAsync(It.IsAny<Package>()), Times.Never);
    }

    [Fact]
    public void WF11_Deliver_WhenPackageInInvalidStatus_ThrowsInvalidOperationException()
    {
        // 1. Przygotowanie (Arrange)
        var package = new Package { Id = 1 };
        MakeReadyForAcceptance(package);
        package.Accept();

        // 2. Właściwy test (Act) & 3. Weryfikacja (Assert)
        var ex = Assert.Throws<InvalidOperationException>(() => package.Deliver());

        Assert.Equal("Cannot deliver package in status Accepted", ex.Message);
        Assert.Equal(PackageStatus.Accepted, package.Status);
        Assert.Null(package.DeliveredAt);
        Assert.Null(package.PickupCode);
    }

    [Fact]
    public void WF12_Reject_AfterShipped_ThrowsInvalidOperationException()
    {
        // 1. Przygotowanie (Arrange)
        var package = new Package { Id = 1, BoxId = 10, Size = BoxSize.Small };
        MakeReadyForAcceptance(package);
        package.Accept();
        package.Prepare();
        package.Ship();

        // 2. Właściwy test (Act) & 3. Weryfikacja (Assert)
        var ex = Assert.Throws<InvalidOperationException>(() => package.Reject());

        Assert.Equal("Cannot reject package in status Shipped", ex.Message);
        Assert.Equal(PackageStatus.Shipped, package.Status);
    }

    #endregion

    #region ETAP 5: Odbiór i ocena (Diagram 05: 05-customer-collect-and-rate)

    [Fact]
    public async Task WF07_DeliveryPhase_WrongRecipientOrInvalidPin_BlocksCollectionAtomically()
    {
        // 1. Przygotowanie (Arrange)
        var packageRepoMock = new Mock<IPackageRepository>();
        const int realRecipientId = 20;
        const int impostorUserId = 999;

        var deliveredPackage = new Package
        {
            Id = 55,
            RecipientId = realRecipientId,
            BoxId = 101,
            DestinationBoxId = 202
        };
        deliveredPackage.ConfirmDeposit();
        deliveredPackage.Payment = new Payment { PackageId = 55, PayerEmail = "p@ex.com", Amount = 15m };
        deliveredPackage.Payment.Complete();
        deliveredPackage.Accept();
        deliveredPackage.Prepare();
        deliveredPackage.Ship();
        deliveredPackage.Deliver();

        packageRepoMock.Setup(r => r.GetByIdAsync(55)).ReturnsAsync(deliveredPackage);

        var fulfillmentService = new PackageFulfillmentService(
            packageRepoMock.Object,
            Mock.Of<INotificationService>(),
            Mock.Of<IPeopleService>());

        var wrongPin = deliveredPackage.PickupCode == "123456" ? "654321" : "123456";

        // 2. Właściwy test (Act) & 3. Weryfikacja (Assert)
        // Próba odbioru przez osobę nieuprawnioną
        var exImpostor = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fulfillmentService.CollectPackageAsync(55, impostorUserId, deliveredPackage.PickupCode!));

        // Próba odbioru ze złym kodem odbioru
        var exPin = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fulfillmentService.CollectPackageAsync(55, realRecipientId, wrongPin));

        Assert.Equal("Only this package's recipient can collect it", exImpostor.Message);
        Assert.Equal("Invalid pickup code", exPin.Message);
        Assert.Equal(PackageStatus.Delivered, deliveredPackage.Status);
        Assert.Null(deliveredPackage.CollectedAt);
    }

    [Fact]
    public async Task WF15_RateCourierAsync_ValidatesScoreBounds_Recipient_AndPostCollectionStatus()
    {
        // 1. Przygotowanie (Arrange)
        var packageRepoMock = new Mock<IPackageRepository>();
        var peopleMock = new Mock<IPeopleService>();
        var ratingService = new RatingService(packageRepoMock.Object, peopleMock.Object);

        var package = new Package { Id = 10, RecipientId = 42, CourierId = 88 };
        MakeReadyForAcceptance(package);
        package.Accept();
        package.Prepare();
        package.Ship();
        package.Deliver();
        packageRepoMock.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(package);

        // 2. Właściwy test (Act) & 3. Weryfikacja (Assert)
        var exLow = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ratingService.RateCourierAsync(10, 42, 0));
        var exHigh = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ratingService.RateCourierAsync(10, 42, 6));
        var exUncollected = await Assert.ThrowsAsync<InvalidOperationException>(() => ratingService.RateCourierAsync(10, 42, 5));

        package.Collect(package.PickupCode!);
        var exUnauthorized = await Assert.ThrowsAsync<InvalidOperationException>(() => ratingService.RateCourierAsync(10, 999, 5));

        await ratingService.RateCourierAsync(10, 42, 4);

        Assert.Contains("Score must be between 1 and 5", exLow.Message);
        Assert.Contains("Score must be between 1 and 5", exHigh.Message);
        Assert.Equal("Can only rate a delivery after it has been collected", exUncollected.Message);
        Assert.Equal("Only this package's recipient can rate it", exUnauthorized.Message);
        peopleMock.Verify(p => p.UpdateRankAsync(88, 4), Times.Once);
    }

    [Fact]
    public async Task WF26_RateCourier_WhenNoCourierAssigned_ThrowsInvalidOperationException()
    {
        // 1. Przygotowanie (Arrange)
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

        // 2. Właściwy test (Act) & 3. Weryfikacja (Assert)
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RateCourierAsync(1, raterUserId: 25, score: 4));

        Assert.Equal("This package has no courier to rate", ex.Message);
    }

    [Fact]
    public async Task WF31_DeliveryPhase_UncollectedPackagePast48Hours_CustomerFailsToCollect_TransitionsToExpired()
    {
        // 1. Przygotowanie (Arrange)
        var packageRepoMock = new Mock<IPackageRepository>();
        var notificationServiceMock = new Mock<INotificationService>();

        var package = new Package
        {
            Id = 1,
            BoxId = 10,
            RecipientId = 2
        };
        MakeReadyForAcceptance(package);
        package.Accept();
        package.Prepare();
        package.Ship();

        var beforeDelivery = DateTime.UtcNow;
        package.Deliver();
        Assert.Equal(PackageStatus.Delivered, package.Status);

        // Weryfikacja że Deliver() odnawia okno 48h od momentu doręczenia
        Assert.NotNull(package.ExpiredAt);
        Assert.True(package.ExpiredAt >= beforeDelivery.AddHours(47));

        // Symulacja upłynięcia 48h na odbiór (termin minął)
        typeof(Package).GetProperty(nameof(Package.ExpiredAt))?
            .SetValue(package, DateTime.UtcNow.AddHours(-1));

        packageRepoMock.Setup(r => r.GetPackagesRequiringExpirationAsync())
            .ReturnsAsync(new List<Package> { package });

        var acceptanceService = new PackageAcceptanceService(
            packageRepoMock.Object,
            Mock.Of<IBoxRepository>(),
            notificationServiceMock.Object,
            Mock.Of<IPeopleService>());

        // 2. Właściwy test (Act) - zadanie w tle przetwarzające przeterminowane przesyłki
        await acceptanceService.ProcessExpiredPackagesAsync();

        // 3. Weryfikacja rezultatu (Assert)
        Assert.Equal(PackageStatus.Expired, package.Status);
        packageRepoMock.Verify(r => r.UpdateAsync(package), Times.Once);
        notificationServiceMock.Verify(n => n.NotifyAsync("system",
            It.Is<string>(msg => msg.Contains("EXPIRED due to inactivity"))), Times.Once);
    }

    [Fact]
    public void WF31_Expire_BeforeDeadlinePassed_ThrowsInvalidOperationException()
    {
        // 1. Przygotowanie (Arrange)
        var package = new Package { Id = 1 };
        MakeReadyForAcceptance(package);
        package.Accept();
        package.Prepare();
        package.Ship();
        package.Deliver();

        // 2. Właściwy test (Act) & 3. Weryfikacja (Assert)
        var ex = Assert.Throws<InvalidOperationException>(() => package.Expire());
        Assert.Contains("deadline has not passed yet", ex.Message);
        Assert.Equal(PackageStatus.Delivered, package.Status);
    }

    #endregion

    #region ETAP 6: Proces zwrotu produktów (Diagram 06: 06-return-process)

    [Fact]
    public async Task WF08_ReturnPhase_CollectedPackage_CustomerReturnsPartialItems_ReversesRoutingAndIntegratesLogistics()
    {
        // 1. Przygotowanie (Arrange)
        var packageRepoMock = new Mock<IPackageRepository>();
        var boxRepoMock = new Mock<IBoxRepository>();
        var orderServiceMock = new Mock<IPackageOrderService>();
        var notificationServiceMock = new Mock<INotificationService>();

        const int originalPackageId = 100;
        const int merchantSenderId = 10;
        const int customerRecipientId = 20;
        const int merchantLockerId = 1;
        const int customerLockerId = 2;
        const int merchantBoxId = 101;
        const int customerBoxId = 202;

        var originalPackage = new Package
        {
            Id = originalPackageId,
            SenderId = merchantSenderId,
            RecipientId = customerRecipientId,
            BoxId = merchantBoxId,
            DestinationBoxId = customerBoxId,
            Size = BoxSize.Medium
        };

        var item1 = new PackageItem { Id = 1, PackageId = originalPackageId, Name = "Klawiatura" };
        var item2 = new PackageItem { Id = 2, PackageId = originalPackageId, Name = "Myszka" };
        var item3 = new PackageItem { Id = 3, PackageId = originalPackageId, Name = "Podkładka" };

        originalPackage.Items.Add(item1);
        originalPackage.Items.Add(item2);
        originalPackage.Items.Add(item3);

        originalPackage.ConfirmDeposit();
        originalPackage.Payment = new Payment { PackageId = originalPackageId, PayerEmail = "c@ex.com", Amount = 15m };
        originalPackage.Payment.Complete();
        originalPackage.Accept();
        originalPackage.Prepare();
        originalPackage.Ship();
        originalPackage.Deliver();
        originalPackage.Collect(originalPackage.PickupCode!);

        packageRepoMock.Setup(r => r.GetByIdAsync(originalPackageId)).ReturnsAsync(originalPackage);
        boxRepoMock.Setup(b => b.GetByIdAsync(merchantBoxId)).ReturnsAsync(new Box { Id = merchantBoxId, ParcelLockerId = merchantLockerId });
        boxRepoMock.Setup(b => b.GetByIdAsync(customerBoxId)).ReturnsAsync(new Box { Id = customerBoxId, ParcelLockerId = customerLockerId });

        var returnPackage = new Package
        {
            Id = 888,
            SenderId = customerRecipientId,
            RecipientId = merchantSenderId,
            BoxId = 301,
            DestinationBoxId = 302,
            Size = BoxSize.Medium
        };

        orderServiceMock.Setup(o => o.CreatePackageAsync(
                customerRecipientId,
                merchantSenderId,
                customerLockerId,
                merchantLockerId,
                BoxSize.Medium,
                null))
            .ReturnsAsync(returnPackage);

        var returnService = new PackageReturnService(
            packageRepoMock.Object,
            boxRepoMock.Object,
            orderServiceMock.Object,
            notificationServiceMock.Object);

        // 2. Właściwy test (Act)
        // Krok 1: Pobranie pozycji przed zwrotem
        var availableBefore = await returnService.GetAvailableItemsForReturnAsync(originalPackageId, customerRecipientId);

        // Krok 2: Utworzenie przesyłki zwrotnej dla 1 przedmiotu
        var createdReturn = await returnService.CreateReturnDeliveryAsync(
            originalPackageId,
            customerRecipientId,
            itemIds: new[] { 2 });

        // Krok 3: Ponowne pobranie pozycji po zwrocie
        var availableAfter = await returnService.GetAvailableItemsForReturnAsync(originalPackageId, customerRecipientId);

        // 3. Weryfikacja rezultatu (Assert)
        Assert.Equal(3, availableBefore.Count);
        Assert.NotNull(createdReturn);
        Assert.Equal(888, createdReturn.Id);
        Assert.Equal(originalPackageId, createdReturn.OriginalPackageId);
        Assert.True(createdReturn.IsReturn);
        Assert.Equal(PackageStatus.Ordered, createdReturn.Status);

        Assert.True(item2.IsReturned);
        Assert.Equal(888, item2.ReturnPackageId);
        Assert.False(item1.IsReturned);
        Assert.False(item3.IsReturned);

        Assert.Equal(2, availableAfter.Count);
        Assert.DoesNotContain(availableAfter, i => i.Id == 2);

        orderServiceMock.Verify(o => o.CreatePackageAsync(
            customerRecipientId,
            merchantSenderId,
            customerLockerId,
            merchantLockerId,
            BoxSize.Medium,
            null), Times.Once);

        packageRepoMock.Verify(r => r.UpdateAsync(originalPackage), Times.Once);
        packageRepoMock.Verify(r => r.UpdateAsync(createdReturn), Times.Once);
        notificationServiceMock.Verify(n => n.NotifyAsync("system",
            It.Is<string>(s => s.Contains("Return delivery 888 created"))), Times.Once);
    }

    [Theory]
    [InlineData(PackageStatus.Ordered)]
    [InlineData(PackageStatus.Accepted)]
    [InlineData(PackageStatus.Preparing)]
    [InlineData(PackageStatus.Shipped)]
    public async Task WF17_CreateReturnDelivery_BeforeCollection_ThrowsInvalidOperationException(PackageStatus status)
    {
        // 1. Przygotowanie (Arrange)
        var packageRepo = new Mock<IPackageRepository>();
        var package = new Package { Id = 1, RecipientId = 20, SenderId = 10, BoxId = 101, DestinationBoxId = 202 };
        package.Items.Add(new PackageItem { Id = 1, PackageId = 1, Name = "Produkt" });

        if (status != PackageStatus.Ordered)
        {
            package.ConfirmDeposit();
            package.Payment = new Payment { PackageId = 1, PayerEmail = "test@ex.com", Amount = 10 };
            package.Payment.Complete();
            package.Accept();
        }
        if (status == PackageStatus.Preparing || status == PackageStatus.Shipped) package.Prepare();
        if (status == PackageStatus.Shipped) package.Ship();

        packageRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);
        var returnService = new PackageReturnService(packageRepo.Object, Mock.Of<IBoxRepository>(), Mock.Of<IPackageOrderService>(), Mock.Of<INotificationService>());

        // 2. Właściwy test (Act) & 3. Weryfikacja (Assert)
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            returnService.CreateReturnDeliveryAsync(1, 20, new[] { 1 }));

        Assert.Contains("Only collected packages can be returned", ex.Message);
    }

    [Fact]
    public async Task WF18_CreateReturnDelivery_ByNonRecipient_ThrowsInvalidOperationException()
    {
        // 1. Przygotowanie (Arrange)
        var packageRepo = new Mock<IPackageRepository>();
        var package = CreateCollectedPackageWithItems(packageId: 1, recipientId: 20);
        packageRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);

        var returnService = new PackageReturnService(packageRepo.Object, Mock.Of<IBoxRepository>(), Mock.Of<IPackageOrderService>(), Mock.Of<INotificationService>());

        // 2. Właściwy test (Act) & 3. Weryfikacja (Assert)
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            returnService.CreateReturnDeliveryAsync(1, customerId: 999, new[] { 1 }));

        Assert.Equal("Only the package recipient can request a return.", ex.Message);
    }

    [Fact]
    public async Task WF19_CreateReturnDelivery_ItemAlreadyReturned_ThrowsInvalidOperationException()
    {
        // 1. Przygotowanie (Arrange)
        var packageRepo = new Mock<IPackageRepository>();
        var package = CreateCollectedPackageWithItems(packageId: 1, recipientId: 20);
        package.Items.First(i => i.Id == 1).ReturnPackageId = 55;
        packageRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);

        var returnService = new PackageReturnService(packageRepo.Object, Mock.Of<IBoxRepository>(), Mock.Of<IPackageOrderService>(), Mock.Of<INotificationService>());

        // 2. Właściwy test (Act) & 3. Weryfikacja (Assert)
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            returnService.CreateReturnDeliveryAsync(1, 20, new[] { 1 }));

        Assert.Equal("One or more selected items have already been included in a return delivery.", ex.Message);
    }

    [Fact]
    public async Task WF20_CreateReturnDelivery_SequentialReturns_AllowsReturningSecondItemInSeparatePackage()
    {
        // 1. Przygotowanie (Arrange)
        var packageRepo = new Mock<IPackageRepository>();
        var boxRepo = new Mock<IBoxRepository>();
        var orderService = new Mock<IPackageOrderService>();
        var package = CreateCollectedPackageWithItems(packageId: 1, senderId: 10, recipientId: 20);

        package.Items.First(i => i.Id == 1).ReturnPackageId = 101;

        boxRepo.Setup(b => b.GetByIdAsync(202)).ReturnsAsync(new Box { Id = 202, ParcelLockerId = 2 });
        boxRepo.Setup(b => b.GetByIdAsync(101)).ReturnsAsync(new Box { Id = 101, ParcelLockerId = 1 });
        packageRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);

        var secondReturnPackage = new Package { Id = 102, SenderId = 20, RecipientId = 10, Size = BoxSize.Medium };
        orderService.Setup(o => o.CreatePackageAsync(20, 10, 2, 1, BoxSize.Medium, null))
            .ReturnsAsync(secondReturnPackage);

        var returnService = new PackageReturnService(packageRepo.Object, boxRepo.Object, orderService.Object, Mock.Of<INotificationService>());

        // 2. Właściwy test (Act)
        var result = await returnService.CreateReturnDeliveryAsync(1, 20, new[] { 2 });

        // 3. Weryfikacja rezultatu (Assert)
        Assert.NotNull(result);
        Assert.Equal(102, result.Id);
        Assert.Equal(102, package.Items.First(i => i.Id == 2).ReturnPackageId);
        Assert.False(package.Items.First(i => i.Id == 3).IsReturned);
    }

    [Fact]
    public async Task WF21_CreateReturnDelivery_WhenNoFreeBoxAvailable_RollsBackAndItemsRemainUnreturned()
    {
        // 1. Przygotowanie (Arrange)
        var packageRepo = new Mock<IPackageRepository>();
        var boxRepo = new Mock<IBoxRepository>();
        var orderService = new Mock<IPackageOrderService>();
        var package = CreateCollectedPackageWithItems(1, 10, 20, 101, 202);

        boxRepo.Setup(b => b.GetByIdAsync(202)).ReturnsAsync(new Box { Id = 202, ParcelLockerId = 2 });
        boxRepo.Setup(b => b.GetByIdAsync(101)).ReturnsAsync(new Box { Id = 101, ParcelLockerId = 1 });
        packageRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);

        orderService.Setup(o => o.CreatePackageAsync(20, 10, 2, 1, BoxSize.Medium, null))
            .ThrowsAsync(new InvalidOperationException("No free Medium box at chosen origin locker"));

        var returnService = new PackageReturnService(packageRepo.Object, boxRepo.Object, orderService.Object, Mock.Of<INotificationService>());

        // 2. Właściwy test (Act) & 3. Weryfikacja (Assert)
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => returnService.CreateReturnDeliveryAsync(1, 20, new[] { 1 }));

        Assert.Contains("No free Medium box", ex.Message);
        Assert.False(package.Items.First(i => i.Id == 1).IsReturned);
        Assert.Null(package.Items.First(i => i.Id == 1).ReturnPackageId);
        packageRepo.Verify(r => r.UpdateAsync(It.IsAny<Package>()), Times.Never);
    }

    [Fact]
    public async Task WF27_ReturnPhase_GetAvailableItems_ByNonRecipient_ThrowsInvalidOperationException()
    {
        // 1. Przygotowanie (Arrange)
        var packageRepo = new Mock<IPackageRepository>();
        var service = new PackageReturnService(
            packageRepo.Object, Mock.Of<IBoxRepository>(), Mock.Of<IPackageOrderService>(), Mock.Of<INotificationService>());

        var package = new Package { Id = 1, RecipientId = 42 };
        packageRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);

        // 2. Właściwy test (Act) & 3. Weryfikacja (Assert)
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetAvailableItemsForReturnAsync(1, customerId: 999));

        Assert.Equal("Only the package recipient can query returnable items.", ex.Message);
    }

    [Fact]
    public async Task WF28_ReturnPhase_CreateReturnDelivery_EmptyItemsOrForeignItem_ThrowsArgumentException()
    {
        // 1. Przygotowanie (Arrange)
        var packageRepo = new Mock<IPackageRepository>();
        var service = new PackageReturnService(
            packageRepo.Object, Mock.Of<IBoxRepository>(), Mock.Of<IPackageOrderService>(), Mock.Of<INotificationService>());

        var package = CreateCollectedPackageWithItems(1, 10, 42, 101, 202);
        packageRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);

        // 2. Właściwy test (Act) & 3. Weryfikacja (Assert)
        // Pusta lista produktów
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateReturnDeliveryAsync(1, 42, Array.Empty<int>()));

        // Produkt nienależący do tej paczki
        var exForeign = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateReturnDeliveryAsync(1, 42, new[] { 999 }));

        Assert.Equal("One or more selected items do not belong to the specified package.", exForeign.Message);
    }

    [Fact]
    public async Task WF32_ReturnPhase_ReturnAllItems_SetsAreAllItemsReturnedTrue()
    {
        // 1. Przygotowanie (Arrange)
        var packageRepo = new Mock<IPackageRepository>();
        var boxRepo = new Mock<IBoxRepository>();
        var orderService = new Mock<IPackageOrderService>();
        var package = CreateCollectedPackageWithItems(packageId: 1, senderId: 10, recipientId: 20);

        boxRepo.Setup(b => b.GetByIdAsync(202)).ReturnsAsync(new Box { Id = 202, ParcelLockerId = 2 });
        boxRepo.Setup(b => b.GetByIdAsync(101)).ReturnsAsync(new Box { Id = 101, ParcelLockerId = 1 });
        packageRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);

        var returnPkg = new Package { Id = 88, SenderId = 20, RecipientId = 10 };
        orderService.Setup(o => o.CreatePackageAsync(20, 10, 2, 1, BoxSize.Medium, null))
            .ReturnsAsync(returnPkg);

        var returnService = new PackageReturnService(packageRepo.Object, boxRepo.Object, orderService.Object, Mock.Of<INotificationService>());

        // 2. Właściwy test (Act)
        await returnService.CreateReturnDeliveryAsync(1, 20, new[] { 1, 2, 3 });

        // 3. Weryfikacja rezultatu (Assert)
        Assert.True(package.HasReturns);
        Assert.True(package.AreAllItemsReturned);
    }

    #endregion
}
