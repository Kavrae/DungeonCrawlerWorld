# The Workshop Debug tab: launch options

(TODO.md "Workshop Debug tab: launch options" (Engine, Medium), the follow-up named in Decision 8 of
`PLAN-workshop-diagnostics-tab.md`, epic child 8 of `EPIC-sprites-ui-engine-and-dev-tools.md`. Planned 2026-10-24,
decisions settled in review 2026-10-25. **Depends on child 8's Phase 2** (the Debug tab, its Launch section, Play
passing that section's arguments, per-user persistence) and comes after it. Child 8 has in fact landed in the code
(commit `5d8c352`, "IDE 8) Diagnostics and debug launch options"), all phases, so this plan is written against the real
`DebugLaunchOptions`, `DebugLaunchSection` and `WorkshopDebugging` and can start once child 8 is accepted. It adds the
launch options that are still typed by hand: the seed, the map size, the benchmark range, headless, and module settings,
plus Stop, which a headless run needs.)

## Context

What exists today, checked in the code:
- **The game's flags** (`DungeonCrawlerWorld/GameShell/Program.cs`), each parsed by its own tolerant parser: a missing
  or malformed value means "not given", never a failed start.
  - `--seed=N` -- `Engine.Math.RandomSeed.Parse`: an `int`; absent or unparseable picks `Random.Shared.Next()`, so
    every run has a seed. No `Format`.
  - `--benchmark-frames=START-END` -- `Engine.Diagnostics.BenchmarkFrameRange.Parse`: `0 <= START < END` (`long`),
    END exclusive. No `Format`.
  - `--headless` -- a literal in `Program.cs`. Without a range it prints `--headless requires
    --benchmark-frames=START-END.` and exits with code 2; with one it runs `HeadlessBenchmark.Run`, which updates
    until `DiagnosticsEngine.IsBenchmarkComplete`, prints `[Headless] Fingerprint <hex>` and exits 0. Headless keeps
    only `Memory | Gauges` of the diagnostics features (`HeadlessBenchmark.Run` masks the rest), and passes the
    diagnostics link on.
  - `--map-size=N` -- `DungeonCrawlerWorld.MapSizeArgument` (internal to GameShell): positive `int`; null or 3072 is
    the streamed window, anything else a bounded fully loaded map (`FloorBuilder.CreateMap`). A game flag, not an
    engine one.
  - `--diagnostics=` -- `DiagnosticsFeaturesParser`, the one flag with a `FormatArgument` (child 8), and
    `--diagnostics-link=` -- `DiagnosticsLinkArgument`, always the shell's.
  - `--setting=Module.Setting=Value` -- `Engine.Settings.CommandLineSettingsSource`; the value is everything after the
    second `=`.
- **Module settings** (`Engine.Settings`): `SettingKey<T>(ModuleId, Name) where T : IParsable<T>`, declared in
  `IModule.DeclareSettings` through `SettingsDeclarations.Declare(key, default, validate?)` into a `SettingsCatalog`
  of `SettingDefinition`s (module id and name, name, `ValueType`, `DefaultValue`, `TryParse` with the invariant
  culture, an opaque `validate`). `ModuleDeclarations.Declare` resolves the catalog against the sources and **keeps
  only the `SettingValues` and failures**: the definitions are not reachable from a build. **No module declares a
  setting yet.** `IParsable<T>` rules out enums (an enum is not `IParsable<TSelf>`), so an enum setting can't be
  declared today.
- **The Debug tab's Launch section** (`Cadence/Workshop/Debug`, child 8): `DebugLaunchSection` -- the diagnostics
  features as a `ToggleList` under All, the full command line in a read-only box with a copy button, and the path of
  the options file. `DebugLaunchOptions(DiagnosticsFeatures)` makes the arguments; `DebugLaunchOptionsStore` saves
  them as JSON at `%LOCALAPPDATA%\Cadence\Workshop\debug-launch-options.json` (one file for every game built on
  Cadence); `WorkshopDebugging.LaunchArguments` is the link argument then the options' arguments, and `CommandLine`
  is `GameLauncher.CommandLineFor` of them, so what is shown is what is launched.
- **Launching** (`Cadence/Workshop/GameLauncher.cs`): one game at a time; Play (F5) and Build and Play (Ctrl+F5);
  arguments are one string appended to the target's; output captured line by line; `Status` and `StatusVersion` say
  what happened last. **There is no way to stop the game from the Workshop** -- a windowed game is closed by its
  window, a headless one only by finishing or crashing.
- **The link and headless** (`Engine.Diagnostics.DiagnosticsLink`, `LiveDiagnosticsSession`): a headless run connects
  like a windowed one, sends its hello (seed, features), a record per frame, and `BenchmarkCompleted`
  (`DiagnosticsLinkBenchmarkCompletion`: range and the benchmark, memory and gauge report paths) as the range closes;
  `DiagnosticsLink.Dispose` sends what is queued before the process exits. Headless frames run back to back (a few ms
  each), faster than the sender's 8 ms drain, so the 600-record ring drops records and the session reports drops.
  `LiveDiagnosticsSession.LatestBenchmarkCompletion` is kept, but **nothing in the Workshop reads it** (only
  `DiagnosticsLinkListenerTests`): a completed benchmark opens nothing today.
- **The App** (`DungeonCrawlerWorld/Workshop/App/Program.cs`): builds the content session over the same module set
  the game will run -- built-ins plus the mods validated from GameShell's own `Mods` folder -- inside
  `WorkshopHostOptions.CreateTabs`, which returns a `WorkshopTabSet` (tabs and load problems). That is the hook a game
  already adds to the shell through, and where its declared settings are at hand.
- **Controls** (UIEngine): `NumericField` (a `double` with minimum, maximum, step and decimal places -- exact for any
  `int`), `Toggle`, `ToggleList`, `DropdownField`, `TextBox`, `Button`.

## Research: how IDEs and engines expose launch options

**Visual Studio -- launch profiles.** A .NET project's `Properties/launchSettings.json` holds named profiles, each
with a `commandName` and optional `commandLineArgs`, environment variables and working directory; the Debug property
page is a UI over the file, the toolbar picks the active profile, and `dotnet run` and VS Code read the same file.
The file is usually checked in; per-user leftovers (`.csproj.user`) are a known source of confusion (arguments
appearing twice). Stop Debugging is Shift+F5. What carries over: the command line is one visible string; several named
profiles serve projects with many targets; per-user state must not hide what runs.

**JetBrains Rider -- run configurations.** Each configuration names a target, its program arguments ("in the same form
you would use on the command line") and **Before launch** tasks (build, another configuration, an external tool), in
order. They live per user in `.idea/workspace.xml` unless **Store as project file** shares one in `.run/`. What
carries over: the per-user default with sharing as a deliberate act; build-before-launch is part of the
configuration (here, the separate Build and Play command already).

**Unreal -- Play settings and Additional Launch Parameters.** Editor Preferences > Level Editor > Play holds the
Play-In-Editor and Standalone options; **Additional Launch Parameters** is free text passed to a standalone game
only, beside dedicated server parameters for multiplayer tests. Everything else is a documented command-line flag
(`-nullrhi`, `-ExecCmds=`, `-ini:` overrides). What carries over: typed options for what the editor knows, one free
text field for everything else, and the standalone (separate process) path as the one that takes arguments.

**Unity -- player command-line arguments.** A built player takes flags such as `-batchmode` (headless: shows nothing,
takes no input) and `-nographics` (no graphics device, batch mode only), and batch mode turns its log off unless
`-logFile` names one. What carries over: headless is a mode with rules of its own (no window, no input, logging
elsewhere) that the launcher must account for, not just another checkbox.

**Godot -- Customize Run Instances.** Debug > Customize Run Instances sets **Main Run Args** (space separated, passed on
every Run Project even with multiple instances off) and feature tags, and optionally several instances, each adding
to or overriding the main arguments. Settings are stored per project in the editor's own metadata, not in the
project's content. What carries over: one main set of arguments is the common case; several instances or profiles are
an opt-in on top.

### What the research decides

1. **Typed options for the flags the tools know, free text for the rest** (Unreal's Additional Launch Parameters,
   Godot's Main Run Args, VS's `commandLineArgs`): the section types seed, range, headless, map size and settings, and
   keeps one "Extra arguments" box so a new flag is usable before a row exists for it.
2. **One set of options, per user, per game; profiles later.** Every tool above has a single active configuration;
   several exist where there are several targets (VS projects, Rider configurations, Godot instances). This Workshop has
   one target, so named profiles are a follow-up, not the first step.
3. **Stored with the user, never in the repository or content** (Rider's `workspace.xml` default, Godot's editor
   metadata), keyed by game so two Cadence games never share a seed or a setting name.
4. **The command line is shown whole** (VS's one string): already true (child 8), and it stays the single truth --
   the shown text is built by the same code that launches.
5. **Headless is a mode with consequences** (Unity's batch mode): it needs a range to end, it can't be closed by a
   window, so the launcher needs Stop (VS's Shift+F5) and the tab must say it is running.

## Design

### The options model (Cadence/Workshop/Debug)

`DebugLaunchOptions` grows from the features alone to everything the Launch section sets, still an immutable record
with structural equality (so `WorkshopDebugging.Options`' save-on-change keeps working):
- `DiagnosticsFeatures` (unchanged).
- `Seed`: `int?` -- null is Random (no `--seed=`, the game picks and reports one).
- `BenchmarkRange`: `BenchmarkFrameRange?` -- null is no benchmark.
- `Headless`: `bool`, meaningful only with a range.
- `GameFlagValues`: the game's flags by flag id, each as the text its argument carries (absent = flag not given).
- `SettingOverrides`: by qualified name (`Module.Setting`), each the text the override carries, invariant culture --
  **only settings changed from their default**, so a default changed in code reaches the next Play.
- `ExtraArguments`: free text, appended last.

`Arguments` becomes `IReadOnlyList<string>` built in a fixed order -- diagnostics, seed, range, headless, game flags in
the game's order, settings sorted by name, then the extra text as typed -- and joined by one
`CommandLineArguments.Join` (Windows quoting: an argument with a space or quote is quoted, so `--setting=Ui.Title=Two
words` survives). `WorkshopDebugging.LaunchArguments` and `CommandLine` both go through it, so shown = launched.

Each flag is formatted by the engine type that parses it, beside its parser, so the two can't drift: add
`RandomSeed.FormatArgument(int)`, `BenchmarkFrameRange.FormatArgument()`, a `HeadlessArgument` (`Engine.Diagnostics`:
the `--headless` constant and `RequiresRange`) that `Program.cs` uses instead of its literal, and
`CommandLineSettingsSource.FormatArgument(qualifiedName, value)`. Round-trip tests, as child 8 did for
`--diagnostics=`.

### Checking options (prevent where simple, report the rest)

`DebugLaunchOptions.Problems(LaunchOptionCatalog)` lists what the game would reject or ignore, each a short text with
the option it concerns:
- **Prevented in the UI:** headless is disabled (and drawn so) unless the benchmark range is on, and takes effect only
  with it, so the rows alone can never make headless without a range; End's minimum follows Start + 1; numbers are
  clamped to their field's range; a stored range that isn't one loads as the default range.
- **Reported, not prevented** (they can come from extra arguments, an older options file, or a changed module set):
  `--headless` typed into Extra arguments without a range (checked with the game's own rule,
  `HeadlessArgument.IsRefused`, over the whole argument list), an override naming a setting no module declares, an override whose text no longer parses
  or validates (`SettingDefinition.TryParse`), a game flag the game no longer declares, and extra arguments that repeat
  a flag a row owns (the game reads the first occurrence, so the row's would silently win or lose). Each shows on its
  row in the section and in the Error List as a warning (source "Launch options", like child 8's "Diagnostics").
- **Play with a problem** that would make the game refuse (headless without a range) is refused with the problem on the
  status line, as `GameLauncher` refuses a second launch; a problem that only drops an option (an unknown setting)
  launches without that option, which is left out of the command line shown.
- **Informational:** with headless on, the features line notes "Headless runs report Memory and Gauges only", since
  `HeadlessBenchmark` drops the others; the checkboxes are left as they are so switching headless off restores them.

### The Launch section (layout)

Under the diagnostics features (child 8), groups in a scrolling column, each row a label and its control:
- **Run:** Seed -- a `NumericField` (int range) and a **Random** toggle (on: field disabled, no `--seed=`), and
  **Use last seed**, which copies the seed from the last session's hello (`LiveDiagnosticsSession`), so a world seen
  once can be replayed (`RandomSeed`'s own reason for always choosing a seed).
- **Benchmark:** a **Benchmark frames** toggle, Start and End number fields (default 600 and 3600, the
  phase-performance-testing skill's range), and **Headless** (disabled unless the toggle is on).
- **Game:** the rows the game added through the hook (below) -- for DungeonCrawlerWorld, Map size.
- **Module settings:** one row per declared setting, grouped by module, sorted by name; changed values marked with a
  reset button that returns the row to its default (and drops the override). Then **Unknown settings**, if any: each
  stored override no module declares, with its value and a remove button.
- **Extra arguments:** a `TextBox`, appended as typed.
- Then, as now, the command line with Copy and the options file's path.

Typed rows from a `SettingDefinition`'s `ValueType`: `bool` -- `Toggle`; integer types -- `NumericField` with the type's
range and no decimals; `float`/`double`/`decimal` -- `NumericField` with three decimals; an enum -- `DropdownField` of
its names; anything else (`string`, `Guid`, `TimeSpan`) -- a `TextBox` whose text is checked by `TryParse` as typed,
invalid drawn as an error and left off the command line.

### The game's hook

`WorkshopTabSet` (what `CreateTabs` already returns) gains `LaunchOptions: GameLaunchOptions`, two lists:
- `Flags`: `LaunchFlag`s, engine types in `Cadence/Workshop/Debug` -- `NumberLaunchFlag(Id, Label, ArgumentPrefix,
  Minimum, Maximum, Step, UnsetLabel)` (unset leaves the flag out), `ToggleLaunchFlag(Id, Label, Argument)` and
  `ChoiceLaunchFlag(Id, Label, ArgumentPrefix, Choices)`. Enough for map size and the next few; anything else waits
  for its first user.
- `Settings`: the `SettingDefinition`s the content build declared, from the same module set the game runs (built-ins
  plus the mods validated from GameShell's `Mods`).

DungeonCrawlerWorld's App adds Map size: `MapSizeArgument` moves from GameShell to Game (`Game.Floors`, public, its
prefix and rule shared), so GameShell parses and the App declares one flag: a `NumberLaunchFlag` from 64 to 1024 in
steps of 64, unset labelled "Streamed (default)". 1024 x 1024 (one neighborhood) is the current soft limit: Workshop
instances are expected to use smaller maps, not larger, so the field offers nothing above it (the game's parser still
takes a larger size typed by hand or in Extra arguments). Any size up to 1024 is the bounded map `FloorBuilder.CreateMap`
starts at (0, 0), so the 32 x 32 starting area always fits from 64 up.

The engine side changes to make the settings reachable and typable (no module declares one yet, so nothing breaks --
initial development, no shims):
- `ModuleDeclarations` keeps the definitions it declared (`SettingDefinitions`), and the App reads them from the content
  build.
- **Enum settings:** `SettingKey<T>`, `SettingValues.Get<T>` and `SettingDefinition<T>` relax `IParsable<T>` to
  `notnull`; `SettingsDeclarations` gets `Declare<T>(...) where T : IParsable<T>` (as now) and `DeclareEnum<T>(...)
  where T : struct, Enum` (parsed by name, case-insensitive, never by number), each making its own definition.
- **Range and description:** `Declare` takes an optional `SettingRange<T>` (minimum, maximum -- also validated, so the
  game and the field agree) and an optional one-line `description` (the row's tooltip). The opaque `validate` stays
  for rules a range can't say.

### Persistence

- The file moves per game: the App passes `%LOCALAPPDATA%\Cadence\Workshop\DungeonCrawlerWorld\launch-options.json`
  (`WorkshopHostOptions.DebugLaunchOptionsPath`, already an option); the shell's default stays for a game that names
  none.
- JSON with a `version` (2); version 1 (features only, child 8's file) is read as features with everything else at
  its default, and the old file is left alone.
- Flag values and setting overrides are stored as text by id and qualified name, **never dropped on load**: a
  setting a mod declares may be missing only because the mod failed validation this run, so its override stays,
  listed under Unknown settings and left off the command line, until the user removes it.

### Running, stopping and finishing

- **Stop** (Shift+F5, VS's binding): a shell command shown after Play while a game or its build is running.
  `GameLauncher.Stop()` kills the process tree (`Process.Kill(entireProcessTree: true)`), sets "Game stopped." and
  frees the launcher; a build in progress is killed the same way and the game never starts. It covers windowed runs
  too (a hung game).
- **What was launched is kept**: `WorkshopDebugging` snapshots the options at each launch (`LaunchedOptions`), so the
  tab describes the running game even after the options are edited.
- **A headless run shows it is running**, with no window to see:
  - The status line, while it runs, reads "Headless benchmark: frame N of END" from the session's latest frame record
    and the launched range (the connection's "dropped N frames" is not shown for headless, where drops are expected).
  - The Debug tab's Live header shows the same, a progress bar over START..END, the launched command line, and the Stop
    button.
- **When the benchmark completes** (headless or windowed): on `BenchmarkCompleted` the Reports section refreshes its
  catalog, selects the run's benchmark report and opens it in its report view; the status line says "Benchmark
  complete: <file> -- open Debug > Reports" unless the Debug tab is already open on Reports. It never switches tabs
  or sections by itself -- jumping away from what the user is doing is disruptive.
- **A headless run that ends without completing** (stopped, crashed, refused with exit code 2) says so on the status
  line with the exit code and, for exit code 2, the game's own error line from the captured output.
- **The fingerprint**: the `[Headless] Fingerprint <hex>` line in the captured output is shown on the status line with
  the completion, so two runs can be compared by eye (the skill checks it the same way).

## Decisions

Settled in review 2026-10-25:
1. **The remaining launch options join the Launch section** of the Debug tab; the section stays shell-owned
   (`Cadence/Workshop/Debug`), the engine's flags typed by the shell and the game's added through the hook.
2. **One set of options per user per game, no named profiles yet** (Research 2). Profiles are a follow-up.
3. **Stored per user under `%LOCALAPPDATA%\Cadence\Workshop\<game>\`**, versioned JSON, never in the repository or
   content; child 8's version-1 file is read once as features.
4. **Each flag is formatted beside its parser, in the engine** (`RandomSeed`, `BenchmarkFrameRange`,
   `HeadlessArgument`, `CommandLineSettingsSource`), with format-then-parse tests.
5. **Arguments are a list, joined by one quoting helper**, used for launching and for the command line shown.
6. **Prevent the simple rules in the UI** (headless needs a range, End after Start, field ranges); **report the rest**
   on the row and in the Error List; refuse Play only for what the game itself refuses.
7. **Seed: a number field, Random, and Use last seed** (from the last session's hello).
8. **Only changed settings are stored and passed** (overrides), each row resettable to its default.
9. **Overrides naming an unknown setting are kept, listed, left off the command line and warned about**, never
   silently dropped.
10. **The game's hook is `WorkshopTabSet.LaunchOptions`**: flags (`NumberLaunchFlag`, `ToggleLaunchFlag`,
    `ChoiceLaunchFlag`) and the declared settings; `MapSizeArgument` moves to Game to be shared.
11. **Settings gain enums (`DeclareEnum`), an optional range and a description**; `ModuleDeclarations` keeps the
    definitions.
12. **An "Extra arguments" box**, appended last, with a warning when it repeats a flag a row owns.
13. **Stop (Shift+F5) kills the game's or build's process tree**, for every run.
14. **A headless run shows its progress** on the status line and the Live header, from the link's frame records and
    the launched range.
15. **A completed benchmark is selected and opened in Reports, but never jumped to**: no tab or section switches by
    itself; the status line points there.
16. **The settings list is built once, when the Workshop starts**; Build and Play does not refresh it.
17. **Map size is offered from 64 to 1024 in steps of 64**: 1024 x 1024 is the current soft limit, and Workshop
    instances are expected to shrink, not grow.
18. **Settings rows are tested with a temporary mod**, deleted once Phase 4's check passes; no example or built-in
    setting is added for it.

## Phases

Each phase ends with the Workshop and the game running and a stop for testing before the next.

### Phase 1 -- Seed, benchmark range and headless (Engine, Workshop shell)

- Engine: `RandomSeed.FormatArgument`, `BenchmarkFrameRange.FormatArgument`, `HeadlessArgument` (used by
  `Program.cs`). Workshop: `DebugLaunchOptions` with seed, range, headless and extra arguments; `CommandLineArguments`;
  the store's version 2 and the per-game path (the App passes it); `Problems` for these options; the Run and Benchmark
  groups and Extra arguments in `DebugLaunchSection`, through `DebugTabView`. UIEngine: `Toggle.Enabled` (dimmed, not
  hit-tested, as `Button.Enabled`) and a read-only `NumericField` ignoring the wheel.
- Tests: format-then-parse for seed (including `int.MinValue`/`MaxValue`) and range; quoting round trip through
  `CommandLineToArgvW`'s rules for spaces and quotes; options save and load (version 2, and a version-1 file read as
  features); headless disabled until the range is on, driven through `UiInputController`; Play refused for `--headless`
  typed into Extra arguments without a range; extra arguments repeating `--seed=` warned about.
- **Built 2026-10-25.**
- Check: open the Debug tab -- expect Random ticked and the command line with no `--seed=`. Untick Random, type
  12345, Play -- expect the command line to show `--seed=12345` and the Live header's seed to read 12345. Close the
  game, tick Random, Play, close, then press Use last seed -- expect the field to hold the seed that run reported.
  Look at Headless -- expect it disabled; tick Benchmark frames -- expect 600 and 3600 and Headless enabled. Set End
  below Start -- expect End to clamp to Start + 1. Close and reopen the Workshop -- expect every value kept, and the
  file under `Cadence\Workshop\DungeonCrawlerWorld\`. Run windowed with the range -- expect the console's benchmark
  table after frame 3600 and the game still running.

### Phase 2 -- Stop, headless progress and the report opening (Workshop shell)

- `GameLauncher.Stop` and the Stop command (Shift+F5); `WorkshopDebugging.LaunchedOptions`; the headless status line
  and Live header progress; `BenchmarkCompleted` refreshing and opening the report in Reports; the fingerprint and
  ended-early lines.
- Tests: Stop kills a running process and a running build (a test target that sleeps), and a later Play starts; a
  scripted session's frame records make "frame N of END"; a `BenchmarkCompleted` message makes the Reports section
  select and open that file (catalog over a temporary folder); a game exiting with code 2 shows its error line.
- **Built 2026-10-25**, awaiting the check below. As built: `GameLauncher.Stop` and `LastExit` (`GameExit`: exit code,
  error lines and the last one, stopped); a command's tile can come and go (`WorkshopCommand.IsShown`), Stop's only while a
  game or build runs; `WorkshopDebugging.Update` owns the status line (launcher, connection, completion, and a headless
  run's progress and ending); the fingerprint line is `HeadlessArgument.FormatFingerprintLine`/`TryParseFingerprintLine`,
  which `HeadlessBenchmark` writes and the shell reads from the game's output; the Live section's launch row (Stop, the
  progress bar for any run with a range, the launched command line) is the Live section's only, never a report viewer's;
  Reports takes up a completion when it next shows (a Refresh, then the report selected and opened, under All if the
  selected tab hides it).
- Check: tick Benchmark frames and Headless, Play -- expect no game window, the status line counting frames toward
  3600 and the Live header's progress bar moving. When it finishes -- expect "Benchmark complete" with the fingerprint
  on the status line, and Debug > Reports showing that `benchmark-*.json` selected and open. Run it again with the same
  seed -- expect the same fingerprint. Play headless with End at 100000 and press Shift+F5 -- expect "Game stopped.",
  no process left in Task Manager, and Play working again. Play windowed and press Shift+F5 -- expect the window to
  close.

### Phase 3 -- Game flags through the hook: map size (Engine, Game, Workshop)

- `LaunchFlag` types and `GameLaunchOptions` on `WorkshopTabSet`; the Game group in the section; `MapSizeArgument`
  moved to `Game.Floors`; the App declaring Map size; unknown stored game flags reported.
- Tests: a number flag formats and parses back through the game's own parser; an unset flag is left out; a stored
  value for a flag the game no longer declares is kept and warned about; the App's tab set carries Map size.
- Check: open the Debug tab -- expect Map size reading "Streamed (default)" and no `--map-size=` on the command line.
  Set 512, Play -- expect `--map-size=512`, the starting area, a bounded map whose edge can be walked to, and no
  window shift. Step it with the wheel -- expect steps of 64 between 64 and 1024, and nothing above 1024. Set it back to
  unset -- expect the flag gone.

### Phase 4 -- Module settings (Engine, Workshop)

- Engine: `ModuleDeclarations.SettingDefinitions`; `DeclareEnum`; `SettingRange<T>` and `description`. The App passes
  the content build's definitions; the Module settings and Unknown settings groups; typed rows; overrides only, with
  reset; `CommandLineSettingsSource.FormatArgument`; setting problems into the Error List.
- **A temporary mod to see it with** (Decision 18): a throwaway project under `DungeonCrawlerWorld/Mods/` built into
  GameShell's `Mods` folder, one module declaring a bool, an int with a range, a double, an enum and a string, each
  with a description, and logging their resolved values at startup so the game's output shows they arrived. It is
  deleted -- project, solution entry and built DLL -- once the check below passes, and is never committed.
- Tests: each value type makes its control and an override the game's `SettingsCatalog.Resolve` reads back as the same
  value (bool, int, double, enum, string with spaces); an enum override is written by name; a range is enforced by the
  field and by validation alike; a value equal to the default is no override; an override for an undeclared setting is
  kept, listed, left off the command line and warned about; `BuiltInModulesTests` still build with no setting declared.
- Check (with the temporary mod built into GameShell's `Mods`): open the Debug tab -- expect the mod's five rows under
  its module, at their defaults, each description as its tooltip, and nothing for them on the command line. Change
  each -- expect a `--setting=Module.Name=Value` per changed row (the string's quoted when it has a space), the row
  marked, and the game's output logging each new value with no settings failure line. Type past the int's range --
  expect the field to clamp. Reset a row -- expect its argument gone. Then delete the temporary mod and restart the
  Workshop -- expect its changed settings under Unknown settings with a warning each in the Error List, the game
  starting without them, and the remove buttons clearing them.

## Open questions

- None. Settled 2026-10-25: launch profiles are later (Decision 2); the settings rows are tested with a temporary mod,
  then deleted (Decision 18); Build and Play does not refresh the settings list (Decision 16); 1024 x 1024 is map
  size's soft limit (Decision 17); a finished benchmark never jumps to Reports (Decision 15).

## Out of scope / follow-ups

- **Named launch profiles and sharing one** in the repository (Rider's Store as project file): a profile dropdown with
  New, Duplicate, Rename and Delete over the same options; the version-2 file leaves room (a list with one entry).
- **Refreshing the settings list after a build**, if the Workshop later rebuilds its content session for another reason.
- **Several instances at once** (Godot's run instances): one game at a time stays `GameLauncher`'s rule.
- **Saving a live capture** -- TODO.md "Workshop Debug tab: save a live capture"; its "record to file as it streams"
  checkbox would join this section.
- **A/B runs from the tab** (two builds, the skill's headless A/B): the skill keeps doing them; the Reports section
  already compares the results.
- **Settings changed at runtime** over the link (console variables) -- TODO.md "Console variables and console
  commands". Every setting stays startup-scoped here.
- **Environment variables and the working directory** per launch (VS profiles have both): nothing needs them yet.
