using Engine.Diagnostics;
using TestSupport;

[assembly: SerialTestMember("EngineHooks", typeof(EngineHookChannel<>), nameof(EngineHookChannel<object>.Subscribe))]
[assembly: SerialTestMember("EngineHooks", typeof(DiagnosticsEngine), nameof(DiagnosticsEngine.Start))]
