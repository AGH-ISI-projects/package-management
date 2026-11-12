using PackageDeliverySystem.Core;

namespace PackageDeliverySystem.Tests.E2E.Infrastructure;

/// <summary>
/// Fully functional in-memory domain service implementation of IReturnService for zero-DB E2E testing.
/// </summary>
public class InMemoryReturnService : IReturnService
{
    private readonly IPackageRepository _packageRepository;
    private readonly IReturnRepository _returnRepository;
    private readonly IPeopleService _peopleService;
    private readonly INotificationService _notificationService;

    public InMemoryReturnService(
        IPackageRepository packageRepository,
        IReturnRepository returnRepository,
        IPeopleService peopleService,
        INotificationService notificationService)
    {
        _packageRepository = packageRepository;
        _returnRepository = returnRepository;
        _peopleService = peopleService;
        _notificationService = notificationService;
    }

    public async Task<Return> RequestReturnAsync(int packageId, int customerId, string reason)
    {
        var package = await _packageRepository.GetByIdAsync(packageId)
            ?? throw new ArgumentException("Package not found");

        if (package.RecipientId != customerId)
            throw new InvalidOperationException("Only the recipient can request a return");

        if (package.Status != PackageStatus.Collected)
            throw new InvalidOperationException($"Cannot request return for package in status {package.Status}. Only collected packages can be returned.");

        var activeReturns = await _returnRepository.GetActiveReturnsByPackageIdAsync(packageId);
        if (activeReturns.Any())
            throw new InvalidOperationException("A return request already exists for this package");

        package.RequestReturn();

        var returnEntity = new Return
        {
            PackageId = packageId,
            Package = package,
            Reason = reason,
            RequestedAt = DateTime.UtcNow
        };

        package.Return = returnEntity;

        await _returnRepository.AddAsync(returnEntity);
        await _packageRepository.UpdateAsync(package);

        await _notificationService.NotifyAsync("system",
            $"Return request {returnEntity.Id} created for package {packageId}.");

        return returnEntity;
    }

    public async Task<Return> RequestReturnAsync(int packageId, string reason)
    {
        var package = await _packageRepository.GetByIdAsync(packageId)
            ?? throw new ArgumentException("Package not found");

        return await RequestReturnAsync(packageId, package.RecipientId, reason);
    }

    public async Task ApproveReturnAsync(int returnId, int operatorId, int returnBoxId, int returnDestBoxId)
    {
        var returnEntity = await _returnRepository.GetByIdAsync(returnId)
            ?? throw new ArgumentException("Return not found");

        var op = await _peopleService.GetOperatorAsync(operatorId)
            ?? throw new ArgumentException("Operator not found");

        var package = returnEntity.Package ?? await _packageRepository.GetByIdAsync(returnEntity.PackageId)
            ?? throw new ArgumentException("Package not found");

        returnEntity.Approve(returnBoxId, returnDestBoxId);
        package.ApproveReturn();

        await _returnRepository.UpdateAsync(returnEntity);
        await _packageRepository.UpdateAsync(package);

        await _notificationService.NotifyAsync("system",
            $"Return {returnId} approved by operator {op.Name}.");
    }

    public async Task ApproveReturnAsync(int returnId, int operatorId, int? destinationBoxId = null)
    {
        var returnEntity = await _returnRepository.GetByIdAsync(returnId)
            ?? throw new ArgumentException("Return not found");

        var op = await _peopleService.GetOperatorAsync(operatorId)
            ?? throw new ArgumentException("Operator not found");

        var package = returnEntity.Package ?? await _packageRepository.GetByIdAsync(returnEntity.PackageId)
            ?? throw new ArgumentException("Package not found");

        returnEntity.Approve(null, destinationBoxId);
        package.ApproveReturn();

        await _returnRepository.UpdateAsync(returnEntity);
        await _packageRepository.UpdateAsync(package);

        await _notificationService.NotifyAsync("system",
            $"Return {returnId} approved by operator {op.Name}.");
    }

    public Task RejectReturnAsync(int returnId, int operatorId)
        => RejectReturnAsync(returnId, operatorId, null);

    public async Task RejectReturnAsync(int returnId, int operatorId, string? reason)
    {
        var returnEntity = await _returnRepository.GetByIdAsync(returnId)
            ?? throw new ArgumentException("Return not found");

        var op = await _peopleService.GetOperatorAsync(operatorId)
            ?? throw new ArgumentException("Operator not found");

        var package = returnEntity.Package ?? await _packageRepository.GetByIdAsync(returnEntity.PackageId)
            ?? throw new ArgumentException("Package not found");

        returnEntity.Reject();
        package.RejectReturn();

        await _returnRepository.UpdateAsync(returnEntity);
        await _packageRepository.UpdateAsync(package);

        await _notificationService.NotifyAsync("system",
            $"Return {returnId} rejected by operator {op.Name}.");
    }

    public Task ShipReturnAsync(int returnId, int courierId)
        => ShipReturnAsync(returnId, courierId, null);

    public async Task ShipReturnAsync(int returnId, int courierId, int? boxId)
    {
        var returnEntity = await _returnRepository.GetByIdAsync(returnId)
            ?? throw new ArgumentException("Return not found");

        var courier = await _peopleService.GetCourierAsync(courierId)
            ?? throw new ArgumentException("Courier not found");

        var package = returnEntity.Package ?? await _packageRepository.GetByIdAsync(returnEntity.PackageId)
            ?? throw new ArgumentException("Package not found");

        returnEntity.Ship(boxId);
        package.ShipReturn();

        await _returnRepository.UpdateAsync(returnEntity);
        await _packageRepository.UpdateAsync(package);

        await _notificationService.NotifyAsync("system",
            $"Return {returnId} shipped by courier {courier.Name}.");
    }

    public async Task CompleteReturnAsync(int returnId)
    {
        var returnEntity = await _returnRepository.GetByIdAsync(returnId)
            ?? throw new ArgumentException("Return not found");

        var package = returnEntity.Package ?? await _packageRepository.GetByIdAsync(returnEntity.PackageId)
            ?? throw new ArgumentException("Package not found");

        returnEntity.Complete();
        package.CompleteReturn();

        if (package.Payment is { Status: PaymentStatus.Completed })
        {
            package.Payment.Refund();
        }

        await _returnRepository.UpdateAsync(returnEntity);
        await _packageRepository.UpdateAsync(package);

        await _notificationService.NotifyAsync("system",
            $"Return {returnId} completed and payment refunded.");
    }

    public async Task CancelReturnAsync(int returnId, string? reason = null)
    {
        var returnEntity = await _returnRepository.GetByIdAsync(returnId)
            ?? throw new ArgumentException("Return not found");

        var package = returnEntity.Package ?? await _packageRepository.GetByIdAsync(returnEntity.PackageId)
            ?? throw new ArgumentException("Package not found");

        returnEntity.Cancel();
        package.CancelReturn();

        await _returnRepository.UpdateAsync(returnEntity);
        await _packageRepository.UpdateAsync(package);
    }

    public async Task<IEnumerable<Package>> GetReturnablePackagesAsync(int orderGroupId, int customerId)
    {
        var allPackages = await _packageRepository.GetAllAsync();
        var orderPackages = allPackages
            .Where(p => p.OrderGroupId == orderGroupId && p.RecipientId == customerId && p.Status == PackageStatus.Collected)
            .ToList();

        var returnable = new List<Package>();
        foreach (var pkg in orderPackages)
        {
            var activeReturns = await _returnRepository.GetActiveReturnsByPackageIdAsync(pkg.Id);
            var allReturns = await _returnRepository.GetByPackageIdAsync(pkg.Id);
            bool hasCompletedReturn = allReturns.Any(r => r.Status == ReturnStatus.Completed);

            if (!activeReturns.Any() && !hasCompletedReturn)
            {
                returnable.Add(pkg);
            }
        }

        return returnable;
    }

    public Task<Return?> GetReturnByIdAsync(int returnId)
    {
        return _returnRepository.GetByIdAsync(returnId);
    }
}
