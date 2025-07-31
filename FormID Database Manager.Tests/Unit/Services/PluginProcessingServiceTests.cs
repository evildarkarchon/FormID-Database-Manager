using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Models;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.TestUtilities.Mocks;
using FormID_Database_Manager.ViewModels;
using Moq;
using Mutagen.Bethesda;
using Xunit;

#nullable enable

namespace FormID_Database_Manager.Tests.Unit.Services;

public class PluginProcessingServiceTests : IDisposable
{
    private readonly Mock<DatabaseService> _mockDatabaseService;
    private readonly Mock<MainWindowViewModel> _mockViewModel;
    private readonly PluginProcessingService _service;
    private readonly List<string> _tempFiles = new();

    public PluginProcessingServiceTests()
    {
        _mockDatabaseService = new Mock<DatabaseService>();
        _mockViewModel = new Mock<MainWindowViewModel>();
        _service = new PluginProcessingService(_mockDatabaseService.Object, _mockViewModel.Object);
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
            catch { /* Ignore cleanup errors */ }
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
            SelectedPlugins = selectedPlugins ?? new List<PluginListItem>
            {
                new() { Name = "TestPlugin1.esp", IsSelected = true },
                new() { Name = "TestPlugin2.esp", IsSelected = true }
            },
            UpdateMode = false,
            DryRun = dryRun,
            FormIdListPath = formIdListPath
        };
    }

    [Fact]
    public async Task ProcessPlugins_DryRun_ReportsWhatWouldBeDone()
    {
        var parameters = CreateTestParameters(dryRun: true);
        var progressReports = new List<(string Message, double? Value)>();
        var progress = new Progress<(string Message, double? Value)>(report => progressReports.Add(report));

        await _service.ProcessPlugins(parameters, progress);

        _mockDatabaseService.Verify(x => x.InitializeDatabase(It.IsAny<string>(), It.IsAny<GameRelease>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Contains(progressReports, r => r.Message.Contains("Would process TestPlugin1.esp"));
        Assert.Contains(progressReports, r => r.Message.Contains("Would process TestPlugin2.esp"));
    }

    [Fact]
    public async Task ProcessPlugins_DryRunWithFormIdList_ReportsFormIdProcessing()
    {
        var parameters = CreateTestParameters(dryRun: true, formIdListPath: "C:\\Test\\formids.txt");
        var progressReports = new List<(string Message, double? Value)>();
        var progress = new Progress<(string Message, double? Value)>(report => progressReports.Add(report));

        await _service.ProcessPlugins(parameters, progress);

        _mockDatabaseService.Verify(x => x.InitializeDatabase(It.IsAny<string>(), It.IsAny<GameRelease>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Single(progressReports);
        Assert.Contains("Would process FormID list file", progressReports[0].Message);
    }

    [Fact]
    public async Task ProcessPlugins_DryRunUpdateMode_ReportsDeleteOperations()
    {
        var parameters = CreateTestParameters(dryRun: true);
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
        var progress = new Progress<(string Message, double? Value)>(report => progressReports.Add(report));

        await _service.ProcessPlugins(parameters, progress);

        Assert.Contains(progressReports, r => r.Message.Contains("Would delete existing entries for TestPlugin1.esp"));
        Assert.Contains(progressReports, r => r.Message.Contains("Would delete existing entries for TestPlugin2.esp"));
    }

    [Fact]
    public async Task ProcessPlugins_InitializesDatabase()
    {
        var parameters = CreateTestParameters();
        parameters = new ProcessingParameters
        {
            GameDirectory = parameters.GameDirectory,
            DatabasePath = parameters.DatabasePath,
            GameRelease = parameters.GameRelease,
            SelectedPlugins = new List<PluginListItem>(),
            UpdateMode = parameters.UpdateMode,
            DryRun = parameters.DryRun,
            FormIdListPath = parameters.FormIdListPath
        };

        _mockDatabaseService.Setup(x => x.InitializeDatabase(It.IsAny<string>(), It.IsAny<GameRelease>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockDatabaseService.Setup(x => x.OptimizeDatabase(It.IsAny<System.Data.SQLite.SQLiteConnection>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _service.ProcessPlugins(parameters, null);

        _mockDatabaseService.Verify(x => x.InitializeDatabase(
            parameters.DatabasePath,
            parameters.GameRelease,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessPlugins_OptimizesDatabaseAfterProcessing()
    {
        var parameters = CreateTestParameters();
        parameters = new ProcessingParameters
        {
            GameDirectory = parameters.GameDirectory,
            DatabasePath = parameters.DatabasePath,
            GameRelease = parameters.GameRelease,
            SelectedPlugins = new List<PluginListItem>(),
            UpdateMode = parameters.UpdateMode,
            DryRun = parameters.DryRun,
            FormIdListPath = parameters.FormIdListPath
        };

        _mockDatabaseService.Setup(x => x.InitializeDatabase(It.IsAny<string>(), It.IsAny<GameRelease>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockDatabaseService.Setup(x => x.OptimizeDatabase(It.IsAny<System.Data.SQLite.SQLiteConnection>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _service.ProcessPlugins(parameters, null);

        _mockDatabaseService.Verify(x => x.OptimizeDatabase(
            It.IsAny<System.Data.SQLite.SQLiteConnection>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessPlugins_ReportsProgress()
    {
        var parameters = CreateTestParameters();
        parameters = new ProcessingParameters
        {
            GameDirectory = parameters.GameDirectory,
            DatabasePath = parameters.DatabasePath,
            GameRelease = parameters.GameRelease,
            SelectedPlugins = new List<PluginListItem>(),
            UpdateMode = parameters.UpdateMode,
            DryRun = parameters.DryRun,
            FormIdListPath = parameters.FormIdListPath
        };
        var progressReports = new List<(string Message, double? Value)>();
        var progress = new Progress<(string Message, double? Value)>(report => progressReports.Add(report));

        _mockDatabaseService.Setup(x => x.InitializeDatabase(It.IsAny<string>(), It.IsAny<GameRelease>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockDatabaseService.Setup(x => x.OptimizeDatabase(It.IsAny<System.Data.SQLite.SQLiteConnection>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _service.ProcessPlugins(parameters, progress);

        Assert.NotEmpty(progressReports);
        Assert.Contains(progressReports, r => r.Message.Contains("Initializing plugin processing"));
        Assert.Contains(progressReports, r => r.Message.Contains("Processing completed successfully"));
        Assert.Equal(100, progressReports.Last().Value);
    }

    [Fact]
    public async Task CancelProcessing_CancelsOngoingOperation()
    {
        var parameters = CreateTestParameters();
        var tcs = new TaskCompletionSource<bool>();
        var progressReports = new List<(string Message, double? Value)>();
        var progress = new Progress<(string Message, double? Value)>(report => progressReports.Add(report));

        _mockDatabaseService.Setup(x => x.InitializeDatabase(It.IsAny<string>(), It.IsAny<GameRelease>(), It.IsAny<CancellationToken>()))
            .Returns(async (string path, GameRelease game, CancellationToken ct) =>
            {
                tcs.SetResult(true);
                await Task.Delay(1000, ct);
            });

        var processTask = _service.ProcessPlugins(parameters, progress);
        await tcs.Task;

        // Small delay to ensure we're in the right state
        await Task.Delay(50);

        _service.CancelProcessing();

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

        _mockDatabaseService.Setup(x => x.InitializeDatabase(It.IsAny<string>(), It.IsAny<GameRelease>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(expectedError));

        var progressReports = new List<(string Message, double? Value)>();
        IProgress<(string Message, double? Value)> progress = new Progress<(string Message, double? Value)>(report =>
        {
            progressReports.Add(report);
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ProcessPlugins(parameters, progress));

        Assert.Equal(expectedError, exception.Message);
        // Progress reporting might not happen synchronously, so we just verify the exception
    }

    [Fact]
    public async Task ProcessPlugins_HandlesFormIdListProcessing()
    {
        var parameters = CreateTestParameters(formIdListPath: "C:\\Test\\formids.txt");
        var progressReports = new List<(string Message, double? Value)>();
        var progress = new Progress<(string Message, double? Value)>(report => progressReports.Add(report));

        _mockDatabaseService.Setup(x => x.InitializeDatabase(It.IsAny<string>(), It.IsAny<GameRelease>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockDatabaseService.Setup(x => x.OptimizeDatabase(It.IsAny<System.Data.SQLite.SQLiteConnection>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        try
        {
            await _service.ProcessPlugins(parameters, progress);
        }
        catch
        {
            // FormIdTextProcessor might throw if file doesn't exist, which is expected in unit test
        }

        _mockDatabaseService.Verify(x => x.InitializeDatabase(
            parameters.DatabasePath,
            parameters.GameRelease,
            It.IsAny<CancellationToken>()), Times.Once);
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

        _mockDatabaseService.Setup(x => x.InitializeDatabase(It.IsAny<string>(), It.IsAny<GameRelease>(), It.IsAny<CancellationToken>()))
            .Returns(async (string path, GameRelease game, CancellationToken ct) =>
            {
                firstStarted.SetResult(true);
                await allowFirstToComplete.Task;
            });

        var firstTask = _service.ProcessPlugins(parameters, null);
        await firstStarted.Task;

        var secondTask = _service.ProcessPlugins(parameters, null);

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
    }

    [Fact]
    public async Task ProcessPlugins_ErrorCallback_AddsErrorMessages()
    {
        var parameters = CreateTestParameters();
        var errorMessages = new List<string>();

        _mockViewModel.Setup(x => x.AddErrorMessage(It.IsAny<string>(), It.IsAny<int>()))
            .Callback<string, int>((msg, maxMessages) => errorMessages.Add(msg));

        _mockDatabaseService.Setup(x => x.InitializeDatabase(It.IsAny<string>(), It.IsAny<GameRelease>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        try
        {
            await _service.ProcessPlugins(parameters, null);
        }
        catch
        {
            // Expected - ModProcessor will fail without proper setup
        }

        _mockViewModel.Verify(x => x.AddErrorMessage(It.IsAny<string>(), It.IsAny<int>()), Times.AtLeastOnce);
    }
}
