using System;
using Xunit;

namespace FormID_Database_Manager.TestUtilities;

/// <summary>
/// Marks long-running or non-deterministic performance tests as manual.
/// Set RUN_MANUAL_PERFORMANCE_TESTS=1 (or true) to execute these tests.
/// </summary>
public sealed class ManualPerformanceFactAttribute : FactAttribute
{
    private const string EnvironmentVariableName = "RUN_MANUAL_PERFORMANCE_TESTS";

    public ManualPerformanceFactAttribute()
    {
        var value = Environment.GetEnvironmentVariable(EnvironmentVariableName)?.Trim();
        var isEnabled =
            string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
            (bool.TryParse(value, out var parsedBoolean) && parsedBoolean);

        if (!isEnabled)
        {
            Skip =
                $"Manual performance test. Set {EnvironmentVariableName}=1 to run this test intentionally.";
        }
    }
}
