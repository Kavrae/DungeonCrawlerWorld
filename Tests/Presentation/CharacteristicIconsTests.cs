using Engine.Math;
using Game.Modules.Core.Components;
using Game.Views;
using Microsoft.Xna.Framework;
using Presentation.UI.Characteristics;

namespace Tests.Presentation;

[TestClass]
public sealed class CharacteristicIconsTests
{
    private static readonly Vector2 IconSize = new(30, 30);

    [TestMethod]
    public void Size_Normal_IsTheFootprint_WithSizeAndFootprintLines()
    {
        var icon = CharacteristicIcons.Size(new Vector2Byte(1, 1), NonBlockingKind.None);

        Assert.AreEqual(new Vector2Byte(1, 1), icon.Footprint);
        Assert.AreEqual("Size : Normal\nFootprint : 1x1", icon.TooltipText);
    }

    [TestMethod]
    public void Size_OneByTwo_IsLarge_AndDrawsItsOwnFootprint()
    {
        var icon = CharacteristicIcons.Size(new Vector2Byte(1, 2), NonBlockingKind.None);

        Assert.AreEqual(new Vector2Byte(1, 2), icon.Footprint);
        Assert.AreEqual("Size : Large\nFootprint : 1x2", icon.TooltipText);
    }

    [TestMethod]
    public void Size_Huge_NamesItsFootprint()
    {
        var icon = CharacteristicIcons.Size(new Vector2Byte(3, 3), NonBlockingKind.None);

        Assert.AreEqual("Size : Huge\nFootprint : 3x3", icon.TooltipText);
    }

    [TestMethod]
    public void Size_Tiny_IsTheFourDots_AndStillNamesItsFootprint()
    {
        var icon = CharacteristicIcons.Size(new Vector2Byte(1, 1), NonBlockingKind.Tiny);

        Assert.IsNull(icon.Footprint);
        Assert.AreEqual(CharacteristicIcons.TinyGlyph, icon.Glyph);
        Assert.AreEqual(CharacteristicIcons.TinySpriteName, icon.SpriteName);
        Assert.AreEqual("Size : Tiny\nFootprint : 1x1", icon.TooltipText);
    }

    [TestMethod]
    public void For_AnOccupant_IsSizeAlone()
    {
        var icons = CharacteristicIcons.For(new OccupantView(new Vector3Int(0, 0, 0), new Vector2Byte(2, 2), NonBlockingKind.None, IsDead: false));

        Assert.AreEqual(1, icons.Count);
        Assert.AreEqual(new Vector2Byte(2, 2), icons[0].Footprint);
    }

    [TestMethod]
    public void FootprintCells_OneByOne_IsOneCellOnTheBottomEdge_CentredAcross()
    {
        var cell = FootprintIconElement.CellRectangle(IconSize, new Vector2Byte(1, 1), 0, 0);

        Assert.AreEqual(new Rectangle(10, 20, 9, 9), cell);
    }

    [TestMethod]
    public void FootprintCells_OneByTwo_IsAColumnOfTwo_OnTheBottomEdge()
    {
        var upper = FootprintIconElement.CellRectangle(IconSize, new Vector2Byte(1, 2), 0, 0);
        var lower = FootprintIconElement.CellRectangle(IconSize, new Vector2Byte(1, 2), 0, 1);

        Assert.AreEqual(new Rectangle(10, 10, 9, 9), upper);
        Assert.AreEqual(new Rectangle(10, 20, 9, 9), lower);
    }

    [TestMethod]
    public void FootprintCells_TwoByTwo_SameCellSizeAsOneByOne_CentredAcross()
    {
        var topLeft = FootprintIconElement.CellRectangle(IconSize, new Vector2Byte(2, 2), 0, 0);
        var bottomRight = FootprintIconElement.CellRectangle(IconSize, new Vector2Byte(2, 2), 1, 1);

        Assert.AreEqual(new Rectangle(5, 10, 9, 9), topLeft);
        Assert.AreEqual(new Rectangle(15, 20, 9, 9), bottomRight);
    }

    [TestMethod]
    public void FootprintCells_ThreeByThree_FillTheIcon()
    {
        var topLeft = FootprintIconElement.CellRectangle(IconSize, new Vector2Byte(3, 3), 0, 0);
        var bottomRight = FootprintIconElement.CellRectangle(IconSize, new Vector2Byte(3, 3), 2, 2);

        Assert.AreEqual(new Rectangle(0, 0, 9, 9), topLeft);
        Assert.AreEqual(new Rectangle(20, 20, 9, 9), bottomRight);
    }
}
