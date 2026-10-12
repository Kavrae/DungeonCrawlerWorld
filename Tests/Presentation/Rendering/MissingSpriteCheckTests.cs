using Engine.Math;
using Game.Blueprints;
using Game.Modules.Actions;
using Game.Modules.Inventory;
using Game.Modules.Lootboxes;
using Game.Terrain;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.Rendering;

namespace Tests.Presentation.Rendering;

[TestClass]
public sealed class MissingSpriteCheckTests
{
    private static ContentSpriteNames NamesOf(BlueprintRegistry blueprints, TerrainRegistry? terrain = null)
    {
        var items = new ItemCatalog();
        return new ContentSpriteNames(blueprints, terrain ?? new TerrainRegistry(), new ActionCatalog(), items, new LootboxCatalog(items));
    }

    private static BlueprintDefinition Blueprint(string name, string spriteName) =>
        new(Guid.NewGuid(), name) { Appearance = new AppearanceFacet { SpriteName = spriteName } };

    /// <summary>Built-in actions that name art the manifest doesn't have yet, and so draw their glyphs. A new gap fails this; giving one of these art means taking it off the list.</summary>
    private static readonly string[] BuiltInSpritesAwaitingArt = ["QuickAttack", "PowerAttack", "Dodge", "Wand"];

    [TestMethod]
    public void Report_TheBuiltInContent_IsMissingOnlyTheSpritesAwaitingArt()
    {
        var context = BuiltInTestModules.Build(new Map(new Vector3Int(5, 5, 3))).Context;
        var output = new StringWriter();

        var missing = MissingSpriteCheck.Report(new ContentSpriteNames(context.Definitions, context.Terrain, context.Actions, context.Items, context.Lootboxes), TestSprites.Resolver, output);

        CollectionAssert.AreEquivalent(BuiltInSpritesAwaitingArt, missing.Select(declared => declared.SpriteName).ToArray(), output.ToString());
    }

    [TestMethod]
    public void Report_ANameTheManifestLacks_IsReportedOnce_NamingWhatDeclaredItFirst()
    {
        var blueprints = new BlueprintRegistry();
        blueprints.Register(Blueprint("Wisp", "No such sprite"));
        blueprints.Register(Blueprint("Second wisp", "No such sprite"));
        var output = new StringWriter();

        var missing = MissingSpriteCheck.Report(NamesOf(blueprints), TestSprites.Resolver, output);

        Assert.HasCount(1, missing);
        Assert.AreEqual("Sprite 'No such sprite' (blueprint Wisp) is not in the sprite manifest; it draws its glyph." + Environment.NewLine, output.ToString());
    }

    [TestMethod]
    public void Report_ANameTheManifestHas_OrADeliberateGlyphOnly_IsNotReported()
    {
        var blueprints = new BlueprintRegistry();
        blueprints.Register(Blueprint("Goblin-ish", "Goblin"));
        blueprints.Register(Blueprint("Glyph only", AppearanceFacet.NoSprite));
        var terrain = new TerrainRegistry();
        terrain.Register(new TerrainDefinition("test:glyph", "Glyph", "", "~", Color.White));

        var missing = MissingSpriteCheck.Report(NamesOf(blueprints, terrain), TestSprites.Resolver, TextWriter.Null);

        Assert.IsEmpty(missing);
    }

    [TestMethod]
    public void Report_TerrainAndActionNames_AreChecked()
    {
        var terrain = new TerrainRegistry();
        terrain.Register(new TerrainDefinition("test:mire", "Mire", "", "~", Color.White, SpriteName: "No such floor"));

        var missing = MissingSpriteCheck.Report(NamesOf(new BlueprintRegistry(), terrain), TestSprites.Resolver, TextWriter.Null);

        CollectionAssert.AreEqual(new[] { new DeclaredSpriteName("No such floor", "terrain test:mire") }, missing.ToArray());
    }
}
