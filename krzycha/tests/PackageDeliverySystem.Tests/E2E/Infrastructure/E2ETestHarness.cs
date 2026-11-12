using PackageDeliverySystem.Core;
using PackageDeliverySystem.Infrastructure;

namespace PackageDeliverySystem.Tests.E2E.Infrastructure;

/// <summary>
/// Opaque-box E2E test harness wiring genuine services to fast in-memory stores.
/// </summary>
public class E2ETestHarness
{
    public E2ETestContext Context { get; }

    public IPackageOrderService OrderService { get; }
    public IPackageIntakeService IntakeService { get; }
    public IPackageAcceptanceService AcceptanceService { get; }
    public IPackageFulfillmentService FulfillmentService { get; }
    public IRatingService RatingService { get; }
    public IReturnService ReturnService { get; }

    public E2ETestHarness(E2ETestContext? context = null)
    {
        Context = context ?? new E2ETestContext();

        OrderService = new PackageOrderService(
            Context.PackageRepository,
            Context.BoxRepository,
            Context.PeopleService);

        IntakeService = new PackageIntakeService(
            Context.PackageRepository,
            Context.PaymentGateway,
            Context.NotificationService,
            Context.PeopleService);

        AcceptanceService = new PackageAcceptanceService(
            Context.PackageRepository,
            Context.BoxRepository,
            Context.NotificationService,
            Context.PeopleService);

        FulfillmentService = new PackageFulfillmentService(
            Context.PackageRepository,
            Context.NotificationService,
            Context.PeopleService);

        RatingService = new RatingService(
            Context.PackageRepository,
            Context.PeopleService);

        ReturnService = new InMemoryReturnService(
            Context.PackageRepository,
            Context.ReturnRepository,
            Context.PeopleService,
            Context.NotificationService);
    }
}
