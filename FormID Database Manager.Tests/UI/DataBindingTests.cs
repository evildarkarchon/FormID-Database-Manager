#nullable enable

using System.ComponentModel;
using FormID_Database_Manager.Models;
using FormID_Database_Manager.TestUtilities.Mocks;
using FormID_Database_Manager.ViewModels;
using Xunit;

namespace FormID_Database_Manager.Tests.UI;

public class DataBindingTests
{
    [Fact]
    public void ViewModel_ImplementsINotifyPropertyChanged()
    {
        var viewModel = new MainWindowViewModel(new SynchronousThreadDispatcher());
        Assert.IsAssignableFrom<INotifyPropertyChanged>(viewModel);
    }

    [Fact]
    public void GameDirectory_NotifiesPropertyChanged()
    {
        var viewModel = new MainWindowViewModel(new SynchronousThreadDispatcher());
        string? changedProperty = null;

        viewModel.PropertyChanged += (_, e) => changedProperty = e.PropertyName;

        viewModel.GameDirectory = @"C:\Games\Skyrim";

        Assert.Equal(nameof(MainWindowViewModel.GameDirectory), changedProperty);
    }

    [Fact]
    public void Progress_UpdatesMultipleProperties()
    {
        var viewModel = new MainWindowViewModel(new SynchronousThreadDispatcher());
        var progressValueChanged = false;
        var progressStatusChanged = false;

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.ProgressValue))
            {
                progressValueChanged = true;
            }

            if (e.PropertyName == nameof(MainWindowViewModel.ProgressStatus))
            {
                progressStatusChanged = true;
            }
        };

        viewModel.UpdateProgress("Processing...", 50);

        Assert.True(progressValueChanged);
        Assert.True(progressStatusChanged);
        Assert.Equal(50, viewModel.ProgressValue);
        Assert.Equal("Processing...", viewModel.ProgressStatus);
    }

    [Fact]
    public void FilteredPlugins_UpdatesWithSearchFilter()
    {
        var viewModel = new MainWindowViewModel(new SynchronousThreadDispatcher());

        viewModel.Plugins.Add(new PluginListItem { Name = "Skyrim.esm" });
        viewModel.Plugins.Add(new PluginListItem { Name = "Update.esm" });
        viewModel.Plugins.Add(new PluginListItem { Name = "Dawnguard.esm" });
        viewModel.Plugins.Add(new PluginListItem { Name = "TestMod.esp" });

        viewModel.PluginFilter = "esm";

        Assert.Equal(3, viewModel.FilteredPlugins.Count);
        Assert.All(viewModel.FilteredPlugins, p => Assert.Contains(".esm", p.Name));

        viewModel.PluginFilter = "";
        Assert.Equal(4, viewModel.FilteredPlugins.Count);
    }

    [Fact]
    public void PluginSelection_PropagatesChanges()
    {
        var plugin = new PluginListItem { Name = "Test.esp", IsSelected = false };
        var raised = false;

        plugin.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PluginListItem.IsSelected))
            {
                raised = true;
            }
        };

        plugin.IsSelected = true;

        Assert.True(raised);
        Assert.True(plugin.IsSelected);
    }
}
