using System.Collections.Concurrent;
using PackageDeliverySystem.Core;

namespace PackageDeliverySystem.Tests.E2E.Infrastructure;

/// <summary>
/// Thread-safe in-memory implementation of IPackageRepository for zero-DB E2E testing.
/// </summary>
public class InMemoryPackageRepository : IPackageRepository
{
    private readonly ConcurrentDictionary<int, Package> _packages = new();
    private int _nextId = 1;

    public Task<Package?> GetByIdAsync(int id)
    {
        _packages.TryGetValue(id, out var package);
        return Task.FromResult(package);
    }

    public Task AddAsync(Package package)
    {
        if (package.Id == 0)
        {
            package.Id = Interlocked.Increment(ref _nextId);
        }

        _packages[package.Id] = package;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Package package)
    {
        _packages[package.Id] = package;
        return Task.CompletedTask;
    }

    public Task<IEnumerable<Package>> GetAllAsync()
    {
        return Task.FromResult<IEnumerable<Package>>(_packages.Values.ToList());
    }

    public Task<IEnumerable<Package>> GetPackagesRequiringExpirationAsync()
    {
        var now = DateTime.UtcNow;
        var expired = _packages.Values
            .Where(p => (p.Status == PackageStatus.Shipped || p.Status == PackageStatus.Delivered)
                        && p.ExpiredAt.HasValue
                        && p.ExpiredAt.Value <= now)
            .ToList();

        return Task.FromResult<IEnumerable<Package>>(expired);
    }

    public Task<IEnumerable<Package>> GetPackagesWithMissedDepositAsync()
    {
        var now = DateTime.UtcNow;
        var missed = _packages.Values
            .Where(p => p.Status == PackageStatus.Ordered
                        && p.DepositConfirmedAt == null
                        && p.DepositDeadline <= now)
            .ToList();

        return Task.FromResult<IEnumerable<Package>>(missed);
    }

    public Task<IEnumerable<Package>> GetByOrderGroupIdAsync(int orderGroupId)
    {
        var grouped = _packages.Values
            .Where(p => p.OrderGroupId == orderGroupId)
            .ToList();

        return Task.FromResult<IEnumerable<Package>>(grouped);
    }

    public Task<IEnumerable<Package>> GetByRecipientIdAsync(int recipientId)
    {
        var packages = _packages.Values
            .Where(p => p.RecipientId == recipientId)
            .ToList();

        return Task.FromResult<IEnumerable<Package>>(packages);
    }

    public void Seed(Package package)
    {
        if (package.Id == 0)
        {
            package.Id = Interlocked.Increment(ref _nextId);
        }
        _packages[package.Id] = package;
    }

    public void Clear()
    {
        _packages.Clear();
    }
}
