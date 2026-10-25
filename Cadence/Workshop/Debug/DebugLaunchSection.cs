using Engine.Diagnostics;
using Microsoft.Xna.Framework;
using UIEngine.UI;
using UIEngine.UI.ColorPalettes;

namespace Workshop.Debug;

/// <summary>The Debug tab's Launch Settings section: the diagnostics features as a ToggleList under All, then the whole command line Play starts the game with and where the settings are saved, each read-only and copyable.</summary>
/// <remarks>Every change is the debugging's Options at once, saved for the next Workshop run and used by the next Play.</remarks>
public sealed class DebugLaunchSection(DebugTabServices services, WorkshopDebugging debugging)
{
    public const string WindowTitle = "Launch Settings";

    private static readonly (string Label, DiagnosticsFeatures Feature)[] Features =
    [
        ("Frame budget", DiagnosticsFeatures.FrameBudget),
        ("Memory", DiagnosticsFeatures.Memory),
        ("Leaks", DiagnosticsFeatures.LeakDetection),
        ("Startup", DiagnosticsFeatures.Startup),
        ("Gauges", DiagnosticsFeatures.Gauges),
    ];

    private const string AllLabel = "All";

    /// <summary>The clipboard icon, drawn as a glyph (the sprite manifest has no "Copy").</summary>
    private const string CopyGlyph = "📋";

    private const string CopySpriteName = "Copy";
    private const float ToggleListWidth = 240f;
    private const float CopyButtonWidth = 28f;

    private TextBox _commandLine = null!;
    private TextBox _optionsFile = null!;
    private Button _copyCommandLine = null!;
    private Button _copyOptionsFile = null!;

    public Window Window { get; private set; } = null!;

    /// <summary>The diagnostics features' checkboxes, under All.</summary>
    public ToggleList FeatureToggles { get; private set; } = null!;

    /// <summary>The command line as shown.</summary>
    public string CommandLineText => _commandLine.OriginalText;

    /// <summary>Where the settings are saved, as shown.</summary>
    public string OptionsFileText => _optionsFile.OriginalText;

    public TextBox CommandLineBox => _commandLine;

    public Button CopyCommandLineButton => _copyCommandLine;

    public Button CopyOptionsFileButton => _copyOptionsFile;

    /// <summary>feature's checkbox; DiagnosticsFeatures.All is the All checkbox.</summary>
    public Toggle ToggleFor(DiagnosticsFeatures feature) => feature == DiagnosticsFeatures.All
        ? FeatureToggles.AllToggle!
        : FeatureToggles.ItemToggle(Array.FindIndex(Features, entry => entry.Feature == feature));

    public void Open(Rectangle bounds)
    {
        Window = DebugTabElements.CreateWindow(services, bounds, WindowTitle);
        var width = Window.ContentSize.X;
        var y = 0f;
        DebugTabElements.CreateText(services, Window, new Vector2(0, y), new Vector2(width, DebugTabChrome.RowHeight), "Diagnostics features (--diagnostics=)");
        y += DebugTabChrome.RowHeight;

        var features = debugging.Options.DiagnosticsFeatures;
        var listHeight = ToggleList.HeightFor(Features.Length, hasAll: true, DebugTabChrome.RowHeight);
        FeatureToggles = services.Pool.CreateElement<ToggleList>(Window, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, y), Size = new Vector2(ToggleListWidth, listHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        FeatureToggles.Configure([.. Features.Select(entry => new ToggleListItem(entry.Label, features.HasFlag(entry.Feature)))], AllLabel, WindowPalette.BodyTextColor, DebugTabChrome.ToggleOn, WindowPalette.ControlBackground, DebugTabChrome.RowHeight);
        FeatureToggles.Changed += OnFeaturesChanged;
        Window.AddChild(FeatureToggles);
        y += listHeight + DebugTabChrome.RowGap;

        DebugTabElements.CreateText(services, Window, new Vector2(0, y), new Vector2(width, DebugTabChrome.RowHeight), "Play and Build and Play start the game with:");
        y += DebugTabChrome.RowHeight;
        (_commandLine, _copyCommandLine) = CreateCopyableRow(y, width, "the command line");
        y += DebugTabChrome.RowHeight + DebugTabChrome.RowGap;

        DebugTabElements.CreateText(services, Window, new Vector2(0, y), new Vector2(width, DebugTabChrome.RowHeight), "Launch settings are saved for you in:");
        y += DebugTabChrome.RowHeight;
        (_optionsFile, _copyOptionsFile) = CreateCopyableRow(y, width, "the settings file's path");
        _optionsFile.SetText(debugging.OptionsFilePath);
        ShowOptions();
    }

    public void Resize(Rectangle bounds)
    {
        DebugTabElements.Place(Window, bounds);
        var width = Window.ContentSize.X;
        PlaceCopyableRow(_commandLine, _copyCommandLine, width);
        PlaceCopyableRow(_optionsFile, _copyOptionsFile, width);
    }

    /// <summary>A read-only text box the width of the section less a copy button at its right, which copies the box's text.</summary>
    private (TextBox Box, Button CopyButton) CreateCopyableRow(float y, float width, string whatIsCopied)
    {
        var box = DebugTabElements.CreateReadOnlyBox(services, Window, new Vector2(0, y), new Vector2(TextBoxWidth(width), DebugTabChrome.RowHeight));

        var copyButton = services.Pool.CreateElement<Button>(Window, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(width - CopyButtonWidth, y), Size = new Vector2(CopyButtonWidth, DebugTabChrome.RowHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true },
            Content = new ElementContentOptions { ContentColor = WindowPalette.ControlBackground },
            Text = new TextOptions { Text = CopyGlyph, TextColor = WindowPalette.ControlLabelTextColor },
            Button = new ButtonOptions { SpriteName = CopySpriteName },
        });
        copyButton.Clicked += _ =>
        {
            services.CopyToClipboard(box.OriginalText);
            services.Status.Show($"Copied {whatIsCopied}.");
        };
        Window.AddChild(copyButton);
        return (box, copyButton);
    }

    private static void PlaceCopyableRow(TextBox box, Button copyButton, float width)
    {
        box.SetSize(new Vector2(TextBoxWidth(width), DebugTabChrome.RowHeight));
        copyButton.SetRelativePosition(new Vector2(width - CopyButtonWidth, copyButton.RelativePosition.Y));
    }

    private static float TextBoxWidth(float width) => System.Math.Max(40, width - CopyButtonWidth - DebugTabChrome.RowGap);

    private void OnFeaturesChanged()
    {
        var features = DiagnosticsFeatures.None;
        for (var index = 0; index < Features.Length; index++)
        {
            if (FeatureToggles.IsItemOn(index))
            {
                features |= Features[index].Feature;
            }
        }

        debugging.Options = new DebugLaunchOptions(features);
        ShowOptions();
    }

    private void ShowOptions()
    {
        var features = debugging.Options.DiagnosticsFeatures;
        FeatureToggles.SetItemStates([.. Features.Select(entry => features.HasFlag(entry.Feature))]);
        _commandLine.SetText(debugging.CommandLine);
    }
}
