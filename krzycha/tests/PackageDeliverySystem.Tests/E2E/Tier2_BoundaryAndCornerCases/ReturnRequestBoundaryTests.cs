using PackageDeliverySystem.Core;
using PackageDeliverySystem.Tests.E2E.Infrastructure;
using Xunit;

namespace PackageDeliverySystem.Tests.E2E.Tier2_BoundaryAndCornerCases;

/// <summary>
/// Tier 2 Boundary & Corner Cases: Return Request & Partial Returns (State guards, duplicate requests, auth).
/// </summary>
public class ReturnRequestBoundaryTests : E2ETestBase
{
    [Fact]
    public async Task TC_T2_RET_01_ReturnRequestBlockedPriorToCollection_Throws()
    {
        // Arrange
        var package = await CreateDeliveredPackageAsync();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Harness.ReturnService.RequestReturnAsync(package.Id, package.RecipientId, "Broken"));

        Assert.Contains("Only collected packages can be returned", ex.Message);
    }

    [Fact]
    public async Task TC_T2_RET_02_DuplicateReturnRequestOnActiveReturn_Throws()
    {
        // Arrange
        var package = await CreateCollectedPackageAsync();
        await Harness.ReturnService.RequestReturnAsync(package.Id, package.RecipientId, "First request");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Harness.ReturnService.RequestReturnAsync(package.Id, package.RecipientId, "Second request"));

        Assert.Equal("A return request already exists for this package", ex.Message);
    }

    [Fact]
    public async Task TC_T2_RET_03_UnauthorizedReturnRequestByNonRecipient_Throws()
    {
        // Arrange
        var package = await CreateCollectedPackageAsync(recipientId: 2);
        int unauthorizedCustomerId = 999;

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Harness.ReturnService.RequestReturnAsync(package.Id, unauthorizedCustomerId, "Item defective"));

        Assert.Equal("Only the recipient can request a return", ex.Message);
    }

    [Fact]
    public async Task TC_T2_RET_04_ReturnRequestForNonExistentPackage_Throws()
    {
        // Arrange
        int nonExistentPackageId = 88888;

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => Harness.ReturnService.RequestReturnAsync(nonExistentPackageId, 2, "Reason"));

        Assert.Contains("Package not found", ex.Message);
    }

    [Fact]
    public async Task TC_T2_RET_05_ReturnRequestOnCancelledPackage_Throws()
    {
        // Arrange
        var package = await CreateOrderedPackageAsync();
        package.Cancel();
        await Context.PackageRepository.UpdateAsync(package);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Harness.ReturnService.RequestReturnAsync(package.Id, package.RecipientId, "Return cancelled"));

        Assert.Contains("Only collected packages can be returned", ex.Message);
    }

    [Fact]
    public async Task TC_T2_RET_06_DomainMethodRequestReturnOnNonCollectedPackage_Throws()
    {
        // Arrange
        var package = await CreateOrderedPackageAsync();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => package.RequestReturn());
        Assert.Contains("Only collected packages can be returned", ex.Message);
    }
}
