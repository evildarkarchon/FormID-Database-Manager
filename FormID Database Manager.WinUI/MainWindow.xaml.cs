using System.Runtime.ExceptionServices;
using FormID_Database_Manager.Models;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.ViewModels;
using FormID_Database_Manager.WinUI.Services;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.WinUI;

public sealed partial class MainWindow : Window, IDisposable
{
    private readonly PluginListPresentationAdapter _pluginListPresentationAdapter;
    private readonly UserWorkflow _userWorkflow;
    private bool _disposed;

    /// <summary>
    /// Initializes the WinUI main window with production platform services.
    /// </summary>
    public MainWindow()
    {
        var dispatcher = new WinUiThreadDispatcher(DispatcherQueue);
        ViewModel = new MainWindowViewModel(dispatcher);
        var gameInstallations = new GameInstallations(new GameInstallationProbe());
        var gameLoadOrders = new GameLoadOrders();
        var pluginList = new PluginList(gameLoadOrders);
        var processingRunExecutor = new ProcessingRunExecutor();

        InitializeWindow();
        _pluginListPresentationAdapter = new PluginListPresentationAdapter(pluginList, ViewModel, dispatcher);
        _userWorkflow = new UserWorkflow(
            ViewModel,
            new WinUiFileDialogService(AppWindow),
            gameInstallations,
            pluginList,
            processingRunExecutor);
    }

    /// <summary>
    /// Initializes the WinUI main window with supplied services for migration smoke tests.
    /// </summary>
    /// <param name="viewModel">The UI-neutral state object shared with the migration core.</param>
    /// <param name="fileDialogService">The picker service used by browse and file-selection handlers.</param>
    /// <param name="gameInstallations">
    /// The module used to detect a game from a browsed directory and to locate its installs.
    /// </param>
    /// <param name="gameLoadOrders">The deterministic or production Game Load Orders module used by Plugin List.</param>
    /// <param name="processingRunExecutor">The owned Processing Run executor canceled during window close.</param>
    internal MainWindow(
        MainWindowViewModel viewModel,
        IFileDialogService? fileDialogService,
        GameInstallations? gameInstallations,
        IGameLoadOrders? gameLoadOrders,
        ProcessingRunExecutor? processingRunExecutor)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        var dispatcher = new WinUiThreadDispatcher(DispatcherQueue);
        var effectiveGameInstallations = gameInstallations ?? new GameInstallations(new GameInstallationProbe());
        var effectiveGameLoadOrders = gameLoadOrders ?? new GameLoadOrders();
        var pluginList = new PluginList(effectiveGameLoadOrders);
        var effectiveProcessingRun = processingRunExecutor ?? new ProcessingRunExecutor();

        InitializeWindow();
        _pluginListPresentationAdapter = new PluginListPresentationAdapter(pluginList, ViewModel, dispatcher);
        _userWorkflow = new UserWorkflow(
            ViewModel,
            fileDialogService ?? new WinUiFileDialogService(AppWindow),
            effectiveGameInstallations,
            pluginList,
            effectiveProcessingRun);
    }

    /// <summary>
    /// Initializes XAML, assigns the root ViewModel, and attaches close-time cleanup.
    /// </summary>
    private void InitializeWindow()
    {
        InitializeComponent();
        Root.DataContext = ViewModel;
        Closed += MainWindow_Closed;
    }

    public MainWindowViewModel ViewModel { get; }

    /// <summary>
    /// Cancels in-flight processing and releases services owned by this window.
    /// </summary>
    /// <remarks>
    /// Workflow disposal can surface a cancellation callback failure from the run being cancelled, so every
    /// window-owned service is retired regardless and the first failure keeps its identity.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Closed -= MainWindow_Closed;

        Exception? primaryException = null;
        // Detach presentation before the workflow retires its authoritative Plugin List.
        RetireService(_pluginListPresentationAdapter.Dispose, ref primaryException);
        RetireService(_userWorkflow.Dispose, ref primaryException);
        if (primaryException is not null)
        {
            // Rethrow through the dispatch info so the caller still sees the original throw site.
            ExceptionDispatchInfo.Throw(primaryException);
        }
    }

    /// <summary>
    /// Runs one window-owned disposal step, keeping the first failure as the exception the caller observes.
    /// </summary>
    /// <param name="step">The cleanup action to run.</param>
    /// <param name="primaryException">
    /// The failure already in flight, replaced only when <paramref name="step" /> raises the first one.
    /// </param>
    private static void RetireService(Action step, ref Exception? primaryException)
    {
        try
        {
            step();
        }
        catch (Exception ex) when (primaryException is null)
        {
            // The first failure becomes the primary exception, but later steps still run so nothing is left live.
            primaryException = ex;
        }
        catch
        {
            // A later cleanup failure cannot replace the primary exception's identity.
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        Dispose();
    }

    /// <summary>
    /// Forwards the explicit GameRelease selection to the authoritative User Workflow.
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
    /// Forwards detected-directory control changes to the authoritative User Workflow boundary.
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
    /// Forwards the explicit Advanced Mode value to the authoritative User Workflow.
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
    /// Handles Browse button clicks through the WinUI folder picker.
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
    /// Handles database picker button clicks.
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
    /// Handles FormID list picker button clicks.
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
    /// Selects every currently loaded plugin.
    /// </summary>
    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        _userWorkflow.SelectAllPlugins();
    }

    /// <summary>
    /// Clears selection for every currently loaded plugin.
    /// </summary>
    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        _userWorkflow.SelectNoPlugins();
    }

    /// <summary>
    /// Sends checkbox intent only for user activation, using the membership identity projected with the item.
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
    /// Handles process button clicks by starting processing or cancelling the active run.
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
