using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HyperTerm.Core.Models;
using HyperTerm.UI.Controls;
using HyperTerm.UI.Services;
using HyperTerm.UI.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HyperTerm.UI.Views;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "The terminal presentation is disposed by the window Closed event before removing the native host.")]
public sealed partial class MainWindow : Window
{
    private WebTerminalHostControl? ActiveTerminalHost =>
        TerminalHost.IsVisible && !TerminalHost.IsInteractionBlocked ? TerminalHost : null;
    private TerminalOverlayPresentation? terminalPresentation;
    private static readonly DataFormat<string> SessionDragFormat =
        DataFormat.CreateInProcessFormat<string>("HyperTerm.SessionId");
    private static readonly DataFormat<string> FolderDragFormat =
        DataFormat.CreateInProcessFormat<string>("HyperTerm.FolderPath");
    private static readonly DataFormat<string> TabDragFormat =
        DataFormat.CreateInProcessFormat<string>("HyperTerm.TabId");

    private PointerPressedEventArgs? sessionDragStartEvent;
    private Point sessionDragStartPoint;
    private SessionTreeNodeViewModel? draggedTreeNode;
    private TreeViewItem? currentDropTarget;
    private PointerPressedEventArgs? tabDragStartEvent;
    private Point tabDragStartPoint;
    private TerminalTabViewModel? draggedTab;
    private ListBoxItem? currentTabDropTarget;
    private bool currentTabDropAfter;
    private double tabAutoScrollDirection;
    private int activationFocusGeneration;
    private readonly DispatcherTimer tabAutoScrollTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(30),
    };
    private readonly double sidebarMinimumWidth;
    private readonly double sidebarMaximumWidth;
    private readonly GridLength sidebarSplitterWidth;
    private double sidebarWidth;

    private ColumnDefinition SidebarColumn => WorkspaceGrid.ColumnDefinitions[0];

    private ColumnDefinition SidebarSplitterColumn => WorkspaceGrid.ColumnDefinitions[1];

    public MainWindow()
    {
        InitializeComponent();
        UpdateMaximizeRestoreIcon();
        TitleBarRoot.AddHandler(
            InputElement.PointerPressedEvent,
            OnTitleBarPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        TerminalTabs.AddHandler(
            InputElement.PointerPressedEvent,
            OnTerminalTabPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        TerminalTabs.AddHandler(
            InputElement.PointerMovedEvent,
            OnTerminalTabPointerMoved,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        TerminalTabs.AddHandler(
            InputElement.PointerReleasedEvent,
            OnTerminalTabPointerReleased,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        TerminalTabs.LayoutUpdated += OnTerminalTabsLayoutUpdated;
        TabScrollViewer.AddHandler(
            InputElement.PointerWheelChangedEvent,
            OnTerminalTabPointerWheelChanged,
            RoutingStrategies.Tunnel);
        DragDrop.SetAllowDrop(TerminalTabs, true);
        DragDrop.AddDragOverHandler(TerminalTabs, OnTerminalTabDragOver);
        DragDrop.AddDragLeaveHandler(TerminalTabs, OnTerminalTabDragLeave);
        DragDrop.AddDropHandler(TerminalTabs, OnTerminalTabDrop);
        tabAutoScrollTimer.Tick += OnTabAutoScrollTick;
        SessionsTree.AddHandler(
            InputElement.PointerPressedEvent,
            OnSessionTreePointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        SessionsTree.AddHandler(
            InputElement.PointerMovedEvent,
            OnSessionTreePointerMoved,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        SessionsTree.AddHandler(
            InputElement.PointerReleasedEvent,
            OnSessionTreePointerReleased,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        DragDrop.SetAllowDrop(SessionsTree, true);
        DragDrop.AddDragOverHandler(SessionsTree, OnSessionTreeDragOver);
        DragDrop.AddDragLeaveHandler(SessionsTree, OnSessionTreeDragLeave);
        DragDrop.AddDropHandler(SessionsTree, OnSessionTreeDrop);
        sidebarMinimumWidth = SidebarColumn.MinWidth;
        sidebarMaximumWidth = SidebarColumn.MaxWidth;
        sidebarSplitterWidth = SidebarSplitterColumn.Width;
        sidebarWidth = SidebarColumn.Width.Value;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty)
        {
            UpdateMaximizeRestoreIcon();
        }
    }

    private void OnMinimizeButtonClick(object? sender, RoutedEventArgs eventArgs) =>
        WindowState = WindowState.Minimized;

    private void OnMaximizeRestoreButtonClick(object? sender, RoutedEventArgs eventArgs) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void OnCloseButtonClick(object? sender, RoutedEventArgs eventArgs) => Close();

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        PointerPoint point = eventArgs.GetCurrentPoint(TitleBarRoot);
        if (!point.Properties.IsLeftButtonPressed ||
            eventArgs.Source is not Visual source ||
            source.FindAncestorOfType<Button>() is not null ||
            source.FindAncestorOfType<ListBoxItem>() is not null ||
            source.FindAncestorOfType<TextBox>() is not null ||
            source.FindAncestorOfType<ScrollBar>() is not null)
        {
            return;
        }

        if (eventArgs.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            eventArgs.Handled = true;
            return;
        }

        BeginMoveDrag(eventArgs);
        eventArgs.Handled = true;
    }

    private void OnResizeGripPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (WindowState != WindowState.Normal ||
            sender is not Control { Tag: string edgeName } ||
            !Enum.TryParse(edgeName, out WindowEdge edge) ||
            !eventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        BeginResizeDrag(edge, eventArgs);
        eventArgs.Handled = true;
    }

    private void UpdateMaximizeRestoreIcon()
    {
        if (MaximizeRestoreIcon is null)
        {
            return;
        }

        bool isMaximized = WindowState == WindowState.Maximized;
        ResizeGripOverlay.IsVisible = !isMaximized;
        MaximizeRestoreIcon.Text = isMaximized ? "\uE923" : "\uE922";
        ToolTip.SetTip(
            MaximizeRestoreButton,
            isMaximized ? "Restore" : "Maximize");
    }

    public MainWindow(MainWindowViewModel viewModel)
        : this(viewModel, new WindowsWebViewPreviewService(), NullLogger<MainWindow>.Instance)
    {
    }

    internal MainWindow(
        MainWindowViewModel viewModel,
        IWebViewPreviewService previewService,
        ILogger<MainWindow> logger)
        : this()
    {
        DataContext = viewModel;
        terminalPresentation = new TerminalOverlayPresentation(
            cancellationToken => TerminalHost.CanCapturePreview
                ? previewService.CaptureAsync(TerminalHost, cancellationToken)
                : Task.FromResult<Avalonia.Media.Imaging.Bitmap?>(null),
            (visible, preview) =>
            {
                TerminalPreview.Source = preview;
                TerminalHost.IsVisible = visible;
                if (!visible && viewModel.IsCommandPaletteOpen)
                {
                    CommandPaletteDialogHost.FocusQueryAfterNativeFocusRelease();
                }
            },
            logger);
        viewModel.PropertyChanged += OnTerminalPresentationChanged;
        viewModel.Workspace.PropertyChanged += OnTerminalPresentationChanged;
        OnTerminalPresentationChanged(this, new(nameof(MainWindowViewModel.IsOverlayOpen)));
        viewModel.CloseWindowRequested += (_, _) => Close();
        viewModel.TerminalSearchRequested += OnTerminalSearchRequested;
        viewModel.SessionEditor.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(SessionEditorViewModel.IsEditorOpen) &&
                viewModel.SessionEditor.IsEditorOpen)
            {
                FocusEditor(SessionEditorDialogHost.NameEditor);
            }
        };
        viewModel.FolderEditor.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(FolderEditorViewModel.IsFolderEditorOpen) &&
                viewModel.FolderEditor.IsFolderEditorOpen)
            {
                FocusEditor(FolderEditorDialogHost.PathEditor);
            }
        };
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(MainWindowViewModel.IsSidebarVisible))
            {
                UpdateSidebarVisibility(viewModel.IsSidebarVisible);
            }
            else if (eventArgs.PropertyName == nameof(MainWindowViewModel.IsCommandPaletteOpen) &&
                     viewModel.IsCommandPaletteOpen)
            {
                ActiveTerminalHost?.CancelWindowActivationFocus();
                WindowsWebViewFocus.TryReleaseFocus(this);
                CommandPaletteDialogHost.FocusQueryAfterNativeFocusRelease();
            }
        };
        Activated += (_, _) =>
        {
            int generation = ++activationFocusGeneration;
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (generation == activationFocusGeneration &&
                        IsActive &&
                        viewModel.AreTerminalHostsVisible)
                    {
                        ActiveTerminalHost?.FocusAfterWindowActivation();
                    }
                },
                DispatcherPriority.Background);
        };
        Deactivated += (_, _) =>
        {
            activationFocusGeneration++;
            ActiveTerminalHost?.CancelWindowActivationFocus();
        };
        viewModel.InitializationCompleted += (_, _) =>
        {
            RestoreWindowState(viewModel.WindowSettings);
        };
        Closing += (_, _) =>
        {
            viewModel.CaptureWindowState(
                Bounds.Width,
                Bounds.Height,
                Position.X,
                Position.Y);
        };
        Closed += (_, _) =>
        {
            viewModel.PropertyChanged -= OnTerminalPresentationChanged;
            viewModel.Workspace.PropertyChanged -= OnTerminalPresentationChanged;
            viewModel.TerminalSearchRequested -= OnTerminalSearchRequested;
            terminalPresentation.Dispose();
            RemoveTerminalHost(TerminalHost);
        };
    }

    private async void OnTerminalSearchRequested(object? sender, EventArgs eventArgs)
    {
        // Search can be requested by a palette command before the deferred close renders.
        await UpdateTerminalPresentationAsync();
        if (ActiveTerminalHost is { } terminalHost)
        {
            await terminalHost.OpenSearchAsync();
        }
    }

    private async void OnTerminalPresentationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(MainWindowViewModel.IsOverlayOpen) or
            nameof(TerminalWorkspaceViewModel.SelectedTab))
        {
            if (DataContext is MainWindowViewModel { IsOverlayOpen: false })
            {
                // Palette commands can close one overlay and open another in the same turn.
                await Dispatcher.UIThread.InvokeAsync(
                    UpdateTerminalPresentationAsync, DispatcherPriority.Background);
            }
            else
            {
                await UpdateTerminalPresentationAsync();
            }
        }
    }

    private async Task UpdateTerminalPresentationAsync()
    {
        if (DataContext is MainWindowViewModel viewModel && terminalPresentation is not null)
        {
            TerminalHost.IsInteractionBlocked = viewModel.IsOverlayOpen;
            await terminalPresentation.UpdateAsync(viewModel.Workspace.SelectedTab?.Id, viewModel.IsOverlayOpen);
        }
    }

    internal static void RemoveTerminalHost(WebTerminalHostControl terminalHost)
    {
        terminalHost.PrepareForRemoval();
        if (terminalHost.Parent is Panel terminalParent)
        {
            terminalParent.Children.Remove(terminalHost);
        }
    }

    private void RestoreWindowState(WindowSettings settings)
    {
        Width = Math.Max(MinWidth, settings.Width);
        Height = Math.Max(MinHeight, settings.Height);

        if (settings.X is not int x || settings.Y is not int y)
        {
            return;
        }

        var savedPosition = new PixelPoint(x, y);
        if (Screens.All.Any(screen => screen.WorkingArea.Contains(savedPosition)))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = savedPosition;
        }
    }

    private static void FocusEditor(TextBox editor)
    {
        Dispatcher.UIThread.Post(
            () =>
            {
                editor.Focus();
                editor.SelectAll();
            },
            DispatcherPriority.Loaded);
    }

    private void UpdateSidebarVisibility(bool isVisible)
    {
        if (!isVisible)
        {
            if (SidebarColumn.ActualWidth > 0)
            {
                sidebarWidth = SidebarColumn.ActualWidth;
            }

            SidebarColumn.MinWidth = 0;
            SidebarColumn.MaxWidth = 0;
            SidebarColumn.Width = new GridLength(0);
            SidebarSplitterColumn.Width = new GridLength(0);
            return;
        }

        SidebarColumn.MinWidth = sidebarMinimumWidth;
        SidebarColumn.MaxWidth = sidebarMaximumWidth;
        SidebarColumn.Width = new GridLength(sidebarWidth);
        SidebarSplitterColumn.Width = sidebarSplitterWidth;
    }
}
