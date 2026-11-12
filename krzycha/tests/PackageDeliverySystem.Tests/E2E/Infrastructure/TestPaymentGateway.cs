using System.Collections.Concurrent;
using PackageDeliverySystem.Core;

namespace PackageDeliverySystem.Tests.E2E.Infrastructure;

/// <summary>
/// Controllable in-memory payment gateway for E2E tests.
/// </summary>
public class TestPaymentGateway : IPaymentGateway
{
    private readonly ConcurrentDictionary<string, PaymentStatus> _statuses = new();
    private readonly ConcurrentBag<(string Email, decimal Amount, string Ref)> _history = new();
    private int _referenceCounter = 1000;

    public PaymentStatus DefaultStatus { get; set; } = PaymentStatus.Completed;

    public Task<string> InitiatePaymentAsync(string payerEmail, decimal amount)
    {
        var reference = $"TEST-PAY-{Interlocked.Increment(ref _referenceCounter)}";
        _statuses[reference] = DefaultStatus;
        _history.Add((payerEmail, amount, reference));
        return Task.FromResult(reference);
    }

    public Task<PaymentStatus> GetPaymentStatusAsync(string externalReference)
    {
        if (_statuses.TryGetValue(externalReference, out var status))
        {
            return Task.FromResult(status);
        }

        return Task.FromResult(DefaultStatus);
    }

    public void SetStatusForReference(string externalReference, PaymentStatus status)
    {
        _statuses[externalReference] = status;
    }

    public IReadOnlyCollection<(string Email, decimal Amount, string Ref)> History => _history.ToArray();

    public void Clear()
    {
        _statuses.Clear();
        _history.Clear();
    }
}
