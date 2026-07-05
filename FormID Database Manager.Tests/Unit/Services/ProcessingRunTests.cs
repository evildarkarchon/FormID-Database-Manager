#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using Microsoft.Data.Sqlite;
using Moq;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

public sealed class ProcessingRunTests : IDisposable
{
    private readonly List<string> _tempDirectories = [];
    private readonly List<string> _tempFiles = [];

    [Fact]
    public async Task ExecuteAsync_DryRunPluginRun_ReportsPluginStatusWithoutOpeningDatabase()
    {
        var databaseService = new Mock<DatabaseService>();
        var sut = new ProcessingRun(databaseService.Object);
        var events = new List<ProcessingRunEvent>();
        var progress = new SynchronousProgress<ProcessingRunEvent>(events.Add);

        var request = new PluginProcessingRunRequest(
            @"C:\Games\Skyrim",
            CreateTempFilePath("dry-run.db"),
            GameRelease.SkyrimSE,
            ["PluginA.esp", "PluginB.esp"],
            UpdateMode.Append,
            dryRun: true);

        await sut.ExecuteAsync(request, progress);

        databaseService.Verify(
            x => x.InitializeDatabase(It.IsAny<string>(), It.IsAny<GameRelease>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.All(events, runEvent => Assert.Equal(ProcessingRunEventKind.Status, runEvent.Kind));
        Assert.Contains(events, runEvent => runEvent.Message.Contains("Would process PluginA.esp", StringComparison.Ordinal));
        Assert.Contains(events, runEvent => runEvent.Message.Contains("Would process PluginB.esp", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_DryRunPluginRun_AllowsEmptyDatabasePath()
    {
        var databaseService = new Mock<DatabaseService>();
        var sut = new ProcessingRun(databaseService.Object);
        var events = new List<ProcessingRunEvent>();
        var progress = new SynchronousProgress<ProcessingRunEvent>(events.Add);

        var request = new PluginProcessingRunRequest(
            @"C:\Games\Skyrim",
            string.Empty,
            GameRelease.SkyrimSE,
            ["PluginA.esp"],
            UpdateMode.Append,
            dryRun: true);

        await sut.ExecuteAsync(request, progress);

        databaseService.Verify(
            x => x.InitializeDatabase(It.IsAny<string>(), It.IsAny<GameRelease>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Contains(events, runEvent => runEvent.Message.Contains("Would process PluginA.esp", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_FormIdTextRun_ImportsRecordsAndReportsCompletionEvents()
    {
        var directory = CreateTempDirectory();
        var databasePath = Path.Combine(directory, "formids.db");
        var textFilePath = Path.Combine(directory, "formids.txt");
        await File.WriteAllLinesAsync(
            textFilePath,
            [
                "PluginA.esp|000001|First Entry",
                "PluginB.esp|000002|Second Entry"
            ],
            TestContext.Current.CancellationToken);

        var sut = new ProcessingRun(new DatabaseService());
        var events = new List<ProcessingRunEvent>();
        var progress = new SynchronousProgress<ProcessingRunEvent>(events.Add);
        var request = new FormIdTextProcessingRunRequest(
            textFilePath,
            databasePath,
            GameRelease.SkyrimSE,
            UpdateMode.Append);

        await sut.ExecuteAsync(request, progress);

        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT plugin, formid, entry FROM {GameRelease.SkyrimSE} ORDER BY id";

        var records = new List<(string Plugin, string FormId, string Entry)>();
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            records.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        Assert.Equal(
            [
                ("PluginA.esp", "000001", "First Entry"),
                ("PluginB.esp", "000002", "Second Entry")
            ],
            records);
        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Status &&
            runEvent.Message.Contains("Completed processing 2 plugins", StringComparison.Ordinal));
        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Status &&
            runEvent.Message.Contains("Processing completed successfully", StringComparison.Ordinal) &&
            runEvent.Value == 100);
    }

    [Fact]
    public async Task ExecuteAsync_PluginRunMissingPluginFile_ReportsTypedErrorEventAndCompletesRun()
    {
        var gameDirectory = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(gameDirectory, "Data"));
        var databasePath = Path.Combine(gameDirectory, "plugins.db");

        var loadOrderProvider = new Mock<IGameLoadOrderProvider>();
        loadOrderProvider
            .Setup(x => x.BuildSnapshot(GameRelease.SkyrimSE, Path.Combine(gameDirectory, "Data"), true))
            .Returns(new GameLoadOrderSnapshot(["Missing.esp"]));

        var sut = new ProcessingRun(new DatabaseService(), loadOrderProvider.Object);
        var events = new List<ProcessingRunEvent>();
        var progress = new SynchronousProgress<ProcessingRunEvent>(events.Add);
        var request = new PluginProcessingRunRequest(
            gameDirectory,
            databasePath,
            GameRelease.SkyrimSE,
            ["Missing.esp"],
            UpdateMode.Append);

        await sut.ExecuteAsync(request, progress);

        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Error &&
            runEvent.Message.Contains("Could not find plugin file", StringComparison.Ordinal));
        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Status &&
            runEvent.Message.Contains("Processing completed successfully", StringComparison.Ordinal));
    }

    [Fact]
    public void PluginProcessingRunRequest_EmptyPluginNames_ThrowsRunValidationException()
    {
        var exception = Assert.Throws<ProcessingRunValidationException>(() =>
            new PluginProcessingRunRequest(
                @"C:\Games\Skyrim",
                @"C:\Databases\formids.db",
                GameRelease.SkyrimSE,
                [],
                UpdateMode.Append));

        Assert.Equal("No plugins selected", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_CancelledDuringInitialization_ReportsCancelledAndThrows()
    {
        using var initializeEntered = new ManualResetEventSlim(false);
        var databaseService = new BlockingInitializeDatabaseService(initializeEntered);
        var sut = new ProcessingRun(databaseService);
        var events = new List<ProcessingRunEvent>();
        var progress = new SynchronousProgress<ProcessingRunEvent>(events.Add);
        var request = new PluginProcessingRunRequest(
            @"C:\Games\Skyrim",
            CreateTempFilePath("cancel.db"),
            GameRelease.SkyrimSE,
            ["Plugin.esp"],
            UpdateMode.Append);

        var processingTask = sut.ExecuteAsync(request, progress);
        Assert.True(initializeEntered.Wait(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));

        sut.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processingTask);
        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Status &&
            runEvent.Message.Contains("Processing cancelled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_FatalInitializationError_ReportsStatusOnlyAndRethrows()
    {
        var sut = new ProcessingRun(new ThrowingInitializeDatabaseService());
        var events = new List<ProcessingRunEvent>();
        var progress = new SynchronousProgress<ProcessingRunEvent>(events.Add);
        var request = new PluginProcessingRunRequest(
            @"C:\Games\Skyrim",
            CreateTempFilePath("fatal.db"),
            GameRelease.SkyrimSE,
            ["Plugin.esp"],
            UpdateMode.Append);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ExecuteAsync(request, progress));

        Assert.Equal("database failed", exception.Message);
        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Status &&
            runEvent.Message.Contains("Error during processing: database failed", StringComparison.Ordinal));
        Assert.DoesNotContain(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Error &&
            runEvent.Message.Contains("Error during processing", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch
            {
            }
        }

        foreach (var directory in _tempDirectories)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    private string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"processing_run_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        _tempDirectories.Add(directory);
        return directory;
    }

    private string CreateTempFilePath(string fileName)
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"processing_run_{Guid.NewGuid():N}_{fileName}");
        _tempFiles.Add(filePath);
        return filePath;
    }

    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value)
        {
            handler(value);
        }
    }

    private sealed class BlockingInitializeDatabaseService(ManualResetEventSlim initializeEntered) : DatabaseService
    {
        public override async Task InitializeDatabase(
            string dbPath,
            GameRelease gameRelease,
            CancellationToken cancellationToken = default)
        {
            initializeEntered.Set();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class ThrowingInitializeDatabaseService : DatabaseService
    {
        public override Task InitializeDatabase(
            string dbPath,
            GameRelease gameRelease,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException(new InvalidOperationException("database failed"));
        }
    }
}
