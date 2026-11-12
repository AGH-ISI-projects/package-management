using Microsoft.EntityFrameworkCore;
using PackageDeliverySystem.Core;
using PackageDeliverySystem.Infrastructure.Data;

namespace PackageDeliverySystem.Infrastructure;

/// <summary>
/// Entity Framework Core repository implementation for Return domain entities.
/// Encapsulates querying, relationship loading, and persistence operations against DeliveryDbContext.
/// </summary>
public class ReturnRepository : IReturnRepository
{
    private readonly DeliveryDbContext _context;

    private static readonly ReturnStatus[] ActiveStatuses =
    {
        ReturnStatus.Requested,
        ReturnStatus.Approved,
        ReturnStatus.Shipped
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="ReturnRepository"/> class.
    /// </summary>
    /// <param name="context">The database context.</param>
    public ReturnRepository(DeliveryDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Retrieves a return entity by its unique identifier, including Package, Payment, and locker boxes.
    /// Returns null if no return is found with the specified ID.
    /// </summary>
    public async Task<Return?> GetByIdAsync(int id)
    {
        return await _context.Returns
            .Include(r => r.Package)
                .ThenInclude(p => p.Payment)
            .Include(r => r.ReturnBox)
            .Include(r => r.ReturnDestinationBox)
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    /// <summary>
    /// Adds a new return entity to the database and persists changes.
    /// </summary>
    public async Task AddAsync(Return returnEntity)
    {
        ArgumentNullException.ThrowIfNull(returnEntity);

        await _context.Returns.AddAsync(returnEntity);
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Updates an existing return entity's status and properties and persists changes.
    /// </summary>
    public async Task UpdateAsync(Return returnEntity)
    {
        ArgumentNullException.ThrowIfNull(returnEntity);

        _context.Returns.Update(returnEntity);
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Deletes a return entity by its unique identifier.
    /// Completes idempotently without throwing if the entity is not found.
    /// </summary>
    public async Task DeleteAsync(int id)
    {
        var returnEntity = await _context.Returns.FindAsync(id);
        if (returnEntity != null)
        {
            _context.Returns.Remove(returnEntity);
            await _context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Retrieves all return records in the system with full navigation graphs.
    /// </summary>
    public async Task<IEnumerable<Return>> GetAllAsync()
    {
        return await _context.Returns
            .Include(r => r.Package)
                .ThenInclude(p => p.Payment)
            .Include(r => r.ReturnBox)
            .Include(r => r.ReturnDestinationBox)
            .ToListAsync();
    }

    /// <summary>
    /// Retrieves all return requests associated with the specified package.
    /// </summary>
    public async Task<IEnumerable<Return>> GetByPackageIdAsync(int packageId)
    {
        return await _context.Returns
            .Include(r => r.Package)
                .ThenInclude(p => p.Payment)
            .Include(r => r.ReturnBox)
            .Include(r => r.ReturnDestinationBox)
            .Where(r => r.PackageId == packageId)
            .ToListAsync();
    }

    /// <summary>
    /// Retrieves active returns (Requested, Approved, Shipped) for the specified package.
    /// Used to enforce duplicate return guards.
    /// </summary>
    public async Task<IEnumerable<Return>> GetActiveReturnsByPackageIdAsync(int packageId)
    {
        return await _context.Returns
            .Include(r => r.Package)
                .ThenInclude(p => p.Payment)
            .Include(r => r.ReturnBox)
            .Include(r => r.ReturnDestinationBox)
            .Where(r => r.PackageId == packageId && ActiveStatuses.Contains(r.Status))
            .ToListAsync();
    }

    /// <summary>
    /// Retrieves returns filtered by their current lifecycle status.
    /// </summary>
    public async Task<IEnumerable<Return>> GetByStatusAsync(ReturnStatus status)
    {
        return await _context.Returns
            .Include(r => r.Package)
                .ThenInclude(p => p.Payment)
            .Include(r => r.ReturnBox)
            .Include(r => r.ReturnDestinationBox)
            .Where(r => r.Status == status)
            .ToListAsync();
    }
}
