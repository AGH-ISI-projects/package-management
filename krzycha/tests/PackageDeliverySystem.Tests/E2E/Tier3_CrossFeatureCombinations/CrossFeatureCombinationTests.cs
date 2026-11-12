using PackageDeliverySystem.Core;
using PackageDeliverySystem.Tests.E2E.Infrastructure;
using Xunit;

namespace PackageDeliverySystem.Tests.E2E.Tier3_CrossFeatureCombinations;

/// <summary>
/// Tier 3 Cross-Feature Combinations: Pairwise and sequential multi-feature interactions.
/// </summary>
public class CrossFeatureCombinationTests : E2ETestBase
{
    [Fact]
    public async Task E2E_T3_01_OrderIntake_PaymentFailureThenCancellation_PermanentlyPreventsDeposit()
    {
        // Arrange
        var package = await CreateOrderedPackageAsync();
        Context.PaymentGateway.DefaultStatus = PaymentStatus.Failed;

        // Act 1: Payment fails
        var refId = await Harness.IntakeService.RequestPaymentAsync(package.Id, "alice@test.com", 29.99m);
        await Harness.IntakeService.ConfirmPaymentAsync(package.Id);

        var midPackage = await Context.PackageRepository.GetByIdAsync(package.Id);
        Assert.NotNull(midPackage?.Payment);
        Assert.Equal(PaymentStatus.Failed, midPackage.Payment.Status);
        Assert.Equal(PackageStatus.Ordered, midPackage.Status);

        // Act 2: Customer cancels
        midPackage.Cancel();
        await Context.PackageRepository.UpdateAsync(midPackage);

        // Assert 1: Status is Cancelled
        var cancelledPkg = await Context.PackageRepository.GetByIdAsync(package.Id);
        Assert.NotNull(cancelledPkg);
        Assert.Equal(PackageStatus.Cancelled, cancelledPkg.Status);

        // Act 3: Subsequent attempts to confirm deposit fail
        var ex = Assert.Throws<InvalidOperationException>(() => cancelledPkg.ConfirmDeposit());
        Assert.Contains("Cannot confirm deposit for package in status Cancelled", ex.Message);
    }

    [Fact]
    public async Task E2E_T3_02_OrderIntake_ConfirmedDepositThenOperatorRejection_BlocksAcceptanceAndShipping()
    {
        // Arrange
        var package = await CreateOrderedPackageAsync();
        await Harness.IntakeService.RequestPaymentAsync(package.Id, "alice@test.com", 15.00m);
        await Harness.IntakeService.ConfirmPaymentAsync(package.Id);
        await Harness.IntakeService.ConfirmDepositAsync(package.Id, 5); // Jan confirms deposit

        var depositedPkg = await Context.PackageRepository.GetByIdAsync(package.Id);
        Assert.NotNull(depositedPkg?.DepositConfirmedAt);
        Assert.Equal(PackageStatus.Ordered, depositedPkg.Status);

        // Act: Operator inspects and rejects (e.g. damaged wrapping)
        int operatorId = 7; // AdminAlice
        await Harness.AcceptanceService.RejectPackageAsync(package.Id, operatorId);

        // Assert
        var rejectedPkg = await Context.PackageRepository.GetByIdAsync(package.Id);
        Assert.NotNull(rejectedPkg);
        Assert.Equal(PackageStatus.Rejected, rejectedPkg.Status);

        // Subsequent acceptance attempt fails
        var exAccept = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Harness.AcceptanceService.AcceptPackageAsync(package.Id, operatorId, package.BoxId!.Value));
        Assert.Equal("Package is not in an orderable state", exAccept.Message);

        // Direct domain guard check
        var exDomain = Assert.Throws<InvalidOperationException>(() => rejectedPkg.Accept());
        Assert.Equal("Cannot accept package in status Rejected", exDomain.Message);

        Assert.True(Context.NotificationService.HasNotificationContaining("rejected by operator AdminAlice"));
    }

    [Fact]
    public async Task E2E_T3_03_FullDeliveryFollowedByInvalidReturnAttempts_GuardsEnforced()
    {
        // Arrange
        var package = await CreateDeliveredPackageAsync(recipientId: 42);
        string pickupCode = package.PickupCode!;

        // Sub-test A: Pre-Collection return attempt throws
        var exPreCollect = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Harness.ReturnService.RequestReturnAsync(package.Id, 42, "Item defective"));
        Assert.Contains("Only collected packages can be returned", exPreCollect.Message);

        // Recipient collects package
        await Harness.FulfillmentService.CollectPackageAsync(package.Id, 42, pickupCode);
        var collectedPkg = await Context.PackageRepository.GetByIdAsync(package.Id);
        Assert.Equal(PackageStatus.Collected, collectedPkg?.Status);

        // Sub-test B: Unauthorized user attempts return
        int attackerId = 999;
        var exUnauthorized = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Harness.ReturnService.RequestReturnAsync(package.Id, attackerId, "Fraudulent return"));
        Assert.Equal("Only the recipient can request a return", exUnauthorized.Message);

        // Sub-test C: Valid return succeeds, subsequent duplicate return attempt throws
        var ret = await Harness.ReturnService.RequestReturnAsync(package.Id, 42, "Item defective");
        Assert.Equal(ReturnStatus.Requested, ret.Status);

        var exDuplicate = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Harness.ReturnService.RequestReturnAsync(package.Id, 42, "Duplicate return"));
        Assert.Equal("A return request already exists for this package", exDuplicate.Message);
    }

    [Fact]
    public async Task E2E_T3_04_FullDeliveryFollowedByValidReturnThenOperatorRejection_RevertsToCollected()
    {
        // Arrange
        var package = await CreateCollectedPackageAsync(recipientId: 50);

        // Act 1: Recipient requests return
        var ret = await Harness.ReturnService.RequestReturnAsync(package.Id, 50, "Changed mind");
        var requestedPkg = await Context.PackageRepository.GetByIdAsync(package.Id);
        Assert.Equal(PackageStatus.ReturnRequested, requestedPkg?.Status);

        // Act 2: Operator reviews and rejects return
        int operatorId = 88; // Stefan
        await Harness.ReturnService.RejectReturnAsync(ret.Id, operatorId);

        // Assert
        var storedReturn = await Context.ReturnRepository.GetByIdAsync(ret.Id);
        var storedPkg = await Context.PackageRepository.GetByIdAsync(package.Id);

        Assert.NotNull(storedReturn);
        Assert.Equal(ReturnStatus.Rejected, storedReturn.Status);

        Assert.NotNull(storedPkg);
        Assert.Equal(PackageStatus.Collected, storedPkg.Status);

        // Verify customer can now rate the delivery
        await Harness.RatingService.RateCourierAsync(storedPkg.Id, 50, 5);
        var courier = await Context.PeopleService.GetCourierAsync(storedPkg.CourierId!.Value);
        Assert.Equal(5, courier?.Rank);
    }

    [Fact]
    public async Task E2E_T3_05_MultiPackageOrder_PartialReturnKeepAndRefund_IndependentLifecycles()
    {
        // Arrange: 3 packages under shared OrderGroupId
        int orderGroupId = 777;
        int customerId = 10;
        Context.PeopleService.SeedUser(new User { Id = customerId, Name = "MultiUser", Email = "multi@test.com" });

        var pkg1 = await CreateCollectedPackageAsync(recipientId: customerId);
        pkg1.OrderGroupId = orderGroupId;
        await Context.PackageRepository.UpdateAsync(pkg1);

        var pkg2 = await CreateCollectedPackageAsync(recipientId: customerId);
        pkg2.OrderGroupId = orderGroupId;
        await Context.PackageRepository.UpdateAsync(pkg2);

        var pkg3 = await CreateCollectedPackageAsync(recipientId: customerId);
        pkg3.OrderGroupId = orderGroupId;
        await Context.PackageRepository.UpdateAsync(pkg3);

        // Act 1: Return pkg1 and pkg2
        var ret1 = await Harness.ReturnService.RequestReturnAsync(pkg1.Id, customerId, "Defective item");
        var ret2 = await Harness.ReturnService.RequestReturnAsync(pkg2.Id, customerId, "Wrong size");

        // Act 2: Operator approves both returns
        await Harness.ReturnService.ApproveReturnAsync(ret1.Id, 7, 301, 302);
        await Harness.ReturnService.ApproveReturnAsync(ret2.Id, 7, 303, 304);

        // Act 3: Courier ships both returns
        await Harness.ReturnService.ShipReturnAsync(ret1.Id, 5);
        await Harness.ReturnService.ShipReturnAsync(ret2.Id, 5);

        // Act 4: Return 1 completes at merchant locker
        await Harness.ReturnService.CompleteReturnAsync(ret1.Id);

        // Act 5: Query returnable packages
        var returnable = (await Harness.ReturnService.GetReturnablePackagesAsync(orderGroupId, customerId)).ToList();

        // Assert:
        // Package 1: Returned & Refunded
        var storedPkg1 = await Context.PackageRepository.GetByIdAsync(pkg1.Id);
        var storedRet1 = await Context.ReturnRepository.GetByIdAsync(ret1.Id);
        Assert.Equal(PackageStatus.Returned, storedPkg1?.Status);
        Assert.Equal(ReturnStatus.Completed, storedRet1?.Status);
        Assert.Equal(PaymentStatus.Refunded, storedPkg1?.Payment?.Status);

        // Package 2: In Progress (Shipped return, not refunded yet)
        var storedPkg2 = await Context.PackageRepository.GetByIdAsync(pkg2.Id);
        var storedRet2 = await Context.ReturnRepository.GetByIdAsync(ret2.Id);
        Assert.Equal(PackageStatus.ReturnInProgress, storedPkg2?.Status);
        Assert.Equal(ReturnStatus.Shipped, storedRet2?.Status);
        Assert.Equal(PaymentStatus.Completed, storedPkg2?.Payment?.Status);

        // Package 3: Kept by customer
        var storedPkg3 = await Context.PackageRepository.GetByIdAsync(pkg3.Id);
        Assert.Equal(PackageStatus.Collected, storedPkg3?.Status);
        Assert.Equal(PaymentStatus.Completed, storedPkg3?.Payment?.Status);

        // Returnable list contains only Package 3
        Assert.Single(returnable);
        Assert.Equal(pkg3.Id, returnable[0].Id);
    }

    [Fact]
    public async Task E2E_T3_06_AcceptedOrderCancellationVsCourierAssignmentRace_Throws()
    {
        // Arrange
        var package = await CreateAcceptedPackageAsync();
        Assert.Equal(PackageStatus.Accepted, package.Status);

        // Act 1: Customer cancels immediately
        package.Cancel();
        await Context.PackageRepository.UpdateAsync(package);
        Assert.Equal(PackageStatus.Cancelled, package.Status);

        // Act 2: Courier assignment dispatcher races and attempts to assign courier
        int courierId = 15; // Piotr
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Harness.FulfillmentService.AssignCourierAsync(package.Id, courierId));

        // Assert
        Assert.Equal("Cannot assign courier to package in status Cancelled", ex.Message);
        var stored = await Context.PackageRepository.GetByIdAsync(package.Id);
        Assert.Null(stored?.Courier);
        Assert.Null(stored?.CourierId);
    }

    [Fact]
    public async Task E2E_T3_07_PaymentRetryFlowInterruptedByMissedDepositExpiration_CancelsAndRejectsDepositAndAcceptance()
    {
        // Arrange: Package created, deposit deadline in past
        var package = await CreateOrderedPackageAsync();
        SetPackageTimestamp(package, nameof(Package.DepositDeadline), DateTime.UtcNow.AddHours(-1));
        await Context.PackageRepository.UpdateAsync(package);

        // Act 1: Scheduled job sweeps missed deposits
        await Harness.IntakeService.ProcessMissedDepositsAsync();

        // Assert: Package cancelled
        var stored = await Context.PackageRepository.GetByIdAsync(package.Id);
        Assert.NotNull(stored);
        Assert.Equal(PackageStatus.Cancelled, stored.Status);

        // Act 2: Attempting to confirm deposit or accept on the cancelled package is rejected
        var exDeposit = Assert.Throws<InvalidOperationException>(() => stored.ConfirmDeposit());
        Assert.Equal("Cannot confirm deposit for package in status Cancelled", exDeposit.Message);

        var exAccept = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Harness.AcceptanceService.AcceptPackageAsync(stored.Id, 7, stored.BoxId!.Value));
        Assert.Equal("Package is not in an orderable state", exAccept.Message);

        Assert.True(Context.NotificationService.HasNotificationContaining("pickup window closed"));
    }

    [Fact]
    public async Task E2E_T3_08_PostCollectionCourierAndSenderRatingWithBoundsChecking_Success()
    {
        // Arrange
        int senderId = 10;
        int courierId = 30;
        int recipientId = 20;

        Context.PeopleService.SeedUser(new User { Id = senderId, Name = "Sender10", Email = "s10@test.com" });
        Context.PeopleService.SeedUser(new User { Id = recipientId, Name = "Recip20", Email = "r20@test.com" });
        Context.PeopleService.SeedCourier(new Courier { Id = courierId, Name = "Courier30", Email = "c30@pds.com" });

        var package = await CreateDeliveredPackageAsync(senderId: senderId, recipientId: recipientId, courierId: courierId);

        // Phase 1: Rating before collection fails
        var exPremature = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Harness.RatingService.RateCourierAsync(package.Id, recipientId, 5));
        Assert.Equal("Can only rate a delivery after it has been collected", exPremature.Message);

        // Phase 2: Collection succeeds
        await Harness.FulfillmentService.CollectPackageAsync(package.Id, recipientId, package.PickupCode!);
        var collectedPkg = await Context.PackageRepository.GetByIdAsync(package.Id);
        Assert.Equal(PackageStatus.Collected, collectedPkg?.Status);

        // Phase 3: Out-of-bounds score validation
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Harness.RatingService.RateCourierAsync(package.Id, recipientId, 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Harness.RatingService.RateCourierAsync(package.Id, recipientId, 6));

        // Phase 4: Unauthorized user validation
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Harness.RatingService.RateCourierAsync(package.Id, 999, 5));

        // Phase 5: Valid ratings for courier (5) and sender (4)
        await Harness.RatingService.RateCourierAsync(package.Id, recipientId, 5);
        await Harness.RatingService.RateSenderAsync(package.Id, recipientId, 4);

        var courier = await Context.PeopleService.GetCourierAsync(courierId);
        var sender = await Context.PeopleService.GetUserAsync(senderId);

        Assert.Equal(5, courier?.Rank);
        Assert.Equal(4, sender?.Rank);
    }
}
