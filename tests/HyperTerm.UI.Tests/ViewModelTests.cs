using HyperTerm.Core.Models;
using HyperTerm.UI.Services;
using HyperTerm.UI.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HyperTerm.UI.Tests;

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
    public async Task Workspace_opens_selected_terminal_profile()
    {
        const string profileId = "command-prompt";
        var workspace = new TerminalWorkspaceViewModel(
            new FakeSessionService(),
            new FakeTerminalSessionFactory(),
            new FakePtySessionFactory());
        workspace.ApplySettings(new ApplicationSettings
        {
            TerminalProfiles =
            [
                new TerminalProfile
                {
                    Id = profileId,
                    Name = "Command Prompt",
                    ExecutablePath = "cmd.exe",
                },
            ],
            DefaultTerminalProfileId = profileId,
        });
        TerminalLaunchProfileViewModel profile = Assert.Single(workspace.TerminalProfiles);

        await workspace.OpenTerminalProfileCommand.ExecuteAsync(profile);

        TerminalTabViewModel tab = Assert.Single(workspace.Tabs);
        Assert.Equal(profileId, tab.Definition.ProfileId);
    }

    [Fact]
    public async Task Workspace_updates_theme_for_open_and_future_tabs()
    {
        var workspace = new TerminalWorkspaceViewModel(
            new FakeSessionService(),
            new FakeTerminalSessionFactory(),
            new FakePtySessionFactory());
        workspace.ApplySettings(new ApplicationSettings());
        await workspace.OpenLocalTerminalCommand.ExecuteAsync(null);
        TerminalTabViewModel existing = Assert.Single(workspace.Tabs);

        workspace.ApplySettings(new ApplicationSettings { Theme = "Default Light" });
        await workspace.OpenLocalTerminalCommand.ExecuteAsync(null);

        Assert.Equal("Default Light", existing.Theme);
        Assert.Equal("Default Light", workspace.Tabs[1].Theme);
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
    public async Task WorkspaceShutdownKeepsPsmuxServerByDefault()
    {
        var psmux = new FakePsmuxService();
        var workspace = new TerminalWorkspaceViewModel(
            new FakeSessionService(),
            new FakeTerminalSessionFactory(),
            new FakePtySessionFactory(),
            psmux);

        await workspace.ShutdownAsync();

        Assert.Equal(0, psmux.StopServerCalls);
    }

    [Fact]
    public async Task WorkspaceShutdownDetachesTabsBeforeStoppingPsmuxServer()
    {
        var psmux = new FakePsmuxService();
        psmux.Sessions.Add(new PsmuxSessionInfo("work", 1, false));
        var workspace = new TerminalWorkspaceViewModel(
            new FakeSessionService(),
            new FakeTerminalSessionFactory(),
            new FakePtySessionFactory(),
            psmux);
        workspace.ApplySettings(new ApplicationSettings
        {
            KeepPsmuxSessionsOnExit = false,
        });
        await workspace.RefreshPsmuxSessionsAsync(TestContext.Current.CancellationToken);
        await workspace.OpenPsmuxSessionCommand.ExecuteAsync(workspace.PsmuxSessions[0]);

        await workspace.ShutdownAsync();

        Assert.Empty(workspace.Tabs);
        Assert.Equal(1, psmux.StopServerCalls);
    }

    [Fact]
    public async Task WorkspaceShutdownContinuesWhenStoppingPsmuxFails()
    {
        var psmux = new FakePsmuxService
        {
            StopServerError = new InvalidOperationException("stop failed"),
        };
        var workspace = new TerminalWorkspaceViewModel(
            new FakeSessionService(),
            new FakeTerminalSessionFactory(),
            new FakePtySessionFactory(),
            psmux);
        workspace.ApplySettings(new ApplicationSettings
        {
            KeepPsmuxSessionsOnExit = false,
        });

        await workspace.ShutdownAsync();

        Assert.Equal(1, psmux.StopServerCalls);
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
    public async Task CommandPaletteCombinesActionsSessionsTabsAndPsmux()
    {
        var sessions = new FakeSessionService();
        sessions.Sessions.Add(FakeSessionService.CreateSession(
            Guid.NewGuid(),
            new SessionDetails(
                "Production server", "prod.test", 22, "admin", null,
                "Production", "Primary host")));
        var folders = new FakeFolderService();
        var explorer = new SessionExplorerViewModel(sessions, folders);
        await explorer.InitializeAsync(TestContext.Current.CancellationToken);
        var psmux = new FakePsmuxService();
        psmux.Sessions.Add(new PsmuxSessionInfo("persistent-work", 2, false));
        var workspace = new TerminalWorkspaceViewModel(
            sessions,
            new FakeTerminalSessionFactory(),
            new FakePtySessionFactory(),
            psmux);
        await workspace.OpenLocalTerminalCommand.ExecuteAsync(null);
        await workspace.OpenSessionAsync(explorer.Sessions[0]);
        await workspace.RefreshPsmuxSessionsAsync(TestContext.Current.CancellationToken);
        await workspace.OpenPsmuxSessionCommand.ExecuteAsync(workspace.PsmuxSessions[0]);
        var viewModel = new MainWindowViewModel(
            explorer,
            workspace,
            CreateSettingsViewModel(new FakeSettingsService(exists: true)),
            new SessionEditorViewModel(sessions),
            new FolderEditorViewModel(folders));

        viewModel.OpenCommandPaletteCommand.Execute(null);

        Assert.True(viewModel.IsCommandPaletteOpen);
        Assert.Contains(viewModel.CommandPaletteResults, item => item.Title == "New SSH session");
        Assert.Contains(viewModel.CommandPaletteResults, item => item.Title == "Production server");
        Assert.Contains(viewModel.CommandPaletteResults, item => item.Category == "Open tab");
        Assert.Contains(viewModel.CommandPaletteResults, item => item.Title == "persistent-work");

        viewModel.CommandPaletteQuery = "prod server";
        Assert.Contains(
            viewModel.CommandPaletteResults,
            item => item.Kind == CommandPaletteItemKind.SavedSshSession);
        Assert.Contains(
            viewModel.CommandPaletteResults,
            item => item.Kind == CommandPaletteItemKind.OpenTab);

        viewModel.CommandPaletteQuery = "  > settings";
        CommandPaletteItemViewModel command = Assert.Single(viewModel.CommandPaletteResults);
        Assert.Equal(CommandPaletteItemKind.Action, command.Kind);
        Assert.Equal("Open settings", command.Title);

        viewModel.CommandPaletteQuery = ">";
        Assert.NotEmpty(viewModel.CommandPaletteResults);
        Assert.All(
            viewModel.CommandPaletteResults,
            item => Assert.Equal(CommandPaletteItemKind.Action, item.Kind));

        viewModel.CommandPaletteQuery = ":";
        Assert.Equal(workspace.Tabs.Count, viewModel.CommandPaletteResults.Count);
        Assert.All(
            viewModel.CommandPaletteResults,
            item => Assert.Equal(CommandPaletteItemKind.OpenTab, item.Kind));
        Assert.DoesNotContain(
            viewModel.CommandPaletteResults,
            item => item.Kind is CommandPaletteItemKind.SavedSshSession or
                CommandPaletteItemKind.PsmuxSession);

        viewModel.CommandPaletteQuery = ": prod";
        CommandPaletteItemViewModel openSession = Assert.Single(
            viewModel.CommandPaletteResults);
        Assert.Equal(CommandPaletteItemKind.OpenTab, openSession.Kind);
        Assert.Equal("Production server", openSession.Title);

        viewModel.CommandPaletteQuery = ": missing";
        Assert.Empty(viewModel.CommandPaletteResults);
        Assert.Null(viewModel.SelectedCommandPaletteItem);
        Assert.Equal("No matching open sessions.", viewModel.CommandPaletteEmptyMessage);

        viewModel.CommandPaletteQuery = "persistent-work";
        Assert.Contains(
            viewModel.CommandPaletteResults,
            item => item.Kind == CommandPaletteItemKind.PsmuxSession);
    }

    [Fact]
    public async Task CommandPaletteExecutesSelectionAndRestoresTerminalVisibility()
    {
        var sessions = new FakeSessionService();
        var folders = new FakeFolderService();
        var explorer = new SessionExplorerViewModel(sessions, folders);
        await explorer.InitializeAsync(TestContext.Current.CancellationToken);
        var workspace = new TerminalWorkspaceViewModel(
            sessions,
            new FakeTerminalSessionFactory(),
            new FakePtySessionFactory());
        await workspace.OpenLocalTerminalCommand.ExecuteAsync(null);
        var viewModel = new MainWindowViewModel(
            explorer,
            workspace,
            CreateSettingsViewModel(new FakeSettingsService(exists: true)),
            new SessionEditorViewModel(sessions),
            new FolderEditorViewModel(folders));
        int overlayNotifications = 0;
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(MainWindowViewModel.IsOverlayOpen))
            {
                overlayNotifications++;
            }
        };
        viewModel.OpenCommandPaletteCommand.Execute(null);
        Assert.False(viewModel.AreTerminalHostsVisible);
        Assert.True(viewModel.IsOverlayOpen);
        viewModel.CommandPaletteQuery = "toggle status bar";

        await viewModel.ExecuteSelectedCommandPaletteItemCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsStatusBarVisible);
        Assert.False(viewModel.IsCommandPaletteOpen);
        Assert.False(viewModel.IsOverlayOpen);
        Assert.True(viewModel.AreTerminalHostsVisible);
        Assert.Equal(2, overlayNotifications);
    }

    [Fact]
    public async Task ApplicationLifecycleInitializesAndShutsDownOnlyOnce()
    {
        var database = new FakeDatabaseInitializer();
        var sessions = new FakeSessionService();
        var folders = new FakeFolderService();
        var workspace = new TerminalWorkspaceViewModel(
            sessions,
            new FakeTerminalSessionFactory(),
            new FakePtySessionFactory());
        var viewModel = new MainWindowViewModel(
            new SessionExplorerViewModel(sessions, folders),
            workspace,
            CreateSettingsViewModel(new FakeSettingsService(exists: true)),
            new SessionEditorViewModel(sessions),
            new FolderEditorViewModel(folders));
        using var lifecycle = new ApplicationLifecycleCoordinator(
            database,
            viewModel,
            NullLogger<ApplicationLifecycleCoordinator>.Instance);

        Task firstInitialization = lifecycle.InitializeAsync();
        Task secondInitialization = lifecycle.InitializeAsync();
        await Task.WhenAll(firstInitialization, secondInitialization);

        Assert.Same(firstInitialization, secondInitialization);
        Assert.Equal(1, database.InitializeCalls);
        Assert.True(viewModel.IsInitialized);
        Assert.Single(workspace.Tabs);

        Task firstShutdown = lifecycle.ShutdownAsync();
        Task secondShutdown = lifecycle.ShutdownAsync();
        await Task.WhenAll(firstShutdown, secondShutdown);

        Assert.Same(firstShutdown, secondShutdown);
        Assert.Empty(workspace.Tabs);
    }

    [Fact]
    public async Task Default_terminal_shortcut_command_ignores_open_overlays()
    {
        var sessions = new FakeSessionService();
        var folders = new FakeFolderService();
        var workspace = new TerminalWorkspaceViewModel(
            sessions,
            new FakeTerminalSessionFactory(),
            new FakePtySessionFactory());
        var viewModel = new MainWindowViewModel(
            new SessionExplorerViewModel(sessions, folders),
            workspace,
            CreateSettingsViewModel(new FakeSettingsService(exists: true)),
            new SessionEditorViewModel(sessions),
            new FolderEditorViewModel(folders));
        int focusRequests = 0;
        workspace.Tabs.CollectionChanged += (_, eventArgs) =>
        {
            if (eventArgs.NewItems?[0] is TerminalTabViewModel tab)
            {
                tab.FocusRequested += (_, _) => focusRequests++;
            }
        };

        await viewModel.OpenDefaultTerminalCommand.ExecuteAsync(null);
        Assert.Single(workspace.Tabs);
        Assert.True(focusRequests > 0);

        viewModel.Settings.OpenSettingsCommand.Execute(null);
        await viewModel.OpenDefaultTerminalCommand.ExecuteAsync(null);

        Assert.Single(workspace.Tabs);
    }

    [Fact]
    public async Task Tab_commands_are_disabled_while_an_overlay_is_open()
    {
        var sessions = new FakeSessionService();
        var folders = new FakeFolderService();
        var workspace = new TerminalWorkspaceViewModel(
            sessions,
            new FakeTerminalSessionFactory(),
            new FakePtySessionFactory());
        var viewModel = new MainWindowViewModel(
            new SessionExplorerViewModel(sessions, folders),
            workspace,
            CreateSettingsViewModel(new FakeSettingsService(exists: true)),
            new SessionEditorViewModel(sessions),
            new FolderEditorViewModel(folders));
        await workspace.OpenLocalTerminalCommand.ExecuteAsync(null);
        await workspace.OpenLocalTerminalCommand.ExecuteAsync(null);
        TerminalTabViewModel selected = workspace.SelectedTab!;

        viewModel.Settings.OpenSettingsCommand.Execute(null);
        Assert.False(viewModel.CanUseTerminalTabs);
        viewModel.NextTerminalTabCommand.Execute(null);
        viewModel.PreviousTerminalTabCommand.Execute(null);
        await viewModel.CloseActiveTerminalTabCommand.ExecuteAsync(null);

        Assert.Equal(2, workspace.Tabs.Count);
        Assert.Same(selected, workspace.SelectedTab);

        viewModel.Settings.CancelSettingsCommand.Execute(null);
        Assert.True(viewModel.CanUseTerminalTabs);
        viewModel.NextTerminalTabCommand.Execute(null);
        Assert.NotSame(selected, workspace.SelectedTab);
        await viewModel.CloseActiveTerminalTabCommand.ExecuteAsync(null);
        Assert.Single(workspace.Tabs);
    }

    [Fact]
    public async Task Opening_primary_overlay_closes_previous_overlay()
    {
        var sessions = new FakeSessionService();
        var folders = new FakeFolderService();
        var psmux = new FakePsmuxService();
        var workspace = new TerminalWorkspaceViewModel(
            sessions,
            new FakeTerminalSessionFactory(),
            new FakePtySessionFactory(),
            psmux);
        var settings = CreateSettingsViewModel(new FakeSettingsService(exists: true));
        await settings.InitializeAsync(TestContext.Current.CancellationToken);
        var sessionEditor = new SessionEditorViewModel(sessions);
        var folderEditor = new FolderEditorViewModel(folders);
        var viewModel = new MainWindowViewModel(
            new SessionExplorerViewModel(sessions, folders),
            workspace,
            settings,
            sessionEditor,
            folderEditor);

        settings.OpenSettingsCommand.Execute(null);
        settings.SettingsTerminalFontSize = 42;
        viewModel.OpenShortcutsCommand.Execute(null);

        Assert.False(settings.IsSettingsOpen);
        Assert.True(viewModel.IsShortcutsOpen);

        settings.OpenSettingsCommand.Execute(null);
        Assert.False(viewModel.IsShortcutsOpen);
        Assert.Equal(13, settings.SettingsTerminalFontSize);

        viewModel.OpenCommandPaletteCommand.Execute(null);
        Assert.False(settings.IsSettingsOpen);
        Assert.True(viewModel.IsCommandPaletteOpen);

        sessionEditor.OpenNew(string.Empty);
        Assert.False(viewModel.IsCommandPaletteOpen);
        Assert.True(sessionEditor.IsEditorOpen);

        folderEditor.OpenNew(string.Empty);
        Assert.False(sessionEditor.IsEditorOpen);
        Assert.True(folderEditor.IsFolderEditorOpen);

        await workspace.OpenPsmuxSessionsCommand.ExecuteAsync(null);
        Assert.False(folderEditor.IsFolderEditorOpen);
        Assert.True(workspace.IsPsmuxSessionsOpen);

        await workspace.OpenPsmuxCreateCommand.ExecuteAsync(null);
        Assert.False(workspace.IsPsmuxSessionsOpen);
        Assert.True(workspace.IsPsmuxCreateOpen);
    }

    [Fact]
    public async Task Psmux_kill_confirmation_keeps_its_parent_until_another_overlay_opens()
    {
        var sessions = new FakeSessionService();
        var folders = new FakeFolderService();
        var psmux = new FakePsmuxService();
        psmux.Sessions.Add(new PsmuxSessionInfo("persistent", 1, false));
        var workspace = new TerminalWorkspaceViewModel(
            sessions,
            new FakeTerminalSessionFactory(),
            new FakePtySessionFactory(),
            psmux);
        var viewModel = new MainWindowViewModel(
            new SessionExplorerViewModel(sessions, folders),
            workspace,
            CreateSettingsViewModel(new FakeSettingsService(exists: true)),
            new SessionEditorViewModel(sessions),
            new FolderEditorViewModel(folders));

        await workspace.OpenPsmuxSessionsCommand.ExecuteAsync(null);
        workspace.RequestKillPsmuxSessionCommand.Execute(workspace.PsmuxSessions[0]);

        Assert.True(workspace.IsPsmuxSessionsOpen);
        Assert.True(workspace.IsPsmuxKillConfirmationOpen);

        viewModel.OpenShortcutsCommand.Execute(null);

        Assert.False(workspace.IsPsmuxKillConfirmationOpen);
        Assert.False(workspace.IsPsmuxSessionsOpen);
        Assert.True(viewModel.IsShortcutsOpen);
    }

    [Fact]
    public void Opening_delete_confirmation_closes_unrelated_overlay()
    {
        var sessions = new FakeSessionService();
        var folders = new FakeFolderService();
        var sessionEditor = new SessionEditorViewModel(sessions);
        var folderEditor = new FolderEditorViewModel(folders);
        var settings = CreateSettingsViewModel(new FakeSettingsService(exists: true));
        var viewModel = new MainWindowViewModel(
            new SessionExplorerViewModel(sessions, folders),
            new TerminalWorkspaceViewModel(
                sessions,
                new FakeTerminalSessionFactory(),
                new FakePtySessionFactory()),
            settings,
            sessionEditor,
            folderEditor);
        var session = new SessionListItemViewModel(FakeSessionService.CreateSession(
            Guid.NewGuid(),
            new SessionDetails(
                "Server", "example.test", 22, "admin", null, string.Empty, null)));

        settings.OpenSettingsCommand.Execute(null);
        sessionEditor.RequestDelete(session);

        Assert.False(settings.IsSettingsOpen);
        Assert.True(sessionEditor.IsDeleteConfirmationOpen);

        folderEditor.OpenDelete(["Production"]);

        Assert.False(sessionEditor.IsDeleteConfirmationOpen);
        Assert.True(folderEditor.IsFolderDeleteConfirmationOpen);
        Assert.True(viewModel.IsOverlayOpen);
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
    public async Task First_run_persists_detected_power_shell_profile()
    {
        var settingsService = new FakeSettingsService(exists: false);
        var viewModel = new SettingsViewModel(
            settingsService,
            new FakeThemeService(),
            new FakeExecutablePicker(),
            new FakeArchiveService(),
            new FakeArchiveFilePicker(),
            new FakeSystemFontService(),
            terminalProfileResolver: new FakeTerminalProfileResolver(
                "pwsh.exe",
                "powershell.exe"));
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        TerminalProfile profile = Assert.Single(settingsService.Value.TerminalProfiles);
        Assert.Equal(TerminalProfileIds.PowerShell, profile.Id);
        Assert.Equal("pwsh.exe", profile.ExecutablePath);
        Assert.Equal("Default Dark", settingsService.Value.Theme);
    }

    [Fact]
    public async Task First_run_falls_back_to_windows_power_shell()
    {
        var settingsService = new FakeSettingsService(exists: false);
        var viewModel = new SettingsViewModel(
            settingsService,
            new FakeThemeService(),
            new FakeExecutablePicker(),
            new FakeArchiveService(),
            new FakeArchiveFilePicker(),
            new FakeSystemFontService(),
            terminalProfileResolver: new FakeTerminalProfileResolver("powershell.exe"));
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        TerminalProfile profile = Assert.Single(settingsService.Value.TerminalProfiles);
        Assert.Equal("powershell.exe", profile.ExecutablePath);
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
    public async Task SettingsSaveCloseToSystemTrayPreference()
    {
        var settingsService = new FakeSettingsService(exists: true);
        var viewModel = CreateSettingsViewModel(settingsService);
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.OpenSettingsCommand.Execute(null);
        viewModel.SettingsCloseToSystemTray = true;

        await viewModel.SaveSettingsCommand.ExecuteAsync(null);

        Assert.True(settingsService.Value.CloseToSystemTray);
    }

    [Fact]
    public async Task SettingsSavePsmuxShutdownPreference()
    {
        var settingsService = new FakeSettingsService(exists: true);
        var viewModel = CreateSettingsViewModel(settingsService);
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.OpenSettingsCommand.Execute(null);
        viewModel.SettingsKeepPsmuxSessionsOnExit = false;

        await viewModel.SaveSettingsCommand.ExecuteAsync(null);

        Assert.False(settingsService.Value.KeepPsmuxSessionsOnExit);
    }

    [Fact]
    public async Task Settings_can_change_default_terminal_profile()
    {
        var settingsService = new FakeSettingsService(exists: true);
        var viewModel = CreateSettingsViewModel(settingsService);
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.OpenSettingsCommand.Execute(null);
        viewModel.AddTerminalProfileCommand.Execute(null);
        TerminalProfileItemViewModel commandPrompt = viewModel.TerminalProfiles.Last();
        commandPrompt.Name = "Command Prompt";
        commandPrompt.ExecutablePath = "cmd.exe";

        viewModel.SetDefaultTerminalProfileCommand.Execute(commandPrompt);
        await viewModel.SaveSettingsCommand.ExecuteAsync(null);

        Assert.Equal(
            commandPrompt.Id,
            settingsService.Value.DefaultTerminalProfileId);
    }

    [Fact]
    public async Task Settings_can_remove_power_shell_after_changing_default()
    {
        var settingsService = new FakeSettingsService(exists: true);
        var viewModel = CreateSettingsViewModel(settingsService);
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.OpenSettingsCommand.Execute(null);
        TerminalProfileItemViewModel powerShell = Assert.Single(viewModel.TerminalProfiles);

        viewModel.DeleteTerminalProfileCommand.Execute(powerShell);
        Assert.Single(viewModel.TerminalProfiles);
        viewModel.AddTerminalProfileCommand.Execute(null);
        TerminalProfileItemViewModel custom = viewModel.TerminalProfiles.Last();
        custom.Name = "Command Prompt";
        custom.ExecutablePath = "cmd.exe";
        viewModel.SetDefaultTerminalProfileCommand.Execute(custom);
        viewModel.DeleteTerminalProfileCommand.Execute(powerShell);
        await viewModel.SaveSettingsCommand.ExecuteAsync(null);

        TerminalProfile saved = Assert.Single(settingsService.Value.TerminalProfiles);
        Assert.Equal(custom.Id, saved.Id);
        Assert.Equal(custom.Id, settingsService.Value.DefaultTerminalProfileId);
        Assert.Equal("pwsh.exe", settingsService.Value.PowerShellPath);
    }

    [Fact]
    public async Task Settings_adds_custom_profile_with_literal_arguments()
    {
        var settingsService = new FakeSettingsService(exists: true);
        var viewModel = CreateSettingsViewModel(settingsService);
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.OpenSettingsCommand.Execute(null);

        viewModel.AddTerminalProfileCommand.Execute(null);
        TerminalProfileItemViewModel profile = viewModel.TerminalProfiles.Last();
        profile.Name = "Developer shell";
        profile.ExecutablePath = "cmd.exe";
        profile.ArgumentsText = "/Q\r\n/K\r\necho ready";
        viewModel.SetDefaultTerminalProfileCommand.Execute(profile);
        await viewModel.SaveSettingsCommand.ExecuteAsync(null);

        TerminalProfile saved = settingsService.Value.TerminalProfiles.Single(item =>
            item.Id == profile.Id);
        Assert.Equal(["/Q", "/K", "echo ready"], saved.Arguments);
        Assert.Equal(profile.Id, settingsService.Value.DefaultTerminalProfileId);
    }

    [Fact]
    public async Task Settings_cancel_discards_added_terminal_profile()
    {
        var settingsService = new FakeSettingsService(exists: true);
        var viewModel = CreateSettingsViewModel(settingsService);
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.OpenSettingsCommand.Execute(null);

        viewModel.AddTerminalProfileCommand.Execute(null);
        Assert.Equal(2, viewModel.TerminalProfiles.Count);
        viewModel.CancelSettingsCommand.Execute(null);
        viewModel.OpenSettingsCommand.Execute(null);

        Assert.Single(viewModel.TerminalProfiles);
        Assert.Empty(settingsService.Value.TerminalProfiles);
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
    public async Task SettingsPollLogsWhileDialogIsOpen()
    {
        var logs = new FakeApplicationLogService { Content = "live diagnostic entry" };
        var viewModel = new SettingsViewModel(
            new FakeSettingsService(exists: true),
            new FakeThemeService(),
            new FakeExecutablePicker(),
            new FakeArchiveService(),
            new FakeArchiveFilePicker(),
            new FakeSystemFontService(),
            logs,
            new FakeLogInteractionService());
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        viewModel.OpenSettingsCommand.Execute(null);
        await logs.WaitForReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("live diagnostic entry", viewModel.LogContent);
        viewModel.CancelSettingsCommand.Execute(null);
        Assert.False(viewModel.IsSettingsOpen);
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
    public async Task SettingsOpenLoadsEditorValues()
    {
        var viewModel = CreateSettingsViewModel(new FakeSettingsService(exists: true));
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.OpenSettingsCommand.Execute(null);
        viewModel.SettingsShowSidebarScrollbar = true;
        viewModel.CancelSettingsCommand.Execute(null);

        viewModel.OpenSettingsCommand.Execute(null);

        Assert.False(viewModel.SettingsShowSidebarScrollbar);
    }

    [Fact]
    public async Task Settings_loads_persisted_font_into_picker_options()
    {
        var settingsService = new FakeSettingsService(exists: true);
        await settingsService.SaveAsync(new ApplicationSettings
        {
            TerminalFontFamily = "Consolas",
        });
        var viewModel = CreateSettingsViewModel(settingsService);

        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Consolas", viewModel.SettingsTerminalFontFamily);
        Assert.Contains("Consolas", viewModel.SystemFontFamilies);
        Assert.Equal(0, viewModel.SettingsTerminalFontFamilyIndex);

        viewModel.SettingsTerminalFontFamilyIndex = -1;

        Assert.Equal("Consolas", viewModel.SettingsTerminalFontFamily);
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Unknown")]
    public async Task Settings_normalizes_legacy_or_unknown_theme(string persistedTheme)
    {
        var settingsService = new FakeSettingsService(exists: true);
        await settingsService.SaveAsync(new ApplicationSettings { Theme = persistedTheme });
        var viewModel = CreateSettingsViewModel(settingsService);

        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Default Dark", viewModel.SettingsTheme.Value);
        Assert.Equal("Default Dark", viewModel.Current.Theme);
    }

    [Fact]
    public async Task Settings_saves_and_applies_default_dark_theme()
    {
        var settingsService = new FakeSettingsService(exists: true);
        var themeService = new FakeThemeService();
        var viewModel = new SettingsViewModel(
            settingsService,
            themeService,
            new FakeExecutablePicker(),
            new FakeArchiveService(),
            new FakeArchiveFilePicker(),
            new FakeSystemFontService());
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.OpenSettingsCommand.Execute(null);

        await viewModel.SaveSettingsCommand.ExecuteAsync(null);

        Assert.Equal("Default Dark", settingsService.Value.Theme);
        Assert.Equal("Default Dark", themeService.AppliedThemes.Last());
    }

    [Fact]
    public async Task Settings_saves_and_applies_default_light_theme()
    {
        var settingsService = new FakeSettingsService(exists: true);
        var themeService = new FakeThemeService();
        var viewModel = new SettingsViewModel(
            settingsService,
            themeService,
            new FakeExecutablePicker(),
            new FakeArchiveService(),
            new FakeArchiveFilePicker(),
            new FakeSystemFontService());
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.OpenSettingsCommand.Execute(null);
        viewModel.SettingsTheme = viewModel.ThemeOptions.Single(option =>
            option.Value == "Default Light");

        await viewModel.SaveSettingsCommand.ExecuteAsync(null);

        Assert.Equal("Default Light", settingsService.Value.Theme);
        Assert.Equal("Default Light", themeService.AppliedThemes.Last());
    }

    [Fact]
    public async Task Settings_saves_and_applies_darcula_theme()
    {
        var settingsService = new FakeSettingsService(exists: true);
        var themeService = new FakeThemeService();
        var viewModel = new SettingsViewModel(
            settingsService,
            themeService,
            new FakeExecutablePicker(),
            new FakeArchiveService(),
            new FakeArchiveFilePicker(),
            new FakeSystemFontService());
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.OpenSettingsCommand.Execute(null);
        viewModel.SettingsTheme = viewModel.ThemeOptions.Single(option =>
            option.Value == "Darcula");

        await viewModel.SaveSettingsCommand.ExecuteAsync(null);

        Assert.Equal("Darcula", settingsService.Value.Theme);
        Assert.Equal("Darcula", themeService.AppliedThemes.Last());
    }

    [Fact]
    public async Task Settings_persists_follow_current_theme_selection_color()
    {
        var settingsService = new FakeSettingsService(exists: true);
        var viewModel = CreateSettingsViewModel(settingsService);
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.OpenSettingsCommand.Execute(null);
        viewModel.SettingsTerminalSelectionColor = viewModel.TerminalSelectionColors.Single(option =>
            option.Value == "Theme");

        await viewModel.SaveSettingsCommand.ExecuteAsync(null);

        Assert.Equal("Theme", settingsService.Value.TerminalSelectionColor);

        var restoredViewModel = CreateSettingsViewModel(settingsService);
        await restoredViewModel.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Theme", restoredViewModel.SettingsTerminalSelectionColor.Value);
    }

    [Fact]
    public async Task Settings_cancel_restores_saved_theme_without_applying_editor_value()
    {
        var settingsService = new FakeSettingsService(exists: true);
        var themeService = new FakeThemeService();
        var viewModel = new SettingsViewModel(
            settingsService,
            themeService,
            new FakeExecutablePicker(),
            new FakeArchiveService(),
            new FakeArchiveFilePicker(),
            new FakeSystemFontService());
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        int initialApplyCount = themeService.AppliedThemes.Count;
        viewModel.OpenSettingsCommand.Execute(null);
        viewModel.SettingsTheme = new ThemeOption(
            "Temporary",
            "Temporary",
            "Test only",
            "#000000",
            "#000000",
            "#000000");

        viewModel.CancelSettingsCommand.Execute(null);
        viewModel.OpenSettingsCommand.Execute(null);

        Assert.Equal("Default Dark", viewModel.SettingsTheme.Value);
        Assert.Equal(initialApplyCount, themeService.AppliedThemes.Count);
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
