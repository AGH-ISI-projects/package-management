namespace PackageDeliverySystem.Core;

/// <summary>
/// Domain service contract for customer returns and partial returns orchestration.
/// </summary>
public interface IReturnService
{
    /// <summary>
    /// Initiates a return request for a collected package on behalf of the customer.
    /// </summary>
    Task<Return> RequestReturnAsync(int packageId, int customerId, string reason);

    /// <summary>
    /// Initiates a return request for a collected package when customer context is implicit.
    /// </summary>
    Task<Return> RequestReturnAsync(int packageId, string reason);

    /// <summary>
    /// Operator approves a return request and explicitly assigns origin and destination return boxes.
    /// </summary>
    Task ApproveReturnAsync(int returnId, int operatorId, int returnBoxId, int returnDestBoxId);

    /// <summary>
    /// Operator approves a return request with optional destination box.
    /// </summary>
    Task ApproveReturnAsync(int returnId, int operatorId, int? destinationBoxId = null);

    /// <summary>
    /// Operator rejects a return request.
    /// </summary>
    Task RejectReturnAsync(int returnId, int operatorId);

    /// <summary>
    /// Operator rejects a return request with a specified rejection reason.
    /// </summary>
    Task RejectReturnAsync(int returnId, int operatorId, string? reason);

    /// <summary>
    /// Courier marks return as picked up from origin return box and in transit.
    /// </summary>
    Task ShipReturnAsync(int returnId, int courierId);

    /// <summary>
    /// Courier marks return as picked up from origin return box and in transit, specifying origin box ID.
    /// </summary>
    Task ShipReturnAsync(int returnId, int courierId, int? boxId);

    /// <summary>
    /// Completes the return upon delivery to destination merchant box; triggers payment refund.
    /// </summary>
    Task CompleteReturnAsync(int returnId);

    /// <summary>
    /// Cancels an active return request.
    /// </summary>
    Task CancelReturnAsync(int returnId, string? reason = null);

    /// <summary>
    /// Queries packages belonging to an order group that are eligible for return by the specified customer.
    /// </summary>
    Task<IEnumerable<Package>> GetReturnablePackagesAsync(int orderGroupId, int customerId);

    /// <summary>
    /// Retrieves a return entity by its ID.
    /// </summary>
    Task<Return?> GetReturnByIdAsync(int returnId);
}
