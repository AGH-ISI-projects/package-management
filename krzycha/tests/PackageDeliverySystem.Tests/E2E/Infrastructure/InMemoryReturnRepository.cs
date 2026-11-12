using System.Collections.Concurrent;
using PackageDeliverySystem.Core;

namespace PackageDeliverySystem.Tests.E2E.Infrastructure;

/// <summary>
/// Thread-safe in-memory implementation of IReturnRepository for zero-DB E2E testing.
/// </summary>
public class InMemoryReturnRepository : IReturnRepository
{
    private readonly ConcurrentDictionary<int, Return> _returns = new();
    private int _nextId = 1;

    public Task<Return?> GetByIdAsync(int id)
    {
        _returns.TryGetValue(id, out var returnEntity);
        return Task.FromResult(returnEntity);
    }

    public Task AddAsync(Return returnEntity)
    {
        if (returnEntity.Id == 0)
        {
            returnEntity.Id = Interlocked.Increment(ref _nextId);
        }

        _returns[returnEntity.Id] = returnEntity;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Return returnEntity)
    {
        _returns[returnEntity.Id] = returnEntity;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int id)
    {
        _returns.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    public Task<IEnumerable<Return>> GetAllAsync()
    {
        return Task.FromResult<IEnumerable<Return>>(_returns.Values.ToList());
    }

    public Task<IEnumerable<Return>> GetByPackageIdAsync(int packageId)
    {
        var results = _returns.Values
            .Where(r => r.PackageId == packageId)
            .ToList();

        return Task.FromResult<IEnumerable<Return>>(results);
    }

    public Task<IEnumerable<Return>> GetActiveReturnsByPackageIdAsync(int packageId)
    {
        var active = _returns.Values
            .Where(r => r.PackageId == packageId &&
                        (r.Status == ReturnStatus.Requested ||
                         r.Status == ReturnStatus.Approved ||
                         r.Status == ReturnStatus.Shipped))
            .ToList();

        return Task.FromResult<IEnumerable<Return>>(active);
    }

    public Task<IEnumerable<Return>> GetByStatusAsync(ReturnStatus status)
    {
        var results = _returns.Values
            .Where(r => r.Status == status)
            .ToList();

        return Task.FromResult<IEnumerable<Return>>(results);
    }

    public void Seed(Return returnEntity)
    {
        if (returnEntity.Id == 0)
        {
            returnEntity.Id = Interlocked.Increment(ref _nextId);
        }
        _returns[returnEntity.Id] = returnEntity;
    }

    public void Clear()
    {
        _returns.Clear();
    }
}
