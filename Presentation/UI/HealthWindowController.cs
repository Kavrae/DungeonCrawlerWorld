using Engine.ECS.Components;
using Game.World;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.Chrome;
using Presentation.UI.ColorPalettes;

namespace Presentation.UI;

/// <summary>
/// Owns the Health button and the HealthWindow it opens/closes -- the original template for the
/// single-button-per-window shape InventoryWindowController and AbilityScoreWindowController now
/// both follow too (Folders have been proven out and removed from both -- see
/// InventoryWindowController's own doc comment). A plain Button, not a Folder -- there's only
/// ever one thing to open, so there's nothing to expand into.
/// </summary>
public sealed class HealthWindowController(
    ElementPoolService elementPoolService,
    World world,
    ComponentManager componentManager,
    FontService fontService,
    LabelRenderer labelRenderer)
{
    /// <summary>♥ (U+2665, "black heart suit") -- renders via the default DroidSans font with no Symbola-Emoji fallback needed, unlike Burning/Poison/Paralysis's own emoji glyphs (see FontService).</summary>
    private const string HeartGlyph = "♥";

    private Button _button = null!;
    private WindowLifecycle<HealthWindow> _slot = null!;

    public void Initialize(UiLayerStack layers)
    {
        _slot = new WindowLifecycle<HealthWindow>(CreateHealthWindow, () => false, layers, () => { });

        _button = elementPoolService.CreateElement<Button>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = HealthWindowChrome.ButtonPosition, Size = HealthWindowChrome.ButtonSize, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, BorderStyle = BorderStyle.Outset, CanUserFocus = false },
            Text = new TextOptions { Text = HeartGlyph, TextColor = WindowPalette.HeartGlyphColor },
        });
        _button.Initialize();
        _button.Clicked += _ => _slot.Toggle();
        layers.Add(UiLayer.DynamicHud, _button);

        // Same reasoning as InventoryWindowController's own button -- opening the Health
        // window from this button while another menu window is already open is a normal part of
        // the workflow menu mode exists to support, not something it should block (see
        // UiLayerStack.MarkMenuModeExempt's own doc comment).
        layers.MarkMenuModeExempt(_button);
    }

    private HealthWindow CreateHealthWindow()
    {
        var window = elementPoolService.CreateElement<HealthWindow>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions
            {
                RelativePosition = HealthWindowChrome.WindowPosition,
                Size = HealthWindowChrome.WindowSize,
                DisplayMode = ElementDisplayMode.Fixed,
            },
            Chrome = new ElementChromeOptions
            {
                ShowTitle = true,
                ShowBorder = true,
                CanUserClose = true,
                CanUserMinimize = false,
                CanUserMove = true,
                CanUserResize = true,
                CanUserFocus = true,
            },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelBackgroundColor },
        });
        window.Configure(world.PlayerEntityId);
        return window;
    }
}
