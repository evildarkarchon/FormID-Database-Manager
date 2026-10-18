#nullable enable

using FormID_Database_Manager.Services;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

/// <summary>
/// Covers the picker outcome's shared "the user selected a path" check, which the Game Context and the User Workflow's
/// database and FormID-list pickers all read instead of each repeating it.
/// </summary>
public sealed class FileDialogResultTests
{
    /// <summary>
    /// Verifies that a successful pick hands back exactly the path the picker produced.
    /// </summary>
    [Fact]
    public void TryGetSelectedPath_Success_ReturnsTheSelectedPath()
    {
        var result = FileDialogResult.Success(@"C:\Games\Skyrim");

        var selected = result.TryGetSelectedPath(out var path);

        Assert.True(selected);
        Assert.Equal(@"C:\Games\Skyrim", path);
    }

    /// <summary>
    /// Verifies that a dismissed picker selects nothing.
    /// </summary>
    [Fact]
    public void TryGetSelectedPath_Cancelled_SelectsNoPath()
    {
        var selected = FileDialogResult.Cancelled().TryGetSelectedPath(out var path);

        Assert.False(selected);
        Assert.Null(path);
    }

    /// <summary>
    /// Verifies that a failed picker selects nothing; reporting the failure stays with each caller, because each one
    /// words it differently.
    /// </summary>
    [Fact]
    public void TryGetSelectedPath_Failure_SelectsNoPath()
    {
        var selected = FileDialogResult.Failure("picker unavailable").TryGetSelectedPath(out var path);

        Assert.False(selected);
        Assert.Null(path);
    }
}
