using PackageDeliverySystem.Core;
using PackageDeliverySystem.Tests.E2E.Infrastructure;
using Xunit;

namespace PackageDeliverySystem.Tests.E2E.Tier2_BoundaryAndCornerCases;

/// <summary>
/// Tier 2 Boundary & Corner Cases: Rating (Score bounds, premature rating, unauthorized rater, missing entities).
/// </summary>
public class RatingBoundaryTests : E2ETestBase
{
    [Fact]
    public async Task TC_T2_RAT_01_RatingScoreBelowLowerBound_Throws()
    {
        // Arrange
        var package = await CreateCollectedPackageAsync();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Harness.RatingService.RateCourierAsync(package.Id, package.RecipientId, 0));

        Assert.Contains("Score must be between 1 and 5", ex.Message);
    }

    [Fact]
    public async Task TC_T2_RAT_02_RatingScoreAboveUpperBound_Throws()
    {
        // Arrange
        var package = await CreateCollectedPackageAsync();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Harness.RatingService.RateCourierAsync(package.Id, package.RecipientId, 6));

        Assert.Contains("Score must be between 1 and 5", ex.Message);
    }

    [Fact]
    public async Task TC_T2_RAT_03_RatingUncollectedPackage_Throws()
    {
        // Arrange
        var package = await CreateDeliveredPackageAsync();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Harness.RatingService.RateCourierAsync(package.Id, package.RecipientId, 5));

        Assert.Equal("Can only rate a delivery after it has been collected", ex.Message);
    }

    [Fact]
    public async Task TC_T2_RAT_04_UnauthorizedUserRatingAttempt_Throws()
    {
        // Arrange
        var package = await CreateCollectedPackageAsync(recipientId: 2);
        int unauthorizedUserId = 999;

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Harness.RatingService.RateCourierAsync(package.Id, unauthorizedUserId, 5));

        Assert.Equal("Only this package's recipient can rate it", ex.Message);
    }

    [Fact]
    public async Task TC_T2_RAT_05_RatingPackageWithMissingCourier_Throws()
    {
        // Arrange: Create collected package without an assigned courier
        var package = await CreateOrderedPackageAsync();
        package.Deliver();
        package.Collect(package.PickupCode!);
        await Context.PackageRepository.UpdateAsync(package);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Harness.RatingService.RateCourierAsync(package.Id, package.RecipientId, 5));

        Assert.Equal("This package has no courier to rate", ex.Message);
    }

    [Fact]
    public async Task TC_T2_RAT_06_RatingNonExistentPackage_Throws()
    {
        // Arrange
        int nonExistentPackageId = 99999;

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => Harness.RatingService.RateCourierAsync(nonExistentPackageId, 2, 5));

        Assert.Contains("Package not found", ex.Message);
    }
}
