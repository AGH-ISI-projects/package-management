using PackageDeliverySystem.Core;
using PackageDeliverySystem.Tests.E2E.Infrastructure;
using Xunit;

namespace PackageDeliverySystem.Tests.E2E.Tier4_RealWorldScenarios;

/// <summary>
/// Tier 4 Real-World Application Scenarios: Multi-actor realistic end-to-end flows.
/// </summary>
public class RealWorldScenariosTests : E2ETestBase
{
    [Fact]
    public async Task E2E_T4_01_Scenario1_MultiPackageEcommerceOrder_WithPartialReturnAndRefund()
    {
        // =========================================================================
        // SCENARIO 1: Multi-package e-commerce checkout with partial return & refund
        // Actors: Customer Alice (101), Merchant TechStore (201), Courier Bob (301), Operator Charlie (401)
        // =========================================================================

        int orderGroupId = 9001;
        int customerAliceId = 101;
        int merchantId = 201;
        int courierBobId = 301;
        int operatorCharlieId = 401;

        // Step 1: Order Intake (3 Packages in same checkout)
        // Pkg 1: Smartwatch ($200.00, Medium)
        var pkg1 = await Harness.OrderService.CreatePackageAsync(merchantId, customerAliceId, 10, 20, BoxSize.Medium);
        pkg1.OrderGroupId = orderGroupId;
        await Harness.IntakeService.RequestPaymentAsync(pkg1.Id, "alice.zielinska@test.com", 200.00m);
        await Harness.IntakeService.ConfirmPaymentAsync(pkg1.Id);
        await Harness.IntakeService.ConfirmDepositAsync(pkg1.Id, courierBobId);

        // Pkg 2: Earbuds ($80.00, Small)
        var pkg2 = await Harness.OrderService.CreatePackageAsync(merchantId, customerAliceId, 10, 20, BoxSize.Small);
        pkg2.OrderGroupId = orderGroupId;
        await Harness.IntakeService.RequestPaymentAsync(pkg2.Id, "alice.zielinska@test.com", 80.00m);
        await Harness.IntakeService.ConfirmPaymentAsync(pkg2.Id);
        await Harness.IntakeService.ConfirmDepositAsync(pkg2.Id, courierBobId);

        // Pkg 3: USB Cable ($15.00, Small)
        var pkg3 = await Harness.OrderService.CreatePackageAsync(merchantId, customerAliceId, 10, 20, BoxSize.Small);
        pkg3.OrderGroupId = orderGroupId;
        await Harness.IntakeService.RequestPaymentAsync(pkg3.Id, "alice.zielinska@test.com", 15.00m);
        await Harness.IntakeService.ConfirmPaymentAsync(pkg3.Id);
        await Harness.IntakeService.ConfirmDepositAsync(pkg3.Id, courierBobId);

        // Step 2: Operator Acceptance & Shipping
        await Harness.AcceptanceService.AcceptPackageAsync(pkg1.Id, operatorCharlieId, pkg1.BoxId!.Value);
        await Harness.AcceptanceService.AcceptPackageAsync(pkg2.Id, operatorCharlieId, pkg2.BoxId!.Value);
        await Harness.AcceptanceService.AcceptPackageAsync(pkg3.Id, operatorCharlieId, pkg3.BoxId!.Value);

        await Harness.FulfillmentService.AssignCourierAsync(pkg1.Id, courierBobId);
        await Harness.FulfillmentService.AssignCourierAsync(pkg2.Id, courierBobId);
        await Harness.FulfillmentService.AssignCourierAsync(pkg3.Id, courierBobId);

        await Harness.FulfillmentService.ShipPackageAsync(pkg1.Id);
        await Harness.FulfillmentService.ShipPackageAsync(pkg2.Id);
        await Harness.FulfillmentService.ShipPackageAsync(pkg3.Id);

        await Harness.FulfillmentService.DeliverPackageAsync(pkg1.Id);
        await Harness.FulfillmentService.DeliverPackageAsync(pkg2.Id);
        await Harness.FulfillmentService.DeliverPackageAsync(pkg3.Id);

        Assert.Equal(PackageStatus.Delivered, pkg1.Status);
        Assert.Equal(PackageStatus.Delivered, pkg2.Status);
        Assert.Equal(PackageStatus.Delivered, pkg3.Status);

        // Step 3: Alice collects all 3 packages from locker
        await Harness.FulfillmentService.CollectPackageAsync(pkg1.Id, customerAliceId, pkg1.PickupCode!);
        await Harness.FulfillmentService.CollectPackageAsync(pkg2.Id, customerAliceId, pkg2.PickupCode!);
        await Harness.FulfillmentService.CollectPackageAsync(pkg3.Id, customerAliceId, pkg3.PickupCode!);

        Assert.Equal(PackageStatus.Collected, pkg1.Status);
        Assert.Equal(PackageStatus.Collected, pkg2.Status);
        Assert.Equal(PackageStatus.Collected, pkg3.Status);

        // Step 4: Alice initiates partial return for defective Smartwatch (pkg1)
        var returnReq = await Harness.ReturnService.RequestReturnAsync(pkg1.Id, customerAliceId, "Cracked screen on delivery");
        Assert.Equal(PackageStatus.ReturnRequested, pkg1.Status);
        Assert.Equal(PackageStatus.Collected, pkg2.Status);
        Assert.Equal(PackageStatus.Collected, pkg3.Status);

        // Step 5: Operator Charlie approves return
        await Harness.ReturnService.ApproveReturnAsync(returnReq.Id, operatorCharlieId, returnBoxId: 21, returnDestBoxId: 11);
        Assert.Equal(ReturnStatus.Approved, returnReq.Status);
        Assert.Equal(PackageStatus.ReturnInProgress, pkg1.Status);

        // Step 6: Courier Bob transports returned item
        await Harness.ReturnService.ShipReturnAsync(returnReq.Id, courierBobId);
        Assert.Equal(ReturnStatus.Shipped, returnReq.Status);

        // Merchant confirms receipt -> return completed
        await Harness.ReturnService.CompleteReturnAsync(returnReq.Id);

        // Step 7: Final Verifications
        var stored1 = await Context.PackageRepository.GetByIdAsync(pkg1.Id);
        var stored2 = await Context.PackageRepository.GetByIdAsync(pkg2.Id);
        var stored3 = await Context.PackageRepository.GetByIdAsync(pkg3.Id);
        var storedRet = await Context.ReturnRepository.GetByIdAsync(returnReq.Id);

        Assert.Equal(PackageStatus.Returned, stored1?.Status);
        Assert.Equal(PaymentStatus.Refunded, stored1?.Payment?.Status);
        Assert.Equal(ReturnStatus.Completed, storedRet?.Status);

        Assert.Equal(PackageStatus.Collected, stored2?.Status);
        Assert.Equal(PaymentStatus.Completed, stored2?.Payment?.Status);

        Assert.Equal(PackageStatus.Collected, stored3?.Status);
        Assert.Equal(PaymentStatus.Completed, stored3?.Payment?.Status);

        // Alice checks remaining returnable items: pkg2 and pkg3
        var returnable = (await Harness.ReturnService.GetReturnablePackagesAsync(orderGroupId, customerAliceId)).ToList();
        Assert.Equal(2, returnable.Count);
        Assert.Contains(returnable, p => p.Id == pkg2.Id);
        Assert.Contains(returnable, p => p.Id == pkg3.Id);
        Assert.DoesNotContain(returnable, p => p.Id == pkg1.Id);
    }

    [Fact]
    public async Task E2E_T4_02_Scenario2_PaymentFailureRetry_To_FiveStarCourierRating()
    {
        // =========================================================================
        // SCENARIO 2: Payment failure recovery to 5-star courier rating
        // Actors: Sender David (102), Recipient Eva (103), Courier Frank (302), Operator Grace (402)
        // =========================================================================

        int senderId = 102;
        int recipientId = 103;
        int courierId = 302;
        int operatorId = 402;

        // Step 1: Order creation & initial payment failure
        var package = await Harness.OrderService.CreatePackageAsync(senderId, recipientId, 10, 20, BoxSize.Medium);
        Context.PaymentGateway.DefaultStatus = PaymentStatus.Failed;

        await Harness.IntakeService.RequestPaymentAsync(package.Id, "david@test.com", 25.00m);
        await Harness.IntakeService.ConfirmPaymentAsync(package.Id);

        var midPkg = await Context.PackageRepository.GetByIdAsync(package.Id);
        Assert.Equal(PaymentStatus.Failed, midPkg?.Payment?.Status);
        Assert.Equal(PackageStatus.Ordered, midPkg?.Status);

        // Step 2: Payment retry with valid card succeeds
        Context.PaymentGateway.DefaultStatus = PaymentStatus.Completed;
        await Harness.IntakeService.RequestPaymentAsync(package.Id, "david@test.com", 25.00m);
        await Harness.IntakeService.ConfirmPaymentAsync(package.Id);

        var paidPkg = await Context.PackageRepository.GetByIdAsync(package.Id);
        Assert.Equal(PaymentStatus.Completed, paidPkg?.Payment?.Status);

        // Step 3: Courier Frank confirms physical deposit
        await Harness.IntakeService.ConfirmDepositAsync(package.Id, courierId);
        Assert.NotNull(paidPkg?.DepositConfirmedAt);

        // Step 4: Operator Grace accepts package
        await Harness.AcceptanceService.AcceptPackageAsync(package.Id, operatorId, package.BoxId!.Value);
        Assert.Equal(PackageStatus.Accepted, package.Status);

        // Step 5: Frank ships and delivers
        await Harness.FulfillmentService.AssignCourierAsync(package.Id, courierId);
        await Harness.FulfillmentService.ShipPackageAsync(package.Id);
        Assert.Equal(PackageStatus.Shipped, package.Status);

        await Harness.FulfillmentService.DeliverPackageAsync(package.Id);
        Assert.Equal(PackageStatus.Delivered, package.Status);
        Assert.NotNull(package.PickupCode);

        // Step 6: Eva collects parcel and rates Frank 5 stars
        await Harness.FulfillmentService.CollectPackageAsync(package.Id, recipientId, package.PickupCode);
        Assert.Equal(PackageStatus.Collected, package.Status);

        await Harness.RatingService.RateCourierAsync(package.Id, recipientId, 5);

        // Step 7: Verifications
        var courier = await Context.PeopleService.GetCourierAsync(courierId);
        Assert.Equal(5, courier?.Rank);

        // Check timestamp monotonicity
        Assert.True(package.DepositConfirmedAt <= package.AcceptedAt);
        Assert.True(package.AcceptedAt <= package.ShippedAt);
        Assert.True(package.ShippedAt <= package.DeliveredAt);
        Assert.True(package.DeliveredAt <= package.CollectedAt);
    }

    [Fact]
    public async Task E2E_T4_03_Scenario3_MissedDepositExpiration_To_ReOrderAndDisputeResolution()
    {
        // =========================================================================
        // SCENARIO 3: Missed deposit auto-cancel, re-order, rejection & support contact
        // Actors: Sender George (104), Recipient Hannah (105), Operator Ivan (403)
        // =========================================================================

        int senderId = 104;
        int recipientId = 105;
        int operatorId = 403;

        // Step 1: Order 1 missed deposit window
        var order1 = await Harness.OrderService.CreatePackageAsync(senderId, recipientId, 10, 20, BoxSize.Large);
        SetPackageTimestamp(order1, nameof(Package.DepositDeadline), DateTime.UtcNow.AddHours(-2));
        await Context.PackageRepository.UpdateAsync(order1);

        // Scheduled worker cancels overdue order
        await Harness.IntakeService.ProcessMissedDepositsAsync();
        var storedOrder1 = await Context.PackageRepository.GetByIdAsync(order1.Id);
        Assert.Equal(PackageStatus.Cancelled, storedOrder1?.Status);

        // Step 2: George re-orders as Order 2
        var order2 = await Harness.OrderService.CreatePackageAsync(senderId, recipientId, 10, 20, BoxSize.Large);
        await Harness.IntakeService.RequestPaymentAsync(order2.Id, "george@test.com", 35.00m);
        await Harness.IntakeService.ConfirmPaymentAsync(order2.Id);
        await Harness.IntakeService.ConfirmDepositAsync(order2.Id, 5);

        // Step 3: Operator Ivan inspects, hears broken glass/ceramics, rejects Order 2
        await Harness.AcceptanceService.RejectPackageAsync(order2.Id, operatorId);
        var storedOrder2 = await Context.PackageRepository.GetByIdAsync(order2.Id);
        Assert.Equal(PackageStatus.Rejected, storedOrder2?.Status);
        Assert.True(Context.NotificationService.HasNotificationContaining("rejected by operator Ivan"));

        // Step 4: George contacts support to dispute rejection
        await Context.NotificationService.NotifyAsync(
            "support",
            "[George Kaminski <george@test.com>] Order 3002 Rejection Dispute: Packed with bubble wrap. Please re-inspect.");

        // Assert: Notification received by support
        Assert.True(Context.NotificationService.HasNotificationContaining("Order 3002 Rejection Dispute", "support"));
        Assert.Equal(PackageStatus.Cancelled, storedOrder1?.Status);
        Assert.Equal(PackageStatus.Rejected, storedOrder2?.Status);
    }

    [Fact]
    public async Task E2E_T4_04_Scenario4_RecipientAbsence_48hExpiration_AndReturnToSender()
    {
        // =========================================================================
        // SCENARIO 4: Recipient absence, uncollected 48h expiration & courier return to sender
        // Actors: Sender Iris (106), Recipient Jack (107), Courier Kevin (303)
        // =========================================================================

        int senderId = 106;
        int recipientId = 107;
        int courierId = 303;

        // Step 1: Package delivered into destination locker
        var package = await CreateDeliveredPackageAsync(senderId, recipientId, courierId);
        Assert.Equal(PackageStatus.Delivered, package.Status);
        Assert.NotNull(package.PickupCode);

        // Step 2: Recipient is absent; 49 hours elapse without collection
        SetPackageTimestamp(package, nameof(Package.ExpiredAt), DateTime.UtcNow.AddHours(-1));
        await Context.PackageRepository.UpdateAsync(package);

        // Automated expiration job runs
        await Harness.AcceptanceService.ProcessExpiredPackagesAsync();
        var expiredPkg = await Context.PackageRepository.GetByIdAsync(package.Id);
        Assert.NotNull(expiredPkg);
        Assert.Equal(PackageStatus.Expired, expiredPkg.Status);

        // Unauthorized/expired collection attempt throws
        var exCollect = Assert.Throws<InvalidOperationException>(() => expiredPkg.Collect(package.PickupCode!));
        Assert.Equal("Cannot collect package in status Expired", exCollect.Message);

        // Step 3: Courier Kevin cleans locker on maintenance route, marks returned to sender
        expiredPkg.MarkAsReturned();
        await Context.PackageRepository.UpdateAsync(expiredPkg);

        var finalPkg = await Context.PackageRepository.GetByIdAsync(package.Id);
        Assert.Equal(PackageStatus.ReturnedToSender, finalPkg?.Status);
    }

    [Fact]
    public async Task E2E_T4_05_Scenario5_CourierFindsEmptyBoxUponPickup_InstantCancelAndReOrder()
    {
        // =========================================================================
        // SCENARIO 5: Courier finds empty box upon pickup, instant cancel & clean re-order
        // Actors: Sender Mike (108), Recipient Nina (109), Courier Oscar (304)
        // =========================================================================

        int senderId = 108;
        int recipientId = 109;
        int courierId = 304;
        int operatorId = 7;

        // Step 1: Mike creates Order 1 and pays online
        var order1 = await Harness.OrderService.CreatePackageAsync(senderId, recipientId, 10, 20, BoxSize.Medium);
        await Harness.IntakeService.RequestPaymentAsync(order1.Id, "mike@test.com", 25.00m);
        await Harness.IntakeService.ConfirmPaymentAsync(order1.Id);

        // Mike forgets to put parcel in box; Courier Oscar opens box and finds it empty
        await Harness.IntakeService.ReportMissingDepositAsync(order1.Id, courierId);

        // Step 2: Instant cancellation
        var storedOrder1 = await Context.PackageRepository.GetByIdAsync(order1.Id);
        Assert.Equal(PackageStatus.Cancelled, storedOrder1?.Status);
        Assert.True(Context.NotificationService.HasNotificationContaining("courier Oscar found the box empty at pickup"));

        // Operator acceptance on cancelled package fails
        var exAccept = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Harness.AcceptanceService.AcceptPackageAsync(order1.Id, operatorId, order1.BoxId!.Value));
        Assert.Equal("Package is not in an orderable state", exAccept.Message);

        // Step 3: Mike realizes mistake, creates Order 2, deposits parcel successfully
        var order2 = await Harness.OrderService.CreatePackageAsync(senderId, recipientId, 10, 20, BoxSize.Medium);
        await Harness.IntakeService.RequestPaymentAsync(order2.Id, "mike@test.com", 25.00m);
        await Harness.IntakeService.ConfirmPaymentAsync(order2.Id);

        // Courier Oscar confirms real physical deposit
        await Harness.IntakeService.ConfirmDepositAsync(order2.Id, courierId);

        // Operator accepts Order 2
        await Harness.AcceptanceService.AcceptPackageAsync(order2.Id, operatorId, order2.BoxId!.Value);

        var storedOrder2 = await Context.PackageRepository.GetByIdAsync(order2.Id);
        Assert.Equal(PackageStatus.Accepted, storedOrder2?.Status);
        Assert.Equal(PackageStatus.Cancelled, storedOrder1?.Status);
    }
}
