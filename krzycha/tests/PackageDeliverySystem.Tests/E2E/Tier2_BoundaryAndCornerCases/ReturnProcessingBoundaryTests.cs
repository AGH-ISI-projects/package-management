using PackageDeliverySystem.Core;
using PackageDeliverySystem.Tests.E2E.Infrastructure;
using Xunit;

namespace PackageDeliverySystem.Tests.E2E.Tier2_BoundaryAndCornerCases;

/// <summary>
/// Tier 2 Boundary & Corner Cases: Return Processing & Refund (Workflow sequencing, invalid personnel, refund invariants).
/// </summary>
public class ReturnProcessingBoundaryTests : E2ETestBase
{
    [Fact]
    public async Task TC_T2_REP_01_OperatorApprovalOnNonRequestedReturn_Throws()
    {
        // Arrange
        var package = await CreateCollectedPackageAsync();
        var ret = await Harness.ReturnService.RequestReturnAsync(package.Id, package.RecipientId, "Reason");
        await Harness.ReturnService.ApproveReturnAsync(ret.Id, 7, 301, 302);
        Assert.Equal(ReturnStatus.Approved, ret.Status);

        // Act & Assert: Attempting to approve again
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Harness.ReturnService.ApproveReturnAsync(ret.Id, 7, 301, 302));

        Assert.Equal("Cannot approve return in status Approved", ex.Message);
    }

    [Fact]
    public async Task TC_T2_REP_02_OperatorRejectionOnShippedReturn_Throws()
    {
        // Arrange
        var package = await CreateCollectedPackageAsync();
        var ret = await Harness.ReturnService.RequestReturnAsync(package.Id, package.RecipientId, "Reason");
        await Harness.ReturnService.ApproveReturnAsync(ret.Id, 7, 301, 302);
        await Harness.ReturnService.ShipReturnAsync(ret.Id, 5);
        Assert.Equal(ReturnStatus.Shipped, ret.Status);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Harness.ReturnService.RejectReturnAsync(ret.Id, 7));

        Assert.Equal("Cannot reject return in status Shipped", ex.Message);
    }

    [Fact]
    public async Task TC_T2_REP_03_CourierShippingOnUnapprovedReturn_Throws()
    {
        // Arrange: Return is in Requested status, not yet Approved
        var package = await CreateCollectedPackageAsync();
        var ret = await Harness.ReturnService.RequestReturnAsync(package.Id, package.RecipientId, "Reason");
        Assert.Equal(ReturnStatus.Requested, ret.Status);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Harness.ReturnService.ShipReturnAsync(ret.Id, 5));

        Assert.Equal("Cannot ship return in status Requested", ex.Message);
    }

    [Fact]
    public async Task TC_T2_REP_04_DirectRefundOnIncompleteOrPendingPayment_Throws()
    {
        // Arrange
        var payment = new Payment { Amount = 30.00m, PayerEmail = "pending@test.com" };
        Assert.Equal(PaymentStatus.Pending, payment.Status);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => payment.Refund());
        Assert.Equal("Cannot refund payment in status Pending", ex.Message);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
    }

    [Fact]
    public async Task TC_T2_REP_05_OperatorAndCourierActionWithInvalidPersonnelId_Throws()
    {
        // Arrange
        var package = await CreateCollectedPackageAsync();
        var ret = await Harness.ReturnService.RequestReturnAsync(package.Id, package.RecipientId, "Reason");
        int nonExistentOperatorId = 9999;
        int nonExistentCourierId = 9999;

        // Act & Assert Part A (Invalid Operator)
        var exOp = await Assert.ThrowsAsync<ArgumentException>(
            () => Harness.ReturnService.ApproveReturnAsync(ret.Id, nonExistentOperatorId, 301, 302));
        Assert.Contains("Operator not found", exOp.Message);

        // Act & Assert Part B (Invalid Courier after valid approval)
        await Harness.ReturnService.ApproveReturnAsync(ret.Id, 7, 301, 302);
        var exCour = await Assert.ThrowsAsync<ArgumentException>(
            () => Harness.ReturnService.ShipReturnAsync(ret.Id, nonExistentCourierId));
        Assert.Contains("Courier not found", exCour.Message);
    }

    [Fact]
    public async Task TC_T2_REP_06_CancellationOfShippedReturn_Throws()
    {
        // Arrange
        var package = await CreateCollectedPackageAsync();
        var ret = await Harness.ReturnService.RequestReturnAsync(package.Id, package.RecipientId, "Reason");
        await Harness.ReturnService.ApproveReturnAsync(ret.Id, 7, 301, 302);
        await Harness.ReturnService.ShipReturnAsync(ret.Id, 5);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => ret.Cancel());
        Assert.Equal("Cannot cancel return in status Shipped", ex.Message);
    }

    [Fact]
    public async Task TC_T2_REP_07_DoubleCompletionOnAlreadyCompletedReturn_Throws()
    {
        // Arrange
        var package = await CreateCollectedPackageAsync();
        var ret = await Harness.ReturnService.RequestReturnAsync(package.Id, package.RecipientId, "Reason");
        await Harness.ReturnService.ApproveReturnAsync(ret.Id, 7, 301, 302);
        await Harness.ReturnService.ShipReturnAsync(ret.Id, 5);
        await Harness.ReturnService.CompleteReturnAsync(ret.Id);
        Assert.Equal(ReturnStatus.Completed, ret.Status);

        // Act & Assert: Completing already completed return
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Harness.ReturnService.CompleteReturnAsync(ret.Id));

        Assert.Contains("Cannot complete return in status Completed", ex.Message);
    }
}
