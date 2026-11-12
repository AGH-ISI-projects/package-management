using PackageDeliverySystem.Core;
using PackageDeliverySystem.Tests.E2E.Infrastructure;
using Xunit;

namespace PackageDeliverySystem.Tests.E2E.Tier1_FeatureCoverage;

/// <summary>
/// Tier 1 Feature Coverage: Courier Transit & Delivery Happy Path Tests (Features 1, 9, 20).
/// </summary>
public class CourierTransitHappyPathTests : E2ETestBase
{
    [Fact]
    public async Task TC_T1_COU_01_CourierAssignmentToAcceptedPackage_Success()
    {
        // Arrange
        var package = await CreateAcceptedPackageAsync();
        int courierId = 4; // CourierDan

        // Act
        await Harness.FulfillmentService.AssignCourierAsync(package.Id, courierId);

        // Assert
        var stored = await Context.PackageRepository.GetByIdAsync(package.Id);
        Assert.NotNull(stored?.Courier);
        Assert.Equal(courierId, stored.Courier.Id);
        Assert.True(Context.NotificationService.HasNotificationContaining("assigned to courier Dan"));
    }

    [Fact]
    public async Task TC_T1_COU_02_CourierPreparesPackage_Success()
    {
        // Arrange
        var package = await CreateAcceptedPackageAsync();

        // Act
        package.Prepare();
        await Context.PackageRepository.UpdateAsync(package);

        // Assert
        var stored = await Context.PackageRepository.GetByIdAsync(package.Id);
        Assert.NotNull(stored);
        Assert.Equal(PackageStatus.Preparing, stored.Status);
        Assert.NotNull(stored.PreparedAt);
    }

    [Fact]
    public async Task TC_T1_COU_03_CourierShipsPackageFromOriginLocker_Success()
    {
        // Arrange
        var package = await CreateAcceptedPackageAsync();
        package.Prepare();

        // Act
        package.Ship();
        await Context.PackageRepository.UpdateAsync(package);

        // Assert
        var stored = await Context.PackageRepository.GetByIdAsync(package.Id);
        Assert.NotNull(stored);
        Assert.Equal(PackageStatus.Shipped, stored.Status);
        Assert.NotNull(stored.ShippedAt);
        Assert.NotNull(stored.ExpiredAt);
        Assert.Equal(stored.ShippedAt!.Value.AddHours(48), stored.ExpiredAt!.Value);
    }

    [Fact]
    public async Task TC_T1_COU_04_CourierDeliversPackageToDestinationLocker_Success()
    {
        // Arrange
        var package = await CreateAcceptedPackageAsync();
        package.Prepare();
        package.Ship();

        // Act
        package.Deliver();
        await Context.PackageRepository.UpdateAsync(package);

        // Assert
        var stored = await Context.PackageRepository.GetByIdAsync(package.Id);
        Assert.NotNull(stored);
        Assert.Equal(PackageStatus.Delivered, stored.Status);
        Assert.NotNull(stored.DeliveredAt);
        Assert.NotNull(stored.PickupCode);
        Assert.Equal(6, stored.PickupCode.Length);
        Assert.True(int.TryParse(stored.PickupCode, out _));
    }

    [Fact]
    public async Task TC_T1_COU_05_FullSequentialCourierTransitServicePipeline_Success()
    {
        // Arrange
        var package = await CreateAcceptedPackageAsync();
        int courierId = 4;
        await Harness.FulfillmentService.AssignCourierAsync(package.Id, courierId);

        // Act 1: Ship
        await Harness.FulfillmentService.ShipPackageAsync(package.Id);
        var storedShipped = await Context.PackageRepository.GetByIdAsync(package.Id);
        Assert.Equal(PackageStatus.Shipped, storedShipped?.Status);

        // Act 2: Deliver
        await Harness.FulfillmentService.DeliverPackageAsync(package.Id);

        // Assert
        var storedDelivered = await Context.PackageRepository.GetByIdAsync(package.Id);
        Assert.NotNull(storedDelivered);
        Assert.Equal(PackageStatus.Delivered, storedDelivered.Status);
        Assert.NotNull(storedDelivered.PickupCode);
        Assert.True(Context.NotificationService.HasNotificationContaining("Pickup code:"));
    }
}
