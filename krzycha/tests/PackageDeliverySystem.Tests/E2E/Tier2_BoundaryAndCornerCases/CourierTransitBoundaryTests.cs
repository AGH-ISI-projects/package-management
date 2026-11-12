using PackageDeliverySystem.Core;
using PackageDeliverySystem.Tests.E2E.Infrastructure;
using Xunit;

namespace PackageDeliverySystem.Tests.E2E.Tier2_BoundaryAndCornerCases;

/// <summary>
/// Tier 2 Boundary & Corner Cases: Courier Transit & Delivery (State guards, sequence violations, invalid personnel).
/// </summary>
public class CourierTransitBoundaryTests : E2ETestBase
{
    [Fact]
    public async Task TC_T2_COU_01_CourierAssignmentProhibitedBeforeAccepted_Throws()
    {
        // Arrange
        var package = await CreateOrderedPackageAsync();
        var courier = new Courier { Id = 4, Name = "Dan" };

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => package.AssignCourier(courier));
        Assert.Equal("Cannot assign courier to package in status Ordered", ex.Message);
        Assert.Null(package.Courier);
    }

    [Fact]
    public async Task TC_T2_COU_02_OperatorAssignmentProhibitedBeforeAccepted_Throws()
    {
        // Arrange
        var package = await CreateOrderedPackageAsync();
        var op = new Operator { Id = 7, Name = "AdminAlice" };

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => package.AssignOperator(op));
        Assert.Equal("Cannot assign operator to package in status Ordered", ex.Message);
        Assert.Null(package.Operator);
    }

    [Fact]
    public async Task TC_T2_COU_03_InvalidStatusDeliveryAttempt_Throws()
    {
        // Arrange
        var package = await CreateAcceptedPackageAsync();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => package.Deliver());
        Assert.Equal("Cannot deliver package in status Accepted", ex.Message);
        Assert.Equal(PackageStatus.Accepted, package.Status);
    }

    [Fact]
    public async Task TC_T2_COU_04_SkippingPreparationStepProhibited_Throws()
    {
        // Arrange
        var package = await CreateAcceptedPackageAsync();

        // Act & Assert: Trying to Ship without Prepare()
        var ex = Assert.Throws<InvalidOperationException>(() => package.Ship());
        Assert.Equal("Cannot ship package in status Accepted", ex.Message);
        Assert.Equal(PackageStatus.Accepted, package.Status);
    }

    [Fact]
    public async Task TC_T2_COU_05_AssignmentOfNonExistentCourier_Throws()
    {
        // Arrange
        var package = await CreateAcceptedPackageAsync();
        int nonExistentCourierId = 7777;

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => Harness.FulfillmentService.AssignCourierAsync(package.Id, nonExistentCourierId));

        Assert.Contains("Courier not found", ex.Message);
        Assert.Null(package.Courier);
    }

    [Fact]
    public async Task TC_T2_COU_06_CourierAssignmentOnAlreadyShippedPackage_Throws()
    {
        // Arrange
        var package = await CreateShippedPackageAsync();
        var anotherCourier = new Courier { Id = 8, Name = "Dave" };

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => package.AssignCourier(anotherCourier));
        Assert.Equal("Cannot assign courier to package in status Shipped", ex.Message);
    }
}
