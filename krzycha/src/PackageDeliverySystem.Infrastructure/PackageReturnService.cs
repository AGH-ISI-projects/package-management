using PackageDeliverySystem.Core;

namespace PackageDeliverySystem.Infrastructure;

public class PackageReturnService : IPackageReturnService
{
    private readonly IPackageRepository _packageRepository;
    private readonly IBoxRepository _boxRepository;
    private readonly IPackageOrderService _packageOrderService;
    private readonly INotificationService _notificationService;

    public PackageReturnService(
        IPackageRepository packageRepository,
        IBoxRepository boxRepository,
        IPackageOrderService packageOrderService,
        INotificationService notificationService)
    {
        _packageRepository = packageRepository ?? throw new ArgumentNullException(nameof(packageRepository));
        _boxRepository = boxRepository ?? throw new ArgumentNullException(nameof(boxRepository));
        _packageOrderService = packageOrderService ?? throw new ArgumentNullException(nameof(packageOrderService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
    }

    public async Task<Package> CreateReturnDeliveryAsync(
        int originalPackageId,
        int customerId,
        IReadOnlyList<int> itemIds,
        int? originLockerId = null,
        BoxSize? size = null)
    {
        if (itemIds == null || itemIds.Count == 0)
            throw new ArgumentException("At least one item must be selected for return.", nameof(itemIds));

        var originalPackage = await _packageRepository.GetByIdAsync(originalPackageId)
            ?? throw new ArgumentException($"Package with id {originalPackageId} was not found.");

        if (originalPackage.RecipientId != customerId)
            throw new InvalidOperationException("Only the package recipient can request a return.");

        if (originalPackage.Status != PackageStatus.Collected)
            throw new InvalidOperationException(
                $"Cannot return a package in status {originalPackage.Status}. Only collected packages can be returned.");

        var itemsToReturn = originalPackage.Items.Where(i => itemIds.Contains(i.Id)).ToList();
        if (itemsToReturn.Count != itemIds.Count)
            throw new ArgumentException("One or more selected items do not belong to the specified package.");

        if (itemsToReturn.Any(i => i.IsReturned))
            throw new InvalidOperationException("One or more selected items have already been included in a return delivery.");

        if (!originalPackage.BoxId.HasValue || !originalPackage.DestinationBoxId.HasValue)
            throw new InvalidOperationException("Original package is missing origin or destination box reference.");

        var originalDestinationBox = await _boxRepository.GetByIdAsync(originalPackage.DestinationBoxId.Value)
            ?? throw new InvalidOperationException("Original destination locker box could not be found.");

        var originalOriginBox = await _boxRepository.GetByIdAsync(originalPackage.BoxId.Value)
            ?? throw new InvalidOperationException("Original origin locker box could not be found.");

        // By default, the customer drops the return at their pickup locker, and it goes back to the merchant's locker
        int returnOriginLockerId = originLockerId ?? originalDestinationBox.ParcelLockerId;
        int returnDestinationLockerId = originalOriginBox.ParcelLockerId;
        var returnSize = size ?? originalPackage.Size;

        // Use the genuine package order service to allocate boxes and create the return package
        var returnPackage = await _packageOrderService.CreatePackageAsync(
            senderId: originalPackage.RecipientId,
            recipientId: originalPackage.SenderId,
            originLockerId: returnOriginLockerId,
            destinationLockerId: returnDestinationLockerId,
            size: returnSize);

        returnPackage.OriginalPackageId = originalPackage.Id;

        // Mark each returned item with the return delivery package id
        foreach (var item in itemsToReturn)
        {
            item.ReturnPackageId = returnPackage.Id;
        }

        await _packageRepository.UpdateAsync(originalPackage);
        await _packageRepository.UpdateAsync(returnPackage);

        await _notificationService.NotifyAsync(
            "system",
            $"Return delivery {returnPackage.Id} created for package {originalPackage.Id} with {itemsToReturn.Count} item(s).");

        return returnPackage;
    }

    public async Task<IReadOnlyList<PackageItem>> GetAvailableItemsForReturnAsync(int originalPackageId, int customerId)
    {
        var package = await _packageRepository.GetByIdAsync(originalPackageId)
            ?? throw new ArgumentException($"Package with id {originalPackageId} was not found.");

        if (package.RecipientId != customerId)
            throw new InvalidOperationException("Only the package recipient can query returnable items.");

        return package.Items.Where(i => !i.IsReturned).ToList();
    }

    public async Task<IReadOnlyList<Package>> GetReturnDeliveriesForPackageAsync(int originalPackageId)
    {
        var allPackages = await _packageRepository.GetAllAsync();
        return allPackages.Where(p => p.OriginalPackageId == originalPackageId).ToList();
    }
}
