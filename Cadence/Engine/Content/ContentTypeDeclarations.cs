namespace Engine.Content;

/// <summary>Where one module registers the content codecs it owns during IModule.DeclareContentTypes.</summary>
public sealed class ContentTypeDeclarations
{
    private readonly ContentTypeRegistry _registry;
    private readonly string _moduleName;

    internal ContentTypeDeclarations(ContentTypeRegistry registry, string moduleName)
    {
        _registry = registry;
        _moduleName = moduleName;
    }

    /// <summary>The version of this module's content, written in the header of every table its codecs write (ContentTable.VersionsKey); ContentTypeRegistry.DefaultContentVersion until set.</summary>
    /// <remarks>Raised when a change to the module's codecs means an older file needs migrating.</remarks>
    public int ContentVersion
    {
        get => _registry.ContentVersionOf(_moduleName);
        set => _registry.SetContentVersion(_moduleName, value);
    }

    /// <summary>Registers the codec for a plain (non-polymorphic) value type, such as a definition or a spec it holds.</summary>
    public void RegisterValue<T>(ContentCodec<T> codec)
        where T : notnull =>
        _registry.AddValueCodec(codec, _moduleName);

    /// <summary>Registers TValue as a member of TBase's polymorphic family under name -- the discriminator written before its properties.</summary>
    /// <remarks>Names are stable: a file names the type by it, so renaming one breaks every file that holds it.</remarks>
    public void RegisterPolymorphic<TBase, TValue>(string name, ContentCodec<TValue> codec)
        where TBase : class
        where TValue : notnull, TBase =>
        _registry.AddPolymorphicCodec(typeof(TBase), name, codec, _moduleName);
}
