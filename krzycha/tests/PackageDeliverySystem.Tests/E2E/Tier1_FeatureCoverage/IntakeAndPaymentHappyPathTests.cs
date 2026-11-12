using PackageDeliverySystem.Core;
using PackageDeliverySystem.Tests.E2E.Infrastructure;
using Xunit;

namespace PackageDeliverySystem.Tests.E2E.Tier1_FeatureCoverage;

/// <summary>
/// Tier 1 Feature Coverage: Intake & Payment Happy Path Tests (Features 1, 5, 6, 7, 16, 17, 18).
/// </summary>
public class IntakeAndPaymentHappyPathTests : E2ETestBase
{
    [Fact]
    public async Task TC_T1_INT_01_StandardPackageCreationAndLockerBoxReservation_Success()
    {
        // Arrange
        int senderId = 1;
        int recipientId = 2;
        int originLockerId = 10;
        int destinationLockerId = 20;
        var size = BoxSize.Medium;

        // Act
        var package = await Harness.OrderService.CreatePackageAsync(
            senderId, recipientId, originLockerId, destinationLockerId, size);

        // Assert
        Assert.NotNull(package);
        Assert.Equal(PackageStatus.Ordered, package.Status);
        Assert.Equal(size, package.Size);
        Assert.NotNull(package.BoxId);
        Assert.NotNull(package.DestinationBoxId);
        Assert.NotEqual(package.BoxId, package.DestinationBoxId);
        Assert.True(package.DepositDeadline > DateTime.UtcNow.AddHours(47));

        var stored = await Context.PackageRepository.GetByIdAsync(package.Id);
        Assert.NotNull(stored);
        Assert.Equal(package.Id, stored.Id);
    }

    [Fact]
    public async Task TC_T1_INT_02_PaymentInitiationAndSuccessfulConfirmation_Success()
    {
        // Arrange
        var package = await CreateOrderedPackageAsync();
        decimal amount = 25.00m;
        string payerEmail = "sender@domain.com";

        // Act
        var paymentRef = await Harness.IntakeService.RequestPaymentAsync(package.Id, payerEmail, amount);
        await Harness.IntakeService.ConfirmPaymentAsync(package.Id);

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(paymentRef));
        var stored = await Context.PackageRepository.GetByIdAsync(package.Id);
        Assert.NotNull(stored?.Payment);
        Assert.Equal(PaymentStatus.Completed, stored.Payment.Status);
        Assert.Equal(paymentRef, stored.Payment.ExternalReference);
        Assert.Equal(amount, stored.Payment.Amount);
        Assert.True(Context.NotificationService.HasNotificationContaining("Payment for package"));
    }

    [Fact]
    public async Task TC_T1_INT_03_VisualPhysicalDepositConfirmationByCourier_Success()
    {
        // Arrange
        var package = await CreateOrderedPackageAsync();
        int courierId = 5; // Jan

        // Act
        await Harness.IntakeService.ConfirmDepositAsync(package.Id, courierId);

        // Assert
        var stored = await Context.PackageRepository.GetByIdAsync(package.Id);
        Assert.NotNull(stored);
        Assert.NotNull(stored.DepositConfirmedAt);
        Assert.Equal(PackageStatus.Ordered, stored.Status);
        Assert.True(Context.NotificationService.HasNotificationContaining("Jan"));
        Assert.True(Context.NotificationService.HasNotificationContaining(package.Id.ToString()));
    }

    [Fact]
    public async Task TC_T1_INT_04_OperatorPackageAcceptanceWithVerifiedPreconditions_Success()
    {
        // Arrange
        var package = await CreateOrderedPackageAsync(size: BoxSize.Small);
        int courierId = 5;
        int operatorId = 7; // AdminAlice

        await Harness.IntakeService.RequestPaymentAsync(package.Id, "alice@test.com", 20.00m);
        await Harness.IntakeService.ConfirmPaymentAsync(package.Id);
        await Harness.IntakeService.ConfirmDepositAsync(package.Id, courierId);

        // Act
        await Harness.AcceptanceService.AcceptPackageAsync(package.Id, operatorId, package.BoxId!.Value);

        // Assert
        var stored = await Context.PackageRepository.GetByIdAsync(package.Id);
        Assert.NotNull(stored);
        Assert.Equal(PackageStatus.Accepted, stored.Status);
        Assert.NotNull(stored.AcceptedAt);
        Assert.NotNull(stored.ExpiredAt);
        Assert.Equal(stored.AcceptedAt!.Value.AddHours(48), stored.ExpiredAt!.Value);
        Assert.NotNull(stored.Operator);
        Assert.Equal(operatorId, stored.Operator.Id);
        Assert.True(Context.NotificationService.HasNotificationContaining("accepted by operator"));
    }

    [Fact]
    public async Task TC_T1_INT_05_PaymentFailureFollowedBySuccessfulRetry_CompletesSuccessfully()
    {
        // Arrange
        var package = await CreateOrderedPackageAsync();
        Context.PaymentGateway.DefaultStatus = PaymentStatus.Failed;

        // Act 1: Initial failure
        var failedRef = await Harness.IntakeService.RequestPaymentAsync(package.Id, "user@test.com", 25.00m);
        await Harness.IntakeService.ConfirmPaymentAsync(package.Id);

        var storedMid = await Context.PackageRepository.GetByIdAsync(package.Id);
        Assert.NotNull(storedMid?.Payment);
        Assert.Equal(PaymentStatus.Failed, storedMid.Payment.Status);

        // Act 2: Retry with success
        Context.PaymentGateway.DefaultStatus = PaymentStatus.Completed;
        var successRef = await Harness.IntakeService.RequestPaymentAsync(package.Id, "user@test.com", 25.00m);
        await Harness.IntakeService.ConfirmPaymentAsync(package.Id);

        // Assert
        var storedFinal = await Context.PackageRepository.GetByIdAsync(package.Id);
        Assert.NotNull(storedFinal?.Payment);
        Assert.Equal(PaymentStatus.Completed, storedFinal.Payment.Status);
        Assert.Equal(successRef, storedFinal.Payment.ExternalReference);
        Assert.NotNull(storedFinal.Payment.CompletedAt);
    }
}
