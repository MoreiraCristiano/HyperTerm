using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;
using System.Collections.ObjectModel;
using HyperTerm.Core.Abstractions.Terminal;
using HyperTerm.Core.Models;

namespace HyperTerm.UI.ViewModels;

public sealed partial class TerminalTabViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly Func<TerminalTabViewModel, Task> closeAction;
    private readonly IPtySessionFactory ptySessionFactory;
    private readonly PaneTree paneTree;
    private Action? killProcess;
    private int terminationSignaled;
    private int disposed;

    public TerminalTabViewModel(
        SessionListItemViewModel session,
        TerminalSessionDefinition definition,
        IPtySessionFactory ptySessionFactory,
        string fontFamily,
        double fontSize,
        string selectionColor,
        string cursorStyle,
        bool cursorBlink,
        string theme,
        Func<TerminalTabViewModel, Task> closeAction)
        : this(session.Id, session.Name, session.Endpoint, session.Folder, definition, ptySessionFactory, fontFamily, fontSize, selectionColor, cursorStyle, cursorBlink, theme, closeAction)
    {
    }

    public TerminalTabViewModel(
        string title,
        TerminalSessionDefinition definition,
        IPtySessionFactory ptySessionFactory,
        string fontFamily,
        double fontSize,
        string selectionColor,
        string cursorStyle,
        bool cursorBlink,
        string theme,
        Func<TerminalTabViewModel, Task> closeAction)
        : this(
            null,
            title,
            definition.Kind == TerminalSessionKind.Psmux
                ? "psmux · persistent"
                : "Local terminal",
            string.Empty,
            definition,
            ptySessionFactory,
            fontFamily,
            fontSize,
            selectionColor,
            cursorStyle,
            cursorBlink,
            theme,
            closeAction)
    {
    }

    private TerminalTabViewModel(
        Guid? sessionId,
        string title,
        string endpoint,
        string folder,
        TerminalSessionDefinition definition,
        IPtySessionFactory ptySessionFactory,
        string fontFamily,
        double fontSize,
        string selectionColor,
        string cursorStyle,
        bool cursorBlink,
        string theme,
        Func<TerminalTabViewModel, Task> closeAction)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(closeAction);
        ArgumentNullException.ThrowIfNull(ptySessionFactory);

        Id = Guid.NewGuid();
        SessionId = sessionId;
        Definition = definition;
        this.closeAction = closeAction;
        this.ptySessionFactory = ptySessionFactory;
        Title = title;
        EditableTitle = title;
        Endpoint = endpoint;
        Folder = folder;
        FontFamily = string.IsNullOrWhiteSpace(fontFamily) ? "Cascadia Mono" : fontFamily;
        FontSize = Math.Clamp(fontSize, 8, 32);
        SelectionColor = selectionColor;
        CursorStyle = cursorStyle;
        CursorBlink = cursorBlink;
        Theme = theme;
        Guid firstPaneId = Guid.NewGuid();
        paneTree = new PaneTree(firstPaneId);
        AddPane(new TerminalPaneViewModel(firstPaneId, definition, ptySessionFactory));
    }

    public Guid Id { get; }

    public Guid? SessionId { get; }

    public bool IsLocal => Definition.Kind != TerminalSessionKind.Ssh;

    public bool IsPsmux => Definition.Kind == TerminalSessionKind.Psmux;

    public string? PsmuxSessionName => Definition.PsmuxSessionName;

    public TerminalSessionDefinition Definition { get; }

    public ObservableCollection<TerminalPaneViewModel> Panes { get; } = [];

    public PaneNode? PaneRoot => paneTree.Root;

    public Guid? ActivePaneId => paneTree.ActivePaneId;

    public TerminalPaneViewModel? ActivePane =>
        ActivePaneId is Guid paneId
            ? Panes.FirstOrDefault(pane => pane.PaneId == paneId)
            : null;

    public string FontFamily { get; private set; }

    public double FontSize { get; private set; }

    public string SelectionColor { get; private set; }

    public string CursorStyle { get; private set; }

    public bool CursorBlink { get; private set; }

    public string Theme { get; private set; }

    public event EventHandler<string>? TerminalOutputReceived;

    public event EventHandler<TerminalPaneOutputEventArgs>? PaneOutputReceived;

    public event EventHandler? PaneLayoutChanged;

    public event EventHandler? FocusRequested;

    public event EventHandler? AppearanceChanged;

    public event EventHandler? Terminating;

    public event EventHandler? PtyStarted;

    public event EventHandler<string>? ApplicationCommandRequested;

    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private string editableTitle = string.Empty;

    [ObservableProperty]
    private bool isRenaming;

    [ObservableProperty]
    private string endpoint = string.Empty;

    [ObservableProperty]
    private string folder = string.Empty;

    [ObservableProperty]
    private string connectionStatus = "Preparing terminal";

    [ObservableProperty]
    private bool isSelected;

    [RelayCommand]
    private Task CloseAsync() => closeAction(this);

    public void BeginRename()
    {
        EditableTitle = Title;
        IsRenaming = true;
    }

    public void CommitRename()
    {
        string newTitle = EditableTitle.Trim();
        if (newTitle.Length > 0)
        {
            Title = newTitle;
        }

        EditableTitle = Title;
        IsRenaming = false;
    }

    public void CancelRename()
    {
        EditableTitle = Title;
        IsRenaming = false;
    }

    public async Task TerminateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        SignalTerminating();
        killProcess?.Invoke();
        killProcess = null;

        foreach (TerminalPaneViewModel pane in Panes.ToArray())
        {
            RemovePaneSubscriptions(pane);
            await pane.DisposeAsync();
        }

        Panes.Clear();
    }

    public async Task StartPtyAsync(
        int columns,
        int rows,
        CancellationToken cancellationToken = default)
    {
        await StartPaneAsync(ActivePaneId, columns, rows, cancellationToken);
    }

    public async Task StartPaneAsync(
        Guid? paneId,
        int columns,
        int rows,
        CancellationToken cancellationToken = default)
    {
        TerminalPaneViewModel? pane = FindPane(paneId);
        if (pane is null || Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        try
        {
            ConnectionStatus = "Starting ConPTY";
            await pane.StartAsync(columns, rows, cancellationToken);
            string terminalName = pane.Definition.Kind switch
            {
                TerminalSessionKind.Psmux => "psmux",
                TerminalSessionKind.Ssh => "SSH",
                _ => pane.Definition.DisplayName ?? "Terminal",
            };
            ConnectionStatus = $"{terminalName} via xterm.js/WebGL";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ReportLaunchFailed(exception.Message);
        }
    }

    public Task WritePtyAsync(
        string data,
        CancellationToken cancellationToken = default) =>
        WritePaneAsync(ActivePaneId, data, cancellationToken);

    public Task WritePaneAsync(
        Guid? paneId,
        string data,
        CancellationToken cancellationToken = default) =>
        FindPane(paneId)?.WriteAsync(data, cancellationToken) ?? Task.CompletedTask;

    public void ResizePty(int columns, int rows) =>
        ResizePane(ActivePaneId, columns, rows);

    public void ResizePane(Guid? paneId, int columns, int rows) =>
        FindPane(paneId)?.Resize(columns, rows);

    public bool SetActivePane(Guid paneId)
    {
        if (!paneTree.SetActive(paneId))
        {
            return false;
        }

        OnPropertyChanged(nameof(ActivePaneId));
        OnPropertyChanged(nameof(ActivePane));
        PaneLayoutChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public TerminalPaneViewModel? SplitActivePane(
        SplitOrientation orientation,
        TerminalSessionDefinition definition)
    {
        if (ActivePaneId is not Guid activePaneId)
        {
            return null;
        }

        Guid paneId = Guid.NewGuid();
        if (!paneTree.Split(activePaneId, paneId, orientation))
        {
            return null;
        }

        var pane = new TerminalPaneViewModel(paneId, definition, ptySessionFactory);
        AddPane(pane);
        NotifyPaneLayoutChanged();
        return pane;
    }

    public async Task<bool> CloseActivePaneAsync()
    {
        if (ActivePaneId is not Guid paneId || FindPane(paneId) is not { } pane)
        {
            return false;
        }

        if (Panes.Count == 1)
        {
            await closeAction(this);
            return true;
        }

        paneTree.Remove(paneId, out _);
        RemovePaneSubscriptions(pane);
        Panes.Remove(pane);
        await pane.DisposeAsync();
        NotifyPaneLayoutChanged();
        return true;
    }

    public bool FocusNextPane() => FocusPane(paneTree.FocusNext());

    public bool FocusPreviousPane() => FocusPane(paneTree.FocusPrevious());

    public bool FocusLeftPane() => FocusPane(paneTree.FocusLeft());

    public bool FocusRightPane() => FocusPane(paneTree.FocusRight());

    public bool FocusUpPane() => FocusPane(paneTree.FocusUp());

    public bool FocusDownPane() => FocusPane(paneTree.FocusDown());

    public bool SetPaneRatio(Guid firstDescendantPaneId, double ratio)
    {
        if (!paneTree.SetRatio(firstDescendantPaneId, ratio))
        {
            return false;
        }

        NotifyPaneLayoutChanged();
        return true;
    }

    public void RequestFocus() =>
        FocusRequested?.Invoke(this, EventArgs.Empty);

    public void RequestApplicationCommand(string command) =>
        ApplicationCommandRequested?.Invoke(this, command);

    public void UpdateAppearance(
        string fontFamily,
        double fontSize,
        string selectionColor,
        string cursorStyle,
        bool cursorBlink,
        string theme)
    {
        FontFamily = fontFamily;
        FontSize = Math.Clamp(fontSize, 8, 32);
        SelectionColor = selectionColor;
        CursorStyle = cursorStyle;
        CursorBlink = cursorBlink;
        Theme = theme;
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPaneOutputReceived(object? sender, string output)
    {
        if (sender is not TerminalPaneViewModel pane)
        {
            return;
        }

        PaneOutputReceived?.Invoke(this, new TerminalPaneOutputEventArgs(pane.PaneId, output));
        if (pane.PaneId == ActivePaneId)
        {
            TerminalOutputReceived?.Invoke(this, output);
        }
    }

    private void SignalTerminating()
    {
        if (Interlocked.Exchange(ref terminationSignaled, 1) == 0)
        {
            Terminating?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnPaneExited(object? sender, int exitCode)
    {
        if (sender is TerminalPaneViewModel { Definition.Kind: TerminalSessionKind.Psmux })
        {
            TerminalOutputReceived?.Invoke(
                this,
                $"\r\n\u001b[31m[HyperTerm] psmux exited with code {exitCode}. " +
                "Check the status bar or application logs for details.\u001b[0m\r\n");
        }

        RunOnUiThread(
            () =>
            {
                if (Volatile.Read(ref disposed) == 0)
                {
                    ReportProcessExited(exitCode);
                }
            });
    }

    private void AddPane(TerminalPaneViewModel pane)
    {
        pane.OutputReceived += OnPaneOutputReceived;
        pane.Exited += OnPaneExited;
        pane.Started += OnPaneStarted;
        Panes.Add(pane);
    }

    private void RemovePaneSubscriptions(TerminalPaneViewModel pane)
    {
        pane.OutputReceived -= OnPaneOutputReceived;
        pane.Exited -= OnPaneExited;
        pane.Started -= OnPaneStarted;
    }

    private void OnPaneStarted(object? sender, EventArgs eventArgs) =>
        PtyStarted?.Invoke(this, EventArgs.Empty);

    private TerminalPaneViewModel? FindPane(Guid? paneId) =>
        paneId is Guid value
            ? Panes.FirstOrDefault(pane => pane.PaneId == value)
            : null;

    private bool FocusPane(bool changed)
    {
        if (changed)
        {
            NotifyPaneLayoutChanged();
            RequestFocus();
        }

        return changed;
    }

    private void NotifyPaneLayoutChanged()
    {
        OnPropertyChanged(nameof(PaneRoot));
        OnPropertyChanged(nameof(ActivePaneId));
        OnPropertyChanged(nameof(ActivePane));
        PaneLayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void RunOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    public void UpdateSession(SessionListItemViewModel session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (SessionId is null || session.Id != SessionId)
        {
            throw new ArgumentException("Session does not belong to this tab.", nameof(session));
        }

        Title = session.Name;
        Endpoint = session.Endpoint;
        Folder = session.Folder;
    }

    internal void RegisterProcess(Action killAction)
    {
        killProcess = killAction;
        string terminalName = Definition.Kind == TerminalSessionKind.Ssh
            ? "SSH"
            : Definition.DisplayName ?? "Terminal";
        ConnectionStatus = $"{terminalName} via ConPTY";
    }

    internal void ReportWaitingForTerminal()
    {
        ConnectionStatus = "Waiting for ConPTY";
    }

    internal void ReportTextCopied(int characterCount)
    {
        ConnectionStatus = $"Copied: {characterCount} characters";
    }

    internal void ReportCopyFailed(string reason)
    {
        ConnectionStatus = $"Copy failed: {reason}";
    }

    internal void ReportPasteFailed(string reason)
    {
        ConnectionStatus = $"Paste failed: {reason}";
    }

    internal void ReportProcessExited(int exitCode)
    {
        killProcess = null;
        ConnectionStatus = $"Terminal exited — code {exitCode}";
    }

    internal void ReportLaunchFailed(string message)
    {
        killProcess = null;
        ConnectionStatus = $"Launch failed: {message}";
    }
}
