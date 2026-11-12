using PackageDeliverySystem.Core;
using PackageDeliverySystem.Tests.E2E.Infrastructure;
using Xunit;

namespace PackageDeliverySystem.Tests.E2E.Tier2_BoundaryAndCornerCases;

/// <summary>
/// Tier 2 Boundary & Corner Cases: Pickup & Expiration (Security guards, premature expiry, bad codes, replays).
/// </summary>
public class PickupAndExpirationBoundaryTests : E2ETestBase
{
    [Fact]
    public async Task TC_T2_PIC_01_CollectionWithInvalidMismatchedPickupCode_Throws()
    {
        // Arrange
        var package = await CreateDeliveredPackageAsync();
        string wrongCode = "000000";

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => package.Collect(wrongCode));
        Assert.Equal("Invalid pickup code", ex.Message);
        Assert.Equal(PackageStatus.Delivered, package.Status);
        Assert.Null(package.CollectedAt);
    }

    [Fact]
    public async Task TC_T2_PIC_02_UnauthorizedCollectionAttemptByNonRecipient_Throws()
    {
        // Arrange
        var package = await CreateDeliveredPackageAsync(recipientId: 2);
        int unauthorizedUserId = 999;
        string validCode = package.PickupCode!;

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Harness.FulfillmentService.CollectPackageAsync(package.Id, unauthorizedUserId, validCode));

        Assert.Equal("Only this package's recipient can collect it", ex.Message);
        var stored = await Context.PackageRepository.GetByIdAsync(package.Id);
        Assert.Equal(PackageStatus.Delivered, stored?.Status);
    }

    [Fact]
    public async Task TC_T2_PIC_03_PrematureExpirationOnFreshlyDeliveredPackage_Throws()
    {
        // Arrange
        var package = await CreateDeliveredPackageAsync();
        SetPackageTimestamp(package, nameof(Package.ExpiredAt), DateTime.UtcNow.AddHours(50));

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => package.Expire());
        Assert.Equal("Cannot expire package in status Delivered", ex.Message);
        Assert.Equal(PackageStatus.Delivered, package.Status);
    }

    [Fact]
    public async Task TC_T2_PIC_04_CollectionAttemptOnNonDeliveredPackage_Throws()
    {
        // Arrange
        var package = await CreateShippedPackageAsync();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => package.Collect("123456"));
        Assert.Equal("Cannot collect package in status Shipped", ex.Message);
        Assert.Equal(PackageStatus.Shipped, package.Status);
    }

    [Fact]
    public async Task TC_T2_PIC_05_DoubleCollectionReplayAttempt_Throws()
    {
        // Arrange
        var package = await CreateCollectedPackageAsync();
        string validCode = package.PickupCode!;

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => package.Collect(validCode));
        Assert.Equal("Cannot collect package in status Collected", ex.Message);
    }

    [Fact]
    public async Task TC_T2_PIC_06_CollectionWithEmptyOrNullPickupCode_Throws()
    {
        // Arrange
        var package = await CreateDeliveredPackageAsync();

        // Act & Assert Empty
        var exEmpty = Assert.Throws<InvalidOperationException>(() => package.Collect(string.Empty));
        Assert.Equal("Invalid pickup code", exEmpty.Message);

        // Act & Assert Null
        var exNull = Assert.Throws<InvalidOperationException>(() => package.Collect(null!));
        Assert.Equal("Invalid pickup code", exNull.Message);
    }
}
