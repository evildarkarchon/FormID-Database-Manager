using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace FormID_Database_Manager.Tests.Helpers;

/// <summary>
///     Provides optimization utilities for test execution
/// </summary>
public static class TestOptimization
{
    private static readonly ConcurrentDictionary<string, object> SharedResources = new();
    private static readonly SemaphoreSlim ResourceLock = new(1, 1);

    /// <summary>
    ///     Gets or creates a shared resource for use across multiple tests
    /// </summary>
    public static async Task<T> GetOrCreateSharedResourceAsync<T>(
        string key,
        Func<Task<T>> factory) where T : class
    {
        if (SharedResources.TryGetValue(key, out var existing))
        {
            return (T)existing;
        }

        await ResourceLock.WaitAsync();
        try
        {
            if (SharedResources.TryGetValue(key, out existing))
            {
                return (T)existing;
            }

            var resource = await factory();
            SharedResources[key] = resource;
            return resource;
        }
        finally
        {
            ResourceLock.Release();
        }
    }

    /// <summary>
    ///     Creates a temporary directory that will be cleaned up when disposed
    /// </summary>
    public static TempDirectory CreateTempDirectory()
    {
        return new TempDirectory();
    }

    /// <summary>
    ///     Runs a test with a timeout to prevent hanging tests
    /// </summary>
    public static async Task RunWithTimeoutAsync(
        Func<Task> testAction,
        TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var testTask = testAction();
        var completedTask = await Task.WhenAny(testTask, Task.Delay(timeout, cts.Token));

        if (completedTask != testTask)
        {
            throw new TimeoutException($"Test exceeded timeout of {timeout}");
        }

        await testTask;
    }

    /// <summary>
    ///     Manages temporary directories for tests
    /// </summary>
    public class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"FormIDTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, true);
                }
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    /// <summary>
    ///     Provides a pool of reusable test databases
    /// </summary>
    public static class DatabasePool
    {
        private static readonly ConcurrentBag<string> AvailableDatabases = new();
        private static readonly HashSet<string> AllDatabases = new();
        private static readonly object Lock = new();

        public static string GetDatabase()
        {
            if (AvailableDatabases.TryTake(out var db))
            {
                return db;
            }

            lock (Lock)
            {
                var dbPath = Path.Combine(
                    Path.GetTempPath(),
                    $"TestDB_{Guid.NewGuid():N}.db");
                AllDatabases.Add(dbPath);
                return dbPath;
            }
        }

        public static void ReturnDatabase(string dbPath)
        {
            // Reset the database for reuse
            if (File.Exists(dbPath))
            {
                using var connection = new SqliteConnection($"Data Source={dbPath}");
                connection.Open();

                // Get all tables and truncate them
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
                var tables = new List<string>();

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        tables.Add(reader.GetString(0));
                    }
                }

                foreach (var table in tables)
                {
                    cmd.CommandText = $"DELETE FROM {table}";
                    cmd.ExecuteNonQuery();
                }
            }

            AvailableDatabases.Add(dbPath);
        }

        public static void Cleanup()
        {
            lock (Lock)
            {
                foreach (var db in AllDatabases)
                {
                    try
                    {
                        if (File.Exists(db))
                        {
                            File.Delete(db);
                        }
                    }
                    catch
                    {
                        // Best effort
                    }
                }

                AllDatabases.Clear();
            }
        }
    }
}
