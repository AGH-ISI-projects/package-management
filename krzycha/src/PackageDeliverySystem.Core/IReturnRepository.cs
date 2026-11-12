namespace PackageDeliverySystem.Core;

/// <summary>
/// Persistence contract for Return domain entities.
/// Encapsulates query and mutation operations for returns against the persistent data store.
/// </summary>
public interface IReturnRepository
{
    /// <summary>
    /// Retrieves a return entity by its unique primary key identifier.
    /// Returns null if no return is found with the specified ID.
    /// </summary>
    Task<Return?> GetByIdAsync(int id);

    /// <summary>
    /// Adds a new return entity to the database and persists changes.
    /// </summary>
    Task AddAsync(Return returnEntity);

    /// <summary>
    /// Updates an existing return entity's status and properties and persists changes.
    /// </summary>
    Task UpdateAsync(Return returnEntity);

    /// <summary>
    /// Deletes a return entity by its unique identifier.
    /// </summary>
    Task DeleteAsync(int id);

    /// <summary>
    /// Retrieves all return records in the system.
    /// </summary>
    Task<IEnumerable<Return>> GetAllAsync();

    /// <summary>
    /// Retrieves all return requests associated with the specified package.
    /// </summary>
    Task<IEnumerable<Return>> GetByPackageIdAsync(int packageId);

    /// <summary>
    /// Retrieves active returns (Requested, Approved, Shipped) for the specified package.
    /// Used to enforce duplicate return guards.
    /// </summary>
    Task<IEnumerable<Return>> GetActiveReturnsByPackageIdAsync(int packageId);

    /// <summary>
    /// Retrieves returns filtered by their current lifecycle status.
    /// </summary>
    Task<IEnumerable<Return>> GetByStatusAsync(ReturnStatus status);
}
