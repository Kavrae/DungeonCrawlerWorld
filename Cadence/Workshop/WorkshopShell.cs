using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.Bootstrap;
using UIEngine.Input;
using UIEngine.UI;
using UIEngine.UI.Chrome;
using UIEngine.UI.ColorPalettes;

namespace Workshop;

/// <summary>The IDE's screen: a tab strip across the top with Play and a status line, and the open tab's windows beneath it.</summary>
/// <remarks>
/// Every window is top-level in the shell's one UiLayerStack, updated and drawn by its UiLayerHost; the tab strip is in
/// the Base layer, and a tab adds its own windows when it opens and removes them when it closes. The shell registers
/// the framework's own element factories; a tab registers the factories of anything else it creates.
/// </remarks>
public sealed class WorkshopShell
{
    private const float TabStripHeight = 30f;
    private const float TabHorizontalPadding = 12f;
    private const float PlayTileWidth = 90f;
    private const float StatusWidth = 420f;

    private static readonly Color SelectedTabColor = WindowPalette.ControlBackground;
    private static readonly Color SelectedTabTextColor = Color.White;

    private readonly UiEngineContext _uiEngine;
    private readonly IReadOnlyList<IIdeTab> _tabs;
    private readonly GameLauncher _gameLauncher;
    private readonly UiLayerStack _layers = new();
    private readonly ContextMenuController _contextMenuController;
    private readonly WorkshopTabContext _tabContext;
    private readonly UiInputController _inputController;
    private readonly UiLayerHost _layerHost;
    private readonly SpriteFontBase _tabFont;
    private readonly List<TextWindow> _tabTiles = [];

    private Window _tabStrip = null!;
    private TextWindow _playTile = null!;
    private TextWindow _statusText = null!;
    private Vector2 _screenSize;
    private int _openTabIndex = -1;

    /// <summary>Whether the status line last said a game is running -- the only time Update asks the launcher, so the line changes when that game exits.</summary>
    private bool _statusShowsGameRunning;

    public WorkshopShell(UiEngineContext uiEngine, IReadOnlyList<IIdeTab> tabs, GameLauncher gameLauncher, Vector2 screenSize)
    {
        if (tabs.Count == 0)
        {
            throw new ArgumentException("The IDE needs at least one tab.", nameof(tabs));
        }

        _uiEngine = uiEngine;
        _tabs = tabs;
        _gameLauncher = gameLauncher;
        _screenSize = screenSize;
        _contextMenuController = new ContextMenuController(uiEngine.ElementPoolService);
        _tabContext = new WorkshopTabContext(uiEngine, _layers, _contextMenuController);
        _tabFont = uiEngine.FontService.GetFont(FontChrome.DefaultFontSize);

        RegisterFrameworkElementFactories();
        _contextMenuController.Initialize(_layers);

        BuildTabStrip();

        IUiInputHandler[] inputHandlers = [new KeyPressInputHandler(Keys.F5, Play)];
        _inputController = new UiInputController(_layers, screenSize, new PointerState(), inputHandlers, [], _contextMenuController);
        _layerHost = new UiLayerHost(_layers, uiEngine.ElementPoolService);

        OpenTab(0);
    }

    public string? OpenTabTitle => _openTabIndex >= 0 ? _tabs[_openTabIndex].Title : null;

    public void Update(GameTime gameTime)
    {
        _inputController.Update();
        UpdateGameClosedStatus();
        _tabs[_openTabIndex].Update(gameTime);
        _layerHost.Update(gameTime);
    }

    public void Draw(GameTime gameTime) => _layerHost.Draw(gameTime);

    /// <summary>Lays everything out again for a client area of screenSize -- the host calls this when the IDE's window is resized.</summary>
    public void Resize(Vector2 screenSize)
    {
        _screenSize = screenSize;
        _inputController.SetScreenSize(screenSize);
        _tabStrip.SetSize(new Vector2(screenSize.X, TabStripHeight));
        _playTile.SetRelativePosition(PlayTilePosition());
        _statusText.SetRelativePosition(StatusPosition());
        _tabs[_openTabIndex].Resize(TabArea());
    }

    private void RegisterFrameworkElementFactories()
    {
        var elementPool = _uiEngine.ElementPoolService;
        var fontService = _uiEngine.FontService;
        var labelRenderer = _uiEngine.LabelRenderer;
        elementPool.RegisterFactory(() => new Window(fontService, elementPool, labelRenderer));
        elementPool.RegisterFactory(() => new TextWindow(fontService, elementPool, labelRenderer));
        elementPool.RegisterFactory(() => new Button(fontService, elementPool, labelRenderer, _uiEngine.SpriteSheetService));
        elementPool.RegisterFactory(() => new TextBox(fontService, elementPool, labelRenderer, contextMenuController: _contextMenuController));
        elementPool.RegisterFactory(() => new ContextMenu(fontService, elementPool, labelRenderer));
        elementPool.RegisterFactory(() => new Tooltip(fontService, elementPool, labelRenderer));
        elementPool.RegisterFactory(() => new TextDivider(fontService, elementPool, labelRenderer));
        elementPool.RegisterFactory(() => new SeparatorBar(fontService, elementPool, labelRenderer));
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

        var x = 0f;
        for (var tabIndex = 0; tabIndex < _tabs.Count; tabIndex++)
        {
            var width = _tabFont.MeasureString(_tabs[tabIndex].Title).X + TabHorizontalPadding * 2;
            var tile = CreateStripTile(new Vector2(x, 0), new Vector2(width, TabStripHeight), _tabs[tabIndex].Title);
            var capturedTabIndex = tabIndex;
            tile.Clicked += _ => OpenTab(capturedTabIndex);
            _tabTiles.Add(tile);
            x += width;
        }

        _playTile = CreateStripTile(PlayTilePosition(), new Vector2(PlayTileWidth, TabStripHeight), "Play (F5)");
        _playTile.Clicked += _ => Play();

        _statusText = _uiEngine.ElementPoolService.CreateElement<TextWindow>(_tabStrip, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = StatusPosition(), Size = new Vector2(StatusWidth, TabStripHeight), DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Text = new TextOptions { Text = string.Empty, TextColor = WindowPalette.TitleTextColor },
        });
        _statusText.ContentFont = _tabFont;
        _tabStrip.AddChild(_statusText);
    }

    private TextWindow CreateStripTile(Vector2 relativePosition, Vector2 size, string label)
    {
        var tile = _uiEngine.ElementPoolService.CreateElement<TextWindow>(_tabStrip, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = relativePosition, Size = size, MinimumSize = size, MaximumSize = size, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, BorderStyle = BorderStyle.Inset, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelContentColor },
            Text = new TextOptions { Text = label, TextColor = WindowPalette.BodyTextColor },
        });
        tile.ContentFont = _tabFont;
        _tabStrip.AddChild(tile);
        return tile;
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
        }

        _openTabIndex = tabIndex;
        _tabs[tabIndex].Open(_tabContext, TabArea());

        for (var index = 0; index < _tabTiles.Count; index++)
        {
            var isOpen = index == tabIndex;
            _tabTiles[index].BorderStyle = isOpen ? BorderStyle.Outset : BorderStyle.Inset;
            _tabTiles[index].SetContentColor(isOpen ? SelectedTabColor : WindowPalette.PanelContentColor);
            _tabTiles[index].TextColor = isOpen ? SelectedTabTextColor : WindowPalette.BodyTextColor;
        }
    }

    private void Play()
    {
        _gameLauncher.TryLaunch(out var status);
        _statusText.UpdateText(status);
        _statusShowsGameRunning = _gameLauncher.IsGameRunning;
    }

    private void UpdateGameClosedStatus()
    {
        if (!_statusShowsGameRunning || _gameLauncher.IsGameRunning)
        {
            return;
        }

        _statusShowsGameRunning = false;
        _statusText.UpdateText(GameLauncher.GameClosedStatus);
    }

    private Rectangle TabArea() => new(0, (int)TabStripHeight, (int)_screenSize.X, (int)(_screenSize.Y - TabStripHeight));

    private Vector2 PlayTilePosition() => new(_screenSize.X - PlayTileWidth, 0);

    private Vector2 StatusPosition() => new(_screenSize.X - PlayTileWidth - StatusWidth, 0);
}
