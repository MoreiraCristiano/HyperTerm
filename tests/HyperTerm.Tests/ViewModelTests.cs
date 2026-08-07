using HyperTerm.Core.Models;
using HyperTerm.UI.ViewModels;
using Xunit;

namespace HyperTerm.Tests;

public sealed class ViewModelTests
{
    [Fact]
    public async Task ExplorerBuildsSortedTreeAndSupportsFolderMultiSelection()
    {
        var sessions = new FakeSessionService();
        var folders = new FakeFolderService();
        folders.Folders.Add(new(Guid.NewGuid(), "Zulu", DateTime.UtcNow));
        folders.Folders.Add(new(Guid.NewGuid(), "Alpha", DateTime.UtcNow));
        sessions.Sessions.Add(FakeSessionService.CreateSession(
            Guid.NewGuid(),
            new SessionDetails("Root", "host", 22, "user", null, string.Empty, null)));
        var explorer = new SessionExplorerViewModel(sessions, folders);

        await explorer.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["Alpha", "Zulu", "Root"], explorer.SessionTree.Select(node => node.Name));
        explorer.SelectSingleTreeNode(explorer.SessionTree[0]);
        explorer.ToggleFolderDeletionSelection(explorer.SessionTree[1]);
        Assert.True(explorer.HasMultipleFoldersSelected);
        Assert.True(explorer.SessionTree[0].IsSelectedForDeletion);
        Assert.True(explorer.SessionTree[1].IsSelectedForDeletion);
    }

    [Fact]
    public async Task ExplorerMarksOnlyFoldersWithoutRealItemsAsEmpty()
    {
        var sessions = new FakeSessionService();
        var folders = new FakeFolderService();
        folders.Folders.Add(new(Guid.NewGuid(), "Empty", DateTime.UtcNow));
        folders.Folders.Add(new(Guid.NewGuid(), "Parent/EmptyChild", DateTime.UtcNow));
        folders.Folders.Add(new(Guid.NewGuid(), "WithSession", DateTime.UtcNow));
        sessions.Sessions.Add(FakeSessionService.CreateSession(
            Guid.NewGuid(),
            new SessionDetails(
                "Server", "host", 22, "user", null, "WithSession", null)));
        var explorer = new SessionExplorerViewModel(sessions, folders);

        await explorer.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.True(explorer.SessionTree.Single(node => node.Name == "Empty").IsEmptyFolder);
        SessionTreeNodeViewModel parent = explorer.SessionTree.Single(
            node => node.Name == "Parent");
        Assert.False(parent.IsEmptyFolder);
        Assert.True(parent.Children.Single(node => node.Name == "EmptyChild").IsEmptyFolder);
        SessionTreeNodeViewModel withSession = explorer.SessionTree.Single(
            node => node.Name == "WithSession");
        Assert.False(withSession.IsEmptyFolder);

        explorer.SearchText = "Server";

        Assert.False(explorer.SessionTree.Single(
            node => node.Name == "WithSession").IsEmptyFolder);
    }

    [Fact]
    public async Task ExplorerSearchesSessionsAndRestoresExpandedFolders()
    {
        var sessions = new FakeSessionService();
        var folders = new FakeFolderService();
        folders.Folders.Add(new(Guid.NewGuid(), "Production/Apps", DateTime.UtcNow));
        folders.Folders.Add(new(Guid.NewGuid(), "Development", DateTime.UtcNow));
        folders.Folders.Add(new(Guid.NewGuid(), "Empty", DateTime.UtcNow));
        sessions.Sessions.Add(FakeSessionService.CreateSession(
            Guid.NewGuid(),
            new SessionDetails(
                "Web", "prod.example.test", 22, "user", null, "Production/Apps", null)));
        sessions.Sessions.Add(FakeSessionService.CreateSession(
            Guid.NewGuid(),
            new SessionDetails(
                "Database", "dev.example.test", 22, "user", null, "Development", null)));
        var explorer = new SessionExplorerViewModel(sessions, folders);

        await explorer.InitializeAsync(TestContext.Current.CancellationToken);
        SessionTreeNodeViewModel production = explorer.SessionTree.Single(
            node => node.Name == "Production");
        SessionTreeNodeViewModel development = explorer.SessionTree.Single(
            node => node.Name == "Development");
        production.IsExpanded = false;
        development.IsExpanded = true;

        int unfilteredRootCount = explorer.SessionTree.Count;
        explorer.SearchText = "no-match";
        Assert.Equal(unfilteredRootCount, explorer.SessionTree.Count);
        explorer.SearchText = "  PROD.EXAMPLE  ";
        await Task.Delay(300, TestContext.Current.CancellationToken);

        SessionTreeNodeViewModel resultFolder = Assert.Single(explorer.SessionTree);
        Assert.Equal("Production", resultFolder.Name);
        Assert.True(resultFolder.IsExpanded);
        SessionTreeNodeViewModel apps = Assert.Single(resultFolder.Children);
        Assert.True(apps.IsExpanded);
        Assert.Equal("Web", Assert.Single(apps.Children).Name);
        Assert.Equal("1 session", explorer.SessionCountText);

        explorer.SearchText = string.Empty;

        Assert.Equal(3, explorer.SessionTree.Count);
        Assert.False(explorer.SessionTree.Single(
            node => node.Name == "Production").IsExpanded);
        Assert.True(explorer.SessionTree.Single(
            node => node.Name == "Development").IsExpanded);
        Assert.Equal("2 sessions", explorer.SessionCountText);
    }

    [Fact]
    public async Task ExplorerPreservesExpandedFoldersWhenMovingSession()
    {
        var sessions = new FakeSessionService();
        var folders = new FakeFolderService();
        folders.Folders.Add(new(Guid.NewGuid(), "Source/Nested", DateTime.UtcNow));
        folders.Folders.Add(new(Guid.NewGuid(), "Destination", DateTime.UtcNow));
        Guid sessionId = Guid.NewGuid();
        sessions.Sessions.Add(FakeSessionService.CreateSession(
            sessionId,
            new SessionDetails(
                "Server", "host", 22, "user", null, "Source/Nested", null)));
        var explorer = new SessionExplorerViewModel(sessions, folders);

        await explorer.InitializeAsync(TestContext.Current.CancellationToken);
        SessionTreeNodeViewModel source = explorer.SessionTree.Single(
            node => node.Name == "Source");
        SessionTreeNodeViewModel nested = source.Children.Single(
            node => node.Name == "Nested");
        SessionTreeNodeViewModel destination = explorer.SessionTree.Single(
            node => node.Name == "Destination");
        source.IsExpanded = true;
        nested.IsExpanded = true;
        destination.IsExpanded = true;

        await explorer.MoveSessionAsync(sessionId, "Destination");

        Assert.True(explorer.SessionTree.Single(node => node.Name == "Source").IsExpanded);
        Assert.True(explorer.SessionTree.Single(node => node.Name == "Source")
            .Children.Single(node => node.Name == "Nested").IsExpanded);
        Assert.True(explorer.SessionTree.Single(node => node.Name == "Destination").IsExpanded);
    }

    [Fact]
    public async Task ExplorerMovesFolderTreeAndPreservesExpansion()
    {
        var sessions = new FakeSessionService();
        var folders = new FakeFolderService();
        folders.Folders.Add(new(Guid.NewGuid(), "Source/Nested", DateTime.UtcNow));
        folders.Folders.Add(new(Guid.NewGuid(), "Destination", DateTime.UtcNow));
        var explorer = new SessionExplorerViewModel(sessions, folders);

        await explorer.InitializeAsync(TestContext.Current.CancellationToken);
        SessionTreeNodeViewModel source = explorer.SessionTree.Single(
            node => node.Name == "Source");
        source.IsExpanded = true;
        source.Children.Single(node => node.Name == "Nested").IsExpanded = true;

        await explorer.MoveFolderAsync("Source", "Destination");

        SessionTreeNodeViewModel destination = explorer.SessionTree.Single(
            node => node.Name == "Destination");
        SessionTreeNodeViewModel movedSource = destination.Children.Single(
            node => node.Name == "Source");
        Assert.True(destination.IsExpanded);
        Assert.True(movedSource.IsExpanded);
        Assert.True(movedSource.Children.Single(node => node.Name == "Nested").IsExpanded);
    }

    [Fact]
    public async Task WorkspaceOpensNavigatesAndClosesLocalTabs()
    {
        var workspace = new TerminalWorkspaceViewModel(
            new FakeSessionService(),
            new FakeTerminalSessionFactory(),
            new FakePtySessionFactory());
        workspace.ApplySettings(new ApplicationSettings());

        await workspace.OpenLocalTerminalCommand.ExecuteAsync(null);
        await workspace.OpenLocalTerminalCommand.ExecuteAsync(null);
        TerminalTabViewModel first = workspace.Tabs[0];
        workspace.PreviousTabCommand.Execute(null);

        Assert.Same(first, workspace.SelectedTab);
        await workspace.CloseSelectedTabCommand.ExecuteAsync(null);
        Assert.Single(workspace.Tabs);
        Assert.True(workspace.HasOpenTabs);
    }

    [Fact]
    public async Task WorkspaceOpensSameSessionInMultipleTabs()
    {
        var sessions = new FakeSessionService();
        var session = FakeSessionService.CreateSession(
            Guid.NewGuid(),
            new SessionDetails("Server", "host", 22, "user", null, string.Empty, null));
        sessions.Sessions.Add(session);
        var workspace = new TerminalWorkspaceViewModel(
            sessions,
            new FakeTerminalSessionFactory(),
            new FakePtySessionFactory());
        var listItem = new SessionListItemViewModel(session);

        await workspace.OpenSessionAsync(listItem);
        await workspace.OpenSessionAsync(listItem);

        Assert.Equal(2, workspace.Tabs.Count);
        Assert.All(workspace.Tabs, tab => Assert.Equal(session.Id, tab.SessionId));
        Assert.NotEqual(workspace.Tabs[0].Id, workspace.Tabs[1].Id);
    }

    [Fact]
    public async Task WorkspaceReordersTabsWithoutChangingSelection()
    {
        var workspace = new TerminalWorkspaceViewModel(
            new FakeSessionService(),
            new FakeTerminalSessionFactory(),
            new FakePtySessionFactory());
        await workspace.OpenLocalTerminalCommand.ExecuteAsync(null);
        await workspace.OpenLocalTerminalCommand.ExecuteAsync(null);
        await workspace.OpenLocalTerminalCommand.ExecuteAsync(null);
        TerminalTabViewModel first = workspace.Tabs[0];
        TerminalTabViewModel second = workspace.Tabs[1];
        TerminalTabViewModel selected = workspace.SelectedTab!;

        workspace.MoveTab(first, selected, insertAfter: true);

        Assert.Equal([second, selected, first], workspace.Tabs);
        Assert.Same(selected, workspace.SelectedTab);

        workspace.MoveTab(first, second, insertAfter: false);

        Assert.Equal([first, second, selected], workspace.Tabs);
        Assert.Same(selected, workspace.SelectedTab);
    }

    [Fact]
    public async Task WorkspaceRestoresSelectionAndFocusAfterTabDrag()
    {
        var workspace = new TerminalWorkspaceViewModel(
            new FakeSessionService(),
            new FakeTerminalSessionFactory(),
            new FakePtySessionFactory());
        await workspace.OpenLocalTerminalCommand.ExecuteAsync(null);
        TerminalTabViewModel selected = workspace.SelectedTab!;
        int focusRequests = 0;
        selected.FocusRequested += (_, _) => focusRequests++;
        workspace.SelectedTab = null;

        workspace.RestoreTabAfterDrag(selected);

        Assert.Same(selected, workspace.SelectedTab);
        Assert.True(selected.IsSelected);
        Assert.Equal(1, focusRequests);
    }

    [Fact]
    public async Task WorkspaceListsAndAttachesPsmuxSessionOnlyOnce()
    {
        var psmux = new FakePsmuxService();
        psmux.Sessions.Add(new PsmuxSessionInfo("work", 2, false));
        var workspace = new TerminalWorkspaceViewModel(
            new FakeSessionService(),
            new FakeTerminalSessionFactory(),
            new FakePtySessionFactory(),
            psmux);

        await workspace.RefreshPsmuxSessionsAsync(TestContext.Current.CancellationToken);
        PsmuxSessionItemViewModel session = Assert.Single(workspace.PsmuxSessions);
        await workspace.OpenPsmuxSessionCommand.ExecuteAsync(session);
        await workspace.OpenPsmuxSessionCommand.ExecuteAsync(session);

        TerminalTabViewModel tab = Assert.Single(workspace.Tabs);
        Assert.True(tab.IsPsmux);
        Assert.Equal("work", tab.PsmuxSessionName);
        Assert.Same(tab, workspace.SelectedTab);
    }

    [Fact]
    public async Task WorkspaceOpensPsmuxSessionsDialogAndSelectsFirstSession()
    {
        var psmux = new FakePsmuxService();
        psmux.Sessions.Add(new PsmuxSessionInfo("alpha", 1, false));
        psmux.Sessions.Add(new PsmuxSessionInfo("beta", 2, true));
        var workspace = new TerminalWorkspaceViewModel(
            new FakeSessionService(),
            new FakeTerminalSessionFactory(),
            new FakePtySessionFactory(),
            psmux);

        await workspace.OpenPsmuxSessionsCommand.ExecuteAsync(null);

        Assert.True(workspace.IsPsmuxSessionsOpen);
        Assert.Equal(2, workspace.PsmuxSessions.Count);
        Assert.Equal("alpha", workspace.SelectedPsmuxSession?.Name);
        Assert.True(workspace.HasSelectedPsmuxSession);
        Assert.False(workspace.HasPsmuxSessionsMessage);
    }

    [Fact]
    public async Task WorkspaceRefreshesPsmuxSessionsAndPreservesSelectionByName()
    {
        var psmux = new FakePsmuxService();
        psmux.Sessions.Add(new PsmuxSessionInfo("alpha", 1, false));
        psmux.Sessions.Add(new PsmuxSessionInfo("beta", 1, false));
        var workspace = new TerminalWorkspaceViewModel(
            new FakeSessionService(),
            new FakeTerminalSessionFactory(),
            new FakePtySessionFactory(),
            psmux);
        await workspace.OpenPsmuxSessionsCommand.ExecuteAsync(null);
        workspace.SelectedPsmuxSession = workspace.PsmuxSessions[1];
        psmux.Sessions.Clear();
        psmux.Sessions.Add(new PsmuxSessionInfo("beta", 3, true));
        psmux.Sessions.Add(new PsmuxSessionInfo("gamma", 1, false));

        await workspace.RefreshPsmuxSessionsCommand.ExecuteAsync(null);

        Assert.Equal("beta", workspace.SelectedPsmuxSession?.Name);
        Assert.Equal(3, workspace.SelectedPsmuxSession?.WindowCount);
    }

    [Fact]
    public async Task WorkspaceAttachesSelectedPsmuxSessionAndClosesDialog()
    {
        var psmux = new FakePsmuxService();
        psmux.Sessions.Add(new PsmuxSessionInfo("work", 2, false));
        var workspace = new TerminalWorkspaceViewModel(
            new FakeSessionService(),
            new FakeTerminalSessionFactory(),
            new FakePtySessionFactory(),
            psmux);
        await workspace.OpenPsmuxSessionsCommand.ExecuteAsync(null);

        await workspace.AttachSelectedPsmuxSessionCommand.ExecuteAsync(null);

        Assert.False(workspace.IsPsmuxSessionsOpen);
        Assert.Equal("work", Assert.Single(workspace.Tabs).PsmuxSessionName);
    }

    [Fact]
    public async Task WorkspaceShowsEmptyPsmuxSessionsMessage()
    {
        var workspace = new TerminalWorkspaceViewModel(
            new FakeSessionService(),
            new FakeTerminalSessionFactory(),
            new FakePtySessionFactory(),
            new FakePsmuxService());

        await workspace.OpenPsmuxSessionsCommand.ExecuteAsync(null);

        Assert.False(workspace.HasPsmuxSessions);
        Assert.False(workspace.HasSelectedPsmuxSession);
        Assert.Equal("No active psmux sessions.", workspace.PsmuxSessionsMessage);
    }

    [Fact]
    public async Task ClosingPsmuxTabDetachesWithoutKillingSession()
    {
        var psmux = new FakePsmuxService();
        psmux.Sessions.Add(new PsmuxSessionInfo("work", 1, true));
        var workspace = new TerminalWorkspaceViewModel(
            new FakeSessionService(),
            new FakeTerminalSessionFactory(),
            new FakePtySessionFactory(),
            psmux);
        await workspace.RefreshPsmuxSessionsAsync(TestContext.Current.CancellationToken);
        await workspace.OpenPsmuxSessionCommand.ExecuteAsync(workspace.PsmuxSessions[0]);

        await workspace.CloseSelectedTabCommand.ExecuteAsync(null);

        Assert.Empty(workspace.Tabs);
        Assert.Empty(psmux.KilledSessions);
        Assert.Single(psmux.Sessions);
    }

    [Fact]
    public async Task WorkspaceConfirmsAndEndsPsmuxSessionWithMatchingTab()
    {
        var psmux = new FakePsmuxService();
        psmux.Sessions.Add(new PsmuxSessionInfo("work", 1, true));
        var workspace = new TerminalWorkspaceViewModel(
            new FakeSessionService(),
            new FakeTerminalSessionFactory(),
            new FakePtySessionFactory(),
            psmux);
        await workspace.OpenPsmuxSessionsCommand.ExecuteAsync(null);
        PsmuxSessionItemViewModel session = Assert.Single(workspace.PsmuxSessions);
        await workspace.OpenLocalTerminalCommand.ExecuteAsync(null);
        await workspace.OpenPsmuxSessionCommand.ExecuteAsync(session);

        workspace.RequestKillPsmuxSessionCommand.Execute(session);

        Assert.True(workspace.IsPsmuxKillConfirmationOpen);
        Assert.Equal("work", workspace.PsmuxSessionPendingKill?.Name);
        Assert.Equal(2, workspace.Tabs.Count);

        await workspace.ConfirmKillPsmuxSessionCommand.ExecuteAsync(null);

        Assert.Equal(["work"], psmux.KilledSessions);
        Assert.Empty(psmux.Sessions);
        TerminalTabViewModel remainingTab = Assert.Single(workspace.Tabs);
        Assert.True(remainingTab.IsLocal);
        Assert.Empty(workspace.PsmuxSessions);
        Assert.False(workspace.IsPsmuxKillConfirmationOpen);
        Assert.Null(workspace.PsmuxSessionPendingKill);
    }

    [Fact]
    public async Task WorkspaceKeepsPsmuxSessionAndTabWhenKillFails()
    {
        var psmux = new FakePsmuxService
        {
            KillError = new InvalidOperationException("kill failed")
        };
        psmux.Sessions.Add(new PsmuxSessionInfo("work", 1, true));
        var workspace = new TerminalWorkspaceViewModel(
            new FakeSessionService(),
            new FakeTerminalSessionFactory(),
            new FakePtySessionFactory(),
            psmux);
        await workspace.OpenPsmuxSessionsCommand.ExecuteAsync(null);
        PsmuxSessionItemViewModel session = Assert.Single(workspace.PsmuxSessions);
        await workspace.OpenPsmuxSessionCommand.ExecuteAsync(session);
        workspace.RequestKillPsmuxSessionCommand.Execute(session);

        await workspace.ConfirmKillPsmuxSessionCommand.ExecuteAsync(null);

        Assert.Empty(psmux.KilledSessions);
        Assert.Single(psmux.Sessions);
        Assert.Single(workspace.Tabs);
        Assert.True(workspace.IsPsmuxKillConfirmationOpen);
        Assert.Equal("kill failed", workspace.PsmuxKillError);
    }

    [Fact]
    public async Task WorkspaceCancelsPsmuxKillConfirmation()
    {
        var psmux = new FakePsmuxService();
        psmux.Sessions.Add(new PsmuxSessionInfo("work", 1, false));
        var workspace = new TerminalWorkspaceViewModel(
            new FakeSessionService(),
            new FakeTerminalSessionFactory(),
            new FakePtySessionFactory(),
            psmux);
        await workspace.OpenPsmuxSessionsCommand.ExecuteAsync(null);

        workspace.RequestKillPsmuxSessionCommand.Execute(workspace.PsmuxSessions[0]);
        workspace.CancelKillPsmuxSessionCommand.Execute(null);

        Assert.False(workspace.IsPsmuxKillConfirmationOpen);
        Assert.Null(workspace.PsmuxSessionPendingKill);
        Assert.Single(psmux.Sessions);
    }

    [Fact]
    public async Task WorkspaceConfirmsDuplicateBeforeAttachingPsmuxSession()
    {
        var psmux = new FakePsmuxService();
        psmux.Sessions.Add(new PsmuxSessionInfo("work", 1, false));
        var workspace = new TerminalWorkspaceViewModel(
            new FakeSessionService(),
            new FakeTerminalSessionFactory(),
            new FakePtySessionFactory(),
            psmux);
        await workspace.OpenPsmuxCreateCommand.ExecuteAsync(null);
        workspace.PsmuxSessionName = "work";

        await workspace.ConfirmPsmuxCreateCommand.ExecuteAsync(null);

        Assert.True(workspace.IsPsmuxDuplicate);
        Assert.Empty(workspace.Tabs);

        await workspace.ConfirmPsmuxCreateCommand.ExecuteAsync(null);

        Assert.Single(workspace.Tabs);
        Assert.False(workspace.IsPsmuxCreateOpen);
    }

    [Fact]
    public async Task WorkspaceListsNewPsmuxSessionBeforePtyStarts()
    {
        var psmux = new FakePsmuxService();
        var workspace = new TerminalWorkspaceViewModel(
            new FakeSessionService(),
            new FakeTerminalSessionFactory(),
            new FakePtySessionFactory(),
            psmux);
        await workspace.OpenPsmuxCreateCommand.ExecuteAsync(null);
        workspace.PsmuxSessionName = "work";

        await workspace.ConfirmPsmuxCreateCommand.ExecuteAsync(null);

        PsmuxSessionItemViewModel session = Assert.Single(workspace.PsmuxSessions);
        Assert.Equal("work", session.Name);
        Assert.Single(workspace.Tabs);
    }

    [Fact]
    public async Task MainWindowReleasesLoadingStateAfterInitialization()
    {
        var sessions = new FakeSessionService();
        var folders = new FakeFolderService();
        var settings = CreateSettingsViewModel(new FakeSettingsService(exists: true));
        var explorer = new SessionExplorerViewModel(sessions, folders);
        var workspace = new TerminalWorkspaceViewModel(
            sessions,
            new FakeTerminalSessionFactory(),
            new FakePtySessionFactory());
        var viewModel = new MainWindowViewModel(
            explorer,
            workspace,
            settings,
            new SessionEditorViewModel(sessions),
            new FolderEditorViewModel(folders));
        bool initializationCompleted = false;
        viewModel.InitializationCompleted += (_, _) => initializationCompleted = true;

        Assert.True(viewModel.IsInitializing);
        Assert.False(viewModel.IsInitialized);

        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.False(viewModel.IsInitializing);
        Assert.True(viewModel.IsInitialized);
        Assert.True(initializationCompleted);
        Assert.Single(workspace.Tabs);
    }

    [Fact]
    public async Task SessionEditorCreatesSessionAndReportsSelection()
    {
        var service = new FakeSessionService();
        var editor = new SessionEditorViewModel(service);
        Guid? selectedId = null;
        editor.SessionsChanged += id => selectedId = id;
        editor.OpenNew("Production");
        editor.EditorName = "Server";
        editor.EditorHost = "example.test";
        editor.EditorUsername = "admin";

        await editor.SaveSessionCommand.ExecuteAsync(null);

        SessionDetails expected = new(
            "Server", "example.test", 22, "admin", string.Empty, "Production", string.Empty);
        Assert.Single(service.Sessions);
        Assert.Equal(expected.Name, service.Sessions[0].Name);
        Assert.Equal(service.Sessions[0].Id, selectedId);
        Assert.False(editor.IsEditorOpen);
    }

    [Fact]
    public async Task FolderEditorForwardsForcedMultiDelete()
    {
        var service = new FakeFolderService();
        var editor = new FolderEditorViewModel(service);
        editor.OpenDelete(["A", "B"]);
        editor.ForceDeleteFolders = true;

        await editor.ConfirmDeleteFolderCommand.ExecuteAsync(null);

        Assert.Equal(["A", "B"], service.DeletedPaths);
        Assert.True(service.UsedForceDelete);
        Assert.False(editor.IsFolderDeleteConfirmationOpen);
    }

    [Fact]
    public async Task FirstRunDefaultPowerShellSavesAndCompletesSetup()
    {
        var settingsService = new FakeSettingsService(exists: false);
        var viewModel = new SettingsViewModel(
            settingsService,
            new FakeThemeService(),
            new FakeExecutablePicker(),
            new FakeArchiveService(),
            new FakeArchiveFilePicker(),
            new FakeSystemFontService());
        bool completed = false;
        viewModel.InitialSetupCompleted += () => completed = true;
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.ShowFirstRunSetup();

        await viewModel.UseDefaultPowerShellCommand.ExecuteAsync(null);

        Assert.Equal("pwsh.exe", settingsService.Value.PowerShellPath);
        Assert.True(completed);
        Assert.False(viewModel.IsPowerShellSetupOpen);
    }

    [Fact]
    public async Task SettingsSavePowerShellCommandFromPath()
    {
        var settingsService = new FakeSettingsService(exists: true);
        var viewModel = CreateSettingsViewModel(settingsService);
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.OpenSettingsCommand.Execute(null);
        viewModel.SettingsPowerShellPath = "powershell.exe";

        await viewModel.SaveSettingsCommand.ExecuteAsync(null);

        Assert.Equal("powershell.exe", settingsService.Value.PowerShellPath);
        Assert.Null(viewModel.SettingsError);
        Assert.False(viewModel.IsSettingsOpen);
    }

    [Fact]
    public async Task SettingsSaveSidebarScrollbarPreference()
    {
        var settingsService = new FakeSettingsService(exists: true);
        var viewModel = CreateSettingsViewModel(settingsService);
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.OpenSettingsCommand.Execute(null);
        viewModel.SettingsShowSidebarScrollbar = true;

        await viewModel.SaveSettingsCommand.ExecuteAsync(null);

        Assert.True(settingsService.Value.ShowSidebarScrollbar);
    }

    [Fact]
    public async Task SettingsSavesAndAppliesLogCapturePreference()
    {
        var settingsService = new FakeSettingsService(exists: true);
        var logs = new FakeApplicationLogService();
        var viewModel = new SettingsViewModel(
            settingsService,
            new FakeThemeService(),
            new FakeExecutablePicker(),
            new FakeArchiveService(),
            new FakeArchiveFilePicker(),
            new FakeSystemFontService(),
            logs,
            new FakeLogInteractionService());
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.OpenSettingsCommand.Execute(null);
        viewModel.SettingsCaptureLogs = false;

        await viewModel.SaveSettingsCommand.ExecuteAsync(null);

        Assert.False(settingsService.Value.CaptureLogs);
        Assert.False(logs.IsEnabled);
    }

    [Fact]
    public async Task SettingsRefreshesAndCopiesLogTail()
    {
        var logs = new FakeApplicationLogService { Content = "diagnostic entry" };
        var interactions = new FakeLogInteractionService();
        var viewModel = new SettingsViewModel(
            new FakeSettingsService(exists: true),
            new FakeThemeService(),
            new FakeExecutablePicker(),
            new FakeArchiveService(),
            new FakeArchiveFilePicker(),
            new FakeSystemFontService(),
            logs,
            interactions);
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        await viewModel.RefreshLogsCommand.ExecuteAsync(null);
        await viewModel.CopyLogsCommand.ExecuteAsync(null);

        Assert.Equal("diagnostic entry", viewModel.LogContent);
        Assert.Equal("diagnostic entry", interactions.CopiedText);
    }

    [Fact]
    public async Task SettingsCancelDoesNotSaveSidebarScrollbarPreference()
    {
        var settingsService = new FakeSettingsService(exists: true);
        var viewModel = CreateSettingsViewModel(settingsService);
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.OpenSettingsCommand.Execute(null);
        viewModel.SettingsShowSidebarScrollbar = true;

        viewModel.CancelSettingsCommand.Execute(null);
        viewModel.OpenSettingsCommand.Execute(null);

        Assert.False(settingsService.Value.ShowSidebarScrollbar);
        Assert.False(viewModel.SettingsShowSidebarScrollbar);
    }

    [Fact]
    public async Task SettingsBrowseUpdatesPowerShellFieldWithoutSaving()
    {
        var settingsService = new FakeSettingsService(exists: true);
        var viewModel = CreateSettingsViewModel(
            settingsService,
            new FakeExecutablePicker(@"C:\Tools\pwsh.exe"));
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        await viewModel.SelectPowerShellCommand.ExecuteAsync(null);

        Assert.Equal(@"C:\Tools\pwsh.exe", viewModel.SettingsPowerShellPath);
        Assert.Equal("pwsh.exe", settingsService.Value.PowerShellPath);
    }

    [Fact]
    public async Task SettingsRejectNonPowerShellExecutable()
    {
        var settingsService = new FakeSettingsService(exists: true);
        var viewModel = CreateSettingsViewModel(settingsService);
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.OpenSettingsCommand.Execute(null);
        viewModel.SettingsPowerShellPath = "cmd.exe";

        await viewModel.SaveSettingsCommand.ExecuteAsync(null);

        Assert.Contains("pwsh.exe or powershell.exe", viewModel.SettingsError);
        Assert.True(viewModel.IsSettingsOpen);
        Assert.Equal("pwsh.exe", settingsService.Value.PowerShellPath);
    }

    [Fact]
    public async Task SettingsAlwaysOpenOnGeneralTab()
    {
        var viewModel = CreateSettingsViewModel(new FakeSettingsService(exists: true));
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.OpenSettingsCommand.Execute(null);
        viewModel.SelectedSettingsTabIndex = 2;
        viewModel.CancelSettingsCommand.Execute(null);

        viewModel.OpenSettingsCommand.Execute(null);

        Assert.Equal(0, viewModel.SelectedSettingsTabIndex);
    }

    private static SettingsViewModel CreateSettingsViewModel(
        FakeSettingsService settingsService,
        FakeExecutablePicker? executablePicker = null) =>
        new(
            settingsService,
            new FakeThemeService(),
            executablePicker ?? new FakeExecutablePicker(),
            new FakeArchiveService(),
            new FakeArchiveFilePicker(),
            new FakeSystemFontService());
}
