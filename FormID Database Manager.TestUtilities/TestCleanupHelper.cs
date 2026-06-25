using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace FormID_Database_Manager.TestUtilities;

/// <summary>
///     Shared teardown for performance and stress tests that create temporary files and a temporary
///     directory, so cleanup behavior stays consistent across test classes.
/// </summary>
public static class TestCleanupHelper
{
    /// <summary>
    ///     Deletes the supplied created files and then the test directory, logging (not throwing)
    ///     any per-item failures to <paramref name="output"/>.
    /// </summary>
    public static void DeleteTestFilesAndDirectory(
        IEnumerable<string> createdFiles,
        string testDirectory,
        ITestOutputHelper output)
    {
        foreach (var file in createdFiles)
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch (Exception ex)
            {
                output.WriteLine($"Failed to delete test file '{file}': {ex.Message}");
            }
        }

        if (Directory.Exists(testDirectory))
        {
            try
            {
                Directory.Delete(testDirectory, true);
            }
            catch (Exception ex)
            {
                output.WriteLine($"Failed to delete test directory '{testDirectory}': {ex.Message}");
            }
        }
    }
}
