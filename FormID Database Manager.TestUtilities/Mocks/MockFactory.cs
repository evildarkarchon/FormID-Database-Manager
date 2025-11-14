using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Models;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.ViewModels;
using Moq;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.TestUtilities.Mocks;

public static class MockFactory
{
    public static Mock<Action<double, string>> CreateProgressCallbackMock()
    {
        return new Mock<Action<double, string>>();
    }

    public static Mock<Action<string, bool>> CreateErrorCallbackMock()
    {
        return new Mock<Action<string, bool>>();
    }

    public static Mock<GameDetectionService> CreateGameDetectionServiceMock()
    {
        var mock = new Mock<GameDetectionService>();

        mock.Setup(x => x.DetectGame(It.IsAny<string>()))
            .Returns(GameRelease.SkyrimSE);

        mock.Setup(x => x.GetBaseGamePlugins(It.IsAny<GameRelease>()))
            .Returns(new HashSet<string> { "Skyrim.esm", "Update.esm", "Dawnguard.esm" });

        return mock;
    }

    public static Mock<DatabaseService> CreateDatabaseServiceMock()
    {
        var mock = new Mock<DatabaseService>();

        mock.Setup(x =>
                x.InitializeDatabase(It.IsAny<string>(), It.IsAny<GameRelease>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        mock.Setup(x => x.InsertRecord(It.IsAny<SQLiteConnection>(), It.IsAny<GameRelease>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        mock.Setup(x => x.ClearPluginEntries(It.IsAny<SQLiteConnection>(), It.IsAny<GameRelease>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        mock.Setup(x => x.OptimizeDatabase(It.IsAny<SQLiteConnection>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return mock;
    }

    public static Mock<PluginListManager> CreatePluginListManagerMock(GameDetectionService gameDetectionService,
        MainWindowViewModel viewModel)
    {
        var mock = new Mock<PluginListManager>(gameDetectionService, viewModel);

        mock.Setup(x => x.RefreshPluginList(
                It.IsAny<string>(),
                It.IsAny<GameRelease>(),
                It.IsAny<ObservableCollection<PluginListItem>>(),
                It.IsAny<bool>()))
            .Returns(Task.CompletedTask);

        return mock;
    }

    public static CancellationTokenSource CreateCancellationTokenSource(int? delayMilliseconds = null)
    {
        var cts = new CancellationTokenSource();
        if (delayMilliseconds.HasValue)
        {
            cts.CancelAfter(delayMilliseconds.Value);
        }

        return cts;
    }
}
