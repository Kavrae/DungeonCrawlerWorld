using System.Net;
using Engine.Diagnostics;

namespace Tests.Diagnostics;

[TestClass]
public sealed class DiagnosticsLinkArgumentTests
{
    [TestMethod]
    public void Format_ThenParse_GivesBackTheEndPoint()
    {
        var endPoint = new IPEndPoint(IPAddress.Loopback, 51234);

        Assert.AreEqual(endPoint, DiagnosticsLinkArgument.Parse(["--seed=3", DiagnosticsLinkArgument.Format(endPoint)]));
    }

    [TestMethod]
    [DataRow("--diagnostics-link=10.0.0.4:51234")]
    [DataRow("--diagnostics-link=127.0.0.1")]
    [DataRow("--diagnostics-link=127.0.0.1:0")]
    [DataRow("--diagnostics-link=nonsense")]
    [DataRow("--diagnostics=frame")]
    public void AnythingButALoopbackAddressAndPort_IsNoLink(string argument)
    {
        Assert.IsNull(DiagnosticsLinkArgument.Parse([argument]));
    }

    [TestMethod]
    [DataRow(DiagnosticsFeatures.None)]
    [DataRow(DiagnosticsFeatures.FrameBudget | DiagnosticsFeatures.Gauges)]
    [DataRow(DiagnosticsFeatures.Memory | DiagnosticsFeatures.Startup | DiagnosticsFeatures.LeakDetection)]
    [DataRow(DiagnosticsFeatures.All)]
    public void FeaturesFormatted_ParseBackAsTheSameFeatures(DiagnosticsFeatures features)
    {
        Assert.AreEqual(features, DiagnosticsFeaturesParser.Parse([DiagnosticsFeaturesParser.FormatArgument(features)]));
    }

    [TestMethod]
    public void FrameBudgetAndGauges_FormatAsFrameAndGauges()
    {
        Assert.AreEqual("--diagnostics=frame,gauges", DiagnosticsFeaturesParser.FormatArgument(DiagnosticsFeatures.FrameBudget | DiagnosticsFeatures.Gauges));
    }
}
