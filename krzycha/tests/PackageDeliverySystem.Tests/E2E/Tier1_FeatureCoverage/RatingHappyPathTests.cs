using PackageDeliverySystem.Core;
using PackageDeliverySystem.Tests.E2E.Infrastructure;
using Xunit;

namespace PackageDeliverySystem.Tests.E2E.Tier1_FeatureCoverage;

/// <summary>
/// Tier 1 Feature Coverage: Rating Happy Path Tests (Feature 19).
/// </summary>
public class RatingHappyPathTests : E2ETestBase
{
    [Fact]
    public async Task TC_T1_RAT_01_RecipientRatesCourierWithMinimumScore_Success()
    {
        // Arrange
        int courierId = 5;
        var package = await CreateCollectedPackageAsync(courierId: courierId);

        // Act
        await Harness.RatingService.RateCourierAsync(package.Id, package.RecipientId, 1);

        // Assert
        var courier = await Context.PeopleService.GetCourierAsync(courierId);
        Assert.NotNull(courier);
        Assert.Equal(1, courier.Rank);
    }

    [Fact]
    public async Task TC_T1_RAT_02_RecipientRatesCourierWithMaximumScore_Success()
    {
        // Arrange
        int courierId = 5;
        var package = await CreateCollectedPackageAsync(courierId: courierId);

        // Act
        await Harness.RatingService.RateCourierAsync(package.Id, package.RecipientId, 5);

        // Assert
        var courier = await Context.PeopleService.GetCourierAsync(courierId);
        Assert.NotNull(courier);
        Assert.Equal(5, courier.Rank);
    }

    [Fact]
    public async Task TC_T1_RAT_03_RecipientRatesCourierWithMidRangeScore_Success()
    {
        // Arrange
        int courierId = 5;
        var package = await CreateCollectedPackageAsync(courierId: courierId);

        // Act
        await Harness.RatingService.RateCourierAsync(package.Id, package.RecipientId, 4);

        // Assert
        var courier = await Context.PeopleService.GetCourierAsync(courierId);
        Assert.NotNull(courier);
        Assert.Equal(4, courier.Rank);
    }

    [Fact]
    public async Task TC_T1_RAT_04_RecipientRatesSenderOfCollectedPackage_Success()
    {
        // Arrange
        int senderId = 1;
        var package = await CreateCollectedPackageAsync(senderId: senderId);

        // Act
        await Harness.RatingService.RateSenderAsync(package.Id, package.RecipientId, 5);

        // Assert
        var sender = await Context.PeopleService.GetUserAsync(senderId);
        Assert.NotNull(sender);
        Assert.Equal(5, sender.Rank);
    }

    [Fact]
    public async Task TC_T1_RAT_05_RatingOverwritesPersonRankDirectly_Success()
    {
        // Arrange
        int courierId = 5;
        var package = await CreateCollectedPackageAsync(courierId: courierId);

        // Act 1: Initial rating 3
        await Harness.RatingService.RateCourierAsync(package.Id, package.RecipientId, 3);
        var courierMid = await Context.PeopleService.GetCourierAsync(courierId);
        Assert.Equal(3, courierMid?.Rank);

        // Act 2: Updated rating 5
        await Harness.RatingService.RateCourierAsync(package.Id, package.RecipientId, 5);

        // Assert
        var courierFinal = await Context.PeopleService.GetCourierAsync(courierId);
        Assert.NotNull(courierFinal);
        Assert.Equal(5, courierFinal.Rank);
    }
}
