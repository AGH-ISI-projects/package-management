using PackageDeliverySystem.Core;
using PackageDeliverySystem.Tests.E2E.Infrastructure;
using Xunit;

namespace PackageDeliverySystem.Tests.E2E.Tier1_FeatureCoverage;

/// <summary>
/// Tier 1 Feature Coverage: Return Processing & Refund Happy Path Tests (Features 24, 27, 28, 31, 32, 33, 36, 37).
/// </summary>
public class ReturnProcessingHappyPathTests : E2ETestBase
{
    [Fact]
    public async Task TC_T1_REP_01_OperatorApprovesReturnRequest_Success()
    {
        // Arrange
        var package = await CreateCollectedPackageAsync();
        var ret = await Harness.ReturnService.RequestReturnAsync(package.Id, package.RecipientId, "Damaged");
        int operatorId = 7; // AdminAlice

        // Act
        await Harness.ReturnService.ApproveReturnAsync(ret.Id, operatorId, returnBoxId: 301, returnDestBoxId: 302);

        // Assert
        var storedReturn = await Context.ReturnRepository.GetByIdAsync(ret.Id);
        var storedPackage = await Context.PackageRepository.GetByIdAsync(package.Id);

        Assert.NotNull(storedReturn);
        Assert.Equal(ReturnStatus.Approved, storedReturn.Status);
        Assert.Equal(301, storedReturn.ReturnBoxId);
        Assert.Equal(302, storedReturn.ReturnDestinationBoxId);
        Assert.NotNull(storedReturn.ApprovedAt);

        Assert.NotNull(storedPackage);
        Assert.Equal(PackageStatus.ReturnInProgress, storedPackage.Status);
    }

    [Fact]
    public async Task TC_T1_REP_02_OperatorRejectsReturnRequest_RevertsPackageToCollected()
    {
        // Arrange
        var package = await CreateCollectedPackageAsync();
        var ret = await Harness.ReturnService.RequestReturnAsync(package.Id, package.RecipientId, "No defect");
        int operatorId = 7;

        // Act
        await Harness.ReturnService.RejectReturnAsync(ret.Id, operatorId);

        // Assert
        var storedReturn = await Context.ReturnRepository.GetByIdAsync(ret.Id);
        var storedPackage = await Context.PackageRepository.GetByIdAsync(package.Id);

        Assert.NotNull(storedReturn);
        Assert.Equal(ReturnStatus.Rejected, storedReturn.Status);

        Assert.NotNull(storedPackage);
        Assert.Equal(PackageStatus.Collected, storedPackage.Status);
    }

    [Fact]
    public async Task TC_T1_REP_03_CourierShipsReturnFromDropOffBox_Success()
    {
        // Arrange
        var package = await CreateCollectedPackageAsync();
        var ret = await Harness.ReturnService.RequestReturnAsync(package.Id, package.RecipientId, "Return requested");
        await Harness.ReturnService.ApproveReturnAsync(ret.Id, 7, 301, 302);
        int courierId = 5;

        // Act
        await Harness.ReturnService.ShipReturnAsync(ret.Id, courierId);

        // Assert
        var storedReturn = await Context.ReturnRepository.GetByIdAsync(ret.Id);
        var storedPackage = await Context.PackageRepository.GetByIdAsync(package.Id);

        Assert.NotNull(storedReturn);
        Assert.Equal(ReturnStatus.Shipped, storedReturn.Status);

        Assert.NotNull(storedPackage);
        Assert.Equal(PackageStatus.ReturnInProgress, storedPackage.Status);
    }

    [Fact]
    public async Task TC_T1_REP_04_ReturnCompletionTriggersPaymentRefund_Success()
    {
        // Arrange
        var package = await CreateCollectedPackageAsync();
        var ret = await Harness.ReturnService.RequestReturnAsync(package.Id, package.RecipientId, "Refund needed");
        await Harness.ReturnService.ApproveReturnAsync(ret.Id, 7, 301, 302);
        await Harness.ReturnService.ShipReturnAsync(ret.Id, 5);

        // Act
        await Harness.ReturnService.CompleteReturnAsync(ret.Id);

        // Assert
        var storedReturn = await Context.ReturnRepository.GetByIdAsync(ret.Id);
        var storedPackage = await Context.PackageRepository.GetByIdAsync(package.Id);

        Assert.NotNull(storedReturn);
        Assert.Equal(ReturnStatus.Completed, storedReturn.Status);
        Assert.NotNull(storedReturn.CompletedAt);

        Assert.NotNull(storedPackage);
        Assert.Equal(PackageStatus.Returned, storedPackage.Status);
        Assert.NotNull(storedPackage.Payment);
        Assert.Equal(PaymentStatus.Refunded, storedPackage.Payment.Status);
    }

    [Fact]
    public async Task TC_T1_REP_05_DirectPaymentDomainRefund_Success()
    {
        // Arrange
        var payment = new Payment { Amount = 50.00m, PayerEmail = "user@test.com" };
        payment.Complete();
        Assert.Equal(PaymentStatus.Completed, payment.Status);

        // Act
        payment.Refund();

        // Assert
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
    }
}
