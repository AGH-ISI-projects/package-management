using PackageDeliverySystem.Core;
using PackageDeliverySystem.Tests.E2E.Infrastructure;
using Xunit;

namespace PackageDeliverySystem.Tests.E2E.Tier1_FeatureCoverage;

/// <summary>
/// Tier 1 Feature Coverage: Cancellation & Rejection Happy Path Tests (Features 2, 3, 4, 13, 14, 15).
/// </summary>
public class CancellationAndRejectionHappyPathTests : E2ETestBase
{
    [Fact]
    public async Task TC_T1_CAN_01_OrderCancellationAtInitialOrderedStage_Success()
    {
        // Arrange
        var package = await CreateOrderedPackageAsync();

        // Act
        package.Cancel();
        await Context.PackageRepository.UpdateAsync(package);

        // Assert
        var stored = await Context.PackageRepository.GetByIdAsync(package.Id);
        Assert.NotNull(stored);
        Assert.Equal(PackageStatus.Cancelled, stored.Status);
    }

    [Fact]
    public async Task TC_T1_CAN_02_OrderCancellationAtAcceptedStage_Success()
    {
        // Arrange
        var package = await CreateAcceptedPackageAsync();
        Assert.Equal(PackageStatus.Accepted, package.Status);

        // Act
        package.Cancel();
        await Context.PackageRepository.UpdateAsync(package);

        // Assert
        var stored = await Context.PackageRepository.GetByIdAsync(package.Id);
        Assert.NotNull(stored);
        Assert.Equal(PackageStatus.Cancelled, stored.Status);
    }

    [Fact]
    public async Task TC_T1_CAN_03_OperatorRejectionFromOrderedStage_Success()
    {
        // Arrange
        var package = await CreateOrderedPackageAsync();
        int operatorId = 3; // OpBob

        // Act
        await Harness.AcceptanceService.RejectPackageAsync(package.Id, operatorId);

        // Assert
        var stored = await Context.PackageRepository.GetByIdAsync(package.Id);
        Assert.NotNull(stored);
        Assert.Equal(PackageStatus.Rejected, stored.Status);
        Assert.True(Context.NotificationService.HasNotificationContaining("rejected by operator OpBob"));
    }

    [Fact]
    public async Task TC_T1_CAN_04_OperatorRejectionFromAcceptedStage_Success()
    {
        // Arrange
        var package = await CreateAcceptedPackageAsync();
        int operatorId = 3; // OpBob

        // Act
        await Harness.AcceptanceService.RejectPackageAsync(package.Id, operatorId);

        // Assert
        var stored = await Context.PackageRepository.GetByIdAsync(package.Id);
        Assert.NotNull(stored);
        Assert.Equal(PackageStatus.Rejected, stored.Status);
        Assert.True(Context.NotificationService.HasNotificationContaining("rejected by operator"));
    }

    [Fact]
    public async Task TC_T1_CAN_05_CourierEmptyLockerDiscoveryCancellation_Success()
    {
        // Arrange
        var package = await CreateOrderedPackageAsync();
        int courierId = 8; // CourierDave

        // Act
        await Harness.IntakeService.ReportMissingDepositAsync(package.Id, courierId);

        // Assert
        var stored = await Context.PackageRepository.GetByIdAsync(package.Id);
        Assert.NotNull(stored);
        Assert.Equal(PackageStatus.Cancelled, stored.Status);
        Assert.True(Context.NotificationService.HasNotificationContaining("found the box empty at pickup"));
    }
}
