using PackageDeliverySystem.Core;
using PackageDeliverySystem.Tests.E2E.Infrastructure;
using Xunit;

namespace PackageDeliverySystem.Tests.E2E.Tier2_BoundaryAndCornerCases;

/// <summary>
/// Tier 2 Boundary & Corner Cases: Cancellation & Rejection (Guards, Invalids, Double-cancels).
/// </summary>
public class CancellationAndRejectionBoundaryTests : E2ETestBase
{
    [Fact]
    public async Task TC_T2_CAN_01_CancellationProhibitedOnceShipped_Throws()
    {
        // Arrange
        var package = await CreateShippedPackageAsync();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => package.Cancel());
        Assert.Equal("Cannot cancel package in status Shipped", ex.Message);
        Assert.Equal(PackageStatus.Shipped, package.Status);
    }

    [Fact]
    public async Task TC_T2_CAN_02_CancellationProhibitedAfterDeliveryOrCollection_Throws()
    {
        // Arrange
        var packageDelivered = await CreateDeliveredPackageAsync();

        // Act & Assert Delivered
        var ex1 = Assert.Throws<InvalidOperationException>(() => packageDelivered.Cancel());
        Assert.Equal("Cannot cancel package in status Delivered", ex1.Message);
        Assert.Equal(PackageStatus.Delivered, packageDelivered.Status);

        // Arrange & Act & Assert Collected
        var packageCollected = await CreateCollectedPackageAsync();
        var ex2 = Assert.Throws<InvalidOperationException>(() => packageCollected.Cancel());
        Assert.Equal("Cannot cancel package in status Collected", ex2.Message);
        Assert.Equal(PackageStatus.Collected, packageCollected.Status);
    }

    [Fact]
    public async Task TC_T2_CAN_03_OperatorRejectionProhibitedOnceShipped_Throws()
    {
        // Arrange
        var package = await CreateShippedPackageAsync();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => package.Reject());
        Assert.Equal("Cannot reject package in status Shipped", ex.Message);
        Assert.Equal(PackageStatus.Shipped, package.Status);
    }

    [Fact]
    public async Task TC_T2_CAN_04_RejectionProhibitedOnDeliveredOrCollectedPackage_Throws()
    {
        // Arrange
        var package = await CreateDeliveredPackageAsync();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => package.Reject());
        Assert.Equal("Cannot reject package in status Delivered", ex.Message);
        Assert.Equal(PackageStatus.Delivered, package.Status);
    }

    [Fact]
    public async Task TC_T2_CAN_05_DoubleCancellationOnAlreadyCancelledPackage_Throws()
    {
        // Arrange
        var package = await CreateOrderedPackageAsync();
        package.Cancel();
        Assert.Equal(PackageStatus.Cancelled, package.Status);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => package.Cancel());
        Assert.Equal("Cannot cancel package in status Cancelled", ex.Message);
    }

    [Fact]
    public async Task TC_T2_CAN_06_OperatorRejectionWithNonExistentOperatorId_Throws()
    {
        // Arrange
        var package = await CreateOrderedPackageAsync();
        int nonExistentOperatorId = 8888;

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => Harness.AcceptanceService.RejectPackageAsync(package.Id, nonExistentOperatorId));

        Assert.Contains("Operator not found", ex.Message);
        var stored = await Context.PackageRepository.GetByIdAsync(package.Id);
        Assert.Equal(PackageStatus.Ordered, stored?.Status);
    }
}
