namespace PackageDeliverySystem.Core;

public class Return
{
    public int Id { get; set; } = 0;
    public int PackageId { get; set; }
    public Package Package { get; set; } = null!;
    public ReturnStatus Status { get; private set; } = ReturnStatus.Requested;
    public string Reason { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ApprovedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public int? ReturnBoxId { get; set; }
    public Box? ReturnBox { get; set; }
    public int? ReturnDestinationBoxId { get; set; }
    public Box? ReturnDestinationBox { get; set; }

    /// <summary>
    /// Approves the return request and assigns locker boxes.
    /// </summary>
    public void Approve(int? returnBoxId = null, int? returnDestinationBoxId = null)
    {
        if (Status != ReturnStatus.Requested)
            throw new InvalidOperationException($"Cannot approve return in status {Status}");

        if (returnBoxId.HasValue)
            ReturnBoxId = returnBoxId.Value;

        if (returnDestinationBoxId.HasValue)
            ReturnDestinationBoxId = returnDestinationBoxId.Value;

        Status = ReturnStatus.Approved;
        ApprovedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Rejects the return request.
    /// </summary>
    public void Reject()
    {
        if (Status != ReturnStatus.Requested)
            throw new InvalidOperationException($"Cannot reject return in status {Status}");

        Status = ReturnStatus.Rejected;
    }

    /// <summary>
    /// Marks the return as shipped by the courier.
    /// </summary>
    public void Ship(int? boxId = null)
    {
        if (Status != ReturnStatus.Approved)
            throw new InvalidOperationException($"Cannot ship return in status {Status}");

        if (boxId.HasValue)
            ReturnBoxId = boxId.Value;

        Status = ReturnStatus.Shipped;
    }

    /// <summary>
    /// Finalizes the return once package arrives at destination locker.
    /// </summary>
    public void Complete()
    {
        if (Status != ReturnStatus.Shipped && Status != ReturnStatus.Approved)
            throw new InvalidOperationException($"Cannot complete return in status {Status}");

        Status = ReturnStatus.Completed;
        CompletedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Cancels the return request.
    /// </summary>
    public void Cancel()
    {
        if (Status != ReturnStatus.Requested && Status != ReturnStatus.Approved)
            throw new InvalidOperationException($"Cannot cancel return in status {Status}");

        Status = ReturnStatus.Cancelled;
    }
}
