using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI;

namespace Tests.Presentation;

/// <summary>
/// Covers Tooltip's SetRows capability (see its own doc comment) directly through SetRows +
/// UpdateText rather than ShowNear -- ShowNear's own SetRelativePosition call reads
/// ElementPoolService.GraphicsDevice.Viewport.Bounds, unavailable headlessly (GraphicsDevice is only
/// ever wired up by the real render loop's Initialize call, never by these tests), so nothing in
/// this codebase drives ShowNear directly today. UpdateText alone -- what ShowNear calls internally
/// to resize -- needs no GraphicsDevice at all, so it's what's exercised here instead.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TooltipTests
{
    private static Tooltip Build()
    {
        var fontService = TestFonts.Shared;
        var labelRenderer = new LabelRenderer();
        var windowService = TestElementPoolServiceFactory.Create(fontService, labelRenderer);
        windowService.RegisterFactory<Tooltip>(() => new Tooltip(fontService, windowService, labelRenderer));

        var tooltip = windowService.CreateElement<Tooltip>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, MaximumSize = new Vector2(220, 220), DisplayMode = ElementDisplayMode.WrapContent, IsVisible = false },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = true, CanUserFocus = false, CanUserClose = false },
        });
        tooltip.Initialize();
        return tooltip;
    }

    [TestMethod]
    public void SetRows_ThenUpdateText_GrowsTallerThanBodyTextAlone()
    {
        var tooltip = Build();
        tooltip.UpdateText("Body text");
        var heightWithoutRows = tooltip.CurrentSize.Y;

        tooltip.SetRows([new TooltipRow("Understocked", "14G", Color.LightGreen)]);
        tooltip.UpdateText("Body text");

        Assert.IsGreaterThan(heightWithoutRows, tooltip.CurrentSize.Y);
    }

    [TestMethod]
    public void SetRows_ClearedWithNull_ShrinksBackToBodyTextOnlyHeight()
    {
        var tooltip = Build();
        tooltip.UpdateText("Body text");
        var heightWithoutRows = tooltip.CurrentSize.Y;

        tooltip.SetRows([new TooltipRow("Overstocked", "7G", Color.IndianRed)]);
        tooltip.UpdateText("Body text");

        tooltip.SetRows(null);
        tooltip.UpdateText("Body text");

        Assert.AreEqual(heightWithoutRows, tooltip.CurrentSize.Y);
    }

    [TestMethod]
    public void SetRows_MoreRows_GrowsTallerThanFewerRows()
    {
        var tooltip = Build();

        tooltip.SetRows([new TooltipRow("Normal", "9G", Color.White)]);
        tooltip.UpdateText("Body text");
        var heightWithOneRow = tooltip.CurrentSize.Y;

        tooltip.SetRows([
            new TooltipRow("Desperate", "14G", Color.White),
            new TooltipRow("Understocked", "11G", Color.White),
            new TooltipRow("Normal", "9G", Color.White),
            new TooltipRow("Overstocked", "7G", Color.White),
            new TooltipRow("Flooded", "4G", Color.White),
        ]);
        tooltip.UpdateText("Body text");

        Assert.IsGreaterThan(heightWithOneRow, tooltip.CurrentSize.Y);
    }

    [TestMethod]
    public void SetRows_NonNull_GrowsWiderThanBodyTextAlone()
    {
        var tooltip = Build();
        tooltip.UpdateText("Body text");
        var widthWithoutRows = tooltip.CurrentSize.X;

        tooltip.SetRows([new TooltipRow("Understocked", "14G", Color.LightGreen)]);
        tooltip.UpdateText("Body text");

        Assert.IsGreaterThan(widthWithoutRows, tooltip.CurrentSize.X);
    }

    [TestMethod]
    public void SetRows_DividerRow_CountsTowardHeightLikeAnyOtherRow()
    {
        var tooltip = Build();

        tooltip.SetRows([new TooltipRow("Normal", "9G", Color.White)]);
        tooltip.UpdateText("Body text");
        var heightWithOneRow = tooltip.CurrentSize.Y;

        tooltip.SetRows([TooltipRow.Divider(Color.Gray), new TooltipRow("Normal", "9G", Color.White)]);
        tooltip.UpdateText("Body text");

        Assert.IsGreaterThan(heightWithOneRow, tooltip.CurrentSize.Y);
    }

    [TestMethod]
    public void Divider_Factory_ProducesEmptyTextAndIsDividerTrue()
    {
        var row = TooltipRow.Divider(Color.Gray);

        Assert.IsTrue(row.IsDivider);
        Assert.AreEqual(string.Empty, row.LeftText);
        Assert.AreEqual(string.Empty, row.RightText);
        Assert.AreEqual(Color.Gray, row.Color);
    }

    [TestMethod]
    public void TooltipRow_GlowColor_DefaultsToNull()
    {
        var row = new TooltipRow("Normal", "9G", Color.White);

        Assert.IsNull(row.GlowColor);
    }

    [TestMethod]
    public void TooltipRow_GlowColor_CanBeSet()
    {
        var row = new TooltipRow("Desperate", "14G", Color.White, GlowColor: Color.LightGreen);

        Assert.AreEqual(Color.LightGreen, row.GlowColor);
    }

    [TestMethod]
    public void TooltipRow_MiddleText_DefaultsToEmpty()
    {
        var row = new TooltipRow("Normal", "9G", Color.White);

        Assert.AreEqual(string.Empty, row.MiddleText);
    }

    [TestMethod]
    public void TooltipRow_MiddleText_CanBeSet()
    {
        var row = new TooltipRow("Understocked", "14G", Color.White, MiddleText: "10-14");

        Assert.AreEqual("10-14", row.MiddleText);
    }

    [TestMethod]
    public void UseFixedWidth_True_PinsWidthToMaximumSizeRegardlessOfRowContent()
    {
        var tooltip = Build();
        tooltip.UseFixedWidth = true;

        tooltip.SetRows([new TooltipRow("Normal", "9G", Color.White, MiddleText: "10-14")]);
        tooltip.UpdateText("X");

        Assert.AreEqual(tooltip.MaximumSize.X, tooltip.CurrentSize.X);
    }
}
