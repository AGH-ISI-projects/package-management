using Moq;
using PackageDeliverySystem.Core;
using PackageDeliverySystem.Infrastructure;
using Xunit;

namespace PackageDeliverySystem.Tests;

public class ReturnTests
{
    // =========================================================================
    // TEST DATA FACTORIES & HELPERS
    // =========================================================================

    private static void MakeReadyForAcceptance(Package package)
    {
        package.ConfirmDeposit();
        package.Payment = new Payment
        {
            PackageId = package.Id,
            PayerEmail = "customer@example.com",
            Amount = 29.99m
        };
        package.Payment.Complete();
    }

    private static Package CreateCollectedPackage(
        int packageId = 1,
        int recipientId = 2,
        int? orderGroupId = null,
        decimal amount = 29.99m)
    {
        var package = new Package
        {
            Id = packageId,
            SenderId = 10,
            RecipientId = recipientId,
            BoxId = 101,
            DestinationBoxId = 202,
            OrderGroupId = orderGroupId
        };

        MakeReadyForAcceptance(package);
        package.Accept();
        package.Prepare();
        package.Ship();
        package.Deliver();
        package.Collect(package.PickupCode!);
        return package;
    }

    private static (
        Mock<IPackageRepository> PackageRepo,
        Mock<IReturnRepository> ReturnRepo,
        Mock<IPeopleService> PeopleService,
        Mock<INotificationService> NotificationService,
        ReturnService Service) CreateReturnServiceContext()
    {
        var packageRepo = new Mock<IPackageRepository>();
        var returnRepo = new Mock<IReturnRepository>();
        var peopleService = new Mock<IPeopleService>();
        var notificationService = new Mock<INotificationService>();

        var service = new ReturnService(
            packageRepo.Object,
            returnRepo.Object,
            peopleService.Object,
            notificationService.Object);

        return (packageRepo, returnRepo, peopleService, notificationService, service);
    }

    // =========================================================================
    // R1: Valid Return Request After Package Collection
    // =========================================================================

    [Fact]
    public async Task R01_RequestReturnAsync_WhenPackageCollected_CreatesReturnAndTransitionsToReturnRequested()
    {
        // Arrange
        var (packageRepo, returnRepo, _, notificationService, returnService) = CreateReturnServiceContext();
        var package = CreateCollectedPackage(packageId: 1, recipientId: 42);

        packageRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);
        returnRepo.Setup(r => r.GetActiveReturnsByPackageIdAsync(1)).ReturnsAsync(new List<Return>());

        // Act
        var returnEntity = await returnService.RequestReturnAsync(1, 42, "Item defective");

        // Assert
        Assert.NotNull(returnEntity);
        Assert.Equal(1, returnEntity.PackageId);
        Assert.Equal(ReturnStatus.Requested, returnEntity.Status);
        Assert.Equal("Item defective", returnEntity.Reason);
        Assert.True((DateTime.UtcNow - returnEntity.RequestedAt).TotalSeconds < 5);

        Assert.Equal(PackageStatus.ReturnRequested, package.Status);
        Assert.Same(returnEntity, package.Return);

        returnRepo.Verify(r => r.AddAsync(returnEntity), Times.Once);
        packageRepo.Verify(r => r.UpdateAsync(package), Times.Once);
        notificationService.Verify(n => n.NotifyAsync("system",
            It.Is<string>(msg => msg.Contains("Return request") && msg.Contains("package 1"))), Times.Once);
    }

    [Fact]
    public async Task R01_RequestReturnAsync_OverloadWithoutCustomerId_UsesPackageRecipientId()
    {
        // Arrange
        var (packageRepo, returnRepo, _, _, returnService) = CreateReturnServiceContext();
        var package = CreateCollectedPackage(packageId: 2, recipientId: 77);

        packageRepo.Setup(r => r.GetByIdAsync(2)).ReturnsAsync(package);
        returnRepo.Setup(r => r.GetActiveReturnsByPackageIdAsync(2)).ReturnsAsync(new List<Return>());

        // Act
        var returnEntity = await returnService.RequestReturnAsync(2, "Implicit recipient return");

        // Assert
        Assert.NotNull(returnEntity);
        Assert.Equal(2, returnEntity.PackageId);
        Assert.Equal(PackageStatus.ReturnRequested, package.Status);
        returnRepo.Verify(r => r.AddAsync(returnEntity), Times.Once);
        packageRepo.Verify(r => r.UpdateAsync(package), Times.Once);
    }

    [Fact]
    public void R01_Domain_PackageRequestReturn_WhenCollected_TransitionsToReturnRequested()
    {
        // Arrange
        var package = CreateCollectedPackage();
        Assert.Equal(PackageStatus.Collected, package.Status);

        // Act
        package.RequestReturn();

        // Assert
        Assert.Equal(PackageStatus.ReturnRequested, package.Status);
    }

    // =========================================================================
    // R2: Return Request Blocked Before Collection
    // =========================================================================

    [Theory]
    [InlineData(PackageStatus.Ordered)]
    [InlineData(PackageStatus.Accepted)]
    [InlineData(PackageStatus.Preparing)]
    [InlineData(PackageStatus.Shipped)]
    [InlineData(PackageStatus.Delivered)]
    public async Task R02_RequestReturnAsync_WhenPackageNotCollected_ThrowsInvalidOperationException(PackageStatus initialStatus)
    {
        // Arrange
        var (packageRepo, returnRepo, _, _, returnService) = CreateReturnServiceContext();
        var package = new Package { Id = 1, RecipientId = 42 };

        if (initialStatus != PackageStatus.Ordered)
        {
            MakeReadyForAcceptance(package);
            package.Accept();

            if (initialStatus == PackageStatus.Preparing || initialStatus == PackageStatus.Shipped || initialStatus == PackageStatus.Delivered)
                package.Prepare();

            if (initialStatus == PackageStatus.Shipped || initialStatus == PackageStatus.Delivered)
                package.Ship();

            if (initialStatus == PackageStatus.Delivered)
                package.Deliver();
        }

        Assert.Equal(initialStatus, package.Status);
        packageRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            returnService.RequestReturnAsync(1, 42, "Premature return"));

        Assert.Contains("Only collected packages can be returned", ex.Message);
        Assert.Equal(initialStatus, package.Status);
        returnRepo.Verify(r => r.AddAsync(It.IsAny<Return>()), Times.Never);
        packageRepo.Verify(r => r.UpdateAsync(It.IsAny<Package>()), Times.Never);
    }

    [Fact]
    public async Task R02_RequestReturnAsync_WhenUserIsNotRecipient_ThrowsInvalidOperationException()
    {
        // Arrange
        var (packageRepo, returnRepo, _, _, returnService) = CreateReturnServiceContext();
        var package = CreateCollectedPackage(packageId: 1, recipientId: 42);
        packageRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            returnService.RequestReturnAsync(1, customerId: 99, reason: "Unauthorized return"));

        Assert.Equal("Only the recipient can request a return", ex.Message);
        Assert.Equal(PackageStatus.Collected, package.Status);
        returnRepo.Verify(r => r.AddAsync(It.IsAny<Return>()), Times.Never);
        packageRepo.Verify(r => r.UpdateAsync(It.IsAny<Package>()), Times.Never);
    }

    [Fact]
    public async Task R02_RequestReturnAsync_WhenPackageNotFound_ThrowsArgumentException()
    {
        // Arrange
        var (packageRepo, returnRepo, _, _, returnService) = CreateReturnServiceContext();
        packageRepo.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((Package?)null);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            returnService.RequestReturnAsync(999, 42, "Missing package"));

        Assert.Equal("Package not found", ex.Message);
        returnRepo.Verify(r => r.AddAsync(It.IsAny<Return>()), Times.Never);
    }

    [Fact]
    public void R02_Domain_PackageRequestReturn_WhenNotCollected_ThrowsInvalidOperationException()
    {
        // Arrange
        var package = new Package { Id = 1 };
        Assert.Equal(PackageStatus.Ordered, package.Status);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => package.RequestReturn());
        Assert.Contains("Only collected packages can be returned", ex.Message);
    }

    // =========================================================================
    // R3: Operator Return Approval
    // =========================================================================

    [Fact]
    public async Task R03_ApproveReturnAsync_ByOperator_AssignsBoxesAndTransitionsToReturnInProgress()
    {
        // Arrange
        var (packageRepo, returnRepo, peopleService, notificationService, returnService) = CreateReturnServiceContext();
        var package = CreateCollectedPackage(packageId: 1);
        package.RequestReturn();

        var returnEntity = new Return
        {
            Id = 10,
            PackageId = 1,
            Package = package,
            Reason = "Defective part"
        };
        package.Return = returnEntity;

        var operatorUser = new Operator { Id = 5, Name = "Alice" };

        returnRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(returnEntity);
        peopleService.Setup(p => p.GetOperatorAsync(5)).ReturnsAsync(operatorUser);

        // Act
        await returnService.ApproveReturnAsync(10, 5, returnBoxId: 301, returnDestBoxId: 401);

        // Assert
        Assert.Equal(ReturnStatus.Approved, returnEntity.Status);
        Assert.NotNull(returnEntity.ApprovedAt);
        Assert.Equal(301, returnEntity.ReturnBoxId);
        Assert.Equal(401, returnEntity.ReturnDestinationBoxId);
        Assert.Equal(PackageStatus.ReturnInProgress, package.Status);

        returnRepo.Verify(r => r.UpdateAsync(returnEntity), Times.Once);
        packageRepo.Verify(r => r.UpdateAsync(package), Times.Once);
        notificationService.Verify(n => n.NotifyAsync("system",
            It.Is<string>(msg => msg.Contains("Return 10 approved by operator Alice"))), Times.Once);
    }

    [Fact]
    public async Task R03_ApproveReturnAsync_OverloadWithOptionalDestBox_AssignsDestinationBox()
    {
        // Arrange
        var (packageRepo, returnRepo, peopleService, notificationService, returnService) = CreateReturnServiceContext();
        var package = CreateCollectedPackage(packageId: 1);
        package.RequestReturn();

        var returnEntity = new Return
        {
            Id = 11,
            PackageId = 1,
            Package = package
        };
        package.Return = returnEntity;

        var operatorUser = new Operator { Id = 5, Name = "Alice" };

        returnRepo.Setup(r => r.GetByIdAsync(11)).ReturnsAsync(returnEntity);
        peopleService.Setup(p => p.GetOperatorAsync(5)).ReturnsAsync(operatorUser);

        // Act
        await returnService.ApproveReturnAsync(11, 5, destinationBoxId: 505);

        // Assert
        Assert.Equal(ReturnStatus.Approved, returnEntity.Status);
        Assert.Null(returnEntity.ReturnBoxId);
        Assert.Equal(505, returnEntity.ReturnDestinationBoxId);
        Assert.Equal(PackageStatus.ReturnInProgress, package.Status);
    }

    [Fact]
    public async Task R03_ApproveReturnAsync_WhenOperatorNotFound_ThrowsArgumentException()
    {
        // Arrange
        var (_, returnRepo, peopleService, _, returnService) = CreateReturnServiceContext();
        var returnEntity = new Return { Id = 10, PackageId = 1 };

        returnRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(returnEntity);
        peopleService.Setup(p => p.GetOperatorAsync(99)).ReturnsAsync((Operator?)null);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            returnService.ApproveReturnAsync(10, 99, 100, 200));

        Assert.Equal("Operator not found", ex.Message);
    }

    [Fact]
    public void R03_Domain_ReturnApprove_AssignsBoxesAndPopulatesApprovedAt()
    {
        // Arrange
        var returnEntity = new Return { Id = 1 };
        Assert.Equal(ReturnStatus.Requested, returnEntity.Status);

        // Act
        returnEntity.Approve(returnBoxId: 50, returnDestinationBoxId: 60);

        // Assert
        Assert.Equal(ReturnStatus.Approved, returnEntity.Status);
        Assert.NotNull(returnEntity.ApprovedAt);
        Assert.Equal(50, returnEntity.ReturnBoxId);
        Assert.Equal(60, returnEntity.ReturnDestinationBoxId);
    }

    [Fact]
    public void R03_Domain_ReturnApprove_WhenNotRequested_ThrowsInvalidOperationException()
    {
        // Arrange
        var returnEntity = new Return { Id = 1 };
        returnEntity.Approve(10, 20);
        Assert.Equal(ReturnStatus.Approved, returnEntity.Status);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => returnEntity.Approve(30, 40));
        Assert.Contains("Cannot approve return in status Approved", ex.Message);
    }

    [Fact]
    public void R03_Domain_PackageApproveReturn_WhenNotReturnRequested_ThrowsInvalidOperationException()
    {
        // Arrange
        var package = CreateCollectedPackage();
        Assert.Equal(PackageStatus.Collected, package.Status);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => package.ApproveReturn());
        Assert.Contains("Cannot approve return for package in status Collected", ex.Message);
    }

    // =========================================================================
    // R4: Operator Return Rejection
    // =========================================================================

    [Fact]
    public async Task R04_RejectReturnAsync_ByOperator_RevertsPackageStatusToCollectedAndSetsRejected()
    {
        // Arrange
        var (packageRepo, returnRepo, peopleService, notificationService, returnService) = CreateReturnServiceContext();
        var package = CreateCollectedPackage(packageId: 1);
        package.RequestReturn();

        var returnEntity = new Return
        {
            Id = 20,
            PackageId = 1,
            Package = package,
            Reason = "No longer wanted"
        };
        package.Return = returnEntity;

        var operatorUser = new Operator { Id = 7, Name = "Bob" };

        returnRepo.Setup(r => r.GetByIdAsync(20)).ReturnsAsync(returnEntity);
        peopleService.Setup(p => p.GetOperatorAsync(7)).ReturnsAsync(operatorUser);

        // Act
        await returnService.RejectReturnAsync(20, 7, "Return window expired");

        // Assert
        Assert.Equal(ReturnStatus.Rejected, returnEntity.Status);
        Assert.Equal(PackageStatus.Collected, package.Status);

        returnRepo.Verify(r => r.UpdateAsync(returnEntity), Times.Once);
        packageRepo.Verify(r => r.UpdateAsync(package), Times.Once);
        notificationService.Verify(n => n.NotifyAsync("system",
            It.Is<string>(msg => msg.Contains("Return 20 rejected by operator Bob"))), Times.Once);
    }

    [Fact]
    public async Task R04_RejectReturnAsync_OverloadWithoutReason_Succeeds()
    {
        // Arrange
        var (packageRepo, returnRepo, peopleService, _, returnService) = CreateReturnServiceContext();
        var package = CreateCollectedPackage(packageId: 1);
        package.RequestReturn();

        var returnEntity = new Return { Id = 21, PackageId = 1, Package = package };
        package.Return = returnEntity;

        var operatorUser = new Operator { Id = 7, Name = "Bob" };
        returnRepo.Setup(r => r.GetByIdAsync(21)).ReturnsAsync(returnEntity);
        peopleService.Setup(p => p.GetOperatorAsync(7)).ReturnsAsync(operatorUser);

        // Act
        await returnService.RejectReturnAsync(21, 7);

        // Assert
        Assert.Equal(ReturnStatus.Rejected, returnEntity.Status);
        Assert.Equal(PackageStatus.Collected, package.Status);
    }

    [Fact]
    public void R04_Domain_ReturnReject_AndPackageRejectReturn_TransitionsCorrectly()
    {
        // Arrange
        var package = CreateCollectedPackage();
        package.RequestReturn();
        var returnEntity = new Return { Id = 1 };

        // Act
        returnEntity.Reject();
        package.RejectReturn();

        // Assert
        Assert.Equal(ReturnStatus.Rejected, returnEntity.Status);
        Assert.Equal(PackageStatus.Collected, package.Status);
    }

    [Fact]
    public void R04_Domain_ReturnReject_WhenNotRequested_ThrowsInvalidOperationException()
    {
        // Arrange
        var returnEntity = new Return { Id = 1 };
        returnEntity.Approve(10, 20);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => returnEntity.Reject());
        Assert.Contains("Cannot reject return in status Approved", ex.Message);
    }

    [Fact]
    public void R04_Domain_PackageRejectReturn_WhenNotReturnRequested_ThrowsInvalidOperationException()
    {
        // Arrange
        var package = CreateCollectedPackage();
        Assert.Equal(PackageStatus.Collected, package.Status);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => package.RejectReturn());
        Assert.Contains("Cannot reject return for package in status Collected", ex.Message);
    }

    // =========================================================================
    // R5: Courier Return Shipping and Completion
    // =========================================================================

    [Fact]
    public async Task R05_ShipAndCompleteReturnAsync_ByCourier_TransitionsThroughShippedToReturned()
    {
        // Arrange
        var (packageRepo, returnRepo, peopleService, notificationService, returnService) = CreateReturnServiceContext();
        var package = CreateCollectedPackage(packageId: 1);
        package.RequestReturn();
        package.ApproveReturn();

        var returnEntity = new Return
        {
            Id = 30,
            PackageId = 1,
            Package = package
        };
        returnEntity.Approve(returnBoxId: 11, returnDestinationBoxId: 22);
        package.Return = returnEntity;

        var courierUser = new Courier { Id = 15, Name = "Jan" };

        returnRepo.Setup(r => r.GetByIdAsync(30)).ReturnsAsync(returnEntity);
        peopleService.Setup(p => p.GetCourierAsync(15)).ReturnsAsync(courierUser);

        // Act 1: Courier ships the return
        await returnService.ShipReturnAsync(30, 15, boxId: 11);

        // Assert 1: Shipped
        Assert.Equal(ReturnStatus.Shipped, returnEntity.Status);
        Assert.Equal(PackageStatus.ReturnInProgress, package.Status);
        returnRepo.Verify(r => r.UpdateAsync(returnEntity), Times.Once);
        notificationService.Verify(n => n.NotifyAsync("system",
            It.Is<string>(msg => msg.Contains("Return 30 shipped by courier Jan"))), Times.Once);

        // Act 2: Courier / system completes the return
        await returnService.CompleteReturnAsync(30);

        // Assert 2: Completed
        Assert.Equal(ReturnStatus.Completed, returnEntity.Status);
        Assert.NotNull(returnEntity.CompletedAt);
        Assert.Equal(PackageStatus.Returned, package.Status);
        returnRepo.Verify(r => r.UpdateAsync(returnEntity), Times.Exactly(2));
        packageRepo.Verify(r => r.UpdateAsync(package), Times.Exactly(2));
        notificationService.Verify(n => n.NotifyAsync("system",
            It.Is<string>(msg => msg.Contains("Return 30 completed"))), Times.Once);
    }

    [Fact]
    public async Task R05_ShipReturnAsync_OverloadWithoutBoxId_TransitionsToShipped()
    {
        // Arrange
        var (packageRepo, returnRepo, peopleService, _, returnService) = CreateReturnServiceContext();
        var package = CreateCollectedPackage(packageId: 1);
        package.RequestReturn();
        package.ApproveReturn();

        var returnEntity = new Return { Id = 31, PackageId = 1, Package = package };
        returnEntity.Approve(10, 20);
        package.Return = returnEntity;

        var courierUser = new Courier { Id = 15, Name = "Jan" };
        returnRepo.Setup(r => r.GetByIdAsync(31)).ReturnsAsync(returnEntity);
        peopleService.Setup(p => p.GetCourierAsync(15)).ReturnsAsync(courierUser);

        // Act
        await returnService.ShipReturnAsync(31, 15);

        // Assert
        Assert.Equal(ReturnStatus.Shipped, returnEntity.Status);
        Assert.Equal(PackageStatus.ReturnInProgress, package.Status);
    }

    [Fact]
    public async Task R05_ShipReturnAsync_WhenCourierNotFound_ThrowsArgumentException()
    {
        // Arrange
        var (_, returnRepo, peopleService, _, returnService) = CreateReturnServiceContext();
        var returnEntity = new Return { Id = 30, PackageId = 1 };
        returnRepo.Setup(r => r.GetByIdAsync(30)).ReturnsAsync(returnEntity);
        peopleService.Setup(p => p.GetCourierAsync(999)).ReturnsAsync((Courier?)null);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            returnService.ShipReturnAsync(30, 999));

        Assert.Equal("Courier not found", ex.Message);
    }

    [Fact]
    public void R05_Domain_ReturnShipAndComplete_TransitionsStatusAndSetsTimestamps()
    {
        // Arrange
        var package = CreateCollectedPackage();
        package.RequestReturn();
        package.ApproveReturn();

        var returnEntity = new Return { Id = 1 };
        returnEntity.Approve(10, 20);

        // Act 1: Ship
        returnEntity.Ship(10);
        package.ShipReturn();

        // Assert 1
        Assert.Equal(ReturnStatus.Shipped, returnEntity.Status);
        Assert.Equal(PackageStatus.ReturnInProgress, package.Status);

        // Act 2: Complete
        returnEntity.Complete();
        package.CompleteReturn();

        // Assert 2
        Assert.Equal(ReturnStatus.Completed, returnEntity.Status);
        Assert.NotNull(returnEntity.CompletedAt);
        Assert.Equal(PackageStatus.Returned, package.Status);
    }

    [Fact]
    public void R05_Domain_ReturnShip_WhenNotApproved_ThrowsInvalidOperationException()
    {
        // Arrange
        var returnEntity = new Return { Id = 1 };
        Assert.Equal(ReturnStatus.Requested, returnEntity.Status);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => returnEntity.Ship(10));
        Assert.Contains("Cannot ship return in status Requested", ex.Message);
    }

    [Fact]
    public void R05_Domain_ReturnComplete_WhenRequested_ThrowsInvalidOperationException()
    {
        // Arrange
        var returnEntity = new Return { Id = 1 };
        Assert.Equal(ReturnStatus.Requested, returnEntity.Status);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => returnEntity.Complete());
        Assert.Contains("Cannot complete return in status Requested", ex.Message);
    }

    [Fact]
    public void R05_Domain_PackageCompleteReturn_WhenNotReturnInProgress_ThrowsInvalidOperationException()
    {
        // Arrange
        var package = CreateCollectedPackage();
        Assert.Equal(PackageStatus.Collected, package.Status);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => package.CompleteReturn());
        Assert.Contains("Cannot complete return for package in status Collected", ex.Message);
    }

    // =========================================================================
    // R6: Partial Returns for Multi-Item Orders (Same OrderGroupId)
    // =========================================================================

    [Fact]
    public async Task R06_PartialReturn_SinglePackageInOrderGroup_LeavesOtherPackagesCollected()
    {
        // Arrange
        var (packageRepo, returnRepo, _, _, returnService) = CreateReturnServiceContext();
        const int orderGroupId = 999;
        const int customerId = 55;

        var pkg1 = CreateCollectedPackage(packageId: 101, recipientId: customerId, orderGroupId: orderGroupId);
        var pkg2 = CreateCollectedPackage(packageId: 102, recipientId: customerId, orderGroupId: orderGroupId);
        var pkg3 = CreateCollectedPackage(packageId: 103, recipientId: customerId, orderGroupId: orderGroupId);

        packageRepo.Setup(r => r.GetByIdAsync(101)).ReturnsAsync(pkg1);
        packageRepo.Setup(r => r.GetByIdAsync(102)).ReturnsAsync(pkg2);
        packageRepo.Setup(r => r.GetByIdAsync(103)).ReturnsAsync(pkg3);
        packageRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Package> { pkg1, pkg2, pkg3 });

        returnRepo.Setup(r => r.GetActiveReturnsByPackageIdAsync(101)).ReturnsAsync(new List<Return>());
        returnRepo.Setup(r => r.GetActiveReturnsByPackageIdAsync(102)).ReturnsAsync(new List<Return>());
        returnRepo.Setup(r => r.GetActiveReturnsByPackageIdAsync(103)).ReturnsAsync(new List<Return>());
        returnRepo.Setup(r => r.GetByPackageIdAsync(It.IsAny<int>())).ReturnsAsync(new List<Return>());

        // Act: Return ONLY package 101
        var returnEntity = await returnService.RequestReturnAsync(101, customerId, "Defective item in bundle");

        // Assert: pkg1 is ReturnRequested; pkg2 and pkg3 remain untouched
        Assert.Equal(PackageStatus.ReturnRequested, pkg1.Status);
        Assert.NotNull(pkg1.Return);
        Assert.Equal(PackageStatus.Collected, pkg2.Status);
        Assert.Null(pkg2.Return);
        Assert.Equal(PackageStatus.Collected, pkg3.Status);
        Assert.Null(pkg3.Return);

        // Configure return repo for subsequent query
        returnRepo.Setup(r => r.GetActiveReturnsByPackageIdAsync(101)).ReturnsAsync(new List<Return> { returnEntity });

        // Act: Query returnable packages for this order group
        var returnable = (await returnService.GetReturnablePackagesAsync(orderGroupId, customerId)).ToList();

        // Assert: Only pkg2 and pkg3 are eligible
        Assert.Equal(2, returnable.Count);
        Assert.DoesNotContain(returnable, p => p.Id == 101);
        Assert.Contains(returnable, p => p.Id == 102);
        Assert.Contains(returnable, p => p.Id == 103);
    }

    [Fact]
    public async Task R06_GetReturnablePackagesAsync_ExcludesPackagesWithActiveOrCompletedReturns()
    {
        // Arrange
        var (packageRepo, returnRepo, _, _, returnService) = CreateReturnServiceContext();
        const int orderGroupId = 888;
        const int customerId = 66;

        var pkgActive = CreateCollectedPackage(packageId: 201, recipientId: customerId, orderGroupId: orderGroupId);
        var pkgCompleted = CreateCollectedPackage(packageId: 202, recipientId: customerId, orderGroupId: orderGroupId);
        var pkgAvailable = CreateCollectedPackage(packageId: 203, recipientId: customerId, orderGroupId: orderGroupId);

        packageRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Package> { pkgActive, pkgCompleted, pkgAvailable });

        var activeReturn = new Return { Id = 1, PackageId = 201 };
        var completedReturn = new Return { Id = 2, PackageId = 202 };
        completedReturn.Approve(1, 2);
        completedReturn.Complete();

        returnRepo.Setup(r => r.GetActiveReturnsByPackageIdAsync(201)).ReturnsAsync(new List<Return> { activeReturn });
        returnRepo.Setup(r => r.GetByPackageIdAsync(201)).ReturnsAsync(new List<Return> { activeReturn });

        returnRepo.Setup(r => r.GetActiveReturnsByPackageIdAsync(202)).ReturnsAsync(new List<Return>());
        returnRepo.Setup(r => r.GetByPackageIdAsync(202)).ReturnsAsync(new List<Return> { completedReturn });

        returnRepo.Setup(r => r.GetActiveReturnsByPackageIdAsync(203)).ReturnsAsync(new List<Return>());
        returnRepo.Setup(r => r.GetByPackageIdAsync(203)).ReturnsAsync(new List<Return>());

        // Act
        var returnable = (await returnService.GetReturnablePackagesAsync(orderGroupId, customerId)).ToList();

        // Assert: only pkgAvailable is returned
        Assert.Single(returnable);
        Assert.Equal(203, returnable[0].Id);
    }

    [Fact]
    public async Task R06_GetReturnablePackagesAsync_WhenDifferentCustomer_ReturnsEmpty()
    {
        // Arrange
        var (packageRepo, _, _, _, returnService) = CreateReturnServiceContext();
        const int orderGroupId = 777;
        var pkg = CreateCollectedPackage(packageId: 301, recipientId: 10, orderGroupId: orderGroupId);
        packageRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Package> { pkg });

        // Act: querying with different customerId (99 instead of 10)
        var returnable = await returnService.GetReturnablePackagesAsync(orderGroupId, customerId: 99);

        // Assert
        Assert.Empty(returnable);
    }

    // =========================================================================
    // R7: Blocking Duplicate Return Requests
    // =========================================================================

    [Fact]
    public async Task R07_RequestReturnAsync_WhenActiveReturnExists_ThrowsInvalidOperationException()
    {
        // Arrange
        var (packageRepo, returnRepo, _, _, returnService) = CreateReturnServiceContext();
        var package = CreateCollectedPackage(packageId: 1, recipientId: 42);

        var existingReturn = new Return
        {
            Id = 5,
            PackageId = 1,
            Package = package
        };

        packageRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(package);
        returnRepo.Setup(r => r.GetActiveReturnsByPackageIdAsync(1)).ReturnsAsync(new List<Return> { existingReturn });

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            returnService.RequestReturnAsync(1, 42, "Second return attempt"));

        Assert.Equal("A return request already exists for this package", ex.Message);
        returnRepo.Verify(r => r.AddAsync(It.IsAny<Return>()), Times.Never);
        packageRepo.Verify(r => r.UpdateAsync(It.IsAny<Package>()), Times.Never);
    }

    [Fact]
    public void R07_Domain_PackageRequestReturn_WhenAlreadyInReturnRequested_ThrowsInvalidOperationException()
    {
        // Arrange
        var package = CreateCollectedPackage();
        package.RequestReturn();
        Assert.Equal(PackageStatus.ReturnRequested, package.Status);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => package.RequestReturn());
        Assert.Contains("Only collected packages can be returned", ex.Message);
    }

    [Fact]
    public void R07_Domain_PackageRequestReturn_WhenInReturnInProgressOrReturned_ThrowsInvalidOperationException()
    {
        // Arrange
        var package = CreateCollectedPackage();
        package.RequestReturn();
        package.ApproveReturn();
        Assert.Equal(PackageStatus.ReturnInProgress, package.Status);

        // Act & Assert 1: InProgress
        var ex1 = Assert.Throws<InvalidOperationException>(() => package.RequestReturn());
        Assert.Contains("Only collected packages can be returned", ex1.Message);

        // Act & Assert 2: Returned
        package.CompleteReturn();
        Assert.Equal(PackageStatus.Returned, package.Status);
        var ex2 = Assert.Throws<InvalidOperationException>(() => package.RequestReturn());
        Assert.Contains("Only collected packages can be returned", ex2.Message);
    }

    // =========================================================================
    // R8: Return Completion Triggers Payment Refund
    // =========================================================================

    [Fact]
    public async Task R08_CompleteReturnAsync_WhenPaymentCompleted_TransitionsPaymentToRefunded()
    {
        // Arrange
        var (packageRepo, returnRepo, _, notificationService, returnService) = CreateReturnServiceContext();
        var package = CreateCollectedPackage(packageId: 1);
        Assert.NotNull(package.Payment);
        Assert.Equal(PaymentStatus.Completed, package.Payment.Status);

        package.RequestReturn();
        package.ApproveReturn();

        var returnEntity = new Return
        {
            Id = 40,
            PackageId = 1,
            Package = package
        };
        returnEntity.Approve(10, 20);
        returnEntity.Ship(10);
        package.Return = returnEntity;

        returnRepo.Setup(r => r.GetByIdAsync(40)).ReturnsAsync(returnEntity);

        // Act
        await returnService.CompleteReturnAsync(40);

        // Assert
        Assert.Equal(PaymentStatus.Refunded, package.Payment.Status);
        Assert.Equal(ReturnStatus.Completed, returnEntity.Status);
        Assert.Equal(PackageStatus.Returned, package.Status);

        returnRepo.Verify(r => r.UpdateAsync(returnEntity), Times.Once);
        packageRepo.Verify(r => r.UpdateAsync(package), Times.Once);
        notificationService.Verify(n => n.NotifyAsync("system",
            It.Is<string>(msg => msg.Contains("Return 40 completed and payment refunded"))), Times.Once);
    }

    [Fact]
    public async Task R08_CompleteReturnAsync_WhenPackageHasNoPayment_CompletesWithoutError()
    {
        // Arrange
        var (packageRepo, returnRepo, _, _, returnService) = CreateReturnServiceContext();
        var package = CreateCollectedPackage(packageId: 1);
        package.Payment = null; // No payment associated

        package.RequestReturn();
        package.ApproveReturn();

        var returnEntity = new Return
        {
            Id = 41,
            PackageId = 1,
            Package = package
        };
        returnEntity.Approve(10, 20);
        package.Return = returnEntity;

        returnRepo.Setup(r => r.GetByIdAsync(41)).ReturnsAsync(returnEntity);

        // Act
        await returnService.CompleteReturnAsync(41);

        // Assert
        Assert.Equal(ReturnStatus.Completed, returnEntity.Status);
        Assert.Equal(PackageStatus.Returned, package.Status);
        returnRepo.Verify(r => r.UpdateAsync(returnEntity), Times.Once);
        packageRepo.Verify(r => r.UpdateAsync(package), Times.Once);
    }

    [Fact]
    public void R08_Domain_PaymentRefund_TransitionsCompletedToRefunded_AndThrowsIfNotCompleted()
    {
        // Arrange
        var payment = new Payment { PackageId = 1, PayerEmail = "user@test.com", Amount = 50.0m };
        Assert.Equal(PaymentStatus.Pending, payment.Status);

        // Act & Assert 1: Cannot refund pending payment
        var ex = Assert.Throws<InvalidOperationException>(() => payment.Refund());
        Assert.Equal("Cannot refund payment in status Pending", ex.Message);

        // Act 2: Complete payment
        payment.Complete();
        Assert.Equal(PaymentStatus.Completed, payment.Status);

        // Act 3: Refund completed payment
        payment.Refund();
        Assert.Equal(PaymentStatus.Refunded, payment.Status);

        // Act & Assert 4: Cannot refund already refunded payment
        var ex2 = Assert.Throws<InvalidOperationException>(() => payment.Refund());
        Assert.Equal("Cannot refund payment in status Refunded", ex2.Message);
    }

    // =========================================================================
    // R9: Return Cancellation & Edge Cases
    // =========================================================================

    [Fact]
    public async Task R09_CancelReturnAsync_WhenReturnRequested_RevertsToCollectedAndCancelsReturn()
    {
        // Arrange
        var (packageRepo, returnRepo, _, _, returnService) = CreateReturnServiceContext();
        var package = CreateCollectedPackage(packageId: 1);
        package.RequestReturn();

        var returnEntity = new Return
        {
            Id = 50,
            PackageId = 1,
            Package = package
        };
        package.Return = returnEntity;

        returnRepo.Setup(r => r.GetByIdAsync(50)).ReturnsAsync(returnEntity);

        // Act
        await returnService.CancelReturnAsync(50, "Changed mind");

        // Assert
        Assert.Equal(ReturnStatus.Cancelled, returnEntity.Status);
        Assert.Equal(PackageStatus.Collected, package.Status);
        returnRepo.Verify(r => r.UpdateAsync(returnEntity), Times.Once);
        packageRepo.Verify(r => r.UpdateAsync(package), Times.Once);
    }

    [Fact]
    public void R09_Domain_CancelReturn_WhenInReturnInProgress_RevertsPackageToCollected()
    {
        // Arrange
        var package = CreateCollectedPackage();
        package.RequestReturn();
        package.ApproveReturn();
        Assert.Equal(PackageStatus.ReturnInProgress, package.Status);

        var returnEntity = new Return { Id = 1 };
        returnEntity.Approve(10, 20);

        // Act
        returnEntity.Cancel();
        package.CancelReturn();

        // Assert
        Assert.Equal(ReturnStatus.Cancelled, returnEntity.Status);
        Assert.Equal(PackageStatus.Collected, package.Status);
    }

    [Fact]
    public void R10_ReturnService_Constructor_ThrowsOnNullDependencies()
    {
        // Arrange
        var packageRepo = new Mock<IPackageRepository>();
        var returnRepo = new Mock<IReturnRepository>();
        var peopleService = new Mock<IPeopleService>();
        var notificationService = new Mock<INotificationService>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>("packageRepository", () =>
            new ReturnService(null!, returnRepo.Object, peopleService.Object, notificationService.Object));

        Assert.Throws<ArgumentNullException>("returnRepository", () =>
            new ReturnService(packageRepo.Object, null!, peopleService.Object, notificationService.Object));

        Assert.Throws<ArgumentNullException>("peopleService", () =>
            new ReturnService(packageRepo.Object, returnRepo.Object, null!, notificationService.Object));

        Assert.Throws<ArgumentNullException>("notificationService", () =>
            new ReturnService(packageRepo.Object, returnRepo.Object, peopleService.Object, null!));
    }
}
