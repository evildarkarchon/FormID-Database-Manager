using System;
using System.Runtime.CompilerServices;
using Xunit;

namespace FormID_Database_Manager.TestUtilities;

/// <summary>
/// Marks long-running or non-deterministic performance tests as manual.
/// Set RUN_MANUAL_PERFORMANCE_TESTS=1 (or true) to execute these tests.
/// </summary>
public sealed class ManualPerformanceFactAttribute : FactAttribute
{
    public ManualPerformanceFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1) : base(sourceFilePath, sourceLineNumber)
    {
        if (!ManualPerformanceSkip.IsEnabled)
        {
            Skip = ManualPerformanceSkip.SkipReason;
        }
    }
}

/// <summary>
/// Marks parameterized long-running or non-deterministic performance tests as manual.
/// Set RUN_MANUAL_PERFORMANCE_TESTS=1 (or true) to execute these tests.
/// </summary>
public sealed class ManualPerformanceTheoryAttribute : TheoryAttribute
{
    public ManualPerformanceTheoryAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1) : base(sourceFilePath, sourceLineNumber)
    {
        if (!ManualPerformanceSkip.IsEnabled)
        {
            Skip = ManualPerformanceSkip.SkipReason;
        }
    }
}

internal static class ManualPerformanceSkip
{
    private const string EnvironmentVariableName = "RUN_MANUAL_PERFORMANCE_TESTS";

    public static bool IsEnabled
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(EnvironmentVariableName)?.Trim();
            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                   (bool.TryParse(value, out var parsedBoolean) && parsedBoolean);
        }
    }

    public static string SkipReason =>
        $"Manual performance test. Set {EnvironmentVariableName}=1 to run this test intentionally.";
}
