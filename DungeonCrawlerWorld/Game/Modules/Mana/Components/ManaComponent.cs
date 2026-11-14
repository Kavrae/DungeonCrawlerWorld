using Engine.Utilities;

namespace Game.Modules.Mana.Components;

/// <summary>An entity's mana: what it has now, and its maximum before any MaximumMana modifier.</summary>
/// <remarks>
/// Float storage, as SimpleHealthComponent's, for exact fractional regeneration: a starting maximum is often 2-12, where a
/// rounded regen tick would stall for seconds or never land. Regeneration isn't stored -- ManaRegenSystem derives it from
/// Intelligence each tick. Only an entity that gains something draining its user's mana holds one
/// (ManaGrant.EnsureManaComponentExists), its BaseMaximumMana then the Intelligence total. CurrentMana is held within the
/// modified maximum; compare it only with that (ManaQueries.TryGetTotals), never with BaseMaximumMana.
/// </remarks>
public struct ManaComponent(float currentMana, float baseMaximumMana)
{
    public float CurrentMana { get; set; } = currentMana;
    public float BaseMaximumMana { get; set; } = baseMaximumMana;

    public override readonly string ToString() =>
        BaseMaximumMana > 0
            ? $"{StringUtility.BuildPercentageBar("MP", (int)CurrentMana, (int)BaseMaximumMana, 20)} {(int)CurrentMana}/{(int)BaseMaximumMana} (base)"
            : $"Invalid BaseMaximumMana: {BaseMaximumMana}";
}
