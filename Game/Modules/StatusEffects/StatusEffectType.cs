namespace Game.Modules.StatusEffects;

/// <summary>Identifies which effect a StatusEffectStack entry belongs to. New effects add a value here -- no new component type needed per effect.</summary>
public enum StatusEffectType : byte
{
    Burning,
    Poison,
    Paralysis,

    /// <summary>Glow-only today -- no IStatusEffectAuraApplier registered for it, so StatusEffectAuraSystem.GrantStacks gracefully no-ops (see its own doc comment: "a StatusEffectAuraSourceComponent can be authored for an effect type before that effect's own module exists"). MapTintGrid's glow still works regardless, since it reacts to AuraSourceAddedEvent/AuraSourceRemovedEvent independently of stack-granting. Granted by Scroll of Torch (see TorchAction's TODO.md entry) via AuraSourceGrant; a future pass can register a real applier for fog-of-war reveal / light-weakness damage without touching this value.</summary>
    Light,
}
