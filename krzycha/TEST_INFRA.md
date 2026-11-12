# Test Infrastructure Specification (`TEST_INFRA.md`)

**Milestone**: ME2E — E2E Testing Track  
**Platform**: .NET 10 / C# 14 / xUnit / Moq / Pure In-Memory Abstractions  
**Workspace**: `krzycha` (Strict isolation from `nasz/`)

---

## 1. Overview & Architectural Goals

The Package Delivery System test suite provides comprehensive, automated quality assurance across two distinct testing paradigms:

1. **Isolated Unit Tests** (`tests/PackageDeliverySystem.Tests/PackageLifecycleTests.cs` and `ReturnTests.cs`):
   - Localized, white-box tests covering specific domain lifecycle transitions (T1–T20) and return workflows (R1–R8).
   - Mock immediate collaborators using Moq.
2. **Opaque-Box End-to-End (E2E) Tests** (`tests/PackageDeliverySystem.Tests/E2E/`):
   - Comprehensive, requirement-driven, black-box scenario tests covering all 37 features from `ORIGINAL_REQUEST.md` and `PROJECT.md`.
   - Test through public service interfaces without bypassing validations or peeking into internal database rows.
   - Organized into a structured 4-Tier test hierarchy:
     - **Tier 1 — Feature Coverage**: Happy-path validation for all 7 feature areas (35 tests; ≥5 per area).
     - **Tier 2 — Boundary & Corner Cases**: Edge values, guard violations, error branches, unauthorized actors, and idempotency checks (44 tests; ≥5 per area).
     - **Tier 3 — Cross-Feature Combinations**: Pairwise and sequential interactions between intake, payment, cancellation, shipping, returns, and ratings (8 comprehensive tests).
     - **Tier 4 — Real-World Application Scenarios**: Multi-package order grouping (`OrderGroupId`), partial returns, courier empty-box discoveries, deposit timeouts, post-delivery expirations, and dispute workflows (5 complex scenarios).

---

## 2. Zero-Database / Zero External Dependency Strategy

In accordance with architectural requirements R1 and R4:
- **Zero External Database**: Tests MUST NOT require a running MySQL instance, Docker daemon, or external network services.
- **Pure In-Memory Test Repositories**:
  - `InMemoryPackageRepository` (implements `IPackageRepository` with `OrderGroupId` querying, thread-safe `ConcurrentDictionary`, and query filtering).
  - `InMemoryBoxRepository` (implements `IBoxRepository` with locker box allocation, size matching, and box exclusion logic).
  - `InMemoryReturnRepository` (implements `IReturnRepository` with status and active-returns filtering).
  - `InMemoryPeopleService` (implements `IPeopleService` with pre-seeded `User`, `Courier`, and `Operator` collections).
  - `TestPaymentGateway` (implements `IPaymentGateway` with configurable success, failure, and status simulation).
  - `TestNotificationService` (implements `INotificationService` with in-memory notification audit log).
  - `InMemoryReturnService` (implements `IReturnService` providing complete domain-orchestrated return handling).
- Real domain services (`PackageOrderService`, `PackageIntakeService`, `PackageAcceptanceService`, `PackageFulfillmentService`, `RatingService`) execute against these fast, zero-latency in-memory repositories.
- **Determinism & Speed**: 100% deterministic execution in sub-milliseconds, zero port collisions, and zero state leakage across parallel test runs.
- **Test State Isolation**:
  - Each test fixture instantiates a fresh `E2ETestContext` with isolated entities and pre-seeded actor personas.
  - Virtual time progression (`AdvanceTime(TimeSpan)`) allows reliable testing of time-based deadlines (`DepositDeadline`, `ExpiredAt`) without calling `Thread.Sleep()`.

---

## 3. Directory Layout

All E2E test code resides under `tests/PackageDeliverySystem.Tests/E2E/`:

```
tests/PackageDeliverySystem.Tests/
├── E2E/
│   ├── Infrastructure/
│   │   ├── E2ETestHarness.cs              # DI harness & service container
│   │   ├── E2ETestContext.cs              # Test context, virtual clock, actor seeding
│   │   ├── E2EActors.cs                   # Pre-seeded Customers, Operators, Couriers, Lockers
│   │   ├── E2ETestBase.cs                 # Base test class with AAA helper assertions
│   │   ├── InMemoryPackageRepository.cs   # In-memory IPackageRepository implementation
│   │   ├── InMemoryBoxRepository.cs       # In-memory IBoxRepository implementation
│   │   ├── InMemoryReturnRepository.cs    # In-memory IReturnRepository implementation
│   │   ├── InMemoryPeopleService.cs       # In-memory IPeopleService implementation
│   │   ├── InMemoryReturnService.cs       # In-memory IReturnService implementation
│   │   ├── TestPaymentGateway.cs          # Controllable IPaymentGateway (success/failure)
│   │   └── TestNotificationService.cs     # Verifiable INotificationService sink
│   ├── Tier1_FeatureCoverage/             # Happy path feature tests (>=5 tests per area)
│   │   ├── IntakeAndPaymentHappyPathTests.cs
│   │   ├── CancellationAndRejectionHappyPathTests.cs
│   │   ├── CourierTransitHappyPathTests.cs
│   │   ├── PickupAndExpirationHappyPathTests.cs
│   │   ├── RatingHappyPathTests.cs
│   │   ├── ReturnRequestHappyPathTests.cs
│   │   └── ReturnProcessingHappyPathTests.cs
│   ├── Tier2_BoundaryAndCornerCases/      # Boundary, edge & error guard tests
│   │   ├── IntakeAndPaymentBoundaryTests.cs
│   │   ├── CancellationAndRejectionBoundaryTests.cs
│   │   ├── CourierTransitBoundaryTests.cs
│   │   ├── PickupAndExpirationBoundaryTests.cs
│   │   ├── RatingBoundaryTests.cs
│   │   ├── ReturnRequestBoundaryTests.cs
│   │   └── ReturnProcessingBoundaryTests.cs
│   ├── Tier3_CrossFeatureCombinations/    # Pairwise cross-feature interaction tests
│   │   └── CrossFeatureCombinationTests.cs
│   └── Tier4_RealWorldScenarios/          # Complex end-to-end multi-step scenarios
│       └── RealWorldScenariosTests.cs
├── PackageLifecycleTests.cs               # M1 Unit tests (T1-T20)
├── UnitTest1.cs                           # Prototype unit/integration tests
└── PackageDeliverySystem.Tests.csproj     # xUnit test project
```

---

## 4. Feature Inventory Mapping (37 Features to 7 Areas)

All 37 features from `PROJECT.md § Feature Inventory` are systematically partitioned into 7 logical user-facing feature areas:

| Area | Feature Area Name | Features Covered | Description | Primary Interfaces |
|---|---|---|---|---|
| 1 | **Intake & Payment** | Features 1 (Intake), 5, 6, 7, 16, 17, 18 | Order creation, box allocation, payment gateway, deposit confirmation, acceptance | `IPackageOrderService`, `IPackageIntakeService`, `IPackageAcceptanceService`, `IPaymentGateway` |
| 2 | **Cancellation & Rejection** | Features 2, 3, 4, 13, 14, 15 | Customer cancellation at Ordered/Accepted, courier empty box cancel, operator rejection | `Package.Cancel`, `Package.Reject`, `IPackageAcceptanceService.RejectPackageAsync` |
| 3 | **Courier Transit & Delivery** | Features 1 (Transit), 9, 20 | Courier assignment, package preparation, shipping, delivery, pickup code generation | `IPackageFulfillmentService.AssignCourierAsync`, `ShipPackageAsync`, `DeliverPackageAsync` |
| 4 | **Pickup & Expiration** | Features 1 (Collect), 8, 10, 11, 12 | Recipient collection with 6-digit code, transit expiration, 48h post-delivery expiration | `IPackageFulfillmentService.CollectPackageAsync`, `Package.Collect`, `PackageAcceptanceService.ProcessExpiredPackagesAsync` |
| 5 | **Rating** | Feature 19 | Post-collection 1–5 score ratings for courier and sender | `IRatingService.RateCourierAsync`, `RateSenderAsync`, `IPeopleService.UpdateRankAsync` |
| 6 | **Return Request & Partial Returns** | Features 21, 22, 23, 25, 26, 29, 30, 34, 35 | Multi-package grouping (`OrderGroupId`), return request, duplicate return guard, partial order returns | `IReturnService.RequestReturnAsync`, `GetReturnablePackagesAsync`, `Package.OrderGroupId` |
| 7 | **Return Processing & Refund** | Features 24, 27, 28, 31, 32, 33, 36, 37 | Operator return approval/rejection, courier return transit, completion, payment refund | `IReturnService.ApproveReturnAsync`, `RejectReturnAsync`, `ShipReturnAsync`, `CompleteReturnAsync`, `Payment.Refund` |

---

## 5. Test Runner Commands & Mechanics

All tests are executed using standard .NET CLI commands.

### 5.1 Full Solution Test Run
Executes all unit tests and E2E tests:
```bash
dotnet test
```

### 5.2 E2E Test Filtering
Use `--filter` with xUnit fully-qualified class and namespace names:

- **Run only the E2E test suite**:
  ```bash
  dotnet test --filter "FullyQualifiedName~PackageDeliverySystem.Tests.E2E"
  ```

- **Run specific E2E Tiers**:
  ```bash
  # Tier 1 - Feature Coverage
  dotnet test --filter "FullyQualifiedName~Tier1"

  # Tier 2 - Boundary and Corner Cases
  dotnet test --filter "FullyQualifiedName~Tier2"

  # Tier 3 - Cross-Feature Combinations
  dotnet test --filter "FullyQualifiedName~Tier3"

  # Tier 4 - Real-World Scenarios
  dotnet test --filter "FullyQualifiedName~Tier4"
  ```

- **Run Pure In-Memory Tests (excluding MySQL prototype)**:
  The prototype integration test `TwoOperators_RacingForTheSameBox_OnlyOneSucceeds` requires an active MySQL container. To run the complete in-memory test suite without MySQL:
  ```bash
  dotnet test --filter "FullyQualifiedName!~TwoOperators_RacingForTheSameBox"
  ```

### 5.3 Output Formatting & Diagnostics
- Standard readable output:
  ```bash
  dotnet test --logger "console;verbosity=normal"
  ```
- Detailed diagnostics:
  ```bash
  dotnet test --logger "console;verbosity=detailed"
  ```
- Code coverage collection:
  ```bash
  dotnet test --collect:"XPlat Code Coverage"
  ```

---

## 6. Pass/Fail Semantics & Exit Codes

- **Exit Code 0**:
  - All test methods executed successfully.
  - Zero test assertions failed.
  - Zero unhandled exceptions occurred.
  - Build and compilation succeeded with 0 errors.
- **Exit Code > 0 (Failure)**:
  - Any assertion failed (e.g., status mismatch, unexpected exception, missing notification).
  - Compilation failed.
  - Runtime or environment fault.

---

## 7. Opaque-Box System Boundaries & Actor Personas

Tests interact strictly through public contracts and high-level personas:

1. **Customer (Sender)**: Initiates package creation via `IPackageOrderService`, requests payment via `IPackageIntakeService`.
2. **Customer (Recipient)**: Receives pickup code via notification, collects package via `IPackageFulfillmentService.CollectPackageAsync`, initiates returns via `IReturnService.RequestReturnAsync`, rates courier/sender via `IRatingService`.
3. **Courier**: Confirms deposit via `IPackageIntakeService.ConfirmDepositAsync`, reports missing deposits, ships package via `IPackageFulfillmentService.ShipPackageAsync`, ships returns via `IReturnService.ShipReturnAsync`.
4. **Operator**: Accepts or rejects packages via `IPackageAcceptanceService`, approves or rejects returns via `IReturnService`.
5. **System / Background Jobs**: Processes expirations via `IPackageAcceptanceService.ProcessExpiredPackagesAsync()`, processes missed deposits via `ProcessMissedDepositsAsync()`.

No test bypasses service validations or modifies underlying private fields. All assertions check observable service outputs, query methods (`GetByIdAsync`, `GetReturnByIdAsync`), and notification sink records.
