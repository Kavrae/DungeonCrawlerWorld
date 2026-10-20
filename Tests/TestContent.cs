using Engine.Bootstrap;
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
    private static readonly Lazy<MasterContent> Master = new(() =>
    {
        var content = MasterContent.Load(MasterContent.DefaultDirectory, EcsBuilder.Declare(GameBootstrapper.BuiltInModules()).ContentTypes);
        content.ThrowIfFailed();
        return content;
    });

    public static ActionDefinition Action(Guid actionId) =>
        Master.Value.Actions.FirstOrDefault(action => action.Id == actionId) ?? throw new InvalidOperationException($"The master manifest has no action {actionId}.");

    public static ItemDefinition Item(Guid itemId) =>
        Master.Value.Items.FirstOrDefault(item => item.Id == itemId) ?? throw new InvalidOperationException($"The master manifest has no item {itemId}.");

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
