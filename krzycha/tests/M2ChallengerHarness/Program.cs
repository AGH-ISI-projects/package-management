using System;
using System.Collections.Generic;
using System.Reflection;
using PackageDeliverySystem.Core;

namespace M2ChallengerHarness;

public static class Program
{
    private static int _passedCount = 0;
    private static int _failedCount = 0;
    private static readonly List<string> Failures = new();

    public static int Main()
    {
        Console.WriteLine("=================================================");
        Console.WriteLine("M2 CHALLENGER EMPIRICAL VERIFICATION HARNESS");
        Console.WriteLine("Target: PackageDeliverySystem.Core (.NET 10)");
        Console.WriteLine("=================================================");

        RunPackageStateMachineTests();
        RunReturnStateMachineTests();
        RunPaymentRefundTests();
        RunEncapsulationAndPropertiesTests();
        RunInterfaceContractReflectionTests();
        RunFullLifecycleScenarioTests();

        Console.WriteLine("=================================================");
        Console.WriteLine($"TOTAL TESTS PASSED: {_passedCount}");
        Console.WriteLine($"TOTAL TESTS FAILED: {_failedCount}");
        Console.WriteLine("=================================================");

        if (_failedCount > 0)
        {
            Console.WriteLine("FAILURE SUMMARY:");
            foreach (var f in Failures)
            {
                Console.WriteLine($" [FAIL] {f}");
            }
            return 1;
        }

        Console.WriteLine("ALL EMPIRICAL TESTS PASSED!");
        return 0;
    }

    private static void Assert(bool condition, string testName, string? details = null)
    {
        if (condition)
        {
            _passedCount++;
            Console.WriteLine($"[PASS] {testName}");
        }
        else
        {
            _failedCount++;
            string msg = $"{testName} - {details ?? "Condition was false"}";
            Failures.Add(msg);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAIL] {msg}");
            Console.ResetColor();
        }
    }

    private static void SetPackageStatus(Package package, PackageStatus status)
    {
        var prop = typeof(Package).GetProperty(nameof(Package.Status), BindingFlags.Public | BindingFlags.Instance);
        var setMethod = prop?.GetSetMethod(nonPublic: true);
        if (setMethod != null)
        {
            setMethod.Invoke(package, new object[] { status });
        }
        else
        {
            var field = typeof(Package).GetField("<Status>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
            field?.SetValue(package, status);
        }
    }

    private static void SetReturnStatus(Return ret, ReturnStatus status)
    {
        var prop = typeof(Return).GetProperty(nameof(Return.Status), BindingFlags.Public | BindingFlags.Instance);
        var setMethod = prop?.GetSetMethod(nonPublic: true);
        if (setMethod != null)
        {
            setMethod.Invoke(ret, new object[] { status });
        }
        else
        {
            var field = typeof(Return).GetField("<Status>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
            field?.SetValue(ret, status);
        }
    }

    private static void SetPaymentStatus(Payment payment, PaymentStatus status)
    {
        var prop = typeof(Payment).GetProperty(nameof(Payment.Status), BindingFlags.Public | BindingFlags.Instance);
        var setMethod = prop?.GetSetMethod(nonPublic: true);
        if (setMethod != null)
        {
            setMethod.Invoke(payment, new object[] { status });
        }
        else
        {
            var field = typeof(Payment).GetField("<Status>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
            field?.SetValue(payment, status);
        }
    }

    private static void RunPackageStateMachineTests()
    {
        Console.WriteLine("\n--- Testing Package.cs State Machine ---");

        var allStatuses = (PackageStatus[])Enum.GetValues(typeof(PackageStatus));

        // 1. RequestReturn: valid only from Collected
        foreach (var status in allStatuses)
        {
            var pkg = new Package();
            SetPackageStatus(pkg, status);

            if (status == PackageStatus.Collected)
            {
                pkg.RequestReturn();
                Assert(pkg.Status == PackageStatus.ReturnRequested,
                    $"Package.RequestReturn() from {status} -> ReturnRequested",
                    $"Expected ReturnRequested but got {pkg.Status}");
            }
            else
            {
                bool threw = false;
                string exMsg = "";
                try
                {
                    pkg.RequestReturn();
                }
                catch (InvalidOperationException ex)
                {
                    threw = true;
                    exMsg = ex.Message;
                }
                catch (Exception ex)
                {
                    threw = false;
                    exMsg = $"Wrong exception type: {ex.GetType().Name}";
                }

                Assert(threw,
                    $"Package.RequestReturn() throws InvalidOperationException from {status}",
                    $"Exception message: {exMsg}");
                Assert(exMsg.Contains("Only collected packages can be returned") &&
                       exMsg.Contains($"Cannot request return for package in status {status}"),
                    $"Package.RequestReturn() message validation from {status}",
                    $"Message was: '{exMsg}'");
            }
        }

        // 2. ApproveReturn: valid only from ReturnRequested
        foreach (var status in allStatuses)
        {
            var pkg = new Package();
            SetPackageStatus(pkg, status);

            if (status == PackageStatus.ReturnRequested)
            {
                pkg.ApproveReturn();
                Assert(pkg.Status == PackageStatus.ReturnInProgress,
                    $"Package.ApproveReturn() from {status} -> ReturnInProgress",
                    $"Expected ReturnInProgress but got {pkg.Status}");
            }
            else
            {
                bool threw = false;
                string exMsg = "";
                try
                {
                    pkg.ApproveReturn();
                }
                catch (InvalidOperationException ex)
                {
                    threw = true;
                    exMsg = ex.Message;
                }

                Assert(threw,
                    $"Package.ApproveReturn() throws InvalidOperationException from {status}",
                    $"Expected exception from {status}, message: {exMsg}");
                Assert(exMsg.Contains($"Cannot approve return for package in status {status}"),
                    $"Package.ApproveReturn() message matches pattern from {status}",
                    $"Message was: '{exMsg}'");
            }
        }

        // 3. RejectReturn: valid only from ReturnRequested -> Collected
        foreach (var status in allStatuses)
        {
            var pkg = new Package();
            SetPackageStatus(pkg, status);

            if (status == PackageStatus.ReturnRequested)
            {
                pkg.RejectReturn();
                Assert(pkg.Status == PackageStatus.Collected,
                    $"Package.RejectReturn() from {status} -> Collected",
                    $"Expected Collected but got {pkg.Status}");
            }
            else
            {
                bool threw = false;
                string exMsg = "";
                try
                {
                    pkg.RejectReturn();
                }
                catch (InvalidOperationException ex)
                {
                    threw = true;
                    exMsg = ex.Message;
                }

                Assert(threw,
                    $"Package.RejectReturn() throws InvalidOperationException from {status}",
                    $"Message: {exMsg}");
                Assert(exMsg.Contains($"Cannot reject return for package in status {status}"),
                    $"Package.RejectReturn() message matches pattern from {status}",
                    $"Message was: '{exMsg}'");
            }
        }

        // 4. ShipReturn: valid only from ReturnInProgress -> ReturnInProgress
        foreach (var status in allStatuses)
        {
            var pkg = new Package();
            SetPackageStatus(pkg, status);

            if (status == PackageStatus.ReturnInProgress)
            {
                pkg.ShipReturn();
                Assert(pkg.Status == PackageStatus.ReturnInProgress,
                    $"Package.ShipReturn() from {status} -> ReturnInProgress",
                    $"Expected ReturnInProgress but got {pkg.Status}");
            }
            else
            {
                bool threw = false;
                string exMsg = "";
                try
                {
                    pkg.ShipReturn();
                }
                catch (InvalidOperationException ex)
                {
                    threw = true;
                    exMsg = ex.Message;
                }

                Assert(threw,
                    $"Package.ShipReturn() throws InvalidOperationException from {status}",
                    $"Message: {exMsg}");
                Assert(exMsg.Contains($"Cannot ship return for package in status {status}"),
                    $"Package.ShipReturn() message matches pattern from {status}",
                    $"Message was: '{exMsg}'");
            }
        }

        // 5. CompleteReturn: valid only from ReturnInProgress -> Returned
        foreach (var status in allStatuses)
        {
            var pkg = new Package();
            SetPackageStatus(pkg, status);

            if (status == PackageStatus.ReturnInProgress)
            {
                pkg.CompleteReturn();
                Assert(pkg.Status == PackageStatus.Returned,
                    $"Package.CompleteReturn() from {status} -> Returned",
                    $"Expected Returned but got {pkg.Status}");
            }
            else
            {
                bool threw = false;
                string exMsg = "";
                try
                {
                    pkg.CompleteReturn();
                }
                catch (InvalidOperationException ex)
                {
                    threw = true;
                    exMsg = ex.Message;
                }

                Assert(threw,
                    $"Package.CompleteReturn() throws InvalidOperationException from {status}",
                    $"Message: {exMsg}");
                Assert(exMsg.Contains($"Cannot complete return for package in status {status}"),
                    $"Package.CompleteReturn() message matches pattern from {status}",
                    $"Message was: '{exMsg}'");
            }
        }

        // 6. CancelReturn: valid from ReturnRequested and ReturnInProgress -> Collected
        foreach (var status in allStatuses)
        {
            var pkg = new Package();
            SetPackageStatus(pkg, status);

            if (status == PackageStatus.ReturnRequested || status == PackageStatus.ReturnInProgress)
            {
                pkg.CancelReturn();
                Assert(pkg.Status == PackageStatus.Collected,
                    $"Package.CancelReturn() from {status} -> Collected",
                    $"Expected Collected but got {pkg.Status}");
            }
            else
            {
                bool threw = false;
                string exMsg = "";
                try
                {
                    pkg.CancelReturn();
                }
                catch (InvalidOperationException ex)
                {
                    threw = true;
                    exMsg = ex.Message;
                }

                Assert(threw,
                    $"Package.CancelReturn() throws InvalidOperationException from {status}",
                    $"Message: {exMsg}");
                Assert(exMsg.Contains($"Cannot cancel return for package in status {status}"),
                    $"Package.CancelReturn() message matches pattern from {status}",
                    $"Message was: '{exMsg}'");
            }
        }

        // 7. Test invalid forward actions while in Return states
        var returnStatuses = new[] { PackageStatus.ReturnRequested, PackageStatus.ReturnInProgress, PackageStatus.Returned };
        foreach (var rStatus in returnStatuses)
        {
            var pkg = new Package();
            SetPackageStatus(pkg, rStatus);

            AssertThrows<InvalidOperationException>(() => pkg.Accept(), $"Package.Accept() blocked in {rStatus}");
            AssertThrows<InvalidOperationException>(() => pkg.ConfirmDeposit(), $"Package.ConfirmDeposit() blocked in {rStatus}");
            AssertThrows<InvalidOperationException>(() => pkg.Prepare(), $"Package.Prepare() blocked in {rStatus}");
            AssertThrows<InvalidOperationException>(() => pkg.Ship(), $"Package.Ship() blocked in {rStatus}");
            AssertThrows<InvalidOperationException>(() => pkg.Deliver(), $"Package.Deliver() blocked in {rStatus}");
            AssertThrows<InvalidOperationException>(() => pkg.Collect("123456"), $"Package.Collect() blocked in {rStatus}");
            AssertThrows<InvalidOperationException>(() => pkg.Reject(), $"Package.Reject() blocked in {rStatus}");
            AssertThrows<InvalidOperationException>(() => pkg.Cancel(), $"Package.Cancel() blocked in {rStatus}");
        }
    }

    private static void RunReturnStateMachineTests()
    {
        Console.WriteLine("\n--- Testing Return.cs State Machine ---");

        var allStatuses = (ReturnStatus[])Enum.GetValues(typeof(ReturnStatus));

        // 1. Approve: valid only from Requested -> Approved
        foreach (var status in allStatuses)
        {
            var ret = new Return();
            SetReturnStatus(ret, status);

            if (status == ReturnStatus.Requested)
            {
                DateTime before = DateTime.UtcNow.AddSeconds(-1);
                ret.Approve(10, 20);
                DateTime after = DateTime.UtcNow.AddSeconds(1);

                Assert(ret.Status == ReturnStatus.Approved,
                    $"Return.Approve() from {status} -> Approved",
                    $"Expected Approved, got {ret.Status}");
                Assert(ret.ReturnBoxId == 10,
                    "Return.Approve() assigned ReturnBoxId",
                    $"Expected 10, got {ret.ReturnBoxId}");
                Assert(ret.ReturnDestinationBoxId == 20,
                    "Return.Approve() assigned ReturnDestinationBoxId",
                    $"Expected 20, got {ret.ReturnDestinationBoxId}");
                Assert(ret.ApprovedAt.HasValue && ret.ApprovedAt.Value >= before && ret.ApprovedAt.Value <= after,
                    "Return.Approve() recorded ApprovedAt timestamp",
                    $"ApprovedAt was {ret.ApprovedAt}");

                // Also test Approve with nulls
                var retNulls = new Return();
                retNulls.Approve();
                Assert(retNulls.Status == ReturnStatus.Approved && retNulls.ReturnBoxId == null && retNulls.ReturnDestinationBoxId == null,
                    "Return.Approve(null, null) succeeds without box assignment",
                    $"Status: {retNulls.Status}, Box: {retNulls.ReturnBoxId}, DestBox: {retNulls.ReturnDestinationBoxId}");
            }
            else
            {
                bool threw = false;
                string exMsg = "";
                try
                {
                    ret.Approve(10, 20);
                }
                catch (InvalidOperationException ex)
                {
                    threw = true;
                    exMsg = ex.Message;
                }

                Assert(threw,
                    $"Return.Approve() throws InvalidOperationException from {status}",
                    $"Message: {exMsg}");
                Assert(exMsg.Contains($"Cannot approve return in status {status}"),
                    $"Return.Approve() error message check from {status}",
                    $"Message was: '{exMsg}'");
            }
        }

        // 2. Reject: valid only from Requested -> Rejected
        foreach (var status in allStatuses)
        {
            var ret = new Return();
            SetReturnStatus(ret, status);

            if (status == ReturnStatus.Requested)
            {
                ret.Reject();
                Assert(ret.Status == ReturnStatus.Rejected,
                    $"Return.Reject() from {status} -> Rejected",
                    $"Expected Rejected, got {ret.Status}");
            }
            else
            {
                bool threw = false;
                string exMsg = "";
                try
                {
                    ret.Reject();
                }
                catch (InvalidOperationException ex)
                {
                    threw = true;
                    exMsg = ex.Message;
                }

                Assert(threw,
                    $"Return.Reject() throws InvalidOperationException from {status}",
                    $"Message: {exMsg}");
                Assert(exMsg.Contains($"Cannot reject return in status {status}"),
                    $"Return.Reject() error message check from {status}",
                    $"Message was: '{exMsg}'");
            }
        }

        // 3. Ship: valid only from Approved -> Shipped
        foreach (var status in allStatuses)
        {
            var ret = new Return();
            SetReturnStatus(ret, status);

            if (status == ReturnStatus.Approved)
            {
                ret.Ship(35);
                Assert(ret.Status == ReturnStatus.Shipped,
                    $"Return.Ship() from {status} -> Shipped",
                    $"Expected Shipped, got {ret.Status}");
                Assert(ret.ReturnBoxId == 35,
                    "Return.Ship() assigned boxId",
                    $"Expected 35, got {ret.ReturnBoxId}");

                // Ship without boxId overload
                var retNoBox = new Return();
                SetReturnStatus(retNoBox, ReturnStatus.Approved);
                retNoBox.ReturnBoxId = 12;
                retNoBox.Ship();
                Assert(retNoBox.Status == ReturnStatus.Shipped && retNoBox.ReturnBoxId == 12,
                    "Return.Ship() without boxId preserves existing ReturnBoxId",
                    $"Status: {retNoBox.Status}, BoxId: {retNoBox.ReturnBoxId}");
            }
            else
            {
                bool threw = false;
                string exMsg = "";
                try
                {
                    ret.Ship(35);
                }
                catch (InvalidOperationException ex)
                {
                    threw = true;
                    exMsg = ex.Message;
                }

                Assert(threw,
                    $"Return.Ship() throws InvalidOperationException from {status}",
                    $"Message: {exMsg}");
                Assert(exMsg.Contains($"Cannot ship return in status {status}"),
                    $"Return.Ship() error message check from {status}",
                    $"Message was: '{exMsg}'");
            }
        }

        // 4. Complete: valid from Shipped and Approved -> Completed
        foreach (var status in allStatuses)
        {
            var ret = new Return();
            SetReturnStatus(ret, status);

            if (status == ReturnStatus.Shipped || status == ReturnStatus.Approved)
            {
                DateTime before = DateTime.UtcNow.AddSeconds(-1);
                ret.Complete();
                DateTime after = DateTime.UtcNow.AddSeconds(1);

                Assert(ret.Status == ReturnStatus.Completed,
                    $"Return.Complete() from {status} -> Completed",
                    $"Expected Completed, got {ret.Status}");
                Assert(ret.CompletedAt.HasValue && ret.CompletedAt.Value >= before && ret.CompletedAt.Value <= after,
                    $"Return.Complete() sets CompletedAt timestamp from {status}",
                    $"CompletedAt: {ret.CompletedAt}");
            }
            else
            {
                bool threw = false;
                string exMsg = "";
                try
                {
                    ret.Complete();
                }
                catch (InvalidOperationException ex)
                {
                    threw = true;
                    exMsg = ex.Message;
                }

                Assert(threw,
                    $"Return.Complete() throws InvalidOperationException from {status}",
                    $"Message: {exMsg}");
                Assert(exMsg.Contains($"Cannot complete return in status {status}"),
                    $"Return.Complete() error message check from {status}",
                    $"Message was: '{exMsg}'");
            }
        }

        // 5. Cancel: valid from Requested and Approved -> Cancelled
        foreach (var status in allStatuses)
        {
            var ret = new Return();
            SetReturnStatus(ret, status);

            if (status == ReturnStatus.Requested || status == ReturnStatus.Approved)
            {
                ret.Cancel();
                Assert(ret.Status == ReturnStatus.Cancelled,
                    $"Return.Cancel() from {status} -> Cancelled",
                    $"Expected Cancelled, got {ret.Status}");
            }
            else
            {
                bool threw = false;
                string exMsg = "";
                try
                {
                    ret.Cancel();
                }
                catch (InvalidOperationException ex)
                {
                    threw = true;
                    exMsg = ex.Message;
                }

                Assert(threw,
                    $"Return.Cancel() throws InvalidOperationException from {status}",
                    $"Message: {exMsg}");
                Assert(exMsg.Contains($"Cannot cancel return in status {status}"),
                    $"Return.Cancel() error message check from {status}",
                    $"Message was: '{exMsg}'");
            }
        }
    }

    private static void RunPaymentRefundTests()
    {
        Console.WriteLine("\n--- Testing Payment.cs Refund Guard ---");

        var allStatuses = (PaymentStatus[])Enum.GetValues(typeof(PaymentStatus));

        foreach (var status in allStatuses)
        {
            var payment = new Payment { Amount = 100m, ExternalReference = "PAY-001" };
            SetPaymentStatus(payment, status);

            if (status == PaymentStatus.Completed)
            {
                payment.Refund();
                Assert(payment.Status == PaymentStatus.Refunded,
                    $"Payment.Refund() from Completed -> Refunded",
                    $"Expected Refunded, got {payment.Status}");
            }
            else
            {
                bool threw = false;
                string exMsg = "";
                try
                {
                    payment.Refund();
                }
                catch (InvalidOperationException ex)
                {
                    threw = true;
                    exMsg = ex.Message;
                }

                Assert(threw,
                    $"Payment.Refund() throws InvalidOperationException from {status}",
                    $"Message: {exMsg}");
                Assert(exMsg.Contains($"Cannot refund payment in status {status}"),
                    $"Payment.Refund() error message check from {status}",
                    $"Message was: '{exMsg}'");
            }
        }
    }

    private static void RunEncapsulationAndPropertiesTests()
    {
        Console.WriteLine("\n--- Testing Encapsulation & Model Properties ---");

        // Package.Status has private set
        var pkgStatusProp = typeof(Package).GetProperty(nameof(Package.Status));
        Assert(pkgStatusProp != null && pkgStatusProp.GetSetMethod(false) == null && pkgStatusProp.GetSetMethod(true) != null,
            "Package.Status has public getter and private setter",
            "Property setter is not private");

        // Package.OrderGroupId exists as int?
        var pkgOrderGroupProp = typeof(Package).GetProperty(nameof(Package.OrderGroupId));
        Assert(pkgOrderGroupProp != null && pkgOrderGroupProp.PropertyType == typeof(int?),
            "Package.OrderGroupId property exists with type int?",
            $"Type is {pkgOrderGroupProp?.PropertyType}");

        // Package.Return navigation property exists as Return?
        var pkgReturnProp = typeof(Package).GetProperty(nameof(Package.Return));
        Assert(pkgReturnProp != null && pkgReturnProp.PropertyType == typeof(Return),
            "Package.Return property exists with type Return?",
            $"Type is {pkgReturnProp?.PropertyType}");

        // Return.Status has private setter
        var retStatusProp = typeof(Return).GetProperty(nameof(Return.Status));
        Assert(retStatusProp != null && retStatusProp.GetSetMethod(false) == null && retStatusProp.GetSetMethod(true) != null,
            "Return.Status has public getter and private setter",
            "Property setter is not private");

        // Return.ApprovedAt has private setter
        var retApprovedAtProp = typeof(Return).GetProperty(nameof(Return.ApprovedAt));
        Assert(retApprovedAtProp != null && retApprovedAtProp.GetSetMethod(false) == null && retApprovedAtProp.GetSetMethod(true) != null,
            "Return.ApprovedAt has public getter and private setter",
            "Property setter is not private");

        // Return.CompletedAt has private setter
        var retCompletedAtProp = typeof(Return).GetProperty(nameof(Return.CompletedAt));
        Assert(retCompletedAtProp != null && retCompletedAtProp.GetSetMethod(false) == null && retCompletedAtProp.GetSetMethod(true) != null,
            "Return.CompletedAt has public getter and private setter",
            "Property setter is not private");

        // Payment.Status has private setter
        var payStatusProp = typeof(Payment).GetProperty(nameof(Payment.Status));
        Assert(payStatusProp != null && payStatusProp.GetSetMethod(false) == null && payStatusProp.GetSetMethod(true) != null,
            "Payment.Status has public getter and private setter",
            "Property setter is not private");
    }

    private static void RunInterfaceContractReflectionTests()
    {
        Console.WriteLine("\n--- Testing Interface Contracts ---");

        // IReturnRepository methods
        var repoType = typeof(IReturnRepository);
        Assert(repoType.IsInterface, "IReturnRepository is an interface");

        string[] repoMethods = {
            "GetByIdAsync", "AddAsync", "UpdateAsync", "DeleteAsync",
            "GetAllAsync", "GetByPackageIdAsync", "GetActiveReturnsByPackageIdAsync", "GetByStatusAsync"
        };
        foreach (var m in repoMethods)
        {
            var method = repoType.GetMethod(m);
            Assert(method != null, $"IReturnRepository has method '{m}'", $"Method {m} not found on IReturnRepository");
        }

        // IReturnService methods
        var serviceType = typeof(IReturnService);
        Assert(serviceType.IsInterface, "IReturnService is an interface");

        string[] serviceMethods = {
            "RequestReturnAsync", "ApproveReturnAsync", "RejectReturnAsync",
            "ShipReturnAsync", "CompleteReturnAsync", "CancelReturnAsync",
            "GetReturnablePackagesAsync", "GetReturnByIdAsync"
        };
        foreach (var m in serviceMethods)
        {
            var found = false;
            foreach (var met in serviceType.GetMethods())
            {
                if (met.Name == m) { found = true; break; }
            }
            Assert(found, $"IReturnService has method '{m}'", $"Method {m} not found on IReturnService");
        }
    }

    private static void RunFullLifecycleScenarioTests()
    {
        Console.WriteLine("\n--- Testing Full Lifecycle Scenarios & Stress Tests ---");

        // Scenario 1: Standard Full Return Happy Path
        {
            var pkg = new Package { Id = 101, OrderGroupId = 55 };
            SetPackageStatus(pkg, PackageStatus.Collected);

            var ret = new Return { Id = 1, PackageId = pkg.Id, Package = pkg, Reason = "Wrong size" };
            pkg.Return = ret;

            pkg.RequestReturn();
            Assert(pkg.Status == PackageStatus.ReturnRequested, "Scenario 1: pkg -> ReturnRequested");
            Assert(ret.Status == ReturnStatus.Requested, "Scenario 1: ret is Requested");

            pkg.ApproveReturn();
            ret.Approve(10, 20);
            Assert(pkg.Status == PackageStatus.ReturnInProgress, "Scenario 1: pkg -> ReturnInProgress");
            Assert(ret.Status == ReturnStatus.Approved, "Scenario 1: ret -> Approved");
            Assert(ret.ReturnBoxId == 10 && ret.ReturnDestinationBoxId == 20, "Scenario 1: ret boxes assigned");

            pkg.ShipReturn();
            ret.Ship();
            Assert(pkg.Status == PackageStatus.ReturnInProgress, "Scenario 1: pkg remains ReturnInProgress during ship");
            Assert(ret.Status == ReturnStatus.Shipped, "Scenario 1: ret -> Shipped");

            pkg.CompleteReturn();
            ret.Complete();
            Assert(pkg.Status == PackageStatus.Returned, "Scenario 1: pkg -> Returned");
            Assert(ret.Status == ReturnStatus.Completed, "Scenario 1: ret -> Completed");
            Assert(ret.CompletedAt.HasValue, "Scenario 1: ret CompletedAt recorded");
        }

        // Scenario 2: Partial Return in Multi-Package Order Group
        {
            var pkg1 = new Package { Id = 201, OrderGroupId = 88 };
            var pkg2 = new Package { Id = 202, OrderGroupId = 88 };
            SetPackageStatus(pkg1, PackageStatus.Collected);
            SetPackageStatus(pkg2, PackageStatus.Collected);

            pkg1.RequestReturn();
            var ret1 = new Return { Id = 10, PackageId = pkg1.Id, Package = pkg1, Reason = "Defective" };
            pkg1.Return = ret1;

            Assert(pkg1.Status == PackageStatus.ReturnRequested, "Scenario 2: pkg1 in ReturnRequested");
            Assert(pkg2.Status == PackageStatus.Collected, "Scenario 2: pkg2 remains Collected untouched");

            pkg1.ApproveReturn();
            ret1.Approve(1, 2);
            Assert(pkg1.Status == PackageStatus.ReturnInProgress, "Scenario 2: pkg1 in ReturnInProgress");
            Assert(pkg2.Status == PackageStatus.Collected, "Scenario 2: pkg2 still Collected");

            pkg1.CompleteReturn();
            ret1.Complete();
            Assert(pkg1.Status == PackageStatus.Returned, "Scenario 2: pkg1 Returned");
            Assert(pkg2.Status == PackageStatus.Collected, "Scenario 2: pkg2 remains Collected throughout partial return");
        }

        // Scenario 3: Return Rejection and Re-Request Flow
        {
            var pkg = new Package { Id = 301 };
            SetPackageStatus(pkg, PackageStatus.Collected);

            var ret1 = new Return { Id = 21, PackageId = pkg.Id, Package = pkg, Reason = "Changed mind" };
            pkg.Return = ret1;

            pkg.RequestReturn();
            Assert(pkg.Status == PackageStatus.ReturnRequested, "Scenario 3: pkg -> ReturnRequested");

            pkg.RejectReturn();
            ret1.Reject();
            Assert(pkg.Status == PackageStatus.Collected, "Scenario 3: pkg reverts to Collected on rejection");
            Assert(ret1.Status == ReturnStatus.Rejected, "Scenario 3: ret1 is Rejected");

            // Customer re-submits a valid return request
            var ret2 = new Return { Id = 22, PackageId = pkg.Id, Package = pkg, Reason = "Damaged during shipment" };
            pkg.Return = ret2;
            pkg.RequestReturn();
            Assert(pkg.Status == PackageStatus.ReturnRequested, "Scenario 3: pkg can be re-requested after rejection");
            pkg.ApproveReturn();
            ret2.Approve(5, 6);
            Assert(pkg.Status == PackageStatus.ReturnInProgress, "Scenario 3: pkg can be approved on second attempt");
        }

        // Scenario 4: Return Cancellation from Requested vs ReturnInProgress
        {
            var pkgA = new Package { Id = 401 };
            SetPackageStatus(pkgA, PackageStatus.Collected);
            var retA = new Return { Id = 31, PackageId = pkgA.Id, Package = pkgA };
            pkgA.Return = retA;
            pkgA.RequestReturn();
            pkgA.CancelReturn();
            retA.Cancel();
            Assert(pkgA.Status == PackageStatus.Collected, "Scenario 4A: CancelReturn from ReturnRequested -> Collected");
            Assert(retA.Status == ReturnStatus.Cancelled, "Scenario 4A: retA Cancelled");

            var pkgB = new Package { Id = 402 };
            SetPackageStatus(pkgB, PackageStatus.Collected);
            var retB = new Return { Id = 32, PackageId = pkgB.Id, Package = pkgB };
            pkgB.Return = retB;
            pkgB.RequestReturn();
            pkgB.ApproveReturn();
            retB.Approve(3, 4);
            pkgB.CancelReturn();
            retB.Cancel();
            Assert(pkgB.Status == PackageStatus.Collected, "Scenario 4B: CancelReturn from ReturnInProgress -> Collected");
            Assert(retB.Status == ReturnStatus.Cancelled, "Scenario 4B: retB Cancelled");
        }

        // Scenario 5: Direct Completion from Approved (Dropoff and merchant acceptance without separate courier transit)
        {
            var retDirect = new Return { Id = 501 };
            retDirect.Approve(1, 2);
            retDirect.Complete();
            Assert(retDirect.Status == ReturnStatus.Completed, "Scenario 5: Return direct Complete from Approved");
            Assert(retDirect.CompletedAt.HasValue, "Scenario 5: Return CompletedAt set on direct complete");
        }
    }

    private static void AssertThrows<TException>(Action action, string testName) where TException : Exception
    {
        try
        {
            action();
            _failedCount++;
            string msg = $"{testName} - Expected {typeof(TException).Name} but no exception was thrown";
            Failures.Add(msg);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAIL] {msg}");
            Console.ResetColor();
        }
        catch (TException)
        {
            _passedCount++;
            Console.WriteLine($"[PASS] {testName}");
        }
        catch (Exception ex)
        {
            _failedCount++;
            string msg = $"{testName} - Expected {typeof(TException).Name} but got {ex.GetType().Name}: {ex.Message}";
            Failures.Add(msg);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAIL] {msg}");
            Console.ResetColor();
        }
    }
}
