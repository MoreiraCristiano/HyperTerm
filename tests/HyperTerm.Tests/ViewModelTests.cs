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

        explorer.SearchText = "no-match";

        Assert.False(explorer.SessionTree.Single(
            node => node.Name == "WithSession").IsEmptyFolder);
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
}
