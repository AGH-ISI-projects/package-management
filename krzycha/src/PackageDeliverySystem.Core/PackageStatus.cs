namespace PackageDeliverySystem.Core;

public enum PackageStatus
{
    Ordered,
    Accepted,
    Preparing,
    Shipped,
    Delivered,
    Collected,
    Rejected,
    Cancelled,
    Expired,
    ReturnedToSender,
    ReturnRequested,
    ReturnInProgress,
    Returned
}
