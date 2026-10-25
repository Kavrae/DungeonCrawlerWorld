using Engine.Diagnostics;
using Microsoft.Xna.Framework;
using UIEngine.UI;
using UIEngine.UI.ColorPalettes;

namespace Workshop.Debug;

/// <summary>The Debug tab's Launch Settings section: the diagnostics features under All, the seed, the benchmark range and headless, extra arguments, what the game would refuse, then the whole command line Play starts the game with and where the settings are saved.</summary>
/// <remarks>
/// Every change is the debugging's Options at once, saved for the next Workshop run and used by the next Play. A switched-off
/// option keeps its value: the seed field is disabled while Random is ticked, and Headless is disabled until Benchmark frames
/// is. End is kept after Start. Use last seed copies the seed the last connected game reported and unticks Random.
/// </remarks>
public sealed class DebugLaunchSection(DebugTabServices services, WorkshopDebugging debugging)
{
    public const string WindowTitle = "Launch Settings";

    /// <summary>What the headless note says while a run would be headless.</summary>
    public const string HeadlessNote = "Headless runs report Memory and Gauges only; the other features are left off.";

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
    private const float RandomToggleWidth = 90f;
    private const float SeedFieldWidth = 130f;
    private const float UseLastSeedWidth = 120f;
    private const float BenchmarkToggleWidth = 150f;
    private const float FrameLabelWidth = 40f;
    private const float FrameFieldWidth = 100f;
    private const float HeadlessToggleWidth = 100f;
    private const double LargestFrame = 100_000_000;
    private const int ProblemLines = 2;

    private TextBox _commandLine = null!;
    private TextBox _optionsFile = null!;
    private Button _copyCommandLine = null!;
    private Button _copyOptionsFile = null!;
    private TextWindow _headlessNote = null!;
    private TextBox _extraArguments = null!;
    private TextWindow _problems = null!;

    public Window Window { get; private set; } = null!;

    /// <summary>The diagnostics features' checkboxes, under All.</summary>
    public ToggleList FeatureToggles { get; private set; } = null!;

    public Toggle RandomSeedToggle { get; private set; } = null!;

    public NumericField SeedField { get; private set; } = null!;

    public Button UseLastSeedButton { get; private set; } = null!;

    public Toggle BenchmarkToggle { get; private set; } = null!;

    public NumericField StartFrameField { get; private set; } = null!;

    public NumericField EndFrameField { get; private set; } = null!;

    public Toggle HeadlessToggle { get; private set; } = null!;

    public TextBox ExtraArgumentsBox => _extraArguments;

    /// <summary>The headless note as shown; empty unless a run would be headless.</summary>
    public string HeadlessNoteText => _headlessNote.OriginalText;

    /// <summary>The options' problems as shown, one a line.</summary>
    public string ProblemsText => _problems.OriginalText;

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
        var options = debugging.Options;
        var y = 0f;
        CreateHeading(ref y, width, $"Diagnostics features ({DiagnosticsFeaturesParser.ArgumentPrefix})");

        var listHeight = ToggleList.HeightFor(Features.Length, hasAll: true, DebugTabChrome.RowHeight);
        FeatureToggles = services.Pool.CreateElement<ToggleList>(Window, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, y), Size = new Vector2(ToggleListWidth, listHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        FeatureToggles.Configure([.. Features.Select(entry => new ToggleListItem(entry.Label, options.DiagnosticsFeatures.HasFlag(entry.Feature)))], AllLabel, WindowPalette.BodyTextColor, DebugTabChrome.ToggleOn, WindowPalette.ControlBackground, DebugTabChrome.RowHeight);
        FeatureToggles.Changed += OnFeaturesChanged;
        Window.AddChild(FeatureToggles);
        y += listHeight + DebugTabChrome.RowGap;

        CreateHeading(ref y, width, $"Seed ({Engine.Math.RandomSeed.ArgumentPrefix})");
        var x = 0f;
        RandomSeedToggle = CreateToggle(ref x, y, RandomToggleWidth, "Random", options.UsesRandomSeed, OnRandomSeedToggled);
        SeedField = CreateNumericField(ref x, y, SeedFieldWidth);
        SeedField.Configure(options.Seed, int.MinValue, int.MaxValue);
        SeedField.ValueChanged += value => Change(debugging.Options with { Seed = (int)value });
        UseLastSeedButton = CreateButton(ref x, y, UseLastSeedWidth, "Use last seed", OnUseLastSeed);
        y += DebugTabChrome.RowHeight + DebugTabChrome.RowGap;

        CreateHeading(ref y, width, $"Benchmark ({BenchmarkFrameRange.ArgumentPrefix}START-END, {HeadlessArgument.Argument})");
        x = 0f;
        BenchmarkToggle = CreateToggle(ref x, y, BenchmarkToggleWidth, "Benchmark frames", options.RunsBenchmark, isOn => Change(debugging.Options with { RunsBenchmark = isOn }));
        CreateLabel(ref x, y, FrameLabelWidth, "Start");
        StartFrameField = CreateNumericField(ref x, y, FrameFieldWidth);
        StartFrameField.Configure(options.BenchmarkFrames.StartFrame, 0, LargestFrame - 1, step: 100);
        StartFrameField.ValueChanged += OnFramesChanged;
        CreateLabel(ref x, y, FrameLabelWidth, "End");
        EndFrameField = CreateNumericField(ref x, y, FrameFieldWidth);
        EndFrameField.Configure(options.BenchmarkFrames.EndFrame, 1, LargestFrame, step: 100);
        EndFrameField.ValueChanged += OnFramesChanged;
        HeadlessToggle = CreateToggle(ref x, y, HeadlessToggleWidth, "Headless", options.Headless, isOn => Change(debugging.Options with { Headless = isOn }));
        y += DebugTabChrome.RowHeight;
        _headlessNote = DebugTabElements.CreateText(services, Window, new Vector2(0, y), new Vector2(width, DebugTabChrome.RowHeight), string.Empty);
        y += DebugTabChrome.RowHeight + DebugTabChrome.RowGap;

        CreateHeading(ref y, width, "Extra arguments, added after the rest as typed:");
        _extraArguments = services.Pool.CreateElement<TextBox>(Window, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, y), Size = new Vector2(width, DebugTabChrome.RowHeight), MaximumSize = new Vector2(4000, DebugTabChrome.RowHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true },
            Content = new ElementContentOptions { ContentColor = Color.White },
            Text = new TextOptions { TextColor = WindowPalette.BodyTextColor },
        });
        _extraArguments.GhostText = "--setting=Module.Setting=Value ...";
        Window.AddChild(_extraArguments);
        _extraArguments.SetText(options.ExtraArguments);
        _extraArguments.TextChanged += _ => CommitExtraArguments();
        y += DebugTabChrome.RowHeight + DebugTabChrome.RowGap;

        _problems = DebugTabElements.CreateText(services, Window, new Vector2(0, y), new Vector2(width, DebugTabChrome.RowHeight * ProblemLines), string.Empty);
        _problems.TextColor = DebugTabChrome.WarningText;
        y += DebugTabChrome.RowHeight * ProblemLines + DebugTabChrome.RowGap;

        CreateHeading(ref y, width, "Play and Build and Play start the game with:");
        (_commandLine, _copyCommandLine) = CreateCopyableRow(y, width, "the command line");
        y += DebugTabChrome.RowHeight + DebugTabChrome.RowGap;

        CreateHeading(ref y, width, "Launch settings are saved for you in:");
        (_optionsFile, _copyOptionsFile) = CreateCopyableRow(y, width, "the settings file's path");
        _optionsFile.SetText(debugging.OptionsFilePath);
        ShowOptions();
        Update();
    }

    public void Resize(Rectangle bounds)
    {
        DebugTabElements.Place(Window, bounds);
        var width = Window.ContentSize.X;
        _extraArguments.SetSize(new Vector2(width, DebugTabChrome.RowHeight));
        _headlessNote.SetSize(new Vector2(width, DebugTabChrome.RowHeight));
        _problems.SetSize(new Vector2(width, DebugTabChrome.RowHeight * ProblemLines));
        PlaceCopyableRow(_commandLine, _copyCommandLine, width);
        PlaceCopyableRow(_optionsFile, _copyOptionsFile, width);
    }

    /// <summary>Enables Use last seed while the last connected game reported a seed. UI thread, once per frame while the section is shown.</summary>
    public void Update() => UseLastSeedButton.Enabled = LastReportedSeed is not null;

    private int? LastReportedSeed => debugging.Link.Session?.Hello?.RandomSeed;

    private void CreateHeading(ref float y, float width, string text)
    {
        DebugTabElements.CreateText(services, Window, new Vector2(0, y), new Vector2(width, DebugTabChrome.RowHeight), text);
        y += DebugTabChrome.RowHeight;
    }

    private void CreateLabel(ref float x, float y, float width, string text)
    {
        DebugTabElements.CreateText(services, Window, new Vector2(x, y), new Vector2(width, DebugTabChrome.RowHeight), text);
        x += width;
    }

    private Toggle CreateToggle(ref float x, float y, float width, string label, bool isOn, Action<bool> onToggled)
    {
        var toggle = services.Pool.CreateElement<Toggle>(Window, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(x, y), Size = new Vector2(width, DebugTabChrome.RowHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        toggle.Configure(label, LabelPosition.East, isOn, DebugTabChrome.ToggleOn, WindowPalette.ControlBackground, WindowPalette.BodyTextColor, services.Fonts.GetFont(UIEngine.UI.Chrome.FontChrome.DefaultFontSize), onToggled);
        Window.AddChild(toggle);
        x += width + DebugTabChrome.RowGap;
        return toggle;
    }

    private NumericField CreateNumericField(ref float x, float y, float width)
    {
        var field = DebugTabElements.CreateChild<NumericField>(services, Window, new Vector2(x, y), new Vector2(width, DebugTabChrome.RowHeight), showBorder: true);
        field.SetContentColor(Color.White);
        x += width + DebugTabChrome.RowGap;
        return field;
    }

    private Button CreateButton(ref float x, float y, float width, string label, Action onClicked)
    {
        var button = services.Pool.CreateElement<Button>(Window, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(x, y), Size = new Vector2(width, DebugTabChrome.RowHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true },
            Content = new ElementContentOptions { ContentColor = WindowPalette.ControlBackground },
            Text = new TextOptions { Text = label, TextColor = WindowPalette.ControlLabelTextColor },
        });
        button.Clicked += _ => onClicked();
        Window.AddChild(button);
        x += width + DebugTabChrome.RowGap;
        return button;
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

        Change(debugging.Options with { DiagnosticsFeatures = features });
    }

    private void OnRandomSeedToggled(bool isOn)
    {
        SeedField.CommitTypedText();
        Change(debugging.Options with { UsesRandomSeed = isOn, Seed = (int)SeedField.Value });
    }

    private void OnUseLastSeed()
    {
        if (LastReportedSeed is not { } seed)
        {
            return;
        }

        Change(debugging.Options with { UsesRandomSeed = false, Seed = seed });
        services.Status.Show($"Seed set to {seed}, the last run's.");
    }

    /// <summary>Takes Start and End as the range, End moved to Start + 1 when it isn't after Start.</summary>
    private void OnFramesChanged(double _)
    {
        var startFrame = (long)StartFrameField.Value;
        var endFrame = System.Math.Max((long)EndFrameField.Value, startFrame + 1);
        Change(debugging.Options with { BenchmarkFrames = new BenchmarkFrameRange(startFrame, endFrame) });
    }

    private void CommitExtraArguments()
    {
        if (_extraArguments.OriginalText != debugging.Options.ExtraArguments)
        {
            Change(debugging.Options with { ExtraArguments = _extraArguments.OriginalText });
        }
    }

    private void Change(DebugLaunchOptions options)
    {
        debugging.Options = options;
        ShowOptions();
    }

    private void ShowOptions()
    {
        var options = debugging.Options;
        FeatureToggles.SetItemStates([.. Features.Select(entry => options.DiagnosticsFeatures.HasFlag(entry.Feature))]);
        RandomSeedToggle.SetIsOn(options.UsesRandomSeed);
        SeedField.SetValue(options.Seed);
        SeedField.Enabled = !options.UsesRandomSeed;
        BenchmarkToggle.SetIsOn(options.RunsBenchmark);
        StartFrameField.SetValue(options.BenchmarkFrames.StartFrame);
        EndFrameField.SetValue(options.BenchmarkFrames.EndFrame);
        HeadlessToggle.SetIsOn(options.Headless);
        HeadlessToggle.Enabled = options.RunsBenchmark;
        _headlessNote.UpdateText(options.RunsHeadless ? HeadlessNote : string.Empty);
        _problems.UpdateText(string.Join('\n', debugging.Problems.Select(static problem => problem.Message)));
        _commandLine.SetText(debugging.CommandLine);
    }
}
