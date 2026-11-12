using PackageDeliverySystem.Core;
using PackageDeliverySystem.Tests.E2E.Infrastructure;
using Xunit;

namespace PackageDeliverySystem.Tests.E2E.Tier1_FeatureCoverage;

/// <summary>
/// Tier 1 Feature Coverage: Return Request & Partial Returns Happy Path Tests (Features 21, 22, 23, 25, 26, 29, 30, 34, 35).
/// </summary>
public class ReturnRequestHappyPathTests : E2ETestBase
{
    [Fact]
    public async Task TC_T1_RET_01_ValidReturnRequestForSingleCollectedPackage_Success()
    {
        // Arrange
        var package = await CreateCollectedPackageAsync();
        string reason = "Item defective";

        // Act
        var returnEntity = await Harness.ReturnService.RequestReturnAsync(package.Id, package.RecipientId, reason);

        // Assert
        Assert.NotNull(returnEntity);
        Assert.Equal(ReturnStatus.Requested, returnEntity.Status);
        Assert.Equal(reason, returnEntity.Reason);
        Assert.Equal(package.Id, returnEntity.PackageId);

        var stored = await Context.PackageRepository.GetByIdAsync(package.Id);
        Assert.NotNull(stored);
        Assert.Equal(PackageStatus.ReturnRequested, stored.Status);

        var storedReturn = await Context.ReturnRepository.GetByIdAsync(returnEntity.Id);
        Assert.NotNull(storedReturn);
        Assert.Equal(ReturnStatus.Requested, storedReturn.Status);
    }

    [Fact]
    public async Task TC_T1_RET_02_PartialReturnOf1ItemIn3ItemOrderGroup_Success()
    {
        // Arrange
        int orderGroupId = 555;
        int customerId = 2;

        var pkg1 = await CreateCollectedPackageAsync(recipientId: customerId);
        pkg1.OrderGroupId = orderGroupId;
        await Context.PackageRepository.UpdateAsync(pkg1);

        var pkg2 = await CreateCollectedPackageAsync(recipientId: customerId);
        pkg2.OrderGroupId = orderGroupId;
        await Context.PackageRepository.UpdateAsync(pkg2);

        var pkg3 = await CreateCollectedPackageAsync(recipientId: customerId);
        pkg3.OrderGroupId = orderGroupId;
        await Context.PackageRepository.UpdateAsync(pkg3);

        // Act: Return only pkg1
        var returnEntity = await Harness.ReturnService.RequestReturnAsync(pkg1.Id, customerId, "Return item 1");

        // Assert
        var stored1 = await Context.PackageRepository.GetByIdAsync(pkg1.Id);
        var stored2 = await Context.PackageRepository.GetByIdAsync(pkg2.Id);
        var stored3 = await Context.PackageRepository.GetByIdAsync(pkg3.Id);

        Assert.Equal(PackageStatus.ReturnRequested, stored1?.Status);
        Assert.Equal(PackageStatus.Collected, stored2?.Status);
        Assert.Equal(PackageStatus.Collected, stored3?.Status);
    }

    [Fact]
    public async Task TC_T1_RET_03_QueryReturnablePackagesForMultiItemOrder_FiltersReturnedItems()
    {
        // Arrange
        int orderGroupId = 556;
        int customerId = 2;

        var pkg1 = await CreateCollectedPackageAsync(recipientId: customerId);
        pkg1.OrderGroupId = orderGroupId;
        await Context.PackageRepository.UpdateAsync(pkg1);

        var pkg2 = await CreateCollectedPackageAsync(recipientId: customerId);
        pkg2.OrderGroupId = orderGroupId;
        await Context.PackageRepository.UpdateAsync(pkg2);

        var pkg3 = await CreateCollectedPackageAsync(recipientId: customerId);
        pkg3.OrderGroupId = orderGroupId;
        await Context.PackageRepository.UpdateAsync(pkg3);

        await Harness.ReturnService.RequestReturnAsync(pkg1.Id, customerId, "Returning item 1");

        // Act
        var returnable = (await Harness.ReturnService.GetReturnablePackagesAsync(orderGroupId, customerId)).ToList();

        // Assert
        Assert.Equal(2, returnable.Count);
        Assert.Contains(returnable, p => p.Id == pkg2.Id);
        Assert.Contains(returnable, p => p.Id == pkg3.Id);
        Assert.DoesNotContain(returnable, p => p.Id == pkg1.Id);
    }

    [Fact]
    public async Task TC_T1_RET_04_SequentialPartialReturnsOnSameOrder_Success()
    {
        // Arrange
        int orderGroupId = 557;
        int customerId = 2;

        var pkg1 = await CreateCollectedPackageAsync(recipientId: customerId);
        pkg1.OrderGroupId = orderGroupId;
        await Context.PackageRepository.UpdateAsync(pkg1);

        var pkg2 = await CreateCollectedPackageAsync(recipientId: customerId);
        pkg2.OrderGroupId = orderGroupId;
        await Context.PackageRepository.UpdateAsync(pkg2);

        var pkg3 = await CreateCollectedPackageAsync(recipientId: customerId);
        pkg3.OrderGroupId = orderGroupId;
        await Context.PackageRepository.UpdateAsync(pkg3);

        // Act 1: First return
        await Harness.ReturnService.RequestReturnAsync(pkg1.Id, customerId, "Returning item 1");

        // Act 2: Second return
        var ret2 = await Harness.ReturnService.RequestReturnAsync(pkg2.Id, customerId, "Returning item 2");

        // Assert
        var stored1 = await Context.PackageRepository.GetByIdAsync(pkg1.Id);
        var stored2 = await Context.PackageRepository.GetByIdAsync(pkg2.Id);
        var stored3 = await Context.PackageRepository.GetByIdAsync(pkg3.Id);

        Assert.Equal(PackageStatus.ReturnRequested, stored1?.Status);
        Assert.Equal(PackageStatus.ReturnRequested, stored2?.Status);
        Assert.Equal(PackageStatus.Collected, stored3?.Status);
        Assert.Equal(ReturnStatus.Requested, ret2.Status);
    }

    [Fact]
    public async Task TC_T1_RET_05_FullReturnOfAllPackagesInOrderGroup_Success()
    {
        // Arrange
        int orderGroupId = 558;
        int customerId = 2;

        var pkg1 = await CreateCollectedPackageAsync(recipientId: customerId);
        pkg1.OrderGroupId = orderGroupId;
        await Context.PackageRepository.UpdateAsync(pkg1);

        var pkg2 = await CreateCollectedPackageAsync(recipientId: customerId);
        pkg2.OrderGroupId = orderGroupId;
        await Context.PackageRepository.UpdateAsync(pkg2);

        // Act: Return all packages
        await Harness.ReturnService.RequestReturnAsync(pkg1.Id, customerId, "Returning 1");
        await Harness.ReturnService.RequestReturnAsync(pkg2.Id, customerId, "Returning 2");

        // Assert
        var returnable = await Harness.ReturnService.GetReturnablePackagesAsync(orderGroupId, customerId);
        Assert.Empty(returnable);

        var stored1 = await Context.PackageRepository.GetByIdAsync(pkg1.Id);
        var stored2 = await Context.PackageRepository.GetByIdAsync(pkg2.Id);
        Assert.Equal(PackageStatus.ReturnRequested, stored1?.Status);
        Assert.Equal(PackageStatus.ReturnRequested, stored2?.Status);
    }
}
