using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PackageDeliverySystem.Core;
using PackageDeliverySystem.Infrastructure;
using PackageDeliverySystem.Infrastructure.Data;

namespace M3ChallengerHarness;

public static class Program
{
    private static int _total = 0;
    private static int _passed = 0;
    private static int _failed = 0;
    private static readonly List<string> FailureMessages = new();

    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("========================================================================");
        Console.WriteLine("STARTING CHALLENGER M3 EMPIRICAL TEST SUITE (Infrastructure Implementation - R3)");
        Console.WriteLine("========================================================================");

        // 1. Reflection & Interface Conformance
        TestInterfaceConformance();

        // 2. Null Guards
        TestNullGuards();

        // 3. DeliveryDbContext & BoxRepository Reflection
        TestDbContextAndBoxRepository();

        // 4. ReturnService Functional & State Invariant Tests
        await TestReturnServiceRequestReturnAsync();
        await TestReturnServiceApproveReturnAsync();
        await TestReturnServiceRejectReturnAsync();
        await TestReturnServiceShipReturnAsync();
        await TestReturnServiceCompleteReturnAsync();
        await TestReturnServiceCancelReturnAsync();
        await TestReturnServiceGetReturnablePackagesAsync();

        Console.WriteLine("========================================================================");
        Console.WriteLine($"RESULTS: Total: {_total}, Passed: {_passed}, Failed: {_failed}");
        Console.WriteLine("========================================================================");

        if (_failed > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("FAILURE SUMMARY:");
            foreach (var f in FailureMessages)
            {
                Console.WriteLine($" [FAIL] {f}");
            }
            Console.ResetColor();
            return 1;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("ALL M3 EMPIRICAL CHALLENGER TESTS PASSED!");
        Console.ResetColor();
        return 0;
    }

    private static void Assert(bool condition, string testName, string? message = null)
    {
        _total++;
        if (condition)
        {
            _passed++;
            Console.WriteLine($"[PASS] {testName}");
        }
        else
        {
            _failed++;
            string err = $"{testName}: {message ?? "Assertion failed"}";
            FailureMessages.Add(err);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAIL] {err}");
            Console.ResetColor();
        }
    }

    private static void AssertThrows<TException>(Action action, string testName) where TException : Exception
    {
        _total++;
        try
        {
            action();
            _failed++;
            string err = $"{testName}: Expected exception {typeof(TException).Name} but none was thrown.";
            FailureMessages.Add(err);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAIL] {err}");
            Console.ResetColor();
        }
        catch (TException)
        {
            _passed++;
            Console.WriteLine($"[PASS] {testName}");
        }
        catch (Exception ex)
        {
            _failed++;
            string err = $"{testName}: Expected exception {typeof(TException).Name} but caught {ex.GetType().Name}: {ex.Message}";
            FailureMessages.Add(err);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAIL] {err}");
            Console.ResetColor();
        }
    }

    private static async Task AssertThrowsAsync<TException>(Func<Task> action, string testName) where TException : Exception
    {
        _total++;
        try
        {
            await action();
            _failed++;
            string err = $"{testName}: Expected exception {typeof(TException).Name} but none was thrown.";
            FailureMessages.Add(err);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAIL] {err}");
            Console.ResetColor();
        }
        catch (TException)
        {
            _passed++;
            Console.WriteLine($"[PASS] {testName}");
        }
        catch (Exception ex)
        {
            _failed++;
            string err = $"{testName}: Expected exception {typeof(TException).Name} but caught {ex.GetType().Name}: {ex.Message}";
            FailureMessages.Add(err);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAIL] {err}");
            Console.ResetColor();
        }
    }

    // -------------------------------------------------------------
    // Category 1: Interface Conformance & Reflection
    // -------------------------------------------------------------
    private static void TestInterfaceConformance()
    {
        Console.WriteLine("\n--- Category 1: Interface Conformance ---");

        var repoType = typeof(ReturnRepository);
        var repoInterface = typeof(IReturnRepository);
        Assert(repoInterface.IsAssignableFrom(repoType), "ReturnRepository implements IReturnRepository");

        string[] repoMethods =
        {
            nameof(IReturnRepository.GetByIdAsync),
            nameof(IReturnRepository.AddAsync),
            nameof(IReturnRepository.UpdateAsync),
            nameof(IReturnRepository.DeleteAsync),
            nameof(IReturnRepository.GetAllAsync),
            nameof(IReturnRepository.GetByPackageIdAsync),
            nameof(IReturnRepository.GetActiveReturnsByPackageIdAsync),
            nameof(IReturnRepository.GetByStatusAsync)
        };

        foreach (var mName in repoMethods)
        {
            var ifaceMethods = repoInterface.GetMethods().Where(m => m.Name == mName).ToList();
            Assert(ifaceMethods.Count > 0, $"IReturnRepository contains method {mName}");
            foreach (var ifaceMethod in ifaceMethods)
            {
                var impl = repoType.GetMethod(ifaceMethod.Name, ifaceMethod.GetParameters().Select(p => p.ParameterType).ToArray());
                Assert(impl != null && impl.ReturnType == ifaceMethod.ReturnType, $"ReturnRepository implements {ifaceMethod.Name} with exact signature");
            }
        }

        var svcType = typeof(ReturnService);
        var svcInterface = typeof(IReturnService);
        Assert(svcInterface.IsAssignableFrom(svcType), "ReturnService implements IReturnService");

        string[] svcMethods =
        {
            nameof(IReturnService.RequestReturnAsync),
            nameof(IReturnService.ApproveReturnAsync),
            nameof(IReturnService.RejectReturnAsync),
            nameof(IReturnService.ShipReturnAsync),
            nameof(IReturnService.CompleteReturnAsync),
            nameof(IReturnService.CancelReturnAsync),
            nameof(IReturnService.GetReturnablePackagesAsync),
            nameof(IReturnService.GetReturnByIdAsync)
        };

        foreach (var mName in svcMethods)
        {
            var ifaceMethods = svcInterface.GetMethods().Where(m => m.Name == mName).ToList();
            Assert(ifaceMethods.Count > 0, $"IReturnService contains method {mName}");
            foreach (var ifaceMethod in ifaceMethods)
            {
                var impl = svcType.GetMethod(ifaceMethod.Name, ifaceMethod.GetParameters().Select(p => p.ParameterType).ToArray());
                Assert(impl != null && impl.ReturnType == ifaceMethod.ReturnType, $"ReturnService implements {ifaceMethod.Name} with exact signature");
            }
        }
    }

    // -------------------------------------------------------------
    // Category 2: Null Guards
    // -------------------------------------------------------------
    private static void TestNullGuards()
    {
        Console.WriteLine("\n--- Category 2: Null Guards ---");

        AssertThrows<ArgumentNullException>(() => new ReturnRepository(null!), "ReturnRepository constructor throws on null context");

        var dummyPkgRepo = new StubPackageRepo();
        var dummyReturnRepo = new StubReturnRepo();
        var dummyPeople = new StubPeopleService();
        var dummyNotif = new StubNotificationService();

        AssertThrows<ArgumentNullException>(() => new ReturnService(null!, dummyReturnRepo, dummyPeople, dummyNotif),
            "ReturnService constructor throws on null packageRepo");

        AssertThrows<ArgumentNullException>(() => new ReturnService(dummyPkgRepo, null!, dummyPeople, dummyNotif),
            "ReturnService constructor throws on null returnRepo");

        AssertThrows<ArgumentNullException>(() => new ReturnService(dummyPkgRepo, dummyReturnRepo, null!, dummyNotif),
            "ReturnService constructor throws on null peopleService");

        AssertThrows<ArgumentNullException>(() => new ReturnService(dummyPkgRepo, dummyReturnRepo, dummyPeople, null!),
            "ReturnService constructor throws on null notificationService");
    }

    // -------------------------------------------------------------
    // Category 3: DbContext & BoxRepository Reflection
    // -------------------------------------------------------------
    private static void TestDbContextAndBoxRepository()
    {
        Console.WriteLine("\n--- Category 3: DbContext & BoxRepository Reflection ---");

        var returnsProp = typeof(DeliveryDbContext).GetProperty("Returns");
        Assert(returnsProp != null, "DeliveryDbContext has property 'Returns'");
        Assert(returnsProp?.PropertyType == typeof(DbSet<Return>), "DeliveryDbContext.Returns is of type DbSet<Return>");
        Assert(returnsProp?.CanRead == true && returnsProp?.CanWrite == true, "DeliveryDbContext.Returns is read/write");

        var field = typeof(BoxRepository).GetField("TerminalStatuses", BindingFlags.NonPublic | BindingFlags.Static);
        Assert(field != null, "BoxRepository contains static TerminalStatuses field");
        if (field != null)
        {
            var val = field.GetValue(null) as PackageStatus[];
            Assert(val != null, "BoxRepository.TerminalStatuses is PackageStatus[]");
            Assert(val != null && val.Contains(PackageStatus.Returned), "BoxRepository.TerminalStatuses contains PackageStatus.Returned");
        }
    }

    // -------------------------------------------------------------
    // Category 4: ReturnService RequestReturnAsync Functional Tests
    // -------------------------------------------------------------
    private static async Task TestReturnServiceRequestReturnAsync()
    {
        Console.WriteLine("\n--- Category 4: RequestReturnAsync Tests ---");

        var pkgRepo = new StubPackageRepo();
        var returnRepo = new StubReturnRepo();
        var people = new StubPeopleService();
        var notif = new StubNotificationService();
        var svc = new ReturnService(pkgRepo, returnRepo, people, notif);

        // 1. Package not found
        await AssertThrowsAsync<ArgumentException>(() => svc.RequestReturnAsync(999, 10, "wrong package"),
            "RequestReturnAsync throws ArgumentException when package not found");

        // 2. Recipient mismatch
        var pNotRecipient = CreatePackage(1, 10, 20, PackageStatus.Collected);
        pkgRepo.Packages[1] = pNotRecipient;
        await AssertThrowsAsync<InvalidOperationException>(() => svc.RequestReturnAsync(1, 999, "not my package"),
            "RequestReturnAsync throws InvalidOperationException when customerId != RecipientId");

        // 3. Package not in Collected status
        var statusesNotCollected = new[]
        {
            PackageStatus.Ordered,
            PackageStatus.Accepted,
            PackageStatus.Preparing,
            PackageStatus.Shipped,
            PackageStatus.Delivered,
            PackageStatus.ReturnedToSender,
            PackageStatus.Returned,
            PackageStatus.Expired,
            PackageStatus.Cancelled,
            PackageStatus.Rejected
        };

        foreach (var st in statusesNotCollected)
        {
            var pBadStatus = CreatePackage(10 + (int)st, 10, 20, st);
            pkgRepo.Packages[pBadStatus.Id] = pBadStatus;
            await AssertThrowsAsync<InvalidOperationException>(() => svc.RequestReturnAsync(pBadStatus.Id, 20, "reason"),
                $"RequestReturnAsync throws InvalidOperationException for status {st}");
        }

        // 4. Duplicate active return guards
        var activeReturnStatuses = new[] { ReturnStatus.Requested, ReturnStatus.Approved, ReturnStatus.Shipped };
        foreach (var rStatus in activeReturnStatuses)
        {
            var pWithActive = CreatePackage(100 + (int)rStatus, 10, 20, PackageStatus.Collected);
            pkgRepo.Packages[pWithActive.Id] = pWithActive;
            returnRepo.Returns[1000 + (int)rStatus] = new Return
            {
                Id = 1000 + (int)rStatus,
                PackageId = pWithActive.Id,
                Package = pWithActive
            };
            SetReturnStatus(returnRepo.Returns[1000 + (int)rStatus], rStatus);

            await AssertThrowsAsync<InvalidOperationException>(() => svc.RequestReturnAsync(pWithActive.Id, 20, "duplicate"),
                $"RequestReturnAsync throws InvalidOperationException when active return exists with status {rStatus}");
        }

        // 5. Happy path (3-arg overload)
        var pValid = CreatePackage(200, 10, 20, PackageStatus.Collected);
        pkgRepo.Packages[200] = pValid;

        var ret = await svc.RequestReturnAsync(200, 20, "Item does not fit");
        Assert(ret != null, "RequestReturnAsync returns created Return entity");
        Assert(ret.PackageId == 200, "Return.PackageId is set correctly");
        Assert(ret.Reason == "Item does not fit", "Return.Reason is set correctly");
        Assert(ret.Status == ReturnStatus.Requested, "Return.Status is Requested");
        Assert(pValid.Status == PackageStatus.ReturnRequested, "Package.Status transitions to ReturnRequested");
        Assert(pValid.Return == ret, "Package.Return navigation is set");
        Assert(returnRepo.Returns.ContainsKey(ret.Id), "Return entity was added to repository");
        Assert(pkgRepo.UpdatedPackages.Contains(200), "Package was updated in repository");
        Assert(notif.Messages.Count > 0, "Notification was emitted");

        // 6. Happy path (2-arg overload using implicit recipient)
        var pImplicit = CreatePackage(201, 10, 25, PackageStatus.Collected);
        pkgRepo.Packages[201] = pImplicit;

        var retImplicit = await svc.RequestReturnAsync(201, "Changed mind");
        Assert(retImplicit != null && retImplicit.Status == ReturnStatus.Requested, "2-arg RequestReturnAsync succeeds using package.RecipientId");
        Assert(pImplicit.Status == PackageStatus.ReturnRequested, "Package status updated to ReturnRequested via 2-arg overload");
    }

    // -------------------------------------------------------------
    // Category 5: ApproveReturnAsync Tests
    // -------------------------------------------------------------
    private static async Task TestReturnServiceApproveReturnAsync()
    {
        Console.WriteLine("\n--- Category 5: ApproveReturnAsync Tests ---");

        var pkgRepo = new StubPackageRepo();
        var returnRepo = new StubReturnRepo();
        var people = new StubPeopleService();
        var notif = new StubNotificationService();
        var svc = new ReturnService(pkgRepo, returnRepo, people, notif);

        people.Operators[1] = new Operator { Id = 1, Name = "Op Charlie" };

        // 1. Return not found
        await AssertThrowsAsync<ArgumentException>(() => svc.ApproveReturnAsync(999, 1, 10, 20),
            "ApproveReturnAsync throws ArgumentException when return not found");

        // 2. Operator not found
        var ret = new Return { Id = 1, PackageId = 100 };
        returnRepo.Returns[1] = ret;
        await AssertThrowsAsync<ArgumentException>(() => svc.ApproveReturnAsync(1, 999, 10, 20),
            "ApproveReturnAsync throws ArgumentException when operator not found");

        // 3. Package not found
        await AssertThrowsAsync<ArgumentException>(() => svc.ApproveReturnAsync(1, 1, 10, 20),
            "ApproveReturnAsync throws ArgumentException when package not found");

        // 4. Happy path with 4 arguments (origin and destination boxes)
        var pkg = CreatePackage(100, 10, 20, PackageStatus.ReturnRequested);
        pkgRepo.Packages[100] = pkg;
        ret.Package = pkg;

        await svc.ApproveReturnAsync(1, 1, 10, 20);
        Assert(ret.Status == ReturnStatus.Approved, "Return.Status is Approved");
        Assert(ret.ReturnBoxId == 10, "Return.ReturnBoxId set");
        Assert(ret.ReturnDestinationBoxId == 20, "Return.ReturnDestinationBoxId set");
        Assert(ret.ApprovedAt != null, "Return.ApprovedAt timestamp is populated");
        Assert(pkg.Status == PackageStatus.ReturnInProgress, "Package.Status transitions to ReturnInProgress");
        Assert(returnRepo.UpdatedReturns.Contains(1), "Return updated in repository");
        Assert(pkgRepo.UpdatedPackages.Contains(100), "Package updated in repository");

        // 5. Happy path with optional destination box overload
        var ret2 = new Return { Id = 2, PackageId = 200 };
        var pkg2 = CreatePackage(200, 10, 20, PackageStatus.ReturnRequested);
        pkgRepo.Packages[200] = pkg2;
        returnRepo.Returns[2] = ret2;
        ret2.Package = pkg2;

        await svc.ApproveReturnAsync(2, 1, 55);
        Assert(ret2.Status == ReturnStatus.Approved, "Return.Status is Approved (optional box overload)");
        Assert(ret2.ReturnBoxId == null, "Return.ReturnBoxId is null");
        Assert(ret2.ReturnDestinationBoxId == 55, "Return.ReturnDestinationBoxId set to 55");
        Assert(pkg2.Status == PackageStatus.ReturnInProgress, "Package.Status is ReturnInProgress");
    }

    // -------------------------------------------------------------
    // Category 6: RejectReturnAsync Tests
    // -------------------------------------------------------------
    private static async Task TestReturnServiceRejectReturnAsync()
    {
        Console.WriteLine("\n--- Category 6: RejectReturnAsync Tests ---");

        var pkgRepo = new StubPackageRepo();
        var returnRepo = new StubReturnRepo();
        var people = new StubPeopleService();
        var notif = new StubNotificationService();
        var svc = new ReturnService(pkgRepo, returnRepo, people, notif);

        people.Operators[1] = new Operator { Id = 1, Name = "Op Charlie" };

        var pkg = CreatePackage(100, 10, 20, PackageStatus.ReturnRequested);
        pkgRepo.Packages[100] = pkg;
        var ret = new Return { Id = 1, PackageId = 100, Package = pkg };
        returnRepo.Returns[1] = ret;

        await svc.RejectReturnAsync(1, 1, "Policy violation");
        Assert(ret.Status == ReturnStatus.Rejected, "Return.Status is Rejected");
        Assert(pkg.Status == PackageStatus.Collected, "Package.Status reverts to Collected");
        Assert(returnRepo.UpdatedReturns.Contains(1), "Return updated in repository");
        Assert(pkgRepo.UpdatedPackages.Contains(100), "Package updated in repository");

        // Overload without reason
        var pkg2 = CreatePackage(200, 10, 20, PackageStatus.ReturnRequested);
        pkgRepo.Packages[200] = pkg2;
        var ret2 = new Return { Id = 2, PackageId = 200, Package = pkg2 };
        returnRepo.Returns[2] = ret2;

        await svc.RejectReturnAsync(2, 1);
        Assert(ret2.Status == ReturnStatus.Rejected, "Return.Status is Rejected (overload without reason)");
        Assert(pkg2.Status == PackageStatus.Collected, "Package.Status reverts to Collected");
    }

    // -------------------------------------------------------------
    // Category 7: ShipReturnAsync Tests
    // -------------------------------------------------------------
    private static async Task TestReturnServiceShipReturnAsync()
    {
        Console.WriteLine("\n--- Category 7: ShipReturnAsync Tests ---");

        var pkgRepo = new StubPackageRepo();
        var returnRepo = new StubReturnRepo();
        var people = new StubPeopleService();
        var notif = new StubNotificationService();
        var svc = new ReturnService(pkgRepo, returnRepo, people, notif);

        people.Couriers[5] = new Courier { Id = 5, Name = "Courier Dave" };

        var pkg = CreatePackage(100, 10, 20, PackageStatus.ReturnInProgress);
        pkgRepo.Packages[100] = pkg;
        var ret = new Return { Id = 1, PackageId = 100, Package = pkg };
        SetReturnStatus(ret, ReturnStatus.Approved);
        returnRepo.Returns[1] = ret;

        await svc.ShipReturnAsync(1, 5, 33);
        Assert(ret.Status == ReturnStatus.Shipped, "Return.Status is Shipped");
        Assert(ret.ReturnBoxId == 33, "Return.ReturnBoxId updated to 33");
        Assert(pkg.Status == PackageStatus.ReturnInProgress, "Package.Status is ReturnInProgress");
        Assert(returnRepo.UpdatedReturns.Contains(1), "Return updated in repository");
        Assert(pkgRepo.UpdatedPackages.Contains(100), "Package updated in repository");

        // Overload without boxId
        var pkg2 = CreatePackage(200, 10, 20, PackageStatus.ReturnInProgress);
        pkgRepo.Packages[200] = pkg2;
        var ret2 = new Return { Id = 2, PackageId = 200, Package = pkg2 };
        SetReturnStatus(ret2, ReturnStatus.Approved);
        returnRepo.Returns[2] = ret2;

        await svc.ShipReturnAsync(2, 5);
        Assert(ret2.Status == ReturnStatus.Shipped, "Return.Status is Shipped (overload without boxId)");
    }

    // -------------------------------------------------------------
    // Category 8: CompleteReturnAsync Tests (with Payment Refund)
    // -------------------------------------------------------------
    private static async Task TestReturnServiceCompleteReturnAsync()
    {
        Console.WriteLine("\n--- Category 8: CompleteReturnAsync & Payment Refund Tests ---");

        var pkgRepo = new StubPackageRepo();
        var returnRepo = new StubReturnRepo();
        var people = new StubPeopleService();
        var notif = new StubNotificationService();
        var svc = new ReturnService(pkgRepo, returnRepo, people, notif);

        // 1. With completed payment: Refund() triggered
        var pkgWithPayment = CreatePackage(100, 10, 20, PackageStatus.ReturnInProgress);
        pkgWithPayment.Payment = new Payment
        {
            Id = 10,
            PackageId = 100,
            Amount = 49.99m,
            PayerEmail = "customer@example.com"
        };
        pkgWithPayment.Payment.Complete();
        Assert(pkgWithPayment.Payment.Status == PaymentStatus.Completed, "Initial payment is Completed");

        pkgRepo.Packages[100] = pkgWithPayment;
        var ret = new Return { Id = 1, PackageId = 100, Package = pkgWithPayment };
        SetReturnStatus(ret, ReturnStatus.Shipped);
        returnRepo.Returns[1] = ret;

        await svc.CompleteReturnAsync(1);
        Assert(ret.Status == ReturnStatus.Completed, "Return.Status is Completed");
        Assert(ret.CompletedAt != null, "Return.CompletedAt is set");
        Assert(pkgWithPayment.Status == PackageStatus.Returned, "Package.Status transitions to Returned");
        Assert(pkgWithPayment.Payment.Status == PaymentStatus.Refunded, "Payment.Status transitions to Refunded");
        Assert(returnRepo.UpdatedReturns.Contains(1), "Return updated in repository");
        Assert(pkgRepo.UpdatedPackages.Contains(100), "Package updated in repository");

        // 2. Package without payment: completes gracefully without throwing
        var pkgNoPayment = CreatePackage(200, 10, 20, PackageStatus.ReturnInProgress);
        pkgRepo.Packages[200] = pkgNoPayment;
        var ret2 = new Return { Id = 2, PackageId = 200, Package = pkgNoPayment };
        SetReturnStatus(ret2, ReturnStatus.Shipped);
        returnRepo.Returns[2] = ret2;

        await svc.CompleteReturnAsync(2);
        Assert(ret2.Status == ReturnStatus.Completed, "Return.Status is Completed for package without payment");
        Assert(pkgNoPayment.Status == PackageStatus.Returned, "Package.Status is Returned");

        // 3. Package with pending payment: does not throw, does not refund
        var pkgPendingPayment = CreatePackage(300, 10, 20, PackageStatus.ReturnInProgress);
        pkgPendingPayment.Payment = new Payment { Id = 30, PackageId = 300 };
        Assert(pkgPendingPayment.Payment.Status == PaymentStatus.Pending, "Initial payment is Pending");
        pkgRepo.Packages[300] = pkgPendingPayment;
        var ret3 = new Return { Id = 3, PackageId = 300, Package = pkgPendingPayment };
        SetReturnStatus(ret3, ReturnStatus.Shipped);
        returnRepo.Returns[3] = ret3;

        await svc.CompleteReturnAsync(3);
        Assert(pkgPendingPayment.Payment.Status == PaymentStatus.Pending, "Payment stays Pending (not Refunded)");
        Assert(ret3.Status == ReturnStatus.Completed, "Return.Status is Completed");
    }

    // -------------------------------------------------------------
    // Category 9: CancelReturnAsync Tests
    // -------------------------------------------------------------
    private static async Task TestReturnServiceCancelReturnAsync()
    {
        Console.WriteLine("\n--- Category 9: CancelReturnAsync Tests ---");

        var pkgRepo = new StubPackageRepo();
        var returnRepo = new StubReturnRepo();
        var people = new StubPeopleService();
        var notif = new StubNotificationService();
        var svc = new ReturnService(pkgRepo, returnRepo, people, notif);

        var pkg = CreatePackage(100, 10, 20, PackageStatus.ReturnRequested);
        pkgRepo.Packages[100] = pkg;
        var ret = new Return { Id = 1, PackageId = 100, Package = pkg };
        returnRepo.Returns[1] = ret;

        await svc.CancelReturnAsync(1, "Customer kept item");
        Assert(ret.Status == ReturnStatus.Cancelled, "Return.Status is Cancelled");
        Assert(pkg.Status == PackageStatus.Collected, "Package.Status reverts to Collected");
        Assert(returnRepo.UpdatedReturns.Contains(1), "Return updated in repository");
        Assert(pkgRepo.UpdatedPackages.Contains(100), "Package updated in repository");
    }

    // -------------------------------------------------------------
    // Category 10: Partial Returns & GetReturnablePackagesAsync Tests
    // -------------------------------------------------------------
    private static async Task TestReturnServiceGetReturnablePackagesAsync()
    {
        Console.WriteLine("\n--- Category 10: Partial Returns & GetReturnablePackagesAsync Tests ---");

        var pkgRepo = new StubPackageRepo();
        var returnRepo = new StubReturnRepo();
        var people = new StubPeopleService();
        var notif = new StubNotificationService();
        var svc = new ReturnService(pkgRepo, returnRepo, people, notif);

        const int targetOrderGroupId = 555;
        const int targetCustomerId = 77;

        // P1: Target group & customer, Collected, NO return history -> ELIGIBLE
        var p1 = CreatePackage(1, 10, targetCustomerId, PackageStatus.Collected);
        p1.OrderGroupId = targetOrderGroupId;
        pkgRepo.Packages[1] = p1;

        // P2: Target group & customer, Collected, has ACTIVE return (Requested) -> EXCLUDED
        var p2 = CreatePackage(2, 10, targetCustomerId, PackageStatus.Collected);
        p2.OrderGroupId = targetOrderGroupId;
        pkgRepo.Packages[2] = p2;
        var ret2 = new Return { Id = 20, PackageId = 2 };
        SetReturnStatus(ret2, ReturnStatus.Requested);
        returnRepo.Returns[20] = ret2;

        // P3: Target group & customer, Collected, has COMPLETED return -> EXCLUDED
        var p3 = CreatePackage(3, 10, targetCustomerId, PackageStatus.Collected);
        p3.OrderGroupId = targetOrderGroupId;
        pkgRepo.Packages[3] = p3;
        var ret3 = new Return { Id = 30, PackageId = 3 };
        SetReturnStatus(ret3, ReturnStatus.Completed);
        returnRepo.Returns[30] = ret3;

        // P4: Target group & customer, status DELIVERED (not Collected) -> EXCLUDED
        var p4 = CreatePackage(4, 10, targetCustomerId, PackageStatus.Delivered);
        p4.OrderGroupId = targetOrderGroupId;
        pkgRepo.Packages[4] = p4;

        // P5: Target group, DIFFERENT customer, Collected -> EXCLUDED
        var p5 = CreatePackage(5, 10, 999, PackageStatus.Collected);
        p5.OrderGroupId = targetOrderGroupId;
        pkgRepo.Packages[5] = p5;

        // P6: DIFFERENT group, Target customer, Collected -> EXCLUDED
        var p6 = CreatePackage(6, 10, targetCustomerId, PackageStatus.Collected);
        p6.OrderGroupId = 888;
        pkgRepo.Packages[6] = p6;

        // P7: Target group & customer, Collected, has CANCELLED return (no active, no completed) -> ELIGIBLE
        var p7 = CreatePackage(7, 10, targetCustomerId, PackageStatus.Collected);
        p7.OrderGroupId = targetOrderGroupId;
        pkgRepo.Packages[7] = p7;
        var ret7 = new Return { Id = 70, PackageId = 7 };
        SetReturnStatus(ret7, ReturnStatus.Cancelled);
        returnRepo.Returns[70] = ret7;

        // P8: Target group & customer, Collected, has REJECTED return (no active, no completed) -> ELIGIBLE
        var p8 = CreatePackage(8, 10, targetCustomerId, PackageStatus.Collected);
        p8.OrderGroupId = targetOrderGroupId;
        pkgRepo.Packages[8] = p8;
        var ret8 = new Return { Id = 80, PackageId = 8 };
        SetReturnStatus(ret8, ReturnStatus.Rejected);
        returnRepo.Returns[80] = ret8;

        var returnable = (await svc.GetReturnablePackagesAsync(targetOrderGroupId, targetCustomerId)).ToList();

        Assert(returnable.Count == 3, $"GetReturnablePackagesAsync returned 3 packages (actual: {returnable.Count})");
        Assert(returnable.Any(p => p.Id == 1), "P1 is included (no returns)");
        Assert(!returnable.Any(p => p.Id == 2), "P2 is excluded (active return)");
        Assert(!returnable.Any(p => p.Id == 3), "P3 is excluded (completed return)");
        Assert(!returnable.Any(p => p.Id == 4), "P4 is excluded (status Delivered)");
        Assert(!returnable.Any(p => p.Id == 5), "P5 is excluded (different customer)");
        Assert(!returnable.Any(p => p.Id == 6), "P6 is excluded (different order group)");
        Assert(returnable.Any(p => p.Id == 7), "P7 is included (prior cancelled return)");
        Assert(returnable.Any(p => p.Id == 8), "P8 is included (prior rejected return)");
    }

    // -------------------------------------------------------------
    // Helper Methods & Test Stubs
    // -------------------------------------------------------------
    private static Package CreatePackage(int id, int senderId, int recipientId, PackageStatus status)
    {
        var pkg = new Package
        {
            Id = id,
            SenderId = senderId,
            RecipientId = recipientId
        };
        typeof(Package).GetProperty("Status")!.SetValue(pkg, status);
        return pkg;
    }

    private static void SetReturnStatus(Return returnEntity, ReturnStatus status)
    {
        typeof(Return).GetProperty("Status")!.SetValue(returnEntity, status);
    }

    private class StubPackageRepo : IPackageRepository
    {
        public readonly Dictionary<int, Package> Packages = new();
        public readonly HashSet<int> UpdatedPackages = new();

        public Task<Package?> GetByIdAsync(int id)
            => Task.FromResult(Packages.TryGetValue(id, out var p) ? p : null);

        public Task AddAsync(Package package)
        {
            Packages[package.Id] = package;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Package package)
        {
            Packages[package.Id] = package;
            UpdatedPackages.Add(package.Id);
            return Task.CompletedTask;
        }

        public Task<IEnumerable<Package>> GetAllAsync()
            => Task.FromResult<IEnumerable<Package>>(Packages.Values.ToList());

        public Task<IEnumerable<Package>> GetPackagesRequiringExpirationAsync()
            => Task.FromResult<IEnumerable<Package>>(Enumerable.Empty<Package>());

        public Task<IEnumerable<Package>> GetPackagesWithMissedDepositAsync()
            => Task.FromResult<IEnumerable<Package>>(Enumerable.Empty<Package>());
    }

    private class StubReturnRepo : IReturnRepository
    {
        public readonly Dictionary<int, Return> Returns = new();
        public readonly HashSet<int> UpdatedReturns = new();
        private int _nextId = 1;

        public Task<Return?> GetByIdAsync(int id)
            => Task.FromResult(Returns.TryGetValue(id, out var r) ? r : null);

        public Task AddAsync(Return returnEntity)
        {
            if (returnEntity.Id == 0)
                returnEntity.Id = _nextId++;
            Returns[returnEntity.Id] = returnEntity;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Return returnEntity)
        {
            Returns[returnEntity.Id] = returnEntity;
            UpdatedReturns.Add(returnEntity.Id);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(int id)
        {
            Returns.Remove(id);
            return Task.CompletedTask;
        }

        public Task<IEnumerable<Return>> GetAllAsync()
            => Task.FromResult<IEnumerable<Return>>(Returns.Values.ToList());

        public Task<IEnumerable<Return>> GetByPackageIdAsync(int packageId)
            => Task.FromResult<IEnumerable<Return>>(Returns.Values.Where(r => r.PackageId == packageId).ToList());

        public Task<IEnumerable<Return>> GetActiveReturnsByPackageIdAsync(int packageId)
        {
            var activeStatuses = new[] { ReturnStatus.Requested, ReturnStatus.Approved, ReturnStatus.Shipped };
            return Task.FromResult<IEnumerable<Return>>(
                Returns.Values.Where(r => r.PackageId == packageId && activeStatuses.Contains(r.Status)).ToList());
        }

        public Task<IEnumerable<Return>> GetByStatusAsync(ReturnStatus status)
            => Task.FromResult<IEnumerable<Return>>(Returns.Values.Where(r => r.Status == status).ToList());
    }

    private class StubPeopleService : IPeopleService
    {
        public readonly Dictionary<int, User> Users = new();
        public readonly Dictionary<int, Courier> Couriers = new();
        public readonly Dictionary<int, Operator> Operators = new();

        public Task<User?> GetUserAsync(int userId)
            => Task.FromResult(Users.TryGetValue(userId, out var u) ? u : null);

        public Task<Courier?> GetCourierAsync(int courierId)
            => Task.FromResult(Couriers.TryGetValue(courierId, out var c) ? c : null);

        public Task<Operator?> GetOperatorAsync(int operatorId)
            => Task.FromResult(Operators.TryGetValue(operatorId, out var o) ? o : null);

        public Task UpdateRankAsync(int personId, int rank)
            => Task.CompletedTask;
    }

    private class StubNotificationService : INotificationService
    {
        public readonly List<string> Messages = new();

        public Task NotifyAsync(string userId, string message)
        {
            Messages.Add($"{userId}: {message}");
            return Task.CompletedTask;
        }
    }
}
