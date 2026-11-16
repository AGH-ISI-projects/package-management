using PackageDeliverySystem.Core;

namespace PackageDeliverySystem.Infrastructure;

public class PackageOrderService : IPackageOrderService
{
    private readonly IPackageRepository _packageRepository;
    private readonly IBoxRepository _boxRepository;
    private readonly IPeopleService _peopleService;
    private readonly INotificationService? _notificationService;

    public PackageOrderService(
        IPackageRepository packageRepository,
        IBoxRepository boxRepository,
        IPeopleService peopleService,
        INotificationService? notificationService = null)
    {
        _packageRepository = packageRepository;
        _boxRepository = boxRepository;
        _peopleService = peopleService;
        _notificationService = notificationService;
    }

    public async Task<Package> CreatePackageAsync(
        int senderId,
        int recipientId,
        int originLockerId,
        int destinationLockerId,
        BoxSize size,
        IEnumerable<PackageItem>? items = null)
    {
        var sender = await _peopleService.GetUserAsync(senderId)
            ?? throw new ArgumentException("Sender not found");
        var recipient = await _peopleService.GetUserAsync(recipientId)
            ?? throw new ArgumentException("Recipient not found");

        var originBox = await _boxRepository.FindFreeBoxAsync(originLockerId, size)
            ?? throw new InvalidOperationException(
                $"No free {size} box at the chosen origin locker - pick another locker or size");

        // excludeBoxId stops the same physical box being picked as both origin and
        // destination, which matters if the sender and recipient use the same locker
        var destinationBox = await _boxRepository.FindFreeBoxAsync(destinationLockerId, size, originBox.Id)
            ?? throw new InvalidOperationException(
                $"No free {size} box at the chosen destination locker - pick another locker or size");

        var package = new Package
        {
            Sender = sender,
            Recipient = recipient,
            Size = size,
            BoxId = originBox.Id,
            DestinationBoxId = destinationBox.Id
        };

        if (items != null)
        {
            foreach (var item in items)
            {
                package.Items.Add(item);
            }
        }

        await _packageRepository.AddAsync(package);
        return package;
    }

    public async Task CancelPackageAsync(int packageId, int? requestingUserId = null)
    {
        var package = await _packageRepository.GetByIdAsync(packageId)
            ?? throw new ArgumentException("Package not found");

        if (requestingUserId.HasValue)
        {
            var userId = requestingUserId.Value;
            bool isSender = package.SenderId == userId;
            bool isOperator = package.OperatorId.HasValue && package.OperatorId.Value == userId;

            if (!isSender && !isOperator)
            {
                throw new InvalidOperationException(
                    $"User {userId} is not authorized to cancel package {packageId}. Only the sender or assigned operator can cancel.");
            }
        }

        package.Cancel();
        await _packageRepository.UpdateAsync(package);

        if (_notificationService != null)
        {
            await _notificationService.NotifyAsync("system",
                $"Package {package.Id} was cancelled{(requestingUserId.HasValue ? $" by user {requestingUserId.Value}" : "")}.");

            if (package.Sender != null && !string.IsNullOrEmpty(package.Sender.Email))
            {
                await _notificationService.NotifyAsync(package.Sender.Email,
                    $"Your package #{package.Id} has been cancelled.");
            }
        }
    }
}
