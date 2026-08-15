using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HyperTerm.Core.Models;
using HyperTerm.UI.Controls;
using HyperTerm.UI.Services;
using HyperTerm.UI.ViewModels;
using HyperTerm.UI.Views;
using HyperTerm.UI.Views.Dialogs;

namespace HyperTerm.UI.Tests;

public sealed class AvaloniaHeadlessTests
{
    [AvaloniaFact]
    [Trait("Category", "Headless")]
    public async Task Terminal_host_removal_is_idempotent_and_clears_tab_references()
    {
        var workspace = new TerminalWorkspaceViewModel(
            new FakeSessionService(),
            new FakeTerminalSessionFactory(),
            new FakePtySessionFactory());
        workspace.ApplySettings(new ApplicationSettings());
        await workspace.OpenLocalTerminalCommand.ExecuteAsync(null);
        TerminalTabViewModel tab = Assert.Single(workspace.Tabs);
        var parent = new Grid();
        var host = new WebTerminalHostControl
        {
            Tabs = workspace.Tabs,
            ActiveTab = tab,
            Tab = tab,
        };
        parent.Children.Add(host);

        MainWindow.RemoveTerminalHost(host);
        MainWindow.RemoveTerminalHost(host);

        Assert.Empty(parent.Children);
        Assert.Null(host.Tabs);
        Assert.Null(host.ActiveTab);
        Assert.Null(host.Tab);
    }

    [AvaloniaFact]
    [Trait("Category", "Headless")]
    public void Headless_application_is_initialized()
    {
        Avalonia.Application? application = Avalonia.Application.Current;
        Assert.NotNull(application);
        Assert.NotNull(application!.Resources);
    }

    [AvaloniaFact]
    [Trait("Category", "Headless")]
    public void Theme_service_applies_available_variants()
    {
        var service = new AvaloniaThemeService();
        var window = new Window();
        window.Styles.Add(new StyleInclude((Uri?)null)
        {
            Source = new Uri("avares://HyperTerm/Styles/DesignSystem.axaml"),
        });
        window.Show();

        service.Apply("Default Light");
        Assert.Equal(Avalonia.Styling.ThemeVariant.Light, Avalonia.Application.Current!.RequestedThemeVariant);
        Assert.True(window.TryGetResource(
            "AppBackgroundBrush",
            Avalonia.Styling.ThemeVariant.Light,
            out object? lightBackground));
        Assert.Equal(
            Color.Parse("#F4F5F7"),
            Assert.IsType<SolidColorBrush>(lightBackground).Color);
        Assert.True(window.TryGetResource(
            "CardBackgroundBrush",
            Avalonia.Styling.ThemeVariant.Light,
            out object? lightCard));
        Assert.Equal(
            Color.Parse("#FFFFFF"),
            Assert.IsType<SolidColorBrush>(lightCard).Color);
        Assert.True(window.TryGetResource(
            "FocusRingBrush",
            Avalonia.Styling.ThemeVariant.Light,
            out object? lightFocusRing));
        Assert.Equal(
            Color.Parse("#3D7FAF"),
            Assert.IsType<SolidColorBrush>(lightFocusRing).Color);

        service.Apply("Darcula");
        Assert.Equal(
            ApplicationThemeVariants.Darcula,
            Avalonia.Application.Current.RequestedThemeVariant);
        Assert.True(window.TryGetResource(
            "AppBackgroundBrush",
            ApplicationThemeVariants.Darcula,
            out object? darculaBackground));
        Assert.Equal(
            Color.Parse("#2B2B2B"),
            Assert.IsType<SolidColorBrush>(darculaBackground).Color);
        Assert.True(window.TryGetResource(
            "BorderBrush",
            ApplicationThemeVariants.Darcula,
            out object? darculaBorder));
        SolidColorBrush darculaBorderBrush = Assert.IsType<SolidColorBrush>(darculaBorder);
        Assert.Equal(Color.Parse("#4B4D50"), darculaBorderBrush.Color);
        Assert.Equal(1, darculaBorderBrush.Opacity);
        Assert.True(window.TryGetResource(
            "CardBackgroundBrush",
            ApplicationThemeVariants.Darcula,
            out object? darculaCard));
        Assert.Equal(
            Color.Parse("#383A3C"),
            Assert.IsType<SolidColorBrush>(darculaCard).Color);
        Assert.True(window.TryGetResource(
            "DisabledForegroundBrush",
            ApplicationThemeVariants.Darcula,
            out object? darculaDisabled));
        Assert.Equal(
            Color.Parse("#737B82"),
            Assert.IsType<SolidColorBrush>(darculaDisabled).Color);

        service.Apply("Default Dark");
        Assert.Equal(Avalonia.Styling.ThemeVariant.Dark, Avalonia.Application.Current.RequestedThemeVariant);
        Assert.True(window.TryGetResource(
            "NavigationHoverBackgroundBrush",
            Avalonia.Styling.ThemeVariant.Dark,
            out object? darkNavigationHover));
        Assert.Equal(
            Color.Parse("#3A3A3D"),
            Assert.IsType<SolidColorBrush>(darkNavigationHover).Color);
        Assert.True(window.TryGetResource(
            "MenuFlyoutPresenterBackground",
            Avalonia.Styling.ThemeVariant.Dark,
            out object? darkMenuBackground));
        Assert.Equal(
            Color.Parse("#2D2D30"),
            Assert.IsType<SolidColorBrush>(darkMenuBackground).Color);
        Assert.True(window.TryGetResource(
            "MenuFlyoutPresenterBorderBrush",
            Avalonia.Styling.ThemeVariant.Dark,
            out object? darkMenuBorder));
        Assert.Equal(
            Color.Parse("#3C3C3C"),
            Assert.IsType<SolidColorBrush>(darkMenuBorder).Color);
        Assert.True(window.TryGetResource(
            "MenuFlyoutItemBackgroundPointerOver",
            Avalonia.Styling.ThemeVariant.Dark,
            out object? darkMenuHover));
        Assert.Equal(
            Color.Parse("#3A3A3D"),
            Assert.IsType<SolidColorBrush>(darkMenuHover).Color);
        window.Close();
    }

    [AvaloniaFact]
    [Trait("Category", "Headless")]
    public void Mintara_exposes_the_complete_semantic_palette()
    {
        var service = new AvaloniaThemeService();
        var window = new Window();
        window.Styles.Add(new StyleInclude((Uri?)null)
        {
            Source = new Uri("avares://HyperTerm/Styles/DesignSystem.axaml"),
        });
        window.Show();

        service.Apply("Mintara");

        Assert.Equal(
            ApplicationThemeVariants.Mintara,
            Avalonia.Application.Current!.RequestedThemeVariant);
        Dictionary<string, string> expectedColors = new()
        {
            ["AppBackgroundBrush"] = "#161B1A",
            ["SecondaryBackgroundBrush"] = "#1B211F",
            ["PanelBackgroundBrush"] = "#202624",
            ["CardBackgroundBrush"] = "#242B29",
            ["HeaderBackgroundBrush"] = "#1B211F",
            ["InputBackgroundBrush"] = "#181E1C",
            ["BorderBrush"] = "#343D3A",
            ["BorderHoverBrush"] = "#4A5753",
            ["BorderFocusBrush"] = "#70CFA9",
            ["HoverBackgroundBrush"] = "#29322F",
            ["NavigationHoverBackgroundBrush"] = "#29322F",
            ["PressedBackgroundBrush"] = "#303B37",
            ["SelectionBackgroundBrush"] = "#315C4D",
            ["SelectionForegroundBrush"] = "#E4F2EC",
            ["AccentBrush"] = "#70CFA9",
            ["AccentForegroundBrush"] = "#102019",
            ["AccentHoverBrush"] = "#82D9B6",
            ["AccentPressedBrush"] = "#5DBD98",
            ["PrimaryTextBrush"] = "#D7E0DC",
            ["MutedTextBrush"] = "#8F9D98",
            ["DisabledForegroundBrush"] = "#65706C",
            ["DisabledBackgroundBrush"] = "#202624",
            ["DangerBrush"] = "#E06C75",
            ["DangerBackgroundBrush"] = "#3B2428",
            ["DangerHoverBackgroundBrush"] = "#4A2B31",
            ["DangerPressedBackgroundBrush"] = "#E06C75",
            ["DangerForegroundBrush"] = "#161B1A",
            ["WarningBrush"] = "#D9B76E",
            ["SuccessBrush"] = "#70CFA9",
            ["OverlayBrush"] = "#B3000000",
            ["ScrollbarBrush"] = "#343D3A",
            ["ScrollbarHoverBrush"] = "#53615C",
            ["FocusRingBrush"] = "#82D9B6",
        };
        foreach ((string key, string color) in expectedColors)
        {
            Assert.True(window.TryGetResource(
                key,
                ApplicationThemeVariants.Mintara,
                out object? resource));
            Assert.Equal(
                Color.Parse(color),
                Assert.IsType<SolidColorBrush>(resource).Color);
        }

        window.Close();
    }

    [AvaloniaFact]
    [Trait("Category", "Headless")]
    public void Vesper_exposes_the_complete_semantic_palette()
    {
        var service = new AvaloniaThemeService();
        var window = new Window();
        window.Styles.Add(new StyleInclude((Uri?)null)
        {
            Source = new Uri("avares://HyperTerm/Styles/DesignSystem.axaml"),
        });
        window.Show();

        service.Apply("Vesper");

        Assert.Equal(
            ApplicationThemeVariants.Vesper,
            Avalonia.Application.Current!.RequestedThemeVariant);
        Dictionary<string, string> expectedColors = new()
        {
            ["AppBackgroundBrush"] = "#17151C",
            ["SecondaryBackgroundBrush"] = "#1D1A24",
            ["PanelBackgroundBrush"] = "#221E2A",
            ["CardBackgroundBrush"] = "#282330",
            ["HeaderBackgroundBrush"] = "#1D1A24",
            ["InputBackgroundBrush"] = "#1A171F",
            ["BorderBrush"] = "#393241",
            ["BorderHoverBrush"] = "#554A61",
            ["BorderFocusBrush"] = "#A277C7",
            ["HoverBackgroundBrush"] = "#302A39",
            ["NavigationHoverBackgroundBrush"] = "#302A39",
            ["PressedBackgroundBrush"] = "#393143",
            ["SelectionBackgroundBrush"] = "#493665",
            ["SelectionForegroundBrush"] = "#F1EAF7",
            ["AccentBrush"] = "#A277C7",
            ["AccentForegroundBrush"] = "#FFFFFF",
            ["AccentHoverBrush"] = "#B58AD7",
            ["AccentPressedBrush"] = "#8D63B3",
            ["PrimaryTextBrush"] = "#DDD7E3",
            ["MutedTextBrush"] = "#958D9E",
            ["DisabledForegroundBrush"] = "#69626F",
            ["DisabledBackgroundBrush"] = "#211D27",
            ["DangerBrush"] = "#DF707A",
            ["DangerBackgroundBrush"] = "#3C242B",
            ["DangerHoverBackgroundBrush"] = "#4B2A33",
            ["DangerPressedBackgroundBrush"] = "#DF707A",
            ["DangerForegroundBrush"] = "#17151C",
            ["WarningBrush"] = "#D7AE69",
            ["SuccessBrush"] = "#72B99A",
            ["OverlayBrush"] = "#B3000000",
            ["ScrollbarBrush"] = "#393241",
            ["ScrollbarHoverBrush"] = "#62566E",
            ["FocusRingBrush"] = "#B58AD7",
        };
        foreach ((string key, string color) in expectedColors)
        {
            Assert.True(window.TryGetResource(
                key,
                ApplicationThemeVariants.Vesper,
                out object? resource));
            Assert.Equal(
                Color.Parse(color),
                Assert.IsType<SolidColorBrush>(resource).Color);
        }

        window.Close();
    }

    [AvaloniaFact]
    [Trait("Category", "Headless")]
    public void Abyss_exposes_the_complete_semantic_palette()
    {
        var service = new AvaloniaThemeService();
        var window = new Window();
        window.Styles.Add(new StyleInclude((Uri?)null)
        {
            Source = new Uri("avares://HyperTerm/Styles/DesignSystem.axaml"),
        });
        window.Show();

        service.Apply("Abyss");

        Assert.Equal(
            ApplicationThemeVariants.Abyss,
            Avalonia.Application.Current!.RequestedThemeVariant);
        Dictionary<string, string> expectedColors = new()
        {
            ["AppBackgroundBrush"] = "#0D1117",
            ["SecondaryBackgroundBrush"] = "#111820",
            ["PanelBackgroundBrush"] = "#161D27",
            ["CardBackgroundBrush"] = "#1B2430",
            ["HeaderBackgroundBrush"] = "#111820",
            ["InputBackgroundBrush"] = "#0F151D",
            ["BorderBrush"] = "#293544",
            ["BorderHoverBrush"] = "#42546A",
            ["BorderFocusBrush"] = "#58A6FF",
            ["HoverBackgroundBrush"] = "#1D2936",
            ["NavigationHoverBackgroundBrush"] = "#1D2936",
            ["PressedBackgroundBrush"] = "#253547",
            ["SelectionBackgroundBrush"] = "#234A70",
            ["SelectionForegroundBrush"] = "#E6F2FF",
            ["AccentBrush"] = "#58A6FF",
            ["AccentForegroundBrush"] = "#08111C",
            ["AccentHoverBrush"] = "#79B8FF",
            ["AccentPressedBrush"] = "#388BFD",
            ["PrimaryTextBrush"] = "#D6E2EE",
            ["MutedTextBrush"] = "#8294A6",
            ["DisabledForegroundBrush"] = "#5D6B78",
            ["DisabledBackgroundBrush"] = "#151C25",
            ["DangerBrush"] = "#E06C75",
            ["DangerBackgroundBrush"] = "#37232A",
            ["DangerHoverBackgroundBrush"] = "#472A33",
            ["DangerPressedBackgroundBrush"] = "#E06C75",
            ["DangerForegroundBrush"] = "#0D1117",
            ["WarningBrush"] = "#D7B66F",
            ["SuccessBrush"] = "#65C89B",
            ["OverlayBrush"] = "#B3000000",
            ["ScrollbarBrush"] = "#293544",
            ["ScrollbarHoverBrush"] = "#4B6075",
            ["FocusRingBrush"] = "#79B8FF",
        };
        foreach ((string key, string color) in expectedColors)
        {
            Assert.True(window.TryGetResource(
                key,
                ApplicationThemeVariants.Abyss,
                out object? resource));
            Assert.Equal(
                Color.Parse(color),
                Assert.IsType<SolidColorBrush>(resource).Color);
        }

        window.Close();
    }

    [AvaloniaFact]
    [Trait("Category", "Headless")]
    public void Pointer_over_applies_hover_background_and_restores_normal_background()
    {
        Avalonia.Application.Current!.RequestedThemeVariant =
            ApplicationThemeVariants.Darcula;
        AddDesignSystemStyles();
        var button = new Button
        {
            Width = 120,
            Height = 30,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Content = "Hover",
        };
        var window = new Window
        {
            Width = 400,
            Height = 240,
            Content = button,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        ContentPresenter presenter = FindTemplatePart<ContentPresenter>(
            button,
            "PART_ContentPresenter");
        AssertBrushColor(button.Background, "#343537");
        AssertBrushColor(presenter.Background, "#343537");

        Point buttonPointerPosition = button.TranslatePoint(
            new Point(button.Bounds.Width / 2, button.Bounds.Height / 2),
            window)!.Value;
        window.MouseMove(buttonPointerPosition);
        Dispatcher.UIThread.RunJobs();
        Assert.True(button.IsPointerOver);
        AssertBrushColor(button.Background, "#404244");
        AssertBrushColor(presenter.Background, "#404244");

        window.MouseDown(buttonPointerPosition, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        AssertBrushColor(button.Background, "#47494C");
        AssertBrushColor(presenter.Background, "#47494C");

        window.MouseUp(buttonPointerPosition, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        AssertBrushColor(button.Background, "#404244");
        AssertBrushColor(presenter.Background, "#404244");

        window.MouseMove(new Avalonia.Point(1, 1));
        Dispatcher.UIThread.RunJobs();
        AssertBrushColor(button.Background, "#343537");
        AssertBrushColor(presenter.Background, "#343537");
        window.Close();
    }

    [AvaloniaFact]
    [Trait("Category", "Headless")]
    public void Settings_tab_hover_is_visible_only_while_hover_state_is_active()
    {
        Avalonia.Application.Current!.RequestedThemeVariant =
            ApplicationThemeVariants.Darcula;
        AddDesignSystemStyles();
        var first = new TabItem { Header = "General", Content = new Border() };
        var selected = new TabItem
        {
            Header = "Terminal",
            Content = new Border(),
            IsSelected = true,
        };
        first.Classes.Add("settingsTab");
        selected.Classes.Add("settingsTab");
        first.IsSelected = false;
        var tabHeaders = new StackPanel
        {
            Width = 400,
            Height = 240,
            Children = { first, selected },
        };
        var window = new Window
        {
            Width = 400,
            Height = 240,
            Content = tabHeaders,
        };
        window.Show();
        first.ApplyTemplate();
        selected.ApplyTemplate();
        Dispatcher.UIThread.RunJobs();
        Border firstLayoutRoot = FindTemplatePart<Border>(first, "PART_LayoutRoot");
        Border selectedLayoutRoot = FindTemplatePart<Border>(selected, "PART_LayoutRoot");
        AssertTransparent(first.Background);
        AssertTransparent(firstLayoutRoot.Background);
        AssertBrushColor(selectedLayoutRoot.Background, "#47494C");

        SetPseudoClass(first, ":pointerover", true);
        Dispatcher.UIThread.RunJobs();
        AssertBrushColor(first.Background, "#404244");
        AssertBrushColor(firstLayoutRoot.Background, "#404244");

        SetPseudoClass(selected, ":pointerover", true);
        Dispatcher.UIThread.RunJobs();
        AssertBrushColor(selectedLayoutRoot.Background, "#47494C");

        SetPseudoClass(first, ":pointerover", false);
        SetPseudoClass(selected, ":pointerover", false);
        Dispatcher.UIThread.RunJobs();
        AssertTransparent(first.Background);
        AssertTransparent(firstLayoutRoot.Background);
        AssertBrushColor(selectedLayoutRoot.Background, "#47494C");
        window.Close();
    }

    [AvaloniaFact]
    [Trait("Category", "Headless")]
    public void Selected_and_disabled_list_items_override_hover_and_pressed_states()
    {
        Avalonia.Application.Current!.RequestedThemeVariant =
            ApplicationThemeVariants.Darcula;
        AddDesignSystemStyles();
        var list = new ListBox
        {
            Width = 240,
            Height = 180,
            ItemsSource = new[] { "Normal", "Selected", "Disabled" },
            SelectedIndex = 1,
        };
        var window = new Window
        {
            Width = 400,
            Height = 240,
            Content = list,
        };
        window.Show();
        list.ApplyTemplate();
        Dispatcher.UIThread.RunJobs();
        ListBoxItem[] items = list.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
        Assert.Equal(3, items.Length);
        ListBoxItem normal = items[0];
        ListBoxItem selected = items[1];
        ListBoxItem disabled = items[2];
        disabled.IsEnabled = false;
        Dispatcher.UIThread.RunJobs();

        ContentPresenter normalPresenter = FindTemplatePart<ContentPresenter>(
            normal,
            "PART_ContentPresenter");
        ContentPresenter selectedPresenter = FindTemplatePart<ContentPresenter>(
            selected,
            "PART_ContentPresenter");
        ContentPresenter disabledPresenter = FindTemplatePart<ContentPresenter>(
            disabled,
            "PART_ContentPresenter");
        AssertTransparent(normalPresenter.Background);
        AssertBrushColor(selectedPresenter.Background, "#214283");
        AssertBrushColor(disabledPresenter.Background, "#303133");

        SetPseudoClass(normal, ":pointerover", true);
        SetPseudoClass(selected, ":pointerover", true);
        SetPseudoClass(disabled, ":pointerover", true);
        Dispatcher.UIThread.RunJobs();
        AssertBrushColor(normalPresenter.Background, "#404244");
        AssertBrushColor(selectedPresenter.Background, "#214283");
        AssertBrushColor(disabledPresenter.Background, "#303133");

        SetPseudoClass(normal, ":pressed", true);
        SetPseudoClass(selected, ":pressed", true);
        Dispatcher.UIThread.RunJobs();
        AssertBrushColor(normalPresenter.Background, "#47494C");
        AssertBrushColor(selectedPresenter.Background, "#214283");

        SetPseudoClass(normal, ":pressed", false);
        SetPseudoClass(selected, ":pressed", false);
        SetPseudoClass(normal, ":pointerover", false);
        SetPseudoClass(selected, ":pointerover", false);
        SetPseudoClass(disabled, ":pointerover", false);
        Dispatcher.UIThread.RunJobs();
        AssertTransparent(normalPresenter.Background);
        AssertBrushColor(selectedPresenter.Background, "#214283");
        AssertBrushColor(disabledPresenter.Background, "#303133");
        window.Close();
    }

    [AvaloniaFact]
    [Trait("Category", "Headless")]
    public void Dark_navigation_lists_and_menus_use_visible_hover()
    {
        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        AddDesignSystemStyles();
        var profileList = new ListBox
        {
            Width = 220,
            Height = 80,
            ItemsSource = new[] { "PowerShell" },
        };
        profileList.Classes.Add("profileList");
        var sessionsTree = new TreeView
        {
            Width = 220,
            Height = 80,
            ItemsSource = new[] { "SSH server" },
        };
        sessionsTree.Classes.Add("sessionsTree");
        var profileMenuItem = new MenuItem { Header = "PowerShell" };
        var window = new Window
        {
            Width = 300,
            Height = 300,
            Content = new StackPanel
            {
                Children = { profileList, sessionsTree, profileMenuItem },
            },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        ListBoxItem profileItem = Assert.Single(
            profileList.GetVisualDescendants().OfType<ListBoxItem>());
        ContentPresenter profilePresenter = FindTemplatePart<ContentPresenter>(
            profileItem,
            "PART_ContentPresenter");
        TreeViewItem sessionItem = Assert.Single(
            sessionsTree.GetVisualDescendants().OfType<TreeViewItem>());
        Border sessionRoot = FindTemplatePart<Border>(sessionItem, "PART_LayoutRoot");
        Border menuRoot = FindTemplatePart<Border>(profileMenuItem, "PART_LayoutRoot");

        SetPseudoClass(profileItem, ":pointerover", true);
        SetPseudoClass(sessionItem, ":pointerover", true);
        SetPseudoClass(sessionRoot, ":pointerover", true);
        profileMenuItem.IsSelected = true;
        Dispatcher.UIThread.RunJobs();

        AssertBrushColor(profilePresenter.Background, "#3A3A3D");
        AssertBrushColor(sessionRoot.Background, "#3A3A3D");
        AssertBrushColor(menuRoot.Background, "#3A3A3D");

        SetPseudoClass(profileItem, ":pointerover", false);
        SetPseudoClass(sessionItem, ":pointerover", false);
        SetPseudoClass(sessionRoot, ":pointerover", false);
        profileMenuItem.IsSelected = false;
        Dispatcher.UIThread.RunJobs();

        AssertTransparent(profilePresenter.Background);
        AssertTransparent(sessionRoot.Background);
        AssertTransparent(menuRoot.Background);
        window.Close();
    }

    [AvaloniaFact]
    [Trait("Category", "Headless")]
    public void Input_hover_changes_only_border_and_focus_remains_dominant()
    {
        Avalonia.Application.Current!.RequestedThemeVariant =
            ApplicationThemeVariants.Darcula;
        AddDesignSystemStyles();
        var textBox = new TextBox
        {
            Width = 220,
            Height = 32,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        var window = new Window
        {
            Width = 400,
            Height = 240,
            Content = textBox,
        };
        window.Show();
        textBox.ApplyTemplate();
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(textBox.Template);
        Border border = FindTemplatePart<Border>(textBox, "PART_BorderElement");
        AssertBrushColor(border.Background, "#292A2C");
        AssertBrushColor(border.BorderBrush, "#4B4D50");

        Point pointerPosition = textBox.TranslatePoint(
            new Point(textBox.Bounds.Width / 2, textBox.Bounds.Height / 2),
            window)!.Value;
        window.MouseMove(pointerPosition);
        Dispatcher.UIThread.RunJobs();
        AssertBrushColor(border.Background, "#292A2C");
        AssertBrushColor(border.BorderBrush, "#686B70");

        textBox.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.True(textBox.IsFocused);
        AssertBrushColor(border.Background, "#292A2C");
        AssertBrushColor(border.BorderBrush, "#6897BB");

        window.MouseMove(new Point(1, 1));
        Dispatcher.UIThread.RunJobs();
        AssertBrushColor(border.Background, "#292A2C");
        AssertBrushColor(border.BorderBrush, "#6897BB");
        window.Close();
    }

    [AvaloniaFact]
    [Trait("Category", "Headless")]
    public void Scrollbar_hover_changes_only_the_thumb_visual()
    {
        Avalonia.Application.Current!.RequestedThemeVariant =
            ApplicationThemeVariants.Darcula;
        AddDesignSystemStyles();
        var scrollBar = new ScrollBar
        {
            Width = 16,
            Height = 180,
            Minimum = 0,
            Maximum = 100,
            Orientation = Avalonia.Layout.Orientation.Vertical,
            Value = 20,
            ViewportSize = 10,
        };
        var window = new Window
        {
            Width = 200,
            Height = 240,
            Content = scrollBar,
        };
        window.Show();
        scrollBar.ApplyTemplate();
        scrollBar.Measure(new Size(16, 180));
        scrollBar.Arrange(new Rect(0, 0, 16, 180));
        Dispatcher.UIThread.RunJobs();
        Thumb thumb = Assert.Single(scrollBar.GetVisualDescendants().OfType<Thumb>());
        Border thumbBorder = Assert.Single(thumb.GetVisualDescendants().OfType<Border>());
        AssertBrushColor(thumbBorder.Background, "#515356");

        SetPseudoClass(thumb, ":pointerover", true);
        Dispatcher.UIThread.RunJobs();
        AssertBrushColor(thumbBorder.Background, "#707377");

        SetPseudoClass(thumb, ":pointerover", false);
        Dispatcher.UIThread.RunJobs();
        AssertBrushColor(thumbBorder.Background, "#515356");
        window.Close();
    }

    [AvaloniaFact]
    [Trait("Category", "Headless")]
    public void Button_variants_keep_their_own_hover_pressed_and_disabled_visuals()
    {
        Avalonia.Application.Current!.RequestedThemeVariant =
            ApplicationThemeVariants.Darcula;
        AddDesignSystemStyles();
        var accent = new Button { Content = "Accent" };
        var danger = new Button { Content = "Danger" };
        var titleBar = new Button { Content = "Title" };
        var tabClose = new Button { Content = "Close tab" };
        var disabled = new Button { Content = "Disabled", IsEnabled = false };
        accent.Classes.Add("accent");
        danger.Classes.Add("danger");
        titleBar.Classes.Add("titleBarButton");
        tabClose.Classes.Add("tabCloseButton");
        var window = new Window
        {
            Width = 500,
            Height = 300,
            Content = new StackPanel
            {
                Children = { accent, danger, titleBar, tabClose, disabled },
            },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        ContentPresenter accentPresenter = FindTemplatePart<ContentPresenter>(
            accent,
            "PART_ContentPresenter");
        ContentPresenter dangerPresenter = FindTemplatePart<ContentPresenter>(
            danger,
            "PART_ContentPresenter");
        ContentPresenter titlePresenter = FindTemplatePart<ContentPresenter>(
            titleBar,
            "PART_ContentPresenter");
        ContentPresenter tabClosePresenter = FindTemplatePart<ContentPresenter>(
            tabClose,
            "PART_ContentPresenter");
        ContentPresenter disabledPresenter = FindTemplatePart<ContentPresenter>(
            disabled,
            "PART_ContentPresenter");
        AssertBrushColor(accentPresenter.Background, "#6897BB");
        AssertBrushColor(dangerPresenter.Background, "#462C2C");
        AssertTransparent(titlePresenter.Background);
        AssertTransparent(tabClosePresenter.Background);
        AssertBrushColor(disabledPresenter.Background, "#303133");

        foreach (Button button in new[] { accent, danger, titleBar, tabClose, disabled })
        {
            SetPseudoClass(button, ":pointerover", true);
        }

        Dispatcher.UIThread.RunJobs();
        AssertBrushColor(accentPresenter.Background, "#78A6C8");
        AssertBrushColor(dangerPresenter.Background, "#543535");
        AssertBrushColor(titlePresenter.Background, "#404244");
        AssertTransparent(tabClosePresenter.Background);
        AssertBrushColor(disabledPresenter.Background, "#303133");

        foreach (Button button in new[] { accent, danger, titleBar, tabClose, disabled })
        {
            SetPseudoClass(button, ":pressed", true);
        }

        Dispatcher.UIThread.RunJobs();
        AssertBrushColor(accentPresenter.Background, "#5B87A8");
        AssertBrushColor(dangerPresenter.Background, "#D66666");
        AssertBrushColor(titlePresenter.Background, "#47494C");
        AssertTransparent(tabClosePresenter.Background);
        AssertBrushColor(disabledPresenter.Background, "#303133");

        foreach (Button button in new[] { accent, danger, titleBar, tabClose, disabled })
        {
            SetPseudoClass(button, ":pressed", false);
            SetPseudoClass(button, ":pointerover", false);
        }

        Dispatcher.UIThread.RunJobs();
        AssertBrushColor(accentPresenter.Background, "#6897BB");
        AssertBrushColor(dangerPresenter.Background, "#462C2C");
        AssertTransparent(titlePresenter.Background);
        AssertTransparent(tabClosePresenter.Background);
        AssertBrushColor(disabledPresenter.Background, "#303133");
        window.Close();
    }

    [AvaloniaFact]
    [Trait("Category", "Headless")]
    public void Combo_numeric_checkbox_and_menu_hover_target_their_fluent_parts()
    {
        Avalonia.Application.Current!.RequestedThemeVariant =
            ApplicationThemeVariants.Darcula;
        AddDesignSystemStyles();
        var comboBox = new ComboBox
        {
            Width = 220,
            ItemsSource = new[] { "One", "Two" },
            SelectedIndex = 0,
        };
        var numeric = new NumericUpDown { Width = 220, Value = 12 };
        var checkBox = new CheckBox { Content = "Enabled", IsChecked = true };
        var menuItem = new MenuItem { Header = "Open" };
        var disabledMenuItem = new MenuItem { Header = "Disabled", IsEnabled = false };
        var window = new Window
        {
            Width = 500,
            Height = 300,
            Content = new StackPanel
            {
                Children = { comboBox, numeric, checkBox, menuItem, disabledMenuItem },
            },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Border comboBackground = FindTemplatePart<Border>(comboBox, "Background");
        ButtonSpinner spinner = FindTemplatePart<ButtonSpinner>(numeric, "PART_Spinner");
        Border checkRectangle = FindTemplatePart<Border>(checkBox, "NormalRectangle");
        Border menuRoot = FindTemplatePart<Border>(menuItem, "PART_LayoutRoot");
        Border disabledMenuRoot = FindTemplatePart<Border>(
            disabledMenuItem,
            "PART_LayoutRoot");
        AssertBrushColor(comboBackground.Background, "#292A2C");
        AssertBrushColor(comboBackground.BorderBrush, "#4B4D50");
        AssertBrushColor(spinner.Background, "#292A2C");
        AssertBrushColor(spinner.BorderBrush, "#4B4D50");
        AssertBrushColor(checkRectangle.Background, "#6897BB");
        AssertTransparent(menuRoot.Background);
        AssertTransparent(disabledMenuRoot.Background);

        SetPseudoClass(comboBox, ":pointerover", true);
        SetPseudoClass(numeric, ":pointerover", true);
        SetPseudoClass(checkBox, ":pointerover", true);
        menuItem.IsSelected = true;
        disabledMenuItem.IsSelected = true;
        Dispatcher.UIThread.RunJobs();
        AssertBrushColor(comboBackground.Background, "#292A2C");
        AssertBrushColor(comboBackground.BorderBrush, "#686B70");
        AssertBrushColor(spinner.Background, "#292A2C");
        AssertBrushColor(spinner.BorderBrush, "#686B70");
        AssertBrushColor(checkRectangle.Background, "#78A6C8");
        AssertBrushColor(menuRoot.Background, "#404244");
        AssertTransparent(disabledMenuRoot.Background);

        SetPseudoClass(comboBox, ":pointerover", false);
        SetPseudoClass(numeric, ":pointerover", false);
        SetPseudoClass(checkBox, ":pointerover", false);
        menuItem.IsSelected = false;
        disabledMenuItem.IsSelected = false;
        Dispatcher.UIThread.RunJobs();
        AssertBrushColor(comboBackground.BorderBrush, "#4B4D50");
        AssertBrushColor(spinner.BorderBrush, "#4B4D50");
        AssertBrushColor(checkRectangle.Background, "#6897BB");
        AssertTransparent(menuRoot.Background);
        AssertTransparent(disabledMenuRoot.Background);
        window.Close();
    }

    [AvaloniaFact]
    [Trait("Category", "Headless")]
    public void Controls_measure_and_focus_on_headless_dispatcher()
    {
        var textBox = new TextBox { Text = "HyperTerm", MinWidth = 100 };
        var window = new Window
        {
            Width = 900,
            Height = 600,
            Content = textBox,
        };

        window.Show();
        textBox.Focus();

        Assert.True(textBox.IsFocused);
        Assert.Equal("HyperTerm", textBox.Text);
        window.Close();
    }

    [AvaloniaFact]
    [Trait("Category", "Headless")]
    public void Command_palette_focuses_query_after_becoming_visible()
    {
        var palette = new CommandPaletteDialog
        {
            IsVisible = false,
        };
        var window = new Window
        {
            Width = 900,
            Height = 600,
            Content = palette,
        };
        window.Show();

        palette.IsVisible = true;
        Dispatcher.UIThread.RunJobs();

        TextBox query = palette.FindControl<TextBox>("QueryEditor")!;
        Assert.True(query.IsFocused);
        window.Close();
    }

    [AvaloniaFact]
    [Trait("Category", "Headless")]
    public void Settings_use_vertical_tabs_with_independent_scrolling()
    {
        var dialog = new SettingsDialog { IsVisible = true };
        var window = new Window
        {
            Width = 1100,
            Height = 760,
            Content = dialog,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        TabControl tabs = dialog.FindControl<TabControl>("SettingsTabs")!;
        TabItem[] items = tabs.Items.OfType<TabItem>().ToArray();
        Assert.Equal(Dock.Left, tabs.TabStripPlacement);
        Assert.Equal(6, items.Length);
        Assert.Equal(
            ["General", "Themes", "Profiles", "Terminal", "Data", "Logs"],
            items.Select(item => item.Header).ToArray());
        Assert.All(items.Where((_, index) => index != 2), item =>
            Assert.IsType<ScrollViewer>(item.Content));
        Assert.IsType<Grid>(items[2].Content);
        Assert.All(items, item => Assert.Contains("settingsTab", item.Classes));
        ComboBox fontFamilyPicker = dialog.FindControl<ComboBox>("FontFamilyPicker")!;
        Assert.Equal(360, fontFamilyPicker.Width);
        Assert.False(fontFamilyPicker.IsEditable);
        Grid themeColumns = dialog.FindControl<Grid>("ThemeColumns")!;
        Assert.Equal(3, themeColumns.ColumnDefinitions.Count);
        Assert.Equal(new GridLength(1, GridUnitType.Star), themeColumns.ColumnDefinitions[0].Width);
        Assert.Equal(new GridLength(16), themeColumns.ColumnDefinitions[1].Width);
        Assert.Equal(new GridLength(1, GridUnitType.Star), themeColumns.ColumnDefinitions[2].Width);
        Assert.Contains("themePicker", dialog.FindControl<ListBox>("ThemePicker")!.Classes);
        Assert.Contains("themePicker", dialog.FindControl<ListBox>("LightThemePicker")!.Classes);
        Assert.Contains(
            "profileList",
            dialog.FindControl<ListBox>("TerminalProfileList")!.Classes);

        tabs.SelectedIndex = 2;
        Dispatcher.UIThread.RunJobs();
        Grid profileLayout = dialog.FindControl<Grid>("TerminalProfileLayout")!;
        Border profileListPanel = dialog.FindControl<Border>("TerminalProfileListPanel")!;
        Border profileEditorPanel = dialog.FindControl<Border>("TerminalProfileEditorPanel")!;
        Assert.Equal(new GridLength(220), profileLayout.ColumnDefinitions[0].Width);
        Assert.Equal(0, Grid.GetColumn(profileListPanel));
        Assert.Equal(2, Grid.GetColumn(profileEditorPanel));
        dialog.IsVisible = false;
        dialog.IsVisible = true;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, tabs.SelectedIndex);
        Assert.Same(items[2], tabs.SelectedItem);
        window.Close();
    }

    [AvaloniaFact]
    [Trait("Category", "Headless")]
    public void Session_manager_uses_master_detail_layout()
    {
        var dialog = new SessionManagerDialog { IsVisible = true };
        var window = new Window
        {
            Width = 1100,
            Height = 760,
            Content = dialog,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Grid layout = dialog.FindControl<Grid>("SessionManagerLayout")!;
        Border listPanel = dialog.FindControl<Border>("SessionManagerListPanel")!;
        Border editorPanel = dialog.FindControl<Border>("SessionManagerEditorPanel")!;
        ListBox sessionList = dialog.FindControl<ListBox>("SessionManagerList")!;
        Assert.Equal(new GridLength(280), layout.ColumnDefinitions[0].Width);
        Assert.Equal(0, Grid.GetColumn(listPanel));
        Assert.Equal(2, Grid.GetColumn(editorPanel));
        Assert.Contains("sessionManagerList", sessionList.Classes);
        window.Close();
    }

    [AvaloniaFact]
    [Trait("Category", "Headless")]
    public void Settings_tab_headers_keep_geometry_when_selection_changes()
    {
        var first = new TabItem { Header = "General", Content = new Border() };
        var second = new TabItem { Header = "Terminal", Content = new Border() };
        first.Classes.Add("settingsTab");
        second.Classes.Add("settingsTab");
        var tabHeaders = new StackPanel
        {
            Width = 600,
            Height = 400,
            Children = { first, second },
        };
        first.IsSelected = true;
        var window = new Window
        {
            Width = 600,
            Height = 400,
            Content = tabHeaders,
        };
        window.Styles.Add(new FluentTheme { DensityStyle = DensityStyle.Compact });
        window.Styles.Add(new StyleInclude((Uri?)null)
        {
            Source = new Uri("avares://HyperTerm/Styles/DesignSystem.axaml"),
        });
        window.Show();
        tabHeaders.Measure(new Avalonia.Size(600, 400));
        tabHeaders.Arrange(new Avalonia.Rect(0, 0, 600, 400));
        Dispatcher.UIThread.RunJobs();

        (double Y, double Height)[] initialGeometry =
        [
            (first.Bounds.Y, first.Bounds.Height),
            (second.Bounds.Y, second.Bounds.Height),
        ];
        Assert.All(initialGeometry, geometry => Assert.Equal(28, geometry.Height));

        first.IsSelected = false;
        second.IsSelected = true;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(initialGeometry[0], (first.Bounds.Y, first.Bounds.Height));
        Assert.Equal(initialGeometry[1], (second.Bounds.Y, second.Bounds.Height));
        window.Close();
    }

    private static void AddDesignSystemStyles()
    {
        Avalonia.Application application = Avalonia.Application.Current!;
        if (!application.Styles.OfType<FluentTheme>().Any())
        {
            application.Styles.Add(new FluentTheme { DensityStyle = DensityStyle.Compact });
        }

        if (application.Styles.OfType<StyleInclude>().Any(
                include => include.Source?.ToString() ==
                    "avares://HyperTerm/Styles/DesignSystem.axaml"))
        {
            return;
        }

        application.Styles.Add(new StyleInclude((Uri?)null)
        {
            Source = new Uri("avares://HyperTerm/Styles/DesignSystem.axaml"),
        });
    }

    [Theory]
    [InlineData(TerminalProfileIds.PowerShell, true)]
    [InlineData("custom-shell", false)]
    public void Terminal_profile_menu_header_contains_only_the_profile_name(
        string profileId,
        bool isDefault)
    {
        var profile = new TerminalProfile
        {
            Id = profileId,
            Name = "Profile name",
            ExecutablePath = "shell.exe",
        };
        var item = new TerminalLaunchProfileViewModel(profile, true, isDefault);

        Assert.Equal("Profile name", MainWindow.GetTerminalProfileMenuHeader(item));
    }

    private static T FindTemplatePart<T>(Control control, string name)
        where T : Control =>
        Assert.Single(
            control.GetVisualDescendants().OfType<T>(),
            candidate => candidate.Name == name);

    private static void AssertBrushColor(IBrush? brush, string expectedColor)
    {
        var solidBrush = Assert.IsAssignableFrom<ISolidColorBrush>(brush);
        Assert.Equal(Color.Parse(expectedColor), solidBrush.Color);
    }

    private static void AssertTransparent(IBrush? brush)
    {
        if (brush is null)
        {
            return;
        }

        Assert.Equal(
            Colors.Transparent,
            Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color);
    }

    private static void SetPseudoClass(
        StyledElement control,
        string pseudoClass,
        bool value)
    {
        PropertyInfo property = typeof(StyledElement).GetProperty(
            "PseudoClasses",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var pseudoClasses = Assert.IsAssignableFrom<IPseudoClasses>(property.GetValue(control));
        pseudoClasses.Set(pseudoClass, value);
    }
}

public sealed class TerminalOutputBufferTests
{
    [Fact]
    public void High_volume_output_drains_in_bounded_batches_without_loss()
    {
        const int maximumBufferSize = 2 * 1024 * 1024;
        const int maximumBatchSize = 128 * 1024;
        const int chunkSize = 1024;
        var buffer = new TerminalOutputBuffer(maximumBufferSize, maximumBatchSize);
        string chunk = new('x', chunkSize);
        for (int index = 0; index < maximumBufferSize / chunkSize; index++)
        {
            Assert.True(buffer.Enqueue(chunk));
        }

        int drainedCharacters = 0;
        string? batch;
        while ((batch = buffer.TryDrainBatch()) is not null)
        {
            Assert.InRange(batch.Length, 1, maximumBatchSize);
            Assert.All(batch, character => Assert.Equal('x', character));
            drainedCharacters += batch.Length;
        }

        Assert.Equal(maximumBufferSize, drainedCharacters);
    }

    [Fact]
    public void Constructor_rejects_non_positive_limits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TerminalOutputBuffer(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TerminalOutputBuffer(1, 0));
    }

    [Fact]
    public void Drain_preserves_order_and_batch_boundary()
    {
        var buffer = new TerminalOutputBuffer(100, 5);
        Assert.True(buffer.Enqueue("ab"));
        Assert.True(buffer.Enqueue("cd"));
        Assert.True(buffer.Enqueue("ef"));

        Assert.Equal("abcd", buffer.TryDrainBatch());
        Assert.Equal("ef", buffer.TryDrainBatch());
        Assert.Null(buffer.TryDrainBatch());
    }

    [Fact]
    public async Task Complete_rejects_late_output_and_unblocks_writer()
    {
        var buffer = new TerminalOutputBuffer(3, 3);
        Assert.True(buffer.Enqueue("abc"));
        Task<bool> blockedWriter = Task.Run(() => buffer.Enqueue("d"));
        Assert.False(blockedWriter.IsCompleted);

        buffer.Complete();

        Assert.False(await blockedWriter.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.False(buffer.Enqueue("late"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{\"type\":\"input\",\"tabId\":\"bad\",\"data\":\"x\"}")]
    [InlineData("{\"type\":\"resize\",\"tabId\":\"00000000000000000000000000000001\",\"columns\":0,\"rows\":24}")]
    public void Web_messages_reject_invalid_payloads(string? body) =>
        Assert.False(WebTerminalMessage.TryParse(body, out _));

    [Fact]
    public void Web_messages_preserve_raw_control_input()
    {
        const string body = "{\"type\":\"input\",\"tabId\":\"00000000000000000000000000000001\",\"data\":\"\\u0003\"}";

        Assert.True(WebTerminalMessage.TryParse(body, out WebTerminalMessage? message));
        Assert.Equal("\u0003", message!.Data);
    }
}
