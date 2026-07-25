namespace Game.World;

public enum StatusEffectSourceKind : byte
{
    Entity,
    Admin,
    AI,
}

public readonly record struct StatusEffectSource(StatusEffectSourceKind Kind, int EntityId)
{
    public static StatusEffectSource FromEntity(int entityId) => new(StatusEffectSourceKind.Entity, entityId);

    public static readonly StatusEffectSource Admin = new(StatusEffectSourceKind.Admin, 0);
    public static readonly StatusEffectSource AI = new(StatusEffectSourceKind.AI, 0);

    public override readonly string ToString() =>
        Kind == StatusEffectSourceKind.Entity
            ? $"Entity#{EntityId}"
            : Kind.ToString();
}
