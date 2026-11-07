using Game.World;

namespace Game.Views;

/// <summary>One aura reaching a cell: its power there and whom its effect there is credited to.</summary>
/// <param name="Credit">The strongest single contributor at the cell, as the aura's credit names it (AuraField.PowerReaching): an entity source's entity or its placer, the terrain, or the aura itself when no one contributor can be named.</param>
public readonly record struct AuraAtCell(byte AuraId, int Power, ActionSource Credit);
