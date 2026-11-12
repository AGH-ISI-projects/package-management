using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using PackageDeliverySystem.Core;

namespace ChallengerVerificationRunner;

public static class Program
{
    private static int _testsRun = 0;
    private static int _testsPassed = 0;
    private static int _testsFailed = 0;
    private static readonly List<string> FailureLog = new();

    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine("STARTING CHALLENGER 2 EMPIRICAL TEST SUITE (Milestone 2 - Core Domain & Contracts)");
        Console.WriteLine("================================================================================");

        TestPaymentRefundTransitions();
        TestPaymentRefundDataPreservation();
        TestPaymentRefundEdgeCases();
        TestPackageOrderGroupIdGrouping();
        TestPackageOrderGroupIdIndependentLifecycles();
        TestPackageOrderGroupIdBoundaryValues();
        TestReturnRepositoryInterfaceContract();
        TestReturnServiceInterfaceContract();
        TestReturnDomainEntityLifecycleAndGuards();
        TestReturnDomainBoundaryAndEdgeCases();
        TestPackageReturnLifecycleMethodsAndGuards();
        TestPackageCancelAndRejectGuardsDuringReturns();
        TestEnumsCompleteness();

        Console.WriteLine("================================================================================");
        Console.WriteLine($"RESULTS: Total: {_testsRun}, Passed: {_testsPassed}, Failed: {_testsFailed}");
        Console.WriteLine("================================================================================");

        if (_testsFailed > 0)
        {
            Console.WriteLine("FAILURES DETECTED:");
            foreach (var f in FailureLog)
            {
                Console.WriteLine($" - {f}");
            }
            return 1;
        }

        Console.WriteLine("ALL ADVERSARIAL CHALLENGER TESTS PASSED SUCCESSFULLY!");
        return 0;
    }

    private static void Assert(bool condition, string testName, string? details = null)
    {
        _testsRun++;
        if (condition)
        {
            _testsPassed++;
            Console.WriteLine($"[PASS] {testName}");
        }
        else
        {
            _testsFailed++;
            string msg = $"[FAIL] {testName} {(details != null ? "- " + details : "")}";
            Console.WriteLine(msg);
            FailureLog.Add(msg);
        }
    }

    private static void AssertThrows<TException>(Action action, string testName) where TException : Exception
    {
        _testsRun++;
        try
        {
            action();
            _testsFailed++;
            string msg = $"[FAIL] {testName} - Expected exception of type {typeof(TException).Name}, but no exception was thrown.";
            Console.WriteLine(msg);
            FailureLog.Add(msg);
        }
        catch (TException)
        {
            _testsPassed++;
            Console.WriteLine($"[PASS] {testName} (threw expected {typeof(TException).Name})");
        }
        catch (Exception ex)
        {
            _testsFailed++;
            string msg = $"[FAIL] {testName} - Expected {typeof(TException).Name}, but threw {ex.GetType().Name}: {ex.Message}";
            Console.WriteLine(msg);
            FailureLog.Add(msg);
        }
    }

    // -------------------------------------------------------------------------
    // 1. Payment.Refund() Tests
    // -------------------------------------------------------------------------
    private static void TestPaymentRefundTransitions()
    {
        Console.WriteLine("\n--- Testing Payment.Refund() State Transitions ---");

        // 1.1 Valid transition: Completed -> Refunded
        var paymentCompleted = new Payment { Amount = 29.99m, PayerEmail = "customer@example.com" };
        paymentCompleted.Complete();
        Assert(paymentCompleted.Status == PaymentStatus.Completed, "Payment status is Completed before refund");
        paymentCompleted.Refund();
        Assert(paymentCompleted.Status == PaymentStatus.Refunded, "Payment.Refund() successfully transitions Completed -> Refunded");

        // 1.2 Invalid transition: Pending -> Refunded (must throw InvalidOperationException)
        var paymentPending = new Payment { Amount = 19.99m, PayerEmail = "pending@example.com" };
        Assert(paymentPending.Status == PaymentStatus.Pending, "Payment initial status is Pending");
        AssertThrows<InvalidOperationException>(() => paymentPending.Refund(), "Payment.Refund() from Pending throws InvalidOperationException");

        // 1.3 Invalid transition: Failed -> Refunded (must throw InvalidOperationException)
        var paymentFailed = new Payment { Amount = 19.99m, PayerEmail = "failed@example.com" };
        paymentFailed.Fail();
        Assert(paymentFailed.Status == PaymentStatus.Failed, "Payment status is Failed");
        AssertThrows<InvalidOperationException>(() => paymentFailed.Refund(), "Payment.Refund() from Failed throws InvalidOperationException");

        // 1.4 Invalid transition: Refunded -> Refunded (double refund must throw InvalidOperationException)
        AssertThrows<InvalidOperationException>(() => paymentCompleted.Refund(), "Second call to Payment.Refund() on already Refunded payment throws InvalidOperationException");
    }

    private static void TestPaymentRefundDataPreservation()
    {
        Console.WriteLine("\n--- Testing Payment.Refund() Data Preservation ---");

        var created = DateTime.UtcNow.AddMinutes(-10);
        var payment = new Payment
        {
            Id = 42,
            PackageId = 1001,
            PayerEmail = "buyer@shop.pl",
            Amount = 149.99m,
            ExternalReference = "PAY-EXT-987654",
            CreatedAt = created
        };

        payment.Complete();
        var completedAt = payment.CompletedAt;
        Assert(completedAt.HasValue, "CompletedAt is recorded on completion");

        payment.Refund();

        Assert(payment.Id == 42, "Payment.Id is preserved after refund");
        Assert(payment.PackageId == 1001, "Payment.PackageId is preserved after refund");
        Assert(payment.PayerEmail == "buyer@shop.pl", "Payment.PayerEmail is preserved after refund");
        Assert(payment.Amount == 149.99m, "Payment.Amount is preserved after refund");
        Assert(payment.ExternalReference == "PAY-EXT-987654", "Payment.ExternalReference is preserved after refund");
        Assert(payment.CreatedAt == created, "Payment.CreatedAt is preserved after refund");
        Assert(payment.CompletedAt == completedAt, "Payment.CompletedAt is preserved after refund");
        Assert(payment.Status == PaymentStatus.Refunded, "Payment.Status is Refunded");
    }

    private static void TestPaymentRefundEdgeCases()
    {
        Console.WriteLine("\n--- Testing Payment.Refund() Edge Cases ---");

        // Extreme amounts: 0, negative, decimal.MaxValue
        var pZero = new Payment { Amount = 0.00m };
        pZero.Complete();
        pZero.Refund();
        Assert(pZero.Status == PaymentStatus.Refunded && pZero.Amount == 0.00m, "Payment with 0 amount can be refunded");

        var pMax = new Payment { Amount = decimal.MaxValue };
        pMax.Complete();
        pMax.Refund();
        Assert(pMax.Status == PaymentStatus.Refunded && pMax.Amount == decimal.MaxValue, "Payment with decimal.MaxValue can be refunded");

        // Calling Complete() after Refund() must throw
        AssertThrows<InvalidOperationException>(() => pZero.Complete(), "Payment.Complete() after Refund() throws InvalidOperationException");

        // Calling Fail() after Refund() must throw
        AssertThrows<InvalidOperationException>(() => pZero.Fail(), "Payment.Fail() after Refund() throws InvalidOperationException");
    }

    // -------------------------------------------------------------------------
    // 2. Package.OrderGroupId & Grouping Tests
    // -------------------------------------------------------------------------
    private static void TestPackageOrderGroupIdGrouping()
    {
        Console.WriteLine("\n--- Testing Package.OrderGroupId Property & Grouping ---");

        var pkg = new Package();
        Assert(pkg.OrderGroupId == null, "Package.OrderGroupId defaults to null");

        pkg.OrderGroupId = 555;
        Assert(pkg.OrderGroupId == 555, "Package.OrderGroupId can be assigned an integer");

        pkg.OrderGroupId = null;
        Assert(pkg.OrderGroupId == null, "Package.OrderGroupId can be reset to null");

        // Verify property reflection type is int?
        var prop = typeof(Package).GetProperty(nameof(Package.OrderGroupId));
        Assert(prop != null, "OrderGroupId property exists on Package");
        Assert(prop?.PropertyType == typeof(int?), "OrderGroupId property type is Nullable<int>");
        Assert(prop?.CanRead == true && prop?.CanWrite == true, "OrderGroupId has public getter and setter");

        // Verify Package.Return navigation property
        var returnProp = typeof(Package).GetProperty(nameof(Package.Return));
        Assert(returnProp != null, "Package.Return property exists");
        Assert(returnProp?.PropertyType == typeof(Return), "Package.Return property type is Return");
        Assert(returnProp?.CanRead == true && returnProp?.CanWrite == true, "Package.Return has public getter and setter");
    }

    private static void TestPackageOrderGroupIdBoundaryValues()
    {
        Console.WriteLine("\n--- Testing Package.OrderGroupId Boundary Values ---");

        var pkg = new Package();
        pkg.OrderGroupId = int.MinValue;
        Assert(pkg.OrderGroupId == int.MinValue, "Package.OrderGroupId supports int.MinValue");

        pkg.OrderGroupId = int.MaxValue;
        Assert(pkg.OrderGroupId == int.MaxValue, "Package.OrderGroupId supports int.MaxValue");

        pkg.OrderGroupId = 0;
        Assert(pkg.OrderGroupId == 0, "Package.OrderGroupId supports 0");

        pkg.OrderGroupId = -1;
        Assert(pkg.OrderGroupId == -1, "Package.OrderGroupId supports -1");
    }

    private static void TestPackageOrderGroupIdIndependentLifecycles()
    {
        Console.WriteLine("\n--- Testing Independent Lifecycles of Grouped Packages ---");

        const int orderGroupId = 888;
        var packages = new List<Package>();
        for (int i = 1; i <= 3; i++)
        {
            var p = new Package
            {
                Id = i,
                OrderGroupId = orderGroupId,
                RecipientId = 10,
                DepositDeadline = DateTime.UtcNow.AddHours(48)
            };
            p.ConfirmDeposit();
            p.Payment = new Payment { Amount = 10m, PayerEmail = $"cust{i}@test.com" };
            p.Payment.Complete();
            p.Accept();
            p.Prepare();
            p.Ship();
            p.Deliver();
            p.Collect(p.PickupCode!);
            packages.Add(p);
        }

        Assert(packages.All(p => p.Status == PackageStatus.Collected), "All 3 packages in order group are Collected");
        Assert(packages.All(p => p.OrderGroupId == orderGroupId), "All 3 packages share same OrderGroupId");

        // Partial return: return package 1
        var pkg1 = packages[0];
        pkg1.RequestReturn();
        Assert(pkg1.Status == PackageStatus.ReturnRequested, "Package 1 transitioned to ReturnRequested");
        Assert(packages[1].Status == PackageStatus.Collected, "Package 2 remains Collected");
        Assert(packages[2].Status == PackageStatus.Collected, "Package 3 remains Collected");

        // Progress package 1 through lifecycle
        pkg1.ApproveReturn();
        Assert(pkg1.Status == PackageStatus.ReturnInProgress, "Package 1 transitioned to ReturnInProgress");
        pkg1.ShipReturn();
        Assert(pkg1.Status == PackageStatus.ReturnInProgress, "Package 1 remains ReturnInProgress during ShipReturn");
        pkg1.CompleteReturn();
        Assert(pkg1.Status == PackageStatus.Returned, "Package 1 transitioned to Returned");

        Assert(packages[1].Status == PackageStatus.Collected, "Package 2 still remains Collected after pkg1 Completed");
        Assert(packages[2].Status == PackageStatus.Collected, "Package 3 still remains Collected after pkg1 Completed");
    }

    // -------------------------------------------------------------------------
    // 3. IReturnRepository Interface Verification
    // -------------------------------------------------------------------------
    private static void TestReturnRepositoryInterfaceContract()
    {
        Console.WriteLine("\n--- Testing IReturnRepository Interface Contract ---");

        var repoType = typeof(IReturnRepository);
        Assert(repoType.IsInterface, "IReturnRepository is an interface");
        Assert(repoType.IsPublic, "IReturnRepository is public");

        // Required methods check
        CheckMethod(repoType, "GetByIdAsync", typeof(Task<Return?>), new[] { typeof(int) });
        CheckMethod(repoType, "AddAsync", typeof(Task), new[] { typeof(Return) });
        CheckMethod(repoType, "UpdateAsync", typeof(Task), new[] { typeof(Return) });
        CheckMethod(repoType, "DeleteAsync", typeof(Task), new[] { typeof(int) });
        CheckMethod(repoType, "GetAllAsync", typeof(Task<IEnumerable<Return>>), Type.EmptyTypes);
        CheckMethod(repoType, "GetByPackageIdAsync", typeof(Task<IEnumerable<Return>>), new[] { typeof(int) });
        CheckMethod(repoType, "GetActiveReturnsByPackageIdAsync", typeof(Task<IEnumerable<Return>>), new[] { typeof(int) });
        CheckMethod(repoType, "GetByStatusAsync", typeof(Task<IEnumerable<Return>>), new[] { typeof(ReturnStatus) });
    }

    // -------------------------------------------------------------------------
    // 4. IReturnService Interface Verification
    // -------------------------------------------------------------------------
    private static void TestReturnServiceInterfaceContract()
    {
        Console.WriteLine("\n--- Testing IReturnService Interface Contract ---");

        var serviceType = typeof(IReturnService);
        Assert(serviceType.IsInterface, "IReturnService is an interface");
        Assert(serviceType.IsPublic, "IReturnService is public");

        // Check methods for R1–R8 compatibility:
        // R1, R6, R7: RequestReturnAsync
        CheckMethod(serviceType, "RequestReturnAsync", typeof(Task<Return>), new[] { typeof(int), typeof(int), typeof(string) });
        CheckMethod(serviceType, "RequestReturnAsync", typeof(Task<Return>), new[] { typeof(int), typeof(string) });

        // R3: ApproveReturnAsync
        CheckMethod(serviceType, "ApproveReturnAsync", typeof(Task), new[] { typeof(int), typeof(int), typeof(int), typeof(int) });
        CheckMethod(serviceType, "ApproveReturnAsync", typeof(Task), new[] { typeof(int), typeof(int), typeof(int?) });

        // R4: RejectReturnAsync
        CheckMethod(serviceType, "RejectReturnAsync", typeof(Task), new[] { typeof(int), typeof(int) });
        CheckMethod(serviceType, "RejectReturnAsync", typeof(Task), new[] { typeof(int), typeof(int), typeof(string) });

        // R5: ShipReturnAsync
        CheckMethod(serviceType, "ShipReturnAsync", typeof(Task), new[] { typeof(int), typeof(int) });
        CheckMethod(serviceType, "ShipReturnAsync", typeof(Task), new[] { typeof(int), typeof(int), typeof(int?) });

        // R5, R8: CompleteReturnAsync
        CheckMethod(serviceType, "CompleteReturnAsync", typeof(Task), new[] { typeof(int) });

        // CancelReturnAsync
        CheckMethod(serviceType, "CancelReturnAsync", typeof(Task), new[] { typeof(int), typeof(string) });

        // R6: GetReturnablePackagesAsync
        CheckMethod(serviceType, "GetReturnablePackagesAsync", typeof(Task<IEnumerable<Package>>), new[] { typeof(int), typeof(int) });

        // GetReturnByIdAsync
        CheckMethod(serviceType, "GetReturnByIdAsync", typeof(Task<Return?>), new[] { typeof(int) });
    }

    private static void CheckMethod(Type interfaceType, string methodName, Type returnType, Type[] paramTypes)
    {
        var methods = interfaceType.GetMethods().Where(m => m.Name == methodName).ToList();
        var match = methods.FirstOrDefault(m =>
        {
            if (m.ReturnType != returnType) return false;
            var pars = m.GetParameters();
            if (pars.Length != paramTypes.Length) return false;
            for (int i = 0; i < pars.Length; i++)
            {
                if (pars[i].ParameterType != paramTypes[i]) return false;
            }
            return true;
        });

        Assert(match != null, $"{interfaceType.Name}.{methodName}({string.Join(", ", paramTypes.Select(p => p.Name))}) -> {returnType.Name} is present");
    }

    // -------------------------------------------------------------------------
    // 5. Return Domain Entity Lifecycle & Guards
    // -------------------------------------------------------------------------
    private static void TestReturnDomainEntityLifecycleAndGuards()
    {
        Console.WriteLine("\n--- Testing Return Domain Entity Lifecycle & Guards ---");

        var ret = new Return { Id = 1, PackageId = 10 };
        Assert(ret.Status == ReturnStatus.Requested, "Initial Return.Status is Requested");
        Assert(ret.ApprovedAt == null, "Initial ApprovedAt is null");
        Assert(ret.CompletedAt == null, "Initial CompletedAt is null");
        Assert(ret.ReturnBoxId == null, "Initial ReturnBoxId is null");
        Assert(ret.ReturnDestinationBoxId == null, "Initial ReturnDestinationBoxId is null");

        // Approve from Requested
        ret.Approve(101, 102);
        Assert(ret.Status == ReturnStatus.Approved, "Return.Approve() transitions to Approved");
        Assert(ret.ApprovedAt.HasValue, "ApprovedAt is set");
        Assert(ret.ReturnBoxId == 101, "ReturnBoxId set to 101");
        Assert(ret.ReturnDestinationBoxId == 102, "ReturnDestinationBoxId set to 102");

        // Invalid: Approve from Approved throws
        AssertThrows<InvalidOperationException>(() => ret.Approve(103, 104), "Return.Approve() from Approved throws InvalidOperationException");

        // Ship from Approved
        ret.Ship(201);
        Assert(ret.Status == ReturnStatus.Shipped, "Return.Ship() transitions to Shipped");
        Assert(ret.ReturnBoxId == 201, "Return.Ship(201) updates ReturnBoxId");

        // Invalid: Ship from Shipped throws
        AssertThrows<InvalidOperationException>(() => ret.Ship(), "Return.Ship() from Shipped throws InvalidOperationException");

        // Complete from Shipped
        ret.Complete();
        Assert(ret.Status == ReturnStatus.Completed, "Return.Complete() transitions to Completed");
        Assert(ret.CompletedAt.HasValue, "CompletedAt is set");

        // Invalid: Complete from Completed throws
        AssertThrows<InvalidOperationException>(() => ret.Complete(), "Return.Complete() from Completed throws InvalidOperationException");

        // Invalid: Cancel from Completed throws
        AssertThrows<InvalidOperationException>(() => ret.Cancel(), "Return.Cancel() from Completed throws InvalidOperationException");

        // Reject from Requested
        var retReject = new Return { Id = 2, PackageId = 20 };
        retReject.Reject();
        Assert(retReject.Status == ReturnStatus.Rejected, "Return.Reject() transitions to Rejected");
        AssertThrows<InvalidOperationException>(() => retReject.Reject(), "Second Return.Reject() throws");
        AssertThrows<InvalidOperationException>(() => retReject.Approve(), "Return.Approve() from Rejected throws");

        // Cancel from Requested
        var retCancel1 = new Return { Id = 3, PackageId = 30 };
        retCancel1.Cancel();
        Assert(retCancel1.Status == ReturnStatus.Cancelled, "Return.Cancel() from Requested transitions to Cancelled");

        // Cancel from Approved
        var retCancel2 = new Return { Id = 4, PackageId = 40 };
        retCancel2.Approve(5, 6);
        retCancel2.Cancel();
        Assert(retCancel2.Status == ReturnStatus.Cancelled, "Return.Cancel() from Approved transitions to Cancelled");

        // Cancel from Shipped, Cancelled, Rejected must throw
        var retCancelShipped = new Return();
        retCancelShipped.Approve();
        retCancelShipped.Ship();
        AssertThrows<InvalidOperationException>(() => retCancelShipped.Cancel(), "Return.Cancel() from Shipped throws InvalidOperationException");

        AssertThrows<InvalidOperationException>(() => retCancel1.Cancel(), "Return.Cancel() from already Cancelled throws InvalidOperationException");
        AssertThrows<InvalidOperationException>(() => retReject.Cancel(), "Return.Cancel() from Rejected throws InvalidOperationException");
    }

    private static void TestReturnDomainBoundaryAndEdgeCases()
    {
        Console.WriteLine("\n--- Testing Return Boundary & Edge Cases ---");

        // Approve with nulls (optional box IDs)
        var ret = new Return();
        ret.Approve(null, null);
        Assert(ret.Status == ReturnStatus.Approved, "Return.Approve(null, null) succeeds");
        Assert(ret.ReturnBoxId == null, "ReturnBoxId remains null");
        Assert(ret.ReturnDestinationBoxId == null, "ReturnDestinationBoxId remains null");

        // Ship with null boxId
        ret.Ship(null);
        Assert(ret.Status == ReturnStatus.Shipped, "Return.Ship(null) succeeds");

        // Complete directly from Approved (allowed per contract)
        var retDirect = new Return();
        retDirect.Approve();
        retDirect.Complete();
        Assert(retDirect.Status == ReturnStatus.Completed, "Return.Complete() directly from Approved succeeds");

        // Complete from invalid states: Requested, Rejected, Cancelled
        var retReq = new Return();
        AssertThrows<InvalidOperationException>(() => retReq.Complete(), "Return.Complete() from Requested throws InvalidOperationException");

        var retRej = new Return();
        retRej.Reject();
        AssertThrows<InvalidOperationException>(() => retRej.Complete(), "Return.Complete() from Rejected throws InvalidOperationException");

        var retCanc = new Return();
        retCanc.Cancel();
        AssertThrows<InvalidOperationException>(() => retCanc.Complete(), "Return.Complete() from Cancelled throws InvalidOperationException");

        // Reason handling: empty, long string
        var longReason = new string('A', 2000);
        var retReason = new Return { Reason = longReason };
        Assert(retReason.Reason.Length == 2000, "Return.Reason accepts large strings");
    }

    // -------------------------------------------------------------------------
    // 6. Package Return Lifecycle Methods & Guards
    // -------------------------------------------------------------------------
    private static void TestPackageReturnLifecycleMethodsAndGuards()
    {
        Console.WriteLine("\n--- Testing Package Return Lifecycle Methods & Guards ---");

        // Test RequestReturn() is only allowed from Collected
        foreach (PackageStatus status in Enum.GetValues<PackageStatus>())
        {
            var p = CreatePackageInStatus(status);
            if (status == PackageStatus.Collected)
            {
                p.RequestReturn();
                Assert(p.Status == PackageStatus.ReturnRequested, "Package.RequestReturn() from Collected -> ReturnRequested");
            }
            else
            {
                AssertThrows<InvalidOperationException>(() => p.RequestReturn(), $"Package.RequestReturn() from {status} throws InvalidOperationException");
            }
        }

        // Test ApproveReturn() is only allowed from ReturnRequested
        foreach (PackageStatus status in Enum.GetValues<PackageStatus>())
        {
            var p = CreatePackageInStatus(status);
            if (status == PackageStatus.ReturnRequested)
            {
                p.ApproveReturn();
                Assert(p.Status == PackageStatus.ReturnInProgress, "Package.ApproveReturn() from ReturnRequested -> ReturnInProgress");
            }
            else
            {
                AssertThrows<InvalidOperationException>(() => p.ApproveReturn(), $"Package.ApproveReturn() from {status} throws InvalidOperationException");
            }
        }

        // Test RejectReturn() is only allowed from ReturnRequested and reverts to Collected
        foreach (PackageStatus status in Enum.GetValues<PackageStatus>())
        {
            var p = CreatePackageInStatus(status);
            if (status == PackageStatus.ReturnRequested)
            {
                p.RejectReturn();
                Assert(p.Status == PackageStatus.Collected, "Package.RejectReturn() from ReturnRequested -> Collected");
            }
            else
            {
                AssertThrows<InvalidOperationException>(() => p.RejectReturn(), $"Package.RejectReturn() from {status} throws InvalidOperationException");
            }
        }

        // Test ShipReturn() is only allowed from ReturnInProgress
        foreach (PackageStatus status in Enum.GetValues<PackageStatus>())
        {
            var p = CreatePackageInStatus(status);
            if (status == PackageStatus.ReturnInProgress)
            {
                p.ShipReturn();
                Assert(p.Status == PackageStatus.ReturnInProgress, "Package.ShipReturn() from ReturnInProgress -> ReturnInProgress");
            }
            else
            {
                AssertThrows<InvalidOperationException>(() => p.ShipReturn(), $"Package.ShipReturn() from {status} throws InvalidOperationException");
            }
        }

        // Test CompleteReturn() is only allowed from ReturnInProgress and transitions to Returned
        foreach (PackageStatus status in Enum.GetValues<PackageStatus>())
        {
            var p = CreatePackageInStatus(status);
            if (status == PackageStatus.ReturnInProgress)
            {
                p.CompleteReturn();
                Assert(p.Status == PackageStatus.Returned, "Package.CompleteReturn() from ReturnInProgress -> Returned");
            }
            else
            {
                AssertThrows<InvalidOperationException>(() => p.CompleteReturn(), $"Package.CompleteReturn() from {status} throws InvalidOperationException");
            }
        }

        // Test CancelReturn() is allowed from ReturnRequested and ReturnInProgress, reverting to Collected
        var pCancel1 = CreatePackageInStatus(PackageStatus.ReturnRequested);
        pCancel1.CancelReturn();
        Assert(pCancel1.Status == PackageStatus.Collected, "Package.CancelReturn() from ReturnRequested -> Collected");

        var pCancel2 = CreatePackageInStatus(PackageStatus.ReturnInProgress);
        pCancel2.CancelReturn();
        Assert(pCancel2.Status == PackageStatus.Collected, "Package.CancelReturn() from ReturnInProgress -> Collected");

        var pCancelInvalid = CreatePackageInStatus(PackageStatus.Ordered);
        AssertThrows<InvalidOperationException>(() => pCancelInvalid.CancelReturn(), "Package.CancelReturn() from Ordered throws InvalidOperationException");
    }

    private static void TestPackageCancelAndRejectGuardsDuringReturns()
    {
        Console.WriteLine("\n--- Testing Package.Cancel() & Reject() Guards During Returns ---");

        var pReq = CreatePackageInStatus(PackageStatus.ReturnRequested);
        AssertThrows<InvalidOperationException>(() => pReq.Cancel(), "Package.Cancel() from ReturnRequested throws InvalidOperationException");
        AssertThrows<InvalidOperationException>(() => pReq.Reject(), "Package.Reject() from ReturnRequested throws InvalidOperationException");

        var pProg = CreatePackageInStatus(PackageStatus.ReturnInProgress);
        AssertThrows<InvalidOperationException>(() => pProg.Cancel(), "Package.Cancel() from ReturnInProgress throws InvalidOperationException");
        AssertThrows<InvalidOperationException>(() => pProg.Reject(), "Package.Reject() from ReturnInProgress throws InvalidOperationException");

        var pRet = CreatePackageInStatus(PackageStatus.Returned);
        AssertThrows<InvalidOperationException>(() => pRet.Cancel(), "Package.Cancel() from Returned throws InvalidOperationException");
        AssertThrows<InvalidOperationException>(() => pRet.Reject(), "Package.Reject() from Returned throws InvalidOperationException");
    }

    private static Package CreatePackageInStatus(PackageStatus targetStatus)
    {
        var p = new Package();
        var statusProp = typeof(Package).GetProperty(nameof(Package.Status))!;
        statusProp.SetValue(p, targetStatus);
        return p;
    }

    // -------------------------------------------------------------------------
    // 7. Enums Completeness
    // -------------------------------------------------------------------------
    private static void TestEnumsCompleteness()
    {
        Console.WriteLine("\n--- Testing Enums Completeness ---");

        var returnStatuses = Enum.GetNames<ReturnStatus>();
        var requiredReturnStatuses = new[] { "Requested", "Approved", "Rejected", "Shipped", "Completed", "Cancelled" };
        foreach (var s in requiredReturnStatuses)
        {
            Assert(returnStatuses.Contains(s), $"ReturnStatus contains '{s}'");
        }

        var paymentStatuses = Enum.GetNames<PaymentStatus>();
        var requiredPaymentStatuses = new[] { "Pending", "Completed", "Failed", "Refunded" };
        foreach (var s in requiredPaymentStatuses)
        {
            Assert(paymentStatuses.Contains(s), $"PaymentStatus contains '{s}'");
        }

        var packageStatuses = Enum.GetNames<PackageStatus>();
        var requiredPackageStatuses = new[] { "ReturnRequested", "ReturnInProgress", "Returned" };
        foreach (var s in requiredPackageStatuses)
        {
            Assert(packageStatuses.Contains(s), $"PackageStatus contains '{s}'");
        }
    }
}
