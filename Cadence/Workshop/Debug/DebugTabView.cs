using Microsoft.Xna.Framework;
using UIEngine.UI;

namespace Workshop.Debug;

/// <summary>The sections the Debug tab switches between.</summary>
public enum DebugSection
{
    Launch,
    Live,
    Reports,
}

/// <summary>The Debug tab's windows: a switch along the top, and the section it shows -- Launch Settings, Live or Reports -- under it.</summary>
/// <remarks>Every section is built when the tab opens; the switch shows one and hides the rest, so the Live section keeps its selection and freeze while another is shown. The section shown is kept across the tab closing and opening.</remarks>
public sealed class DebugTabView(DebugTabServices services, WorkshopDebugging debugging)
{
    private readonly List<(DebugSection Section, TextWindow Tile)> _tiles = [];
    private Window _switch = null!;

    public DebugSection Section { get; private set; } = DebugSection.Launch;

    public DebugLaunchSection Launch { get; } = new(services, debugging);

    public DebugLiveSection Live { get; } = new(services);

    public DebugReportsSection Reports { get; } = new(services, debugging);

    /// <summary>section's tile on the switch.</summary>
    public TextWindow TileFor(DebugSection section) => _tiles.First(entry => entry.Section == section).Tile;

    public void Open(Rectangle area)
    {
        var (switchBounds, sectionBounds) = Rows(area);
        _switch = services.Pool.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(switchBounds.X, switchBounds.Y), Size = new Vector2(switchBounds.Width, switchBounds.Height), MaximumSize = new Vector2(4000, 4000), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        _switch.ContentPadding = Vector2.Zero;
        _switch.Initialize();
        services.Layers.Add(UiLayer.Base, _switch);

        _tiles.Clear();
        var x = 0f;
        foreach (var section in Enum.GetValues<DebugSection>())
        {
            _tiles.Add((section, CreateTile(section, x)));
            x += DebugTabChrome.SectionTileWidth;
        }

        Launch.Open(sectionBounds);
        Live.Open(sectionBounds);
        Reports.Open(sectionBounds);
        ShowSection(Section);
        debugging.GameLaunched += ShowLiveOnLaunch;
    }

    /// <summary>A launch from Launch Settings goes on to show what the game reports.</summary>
    private void ShowLiveOnLaunch()
    {
        if (Section == DebugSection.Launch)
        {
            ShowSection(DebugSection.Live);
        }
    }

    public void Resize(Rectangle area)
    {
        var (switchBounds, sectionBounds) = Rows(area);
        DebugTabElements.Place(_switch, switchBounds);
        Launch.Resize(sectionBounds);
        Live.Resize(sectionBounds);
        Reports.Resize(sectionBounds);
    }

    /// <summary>Updates the section shown: Live's view of the link's session, or Reports. UI thread, once per frame.</summary>
    public void Update()
    {
        if (Section == DebugSection.Live)
        {
            Live.Update(debugging.Link.Session);
        }
        else if (Section == DebugSection.Reports)
        {
            Reports.Update();
        }
    }

    public void ShowSection(DebugSection section)
    {
        Section = section;
        Launch.Window.IsVisible = section == DebugSection.Launch;
        Live.Window.IsVisible = section == DebugSection.Live;
        Reports.SetVisible(section == DebugSection.Reports);
        foreach (var (tileSection, tile) in _tiles)
        {
            DebugTabElements.StyleTile(tile, isSelected: tileSection == section);
        }

        if (section == DebugSection.Live)
        {
            Live.Update(debugging.Link.Session);
        }
    }

    public void Close()
    {
        debugging.GameLaunched -= ShowLiveOnLaunch;
        DebugTabElements.Close(services, _switch);
        DebugTabElements.Close(services, Launch.Window);
        DebugTabElements.Close(services, Live.Window);
        Reports.Close();
    }

    private TextWindow CreateTile(DebugSection section, float x) =>
        DebugTabElements.CreateTile(services, _switch, new Vector2(x, 0), DebugTabChrome.SectionTileWidth, LabelOf(section), () => ShowSection(section));

    /// <summary>section's name on its tile -- the title of its window.</summary>
    private static string LabelOf(DebugSection section) => section switch
    {
        DebugSection.Launch => DebugLaunchSection.WindowTitle,
        _ => section.ToString(),
    };

    private static (Rectangle Switch, Rectangle Section) Rows(Rectangle area)
    {
        var left = (int)(area.X + DebugTabChrome.Margin);
        var width = (int)System.Math.Max(100, area.Width - DebugTabChrome.Margin * 2);
        var switchTop = (int)(area.Y + DebugTabChrome.Margin);
        var sectionTop = (int)(switchTop + DebugTabChrome.RowHeight + DebugTabChrome.RowGap);
        return (
            new Rectangle(left, switchTop, width, (int)DebugTabChrome.RowHeight),
            new Rectangle(left, sectionTop, width, (int)System.Math.Max(100, area.Bottom - DebugTabChrome.Margin - sectionTop)));
    }
}
