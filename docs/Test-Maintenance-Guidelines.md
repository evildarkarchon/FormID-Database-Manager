# FormID Database Manager - Test Maintenance Guidelines

## Overview

This document provides guidelines for maintaining the test suite of the FormID Database Manager project. It covers routine maintenance tasks, troubleshooting procedures, and long-term test health strategies.

## Maintenance Schedule

### Daily Tasks

#### 1. Test Suite Monitoring
- **Run Tests Locally**: Execute the full test suite
- **Address Failures**: Any failing tests should be investigated immediately
- **Platform-Specific Issues**: Pay attention to platform-specific failures (Windows/Linux/macOS)

```bash
# Run full test suite
dotnet test --configuration Release
```

#### 2. Flaky Test Detection
Monitor for tests that pass/fail intermittently:
```bash
# Run tests multiple times to detect flakiness
for i in {1..5}; do dotnet test --filter "TestName"; done
```

### Weekly Tasks

#### 1. Test Execution Time Analysis
```bash
# Generate detailed test execution report
dotnet test --logger "trx;LogFileName=test_results.trx"

# Review long-running tests (>1 second for unit tests)
dotnet test --logger "console;verbosity=detailed" | grep -E "Passed.*[1-9][0-9]*\.[0-9]+ sec"
```

#### 2. Skip Reason Review
Review why tests are being skipped:
```bash
# List all skipped tests with reasons
dotnet test --logger "console;verbosity=detailed" | grep -A2 "Skipped"
```

Common skip reasons to address:
- Missing game installations (document in README)
- Platform-specific features (ensure proper conditions)
- Disabled tests (re-enable or remove)

#### 3. Code Coverage Trends
```bash
# Generate coverage report
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=lcov /p:CoverletOutput=./coverage.info

# Check coverage percentage
# Ensure it stays above 80% threshold
```

### Monthly Tasks

#### 1. Test Data Maintenance

**Update Test Data Files**:
```bash
# Check test data age
find TestData -type f -mtime +30 -ls

# Review and update stale test data
# Ensure test data still represents real-world scenarios
```

**Clean Orphaned Test Files**:
```bash
# Find and remove temporary test files
find %TEMP% -name "FormIDTest_*" -mtime +7 -delete
find %TEMP% -name "TestDB_*.db" -mtime +7 -delete
```

#### 2. Dependency Updates

**Check for Test Framework Updates**:
```xml
<!-- Review and update test dependencies in .csproj files -->
<PackageReference Include="xunit" Version="2.x.x" />
<PackageReference Include="Moq" Version="4.x.x" />
<PackageReference Include="coverlet.collector" Version="x.x.x" />
```

**Update Test Utilities**:
- Review MockFactory for new service additions
- Update test builders for new model properties
- Ensure fixtures support new features

#### 3. Performance Baseline Updates

```bash
# Run performance benchmarks
dotnet run -c Release --project "FormID Database Manager.Tests" -- --filter "*Benchmark*"

# Compare with previous baselines
# Update expected performance thresholds if needed
```

### Quarterly Tasks

#### 1. Test Strategy Review

**Coverage Analysis**:
- Identify untested code paths
- Review critical path coverage
- Plan tests for new features

**Test Distribution Review**:
```bash
# Count tests by category
dotnet test --list-tests | grep -E "\[Collection|Category" | sort | uniq -c

# Ideal distribution:
# - 60% Unit tests
# - 25% Integration tests
# - 10% UI tests
# - 5% Performance tests
```

#### 2. Test Refactoring

**Identify Candidates for Refactoring**:
- Tests with duplicate setup code
- Tests testing multiple concerns
- Tests with unclear names
- Tests with complex arrangements

**Apply Refactoring Patterns**:
```csharp
// Before: Duplicate setup
[Fact]
public void Test1()
{
    var service = new Service();
    var data = CreateTestData();
    // test logic
}

// After: Shared fixture
public class ServiceTests : IClassFixture<ServiceFixture>
{
    private readonly ServiceFixture _fixture;
    // tests use _fixture
}
```

## Troubleshooting Guide

### Common Test Failures

#### 1. Database Lock Errors
**Symptom**: `database is locked` errors
**Solution**:
```csharp
// Ensure proper collection attribute
[Collection("Database Tests")]
public class MyDatabaseTest
```

#### 2. File Access Violations
**Symptom**: `Access to the path is denied`
**Solution**:
- Use unique temp directories
- Ensure proper cleanup in Dispose()
- Check antivirus exclusions

#### 3. Timeout Failures
**Symptom**: Tests timing out in CI but passing locally
**Solution**:
```csharp
// Increase timeout for CI environments
[Fact(Timeout = 30000)] // 30 seconds
public async Task SlowTest()
```

#### 4. Platform-Specific Failures
**Symptom**: Tests fail on specific OS
**Solution**:
```csharp
// Skip on incompatible platforms
[SkippableFact]
public void WindowsOnlyTest()
{
    Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows));
    // test logic
}
```

### Debugging Techniques

#### 1. Verbose Output
```bash
# Maximum verbosity
dotnet test --logger "console;verbosity=detailed" --diag:diagnostic.log
```

#### 2. Test Isolation
```bash
# Run single test in isolation
dotnet test --filter "FullyQualifiedName=Namespace.Class.Method"
```

#### 3. Debugging in IDE
```csharp
// Add conditional breakpoint
#if DEBUG
if (System.Diagnostics.Debugger.IsAttached)
    System.Diagnostics.Debugger.Break();
#endif
```

#### 4. Capture Test Output
```csharp
public class MyTest
{
    private readonly ITestOutputHelper _output;
    
    public MyTest(ITestOutputHelper output)
    {
        _output = output;
    }
    
    [Fact]
    public void Test()
    {
        _output.WriteLine($"Debug info: {variable}");
    }
}
```

## Test Health Metrics

### Key Performance Indicators (KPIs)

1. **Test Execution Time**
   - Unit tests: < 100ms average
   - Integration tests: < 1s average
   - UI tests: < 2s average
   - Total suite: < 2 minutes

2. **Code Coverage**
   - Overall: > 80%
   - Critical services: > 90%
   - New code: > 85%

3. **Test Reliability**
   - Flaky test rate: < 1%
   - CI pass rate: > 95%

4. **Test Maintenance Burden**
   - Test-to-code ratio: 1.5:1
   - Test changes per feature: < 3

### Monitoring Dashboard

Create a simple dashboard script:
```powershell
# test-health.ps1
Write-Host "=== Test Health Report ===" -ForegroundColor Cyan

# Test count
$testCount = dotnet test --list-tests | Measure-Object -Line
Write-Host "Total Tests: $($testCount.Lines)"

# Last run results
$results = dotnet test --no-build --logger "json" 
Write-Host "Last Run: Passed X, Failed Y, Skipped Z"

# Coverage
$coverage = dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=json
Write-Host "Coverage: XX%"

# Execution time
Write-Host "Total Execution Time: XX seconds"
```

## Best Practices for Test Maintenance

### 1. Regular Cleanup
- Remove commented-out tests
- Delete obsolete test data
- Archive old performance baselines

### 2. Documentation Updates
- Keep test documentation in sync with code
- Document reasons for skipped tests
- Maintain examples of common test patterns

### 3. Continuous Improvement
- Regular test refactoring sessions
- Performance optimization sprints
- Coverage improvement initiatives

### 4. Team Practices
- Test review in pull requests
- Pair programming for complex tests
- Knowledge sharing sessions

## Emergency Procedures

### All Tests Failing
1. Check test framework versions
2. Verify test discovery: `dotnet test --list-tests`
3. Check for global test configuration issues
4. Revert recent test infrastructure changes

### Build Process Issues
1. Check for environment-specific issues
2. Review recent dependency updates
3. Verify SDK and runtime versions
4. Consider reverting to last known good state

### Performance Regression
1. Run benchmarks locally
2. Compare with historical baselines
3. Profile the affected code
4. Consider temporary threshold adjustments

## Long-term Improvements

### 1. Test Infrastructure
- [ ] Implement test result trending
- [ ] Add automated flaky test detection
- [ ] Create test performance dashboard
- [ ] Set up test impact analysis

### 2. Test Quality
- [ ] Implement mutation testing
- [ ] Add property-based tests
- [ ] Increase integration test scenarios
- [ ] Improve error message assertions

### 3. Developer Experience
- [ ] Create test templates/snippets
- [ ] Improve test data generators
- [ ] Add test debugging helpers
- [ ] Enhance test reporting

## Appendix: Useful Commands

```bash
# Find slowest tests
dotnet test --logger:"console;verbosity=detailed" | Sort-Object -Property Time -Descending | Select-Object -First 10

# Run tests in random order (detect dependencies)
dotnet test -- xunit.execution.DisableParallelization=true xunit.execution.Random=true

# Generate detailed test report
dotnet test --logger:"html;LogFileName=test_report.html"

# Check for unused test files
git ls-files "*.cs" | xargs grep -L "[Fact\|Theory\|Test]"

# Find tests without assertions
grep -r "public.*void.*Test" . | xargs grep -L "Assert\|Verify"
```

## Contact and Support

For test-related issues:
1. Check this maintenance guide
2. Review test best practices document
3. Consult team lead for architecture decisions
4. Create GitHub issue for persistent problems

Remember: A well-maintained test suite is crucial for project success. Regular maintenance prevents technical debt and ensures long-term project health.