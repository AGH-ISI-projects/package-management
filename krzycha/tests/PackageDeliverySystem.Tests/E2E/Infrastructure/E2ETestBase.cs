using System.Reflection;
using PackageDeliverySystem.Core;

namespace PackageDeliverySystem.Tests.E2E.Infrastructure;

/// <summary>
/// Base class for all opaque-box E2E tests, offering fresh harnesses, actor access, and state-setup helpers.
/// </summary>
public abstract class E2ETestBase
{
    protected E2ETestHarness Harness { get; }
    protected E2ETestContext Context => Harness.Context;

    protected E2ETestBase()
    {
        Harness = new E2ETestHarness();
    }

    /// <summary>
    /// Helper to create and persist a package in Ordered status.
    /// </summary>
    protected async Task<Package> CreateOrderedPackageAsync(
        int senderId = 1,
        int recipientId = 2,
        int originLockerId = 10,
        int destinationLockerId = 20,
        BoxSize size = BoxSize.Medium)
    {
        return await Harness.OrderService.CreatePackageAsync(senderId, recipientId, originLockerId, destinationLockerId, size);
    }

    /// <summary>
    /// Helper to progress a package to Accepted status with completed payment and deposit.
    /// </summary>
    protected async Task<Package> CreateAcceptedPackageAsync(
        int senderId = 1,
        int recipientId = 2,
        int originLockerId = 10,
        int destinationLockerId = 20,
        BoxSize size = BoxSize.Medium,
        int operatorId = 7,
        int courierId = 5,
        decimal amount = 25.00m)
    {
        var package = await CreateOrderedPackageAsync(senderId, recipientId, originLockerId, destinationLockerId, size);

        await Harness.IntakeService.RequestPaymentAsync(package.Id, "payer@test.com", amount);
        await Harness.IntakeService.ConfirmPaymentAsync(package.Id);

        await Harness.IntakeService.ConfirmDepositAsync(package.Id, courierId);

        await Harness.AcceptanceService.AcceptPackageAsync(package.Id, operatorId, package.BoxId!.Value);

        return package;
    }

    /// <summary>
    /// Helper to progress a package to Shipped status.
    /// </summary>
    protected async Task<Package> CreateShippedPackageAsync(
        int senderId = 1,
        int recipientId = 2,
        int courierId = 5)
    {
        var package = await CreateAcceptedPackageAsync(senderId, recipientId, courierId: courierId);
        await Harness.FulfillmentService.AssignCourierAsync(package.Id, courierId);
        await Harness.FulfillmentService.ShipPackageAsync(package.Id);
        return package;
    }

    /// <summary>
    /// Helper to progress a package to Delivered status with a generated pickup code.
    /// </summary>
    protected async Task<Package> CreateDeliveredPackageAsync(
        int senderId = 1,
        int recipientId = 2,
        int courierId = 5)
    {
        var package = await CreateShippedPackageAsync(senderId, recipientId, courierId);
        await Harness.FulfillmentService.DeliverPackageAsync(package.Id);
        return package;
    }

    /// <summary>
    /// Helper to progress a package to Collected status.
    /// </summary>
    protected async Task<Package> CreateCollectedPackageAsync(
        int senderId = 1,
        int recipientId = 2,
        int courierId = 5)
    {
        var package = await CreateDeliveredPackageAsync(senderId, recipientId, courierId);
        await Harness.FulfillmentService.CollectPackageAsync(package.Id, recipientId, package.PickupCode!);
        return package;
    }

    /// <summary>
    /// Helper to simulate elapsed time on private/protected timestamp properties when testing timeouts.
    /// </summary>
    protected void SetPackageTimestamp(Package package, string propertyName, DateTime? value)
    {
        var prop = typeof(Package).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (prop != null && prop.CanWrite)
        {
            prop.SetValue(package, value);
        }
        else
        {
            var setter = prop?.GetSetMethod(true);
            setter?.Invoke(package, new object?[] { value });
        }
    }
}
