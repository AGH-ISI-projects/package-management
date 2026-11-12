using System.Collections.Concurrent;
using PackageDeliverySystem.Core;

namespace PackageDeliverySystem.Tests.E2E.Infrastructure;

/// <summary>
/// Thread-safe in-memory implementation of IBoxRepository for zero-DB E2E testing.
/// </summary>
public class InMemoryBoxRepository : IBoxRepository
{
    private readonly ConcurrentDictionary<int, Box> _boxes = new();
    private readonly InMemoryPackageRepository? _packageRepository;
    private int _nextId = 1;

    public InMemoryBoxRepository(InMemoryPackageRepository? packageRepository = null)
    {
        _packageRepository = packageRepository;
    }

    public Task<Box?> GetByIdAsync(int boxId)
    {
        _boxes.TryGetValue(boxId, out var box);
        return Task.FromResult(box);
    }

    public async Task<Box?> FindFreeBoxAsync(int parcelLockerId, BoxSize size, int? excludeBoxId = null)
    {
        var occupiedBoxIds = new HashSet<int>();

        if (_packageRepository != null)
        {
            var allPackages = await _packageRepository.GetAllAsync();
            foreach (var pkg in allPackages)
            {
                // Active packages occupy their assigned boxes
                if (pkg.Status != PackageStatus.Collected &&
                    pkg.Status != PackageStatus.Cancelled &&
                    pkg.Status != PackageStatus.Rejected &&
                    pkg.Status != PackageStatus.Returned &&
                    pkg.Status != PackageStatus.ReturnedToSender)
                {
                    if (pkg.BoxId.HasValue)
                        occupiedBoxIds.Add(pkg.BoxId.Value);
                    if (pkg.DestinationBoxId.HasValue)
                        occupiedBoxIds.Add(pkg.DestinationBoxId.Value);
                }
            }
        }

        var candidate = _boxes.Values.FirstOrDefault(b =>
            b.ParcelLockerId == parcelLockerId &&
            b.Size == size &&
            (!excludeBoxId.HasValue || b.Id != excludeBoxId.Value) &&
            !occupiedBoxIds.Contains(b.Id));

        return candidate;
    }

    public void Seed(Box box)
    {
        if (box.Id == 0)
        {
            box.Id = Interlocked.Increment(ref _nextId);
        }
        _boxes[box.Id] = box;
    }

    public void SeedRange(IEnumerable<Box> boxes)
    {
        foreach (var box in boxes)
        {
            Seed(box);
        }
    }

    public void Clear()
    {
        _boxes.Clear();
    }
}
