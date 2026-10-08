using Engine.ECS.Components;
using Engine.ECS.Entities;
using Game.Spawning;
using Game.Modules.Core.Components;
using Game.Modules.Crawler.Components;
using Game.Blueprints;

namespace Game.World;

public enum ActionSourceKind : byte
{
    Entity,
    Admin,
    AI,

    /// <summary>A kind of terrain (lava) rather than any one entity -- see TerrainTypeId.</summary>
    Terrain,

    /// <summary>A kind of aura rather than any one of its sources -- see AuraId. Credited when no single contributor can be named: a share of the aura several terrain types radiate (AuraField.Attribute).</summary>
    Aura,
}

/// <summary>What caused an effect: an entity, a kind of terrain, a kind of aura, an admin action or AI.</summary>
/// <remarks>
/// An entity source holds the entity's stable key and a handle to who it was when the source was
/// created, never its runtime id. Anything that records what caused something keeps one -- a status
/// effect timer, a stat modifier, a corpse's KilledBy -- long after the source was created, by which
/// time the entity may be unloaded and its id reused.
///
/// Packed into 8 bytes with no references, since the densest holders store one per instance -- the
/// burning, poison, body-part burning and stat-modifier pools: measured on the 3x3, a 16-byte source
/// made BurningSystem about 50% slower. Kind takes the top 4 bits, the detail (identity handle or
/// terrain type) the next 24, and the key the low 36 -- 68.7 billion keys, and 16.7 million
/// identities. Making a source past either limit throws rather than aliasing.
/// </remarks>
public readonly record struct ActionSource
{
    private const int KeyBits = 36;
    private const int DetailBits = 24;
    private const ulong KeyMask = (1UL << KeyBits) - 1;
    private const ulong DetailMask = (1UL << DetailBits) - 1;

    private readonly ulong _packed;

    private ActionSource(ActionSourceKind kind, EntityKey key, int detail)
    {
        if (key.Value > KeyMask)
        {
            throw new InvalidOperationException($"{key} doesn't fit a ActionSource's {KeyBits}-bit key.");
        }

        if ((ulong)(uint)detail > DetailMask)
        {
            throw new InvalidOperationException($"Detail {detail} doesn't fit a ActionSource's {DetailBits} bits.");
        }

        _packed = ((ulong)kind << (KeyBits + DetailBits)) | ((ulong)(uint)detail << KeyBits) | key.Value;
    }

    public ActionSourceKind Kind => (ActionSourceKind)(_packed >> (KeyBits + DetailBits));

    /// <summary>The source entity's stable key for Kind Entity; None otherwise.</summary>
    public EntityKey Key => new(_packed & KeyMask);

    /// <summary>The identity handle for Kind Entity; the terrain type id for Kind Terrain; the aura id for Kind Aura; 0 otherwise.</summary>
    private int Detail => (int)((_packed >> KeyBits) & DetailMask);

    /// <summary>The terrain type for Kind Terrain; 0 otherwise.</summary>
    public ushort TerrainTypeId => Kind == ActionSourceKind.Terrain ? (ushort)Detail : (ushort)0;

    /// <summary>The aura's session-local id for Kind Aura; 0 otherwise.</summary>
    public byte AuraId => Kind == ActionSourceKind.Aura ? (byte)Detail : (byte)0;

    /// <summary>Who the source entity was when this source was created, for Kind Entity; the unknown identity otherwise.</summary>
    public EntityIdentity Identity => EntityIdentities.Get(Kind == ActionSourceKind.Entity ? Detail : EntityIdentities.UnknownHandle);

    /// <summary>A source for entityId, recording its key and its current name and crawler number.</summary>
    /// <param name="creatures">The session's races and classes, so a creature that is named by its race rather than by a component of its own (see EntityNaming) is still named here.</param>
    public static ActionSource FromEntity(ComponentManager componentManager, EntityKeys entityKeys, int entityId, BlueprintRegistry creatures)
    {
        ArgumentNullException.ThrowIfNull(componentManager);
        ArgumentNullException.ThrowIfNull(entityKeys);

        var name = EntityNaming.TryResolveName(componentManager, creatures, entityId, out var resolved)
            ? resolved
            : null;
        int? crawlerNumber = componentManager.GetPackedPool<CrawlerComponent>().TryGetReadonly(entityId, out var crawler)
            ? crawler.CrawlerNumber
            : null;

        return new ActionSource(ActionSourceKind.Entity, entityKeys.GetKey(entityId), EntityIdentities.Intern(new EntityIdentity(name, crawlerNumber)));
    }

    public static ActionSource FromTerrain(ushort terrainTypeId) => new(ActionSourceKind.Terrain, EntityKey.None, terrainTypeId);

    /// <param name="auraId">The aura's session-local id (AuraCatalog).</param>
    public static ActionSource FromAura(byte auraId) => new(ActionSourceKind.Aura, EntityKey.None, auraId);

    public static readonly ActionSource Admin = new(ActionSourceKind.Admin, EntityKey.None, 0);
    public static readonly ActionSource AI = new(ActionSourceKind.AI, EntityKey.None, 0);

    /// <summary>Whether this is the entity holding key. Never true for None.</summary>
    public bool IsEntity(EntityKey key) => Kind == ActionSourceKind.Entity && !key.IsNone && Key == key;

    public override string ToString() => Kind switch
    {
        ActionSourceKind.Entity => $"{Identity.DisplayName} ({Key})",
        ActionSourceKind.Terrain => $"Terrain#{TerrainTypeId}",
        ActionSourceKind.Aura => $"Aura#{AuraId}",
        _ => Kind.ToString(),
    };
}
