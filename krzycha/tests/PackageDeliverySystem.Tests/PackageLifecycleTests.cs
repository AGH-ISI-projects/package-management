using System.Reflection;
using System.Text.RegularExpressions;
using Moq;
using PackageDeliverySystem.Core;
using PackageDeliverySystem.Infrastructure;
using Xunit;

namespace PackageDeliverySystem.Tests;

public class PackageLifecycleTests
{
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

    // =========================================================================
    // GROUP A: Happy Path, Cancellation & Payment Flow (T1 - T5)
    // =========================================================================

    [Fact]
    public void T01_FullLifecycleHappyPath_TransitionsToCollectedWithValidTimestampsAndPickupCode()
    {
        // Arrange
        var package = new Package
        {
            Id = 1,
            SenderId = 10,
            RecipientId = 20,
            BoxId = 101,
            DestinationBoxId = 202
        };
        Assert.Equal(PackageStatus.Ordered, package.Status);

        package.ConfirmDeposit();
        package.Payment = new Payment
        {
            PackageId = package.Id,
            PayerEmail = "customer@example.com",
            Amount = 19.99m
        };
        package.Payment.Complete();

        // Act
        package.Accept();
        package.Prepare();
        package.Ship();
        package.Deliver();
        package.Collect(package.PickupCode!);

        // Assert
        Assert.Equal(PackageStatus.Collected, package.Status);
        Assert.NotNull(package.DepositConfirmedAt);
        Assert.NotNull(package.AcceptedAt);
        Assert.NotNull(package.PreparedAt);
        Assert.NotNull(package.ShippedAt);
        Assert.NotNull(package.DeliveredAt);
        Assert.NotNull(package.CollectedAt);
        Assert.NotNull(package.PickupCode);
        Assert.Matches(@"^[0-9]{6}$", package.PickupCode);

        // Monotonic timestamp ordering
        Assert.True(package.DepositConfirmedAt <= package.AcceptedAt);
        Assert.True(package.AcceptedAt <= package.PreparedAt);
        Assert.True(package.PreparedAt <= package.ShippedAt);
        Assert.True(package.ShippedAt <= package.DeliveredAt);
        Assert.True(package.DeliveredAt <= package.CollectedAt);
    }

    [Fact]
    public void T02_Cancel_FromOrderedStage_TransitionsToCancelled()
    {
        // Arrange
        var package = new Package { Id = 1 };
        Assert.Equal(PackageStatus.Ordered, package.Status);

        // Act
        package.Cancel();

        // Assert
        Assert.Equal(PackageStatus.Cancelled, package.Status);
    }

    [Fact]
    public void T03_Cancel_FromAcceptedStage_TransitionsToCancelled()
    {
        // Arrange
        var package = new Package { Id = 1 };
        MakeReadyForAcceptance(package);
        package.Accept();
        Assert.Equal(PackageStatus.Accepted, package.Status);

        // Act
        package.Cancel();

        // Assert
        Assert.Equal(PackageStatus.Cancelled, package.Status);
    }

    [Fact]
    public void T04_Cancel_AfterShipped_ThrowsInvalidOperationException()
    {
        // Arrange
        var package = new Package { Id = 1 };
        MakeReadyForAcceptance(package);
        package.Accept();
        package.Prepare();
        package.Ship();
        Assert.Equal(PackageStatus.Shipped, package.Status);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => package.Cancel());
        Assert.Equal("Cannot cancel package in status Shipped", ex.Message);
        Assert.Equal(PackageStatus.Shipped, package.Status);
    }

    [Fact]
    public async Task T05_RequestPaymentAndConfirm_FailureAndRetryFlow_CompletesSuccessfully()
    {
        // Arrange
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

        // Act & Assert - Attempt 1 (Failure)
        var ref1 = await intakeService.RequestPaymentAsync(1, "user@example.com", 25.00m);
        Assert.Equal("REF-FAIL-1", ref1);

        await intakeService.ConfirmPaymentAsync(1);
        Assert.NotNull(package.Payment);
        Assert.Equal(PaymentStatus.Failed, package.Payment.Status);

        // Act & Assert - Attempt 2 (Retry succeeds)
        var ref2 = await intakeService.RequestPaymentAsync(1, "user@example.com", 25.00m);
        Assert.Equal("REF-SUCCESS-2", ref2);

        await intakeService.ConfirmPaymentAsync(1);
        Assert.Equal(PaymentStatus.Completed, package.Payment.Status);
        Assert.Equal("REF-SUCCESS-2", package.Payment.ExternalReference);
        Assert.NotNull(package.Payment.CompletedAt);

        packageRepoMock.Verify(r => r.UpdateAsync(package), Times.Exactly(4));
        notificationMock.Verify(n => n.NotifyAsync("system",
            It.Is<string>(s => s.Contains("Payment for package 1 has been confirmed."))), Times.Once);
    }

    // =========================================================================
    // GROUP B: Courier Issues & Operational Anomalies (T6 - T9)
    // =========================================================================

    [Fact]
    public async Task T06_ReportMissingDepositAsync_WhenEmptyBoxReported_CancelsPackageAndNotifies()
    {
        // Arrange
        var packageRepoMock = new Mock<IPackageRepository>();
        var paymentGatewayMock = new Mock<IPaymentGateway>();
        var notificationMock = new Mock<INotificationService>();
        var peopleMock = new Mock<IPeopleService>();

        var intakeService = new PackageIntakeService(
            packageRepoMock.Object,
            paymentGatewayMock.Object,
            notificationMock.Object,
            peopleMock.Object);

        var package = new Package { Id = 10 };
        var courier = new Courier { Id = 7, Name = "Jan", Surname = "Kowalski" };

        packageRepoMock.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(package);
        peopleMock.Setup(p => p.GetCourierAsync(7)).ReturnsAsync(courier);

        // Act
        await intakeService.ReportMissingDepositAsync(10, 7);

        // Assert
        Assert.Equal(PackageStatus.Cancelled, package.Status);
        packageRepoMock.Verify(r => r.UpdateAsync(package), Times.Once);
        notificationMock.Verify(n => n.NotifyAsync("system",
            "Package 10 was cancelled: courier Jan found the box empty at pickup (sender's fault)."),
            Times.Once);
    }

    [Fact]
    public async Task T07_ProcessMissedDepositsAsync_BackgroundJob_CancelsExpiredOrdersAndNotifies()
    {
        // Arrange
        var packageRepoMock = new Mock<IPackageRepository>();
        var paymentGatewayMock = new Mock<IPaymentGateway>();
        var notificationMock = new Mock<INotificationService>();
        var peopleMock = new Mock<IPeopleService>();

        var intakeService = new PackageIntakeService(
            packageRepoMock.Object,
            paymentGatewayMock.Object,
            notificationMock.Object,
            peopleMock.Object);

        var pkg1 = new Package { Id = 101, DepositDeadline = DateTime.UtcNow.AddHours(-1) };
        var pkg2 = new Package { Id = 102, DepositDeadline = DateTime.UtcNow.AddHours(-5) };

        packageRepoMock.Setup(r => r.GetPackagesWithMissedDepositAsync())
            .ReturnsAsync(new List<Package> { pkg1, pkg2 });

        // Act
        await intakeService.ProcessMissedDepositsAsync();

        // Assert
        Assert.Equal(PackageStatus.Cancelled, pkg1.Status);
        Assert.Equal(PackageStatus.Cancelled, pkg2.Status);
        packageRepoMock.Verify(r => r.UpdateAsync(pkg1), Times.Once);
        packageRepoMock.Verify(r => r.UpdateAsync(pkg2), Times.Once);
        notificationMock.Verify(n => n.NotifyAsync("system",
            "Package 101 was cancelled: nothing was deposited before the pickup window closed."), Times.Once);
        notificationMock.Verify(n => n.NotifyAsync("system",
            "Package 102 was cancelled: nothing was deposited before the pickup window closed."), Times.Once);
    }

    [Fact]
    public void T08_Expire_PackageTransitExpirationDuringShipping_TransitionsToExpired()
    {
        // Arrange
        var package = new Package { Id = 1 };
        MakeReadyForAcceptance(package);
        package.Accept();
        package.Prepare();
        package.Ship();
        Assert.Equal(PackageStatus.Shipped, package.Status);

        // Simulate transit deadline passing in the past via reflection
        typeof(Package).GetProperty(nameof(Package.ExpiredAt))!
            .SetValue(package, DateTime.UtcNow.AddHours(-1));

        // Act
        package.Expire();

        // Assert
        Assert.Equal(PackageStatus.Expired, package.Status);
    }

    [Fact]
    public void T09_Deliver_WhenPackageInInvalidStatus_ThrowsInvalidOperationException()
    {
        // Arrange
        var package = new Package { Id = 1 };
        MakeReadyForAcceptance(package);
        package.Accept();
        Assert.Equal(PackageStatus.Accepted, package.Status);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => package.Deliver());
        Assert.Equal("Cannot deliver package in status Accepted", ex.Message);
        Assert.Equal(PackageStatus.Accepted, package.Status);
        Assert.Null(package.DeliveredAt);
        Assert.Null(package.PickupCode);
    }

    // =========================================================================
    // GROUP C: Recipient Issues & Pickup Anomalies (T10 - T12)
    // =========================================================================

    [Fact]
    public void T10_Collect_WithInvalidPickupCode_ThrowsInvalidOperationException()
    {
        // Arrange
        var package = new Package
        {
            Id = 1,
            BoxId = 10,
            Size = BoxSize.Small,
            RecipientId = 2
        };
        MakeReadyForAcceptance(package);
        package.Accept();
        package.Prepare();
        package.Ship();
        package.Deliver();

        Assert.Equal(PackageStatus.Delivered, package.Status);
        Assert.NotNull(package.PickupCode);

        var wrongCode = package.PickupCode == "000000" ? "999999" : "000000";

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => package.Collect(wrongCode));
        Assert.Equal("Invalid pickup code", ex.Message);
        Assert.Equal(PackageStatus.Delivered, package.Status);
        Assert.Null(package.CollectedAt);
    }

    [Fact]
    public void T11_Expire_DeliveredPackageUncollectedPast48Hours_TransitionsToExpired()
    {
        // Arrange
        var package = new Package
        {
            Id = 1,
            BoxId = 10,
            Size = BoxSize.Small,
            RecipientId = 2
        };
        MakeReadyForAcceptance(package);
        package.Accept();
        package.Prepare();
        package.Ship();
        package.Deliver();

        Assert.Equal(PackageStatus.Delivered, package.Status);

        // Simulate 48h uncollected past deadline
        typeof(Package).GetProperty(nameof(Package.ExpiredAt))!
            .SetValue(package, DateTime.UtcNow.AddHours(-1));

        // Act
        package.Expire();

        // Assert
        Assert.Equal(PackageStatus.Expired, package.Status);
    }

    [Fact]
    public async Task T12_CollectPackageAsync_UnauthorizedUser_ThrowsInvalidOperationException()
    {
        // Arrange
        var packageRepoMock = new Mock<IPackageRepository>();
        var notificationMock = new Mock<INotificationService>();
        var peopleMock = new Mock<IPeopleService>();

        var fulfillmentService = new PackageFulfillmentService(
            packageRepoMock.Object,
            notificationMock.Object,
            peopleMock.Object);

        var package = new Package
        {
            Id = 1,
            BoxId = 10,
            Size = BoxSize.Small,
            RecipientId = 42
        };
        MakeReadyForAcceptance(package);
        package.Accept();
        package.Prepare();
        package.Ship();
        package.Deliver();

        packageRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);

        var unauthorizedUserId = 999;

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fulfillmentService.CollectPackageAsync(1, unauthorizedUserId, package.PickupCode!));

        Assert.Equal("Only this package's recipient can collect it", ex.Message);
        Assert.Equal(PackageStatus.Delivered, package.Status);
        Assert.Null(package.CollectedAt);

        packageRepoMock.Verify(r => r.UpdateAsync(It.IsAny<Package>()), Times.Never);
        notificationMock.Verify(n => n.NotifyAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // =========================================================================
    // GROUP D: Operator Interventions & Rejections (T13 - T15)
    // =========================================================================

    [Fact]
    public async Task T13_RejectPackageAsync_FromOrderedStage_TransitionsToRejectedAndNotifies()
    {
        // Arrange
        var packageRepoMock = new Mock<IPackageRepository>();
        var boxRepoMock = new Mock<IBoxRepository>();
        var notificationMock = new Mock<INotificationService>();
        var peopleMock = new Mock<IPeopleService>();

        var acceptanceService = new PackageAcceptanceService(
            packageRepoMock.Object,
            boxRepoMock.Object,
            notificationMock.Object,
            peopleMock.Object);

        var package = new Package
        {
            Id = 1,
            BoxId = 10,
            Size = BoxSize.Small
        };
        Assert.Equal(PackageStatus.Ordered, package.Status);

        var operatorUser = new Operator { Id = 7, Name = "Alice" };

        packageRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);
        peopleMock.Setup(p => p.GetOperatorAsync(7)).ReturnsAsync(operatorUser);

        // Act
        await acceptanceService.RejectPackageAsync(1, 7);

        // Assert
        Assert.Equal(PackageStatus.Rejected, package.Status);
        packageRepoMock.Verify(r => r.UpdateAsync(package), Times.Once);
        notificationMock.Verify(n => n.NotifyAsync("system",
            It.Is<string>(msg => msg.Contains("rejected") && msg.Contains("Alice") && msg.Contains($"Package {package.Id}"))),
            Times.Once);
    }

    [Fact]
    public async Task T14_RejectPackageAsync_FromAcceptedStage_TransitionsToRejectedAndNotifies()
    {
        // Arrange
        var packageRepoMock = new Mock<IPackageRepository>();
        var boxRepoMock = new Mock<IBoxRepository>();
        var notificationMock = new Mock<INotificationService>();
        var peopleMock = new Mock<IPeopleService>();

        var acceptanceService = new PackageAcceptanceService(
            packageRepoMock.Object,
            boxRepoMock.Object,
            notificationMock.Object,
            peopleMock.Object);

        var package = new Package
        {
            Id = 1,
            BoxId = 10,
            Size = BoxSize.Small
        };
        MakeReadyForAcceptance(package);
        package.Accept();
        Assert.Equal(PackageStatus.Accepted, package.Status);

        var operatorUser = new Operator { Id = 7, Name = "Alice" };

        packageRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);
        peopleMock.Setup(p => p.GetOperatorAsync(7)).ReturnsAsync(operatorUser);

        // Act
        await acceptanceService.RejectPackageAsync(1, 7);

        // Assert
        Assert.Equal(PackageStatus.Rejected, package.Status);
        packageRepoMock.Verify(r => r.UpdateAsync(package), Times.Once);
        notificationMock.Verify(n => n.NotifyAsync("system",
            It.Is<string>(msg => msg.Contains("rejected") && msg.Contains("Alice") && msg.Contains($"Package {package.Id}"))),
            Times.Once);
    }

    [Fact]
    public void T15_Reject_AfterShipped_ThrowsInvalidOperationException()
    {
        // Arrange
        var package = new Package
        {
            Id = 1,
            BoxId = 10,
            Size = BoxSize.Small
        };
        MakeReadyForAcceptance(package);
        package.Accept();
        package.Prepare();
        package.Ship();
        Assert.Equal(PackageStatus.Shipped, package.Status);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => package.Reject());
        Assert.Equal("Cannot reject package in status Shipped", ex.Message);
        Assert.Equal(PackageStatus.Shipped, package.Status);
    }

    // =========================================================================
    // GROUP E: Preconditions, Idempotency & Role Guards (T16 - T20)
    // =========================================================================

    [Fact]
    public void T16_Accept_WithoutConfirmedDeposit_ThrowsInvalidOperationException()
    {
        // Arrange
        var package = new Package
        {
            Id = 1,
            Payment = new Payment
            {
                PackageId = 1,
                PayerEmail = "payer@example.com",
                Amount = 14.99m
            }
        };
        package.Payment.Complete();
        // DepositConfirmedAt left null

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => package.Accept());
        Assert.Equal("Cannot accept a package that was never deposited into its box", ex.Message);
        Assert.Equal(PackageStatus.Ordered, package.Status);
        Assert.Null(package.AcceptedAt);
    }

    [Fact]
    public void T17_Accept_WithoutCompletedPayment_ThrowsInvalidOperationException()
    {
        // Arrange
        var package = new Package { Id = 1 };
        package.ConfirmDeposit();
        // Payment left null

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => package.Accept());
        Assert.Equal("Cannot accept a package that has not been paid for", ex.Message);
        Assert.Equal(PackageStatus.Ordered, package.Status);
        Assert.Null(package.AcceptedAt);
    }

    [Fact]
    public async Task T18_ConfirmPaymentAsync_WhenAlreadyCompleted_IsIdempotent()
    {
        // Arrange
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

        // Act
        await intakeService.ConfirmPaymentAsync(1);

        // Assert
        Assert.Equal(PaymentStatus.Completed, package.Payment.Status);
        paymentGatewayMock.Verify(g => g.GetPaymentStatusAsync(It.IsAny<string>()), Times.Never);
        packageRepoMock.Verify(r => r.UpdateAsync(It.IsAny<Package>()), Times.Never);
        notificationMock.Verify(n => n.NotifyAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task T19_RateCourierAsync_ValidatesScoreBounds_Recipient_AndPostCollectionStatus()
    {
        // Arrange
        var packageRepoMock = new Mock<IPackageRepository>();
        var peopleMock = new Mock<IPeopleService>();
        var ratingService = new RatingService(packageRepoMock.Object, peopleMock.Object);

        var package = new Package
        {
            Id = 10,
            RecipientId = 42,
            CourierId = 88
        };
        MakeReadyForAcceptance(package);
        package.Accept();
        package.Prepare();
        package.Ship();
        package.Deliver();
        packageRepoMock.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(package);

        // Act & Assert 1: Score < 1 throws ArgumentOutOfRangeException
        var exLow = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => ratingService.RateCourierAsync(10, 42, 0));
        Assert.Contains("Score must be between 1 and 5", exLow.Message);
        Assert.Equal("score", exLow.ParamName);

        // Act & Assert 2: Score > 5 throws ArgumentOutOfRangeException
        var exHigh = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => ratingService.RateCourierAsync(10, 42, 6));
        Assert.Contains("Score must be between 1 and 5", exHigh.Message);
        Assert.Equal("score", exHigh.ParamName);

        // Act & Assert 3: Rating before collection (status is Delivered) throws InvalidOperationException
        var exUncollected = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ratingService.RateCourierAsync(10, 42, 5));
        Assert.Equal("Can only rate a delivery after it has been collected", exUncollected.Message);

        // Act & Assert 4: Collect package, then attempt rating with unauthorized user
        package.Collect(package.PickupCode!);
        var exUnauthorized = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ratingService.RateCourierAsync(10, 999, 5));
        Assert.Equal("Only this package's recipient can rate it", exUnauthorized.Message);

        // Act & Assert 5: Valid rating by recipient succeeds and updates rank
        await ratingService.RateCourierAsync(10, 42, 4);
        peopleMock.Verify(p => p.UpdateRankAsync(88, 4), Times.Once);
    }

    [Fact]
    public void T20_AssignCourierAndOperator_BeforeAcceptedStage_ThrowsInvalidOperationException()
    {
        // Arrange
        var package = new Package(); // Status defaults to PackageStatus.Ordered
        var courier = new Courier { Id = 10, Name = "Jan", Surname = "Kowalski" };
        var op = new Operator { Id = 20, Name = "Anna", Surname = "Nowak" };

        // Act & Assert: AssignCourier on Ordered package throws
        var exCourier = Assert.Throws<InvalidOperationException>(() => package.AssignCourier(courier));
        Assert.Equal("Cannot assign courier to package in status Ordered", exCourier.Message);

        // Act & Assert: AssignOperator on Ordered package throws
        var exOp = Assert.Throws<InvalidOperationException>(() => package.AssignOperator(op));
        Assert.Equal("Cannot assign operator to package in status Ordered", exOp.Message);

        // Assert invariant state
        Assert.Null(package.Courier);
        Assert.Null(package.Operator);
        Assert.Equal(PackageStatus.Ordered, package.Status);
    }
}
