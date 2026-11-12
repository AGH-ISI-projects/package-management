using System.Collections.Concurrent;
using PackageDeliverySystem.Core;

namespace PackageDeliverySystem.Tests.E2E.Infrastructure;

/// <summary>
/// Thread-safe in-memory implementation of IPeopleService for zero-DB E2E testing.
/// </summary>
public class InMemoryPeopleService : IPeopleService
{
    private readonly ConcurrentDictionary<int, User> _users = new();
    private readonly ConcurrentDictionary<int, Courier> _couriers = new();
    private readonly ConcurrentDictionary<int, Operator> _operators = new();

    public Task<User?> GetUserAsync(int userId)
    {
        _users.TryGetValue(userId, out var user);
        return Task.FromResult(user);
    }

    public Task<Courier?> GetCourierAsync(int courierId)
    {
        _couriers.TryGetValue(courierId, out var courier);
        return Task.FromResult(courier);
    }

    public Task<Operator?> GetOperatorAsync(int operatorId)
    {
        _operators.TryGetValue(operatorId, out var op);
        return Task.FromResult(op);
    }

    public Task UpdateRankAsync(int personId, int rank)
    {
        if (_users.TryGetValue(personId, out var user))
        {
            user.Rank = rank;
            return Task.CompletedTask;
        }

        if (_couriers.TryGetValue(personId, out var courier))
        {
            courier.Rank = rank;
            return Task.CompletedTask;
        }

        if (_operators.TryGetValue(personId, out var op))
        {
            op.Rank = rank;
            return Task.CompletedTask;
        }

        return Task.CompletedTask;
    }

    public void SeedUser(User user) => _users[user.Id] = user;
    public void SeedCourier(Courier courier) => _couriers[courier.Id] = courier;
    public void SeedOperator(Operator op) => _operators[op.Id] = op;

    public void Clear()
    {
        _users.Clear();
        _couriers.Clear();
        _operators.Clear();
    }
}
