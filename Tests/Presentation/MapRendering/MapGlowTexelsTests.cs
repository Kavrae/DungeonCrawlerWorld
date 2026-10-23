using Engine.Events;
using Engine.Math;
using Game.Modules.Auras;
using Game.Modules.Auras.Components;
using Game.Terrain;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.MapRendering;

namespace Tests.Presentation.MapRendering;

[TestClass]
public sealed class MapGlowTexelsTests
{
    private const byte OrangeAuraId = 0;
    private static readonly AuraDefinition OrangeAura = new(new Guid("00000000-0000-0000-0000-0000000000c1"), "Orange", Color.Orange);
    private static readonly MapImageKey Key = new(new Point(20, 30), TileColumns: 10, TileRows: 6, TileSize: new Point(32, 32), MapLayer: 0);

    private static (AuraField Field, MapGlowTexels Texels) Build(float glowOpacityMultiplier = 0.5f)
    {
        var auras = new AuraCatalog();
        auras.Register(OrangeAura with { GlowOpacityMultiplier = glowOpacityMultiplier });
        var world = TestWorlds.Create(new Map(new Vector3Int(100, 100, 1)));
        var field = new AuraField(world, new TerrainRegistry(), auras, new EventBus());
        return (field, new MapGlowTexels(new FakeMapView(), new AuraGlowBlend(new AuraPowerView(field, auras))));
    }

    [TestMethod]
    public void Covers_TheGridAndTheMarginOnEverySide()
    {
        var (_, texels) = Build();

        texels.Build(Key);

        Assert.AreEqual(Key.TileColumns + MapGlowTexels.MarginTiles * 2, texels.Width);
        Assert.AreEqual(Key.TileRows + MapGlowTexels.MarginTiles * 2, texels.Height);
    }

    /// <summary>The colour the glow overlay used to draw over the tile: the aura's colour times its capped power's share, times its opacity multiplier (half by default).</summary>
    [TestMethod]
    public void GlowingTile_IsItsColourPremultipliedByItsStrength()
    {
        var (field, texels) = Build();
        field.AddSource(new Vector3Int(Key.Scroll.X + 3, Key.Scroll.Y + 2, 0), new AuraSourceComponent(OrangeAuraId, power: 4, size: 0));

        texels.Build(Key);

        Assert.AreEqual(Color.Orange * (4f / MapGlowTexels.FullGlowPower * 0.5f), texels.TexelAt(3, 2));
    }

    [TestMethod]
    public void PowerPastFull_GlowsNoBrighter()
    {
        var (field, texels) = Build();
        field.AddSource(new Vector3Int(Key.Scroll.X + 3, Key.Scroll.Y + 2, 0), new AuraSourceComponent(OrangeAuraId, power: 20, size: 0));

        texels.Build(Key);

        Assert.AreEqual(Color.Orange * 0.5f, texels.TexelAt(3, 2));
    }

    [TestMethod]
    public void MoreOpaqueAura_GlowsAtItsOwnOpacity()
    {
        var (field, texels) = Build(glowOpacityMultiplier: 0.75f);
        field.AddSource(new Vector3Int(Key.Scroll.X + 3, Key.Scroll.Y + 2, 0), new AuraSourceComponent(OrangeAuraId, power: 4, size: 0));

        texels.Build(Key);

        Assert.AreEqual(Color.Orange * (4f / MapGlowTexels.FullGlowPower * 0.75f), texels.TexelAt(3, 2));
    }

    [TestMethod]
    public void GlowOpacity_NeverPastFullyOpaque()
    {
        var (field, texels) = Build(glowOpacityMultiplier: 5f);
        field.AddSource(new Vector3Int(Key.Scroll.X + 3, Key.Scroll.Y + 2, 0), new AuraSourceComponent(OrangeAuraId, power: 8, size: 0));

        texels.Build(Key);

        Assert.AreEqual(Color.Orange, texels.TexelAt(3, 2));
    }

    [TestMethod]
    public void WhereNoAuraReaches_IsTransparent()
    {
        var (field, texels) = Build();
        field.AddSource(new Vector3Int(Key.Scroll.X + 3, Key.Scroll.Y + 2, 0), new AuraSourceComponent(OrangeAuraId, power: 8, size: 0));

        texels.Build(Key);

        Assert.AreEqual(Color.Transparent, texels.TexelAt(5, 2));
    }

    /// <summary>A sprite anchored in the overscan just outside the grid reads the glow of the tile it stands on.</summary>
    [TestMethod]
    public void TileOutsideTheGrid_InTheMargin_HasItsGlow()
    {
        var (field, texels) = Build();
        field.AddSource(new Vector3Int(Key.Scroll.X - 2, Key.Scroll.Y + Key.TileRows + 1, 0), new AuraSourceComponent(OrangeAuraId, power: 8, size: 0));

        texels.Build(Key);

        Assert.AreNotEqual(Color.Transparent, texels.TexelAt(-2, Key.TileRows + 1));
    }

    [TestMethod]
    public void OffTheMap_IsTransparent()
    {
        var (_, texels) = Build();

        texels.Build(Key with { Scroll = new Point(-5, -5) });

        Assert.AreEqual(Color.Transparent, texels.TexelAt(0, 0));
    }
}
