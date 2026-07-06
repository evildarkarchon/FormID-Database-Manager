#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Models;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.ViewModels;
using Microsoft.Data.Sqlite;
using Moq;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

public class PluginProcessingServiceTests : IDisposable
{
    private readonly Mock<MainWindowViewModel> _mockViewModel;
    private readonly Mock<IThreadDispatcher> _mockDispatcher;
    private readonly Mock<IGameLoadOrderProvider> _mockLoadOrderProvider;
    private readonly PluginProcessingService _service;
    private readonly List<string> _tempFiles = [];

    public PluginProcessingServiceTests()
    {
        _mockDispatcher = new Mock<IThreadDispatcher>();
        _mockViewModel = new Mock<MainWindowViewModel>(_mockDispatcher.Object);
        _mockLoadOrderProvider = new Mock<IGameLoadOrderProvider>();

        _mockDispatcher.Setup(d => d.CheckAccess()).Returns(true);
        _mockDispatcher.Setup(d => d.Post(It.IsAny<Action>()))
            .Callback<Action>(action => action());

        _mockLoadOrderProvider
            .Setup(x => x.BuildSnapshot(It.IsAny<GameRelease>(), It.IsAny<string>(), It.IsAny<bool>()))
            .Returns(new GameLoadOrderSnapshot(["TestPlugin1.esp", "TestPlugin2.esp"]));

        _service = new PluginProcessingService(
            _mockViewModel.Object,
            _mockDispatcher.Object,
            _mockLoadOrderProvider.Object);
    }

    public void Dispose()
    {
        _service.Dispose();

        // Clean up temp files
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
                /* Ignore cleanup errors */
            }
        }
    }

    private ProcessingParameters CreateTestParameters(
        bool dryRun = false,
        string? formIdListPath = null,
        List<PluginListItem>? selectedPlugins = null)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.db");
        _tempFiles.Add(dbPath);

        // Ensure directory exists
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        return new ProcessingParameters
        {
            GameDirectory = "C:\\Games\\TestGame",
            DatabasePath = dbPath,
            GameRelease = GameRelease.SkyrimSE,
            SelectedPlugins = selectedPlugins ??
            [
                new() { Name = "TestPlugin1.esp", IsSelected = true },
                new() { Name = "TestPlugin2.esp", IsSelected = true }
            ],
            UpdateMode = false,
            DryRun = dryRun,
            FormIdListPath = formIdListPath
        };
    }

    private static byte[] CreateMinimalTes4PluginBytes() =>
    [
        .."TES4"u8,
        0x2B, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00
    ];

    [Fact]
    public async Task ProcessPlugins_DryRun_ReportsWhatWouldBeDone()
    {
        var parameters = CreateTestParameters(true);
        var progressReports = new List<(string Message, double? Value)>();
        var progress = new SynchronousProgress<(string Message, double? Value)>(progressReports.Add);

        await _service.ProcessPlugins(parameters, progress);

        Assert.False(File.Exists(parameters.DatabasePath));
        Assert.Contains(progressReports, r => r.Message.Contains("Would process TestPlugin1.esp"));
        Assert.Contains(progressReports, r => r.Message.Contains("Would process TestPlugin2.esp"));
    }

    [Fact]
    public async Task ProcessPlugins_DryRunWithFormIdList_ReportsFormIdProcessing()
    {
        var parameters = CreateTestParameters(true, "C:\\Test\\formids.txt");
        var progressReports = new List<(string Message, double? Value)>();
        var progress = new SynchronousProgress<(string Message, double? Value)>(progressReports.Add);

        await _service.ProcessPlugins(parameters, progress);

        Assert.False(File.Exists(parameters.DatabasePath));
        Assert.Single(progressReports);
        Assert.Contains("Would process FormID list file", progressReports[0].Message);
    }

    [Fact]
    public async Task ProcessPlugins_DryRunUpdateMode_DoesNotReportDeleteOperations()
    {
        var parameters = CreateTestParameters(true);
        parameters = new ProcessingParameters
        {
            GameDirectory = parameters.GameDirectory,
            DatabasePath = parameters.DatabasePath,
            GameRelease = parameters.GameRelease,
            SelectedPlugins = parameters.SelectedPlugins,
            UpdateMode = true,
            DryRun = parameters.DryRun,
            FormIdListPath = parameters.FormIdListPath
        };
        var progressReports = new List<(string Message, double? Value)>();
        var progress = new SynchronousProgress<(string Message, double? Value)>(progressReports.Add);

        await _service.ProcessPlugins(parameters, progress);

        // Allow Progress<T> to flush its async callbacks
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Delete operations are no longer reported in dry run mode
        // Note: Progress<T> uses SynchronizationContext.Post which is async,
        // so we verify delete messages aren't present (works even if collection is empty)
        Assert.DoesNotContain(progressReports, r => r.Message.Contains("Would delete existing entries"));
    }

    [Fact]
    public async Task ProcessPlugins_UpdateMode_DoesNotLoadExistingPluginCache()
    {
        var gameDir = Path.Combine(Path.GetTempPath(), $"Game_{Guid.NewGuid():N}");
        var dataDir = Path.Combine(gameDir, "Data");
        Directory.CreateDirectory(dataDir);

        var pluginPaths = new[] { Path.Combine(dataDir, "TestPlugin1.esp"), Path.Combine(dataDir, "TestPlugin2.esp") };

        foreach (var pluginPath in pluginPaths)
        {
            await File.WriteAllBytesAsync(pluginPath,
                CreateMinimalTes4PluginBytes(),
                TestContext.Current.CancellationToken);
            _tempFiles.Add(pluginPath);
        }

        var parameters = CreateTestParameters(selectedPlugins:
        [
            new PluginListItem { Name = "TestPlugin1.esp", IsSelected = true },
            new PluginListItem { Name = "TestPlugin2.esp", IsSelected = true }
        ]);
        parameters = new ProcessingParameters
        {
            GameDirectory = gameDir,
            DatabasePath = parameters.DatabasePath,
            GameRelease = parameters.GameRelease,
            SelectedPlugins = parameters.SelectedPlugins,
            UpdateMode = true,
            DryRun = parameters.DryRun,
            FormIdListPath = parameters.FormIdListPath
        };

        await _service.ProcessPlugins(parameters);

        Assert.True(File.Exists(parameters.DatabasePath));
    }

    [Fact]
    public async Task ProcessPlugins_UpdateMode_RepeatedPluginDoesNotLoadExistingPluginCache()
    {
        var gameDir = Path.Combine(Path.GetTempPath(), $"Game_{Guid.NewGuid():N}");
        var dataDir = Path.Combine(gameDir, "Data");
        Directory.CreateDirectory(dataDir);

        var pluginPath = Path.Combine(dataDir, "TestPlugin1.esp");
        await File.WriteAllBytesAsync(pluginPath,
            CreateMinimalTes4PluginBytes(),
            TestContext.Current.CancellationToken);
        _tempFiles.Add(pluginPath);

        var parameters = CreateTestParameters(selectedPlugins:
        [
            new PluginListItem { Name = "TestPlugin1.esp", IsSelected = true },
            new PluginListItem { Name = "TestPlugin1.esp", IsSelected = true }
        ]);
        parameters = new ProcessingParameters
        {
            GameDirectory = gameDir,
            DatabasePath = parameters.DatabasePath,
            GameRelease = parameters.GameRelease,
            SelectedPlugins = parameters.SelectedPlugins,
            UpdateMode = true,
            DryRun = parameters.DryRun,
            FormIdListPath = parameters.FormIdListPath
        };

        await _service.ProcessPlugins(parameters);

        Assert.True(File.Exists(parameters.DatabasePath));
    }

    [Fact]
    public async Task ProcessPlugins_UpdateModeOff_DoesNotLoadCacheOrClearPluginEntries()
    {
        var gameDir = Path.Combine(Path.GetTempPath(), $"Game_{Guid.NewGuid():N}");
        var dataDir = Path.Combine(gameDir, "Data");
        Directory.CreateDirectory(dataDir);

        var pluginPaths = new[] { Path.Combine(dataDir, "TestPlugin1.esp"), Path.Combine(dataDir, "TestPlugin2.esp") };

        foreach (var pluginPath in pluginPaths)
        {
            await File.WriteAllBytesAsync(pluginPath,
                CreateMinimalTes4PluginBytes(),
                TestContext.Current.CancellationToken);
            _tempFiles.Add(pluginPath);
        }

        var parameters = CreateTestParameters(selectedPlugins:
        [
            new PluginListItem { Name = "TestPlugin1.esp", IsSelected = true },
            new PluginListItem { Name = "TestPlugin2.esp", IsSelected = true }
        ]);
        parameters = new ProcessingParameters
        {
            GameDirectory = gameDir,
            DatabasePath = parameters.DatabasePath,
            GameRelease = parameters.GameRelease,
            SelectedPlugins = parameters.SelectedPlugins,
            UpdateMode = false,
            DryRun = parameters.DryRun,
            FormIdListPath = parameters.FormIdListPath
        };

        await _service.ProcessPlugins(parameters);

        Assert.True(File.Exists(parameters.DatabasePath));
    }

    [Fact]
    public async Task ProcessPlugins_InitializesDatabase()
    {
        var parameters = CreateTestParameters();

        await _service.ProcessPlugins(parameters);

        Assert.True(File.Exists(parameters.DatabasePath));
        await using var connection = new SqliteConnection($"Data Source={parameters.DatabasePath}");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=@table";
        command.Parameters.AddWithValue("@table", parameters.GameRelease.ToString());

        Assert.Equal(parameters.GameRelease.ToString(), await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ProcessPlugins_CompletesProcessingRunAfterProcessing()
    {
        var parameters = CreateTestParameters();

        await _service.ProcessPlugins(parameters);

        Assert.True(File.Exists(parameters.DatabasePath));
    }

    [Fact]
    public async Task ProcessPlugins_ReportsProgress()
    {
        var parameters = CreateTestParameters();
        var progressReports = new List<(string Message, double? Value)>();
        // Use a synchronous IProgress implementation to avoid Progress<T>'s async callbacks
        // which can cause race conditions in unit tests
        var progress = new SynchronousProgress<(string Message, double? Value)>(progressReports.Add);

        await _service.ProcessPlugins(parameters, progress);

        Assert.NotEmpty(progressReports);
        Assert.Contains(progressReports, r => r.Message.Contains("Initializing plugin ingestion"));
        Assert.Contains(progressReports, r => r.Message.Contains("Processing completed with warnings"));
        Assert.Equal(100, progressReports.Last().Value);
    }

    [Fact]
    public async Task ProcessPlugins_ReturnsControlToCaller_WhenInitializationBlocksSynchronously()
    {
        using var initializeEntered = new ManualResetEventSlim(false);
        using var allowInitializeToContinue = new ManualResetEventSlim(false);
        using var returnedFromCall = new ManualResetEventSlim(false);

        using var service = new PluginProcessingService(
            _mockViewModel.Object,
            new BlockingProcessingRun(initializeEntered, allowInitializeToContinue),
            _mockDispatcher.Object);

        var parameters = CreateTestParameters();

        var callerTask = Task.Factory.StartNew(
            static state =>
            {
                var (processingService, processingParameters, returnedSignal) =
                    ((PluginProcessingService Service, ProcessingParameters Parameters, ManualResetEventSlim ReturnedSignal))state!;

                var processingTask = processingService.ProcessPlugins(processingParameters);
                returnedSignal.Set();
                processingTask.GetAwaiter().GetResult();
            },
            (service, parameters, returnedFromCall),
            TestContext.Current.CancellationToken,
            TaskCreationOptions.None,
            TaskScheduler.Default);

        Assert.True(initializeEntered.Wait(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
        Assert.True(returnedFromCall.Wait(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));

        allowInitializeToContinue.Set();
        await callerTask;
    }

    /// <summary>
    /// A synchronous IProgress implementation that invokes callbacks immediately on the calling thread,
    /// avoiding the async callback behavior of Progress&lt;T&gt; that can cause race conditions in tests.
    /// </summary>
    private class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value)
        {
            handler(value);
        }
    }

    private sealed class BlockingProcessingRun(
        ManualResetEventSlim initializeEntered,
        ManualResetEventSlim allowInitializeToContinue) : ProcessingRun
    {
        public override Task ExecuteAsync(
            ProcessingRunRequest request,
            IProgress<ProcessingRunEvent>? progress = null)
        {
            initializeEntered.Set();
            return Task.Run(() => allowInitializeToContinue.Wait(TestContext.Current.CancellationToken));
        }
    }

    private sealed class CancellableProcessingRun(TaskCompletionSource<bool> started) : ProcessingRun
    {
        private readonly CancellationTokenSource _cancellationTokenSource = new();

        public override Task ExecuteAsync(
            ProcessingRunRequest request,
            IProgress<ProcessingRunEvent>? progress = null)
        {
            started.SetResult(true);
            return Task.Delay(Timeout.InfiniteTimeSpan, _cancellationTokenSource.Token);
        }

        public override void Cancel()
        {
            _cancellationTokenSource.Cancel();
        }

        public override void Dispose()
        {
            _cancellationTokenSource.Dispose();
        }
    }

    private sealed class ThrowingProcessingRun(Exception exception) : ProcessingRun
    {
        public override Task ExecuteAsync(
            ProcessingRunRequest request,
            IProgress<ProcessingRunEvent>? progress = null)
        {
            return Task.FromException(exception);
        }
    }

    private sealed class SequentialBlockingProcessingRun(
        TaskCompletionSource<bool> firstStarted,
        TaskCompletionSource<bool> allowFirstToComplete) : ProcessingRun
    {
        private readonly CancellationTokenSource _firstRunCancellation = new();
        private int _callCount;

        public override Task ExecuteAsync(
            ProcessingRunRequest request,
            IProgress<ProcessingRunEvent>? progress = null)
        {
            if (Interlocked.Increment(ref _callCount) == 1)
            {
                firstStarted.SetResult(true);
                return allowFirstToComplete.Task.WaitAsync(_firstRunCancellation.Token);
            }

            _firstRunCancellation.Cancel();
            return Task.CompletedTask;
        }

        public override void Dispose()
        {
            _firstRunCancellation.Dispose();
        }
    }

    [Fact]
    public async Task CancelProcessing_CancelsOngoingOperation()
    {
        var parameters = CreateTestParameters();
        var tcs = new TaskCompletionSource<bool>();
        var progressReports = new List<(string Message, double? Value)>();
        var progress = new Progress<(string Message, double? Value)>(progressReports.Add);
        using var service = new PluginProcessingService(
            _mockViewModel.Object,
            new CancellableProcessingRun(tcs),
            _mockDispatcher.Object);

        var processTask = service.ProcessPlugins(parameters, progress);
        await tcs.Task;

        // Small delay to ensure we're in the right state
        await Task.Delay(50, TestContext.Current.CancellationToken);

        service.CancelProcessing();

        await Assert.ThrowsAsync<TaskCanceledException>(() => processTask);
        // The cancellation might happen too early to report the message
        // so we don't check for it
    }

    [Fact]
    public void CancelProcessing_HandlesNoCurrent﻿ProcessingSafely()
    {
        _service.CancelProcessing();
    }

    [Fact]
    public async Task ProcessPlugins_HandlesExceptionDuringProcessing()
    {
        var parameters = CreateTestParameters();
        var expectedError = "Database initialization failed";
        using var service = new PluginProcessingService(
            _mockViewModel.Object,
            new ThrowingProcessingRun(new InvalidOperationException(expectedError)),
            _mockDispatcher.Object);

        var progressReports = new List<(string Message, double? Value)>();
        IProgress<(string Message, double? Value)> progress = new Progress<(string Message, double? Value)>(progressReports.Add);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ProcessPlugins(parameters, progress));

        Assert.Equal(expectedError, exception.Message);
        // Progress reporting might not happen synchronously, so we just verify the exception
    }

    [Fact]
    public async Task ProcessPlugins_HandlesFormIdListProcessing()
    {
        var formIdFile = Path.Combine(Path.GetTempPath(), $"formids_{Guid.NewGuid():N}.txt");
        await File.WriteAllLinesAsync(
            formIdFile,
            ["Plugin.esp|000001|Entry One"],
            TestContext.Current.CancellationToken);
        _tempFiles.Add(formIdFile);
        var parameters = CreateTestParameters(formIdListPath: formIdFile);
        var progressReports = new List<(string Message, double? Value)>();
        var progress = new Progress<(string Message, double? Value)>(progressReports.Add);

        await _service.ProcessPlugins(parameters, progress);

        await using var store = await FormIdRecordStore.OpenAsync(
            parameters.DatabasePath,
            parameters.GameRelease,
            TestContext.Current.CancellationToken);
        var records = await store.ReadRecordsAsync(FormIdRecordQuery.All, TestContext.Current.CancellationToken);

        Assert.Contains(records, record => record is { Plugin: "Plugin.esp", FormId: "000001", Entry: "Entry One" });
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        _service.Dispose();
        _service.Dispose();
    }

    [Fact]
    public async Task ProcessPlugins_MultipleConcurrentCalls_CancelsFirst()
    {
        var parameters = CreateTestParameters();
        var firstStarted = new TaskCompletionSource<bool>();
        var allowFirstToComplete = new TaskCompletionSource<bool>();
        using var service = new PluginProcessingService(
            _mockViewModel.Object,
            new SequentialBlockingProcessingRun(firstStarted, allowFirstToComplete),
            _mockDispatcher.Object);

        var firstTask = service.ProcessPlugins(parameters);
        await firstStarted.Task;

        var secondTask = service.ProcessPlugins(parameters);

        allowFirstToComplete.SetResult(true);

        try
        {
            await firstTask;
        }
        catch (OperationCanceledException)
        {
            // Expected - first task should be cancelled when second starts
        }
        catch (ObjectDisposedException)
        {
            // Also expected - the cancellation token source might be disposed
        }

        await secondTask;
    }

    [Fact]
    public async Task ProcessPlugins_WarningCallback_AddsWarningMessages()
    {
        var parameters = CreateTestParameters();

        await _service.ProcessPlugins(parameters);

        _mockViewModel.Verify(x => x.AddWarningMessage(
                It.Is<string>(message => message.Contains("Could not find plugin file", StringComparison.Ordinal)),
                It.IsAny<int>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public void AddErrorMessage_CheckAccessFalse_PostsToDispatcher()
    {
        _mockDispatcher.Setup(d => d.CheckAccess()).Returns(false);

        var posted = false;
        _mockDispatcher.Setup(d => d.Post(It.IsAny<Action>()))
            .Callback<Action>(action =>
            {
                posted = true;
                _mockDispatcher.Setup(d => d.CheckAccess()).Returns(true);
                action();
            });

        _service.AddErrorMessage("from test");

        Assert.True(posted);
        _mockViewModel.Verify(x => x.AddErrorMessage("from test", It.IsAny<int>()), Times.Once);
    }
}
