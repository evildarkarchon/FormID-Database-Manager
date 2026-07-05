using System.Diagnostics.CodeAnalysis;
using System.ComponentModel;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.ViewModels;
using FormID_Database_Manager.WinUI.Services;

namespace FormID_Database_Manager.WinUI;

public sealed partial class MainWindow : Window, IDisposable
{
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
        var pluginListManager = new PluginListManager(gameDetectionService, ViewModel, dispatcher);
        var processingRun = new ProcessingRun(new DatabaseService());

        InitializeWindow();
        _userWorkflow = new UserWorkflow(
            ViewModel,
            new WinUiFileDialogService(AppWindow),
            gameDetectionService,
            gameLocationService,
            pluginListManager,
            processingRun);
    }

    /// <summary>
    /// Initializes the WinUI main window with supplied services for migration smoke tests.
    /// </summary>
    /// <param name="viewModel">The UI-neutral state object shared with the migration core.</param>
    /// <param name="fileDialogService">The picker service used by browse and file-selection handlers.</param>
    /// <param name="gameDetectionService">The service used to detect a game from a browsed directory.</param>
    /// <param name="gameLocationService">The service used to find installed game folders.</param>
    /// <param name="pluginListManager">The service used to load and select plugins.</param>
    /// <param name="processingRun">The owned Processing Run module canceled during window close.</param>
    internal MainWindow(
        MainWindowViewModel viewModel,
        IFileDialogService? fileDialogService,
        GameDetectionService? gameDetectionService,
        IGameLocationService? gameLocationService,
        PluginListManager? pluginListManager,
        ProcessingRun? processingRun)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        var dispatcher = new WinUiThreadDispatcher(DispatcherQueue);
        var effectiveGameDetectionService = gameDetectionService ?? new GameDetectionService();
        var effectiveGameLocationService = gameLocationService ?? new GameLocationService();
        var effectivePluginListManager = pluginListManager ??
                                          new PluginListManager(effectiveGameDetectionService, ViewModel, dispatcher);
        var effectiveProcessingRun = processingRun ?? new ProcessingRun(new DatabaseService());

        InitializeWindow();
        _userWorkflow = new UserWorkflow(
            ViewModel,
            fileDialogService ?? new WinUiFileDialogService(AppWindow),
            effectiveGameDetectionService,
            effectiveGameLocationService,
            effectivePluginListManager,
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
            await _userWorkflow.RefreshPluginsForCurrentSelectionAsync();
        }
        catch (Exception ex)
        {
            ViewModel.AddErrorMessage($"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles game selection changes by loading installed directories and refreshing plugins.
    /// </summary>
    private async void GameComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            await _userWorkflow.SelectGameAsync();
        }
        catch (Exception ex)
        {
            ViewModel.AddErrorMessage($"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles detected-directory changes by reloading plugins for the current selection.
    /// </summary>
    private async void DirectoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            await _userWorkflow.SelectDetectedDirectoryAsync();
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
