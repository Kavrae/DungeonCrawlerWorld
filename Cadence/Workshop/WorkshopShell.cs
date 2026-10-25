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

/// <summary>The IDE's screen: a tab strip across the top with the shell's commands and a status line, the open tab's windows beneath it, and the bottom panel (Error List, Dependents) along the bottom edge.</summary>
/// <remarks>
/// Every window is top-level in the shell's one UiLayerStack, updated and drawn by its UiLayerHost; the tab strip is in
/// the Base layer, and a tab adds its own windows when it opens and removes them when it closes. The shell registers
/// the framework's own element factories; a tab registers the factories of anything else it creates.
///
/// Commands sit at the strip's right edge -- Play, Build and Play when the launch target can build, then the host's own --
/// with the Debug tab, which the shell adds to every Workshop, left of them and the status line left of that. Tabs fill the rest from the left; tabs that don't fit move behind an overflow tile
/// that lists them. The game's captured output is drained every frame into the IDE's standard output and, when the host
/// gives one, a log file. Play tells the game where the shell's DiagnosticsLinkListener listens, and the status line
/// shows that link's connection when it changes. The bottom panel is in the Base layer below the tab area, and dragging its top edge gives the
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
    private readonly StreamWriter? _gameOutputLog;
    private readonly WorkshopBottomPanel _bottomPanel;
    private readonly WorkshopDebugging _debugging;

    /// <summary>The host's tabs come first in _tabs; the one after them is the pinned Debug tab.</summary>
    private readonly int _hostTabCount;

    private float _pinnedTabTileWidth;

    private Window _tabStrip = null!;
    private TextWindow _overflowTile = null!;
    private TextWindow _statusText = null!;
    private Vector2 _screenSize;
    private int _openTabIndex = -1;
    private int _visibleTabCount;
    private int _shownLauncherStatusVersion;
    private LiveDiagnosticsSession? _shownDiagnosticsSession;
    private int _shownDiagnosticsStatusVersion;
    private IReadOnlyList<WorkshopProblem> _loadProblems = [];
    private LiveDiagnosticsSession? _shownLeaksSession;
    private int _shownLeaksReportVersion;
    private int _shownLeakProblemCount;
    private float _bottomPanelHeight = InitialBottomPanelHeight;

    /// <param name="tabSet">The host's tabs, laid out from the strip's left; the shell adds the Debug tab, pinned at the right beside the commands.</param>
    /// <param name="debugLaunchOptionsPath">Where the Debug tab keeps this user's launch options.</param>
    /// <param name="diagnosticsReportDirectories">The folders the Debug tab's Reports section lists.</param>
    public WorkshopShell(UiEngineContext uiEngine, WorkshopTabSet tabSet, GameLauncher gameLauncher, IReadOnlyList<WorkshopCommand> hostCommands, string? gameOutputLogPath, Vector2 screenSize, string debugLaunchOptionsPath, IReadOnlyList<string> diagnosticsReportDirectories)
    {
        _uiEngine = uiEngine;
        _gameLauncher = gameLauncher;
        _debugging = new WorkshopDebugging(new DiagnosticsLinkListener(), new DebugLaunchOptionsStore(debugLaunchOptionsPath), gameLauncher, diagnosticsReportDirectories);
        _debugging.Link.Start();
        _hostTabCount = tabSet.Tabs.Count;
        _tabs = [.. tabSet.Tabs, new DebugTab(uiEngine, _debugging)];
        var tabs = _tabs;
        _screenSize = screenSize;
        _commands = [.. PlayCommands(), .. hostCommands];
        _contextMenuController = new ContextMenuController(new DropdownController(uiEngine.ElementPoolService, uiEngine.RenderService));
        _tabFont = uiEngine.FontService.GetFont(FontChrome.DefaultFontSize);
        _gameOutputLog = OpenGameOutputLog(gameOutputLogPath);

        uiEngine.RegisterFrameworkElementFactories(_contextMenuController);
        _contextMenuController.Dropdowns.Initialize(_layers);

        BuildTabStrip();

        IUiInputHandler[] commandHotkeys = [.. _commands.Select(command => new KeyPressInputHandler(command.Hotkey, () => command.Run(this), firesWhileTyping: true, command.Modifiers))];
        _inputController = new UiInputController(_layers, screenSize, new PointerState(), commandHotkeys, [], _contextMenuController.Dropdowns);
        _layerHost = new UiLayerHost(_layers, uiEngine.RenderService);
        _tabNavigator = new TabNavigator(tabs, OpenTab, this);
        _loadProblems = tabSet.LoadProblems;
        _bottomPanel = BuildBottomPanel(tabSet.LoadProblems);
        _tabContexts = [.. tabs.Select(_ => new WorkshopTabContext(uiEngine, _layers, _contextMenuController, this, _inputController, _tabNavigator, _bottomPanel))];

        LayOutTabStrip();
        LayOutBottomPanel();
        OpenTab(0);
    }

    public string? OpenTabTitle => _openTabIndex >= 0 ? _tabs[_openTabIndex].Title : null;

    public void Update(GameTime gameTime)
    {
        _inputController.Update();
        _tabNavigator.ProcessPendingRequest();
        ShowLauncherStatusIfChanged();
        _debugging.Link.Update();
        ShowDiagnosticsStatusIfChanged();
        ShowLeakProblemsIfChanged();
        ForwardGameOutput();
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
        yield return new WorkshopCommand("Play", Keys.F5, KeyModifiers.None, status => status.Show(Launch(buildFirst: false)));

        if (_gameLauncher.CanBuild)
        {
            yield return new WorkshopCommand("Build and Play", Keys.F5, KeyModifiers.Control, status => status.Show(Launch(buildFirst: true)));
        }
    }

    /// <summary>Starts the game told where the diagnostics link listens, with the Debug tab's launch options.</summary>
    private string Launch(bool buildFirst)
    {
        if (_gameLauncher.TryLaunch(out var status, _debugging.LaunchArguments, buildFirst))
        {
            _debugging.NotifyGameLaunched();
        }

        return status;
    }

    private void ShowDiagnosticsStatusIfChanged()
    {
        if (_debugging.Link.Session is not { } session)
        {
            return;
        }

        if (ReferenceEquals(session, _shownDiagnosticsSession) && session.StatusVersion == _shownDiagnosticsStatusVersion)
        {
            return;
        }

        _shownDiagnosticsSession = session;
        _shownDiagnosticsStatusVersion = session.StatusVersion;
        Show(session.StatusText);
    }

    /// <summary>Puts the connected game's latest leak findings in the Error List after the load problems, whichever tab is open; a new game's first report replaces the last game's.</summary>
    private void ShowLeakProblemsIfChanged()
    {
        var session = _debugging.Link.Session;
        var reportVersion = session?.PeriodicReportVersion ?? 0;
        if (ReferenceEquals(session, _shownLeaksSession) && reportVersion == _shownLeaksReportVersion)
        {
            return;
        }

        _shownLeaksSession = session;
        _shownLeaksReportVersion = reportVersion;
        var leakProblems = DiagnosticsProblems.LeaksOf(session?.LatestPeriodicReport);
        if (leakProblems.Count == 0 && _shownLeakProblemCount == 0)
        {
            return;
        }

        _shownLeakProblemCount = leakProblems.Count;
        _bottomPanel.SetProblems([.. _loadProblems, .. leakProblems]);
    }

    private static StreamWriter? OpenGameOutputLog(string? path)
    {
        if (path is null)
        {
            return null;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        return new StreamWriter(path, append: true) { AutoFlush = true };
    }

    private void ShowLauncherStatusIfChanged()
    {
        var version = _gameLauncher.StatusVersion;
        if (version == _shownLauncherStatusVersion)
        {
            return;
        }

        _shownLauncherStatusVersion = version;
        Show(_gameLauncher.Status);
    }

    private void ForwardGameOutput()
    {
        if (_gameLauncher.DrainOutput(_drainedOutput) == 0)
        {
            return;
        }

        foreach (var line in _drainedOutput)
        {
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
            var width = TileWidthFor(_tabs[tabIndex].Title);
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
        elementPool.RegisterFactory(() => new WorkshopBottomPanel(_uiEngine.FontService, elementPool, _uiEngine.RenderService));
        var panel = elementPool.CreateElement<WorkshopBottomPanel>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = new Vector2(_screenSize.X, _bottomPanelHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelContentColor },
        });
        panel.Configure(_tabNavigator);
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

    /// <summary>Places the commands from the right edge, the Debug tab left of them, the status line left of that, and as many of the host's tabs as fit from the left, the rest behind the overflow tile.</summary>
    private void LayOutTabStrip()
    {
        var right = _screenSize.X;
        for (var index = _commandTiles.Count - 1; index >= 0; index--)
        {
            right -= _commandTiles[index].Width;
            _commandTiles[index].Tile.SetRelativePosition(new Vector2(right, 0));
        }

        right -= _pinnedTabTileWidth;
        _tabTiles[_hostTabCount].SetRelativePosition(new Vector2(right, 0));

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
        _bottomPanel.ClearDependents(_tabs[tabIndex].ShowsDependents);
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
