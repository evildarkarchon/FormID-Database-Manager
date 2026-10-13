using FormID_Database_Manager.Models;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.ViewModels;
using FormID_Database_Manager.WinUI.Services;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.WinUI;

public sealed partial class MainWindow : Window, IDisposable
{
    private readonly UserWorkflow _userWorkflow;
    private bool _disposed;

    /// <summary>
    ///     Initializes the WinUI main window with production platform services.
    /// </summary>
    /// <remarks>
    ///     The shell supplies only platform pieces — the dispatcher, the ViewModel, and the file dialog adapter — and
    ///     the User Workflow factory builds every Core collaborator. The window is initialized first so the file dialog
    ///     adapter is built after the window exists.
    /// </remarks>
    public MainWindow()
    {
        var dispatcher = new WinUiThreadDispatcher(DispatcherQueue);
        ViewModel = new MainWindowViewModel(dispatcher);

        InitializeWindow();
        _userWorkflow = UserWorkflow.Create(
            ViewModel,
            dispatcher,
            new WinUiFileDialogService(AppWindow));
    }

    public MainWindowViewModel ViewModel { get; }

    /// <summary>
    ///     Cancels in-flight processing and releases services owned by this window.
    /// </summary>
    /// <remarks>
    ///     The User Workflow owns the teardown order of every Core collaborator, including detaching Plugin List
    ///     presentation first, and retires all of them even when one step fails. A failure it surfaces, such as a
    ///     cancellation callback failure from the run being cancelled, propagates unchanged.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Closed -= MainWindow_Closed;
        _userWorkflow.Dispose();
    }

    /// <summary>
    ///     Initializes XAML, assigns the root ViewModel, and attaches close-time cleanup.
    /// </summary>
    private void InitializeWindow()
    {
        InitializeComponent();
        Root.DataContext = ViewModel;
        Closed += MainWindow_Closed;
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        Dispose();
    }

    /// <summary>
    ///     Forwards the explicit GameRelease selection to the authoritative User Workflow.
    /// </summary>
    /// <param name="sender">The ComboBox whose typed selected value is forwarded.</param>
    /// <param name="e">The selection-change event details.</param>
    private async void GameComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            var selectedGameRelease = sender is ComboBox { SelectedItem: GameRelease gameRelease }
                ? gameRelease
                : (GameRelease?)null;
            await _userWorkflow.SelectGameReleaseAsync(selectedGameRelease);
        }
        catch (Exception ex)
        {
            ViewModel.AddErrorMessage($"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>
    ///     Forwards detected-directory control changes to the authoritative User Workflow boundary.
    /// </summary>
    /// <param name="sender">The ComboBox whose selected directory value is forwarded.</param>
    /// <param name="e">The selection-change event details.</param>
    private async void DirectoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (sender is not ComboBox { SelectedItem: string selectedDirectory })
            {
                return;
            }

            await _userWorkflow.SelectDetectedDirectoryAsync(selectedDirectory);
        }
        catch (Exception ex)
        {
            ViewModel.AddErrorMessage($"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>
    ///     Forwards the explicit Advanced Mode value to the authoritative User Workflow.
    /// </summary>
    /// <param name="sender">The CheckBox whose selected mode is forwarded.</param>
    /// <param name="e">The click event details.</param>
    private async void AdvancedModeCheckBox_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not CheckBox checkBox)
            {
                return;
            }

            var advancedMode = checkBox.IsChecked == true ? AdvancedMode.On : AdvancedMode.Off;
            await _userWorkflow.SetAdvancedModeAsync(advancedMode);
        }
        catch (Exception ex)
        {
            ViewModel.AddErrorMessage($"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>
    ///     Handles Browse button clicks through the WinUI folder picker.
    /// </summary>
    private async void BrowseDirectory_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _userWorkflow.BrowseGameDirectoryAsync();
        }
        catch (Exception ex)
        {
            ViewModel.AddErrorMessage($"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>
    ///     Handles database picker button clicks.
    /// </summary>
    private async void OnSelectDatabase_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _userWorkflow.SelectDatabaseAsync();
        }
        catch (Exception ex)
        {
            ViewModel.AddErrorMessage($"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>
    ///     Handles FormID list picker button clicks.
    /// </summary>
    private async void OnSelectFormIdList_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _userWorkflow.SelectFormIdListAsync();
        }
        catch (Exception ex)
        {
            ViewModel.AddErrorMessage($"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>
    ///     Selects every currently loaded plugin.
    /// </summary>
    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        _userWorkflow.SelectAllPlugins();
    }

    /// <summary>
    ///     Clears selection for every currently loaded plugin.
    /// </summary>
    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        _userWorkflow.SelectNoPlugins();
    }

    /// <summary>
    ///     Sends checkbox intent only for user activation, using the membership identity projected with the item.
    /// </summary>
    private void PluginCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: PluginListItem plugin } checkBox)
        {
            return;
        }

        _userWorkflow.SetPluginSelection(
            plugin.MembershipVersion,
            plugin.Name,
            checkBox.IsChecked == true);
    }

    /// <summary>
    ///     Handles process button clicks by starting processing or cancelling the active run.
    /// </summary>
    private async void ProcessFormIds_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _userWorkflow.ProcessFormIdsAsync();
        }
        catch (Exception ex)
        {
            ViewModel.AddErrorMessage($"Unexpected error: {ex.Message}");
        }
    }
}
