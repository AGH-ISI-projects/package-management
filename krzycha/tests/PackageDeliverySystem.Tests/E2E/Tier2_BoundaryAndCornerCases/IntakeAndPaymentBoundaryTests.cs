using PackageDeliverySystem.Core;
using PackageDeliverySystem.Tests.E2E.Infrastructure;
using Xunit;

namespace PackageDeliverySystem.Tests.E2E.Tier2_BoundaryAndCornerCases;

/// <summary>
/// Tier 2 Boundary & Corner Cases: Intake & Payment (Limits, Nulls, Invalid States, Timeouts).
/// </summary>
public class IntakeAndPaymentBoundaryTests : E2ETestBase
{
    [Fact]
    public async Task TC_T2_INT_01_PaymentConfirmationIdempotencyOnCompletedPayment_NoOp()
    {
        // Arrange
        var package = await CreateOrderedPackageAsync();
        await Harness.IntakeService.RequestPaymentAsync(package.Id, "user@test.com", 25.00m);
        await Harness.IntakeService.ConfirmPaymentAsync(package.Id);

        var notificationsCountBefore = Context.NotificationService.Notifications.Count;

        // Act: Confirm payment a second time
        await Harness.IntakeService.ConfirmPaymentAsync(package.Id);

        // Assert: No new notification emitted, status remains Completed
        var stored = await Context.PackageRepository.GetByIdAsync(package.Id);
        Assert.NotNull(stored?.Payment);
        Assert.Equal(PaymentStatus.Completed, stored.Payment.Status);
        Assert.Equal(notificationsCountBefore, Context.NotificationService.Notifications.Count);
    }

    [Fact]
    public async Task TC_T2_INT_02_AcceptanceGuard_MissingPhysicalDeposit_Throws()
    {
        // Arrange
        var package = await CreateOrderedPackageAsync();
        await Harness.IntakeService.RequestPaymentAsync(package.Id, "user@test.com", 25.00m);
        await Harness.IntakeService.ConfirmPaymentAsync(package.Id);
        // Notice: Deposit is NOT confirmed

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => package.Accept());
        Assert.Equal("Cannot accept a package that was never deposited into its box", ex.Message);
        Assert.Equal(PackageStatus.Ordered, package.Status);
    }

    [Fact]
    public async Task TC_T2_INT_03_AcceptanceGuard_UnpaidOrMissingPayment_Throws()
    {
        // Arrange
        var package = await CreateOrderedPackageAsync();
        await Harness.IntakeService.ConfirmDepositAsync(package.Id, 5);
        // Notice: Payment is NOT requested or completed

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => package.Accept());
        Assert.Equal("Cannot accept a package that has not been paid for", ex.Message);
        Assert.Equal(PackageStatus.Ordered, package.Status);
    }

    [Fact]
    public async Task TC_T2_INT_04_AcceptanceGuard_BoxSizeMismatch_Throws()
    {
        // Arrange: Declared package size is Large, but assigned box is Small
        var package = await CreateOrderedPackageAsync(size: BoxSize.Large);
        await Harness.IntakeService.RequestPaymentAsync(package.Id, "user@test.com", 25.00m);
        await Harness.IntakeService.ConfirmPaymentAsync(package.Id);
        await Harness.IntakeService.ConfirmDepositAsync(package.Id, 5);

        // Box 102 in origin locker is Small
        int smallBoxId = 102;
        package.BoxId = smallBoxId;
        await Context.PackageRepository.UpdateAsync(package);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Harness.AcceptanceService.AcceptPackageAsync(package.Id, 7, smallBoxId));

        Assert.Contains("Package declared as Large cannot be accepted into a Small box", ex.Message);
        var stored = await Context.PackageRepository.GetByIdAsync(package.Id);
        Assert.Equal(PackageStatus.Ordered, stored?.Status);
    }

    [Fact]
    public async Task TC_T2_INT_05_DepositConfirmationAfterDeadlineExpiry_Throws()
    {
        // Arrange
        var package = await CreateOrderedPackageAsync();
        package.DepositDeadline = DateTime.UtcNow.AddSeconds(-10);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => package.ConfirmDeposit());
        Assert.Equal("Deposit window has expired; the order must be re-created", ex.Message);
        Assert.Null(package.DepositConfirmedAt);
    }

    [Fact]
    public async Task TC_T2_INT_06_SecondPaymentRequestOnCompletedPayment_Blocked()
    {
        // Arrange
        var package = await CreateOrderedPackageAsync();
        await Harness.IntakeService.RequestPaymentAsync(package.Id, "user@test.com", 25.00m);
        await Harness.IntakeService.ConfirmPaymentAsync(package.Id);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Harness.IntakeService.RequestPaymentAsync(package.Id, "user@test.com", 25.00m));

        Assert.Equal("This package has already been paid for.", ex.Message);
    }

    [Fact]
    public async Task TC_T2_INT_07_DepositConfirmationWithNonExistentCourier_Throws()
    {
        // Arrange
        var package = await CreateOrderedPackageAsync();
        int nonExistentCourierId = 9999;

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => Harness.IntakeService.ConfirmDepositAsync(package.Id, nonExistentCourierId));

        Assert.Contains("Courier not found", ex.Message);
        Assert.Null(package.DepositConfirmedAt);
    }
}
