namespace PackageDeliverySystem.Core;

/// <summary>
/// Represents the lifecycle states of a customer return request.
/// </summary>
public enum ReturnStatus
{
    Requested,
    Approved,
    Rejected,
    Shipped,
    Completed,
    Cancelled
}
