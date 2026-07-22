using System;
using System.Collections.Generic;
using System.Threading;
using FormID_Database_Manager.Services;
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
            .Returns(["Skyrim.esm", "Update.esm", "Dawnguard.esm"]);

        return mock;
    }

    public static Mock<IGameLocationService> CreateGameLocationServiceMock(
        List<string>? folders = null)
    {
        var mock = new Mock<IGameLocationService>();

        mock.Setup(x => x.GetGameFolders(It.IsAny<GameRelease>()))
            .Returns(folders ?? []);

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
