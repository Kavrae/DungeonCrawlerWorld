using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.Bootstrap;
using UIEngine.Input;
using UIEngine.UI;
using UIEngine.UI.Chrome;
using UIEngine.UI.ColorPalettes;
using Workshop.Debug;

namespace Workshop;

/// <summary>The IDE's screen: a tab strip across the top with the shell's commands and a status line, the open tab's windows beneath it, and the bottom panel (Error List, Used By, Uses) along the bottom edge.</summary>
/// <remarks>
/// Every window is top-level in the shell's one UiLayerStack, updated and drawn by its UiLayerHost; the tab strip is in
/// the Base layer, and a tab adds its own windows when it opens and removes them when it closes. The shell registers
/// the framework's own element factories; a tab registers the factories of anything else it creates.
///
/// Commands sit at the strip's right edge -- Play and Build and Play (when the launch target can build) while no game or build runs, Stop while one does, then the host's own; a host's Save All and Publish All sit left of the Debug tab --
/// with the Debug tab, which the shell adds to every Workshop, left of them and the status line left of that. Tabs fill the rest from the left; tabs that don't fit move behind an overflow tile
/// that lists them. The game's captured output is drained every frame into the IDE's standard output and, when the host
/// gives one, a log file. Play tells the game where the shell's DiagnosticsLinkListener listens, and the status line
/// says what WorkshopDebugging makes of the launch and that link when it changes. The bottom panel is in the Base layer below the tab area, and dragging its top edge gives the
/// open tab the height it leaves.
/// </remarks>
public sealed class WorkshopShell : IWorkshopStatus, IDisposable
{
    private const float TabStripHeight = 30f;
    private const float TabHorizontalPadding = 12f;
    private const float StatusWidth = 420f;
    private const float OverflowTileWidth = 36f;
    private const string OverflowTileLabel = ">>";
    private const float InitialBottomPanelHeight = 180f;
    private const float MinimumTabAreaHeight = 120f;
    private const string UnsavedMarker = " *";
    private const string SaveAllTitle = "Save All";

    /// <summary>The gap between Publish All and the Debug tab: the one between a tab's own record buttons.</summary>
    private const float SaveCommandsGap = 4f;
    private const string PublishAllTitle = "Publish All";
    private const string UndoTitle = "Undo";
    private const string RedoTitle = "Redo";

    private static readonly Color SelectedTabColor = WindowPalette.ControlBackground;
    private static readonly Color SelectedTabTextColor = Color.White;

    private readonly UiEngineContext _uiEngine;
    private readonly IReadOnlyList<IIdeTab> _tabs;
    private readonly GameLauncher _gameLauncher;
    private readonly IReadOnlyList<WorkshopCommand> _commands;
    private readonly UiLayerStack _layers = new();
    private readonly ContextMenuController _contextMenuController;
    private readonly UiInputController _inputController;
    private readonly UiLayerHost _layerHost;
    private readonly SpriteFontBase _tabFont;
    private readonly List<TextWindow> _tabTiles = [];
    private readonly List<float> _tabTileWidths = [];
    private readonly List<(WorkshopCommand Command, TextWindow Tile, float Width)> _commandTiles = [];
    private readonly WorkshopTabContext[] _tabContexts;
    private readonly TabNavigator _tabNavigator;
    private readonly List<GameOutputLine> _drainedOutput = [];
    private readonly Engine.Diagnostics.RollingLogFile? _gameOutputLog;
    private readonly WorkshopBottomPanel _bottomPanel;
    private readonly WorkshopDebugging _debugging;
    private readonly IWorkshopEditing? _editing;

    /// <summary>The host's tabs come first in _tabs; the one after them is the pinned Debug tab.</summary>
    private readonly int _hostTabCount;

    private float _pinnedTabTileWidth;

    private Window _tabStrip = null!;
    private TextWindow _overflowTile = null!;
    private TextWindow _statusText = null!;
    private Vector2 _screenSize;
    private int _openTabIndex = -1;
    private int _visibleTabCount;
    private bool[] _shownCommandTiles = [];
    private IReadOnlyList<WorkshopProblem> _loadProblems = [];
    private LiveDiagnosticsSession? _shownLeaksSession;
    private int _shownLeaksReportVersion;
    private int _shownLaunchOptionsVersion = -1;
    private int _shownAddedProblemCount;
    private int _shownEditingProblemsVersion;
    private bool _shownPendingPublish;
    private int _shownPendingPublishVersion;
    private int _shownLoadProblemsVersion;
    private bool _isPromptOpen;
    private float _bottomPanelHeight = InitialBottomPanelHeight;

    /// <param name="tabSet">The host's tabs, laid out from the strip's left; the shell adds the Debug tab, pinned at the right beside the commands.</param>
    /// <param name="debugLaunchOptionsPath">Where the Debug tab keeps this user's launch options.</param>
    /// <param name="diagnosticsReportDirectories">The folders the Debug tab's Reports section lists.</param>
    /// <param name="browserFilters">Where every tab keeps its browser's checked filters; null keeps them for the run only.</param>
    public WorkshopShell(UiEngineContext uiEngine, WorkshopTabSet tabSet, GameLauncher gameLauncher, IReadOnlyList<WorkshopCommand> hostCommands, string? gameOutputLogPath, Vector2 screenSize, string debugLaunchOptionsPath, IReadOnlyList<string> diagnosticsReportDirectories, Browsing.BrowserFilterStore? browserFilters = null)
    {
        _uiEngine = uiEngine;
        _gameLauncher = gameLauncher;
        _debugging = new WorkshopDebugging(new DiagnosticsLinkListener(), new DebugLaunchOptionsStore(debugLaunchOptionsPath, DebugLaunchOptionsStore.DefaultFilePath), gameLauncher, diagnosticsReportDirectories);
        _debugging.Link.Start();
        _hostTabCount = tabSet.Tabs.Count;
        _tabs = [.. tabSet.Tabs, new DebugTab(uiEngine, _debugging)];
        var tabs = _tabs;
        _screenSize = screenSize;
        _editing = tabSet.Editing;
        _shownLoadProblemsVersion = _editing?.LoadProblemsVersion ?? 0;
        _commands = [.. SaveCommands(), .. PlayCommands(), .. hostCommands];
        _contextMenuController = new ContextMenuController(new DropdownController(uiEngine.ElementPoolService, uiEngine.RenderService));
        _tabFont = uiEngine.FontService.GetFont(FontChrome.DefaultFontSize);
        _gameOutputLog = OpenGameOutputLog(gameOutputLogPath);

        uiEngine.RegisterFrameworkElementFactories(_contextMenuController);
        _contextMenuController.Dropdowns.Initialize(_layers);
        var tooltipController = new TooltipController();
        tooltipController.Initialize(uiEngine.ElementPoolService, _layers);

        BuildTabStrip();

        IUiInputHandler[] commandHotkeys = [.. _commands.Select(command => new KeyPressInputHandler(command.Hotkey, () => command.Run(this), firesWhileTyping: true, command.Modifiers))];
        _inputController = new UiInputController(_layers, screenSize, new PointerState(), commandHotkeys, [], _contextMenuController.Dropdowns, tooltipController);
        _layerHost = new UiLayerHost(_layers, uiEngine.RenderService);
        _tabNavigator = new TabNavigator(tabs, OpenTab, this);
        _loadProblems = tabSet.LoadProblems;
        _bottomPanel = BuildBottomPanel(tabSet.LoadProblems);
        _tabContexts = [.. tabs.Select(_ => new WorkshopTabContext(uiEngine, _layers, _contextMenuController, this, _inputController, _tabNavigator, _bottomPanel) { BrowserFilters = browserFilters })];

        LayOutTabStrip();
        LayOutBottomPanel();
        OpenTab(0);
    }

    public string? OpenTabTitle => _openTabIndex >= 0 ? _tabs[_openTabIndex].Title : null;

    public void Update(GameTime gameTime)
    {
        _inputController.Update();
        if (_editing?.Update() is { } editingStatus)
        {
            Show(editingStatus);
        }

        _tabNavigator.ProcessPendingRequest();
        _debugging.Link.Update();
        ForwardGameOutput();
        _debugging.Update(this);
        ShowProblemsIfChanged();
        ShowPendingPublishIfChanged();
        ShowUnsavedMarkers();
        LayOutTabStripIfCommandsChanged();
        _tabs[_openTabIndex].Update(gameTime);
        _layerHost.Update(gameTime);
    }

    public void Draw(GameTime gameTime) => _layerHost.Draw(gameTime);

    /// <summary>Lays everything out again for a client area of screenSize -- the host calls this when the IDE's window is resized.</summary>
    public void Resize(Vector2 screenSize)
    {
        _screenSize = screenSize;
        _inputController.SetScreenSize(screenSize);
        _tabStrip.SetMaximumSize(new Vector2(screenSize.X, TabStripHeight));
        _tabStrip.SetSize(new Vector2(screenSize.X, TabStripHeight));
        LayOutTabStrip();
        LayOutBottomPanel();
        _tabs[_openTabIndex].Resize(TabArea());
        _layers.MeasureAndArrangeRoots();
    }

    /// <inheritdoc/>
    public void Show(string message) => _statusText.UpdateText(message);

    public void Dispose()
    {
        _gameOutputLog?.Dispose();
        _gameLauncher.Dispose();
        _debugging.Dispose();
    }

    private IEnumerable<WorkshopCommand> PlayCommands()
    {
        yield return new WorkshopCommand("Play", Keys.F5, KeyModifiers.None, status => status.Show(Launch(buildFirst: false)), IsShown: () => !_gameLauncher.IsGameRunning);

        if (_gameLauncher.CanBuild)
        {
            yield return new WorkshopCommand("Build and Play", Keys.F5, KeyModifiers.Control, status => status.Show(Launch(buildFirst: true)), IsShown: () => !_gameLauncher.IsGameRunning);
        }

        yield return new WorkshopCommand("Stop", Keys.F5, KeyModifiers.Shift, status => status.Show(Stop()), IsShown: () => _gameLauncher.IsGameRunning);
    }

    private IEnumerable<WorkshopCommand> SaveCommands()
    {
        if (_editing is { } editing)
        {
            yield return new WorkshopCommand(SaveAllTitle, Keys.S, KeyModifiers.Control, status => status.Show(editing.Save()));
            yield return new WorkshopCommand(PublishAllTitle, Keys.S, KeyModifiers.Control | KeyModifiers.Shift, status => status.Show(editing.Publish()));
            yield return new WorkshopCommand(UndoTitle, Keys.Z, KeyModifiers.Control, status => status.Show(editing.Undo(_tabNavigator)), IsShown: () => false);
            yield return new WorkshopCommand(RedoTitle, Keys.Y, KeyModifiers.Control, status => status.Show(editing.Redo(_tabNavigator)), IsShown: () => false);
        }
    }

    /// <summary>Exits through exit -- once the user has saved or discarded what is unsaved, when there is any (Save, Discard, Cancel).</summary>
    public void RequestClose(Action exit)
    {
        if (_editing is not { HasUnsavedChanges: true } editing)
        {
            exit();
            return;
        }

        AskAboutUnsavedChanges("Quit", "There are unsaved changes. Save them before quitting?",
        [
            new DialogButton("Save", () =>
            {
                var status = editing.Save();
                if (editing.HasUnsavedChanges)
                {
                    Show(status);
                    return;
                }

                exit();
            }),
            new DialogButton("Discard", exit),
            new DialogButton("Cancel", () => { }),
        ]);
    }

    /// <summary>Asks question over everything, once at a time; the last button is Cancel.</summary>
    private void AskAboutUnsavedChanges(string title, string question, IReadOnlyList<DialogButton> buttons)
    {
        if (_isPromptOpen)
        {
            return;
        }

        _isPromptOpen = true;
        ModalDialog.Show(_uiEngine.ElementPoolService, _layers, new Rectangle(0, 0, (int)_screenSize.X, (int)_screenSize.Y), new ModalDialogRequest(title,
            [.. buttons.Select(button => button with
            {
                OnChosen = () =>
                {
                    _isPromptOpen = false;
                    button.OnChosen();
                },
            })], CancelButtonIndex: buttons.Count - 1)
        {
            Message = question,
            BodySize = new Vector2(420, 70),
        });
    }

    /// <summary>Ends the game Play started, or its build.</summary>
    private string Stop()
    {
        _debugging.Stop(out var status);
        return status;
    }

    /// <summary>Starts the game, first asking whether to publish what isn't published -- the game reads only what is (Publish and Play, Play Anyway, Cancel).</summary>
    private string Launch(bool buildFirst)
    {
        if (_editing is not { HasUnpublishedChanges: true } editing)
        {
            return LaunchNow(buildFirst);
        }

        AskAboutUnsavedChanges(buildFirst ? "Build and Play" : "Play", "There are unpublished changes, and the game reads only what is published. Publish them first? Anything that fails is saved as a draft.",
        [
            new DialogButton("Publish and Play", () =>
            {
                var status = editing.Publish();
                Show(editing.HasUnsavedChanges ? status : LaunchNow(buildFirst));
            }),
            new DialogButton("Play Anyway", () => Show(LaunchNow(buildFirst))),
            new DialogButton("Cancel", () => { }),
        ]);
        return "Unpublished changes: publish them first?";
    }

    /// <summary>Starts the game told where the diagnostics link listens, with the Debug tab's launch options -- unless the game would refuse them.</summary>
    private string LaunchNow(bool buildFirst)
    {
        if (_debugging.LaunchRefusal is { } refusal)
        {
            return $"Not started. {refusal}";
        }

        if (_gameLauncher.TryLaunch(out var status, _debugging.LaunchArguments, buildFirst))
        {
            _debugging.NotifyGameLaunched();
        }

        return status;
    }

    /// <summary>Lists in To Publish every record the editing tabs would publish, whenever any of them changed -- never for a host that edits nothing.</summary>
    private void ShowPendingPublishIfChanged()
    {
        if (_editing is null)
        {
            return;
        }

        var version = EditingProblemsVersion();
        if (_shownPendingPublish && version == _shownPendingPublishVersion)
        {
            return;
        }

        _shownPendingPublish = true;
        _shownPendingPublishVersion = version;
        _bottomPanel.SetPendingPublish([.. _tabs.OfType<IWorkshopEditingTab>().SelectMany(tab => tab.PendingPublish)]);
    }

    /// <summary>Puts the launch options' problems and the connected game's latest leak findings in the Error List after the load problems, whichever tab is open; a new game's first report replaces the last game's.</summary>
    private void ShowProblemsIfChanged()
    {
        var session = _debugging.Link.Session;
        var reportVersion = session?.PeriodicReportVersion ?? 0;
        var launchOptionsVersion = _debugging.OptionsVersion;
        var editingVersion = EditingProblemsVersion();
        var loadProblemsVersion = _editing?.LoadProblemsVersion ?? 0;
        if (ReferenceEquals(session, _shownLeaksSession) && reportVersion == _shownLeaksReportVersion && launchOptionsVersion == _shownLaunchOptionsVersion && editingVersion == _shownEditingProblemsVersion && loadProblemsVersion == _shownLoadProblemsVersion)
        {
            return;
        }

        if (loadProblemsVersion != _shownLoadProblemsVersion && _editing is { } editing)
        {
            _shownLoadProblemsVersion = loadProblemsVersion;
            _loadProblems = editing.LoadProblems;
            _shownAddedProblemCount = -1;
        }

        _shownLeaksSession = session;
        _shownLeaksReportVersion = reportVersion;
        _shownLaunchOptionsVersion = launchOptionsVersion;
        _shownEditingProblemsVersion = editingVersion;
        var editingProblems = _tabs.OfType<IWorkshopEditingTab>().SelectMany(tab => tab.Problems).ToList();
        var launchProblems = DebugLaunchProblems.Of(_debugging.Problems);
        var leakProblems = DiagnosticsProblems.LeaksOf(session?.LatestPeriodicReport);
        var addedProblemCount = editingProblems.Count + launchProblems.Count + leakProblems.Count;
        if (addedProblemCount == 0 && _shownAddedProblemCount == 0)
        {
            return;
        }

        _shownAddedProblemCount = addedProblemCount;
        _bottomPanel.SetProblems([.. _loadProblems, .. editingProblems, .. launchProblems, .. leakProblems]);
    }

    private int EditingProblemsVersion()
    {
        var version = 0;
        foreach (var tab in _tabs)
        {
            if (tab is IWorkshopEditingTab editingTab)
            {
                version = unchecked(version * 31 + editingTab.ProblemsVersion);
            }
        }

        return version;
    }

    /// <summary>Marks each editing tab's tile with "*" while it holds unsaved changes.</summary>
    private void ShowUnsavedMarkers()
    {
        for (var tabIndex = 0; tabIndex < _hostTabCount; tabIndex++)
        {
            if (_tabs[tabIndex] is IWorkshopEditingTab editingTab)
            {
                _tabTiles[tabIndex].UpdateText(editingTab.HasUnsavedChanges ? editingTab.Title + UnsavedMarker : editingTab.Title);
            }
        }
    }

    private static Engine.Diagnostics.RollingLogFile? OpenGameOutputLog(string? path) =>
        path is null ? null : new Engine.Diagnostics.RollingLogFile(path);

    private void ForwardGameOutput()
    {
        if (_gameLauncher.DrainOutput(_drainedOutput) == 0)
        {
            return;
        }

        foreach (var line in _drainedOutput)
        {
            _debugging.ObserveGameOutput(line);
            (line.IsError ? Console.Error : Console.Out).WriteLine(line.Text);
            _gameOutputLog?.WriteLine(line.IsError ? $"[error] {line.Text}" : line.Text);
        }

        _drainedOutput.Clear();
    }

    private void BuildTabStrip()
    {
        var elementPool = _uiEngine.ElementPoolService;
        _tabStrip = elementPool.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = new Vector2(_screenSize.X, TabStripHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = WindowPalette.HeaderBackground },
        });
        _tabStrip.ContentPadding = Vector2.Zero;
        _tabStrip.Initialize();
        _layers.Add(UiLayer.Base, _tabStrip);

        for (var tabIndex = 0; tabIndex < _tabs.Count; tabIndex++)
        {
            var width = TileWidthFor(_tabs[tabIndex] is IWorkshopEditingTab ? _tabs[tabIndex].Title + UnsavedMarker : _tabs[tabIndex].Title);
            var tile = CreateStripTile(width, _tabs[tabIndex].Title);
            var capturedTabIndex = tabIndex;
            tile.Clicked += _ => OpenTab(capturedTabIndex);
            _tabTiles.Add(tile);
            if (tabIndex < _hostTabCount)
            {
                _tabTileWidths.Add(width);
            }
            else
            {
                _pinnedTabTileWidth = width;
            }
        }

        _overflowTile = CreateStripTile(OverflowTileWidth, OverflowTileLabel);
        _overflowTile.Clicked += _ => OpenOverflowMenu();

        foreach (var command in _commands)
        {
            var label = $"{command.Title} ({command.HotkeyText})";
            var width = TileWidthFor(label);
            var tile = CreateStripTile(width, label);
            tile.Clicked += _ => command.Run(this);
            _commandTiles.Add((command, tile, width));
        }

        _statusText = elementPool.CreateElement<TextWindow>(_tabStrip, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = new Vector2(StatusWidth, TabStripHeight), DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Text = new TextOptions { Text = string.Empty, TextColor = WindowPalette.TitleTextColor },
        });
        _statusText.ContentFont = _tabFont;
        _tabStrip.AddChild(_statusText);
    }

    private WorkshopBottomPanel BuildBottomPanel(IReadOnlyList<WorkshopProblem> loadProblems)
    {
        var elementPool = _uiEngine.ElementPoolService;
        elementPool.RegisterFactory(() => new WorkshopBottomPanel(_uiEngine.ElementServices));
        var panel = elementPool.CreateElement<WorkshopBottomPanel>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = new Vector2(_screenSize.X, _bottomPanelHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelContentColor },
        });
        panel.Configure(_tabNavigator, _contextMenuController);
        panel.Initialize();
        panel.SetProblems(loadProblems);
        panel.HeightDragged += OnBottomPanelHeightDragged;
        _layers.Add(UiLayer.Base, panel);
        return panel;
    }

    private void OnBottomPanelHeightDragged(float height)
    {
        _bottomPanelHeight = height;
        LayOutBottomPanel();
        _tabs[_openTabIndex].Resize(TabArea());
    }

    /// <summary>Along the bottom edge, never so tall that the tab area is shorter than MinimumTabAreaHeight.</summary>
    private void LayOutBottomPanel()
    {
        var maximumHeight = System.Math.Max(WorkshopBottomPanel.MinimumHeight, _screenSize.Y - TabStripHeight - MinimumTabAreaHeight);
        _bottomPanelHeight = System.Math.Clamp(_bottomPanelHeight, WorkshopBottomPanel.MinimumHeight, maximumHeight);
        _bottomPanel.SetMaximumSize(_screenSize);
        _bottomPanel.SetBounds(new Vector2(0, _screenSize.Y - _bottomPanelHeight), new Vector2(_screenSize.X, _bottomPanelHeight));
    }

    private float TileWidthFor(string label) => _tabFont.MeasureString(label).X + TabHorizontalPadding * 2;

    private TextWindow CreateStripTile(float width, string label)
    {
        var size = new Vector2(width, TabStripHeight);
        var tile = _uiEngine.ElementPoolService.CreateElement<TextWindow>(_tabStrip, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = size, MinimumSize = size, MaximumSize = size, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, BorderStyle = BorderStyle.Inset, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelContentColor },
            Text = new TextOptions { Text = label, TextColor = WindowPalette.BodyTextColor },
        });
        tile.ContentFont = _tabFont;
        _tabStrip.AddChild(tile);
        return tile;
    }

    /// <summary>Lays the strip out again when a command's tile has come or gone (Stop, while a game runs).</summary>
    private void LayOutTabStripIfCommandsChanged()
    {
        for (var index = 0; index < _commandTiles.Count; index++)
        {
            if (_commandTiles[index].Command.ShowsTile != _shownCommandTiles[index])
            {
                LayOutTabStrip();
                return;
            }
        }
    }

    /// <summary>Places the play and host commands shown from the right edge, the Debug tab left of them, the save commands left of that (Save All, Publish All, Debug, Play, Build and Play), the status line left of those, and as many of the host's tabs as fit from the left, the rest behind the overflow tile.</summary>
    private void LayOutTabStrip()
    {
        var right = _screenSize.X;
        _shownCommandTiles = new bool[_commandTiles.Count];
        right = PlaceCommandTiles(right, beforeDebugTab: false);
        right -= _pinnedTabTileWidth;
        _tabTiles[_hostTabCount].SetRelativePosition(new Vector2(right, 0));
        right = PlaceCommandTiles(right - SaveCommandsGap, beforeDebugTab: true);

        var statusWidth = System.Math.Max(0, System.Math.Min(StatusWidth, right / 2));
        right -= statusWidth;
        _statusText.SetSize(new Vector2(statusWidth, TabStripHeight));
        _statusText.SetRelativePosition(new Vector2(right, 0));

        _visibleTabCount = TabStripLayout.VisibleTabCount(_tabTileWidths, right, OverflowTileWidth);
        var x = 0f;
        for (var tabIndex = 0; tabIndex < _hostTabCount; tabIndex++)
        {
            var isVisible = tabIndex < _visibleTabCount;
            _tabTiles[tabIndex].IsVisible = isVisible;
            if (isVisible)
            {
                _tabTiles[tabIndex].SetRelativePosition(new Vector2(x, 0));
                x += _tabTileWidths[tabIndex];
            }
        }

        _overflowTile.IsVisible = _visibleTabCount < _hostTabCount;
        _overflowTile.SetRelativePosition(new Vector2(x, 0));
        ShowOpenTabSelected();
    }

    /// <summary>Places the shown command tiles that go left of the Debug tab (beforeDebugTab) or right of it, right to left ending at right, in their order; returns where they end.</summary>
    private float PlaceCommandTiles(float right, bool beforeDebugTab)
    {
        for (var index = _commandTiles.Count - 1; index >= 0; index--)
        {
            var (command, tile, width) = _commandTiles[index];
            if (IsSaveCommand(command) != beforeDebugTab)
            {
                continue;
            }

            var isShown = command.ShowsTile;
            _shownCommandTiles[index] = isShown;
            tile.IsVisible = isShown;
            if (isShown)
            {
                right -= width;
                tile.SetRelativePosition(new Vector2(right, 0));
            }
        }

        return right;
    }

    private static bool IsSaveCommand(WorkshopCommand command) => command.Title is SaveAllTitle or PublishAllTitle;

    /// <summary>Lists every tab the strip has no room for; choosing one opens it.</summary>
    private void OpenOverflowMenu()
    {
        var options = new List<DropdownOption>();
        for (var tabIndex = _visibleTabCount; tabIndex < _hostTabCount; tabIndex++)
        {
            var capturedTabIndex = tabIndex;
            options.Add(new DropdownOption(_tabs[tabIndex].Title, null, Enabled: tabIndex != _openTabIndex, () => OpenTab(capturedTabIndex)));
        }

        var anchor = _overflowTile.AbsolutePosition + new Vector2(0, TabStripHeight);
        _contextMenuController.Open(anchor, options);
    }

    private void OpenTab(int tabIndex)
    {
        if (tabIndex == _openTabIndex)
        {
            return;
        }

        if (_openTabIndex >= 0)
        {
            _tabs[_openTabIndex].Close();
            _tabContexts[_openTabIndex].RemoveAllInputHandlers();
        }

        _openTabIndex = tabIndex;
        _bottomPanel.ClearRelatedRecords(_tabs[tabIndex].ShowsRelatedRecords);
        _tabs[tabIndex].Open(_tabContexts[tabIndex], TabArea());
        ShowOpenTabSelected();
    }

    /// <summary>Marks the open tab's tile, or the overflow tile when the open tab is behind it.</summary>
    private void ShowOpenTabSelected()
    {
        for (var index = 0; index < _tabTiles.Count; index++)
        {
            StyleTile(_tabTiles[index], isSelected: index == _openTabIndex);
        }

        StyleTile(_overflowTile, isSelected: _openTabIndex >= _visibleTabCount && _openTabIndex < _hostTabCount);
    }

    private static void StyleTile(TextWindow tile, bool isSelected)
    {
        tile.BorderStyle = isSelected ? BorderStyle.Outset : BorderStyle.Inset;
        tile.SetContentColor(isSelected ? SelectedTabColor : WindowPalette.PanelContentColor);
        tile.TextColor = isSelected ? SelectedTabTextColor : WindowPalette.BodyTextColor;
    }

    private Rectangle TabArea() => new(0, (int)TabStripHeight, (int)_screenSize.X, (int)(_screenSize.Y - TabStripHeight - _bottomPanelHeight));
}
