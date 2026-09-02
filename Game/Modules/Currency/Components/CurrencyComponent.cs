namespace Game.Modules.Currency.Components;

/// <summary>
/// An entity's spendable currency balances. Gold is the common currency shops will accept;
/// Credits is an extremely rare currency reserved for late-game features, no consumer yet.
/// </summary>
public struct CurrencyComponent(int gold, int credits)
{
    public int Gold { get; set; } = gold;
    public int Credits { get; set; } = credits;

    /// <summary>Mirrors DeadComponent/ManaComponent's own "Label : Value" per-line convention -- ComponentInspector's admin dump was otherwise falling back to ValueType's own bare-type-name ToString() (a plain struct, not a record struct, gets no field-value ToString for free).</summary>
    public override readonly string ToString() => $"Gold : {Gold}\nCredits : {Credits}";
}
