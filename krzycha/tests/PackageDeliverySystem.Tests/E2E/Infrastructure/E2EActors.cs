using PackageDeliverySystem.Core;

namespace PackageDeliverySystem.Tests.E2E.Infrastructure;

/// <summary>
/// Pre-seeded personas and infrastructure fixtures for opaque-box E2E testing.
/// </summary>
public static class E2EActors
{
    // Customers / Senders / Recipients
    public static readonly User Alice = new()
    {
        Id = 1,
        Name = "Alice",
        Surname = "Kowalska",
        Email = "alice@test.com",
        PhoneNumber = "+48123456789"
    };

    public static readonly User Bob = new()
    {
        Id = 2,
        Name = "Bob",
        Surname = "Nowak",
        Email = "bob@test.com",
        PhoneNumber = "+48987654321"
    };

    public static readonly User MerchantTechStore = new()
    {
        Id = 201,
        Name = "TechStore",
        Surname = "Retailer",
        Email = "support@techstore.com",
        PhoneNumber = "+48221100220"
    };

    public static readonly User CustomerAlice = new()
    {
        Id = 101,
        Name = "Alice",
        Surname = "Zielińska",
        Email = "alice.zielinska@test.com",
        PhoneNumber = "+48501111222"
    };

    public static readonly User David = new()
    {
        Id = 102,
        Name = "David",
        Surname = "Wisniewski",
        Email = "david@test.com",
        PhoneNumber = "+48502222333"
    };

    public static readonly User Eva = new()
    {
        Id = 103,
        Name = "Eva",
        Surname = "Lewandowska",
        Email = "eva@test.com",
        PhoneNumber = "+48503333444"
    };

    public static readonly User George = new()
    {
        Id = 104,
        Name = "George",
        Surname = "Kaminski",
        Email = "george@test.com",
        PhoneNumber = "+48504444555"
    };

    public static readonly User Hannah = new()
    {
        Id = 105,
        Name = "Hannah",
        Surname = "Dabrowska",
        Email = "hannah@test.com",
        PhoneNumber = "+48505555666"
    };

    public static readonly User Iris = new()
    {
        Id = 106,
        Name = "Iris",
        Surname = "Kozlowska",
        Email = "iris@test.com",
        PhoneNumber = "+48506666777"
    };

    public static readonly User Jack = new()
    {
        Id = 107,
        Name = "Jack",
        Surname = "Jankowski",
        Email = "jack@test.com",
        PhoneNumber = "+48507777888"
    };

    public static readonly User Mike = new()
    {
        Id = 108,
        Name = "Mike",
        Surname = "Wojcik",
        Email = "mike@test.com",
        PhoneNumber = "+48508888999"
    };

    public static readonly User Nina = new()
    {
        Id = 109,
        Name = "Nina",
        Surname = "Kowalczyk",
        Email = "nina@test.com",
        PhoneNumber = "+48509999000"
    };

    // Couriers
    public static readonly Courier CourierJan = new()
    {
        Id = 5,
        Name = "Jan",
        Surname = "Kurier",
        Email = "jan.kurier@pds.com",
        PhoneNumber = "+48600100200"
    };

    public static readonly Courier CourierDan = new()
    {
        Id = 4,
        Name = "Dan",
        Surname = "Dostawca",
        Email = "dan@pds.com",
        PhoneNumber = "+48600200300"
    };

    public static readonly Courier CourierDave = new()
    {
        Id = 8,
        Name = "Dave",
        Surname = "Przewoznik",
        Email = "dave@pds.com",
        PhoneNumber = "+48600300400"
    };

    public static readonly Courier CourierPiotr = new()
    {
        Id = 15,
        Name = "Piotr",
        Surname = "Szybki",
        Email = "piotr@pds.com",
        PhoneNumber = "+48600400500"
    };

    public static readonly Courier CourierBob = new()
    {
        Id = 301,
        Name = "Bob",
        Surname = "Courier",
        Email = "bob.courier@pds.com",
        PhoneNumber = "+48600500600"
    };

    public static readonly Courier CourierFrank = new()
    {
        Id = 302,
        Name = "Frank",
        Surname = "Express",
        Email = "frank@pds.com",
        PhoneNumber = "+48600600700"
    };

    public static readonly Courier CourierKevin = new()
    {
        Id = 303,
        Name = "Kevin",
        Surname = "Swift",
        Email = "kevin@pds.com",
        PhoneNumber = "+48600700800"
    };

    public static readonly Courier Oscar = new()
    {
        Id = 304,
        Name = "Oscar",
        Surname = "Transporter",
        Email = "oscar@pds.com",
        PhoneNumber = "+48600800900"
    };

    // Operators
    public static readonly Operator AdminAlice = new()
    {
        Id = 7,
        Name = "AdminAlice",
        Surname = "Operator",
        Email = "alice.admin@pds.com",
        PhoneNumber = "+48700100200"
    };

    public static readonly Operator OpBob = new()
    {
        Id = 3,
        Name = "OpBob",
        Surname = "Weryfikator",
        Email = "bob.op@pds.com",
        PhoneNumber = "+48700200300"
    };

    public static readonly Operator Charlie = new()
    {
        Id = 401,
        Name = "Charlie",
        Surname = "Manager",
        Email = "charlie@pds.com",
        PhoneNumber = "+48700300400"
    };

    public static readonly Operator Grace = new()
    {
        Id = 402,
        Name = "Grace",
        Surname = "Supervisor",
        Email = "grace@pds.com",
        PhoneNumber = "+48700400500"
    };

    public static readonly Operator Ivan = new()
    {
        Id = 403,
        Name = "Ivan",
        Surname = "Inspector",
        Email = "ivan@pds.com",
        PhoneNumber = "+48700500600"
    };

    public static readonly Operator Oleg = new()
    {
        Id = 30,
        Name = "Oleg",
        Surname = "Kontroler",
        Email = "oleg@pds.com",
        PhoneNumber = "+48700600700"
    };

    public static readonly Operator Stefan = new()
    {
        Id = 88,
        Name = "Stefan",
        Surname = "Audytor",
        Email = "stefan@pds.com",
        PhoneNumber = "+48700700800"
    };

    public static IEnumerable<User> GetAllUsers() => new[]
    {
        Alice, Bob, MerchantTechStore, CustomerAlice, David, Eva, George, Hannah, Iris, Jack, Mike, Nina
    };

    public static IEnumerable<Courier> GetAllCouriers() => new[]
    {
        CourierJan, CourierDan, CourierDave, CourierPiotr, CourierBob, CourierFrank, CourierKevin, Oscar
    };

    public static IEnumerable<Operator> GetAllOperators() => new[]
    {
        AdminAlice, OpBob, Charlie, Grace, Ivan, Oleg, Stefan
    };

    public static IEnumerable<Box> CreateDefaultBoxes()
    {
        return new List<Box>
        {
            // Origin Locker 10
            new() { Id = 101, ParcelLockerId = 10, Size = BoxSize.Medium },
            new() { Id = 102, ParcelLockerId = 10, Size = BoxSize.Small },
            new() { Id = 103, ParcelLockerId = 10, Size = BoxSize.Large },
            new() { Id = 104, ParcelLockerId = 10, Size = BoxSize.Small },
            new() { Id = 105, ParcelLockerId = 10, Size = BoxSize.Medium },
            new() { Id = 10,  ParcelLockerId = 10, Size = BoxSize.Medium },
            new() { Id = 11,  ParcelLockerId = 10, Size = BoxSize.Medium },
            new() { Id = 15,  ParcelLockerId = 10, Size = BoxSize.Large },
            new() { Id = 8,   ParcelLockerId = 10, Size = BoxSize.Medium },

            // Destination Locker 20
            new() { Id = 201, ParcelLockerId = 20, Size = BoxSize.Medium },
            new() { Id = 202, ParcelLockerId = 20, Size = BoxSize.Small },
            new() { Id = 203, ParcelLockerId = 20, Size = BoxSize.Large },
            new() { Id = 204, ParcelLockerId = 20, Size = BoxSize.Small },
            new() { Id = 205, ParcelLockerId = 20, Size = BoxSize.Medium },
            new() { Id = 21,  ParcelLockerId = 20, Size = BoxSize.Medium },
            new() { Id = 30,  ParcelLockerId = 20, Size = BoxSize.Medium },
            new() { Id = 45,  ParcelLockerId = 20, Size = BoxSize.Medium },

            // Return Locker 30
            new() { Id = 301, ParcelLockerId = 30, Size = BoxSize.Medium },
            new() { Id = 302, ParcelLockerId = 30, Size = BoxSize.Small },
            new() { Id = 303, ParcelLockerId = 30, Size = BoxSize.Large },
            new() { Id = 304, ParcelLockerId = 30, Size = BoxSize.Medium }
        };
    }
}
