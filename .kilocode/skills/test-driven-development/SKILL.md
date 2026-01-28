---
name: test-driven-development
description: Guides through the complete Test Driven Development lifecycle with strict Red-Green-Refactor cycle enforcement. Analyzes requirements, generates comprehensive failing unit tests covering edge cases and boundary conditions before any implementation exists. Executes tests to confirm they fail for the right reasons, then guides minimal production code writing to pass tests without over-engineering. Facilitates refactoring to improve code quality while maintaining green test status. Maintains tight feedback loop with automatic test execution, provides testing strategy explanations, suggests error handling test cases, ensures test isolation, and integrates with version control workflows for incremental commits at each stage. Use when implementing new features, writing tests, refactoring code, or following TDD methodology.
license: GPL-3.0
compatibility: Designed for C# .NET projects using xUnit, NUnit, or MSTest. Requires dotnet CLI and optionally coverlet for code coverage.
metadata:
  author: FormID Database Manager
  version: "1.0"
  category: development-methodology
---

# Test Driven Development (TDD) Skill

A comprehensive guide for strict Red-Green-Refactor cycle enforcement.

## Overview

This skill provides systematic guidance through TDD, ensuring:
- Comprehensive test-first development
- Minimal implementation to pass tests
- Continuous refactoring for quality
- Tight feedback loops with automated testing
- Version control integration for incremental progress

## Prerequisites

Before using this skill, ensure:
- Test framework is configured (xUnit, NUnit, MSTest, etc.)
- Build system is operational
- Version control is initialized
- Code coverage tools are available (optional but recommended)

---

## Phase 1: RED - Write Failing Tests

### 1.1 Requirements Analysis

**Before writing any tests, analyze the requirements:**

```markdown
### Requirements Checklist
- [ ] Understand the feature/story being implemented
- [ ] Identify all acceptance criteria
- [ ] Determine input/output contracts
- [ ] Identify dependencies and collaborators
- [ ] Define error scenarios and edge cases
- [ ] Establish performance constraints (if applicable)
```

### 1.2 Test Strategy Design

**Create a comprehensive test plan covering:**

#### A. Happy Path Tests
- Normal expected behavior
- Standard input variations
- Typical use cases

#### B. Edge Case Tests
- Boundary values (min/max, empty, null)
- Empty collections/strings
- Single element scenarios
- Maximum capacity scenarios

#### C. Error Handling Tests
- Invalid inputs
- Null parameters
- Out-of-range values
- Malformed data
- Resource unavailability

#### D. State-Based Tests
- Object state transitions
- Pre-condition/post-condition verification
- Invariant preservation

#### E. Interaction Tests (when using mocks)
- Verify collaborator calls
- Verify call order
- Verify call counts

### 1.3 Test Implementation Guidelines

**Naming Convention:**
```csharp
// Pattern: MethodName_Scenario_ExpectedBehavior
[Fact]
public void CalculateTotal_EmptyCart_ReturnsZero()

[Fact]
public void ProcessPayment_InvalidCardNumber_ThrowsValidationException()

[Fact]
public void SaveDocument_NullContent_ThrowsArgumentNullException()
```

**Test Structure (Arrange-Act-Assert):**
```csharp
[Fact]
public void ProcessOrder_ValidOrder_ReturnsConfirmation()
{
    // Arrange - Set up test data and dependencies
    var order = new Order { Items = new[] { item1, item2 } };
    var service = new OrderService(mockRepository.Object);
    
    // Act - Execute the operation being tested
    var result = service.ProcessOrder(order);
    
    // Assert - Verify expected outcomes
    Assert.NotNull(result);
    Assert.Equal(order.Id, result.OrderId);
    mockRepository.Verify(r => r.Save(order), Times.Once);
}
```

**Test Independence Principles:**
1. Each test should be isolated - no shared mutable state
2. Tests should not depend on execution order
3. Tests should clean up after themselves
4. Use fresh instances for each test
5. Avoid static/shared resources without proper isolation

### 1.4 Minimal Test Implementation

**Write the minimum test code to define expected behavior:**

```csharp
// Example: Testing a new CalculateDiscount method
public class PricingServiceTests
{
    private readonly PricingService _service;
    
    public PricingServiceTests()
    {
        _service = new PricingService();
    }
    
    [Fact]
    public void CalculateDiscount_ValidPercentage_ReturnsDiscountedPrice()
    {
        // This test will fail until CalculateDiscount is implemented
        var result = _service.CalculateDiscount(100m, 0.10m);
        
        Assert.Equal(90m, result);
    }
    
    [Fact]
    public void CalculateDiscount_ZeroPercentage_ReturnsOriginalPrice()
    {
        var result = _service.CalculateDiscount(100m, 0m);
        
        Assert.Equal(100m, result);
    }
    
    [Fact]
    public void CalculateDiscount_NegativePercentage_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => 
            _service.CalculateDiscount(100m, -0.10m));
    }
    
    [Fact]
    public void CalculateDiscount_PercentageOver100_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => 
            _service.CalculateDiscount(100m, 1.50m));
    }
}
```

### 1.5 Verify Tests Fail for Right Reasons

**Execute tests and confirm they fail:**

```bash
# Run all new tests
dotnet test --filter "FullyQualifiedName~PricingServiceTests"

# Expected output: Tests fail with compilation errors or NotImplementedException
```

**Valid failure reasons:**
- Compilation error: Type/method doesn't exist
- Runtime error: Method not implemented
- Assertion failure: Method returns default/null

**Invalid failure reasons (indicates test problem):**
- NullReferenceException in test setup
- Test logic errors
- Wrong assertion values
- Missing test dependencies

---

## Phase 2: GREEN - Make Tests Pass

### 2.1 Minimal Implementation Strategy

**Write the minimum code necessary to pass the tests:**

```csharp
// Initial implementation - minimal to pass tests
public class PricingService
{
    public decimal CalculateDiscount(decimal price, decimal percentage)
    {
        // Minimal implementation
        if (percentage < 0 || percentage > 1)
            throw new ArgumentException("Percentage must be between 0 and 1", nameof(percentage));
            
        return price * (1 - percentage);
    }
}
```

**Guidelines for minimal implementation:**
1. Don't add functionality not tested
2. Don't optimize prematurely
3. Don't handle edge cases not in tests
4. Use simple, obvious implementations
5. Hardcode if needed (temporarily) to pass tests

### 2.2 Verify Green Status

**Run tests to confirm they pass:**

```bash
dotnet test --filter "FullyQualifiedName~PricingServiceTests"

# Expected: All tests pass
```

### 2.3 Commit Green State

**Commit the working implementation:**

```bash
git add .
git commit -m "GREEN: Implement CalculateDiscount with validation

- Add CalculateDiscount method to PricingService
- Handle percentage validation (0-1 range)
- Pass all discount calculation tests

Related tests: PricingServiceTests"
```

---

## Phase 3: REFACTOR - Improve Code Quality

### 3.1 Refactoring Checklist

**Before refactoring, ensure:**
- [ ] All tests pass (green bar)
- [ ] Code coverage is adequate
- [ ] No compiler warnings
- [ ] No code analysis violations

### 3.2 Refactoring Targets

#### A. Code Smells to Address
- **Duplication**: Extract methods, introduce inheritance
- **Long Methods**: Extract smaller, focused methods
- **Large Classes**: Split responsibilities
- **Feature Envy**: Move methods to appropriate classes
- **Primitive Obsession**: Introduce value objects
- **Shotgun Surgery**: Consolidate related changes

#### B. Quality Improvements
- **Naming**: Clear, intention-revealing names
- **Comments**: Remove redundant comments, add why not what
- **Structure**: Consistent formatting and organization
- **Complexity**: Reduce cyclomatic complexity
- **Coupling**: Minimize dependencies between components

### 3.3 Common Refactoring Patterns

**Extract Method:**
```csharp
// Before
public decimal CalculateDiscount(decimal price, decimal percentage)
{
    if (percentage < 0 || percentage > 1)
        throw new ArgumentException("Percentage must be between 0 and 1", nameof(percentage));
    
    var discountAmount = price * percentage;
    var finalPrice = price - discountAmount;
    
    return finalPrice;
}

// After
public decimal CalculateDiscount(decimal price, decimal percentage)
{
    ValidatePercentage(percentage);
    return ApplyDiscount(price, percentage);
}

private static void ValidatePercentage(decimal percentage)
{
    if (percentage < 0 || percentage > 1)
        throw new ArgumentException("Percentage must be between 0 and 1", nameof(percentage));
}

private static decimal ApplyDiscount(decimal price, decimal percentage)
{
    return price * (1 - percentage);
}
```

**Introduce Constant:**
```csharp
// Before
if (percentage > 1)

// After
private const decimal MaxDiscountPercentage = 1.0m;
if (percentage > MaxDiscountPercentage)
```

### 3.4 Refactoring Safety Protocol

**After each refactoring change:**

```bash
# 1. Run tests immediately
dotnet test --filter "FullyQualifiedName~PricingServiceTests"

# 2. If tests fail, revert and try again
git checkout -- .

# 3. If tests pass, consider committing the refactoring
git add .
git commit -m "REFACTOR: Extract validation logic in PricingService

- Extract ValidatePercentage method
- Extract ApplyDiscount method
- Improve code readability and maintainability

All tests passing"
```

### 3.5 Refactoring Verification

**Ensure refactoring doesn't change behavior:**
- All tests must pass without modification
- Test coverage should remain stable or improve
- Public API should remain unchanged
- Performance characteristics should be maintained

---

## Automated Feedback Loop

### Continuous Test Execution

**Set up automatic test execution:**

```bash
# Watch mode for continuous testing
dotnet watch test --project "YourProject.Tests"

# Or use file watcher with test runner
```

### Coverage Analysis

**Track code coverage:**

```bash
# Run tests with coverage
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=lcov

# Check coverage for specific class
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura

# Generate coverage report
dotnet test /p:CollectCoverage=true /p:CoverletOutput=./coverage.xml
```

**Coverage goals:**
- Minimum 80% line coverage
- 100% coverage for critical paths
- Focus on branch coverage for complex logic

---

## Testing Strategies by Scenario

### 1. Testing Async Methods

```csharp
[Fact]
public async Task SaveAsync_ValidData_PersistsToDatabase()
{
    // Arrange
    var data = new Data { Id = 1, Value = "test" };
    
    // Act
    await _service.SaveAsync(data);
    
    // Assert
    var saved = await _repository.GetAsync(1);
    Assert.NotNull(saved);
    Assert.Equal("test", saved.Value);
}

[Fact]
public async Task GetAsync_NonExistentId_ReturnsNull()
{
    var result = await _service.GetAsync(999);
    
    Assert.Null(result);
}
```

### 2. Testing with Mocks

```csharp
[Fact]
public void ProcessOrder_SendsConfirmationEmail()
{
    // Arrange
    var mockEmailService = new Mock<IEmailService>();
    var service = new OrderService(mockEmailService.Object);
    var order = CreateValidOrder();
    
    // Act
    service.ProcessOrder(order);
    
    // Assert
    mockEmailService.Verify(
        e => e.SendConfirmationEmail(order.CustomerEmail, order.Id),
        Times.Once);
}
```

### 3. Testing Exception Handling

```csharp
[Fact]
public void Divide_DivisorZero_ThrowsDivideByZeroException()
{
    var exception = Assert.Throws<DivideByZeroException>(
        () => _calculator.Divide(10, 0));
    
    Assert.Equal("Attempted to divide by zero.", exception.Message);
}

[Fact]
public void ProcessFile_NonExistentFile_ThrowsFileNotFoundException()
{
    var exception = await Assert.ThrowsAsync<FileNotFoundException>(
        async () => await _processor.ProcessFile("missing.txt"));
    
    Assert.Contains("missing.txt", exception.FileName);
}
```

### 4. Testing Collections

```csharp
[Fact]
public void GetActiveUsers_ReturnsOnlyActiveUsers()
{
    var users = _service.GetActiveUsers();
    
    Assert.All(users, user => Assert.True(user.IsActive));
    Assert.Equal(3, users.Count);
}

[Fact]
public void SortByName_OrdersAlphabetically()
{
    var items = new[] { "Charlie", "Alice", "Bob" };
    
    var sorted = _service.SortByName(items);
    
    Assert.Equal(new[] { "Alice", "Bob", "Charlie" }, sorted);
}
```

### 5. Testing State Changes

```csharp
[Fact]
public void AddItem_IncreasesItemCount()
{
    var cart = new ShoppingCart();
    var initialCount = cart.ItemCount;
    
    cart.AddItem(new Item { Price = 10m });
    
    Assert.Equal(initialCount + 1, cart.ItemCount);
}

[Fact]
public void SubmitOrder_ChangesStatusToSubmitted()
{
    var order = new Order { Status = OrderStatus.Draft };
    
    order.Submit();
    
    Assert.Equal(OrderStatus.Submitted, order.Status);
}
```

---

## Version Control Integration

### Commit Strategy

**RED Phase Commits:**
```bash
# Commit failing tests
git add .
git commit -m "RED: Add tests for discount calculation

- Test normal discount calculation
- Test zero percentage edge case
- Test invalid percentage validation

Tests failing as expected - implementation pending"
```

**GREEN Phase Commits:**
```bash
# Commit passing implementation
git add .
git commit -m "GREEN: Implement discount calculation logic

- Add CalculateDiscount method
- Add percentage validation
- All tests now passing

Implements: PricingServiceTests"
```

**REFACTOR Phase Commits:**
```bash
# Commit each refactoring step
git add .
git commit -m "REFACTOR: Extract discount calculation methods

- Separate validation from calculation
- Improve method naming
- Reduce complexity

Behavior unchanged - all tests passing"
```

### Branching Strategy

**Feature Branch Workflow:**
```bash
# Start new feature
git checkout -b feature/discount-calculation

# Complete TDD cycle for feature
git add .
git commit -m "Complete discount calculation feature with TDD

- Comprehensive test coverage
- Clean implementation
- Refactored for maintainability"

# Merge back to main
git checkout main
git merge --no-ff feature/discount-calculation
```

---

## Coverage Gap Analysis

### Identifying Missing Tests

**Review checklist for comprehensive coverage:**

1. **Input Validation**
   - [ ] Null inputs
   - [ ] Empty strings/collections
   - [ ] Whitespace-only strings
   - [ ] Boundary values (min/max)
   - [ ] Invalid formats/types

2. **Error Scenarios**
   - [ ] Resource not found
   - [ ] Permission denied
   - [ ] Network failures
   - [ ] Timeout scenarios
   - [ ] Concurrent access conflicts

3. **State Transitions**
   - [ ] Initial state
   - [ ] Valid transitions
   - [ ] Invalid transitions
   - [ ] Terminal states

4. **Concurrency**
   - [ ] Thread safety
   - [ ] Race conditions
   - [ ] Deadlock prevention

5. **Performance**
   - [ ] Large input handling
   - [ ] Timeout behavior
   - [ ] Memory usage

### Coverage Report Interpretation

```bash
# Generate detailed coverage report
dotnet test /p:CollectCoverage=true \
  /p:CoverletOutputFormat=cobertura \
  /p:Exclude="[*Tests*]*,[xunit.*]*"

# Analyze uncovered lines and branches
# Focus on:
# - Critical business logic
# - Error handling paths
# - Edge cases
```

---

## Common TDD Anti-Patterns

### 1. The Liar
**Problem**: Test passes but doesn't actually verify anything.
```csharp
// Anti-pattern
[Fact]
public void TestSomething()
{
    var result = service.DoSomething();
    Assert.True(true); // Always passes!
}
```

### 2. The Secret Catcher
**Problem**: Test catches all exceptions silently.
```csharp
// Anti-pattern
[Fact]
public void TestSomething()
{
    try
    {
        service.DoSomething();
    }
    catch { } // Hides all failures!
}
```

### 3. The Free Ride
**Problem**: One test verifies multiple unrelated things.
```csharp
// Anti-pattern - testing too much
[Fact]
public void TestEverything()
{
    service.Method1();
    service.Method2();
    service.Method3();
    // Multiple assertions without clear focus
}
```

### 4. The Happy Path
**Problem**: Only tests ideal scenarios, ignores edge cases.
```csharp
// Anti-pattern - missing error cases
[Fact]
public void Divide_ReturnsCorrectResult()
{
    Assert.Equal(2, calculator.Divide(10, 5));
    // Missing: divide by zero, negative numbers, etc.
}
```

### 5. The Slow Poke
**Problem**: Tests are slow due to real dependencies.
```csharp
// Anti-pattern - hitting real database
[Fact]
public void TestWithRealDatabase()
{
    var repo = new RealDatabaseRepository(); // Slow!
    // ...
}
```

---

## Skill Execution Checklist

### Before Starting
- [ ] Understand requirements completely
- [ ] Identify test boundaries
- [ ] Set up test infrastructure
- [ ] Configure code coverage tools

### During RED Phase
- [ ] Write comprehensive failing tests
- [ ] Cover happy path, edge cases, errors
- [ ] Verify tests fail for right reasons
- [ ] Commit failing tests

### During GREEN Phase
- [ ] Write minimum implementation
- [ ] Verify all tests pass
- [ ] No over-engineering
- [ ] Commit green implementation

### During REFACTOR Phase
- [ ] Run tests after each change
- [ ] Address code smells
- [ ] Improve naming and structure
- [ ] Commit refactoring steps

### After Completion
- [ ] Review test coverage
- [ ] Verify all tests pass
- [ ] Run full test suite
- [ ] Document any testing gaps
- [ ] Final commit with summary

---

## Integration with Existing Codebase

### Working with Legacy Code

1. **Characterization Tests First**
   - Write tests to document current behavior
   - Don't change behavior yet
   - Establish safety net

2. **Incremental Refactoring**
   - Small, safe changes
   - Tests after each change
   - Don't refactor without tests

3. **Dependency Breaking**
   - Use seams to isolate code
   - Introduce interfaces for testing
   - Extract and test components

### Adding to Existing Test Suites

1. **Follow Existing Conventions**
   - Match naming patterns
   - Use established test helpers
   - Respect test categorization

2. **Maintain Test Isolation**
   - Don't share state between tests
   - Clean up test data
   - Reset mocks between tests

---

## Best Practices Summary

1. **Test First**: Always write tests before implementation
2. **Red-Green-Refactor**: Follow the cycle strictly
3. **Small Steps**: Make incremental, focused changes
4. **Fast Feedback**: Run tests frequently
5. **Clean Tests**: Tests should be readable and maintainable
6. **One Concept**: One test should verify one concept
7. **Arrange-Act-Assert**: Clear test structure
8. **Independent Tests**: No dependencies between tests
9. **Commit Often**: Version control at each phase
10. **Coverage Awareness**: Monitor but don't obsess over coverage metrics

---

## Troubleshooting

### Tests Won't Compile
- Check for missing using statements
- Verify namespace references
- Ensure test framework is properly referenced

### Tests Pass When They Should Fail
- Check assertions are actually verifying behavior
- Verify test setup is correct
- Look for exceptions being swallowed

### Tests Fail Intermittently
- Check for shared mutable state
- Look for timing issues in async tests
- Verify random data generation is controlled

### Slow Test Execution
- Review for real dependency usage
- Check for unnecessary setup/teardown
- Consider test parallelization settings
