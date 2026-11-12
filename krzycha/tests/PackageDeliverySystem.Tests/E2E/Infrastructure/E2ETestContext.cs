using PackageDeliverySystem.Core;

namespace PackageDeliverySystem.Tests.E2E.Infrastructure;

/// <summary>
/// Encapsulates isolated state, repositories, services, and virtual time progression for an E2E test.
/// </summary>
public class E2ETestContext
{
    public InMemoryPackageRepository PackageRepository { get; }
    public InMemoryBoxRepository BoxRepository { get; }
    public InMemoryReturnRepository ReturnRepository { get; }
    public InMemoryPeopleService PeopleService { get; }
    public TestPaymentGateway PaymentGateway { get; }
    public TestNotificationService NotificationService { get; }

    public DateTime CurrentTime { get; private set; }

    public E2ETestContext()
    {
        CurrentTime = DateTime.UtcNow;

        PackageRepository = new InMemoryPackageRepository();
        BoxRepository = new InMemoryBoxRepository(PackageRepository);
        ReturnRepository = new InMemoryReturnRepository();
        PeopleService = new InMemoryPeopleService();
        PaymentGateway = new TestPaymentGateway();
        NotificationService = new TestNotificationService();

        SeedDefaults();
    }

    private void SeedDefaults()
    {
        foreach (var user in E2EActors.GetAllUsers())
        {
            PeopleService.SeedUser(user);
        }

        foreach (var courier in E2EActors.GetAllCouriers())
        {
            PeopleService.SeedCourier(courier);
        }

        foreach (var op in E2EActors.GetAllOperators())
        {
            PeopleService.SeedOperator(op);
        }

        BoxRepository.SeedRange(E2EActors.CreateDefaultBoxes());
    }

    public void AdvanceTime(TimeSpan duration)
    {
        CurrentTime = CurrentTime.Add(duration);
    }
}
