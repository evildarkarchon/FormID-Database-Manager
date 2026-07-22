using System.Diagnostics.CodeAnalysis;
using System.ComponentModel;
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
        var gameDetectionService = new GameDetectionService();
        var gameLocationService = new GameLocationService();
        var pluginListDiscovery = new PluginListDiscovery();
        var pluginList = new PluginList(gameDetectionService, pluginListDiscovery);
        var processingRunExecutor = new ProcessingRunExecutor();

        InitializeWindow();
        _pluginListPresentationAdapter = new PluginListPresentationAdapter(pluginList, ViewModel, dispatcher);
        _userWorkflow = new UserWorkflow(
            ViewModel,
            new WinUiFileDialogService(AppWindow),
            gameDetectionService,
            gameLocationService,
            pluginList,
            processingRunExecutor);
    }

    /// <summary>
    /// Initializes the WinUI main window with supplied services for migration smoke tests.
    /// </summary>
    /// <param name="viewModel">The UI-neutral state object shared with the migration core.</param>
    /// <param name="fileDialogService">The picker service used by browse and file-selection handlers.</param>
    /// <param name="gameDetectionService">The service used to detect a game from a browsed directory.</param>
    /// <param name="gameLocationService">The service used to find installed game folders.</param>
    /// <param name="pluginListDiscovery">The deterministic or production adapter used to discover Plugins.</param>
    /// <param name="processingRunExecutor">The owned Processing Run executor canceled during window close.</param>
    internal MainWindow(
        MainWindowViewModel viewModel,
        IFileDialogService? fileDialogService,
        GameDetectionService? gameDetectionService,
        IGameLocationService? gameLocationService,
        IPluginListDiscovery? pluginListDiscovery,
        ProcessingRunExecutor? processingRunExecutor)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        var dispatcher = new WinUiThreadDispatcher(DispatcherQueue);
        var effectiveGameDetectionService = gameDetectionService ?? new GameDetectionService();
        var effectiveGameLocationService = gameLocationService ?? new GameLocationService();
        var effectivePluginListDiscovery = pluginListDiscovery ?? new PluginListDiscovery();
        var pluginList = new PluginList(effectiveGameDetectionService, effectivePluginListDiscovery);
        var effectiveProcessingRun = processingRunExecutor ?? new ProcessingRunExecutor();

        InitializeWindow();
        _pluginListPresentationAdapter = new PluginListPresentationAdapter(pluginList, ViewModel, dispatcher);
        _userWorkflow = new UserWorkflow(
            ViewModel,
            fileDialogService ?? new WinUiFileDialogService(AppWindow),
            effectiveGameDetectionService,
            effectiveGameLocationService,
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
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        Closed += MainWindow_Closed;
    }

    public MainWindowViewModel ViewModel { get; }

    /// <summary>
    /// Cancels in-flight processing and releases services owned by this window.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Closed -= MainWindow_Closed;
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        // Detach presentation before the workflow retires its authoritative Plugin List.
        _pluginListPresentationAdapter.Dispose();
        _userWorkflow.Dispose();
        ViewModel.Dispose();
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        Dispose();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainWindowViewModel.AdvancedMode))
        {
            _ = RefreshPluginsForAdvancedModeAsync();
        }
    }

    private async Task RefreshPluginsForAdvancedModeAsync()
    {
        try
        {
            await _userWorkflow.ApplyGameContextTransitionAsync(GameContextTransition.AdvancedModeChanged());
        }
        catch (Exception ex)
        {
            ViewModel.AddErrorMessage($"Unexpected error: {ex.Message}");
        }
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
    /// Forwards the explicit detected-directory selection to the authoritative User Workflow.
    /// </summary>
    /// <param name="sender">The ComboBox whose selected directory value is forwarded.</param>
    /// <param name="e">The selection-change event details.</param>
    private async void DirectoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            var selectedDirectory = (sender as ComboBox)?.SelectedItem as string;
            await _userWorkflow.SelectDetectedDirectoryAsync(selectedDirectory);
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
    [RequiresUnreferencedCode("Uses reflection-based name extraction for Mutagen records.")]
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
