namespace PackageDeliverySystem.Core;

public class PackageItem
{
    public int Id { get; set; } = 0;
    public int PackageId { get; set; }
    public Package Package { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }

    /// <summary>
    /// Foreign key pointing to the return delivery (Package) in which this item is being returned.
    /// Null means the item has not been returned yet.
    /// </summary>
    public int? ReturnPackageId { get; set; }
    public Package? ReturnPackage { get; set; }

    public bool IsReturned => ReturnPackageId.HasValue;
}
