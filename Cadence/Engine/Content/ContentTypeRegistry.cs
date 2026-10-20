using Engine.Modules;

namespace Engine.Content;

/// <summary>Every content codec a build's modules registered: one per plain value type, and the named members of each polymorphic family (effect entries, activators, contents).</summary>
/// <remarks>
/// Built once per build from every module's DeclareContentTypes, before any module registers components, so every codec
/// exists before a Configure reads a manifest. A mod adds members to a family the same way a built-in does; a module
/// replaced by a mod registers nothing, and its replacement registers its own.
/// </remarks>
public sealed class ContentTypeRegistry
{
    private readonly Dictionary<Type, IContentCodec> _valueCodecsByType = [];
    private readonly Dictionary<Type, PolymorphicFamily> _familiesByBaseType = [];

    /// <summary>Runs every module's DeclareContentTypes, in order.</summary>
    public static ContentTypeRegistry Declare(IEnumerable<IModule> modules)
    {
        var registry = new ContentTypeRegistry();
        foreach (var module in modules)
        {
            module.DeclareContentTypes(new ContentTypeDeclarations(registry, module.Name));
        }

        return registry;
    }

    /// <summary>The plain value types with a codec.</summary>
    public IEnumerable<Type> ValueTypes => _valueCodecsByType.Keys;

    /// <summary>The base types with a polymorphic family.</summary>
    public IEnumerable<Type> PolymorphicBaseTypes => _familiesByBaseType.Keys;

    public bool IsPolymorphicBase(Type type) => _familiesByBaseType.ContainsKey(type);

    public bool TryGetValueCodec(Type valueType, out IContentCodec codec) => _valueCodecsByType.TryGetValue(valueType, out codec!);

    public IContentCodec GetValueCodec(Type valueType) =>
        _valueCodecsByType.TryGetValue(valueType, out var codec)
            ? codec
            : throw new InvalidOperationException($"No content codec is registered for {valueType.Name}.");

    /// <summary>Every member of baseType's family: its discriminator and its codec, in registration order.</summary>
    public IReadOnlyList<(string Name, IContentCodec Codec)> MembersOf(Type baseType) =>
        _familiesByBaseType.TryGetValue(baseType, out var family) ? family.Members : [];

    /// <summary>The discriminator and codec for value's runtime type within baseType's family.</summary>
    public (string Name, IContentCodec Codec) GetMember(Type baseType, Type valueType)
    {
        var family = GetFamily(baseType);
        return family.MembersByType.TryGetValue(valueType, out var member)
            ? member
            : throw new InvalidOperationException($"{valueType.Name} has no content codec registered as a {baseType.Name}.");
    }

    /// <summary>The codec registered under name in baseType's family -- false for a name nothing registered (a removed mod's type).</summary>
    public bool TryGetMember(Type baseType, string name, out IContentCodec codec)
    {
        codec = null!;
        return _familiesByBaseType.TryGetValue(baseType, out var family) && family.CodecsByName.TryGetValue(name, out codec!);
    }

    internal void AddValueCodec(IContentCodec codec, string moduleName)
    {
        if (!_valueCodecsByType.TryAdd(codec.ValueType, codec))
        {
            throw new InvalidOperationException($"{moduleName} registers a content codec for {codec.ValueType.Name}, which already has one.");
        }
    }

    internal void AddPolymorphicCodec(Type baseType, string name, IContentCodec codec, string moduleName)
    {
        if (!_familiesByBaseType.TryGetValue(baseType, out var family))
        {
            family = new PolymorphicFamily();
            _familiesByBaseType[baseType] = family;
        }

        if (!family.CodecsByName.TryAdd(name, codec))
        {
            throw new InvalidOperationException($"{moduleName} registers '{name}' as a {baseType.Name}, a name another codec already has.");
        }

        if (!family.MembersByType.TryAdd(codec.ValueType, (name, codec)))
        {
            throw new InvalidOperationException($"{moduleName} registers {codec.ValueType.Name} as a {baseType.Name} twice.");
        }

        family.Members.Add((name, codec));
    }

    private PolymorphicFamily GetFamily(Type baseType) =>
        _familiesByBaseType.TryGetValue(baseType, out var family)
            ? family
            : throw new InvalidOperationException($"No content codecs are registered as a {baseType.Name}.");

    private sealed class PolymorphicFamily
    {
        public Dictionary<string, IContentCodec> CodecsByName { get; } = new(StringComparer.Ordinal);

        public Dictionary<Type, (string Name, IContentCodec Codec)> MembersByType { get; } = [];

        public List<(string Name, IContentCodec Codec)> Members { get; } = [];
    }
}
