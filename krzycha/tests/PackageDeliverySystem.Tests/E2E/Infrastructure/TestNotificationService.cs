using System.Collections.Concurrent;
using PackageDeliverySystem.Core;

namespace PackageDeliverySystem.Tests.E2E.Infrastructure;

/// <summary>
/// In-memory notification sink for asserting emitted notifications in E2E tests.
/// </summary>
public class TestNotificationService : INotificationService
{
    private readonly ConcurrentBag<(string Recipient, string Message, DateTime Timestamp)> _notifications = new();

    public Task NotifyAsync(string userId, string message)
    {
        _notifications.Add((userId, message, DateTime.UtcNow));
        return Task.CompletedTask;
    }

    public IReadOnlyCollection<(string Recipient, string Message, DateTime Timestamp)> Notifications => _notifications.ToArray();

    public bool HasNotificationContaining(string substring, string? userId = null)
    {
        return _notifications.Any(n =>
            (userId == null || n.Recipient == userId) &&
            n.Message.Contains(substring, StringComparison.OrdinalIgnoreCase));
    }

    public void Clear()
    {
        _notifications.Clear();
    }
}
