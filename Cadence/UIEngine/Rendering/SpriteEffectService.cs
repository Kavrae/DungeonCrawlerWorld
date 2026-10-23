using Microsoft.Xna.Framework.Graphics;

namespace UIEngine.Rendering;

/// <summary>The sprite effects: the one SpriteEffectBatch draws with, embedded in UIEngine, and any a game or mod ships as content.</summary>
/// <remarks>
/// <para>
/// Effects are fx_2_0 binaries (fxc, see UIEngine.csproj's CompileSpriteEffects). The sprite effect is embedded because it is
/// half of a contract whose other half is SpriteEffectVertex; a game's or mod's own effects are content files in the effects
/// directory (name.fxb), never engine resources.
/// </para>
/// <para>
/// A content file named like the sprite effect (SpriteUber.fxb) replaces it -- to iterate on the shader without rebuilding, or
/// a mod's replacement -- only when it declares the vertex layout this build draws with (its VertexVersion parameter equals
/// SpriteEffectVertex.Version) and loads; otherwise it is refused with one line to problems naming the file and both versions, and the
/// embedded effect is used.
/// </para>
/// </remarks>
public sealed class SpriteEffectService
{
    public const string SpriteEffectName = "SpriteUber";
    public const string VertexVersionParameter = "VertexVersion";
    private const string EffectFileExtension = ".fxb";

    private readonly GraphicsDevice _graphicsDevice;
    private readonly string _effectsDirectory;

    /// <param name="effectsDirectory">Directory holding content effects, relative to the exe's output directory (resolved against AppContext.BaseDirectory, as FontService's fontsDirectory). Need not exist.</param>
    /// <param name="problems">Where a refused override of the sprite effect is reported.</param>
    public SpriteEffectService(GraphicsDevice graphicsDevice, string effectsDirectory, TextWriter problems)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(effectsDirectory);

        _graphicsDevice = graphicsDevice;
        _effectsDirectory = Path.Combine(AppContext.BaseDirectory, effectsDirectory);
        SpriteEffect = LoadSpriteEffect(problems);
    }

    /// <summary>The effect SpriteEffectBatch draws with: the embedded one, or an accepted content override of it.</summary>
    public Effect SpriteEffect { get; }

    /// <summary>The content effect name.fxb from the effects directory.</summary>
    /// <exception cref="FileNotFoundException">No such file.</exception>
    public Effect Load(string name) => new(_graphicsDevice, File.ReadAllBytes(ContentEffectPath(name)));

    /// <summary>The bytes of the effect embedded in UIEngine as Shaders/name.fxb.</summary>
    /// <exception cref="InvalidOperationException">UIEngine embeds no such effect.</exception>
    public static byte[] ReadEmbedded(string name)
    {
        using var stream = typeof(SpriteEffectService).Assembly.GetManifestResourceStream(EmbeddedResourceName(name))
            ?? throw new InvalidOperationException($"UIEngine embeds no effect named {name} (looked for resource {EmbeddedResourceName(name)}).");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }

    /// <summary>Whether an override declaring declaredVersion may replace the sprite effect; a refusal is one line to problems.</summary>
    /// <param name="declaredVersion">Its VertexVersion parameter's value, or null when it has none that is a float (DeclaredVertexVersion).</param>
    public static bool AcceptsOverride(string overridePath, float? declaredVersion, TextWriter problems)
    {
        if (declaredVersion == SpriteEffectVertex.Version)
        {
            return true;
        }

        var declared = declaredVersion is { } version ? $"declares {VertexVersionParameter} {version}" : $"declares no float {VertexVersionParameter}";
        problems.WriteLine($"{overridePath} was not used in place of the built-in sprite effect: it {declared}, and this build draws with vertex version {SpriteEffectVertex.Version}.");
        return false;
    }

    /// <summary>The value of an effect's VertexVersion parameter, or null when it has none or it isn't a float.</summary>
    public static float? DeclaredVertexVersion(EffectParameter? parameter) =>
        parameter is { ParameterClass: EffectParameterClass.Scalar, ParameterType: EffectParameterType.Single } ? parameter.GetValueSingle() : null;

    private static string EmbeddedResourceName(string name) => $"UIEngine.Shaders.{name}{EffectFileExtension}";

    private string ContentEffectPath(string name) => Path.Combine(_effectsDirectory, name + EffectFileExtension);

    private Effect LoadSpriteEffect(TextWriter problems)
    {
        var overridePath = ContentEffectPath(SpriteEffectName);
        if (File.Exists(overridePath) && TryLoadOverride(overridePath, problems) is { } candidate)
        {
            if (AcceptsOverride(overridePath, DeclaredVertexVersion(candidate.Parameters[VertexVersionParameter]), problems))
            {
                return candidate;
            }

            candidate.Dispose();
        }

        return new Effect(_graphicsDevice, ReadEmbedded(SpriteEffectName));
    }

    /// <summary>The override at overridePath, or null with one line to problems when it can't be read or isn't an effect.</summary>
    private Effect? TryLoadOverride(string overridePath, TextWriter problems)
    {
        try
        {
            return new Effect(_graphicsDevice, File.ReadAllBytes(overridePath));
        }
        catch (Exception exception)
        {
            problems.WriteLine($"{overridePath} was not used in place of the built-in sprite effect: it could not be loaded ({exception.Message}).");
            return null;
        }
    }
}
