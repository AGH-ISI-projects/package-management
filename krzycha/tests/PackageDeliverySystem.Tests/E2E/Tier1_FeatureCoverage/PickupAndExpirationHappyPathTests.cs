using PackageDeliverySystem.Core;
using PackageDeliverySystem.Tests.E2E.Infrastructure;
using Xunit;

namespace PackageDeliverySystem.Tests.E2E.Tier1_FeatureCoverage;

/// <summary>
/// Tier 1 Feature Coverage: Pickup & Expiration Happy Path Tests (Features 1, 8, 10, 11, 12).
/// </summary>
public class PickupAndExpirationHappyPathTests : E2ETestBase
{
    [Fact]
    public async Task TC_T1_PIC_01_RecipientSuccessfulCollectionWithValidCode_Success()
    {
        // Arrange
        var package = await CreateDeliveredPackageAsync();
        string pickupCode = package.PickupCode!;

        // Act
        await Harness.FulfillmentService.CollectPackageAsync(package.Id, package.RecipientId, pickupCode);

        // Assert
        var stored = await Context.PackageRepository.GetByIdAsync(package.Id);
        Assert.NotNull(stored);
        Assert.Equal(PackageStatus.Collected, stored.Status);
        Assert.NotNull(stored.CollectedAt);
        Assert.True(Context.NotificationService.HasNotificationContaining("has been collected"));
    }

    [Fact]
    public async Task TC_T1_PIC_02_DirectDomainCollectCall_Success()
    {
        // Arrange
        var package = await CreateDeliveredPackageAsync();
        string pickupCode = package.PickupCode!;

        // Act
        package.Collect(pickupCode);
        await Context.PackageRepository.UpdateAsync(package);

        // Assert
        var stored = await Context.PackageRepository.GetByIdAsync(package.Id);
        Assert.NotNull(stored);
        Assert.Equal(PackageStatus.Collected, stored.Status);
        Assert.NotNull(stored.CollectedAt);
        Assert.True(stored.CollectedAt <= DateTime.UtcNow);
    }

    [Fact]
    public async Task TC_T1_PIC_03_TransitInactivityExpiration_Success()
    {
        // Arrange
        var package = await CreateShippedPackageAsync();
        SetPackageTimestamp(package, nameof(Package.ExpiredAt), DateTime.UtcNow.AddMinutes(-5));

        // Act
        package.Expire();
        await Context.PackageRepository.UpdateAsync(package);

        // Assert
        var stored = await Context.PackageRepository.GetByIdAsync(package.Id);
        Assert.NotNull(stored);
        Assert.Equal(PackageStatus.Expired, stored.Status);
    }

    [Fact]
    public async Task TC_T1_PIC_04_PostDelivery48hUncollectedExpiration_Success()
    {
        // Arrange
        var package = await CreateDeliveredPackageAsync();
        SetPackageTimestamp(package, nameof(Package.ExpiredAt), DateTime.UtcNow.AddHours(-1));

        // Act
        package.Expire();
        await Context.PackageRepository.UpdateAsync(package);

        // Assert
        var stored = await Context.PackageRepository.GetByIdAsync(package.Id);
        Assert.NotNull(stored);
        Assert.Equal(PackageStatus.Expired, stored.Status);
    }

    [Fact]
    public async Task TC_T1_PIC_05_BatchExpirationProcessingJob_Success()
    {
        // Arrange
        var pkg1 = await CreateDeliveredPackageAsync();
        SetPackageTimestamp(pkg1, nameof(Package.ExpiredAt), DateTime.UtcNow.AddHours(-2));
        await Context.PackageRepository.UpdateAsync(pkg1);

        var pkg2 = await CreateShippedPackageAsync();
        SetPackageTimestamp(pkg2, nameof(Package.ExpiredAt), DateTime.UtcNow.AddMinutes(-10));
        await Context.PackageRepository.UpdateAsync(pkg2);

        // Act
        await Harness.AcceptanceService.ProcessExpiredPackagesAsync();

        // Assert
        var stored1 = await Context.PackageRepository.GetByIdAsync(pkg1.Id);
        var stored2 = await Context.PackageRepository.GetByIdAsync(pkg2.Id);
        Assert.Equal(PackageStatus.Expired, stored1?.Status);
        Assert.Equal(PackageStatus.Expired, stored2?.Status);
        Assert.True(Context.NotificationService.HasNotificationContaining("marked as EXPIRED"));
    }
}
