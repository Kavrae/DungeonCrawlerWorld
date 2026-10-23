using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Tests.Architecture;
using UIEngine.Rendering;

namespace Tests.UIEngine.Rendering;

[TestClass]
public sealed class SpriteEffectServiceTests
{
    private const string OverridePath = @"C:\Game\Effects\SpriteUber.fxb";

    private static string ShaderPath(string fileName) =>
        Path.Combine(RepositoryProjects.FindRepositoryRoot(), "Cadence", "UIEngine", "Shaders", fileName);

    [TestMethod]
    public void SpriteEffect_IsEmbedded_AsAnFx20Binary()
    {
        var bytes = SpriteEffectService.ReadEmbedded(SpriteEffectService.SpriteEffectName);

        CollectionAssert.AreEqual(new byte[] { 0x01, 0x09, 0xFF, 0xFE }, bytes.Take(4).ToArray(), "A D3D9 effect (fx_2_0) starts with this signature, which MojoShader reads.");
    }

    [TestMethod]
    public void ReadEmbedded_UnknownName_Throws() =>
        Assert.ThrowsExactly<InvalidOperationException>(() => SpriteEffectService.ReadEmbedded("NoSuchEffect"));

    /// <summary>The checked-in binary was compiled from the source as it stands: its recorded hash is the source's.</summary>
    [TestMethod]
    public void CheckedInBinary_WasCompiledFromTheCurrentSource()
    {
        var sourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(ShaderPath("SpriteUber.fx"))));
        var recordedHash = File.ReadAllText(ShaderPath("SpriteUber.fxb.sha256")).Trim();

        Assert.AreEqual(sourceHash, recordedHash, ignoreCase: true, "Build UIEngine with fxc available to recompile SpriteUber.fx.");
    }

    /// <summary>The shader declares the vertex layout version the C# draws with, so the two can't drift apart unnoticed.</summary>
    [TestMethod]
    public void Shader_DeclaresTheVertexVersionTheBatchDrawsWith()
    {
        var match = Regex.Match(File.ReadAllText(ShaderPath("SpriteUber.fx")), $@"float\s+{SpriteEffectService.VertexVersionParameter}\s*=\s*([0-9.]+)\s*;");

        Assert.IsTrue(match.Success, "SpriteUber.fx declares no VertexVersion.");
        Assert.AreEqual(SpriteEffectVertex.Version, float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
    }

    /// <summary>The declaration's offsets were written by hand: its stride is the struct's size, so no element reads another's bytes.</summary>
    [TestMethod]
    public void VertexDeclaration_StrideIsTheVertexSize() =>
        Assert.AreEqual(System.Runtime.InteropServices.Marshal.SizeOf<SpriteEffectVertex>(), SpriteEffectVertex.Declaration.VertexStride);

    /// <summary>Every mode beyond Plain is a constant in the shader with the value SpriteDrawStyle writes into the vertex.</summary>
    [TestMethod]
    public void Shader_DeclaresEveryDrawModeWithItsValue()
    {
        var shader = File.ReadAllText(ShaderPath("SpriteUber.fx"));

        foreach (var mode in Enum.GetValues<SpriteDrawMode>().Where(mode => mode != SpriteDrawMode.Plain))
        {
            var match = Regex.Match(shader, $@"static\s+const\s+float\s+{mode}Mode\s*=\s*([0-9.]+)\s*;");

            Assert.IsTrue(match.Success, $"SpriteUber.fx declares no {mode}Mode.");
            Assert.AreEqual(new SpriteDrawStyle(mode).ToVertexStyle().Mode, float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), $"{mode}Mode");
        }
    }

    /// <summary>Every glow beyond None is a constant in the shader with the value SpriteDrawStyle writes into the vertex.</summary>
    [TestMethod]
    public void Shader_DeclaresEveryGlowWithItsValue()
    {
        var shader = File.ReadAllText(ShaderPath("SpriteUber.fx"));

        foreach (var glow in Enum.GetValues<SpriteGlow>().Where(glow => glow != SpriteGlow.None))
        {
            var match = Regex.Match(shader, $@"static\s+const\s+float\s+Glow{glow}\s*=\s*([0-9.]+)\s*;");

            Assert.IsTrue(match.Success, $"SpriteUber.fx declares no Glow{glow}.");
            Assert.AreEqual(new SpriteDrawStyle(SpriteDrawMode.Plain, glow).ToVertexStyle().Glow, float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), $"Glow{glow}");
        }
    }

    [TestMethod]
    public void Shader_DeclaresTheOutlineLayerWithItsValue()
    {
        var match = Regex.Match(File.ReadAllText(ShaderPath("SpriteUber.fx")), @"static\s+const\s+float\s+OutlineOnly\s*=\s*([0-9.]+)\s*;");

        Assert.IsTrue(match.Success, "SpriteUber.fx declares no OutlineOnly.");
        Assert.AreEqual(new SpriteDrawStyle(SpriteDrawMode.Plain, OutlineOnly: true).ToVertexStyle().OutlineOnly, float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void DrawStyle_WritesModeGlowAndGroundRowIntoTheVertex()
    {
        var style = SpriteDrawStyle.Desaturated.WithGlowAlongGroundRow(123.5f);

        Assert.AreEqual(new SpriteEffectVertexStyle((byte)SpriteDrawMode.Desaturate, (byte)SpriteGlow.AlongGroundRow, 0, 0, 123.5f), style.ToVertexStyle());
        Assert.AreEqual(SpriteGlow.AtPixel, style.WithGlowAtPixel().Glow);
        Assert.AreEqual(0f, style.WithGlowAtPixel().GroundRowY);
    }

    [TestMethod]
    public void Override_DeclaringThisVertexVersion_IsAcceptedSilently()
    {
        using var problems = new StringWriter();

        Assert.IsTrue(SpriteEffectService.AcceptsOverride(OverridePath, SpriteEffectVertex.Version, problems));
        Assert.AreEqual(string.Empty, problems.ToString());
    }

    [TestMethod]
    public void Override_DeclaringAnotherVertexVersion_IsRefused_NamingTheFileAndBothVersions()
    {
        using var problems = new StringWriter();

        Assert.IsFalse(SpriteEffectService.AcceptsOverride(OverridePath, SpriteEffectVertex.Version + 1, problems));

        var lines = problems.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.AreEqual(1, lines.Length);
        StringAssert.Contains(lines[0], OverridePath);
        StringAssert.Contains(lines[0], $"VertexVersion {SpriteEffectVertex.Version + 1}");
        StringAssert.Contains(lines[0], $"vertex version {SpriteEffectVertex.Version}");
    }

    [TestMethod]
    public void Override_DeclaringNoVertexVersion_IsRefused()
    {
        using var problems = new StringWriter();

        Assert.IsFalse(SpriteEffectService.AcceptsOverride(OverridePath, null, problems));
        StringAssert.Contains(problems.ToString(), "declares no float VertexVersion");
    }
}
