namespace PackageDeliverySystem.Core;

/// <summary>
/// Domain service orchestrating return deliveries (including partial returns of specific items)
/// for collected packages.
/// </summary>
public interface IPackageReturnService
{
    /// <summary>
    /// Creates a return delivery (Package) for specific items from a collected package.
    /// Sets sender as the recipient and recipient as the original sender (merchant).
    /// </summary>
    Task<Package> CreateReturnDeliveryAsync(
        int originalPackageId,
        int customerId,
        IReadOnlyList<int> itemIds,
        int? originLockerId = null,
        BoxSize? size = null);

    /// <summary>
    /// Returns items belonging to the package that have not yet been included in any return delivery.
    /// </summary>
    Task<IReadOnlyList<PackageItem>> GetAvailableItemsForReturnAsync(int originalPackageId, int customerId);

    /// <summary>
    /// Returns all return deliveries created for the specified original package.
    /// </summary>
    Task<IReadOnlyList<Package>> GetReturnDeliveriesForPackageAsync(int originalPackageId);
}
