using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SuperTerminal.Core.Abstractions.Terminal;
using SuperTerminal.Core.Models;

namespace SuperTerminal.UI.ViewModels;

public sealed partial class TerminalTabViewModel : ViewModelBase
{
    private readonly Func<TerminalTabViewModel, Task> closeAction;
    private readonly IPtySessionFactory ptySessionFactory;
    private readonly SemaphoreSlim startGate = new(1, 1);
    private Action? killProcess;
    private IPtySession? ptySession;

    public TerminalTabViewModel(
        SessionListItemViewModel session,
        TerminalSessionDefinition definition,
        IPtySessionFactory ptySessionFactory,
        string fontFamily,
        double fontSize,
        string cursorStyle,
        bool cursorBlink,
        Func<TerminalTabViewModel, Task> closeAction)
        : this(session.Id, session.Name, session.Endpoint, session.Folder, definition, ptySessionFactory, fontFamily, fontSize, cursorStyle, cursorBlink, closeAction)
    {
    }

    public TerminalTabViewModel(
        string title,
        TerminalSessionDefinition definition,
        IPtySessionFactory ptySessionFactory,
        string fontFamily,
        double fontSize,
        string cursorStyle,
        bool cursorBlink,
        Func<TerminalTabViewModel, Task> closeAction)
        : this(null, title, "Local terminal", string.Empty, definition, ptySessionFactory, fontFamily, fontSize, cursorStyle, cursorBlink, closeAction)
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
        string cursorStyle,
        bool cursorBlink,
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
        CursorStyle = cursorStyle;
        CursorBlink = cursorBlink;
    }

    public Guid Id { get; }

    public Guid? SessionId { get; }

    public bool IsLocal => SessionId is null;

    public TerminalSessionDefinition Definition { get; }

    public string FontFamily { get; private set; }

    public double FontSize { get; private set; }

    public string CursorStyle { get; private set; }

    public bool CursorBlink { get; private set; }

    public event EventHandler<string>? TerminalOutputReceived;

    public event EventHandler? FocusRequested;

    public event EventHandler? AppearanceChanged;

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
    private string connectionStatus = "Preparing PowerShell";

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
        killProcess?.Invoke();
        killProcess = null;
        if (ptySession is not null)
        {
            await ptySession.DisposeAsync();
            ptySession = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        killProcess?.Invoke();
        killProcess = null;
        if (ptySession is not null)
        {
            await ptySession.DisposeAsync();
            ptySession = null;
        }
        startGate.Dispose();
    }

    public async Task StartPtyAsync(int columns, int rows)
    {
        await startGate.WaitAsync();
        try
        {
            if (ptySession is not null)
            {
                ptySession.Resize(columns, rows);
                return;
            }

            ConnectionStatus = "Starting ConPTY";
            ptySession = await ptySessionFactory.CreateAsync(Definition, columns, rows);
            ptySession.OutputReceived += OnPtyOutputReceived;
            ptySession.Exited += OnPtyExited;
            ConnectionStatus = "PowerShell via xterm.js/WebGL";
        }
        catch (Exception exception)
        {
            ReportLaunchFailed(exception.Message);
        }
        finally
        {
            startGate.Release();
        }
    }

    public Task WritePtyAsync(string data) =>
        ptySession?.WriteAsync(data) ?? Task.CompletedTask;

    public void ResizePty(int columns, int rows) =>
        ptySession?.Resize(columns, rows);

    public void RequestFocus() =>
        FocusRequested?.Invoke(this, EventArgs.Empty);

    public void UpdateAppearance(
        string fontFamily,
        double fontSize,
        string cursorStyle,
        bool cursorBlink)
    {
        FontFamily = fontFamily;
        FontSize = Math.Clamp(fontSize, 8, 32);
        CursorStyle = cursorStyle;
        CursorBlink = cursorBlink;
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPtyOutputReceived(object? sender, string output) =>
        TerminalOutputReceived?.Invoke(this, output);

    private void OnPtyExited(object? sender, int exitCode) =>
        ReportProcessExited(exitCode);

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
        ConnectionStatus = "PowerShell via ConPTY";
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

    internal void ReportProcessExited(int exitCode)
    {
        killProcess = null;
        ConnectionStatus = $"PowerShell exited — code {exitCode}";
    }

    internal void ReportLaunchFailed(string message)
    {
        killProcess = null;
        ConnectionStatus = $"Launch failed: {message}";
    }
}
