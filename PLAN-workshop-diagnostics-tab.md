# The Workshop Debug tab: diagnostics and launch options

(Child 8 of `EPIC-sprites-ui-engine-and-dev-tools.md`. Planned 2026-10-23; decisions settled in review 2026-10-24. The
debugging half of Cadence Workshop, as one **Debug** tab: how a debug run is launched (the diagnostics features now,
the other launch options later), a running game's gauges, frame costs, memory, leaks and startup, live, and the reports
and benchmarks already written to `Log/diagnostics/`, opened and compared. TODO.md's "Workshop Debug Options tab" is
folded into it.)

## Context

What exists today:
- **The diagnostics engine** (`Engine.Diagnostics`): measured code emits through static `EngineHooks` channels
  (`FrameCosts`, `SimulationFrames`, `Sessions`, `DiagnosticScopes`), each with one listener; `DiagnosticsEngine` is
  that listener and fans out to its feature trackers (`DiagnosticsFeatures`: FrameBudget, Memory, Startup,
  LeakDetection, Gauges). Everything per frame runs from `SystemManager.Update`'s frame boundary.
  - `FrameBudgetTracker`: ms spent per second per `[category] group.item`.
  - `ComponentMemoryTracker`/`PoolMemoryReport`: pool memory against holder counts.
  - `LeakDetector`: per-pool findings against `EcsContext.EntityPopulations`.
  - `StartupProfiler`: nested `DiagnosticScope`s until the first simulation frame.
  - `GaugeTracker`/`GaugeHistory`: every registered gauge sampled per frame, the last 300 frames kept for display.
  - `FrameRangeBenchmark`: the `--benchmark-frames` one-shot report.
- **What it writes:** `Log/diagnostics/latest.json|txt` every 5 s (`DiagnosticsReportWriter`, written off the
  simulation thread), `benchmark-*.json`, memory and `gauges-*.json` per benchmark range, the startup report. The
  phase-performance-testing skill reads these, saves baselines and diffs A/B runs.
- **Where it is shown:** the game's F3 window (`Presentation/UI/Diagnostics/DiagnosticsWindow`): rates, entity counts,
  the frame-time graph, every gauge with its history (`SparklineRenderer`, now in UIEngine), leak findings. It reads
  `DiagnosticsEngine.LiveGauges` directly. It is one of two Presentation types exempt from the no-store rule.
- **The Workshop** (`Cadence/Workshop`): Play (F5) and Build and Play (Ctrl+F5) start GameShell's built exe **as its own
  process** (`GameLauncher`, child 5's decision: a crash in one never takes the other), with no arguments; its output is
  captured into `Log/workshop-game-output.log`. Tabs are `IIdeTab`s; the engine-side Sprites tab
  (`Cadence/Workshop/Sprites`) shows that a tab needing no game lives in Cadence. The bottom panel has an Error List and
  Dependents. Controls: `ListView`, `SplitPane`, `DropdownField`, `NumericField`, `Toggle`, `ModalDialog`.
- **Related TODO.md items:** "Workshop Debug Options tab" (Engine: launch arguments, diagnostics checkboxes among them --
  folded into this plan's Debug tab, its remaining options now "Workshop Debug tab: launch options"),
  "IDE bottom panel: an Output tab" (the game's console), "Console variables and console commands", "Debug drawing and an
  AI debugger" (Game). This plan's link is what the last two will travel over.

## Research: how editors watch a running game

**Godot -- the editor launches the game, and the game connects back.** Running a project starts a separate process with
the editor's debugger address on its command line; the game connects to the editor over TCP and streams to the
Debugger panel: errors, the remote scene tree, the profiler and the **Monitors** tab -- built-in performance monitors
plus the project's own, registered with `Performance.add_custom_monitor("category/name", callable)` and graphed live.
Some built-in monitors exist only in debug builds. What carries over: the exact shape of Play here -- a separate process
started by the IDE, told where to connect; gauges are already custom monitors (group/name, read at frame end).

**Unity -- the Profiler attaches to a player.** A development build either auto-connects to the editor whose address was
baked in at build time, or is picked from "Attach to Player" (by IP and port, ports 54998-55511). Profiling Play Mode
inside the editor measures the editor too, which is why Unity recommends profiling a standalone build for real numbers.
What carries over: in-editor hosting contaminates measurements; attaching to a separate process is the accurate default.

**Unreal Insights -- trace channels to a live session or a file.** The game streams trace data (enabled channels: CPU,
frame, memory, ...) to Unreal Trace Server or straight to Insights (`-ListenForDirectTrace`, default port 1986). A live
session updates in real time, is recorded as it streams, and can be reopened later as a `.utrace` file; Insights can
hold several sessions. What carries over: one format for live and recorded data; the IDE records what it receives; the
game chooses channels at launch.

**Tracy -- the program is the client, the profiler UI the server**, over TCP (default 8086); in on-demand mode nothing
is collected until a profiler connects. What carries over: zero cost with nothing connected -- the rule `EngineHooks`
already keeps.

**Bevy Remote Protocol (`bevy_remote`)** -- JSON-RPC 2.0 over HTTP into a running app, inspecting and changing the ECS
(`world.get_components`); editors (a VS Code inspector, an MCP server) are built on it. What carries over: the same
connection later carries requests -- inspect an entity, run a console command -- not only a stream out.

### What the research decides

1. **Attach, don't host.** Every engine above watches a separate process, and Unity warns that in-editor numbers include
   the editor. Here too: Play is already a separate process; GameShell is its own FNA `Game` with its own window, which
   can't be embedded as a tab without rewriting it; a game crash must not take the IDE; and `EngineHooks`' channels are
   process-wide with one listener each. This answers the epic's open question.
2. **The IDE listens, the game connects** (Godot): the Workshop opens a loopback port and Play passes it on the game's
   command line. A game started without it connects nowhere and pays nothing.
3. **One format for live and recorded** (Insights): the periodic report sent over the link is the same JSON as
   `latest.json`, so the Reports view and the Live view read one shape.
4. **A link that can carry requests later** (Bevy, Godot's remote scene tree): messages in both directions from the
   start, though this plan only streams out.

## Design

### The link (Engine.Diagnostics, game side)

- **`DiagnosticsLink`**: a new consumer inside `DiagnosticsEngine`, the hooks' one listener, so nothing else subscribes
  (`--diagnostics-link=127.0.0.1:PORT`, parsed beside `--diagnostics=`). Without the argument it is never created.
- **At session start** it sends `Hello`: protocol version, process id, seed, enabled features, and the gauge
  descriptors (group, name, kind, unit -- frozen at `BeginSession`, so sent once).
- **Every simulation frame, at the frame boundary**, it writes one fixed-size record -- frame number, update
  milliseconds, draw rate and update rate, paused, then each gauge's latest value -- into a **preallocated ring**. No
  allocation per frame, no lock: one producer (the simulation thread), one consumer (the sender).
- **A sender thread** drains the ring to the socket. If the IDE falls behind, the ring overwrites the oldest records and
  counts what it dropped; the next record carries the count. **The simulation never waits on the link.**
- **Every periodic report** (the 5 s `latest.json` content: frame costs, memory, leaks, gauge summaries) is sent as that
  JSON, from the task that already writes the file; the startup report likewise when it completes; a benchmark's
  completion sends its file path.
- **Failure is silent to the game**: a refused connection, a closed IDE or a write error disables the link and writes
  one `Console.Error` line. The game runs on, and so do its own reports.
- **Framing:** length-prefixed messages with a type byte -- binary little-endian for frame records (doubles), UTF-8 JSON
  for everything else. Loopback only.
- **Determinism:** the link reads only what the trackers already hold and never touches a pool, a sequence or the
  clock, so a seeded run simulates the same world with it on or off.

### The listener (Cadence/Workshop)

- **`DiagnosticsLinkListener`**: a `TcpListener` on `127.0.0.1`, port 0 (the OS picks), opened when the Workshop
  starts. One connection at a time, the game Play last started; a second is refused.
- **`LiveDiagnosticsSession`**: what the IDE keeps of a connected game -- the hello, a **long history** of frame
  records (10 minutes at 60 fps, a ring per gauge preallocated from the hello's descriptor count; the IDE has memory the
  game's 300-frame ring didn't need), the latest periodic report, the startup report, dropped-frame and connection
  state. Parsed on the receiving thread, handed to the UI thread once per IDE frame.
- **Play passes the link and the launch options**: `GameLauncher` adds `--diagnostics-link=…` (always, the shell's)
  and the arguments the Debug tab's launch options make (`--diagnostics=` from its checkboxes today) to every launch.
- The session survives the game exiting: its data stays on screen, marked "ended", until the next Play.

### The Debug tab (Cadence/Workshop/Debug)

**One tab for launching with debug options and for watching what the game reports.** It folds TODO.md's "Workshop
Debug Options tab" into this one: both are about a debug run -- how it starts and what it shows -- and the diagnostics
features chosen at launch decide what the live views can show. Engine-side, like the Sprites tab: it knows launch
flags, gauges, frame costs, pools and scopes, nothing about the game.

**Where it sits:** at the strip's right edge beside the Play and Build and Play tiles, not among the builder tabs on the
left, as the Debug Options TODO placed it -- it configures how the game is launched, and shows that launch.

Three sections on a switch at its top: **Launch**, **Live** and **Reports**.

**Launch** (this plan: diagnostics only):
- A checkbox per diagnostics feature -- Frame budget, Memory, Leaks, Startup, Gauges -- and **All**; Gauges and Frame
  budget on by default. They make `--diagnostics=` for Play and Build and Play.
- The command line Play will start the game with, shown in full under the options, so nothing is hidden.
- Persisted per user between IDE runs, outside the repository and never in a content file (IMPLEMENTATION-NOTES,
  "Where the manifest lives": editor state never goes in content).
- **Later** (TODO.md "Workshop Debug tab: launch options"): the seed (plus "random"), map size, the benchmark range,
  headless, and a row per declared module setting, with the game adding its own flags through a hook.

**Live:**
- **Header:** connection (waiting for Play, connected, ended, dropped N frames), process id, seed, the features the game
  was started with, current frame, update and draw rates, paused. A view whose feature is off says which checkbox
  turns it on.
- **Frame graph:** update milliseconds over the history, with the 16.7 ms line, spikes marked; **Freeze** stops the
  view advancing (the session keeps recording) so a spike can be read; a cursor over the graph reads the frame.
- **Gauges:** a `ListView` grouped by gauge group -- value, minimum, maximum and average over the visible window, unit,
  and a sparkline per row; selecting rows draws them large under the frame graph, on the same frame axis.
- **Frame costs:** the latest report's rows (category, group, item, ms per second), sortable, with the change since
  the previous report.
- **Memory:** per pool -- bytes, holders, bytes per holder, the population it is held against.
- **Leaks:** the leak detector's findings, which also go to the **Error List** as warnings (table "Diagnostics", no
  record), so they show while another tab is open.
- **Startup:** the scope tree with times, nested as `StartupProfiler` recorded it.

**Reports:**
- A browser over `Log/diagnostics/`: `latest.json`, benchmark runs, memory and gauge series, startup reports, and saved
  baselines, newest first, each with its seed, frame range and date.
- Opening one shows it in the same views as Live (a benchmark's rows in Frame costs, a gauge series in Gauges), read by
  one `DiagnosticsReportReader` that the Live view's periodic reports also go through.
- **Compare:** pick two benchmark runs (or a run and a baseline) and see each row's ms per frame, the change and the
  ratio, sorted by change, rows past a threshold highlighted -- the comparison the phase-performance-testing skill prints,
  computed by an engine-side `BenchmarkComparison` the skill can keep using its own way.

### Shared with the F3 window

The F3 window stays: a game run without the IDE still needs it. What both show is computed once, in the engine:
- **Gauge formatting** (units from the `...Bytes`/`...Milliseconds` suffix, rounding) moves from `DiagnosticsWindow` to
  `Engine.Diagnostics` (`GaugeValueFormatting`).
- **Window statistics** (minimum, maximum, average over a span of a ring) as `GaugeWindowStatistics`.
- `SparklineRenderer` is already in UIEngine.
The F3 window must look exactly as it does now.

## Decisions

Settled in review 2026-10-24:
1. **The IDE connects to the game Play started; it never hosts a session.** Answers the epic's open question.
2. **The Workshop listens on a loopback port; the game connects when told to** by `--diagnostics-link=`. Without it,
   nothing is created and nothing costs.
3. **The link lives inside `DiagnosticsEngine`**, the hooks' one listener, as another consumer of what its trackers
   already measure.
4. **Per frame: a fixed-size record into a preallocated ring, sent by its own thread, oldest dropped when behind.**
   The simulation never waits and allocates nothing for it.
5. **Reports travel as the same JSON `latest.json` holds**, so Live and Reports read one format.
6. **Messages in both directions from the start**, though only the stream is used; console commands, entity inspection
   and the AI debugger come over the same link later.
7. **Debug options and diagnostics are one tab, the Debug tab**, engine-side (`Cadence/Workshop/Debug`), at the strip's
   right edge beside Play. TODO.md's "Workshop Debug Options tab" is folded into it.
8. **This plan builds diagnostics and its feature checkboxes**; the other launch options (seed, map size, benchmark
   range, headless, module settings) are TODO.md "Workshop Debug tab: launch options".
9. **Launch options persist per user**, never in content, and the full command line is shown.
10. **Leak findings go to the Error List** as warnings.
11. **The F3 window stays**, sharing formatting and statistics with the tab through the engine.
12. **The IDE keeps 10 minutes of frame history** per gauge; the game's own 300-frame ring is unchanged.
13. **Saving and reopening a live capture** is TODO.md "Workshop Debug tab: save a live capture".

## Phases

Each phase ends with the Workshop and the game running and a stop for testing before the next.

### Phase 1 -- The link (Engine, Workshop shell)

- `DiagnosticsLink` (ring, sender thread, framing, hello, frame records, periodic and startup reports, benchmark
  completion), `--diagnostics-link=` parsed beside `--diagnostics=`; `DiagnosticsLinkListener` and
  `LiveDiagnosticsSession` in the Workshop; `GameLauncher` passing the link (features still fixed at Gauges and Frame
  budget until Phase 2's checkboxes).
- The status line shows the connection: "Diagnostics: connected (pid N)", "ended", "dropped N frames".
- Tests: a loopback round trip of every message type; no allocation per frame on the simulation thread with the link on
  (`GC.GetAllocatedBytesForCurrentThread` across many frames); the ring drops oldest and counts; a closed listener
  disables the link with one error line and the run continues; a seeded headless run with the link on and off ends on
  the same world fingerprint.
- Check: start the Workshop, press F5 -- expect "Diagnostics: connected" within a second of the game's window opening.
  Close the game -- expect "ended". Start the game from a terminal without the argument -- expect nothing connects and
  no error. Kill the Workshop while the game runs -- expect the game to keep running and log one line.
- Benchmark A/B (headless, link off) against the current baseline: expect no change; then one run with the link
  connected, to report its cost.

### Phase 2 -- The Debug tab: Launch, and Live header, frame graph and gauges (Workshop)

- The tab at the strip's right edge; its Launch section (feature checkboxes and All, the command line shown, per-user
  persistence) feeding `GameLauncher`; its Live header, the frame graph with Freeze and the cursor readout, the gauges
  list with sparklines and selected rows graphed large. `GaugeValueFormatting` and `GaugeWindowStatistics` move to the
  engine; the F3 window uses them.
- Text through `KeyedCache` at the precision shown, rows drawn only when visible, as the F3 window does.
- Tests: the checkboxes make the `--diagnostics=` the parser reads back as the same features (All included); options
  survive a restart; formatting and statistics against today's F3 output for the same values; the session's ring keeps
  10 minutes and reads a window oldest-first; the tab driven through `UiInputController` (tick a checkbox, select a
  gauge row, Freeze).
- Check: open the Debug tab -- expect Gauges and Frame budget ticked and the command line ending in
  `--diagnostics=frame,gauges`. Untick Gauges, close and reopen the Workshop -- expect it still unticked. Tick Gauges
  again and Play -- expect every gauge the F3 window lists, with the same values (open F3 in the game beside it). Walk
  until a window shift -- expect the streamer's gauges and an update-time spike on the graph; press Freeze and hover the
  spike -- expect its frame number and milliseconds. Play with Gauges off -- expect the gauge view to say Gauges is off.

### Phase 3 -- Live: frame costs, memory, leaks and startup (Workshop)

- The four views over the periodic and startup reports; leak findings into the Error List.
- Tests: `DiagnosticsReportReader` reads every report `DiagnosticsReportWriter` writes (round trip of a generated
  report); leak findings become warnings with their pool named.
- Check: Play with Frame budget on -- expect the costs table to refresh every 5 s, sorted by ms per second, with the top
  rows the game's console prints. Tick Memory and Leaks and Play -- expect pools with holders, and no leak warning in a
  normal walk. Tick Startup and Play -- expect the scope tree, with each module's phases under its build stage.

### Phase 4 -- Reports and Compare (Workshop)

- The Reports browser over `Log/diagnostics/`, each kind opened in the Live views, and Compare over two benchmark runs
  or a run and a baseline (`BenchmarkComparison`).
- Tests: every report kind in the folder today opens; Compare matches the skill's printed diff for two saved runs.
- Check: open the current `baseline-build-release` -- expect its rows and seed. Compare it with a fresh benchmark run
  from the skill -- expect the same per-system changes the skill reports, the largest first.

## Open questions

- None.

## Out of scope / follow-ups

- **The other launch options** -- TODO.md "Workshop Debug tab: launch options" (Engine, Medium), planned in
  `PLAN-workshop-debug-tab-launch-options.md`.
- **Saving and reopening a live capture** -- TODO.md "Workshop Debug tab: save a live capture" (Engine, Medium).
- Requests over the link: console commands ("Console variables and console commands"), entity inspection, the AI
  debugger and debug drawing ("Debug drawing and an AI debugger"), pausing or stepping the simulation from the IDE.
- The game's console in the bottom panel ("IDE bottom panel: an Output tab").
- Attaching to a game the Workshop didn't start, or on another machine.
- Several sessions at once (two games side by side).
