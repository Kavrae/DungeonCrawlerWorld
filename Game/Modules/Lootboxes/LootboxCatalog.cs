using Engine.Modules;
using Game.Modules.Inventory;

namespace Game.Modules.Lootboxes;

/// <summary>Every loot box type, and the item definition of each type and rarity that has been granted.</summary>
/// <remarks>
/// There are many types and few of their type-rarity pairs are ever granted, so no box's item definition
/// exists up front: GetOrCreateItem builds one the first time its pair is needed and registers it into
/// the ItemCatalog. Its id is a pure function of the pair (see ItemIdFor), so a box means the same item
/// in every session, and an ItemCatalog lookup of a pair nothing has created yet still resolves through
/// TryResolveItem.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class LootboxCatalog(ItemCatalog itemCatalog) : Catalog<LootboxTypeDefinition>(static definition => definition.Id), IItemDefinitionSource
{
    public const string DefaultSpriteName = "Inventory";
    public const string DefaultGlyph = "L";

    private static readonly Guid ItemIdNamespace = new("5b0e7c1a-3f42-4d8e-9a61-7c2d4e8f1b30");

    private static readonly LootboxRarity[] Rarities = Enum.GetValues<LootboxRarity>();

    private readonly Dictionary<LootboxKind, ItemDefinition> _itemDefinitionsByKind = [];
    private readonly Dictionary<(LootboxKind Kind, IItemContents Contents), ItemDefinition> _overriddenItemDefinitions = [];
    private readonly Dictionary<Guid, LootboxKind> _kindsByItemDefinitionId = [];
    private int _typeCountWhenKindsWereIndexed = -1;

    /// <summary>The item id of kind's box, whether or not its definition has been created.</summary>
    public static Guid ItemIdFor(LootboxKind kind)
    {
        Span<byte> bytes = stackalloc byte[33];
        ItemIdNamespace.TryWriteBytes(bytes, bigEndian: true, out _);
        kind.TypeId.TryWriteBytes(bytes[16..], bigEndian: true, out _);
        bytes[32] = (byte)kind.Rarity;

        Span<byte> hash = stackalloc byte[32];
        System.Security.Cryptography.SHA256.HashData(bytes, hash);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x80);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash[..16], bigEndian: true);
    }

    /// <summary>"Bronze Adventurer Box".</summary>
    public string DisplayName(LootboxKind kind) => $"{kind.Rarity} {GetTypeDefinition(kind.TypeId).Name} Box";

    /// <summary>The item definition of kind's box, created and registered into the ItemCatalog the first time it's asked for.</summary>
    public ItemDefinition GetOrCreateItem(LootboxKind kind)
    {
        if (_itemDefinitionsByKind.TryGetValue(kind, out var existing))
        {
            return existing;
        }

        var type = GetTypeDefinition(kind.TypeId);
        var itemDefinition = new ItemDefinition(
            ItemIdFor(kind),
            DisplayName(kind),
            type.SpriteName ?? DefaultSpriteName,
            type.Glyph ?? DefaultGlyph,
            LootboxRarityColors.For(kind.Rarity),
            Tags: [Tag.Lootbox],
            Effects: [],
            Description: $"A {kind.Rarity} {type.Name} loot box. Opening any loot box opens every loot box you're holding.",
            Summary: "Open to receive its rewards.",
            Contents: RandomSingleStackContents.Instance,
            CanTrade: false,
            SpriteTint: LootboxRarityColors.For(kind.Rarity));

        _itemDefinitionsByKind.Add(kind, itemDefinition);
        _kindsByItemDefinitionId[itemDefinition.Id] = kind;
        itemCatalog.Register(itemDefinition);
        return itemDefinition;
    }

    /// <summary>The definition a granted reward's box carries: its kind's own, or a copy of it with the reward's contents in place of the usual ones.</summary>
    /// <remarks>A copy is made once per kind and contents and shared by every grant of it.</remarks>
    public ItemDefinition GetOrCreateItem(LootboxReward reward)
    {
        var itemDefinition = GetOrCreateItem(reward.Kind);
        if (reward.Contents is not { } contents)
        {
            return itemDefinition;
        }

        if (!_overriddenItemDefinitions.TryGetValue((reward.Kind, contents), out var overridden))
        {
            overridden = itemDefinition with { Contents = contents };
            _overriddenItemDefinitions.Add((reward.Kind, contents), overridden);
        }

        return overridden;
    }

    /// <summary>Which kind of box itemDefinitionId is; false for an item that isn't a loot box.</summary>
    public bool TryGetKind(Guid itemDefinitionId, out LootboxKind kind)
    {
        if (_kindsByItemDefinitionId.TryGetValue(itemDefinitionId, out kind))
        {
            return true;
        }

        IndexEveryKind();
        return _kindsByItemDefinitionId.TryGetValue(itemDefinitionId, out kind);
    }

    public bool TryResolveItem(Guid itemDefinitionId, out ItemDefinition definition)
    {
        if (TryGetKind(itemDefinitionId, out var kind))
        {
            definition = GetOrCreateItem(kind);
            return true;
        }

        definition = null!;
        return false;
    }

    /// <summary>The registered type typeId names; throws for an unregistered one.</summary>
    public LootboxTypeDefinition GetTypeDefinition(Guid typeId) =>
        TryGet(typeId, out var type) ? type : throw new InvalidOperationException($"No loot box type is registered with id {typeId}.");

    /// <summary>Indexes the item id of every type and rarity, so an id no box has been created for yet can still be recognized.</summary>
    /// <remarks>Ids only, no definitions; redone only when types have been registered since the last time.</remarks>
    private void IndexEveryKind()
    {
        if (_typeCountWhenKindsWereIndexed == Count)
        {
            return;
        }

        foreach (var type in Definitions)
        {
            foreach (var rarity in Rarities)
            {
                var kind = new LootboxKind(type.Id, rarity);
                _kindsByItemDefinitionId[ItemIdFor(kind)] = kind;
            }
        }

        _typeCountWhenKindsWereIndexed = Count;
    }
}
