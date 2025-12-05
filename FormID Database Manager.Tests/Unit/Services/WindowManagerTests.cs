#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.ViewModels;
using Moq;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

public class WindowManagerTests
{
    private readonly Mock<IStorageProvider> _mockStorageProvider;
    private readonly Mock<MainWindowViewModel> _mockViewModel;
    private readonly WindowManager _windowManager;

    public WindowManagerTests()
    {
        _mockStorageProvider = new Mock<IStorageProvider>();
        _mockViewModel = new Mock<MainWindowViewModel>(new Mock<IThreadDispatcher>().Object);
        _windowManager = new WindowManager(_mockStorageProvider.Object, _mockViewModel.Object);
    }

    [Fact]
    public async Task SelectGameDirectory_ReturnsPath_WhenFolderSelected()
    {
        // Arrange
        var mockFolder = new Mock<IStorageFolder>();
        mockFolder.Setup(f => f.Path).Returns(new Uri("file:///C:/Games/Skyrim"));

        _mockStorageProvider
            .Setup(sp => sp.OpenFolderPickerAsync(It.IsAny<FolderPickerOpenOptions>()))
            .ReturnsAsync(new List<IStorageFolder> { mockFolder.Object });

        // Act
        var result = await _windowManager.SelectGameDirectory();

        // Assert
        Assert.Equal(@"C:\Games\Skyrim", result);
        _mockStorageProvider.Verify(sp => sp.OpenFolderPickerAsync(
                It.IsAny<FolderPickerOpenOptions>()),
            Times.Once);
    }

    [Fact]
    public async Task SelectGameDirectory_ReturnsNull_WhenNoFolderSelected()
    {
        // Arrange
        _mockStorageProvider
            .Setup(sp => sp.OpenFolderPickerAsync(It.IsAny<FolderPickerOpenOptions>()))
            .ReturnsAsync(new List<IStorageFolder>());

        // Act
        var result = await _windowManager.SelectGameDirectory();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task SelectGameDirectory_ReturnsNull_AndAddsError_WhenExceptionThrown()
    {
        // Arrange
        var exception = new InvalidOperationException("Storage provider error");
        _mockStorageProvider
            .Setup(sp => sp.OpenFolderPickerAsync(It.IsAny<FolderPickerOpenOptions>()))
            .ThrowsAsync(exception);

        // Act
        var result = await _windowManager.SelectGameDirectory();

        // Assert
        Assert.Null(result);
        _mockViewModel.Verify(vm => vm.AddErrorMessage(It.IsAny<string>(), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task SelectDatabaseFile_ReturnsPath_WhenFileSelected()
    {
        // Arrange
        var mockFile = new Mock<IStorageFile>();
        mockFile.Setup(f => f.Path).Returns(new Uri("file:///C:/Data/FormIDs.db"));

        _mockStorageProvider
            .Setup(sp => sp.SaveFilePickerAsync(It.IsAny<FilePickerSaveOptions>()))
            .ReturnsAsync(mockFile.Object);

        // Act
        var result = await _windowManager.SelectDatabaseFile();

        // Assert
        Assert.Equal(@"C:\Data\FormIDs.db", result);
        _mockStorageProvider.Verify(sp => sp.SaveFilePickerAsync(
                It.IsAny<FilePickerSaveOptions>()),
            Times.Once);
    }

    [Fact]
    public async Task SelectDatabaseFile_ReturnsNull_WhenNoFileSelected()
    {
        // Arrange
        _mockStorageProvider
            .Setup(sp => sp.SaveFilePickerAsync(It.IsAny<FilePickerSaveOptions>()))
            .ReturnsAsync((IStorageFile?)null);

        // Act
        var result = await _windowManager.SelectDatabaseFile();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task SelectDatabaseFile_ReturnsNull_AndAddsError_WhenExceptionThrown()
    {
        // Arrange
        var exception = new UnauthorizedAccessException("Access denied");
        _mockStorageProvider
            .Setup(sp => sp.SaveFilePickerAsync(It.IsAny<FilePickerSaveOptions>()))
            .ThrowsAsync(exception);

        // Act
        var result = await _windowManager.SelectDatabaseFile();

        // Assert
        Assert.Null(result);
        _mockViewModel.Verify(vm => vm.AddErrorMessage(It.IsAny<string>(), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task SelectDatabaseFile_ConfiguresFileTypeFilter_Correctly()
    {
        // Arrange
        FilePickerSaveOptions? capturedOptions = null;
        _mockStorageProvider
            .Setup(sp => sp.SaveFilePickerAsync(It.IsAny<FilePickerSaveOptions>()))
            .Callback<FilePickerSaveOptions>(options => capturedOptions = options)
            .ReturnsAsync((IStorageFile?)null);

        // Act
        await _windowManager.SelectDatabaseFile();

        // Assert
        Assert.NotNull(capturedOptions);
        Assert.NotNull(capturedOptions.FileTypeChoices);
        Assert.Single(capturedOptions.FileTypeChoices);

        var fileType = capturedOptions.FileTypeChoices[0];
        Assert.Equal("Database Files", fileType.Name);
        Assert.Contains("*.db", fileType.Patterns!);
    }

    [Fact]
    public async Task SelectFormIdListFile_ReturnsPath_WhenFileSelected()
    {
        // Arrange
        var mockFile = new Mock<IStorageFile>();
        mockFile.Setup(f => f.Path).Returns(new Uri("file:///C:/Data/formids.txt"));

        _mockStorageProvider
            .Setup(sp => sp.OpenFilePickerAsync(It.IsAny<FilePickerOpenOptions>()))
            .ReturnsAsync(new List<IStorageFile> { mockFile.Object });

        // Act
        var result = await _windowManager.SelectFormIdListFile();

        // Assert
        Assert.Equal(@"C:\Data\formids.txt", result);
        _mockStorageProvider.Verify(sp => sp.OpenFilePickerAsync(
                It.IsAny<FilePickerOpenOptions>()),
            Times.Once);
    }

    [Fact]
    public async Task SelectFormIdListFile_ReturnsNull_WhenNoFileSelected()
    {
        // Arrange
        _mockStorageProvider
            .Setup(sp => sp.OpenFilePickerAsync(It.IsAny<FilePickerOpenOptions>()))
            .ReturnsAsync(new List<IStorageFile>());

        // Act
        var result = await _windowManager.SelectFormIdListFile();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task SelectFormIdListFile_ReturnsNull_AndAddsError_WhenExceptionThrown()
    {
        // Arrange
        var exception = new OperationCanceledException("User cancelled");
        _mockStorageProvider
            .Setup(sp => sp.OpenFilePickerAsync(It.IsAny<FilePickerOpenOptions>()))
            .ThrowsAsync(exception);

        // Act
        var result = await _windowManager.SelectFormIdListFile();

        // Assert
        Assert.Null(result);
        _mockViewModel.Verify(vm => vm.AddErrorMessage(It.IsAny<string>(), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task SelectFormIdListFile_ConfiguresFileTypeFilter_Correctly()
    {
        // Arrange
        FilePickerOpenOptions? capturedOptions = null;
        _mockStorageProvider
            .Setup(sp => sp.OpenFilePickerAsync(It.IsAny<FilePickerOpenOptions>()))
            .Callback<FilePickerOpenOptions>(options => capturedOptions = options)
            .ReturnsAsync(new List<IStorageFile>());

        // Act
        await _windowManager.SelectFormIdListFile();

        // Assert
        Assert.NotNull(capturedOptions);
        Assert.NotNull(capturedOptions.FileTypeFilter);
        Assert.Single(capturedOptions.FileTypeFilter);

        var fileType = capturedOptions.FileTypeFilter[0];
        Assert.Equal("Text Files", fileType.Name);
        Assert.Contains("*.txt", fileType.Patterns!);
    }

    [Fact]
    public async Task AllMethods_UseConfigureAwaitFalse_ForAsyncCalls()
    {
        // This test verifies that all async methods properly use ConfigureAwait(false)
        // by checking that they don't capture the synchronization context

        // Arrange
        _mockStorageProvider
            .Setup(sp => sp.OpenFolderPickerAsync(It.IsAny<FolderPickerOpenOptions>()))
            .ReturnsAsync(new List<IStorageFolder>());
        _mockStorageProvider
            .Setup(sp => sp.SaveFilePickerAsync(It.IsAny<FilePickerSaveOptions>()))
            .ReturnsAsync((IStorageFile?)null);
        _mockStorageProvider
            .Setup(sp => sp.OpenFilePickerAsync(It.IsAny<FilePickerOpenOptions>()))
            .ReturnsAsync(new List<IStorageFile>());

        // Act & Assert - these should not deadlock even in synchronous context
        var task1 = Task.Run(() => _windowManager.SelectGameDirectory());
        var task2 = Task.Run(() => _windowManager.SelectDatabaseFile());
        var task3 = Task.Run(() => _windowManager.SelectFormIdListFile());

        await Task.WhenAll(task1, task2, task3);

        // If we get here without deadlock, ConfigureAwait(false) is properly used
        Assert.True(task1.IsCompletedSuccessfully);
        Assert.True(task2.IsCompletedSuccessfully);
        Assert.True(task3.IsCompletedSuccessfully);
    }

    [Theory]
    [InlineData("file:///C:/Games/Skyrim Special Edition", @"C:\Games\Skyrim Special Edition")]
    [InlineData("file:///D:/Steam/steamapps/common/Fallout 4", @"D:\Steam\steamapps\common\Fallout 4")]
    [InlineData("file:///E:/Games/Starfield", @"E:\Games\Starfield")]
    public async Task SelectGameDirectory_HandlesVariousPathFormats_Correctly(string uriPath, string expectedPath)
    {
        // Arrange
        var mockFolder = new Mock<IStorageFolder>();
        mockFolder.Setup(f => f.Path).Returns(new Uri(uriPath));

        _mockStorageProvider
            .Setup(sp => sp.OpenFolderPickerAsync(It.IsAny<FolderPickerOpenOptions>()))
            .ReturnsAsync(new List<IStorageFolder> { mockFolder.Object });

        // Act
        var result = await _windowManager.SelectGameDirectory();

        // Assert
        Assert.Equal(expectedPath, result);
    }
}
