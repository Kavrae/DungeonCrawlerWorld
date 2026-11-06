using Engine.Bootstrap;
using Engine.Content;
using Game.Bootstrap;
using Game.Content;
using Game.Effects;
using Game.Effects.Entries;
using Game.Modules.Actions;
using Game.Modules.Auras;
using Game.Modules.Inventory;

namespace Tests;

/// <summary>The built-in actions, items and auras as the game loads them: the master manifest copied into the test output, read once through the built-in modules' codecs.</summary>
/// <remarks>What a test that once called a definition's own Build() now reads -- so tests run against the shipped content, not a C# copy of it. One instance per Guid for the whole run, as within a session.</remarks>
internal static class TestContent
{
    /// <summary>The master manifest in the test output, as the layer a build reads.</summary>
    public static ContentLayer MasterLayer { get; } = GameContent.MasterLayer(GameContent.DefaultMasterDirectory);

    /// <summary>The master manifest alone: the layers every test build reads, as GameShell's are with no mods or save.</summary>
    public static ContentLayers Layers { get; } = new([MasterLayer]);

    private static readonly Lazy<GameContent> Master = new(() =>
    {
        var content = GameContent.Load(Layers, EcsBuilder.Declare(GameBootstrapper.BuiltInModules()).ContentTypes);
        content.ThrowIfFailed();
        return content;
    });

    /// <summary>Every item the master manifest holds, in its order.</summary>
    public static IReadOnlyList<ItemDefinition> Items => Master.Value.Items;

    public static ActionDefinition Action(Guid actionId) =>
        Master.Value.Actions.FirstOrDefault(action => action.Id == actionId) ?? throw new InvalidOperationException($"The master manifest has no action {actionId}.");

    public static ItemDefinition Item(Guid itemId) =>
        Master.Value.Items.FirstOrDefault(item => item.Id == itemId) ?? throw new InvalidOperationException($"The master manifest has no item {itemId}.");

    /// <summary>Every built-in module's codecs: what a test writes a content line with.</summary>
    public static ContentTypeRegistry Types => LazyTypes.Value;

    private static readonly Lazy<ContentTypeRegistry> LazyTypes = new(() => EcsBuilder.Declare(GameBootstrapper.BuiltInModules()).ContentTypes);

    /// <summary>The master manifest with auras on top, in a mod layer of their own: for a test whose terrain or blueprint radiates an aura only it declares.</summary>
    public static ContentLayers WithAuras(params AuraDefinition[] auras) =>
        Layers.With(ContentLayer.FromText("Test auras", ContentLayerKind.Mod, new Dictionary<string, string>
        {
            [GameContent.AurasFileName] = string.Join('\n', [ContentTable.Header(GameContent.AurasTable), .. auras.Select(aura => ContentTable.WriteLine(aura, Types))]) + "\n",
        }));

    public static AuraDefinition Aura(Guid auraId) =>
        Master.Value.Auras.FirstOrDefault(aura => aura.Id == auraId) ?? throw new InvalidOperationException($"The master manifest has no aura {auraId}.");

    /// <summary>The mana an action's activation effects drain -- what a spell costs.</summary>
    public static ushort ManaCostOf(Guid actionId)
    {
        var total = 0;
        EffectEntries.ForEach(Action(actionId).ActivationEffects, entry =>
        {
            if (entry is ManaDrain drain)
            {
                total += drain.Amount;
            }
        });

        return (ushort)total;
    }
}
