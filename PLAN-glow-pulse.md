# Glow pulse: Static or Pulse on every glow

(From TODO.md "Folder glow blink". Investigated 2026-11-07, decisions settled the same day. One phase.)

## Verdict in one paragraph

Every glow is one quad drawn by `GlowRenderer.Draw`, which already takes an `alphaMultiplier`. A pulse is a
time-varying value for that multiplier, so the shader, the batch and `GlowMode` stay as they are. A new UIEngine value type,
`GlowAnimation`, is either `Static` (always full strength) or `Pulse(period, minimumStrength, cycles)`. A pulse is a smooth
cosine from full strength down to the minimum and back, once per period, for `cycles` cycles (default 5), and then holds steady at
full strength. With no cycle count, or `int.MaxValue`, it pulses forever. A pulse counts from a start that its holder
restarts: `GlowPulseClock`, a small mutable struct that `Element` keeps for each glow, and that any other drawer of a glow
can keep too. Every way of asking for a glow takes one. Everything defaults to `Static`, so no current glow
changes. `NotificationCenter` opts in: a notification added unread restarts a 2 s, 25%-minimum, 5-cycle pulse on the
Folder and on that notification's category badge, in step. After that, both glow steadily while they have unread notifications, and
not at all once they have none.

## How it works today

- `GlowRenderer.Draw(target, unitRectangle, bounds, color, mode, alphaMultiplier = 1, style)` (`Cadence/UIEngine/UI/GlowRenderer.cs`)
  draws one quad with alpha `FadeMaximumAlpha * alphaMultiplier`. It is static and keeps no state.
- `Element.SetGlow(isGlowing, color, mode)` / `SetOverlayGlow(...)` (`Element.cs:225`, `:237`) set fields.
  `Element.Draw(GameTime)` draws them (`:681` before the header, `:734` over content). `Build` resets them for pooling (`:493`).
  The glow draws in every display mode, so a minimized Folder's border glows.
- `NotificationCenter.RefreshUnreadSummary` (`Presentation/UI/Notifications/NotificationCenter.cs:371`) glows each
  category's summary window while it has unread notifications, and glows the `_folder` while any category does,
  both in `WindowPalette.AttentionGlow`. It is called when a notification is added unread (`AddNotification(..., showImmediately: false)`, `:143`),
  when one is minimized back into the queue (`MinimizeNotification`, `:301`), and when one is opened (`:160`).
- Glows drawn directly through `GlowRenderer.Draw` in a `DrawContent`: hotbar armed, selected and drop-target glows, the
  `ToggleActiveMarker`, inventory compare-eligible, grid squares, empty trade slot, tooltip band rows and the map's
  dodging glow (`MapRenderer`). Each one already passes its own alpha or the default.
- Time: `Draw(GameTime)` gets the host's `GameTime`. `TotalGameTime` keeps running while the simulation is paused
  (the `GameLoop` pauses only `EcsContext.Update`), which is the "presentation time, not simulation frames" rule
  that TODO "Animation primitives" sets for UI animation. The Workshop's area-loading spinner (`AreaMapView.cs:233`) already
  animates from `gameTime.TotalGameTime` the same way.

## Industry guidance on pulse speed

- **WCAG 2.3.1 / 2.3.2 (Level A / AAA):** at most 3 flashes in any one second. This is the only normative limit.
  `GlowAnimation` refuses a period shorter than 1/3 s, so no caller can configure a pulse that breaks it.
- **Accessibility guides** (BC Gov digital, UI/UX Atlas, TestParty): use a slow pulse of 1-2 s per cycle, or ≤ 1 Hz, for
  status indicators. A notification dot that "pulses rapidly when new content arrives" is listed as a typical failure.
- **Motion and attention heuristics** (common design-skill references; Material 3 and Apple HIG give no figure for
  a looping indicator): an ordinary repeating attention pulse runs 2-3 s per cycle, and an urgent one 1-1.5 s. Stop after 3-5 cycles.
- **Settled:** 2 s period, 25% minimum strength, 5 cycles (10 s), then steady. The period, minimum and cycle count are
  set per use. 5 cycles is the default for every pulse.

## Phase 1: `GlowAnimation` and `GlowPulseClock`, wired through every glow, the Folder and badges pulsing

### `GlowAnimation` (new, `Cadence/UIEngine/UI/GlowAnimation.cs`)
- `readonly record struct GlowAnimation`, with `default` meaning `Static`. Members:
  - `const int DefaultPulseCycles = 5`, `const float DefaultPulseMinimumStrength = 0.25f`, and
    `static readonly TimeSpan MinimumPulsePeriod` (1/3 s, from WCAG 2.3.1).
  - `static GlowAnimation Static`.
  - `static GlowAnimation Pulse(TimeSpan period, float minimumStrength = DefaultPulseMinimumStrength, int? cycles = DefaultPulseCycles)`.
    `null` or `int.MaxValue` cycles means forever. It throws `ArgumentOutOfRangeException` for a period below `MinimumPulsePeriod`,
    a minimum outside 0..1, or fewer than 1 cycle. Callers pass constants, so a bad value fails at the first call, not at draw time.
  - `float StrengthAt(TimeSpan elapsedSincePulseStart)`. It returns 1 for `Static`, and 1 once a finite pulse has finished
    (`elapsed >= cycles × period`). Otherwise it returns `minimum + (1 - minimum) · (0.5 + 0.5 · cos(2π · elapsed / period))`.
    That curve is at full strength on every cycle boundary, so a pulse starts bright and hands over to the steady glow
    with no jump. The elapsed time is reduced modulo the period in `double` ticks before converting to `float`, so a forever-pulse
    keeps its precision after hours.
  - `bool IsPulsing` (a pulse at all) and `bool PulsesForever`, so tests and inspection can tell them apart.

### `GlowPulseClock` (new, same folder)
- A mutable struct holding the start of the current pulse: `TimeSpan? _start` and a `bool _restartPending`.
  - `Restart()` marks it pending. The caller usually has no clock (`SetGlow` is called from input or event handling, not
    from a draw), so the start is taken at the next `ElapsedAt`. Two clocks restarted in the same update therefore take the same frame's
    time and pulse in step: the Folder and its badge.
  - `TimeSpan ElapsedAt(TimeSpan presentationTime)` takes the start if one is pending, or if it has never started, and returns `presentationTime - start`.
  - `Reset()` returns it to never-started (for pooling).
- This is what any glow drawn outside `Element` keeps if it wants a finite pulse. A forever-pulse can skip it and pass
  `gameTime.TotalGameTime` directly.

### `GlowRenderer`
- New overload: `Draw(target, unitRectangle, bounds, color, mode, GlowAnimation animation, TimeSpan elapsedSincePulseStart, float alphaMultiplier = 1, style = default)`.
  It forwards to the existing one with `alphaMultiplier × animation.StrengthAt(elapsed)`. No existing caller changes.
  They all stay `Static` through the old overload.

### `Element`
- `SetGlow(bool isGlowing, Color? color = null, GlowMode mode = ExteriorFade, GlowAnimation animation = default)`, and the same
  added to `SetOverlayGlow`. New fields `_glowAnimation` / `_overlayGlowAnimation`, and `_glowPulse` / `_overlayGlowPulse` (`GlowPulseClock`).
  A public `GlowAnimation` getter goes beside `GlowColor`/`GlowMode`.
- **When a pulse starts:** `SetGlow` restarts the clock when it turns the glow on from off, or when it changes the
  animation. Calling it again with the glow already on and the same animation changes nothing, so `RefreshUnreadSummary`
  running on every read never restarts a pulse. `RestartGlowPulse()` / `RestartOverlayGlowPulse()` restart it explicitly:
  "something new arrived while already glowing".
- `Build`'s pooling reset sets both animations to `Static` and `Reset()`s both clocks.
- `Draw` calls the new overload with `_glowPulse.ElapsedAt(gameTime.TotalGameTime)`: one cosine per pulsing element per
  frame, and nothing for a static or finished one beyond a comparison. No cache is needed, because there is no text or layout involved.
- Doc: the `SetGlow` `<summary>` stays one sentence. The pulse rules (start on off→on or animation change, explicit restart,
  steady after its cycles) go in `<remarks>`.

### `TooltipRow`
- Add the optional record parameter `GlowAnimation GlowAnimation = default`. `Tooltip` keeps one `GlowPulseClock` and restarts it when
  `ShowNear` takes changed content (unchanged content is already skipped, so a re-pushed tooltip doesn't restart). `TooltipRowDrawing.Draw`
  receives the elapsed time from `Tooltip.DrawContent(gameTime)`. No row uses it yet. It exists so that "any glow" covers tooltip band rows too.

### `NotificationCenter`
- `private static readonly GlowAnimation UnreadNotificationGlowPulse = GlowAnimation.Pulse(TimeSpan.FromSeconds(2));`
  (25% minimum and 5 cycles are the defaults).
- `RefreshUnreadSummary` passes it to both `_folder.SetGlow(...)` and `entry.SummaryWindow.SetGlow(...)`. Unread notifications mean a glow
  (pulsing, then steady), and none means no glow, as today.
- `AddNotification(..., showImmediately: false)` adds the notification, refreshes, then calls `_folder.RestartGlowPulse()` and that
  category's `SummaryWindow.RestartGlowPulse()`. Each new unread notification starts a fresh 5-cycle pulse on the Folder
  and its own badge, in step. Other categories' badges keep their steady glow.
- **A minimized notification** returning to the queue isn't new, so it doesn't call `RestartGlowPulse`. If it is the only
  unread notification, though, it turns the glow on from off, and that starts a pulse by `SetGlow`'s rule. The pulse shows where it went.
- Delete TODO.md "Folder glow blink". Update the "Animation primitives" entry's list to drop it and to name
  `GlowAnimation`/`GlowPulseClock` as the first small piece of "Curves" (glow pulses), so that work folds it in.
- Add an accessibility TODO entry: when a reduced-motion setting exists, `GlowAnimation.StrengthAt` is the single place to return 1.
- Add one line to the CLAUDE.md UIEngine bullet: a glow is `Static` or `Pulse(period, minimum, cycles)` (`GlowAnimation`, 5
  cycles by default, `null` = forever, period never shorter than 1/3 s), timed from its `GlowPulseClock` on presentation time.

### Out of scope
- **The map's tile-overlay ring** (`MapGameOverlay` → `SpriteEffectTileOverlay`): it is written into texels, not drawn through
  `GlowRenderer`. It can take a `GlowAnimation` when something asks it to pulse.
- **Converting an existing direct glow** (armed slot, toggle marker and the rest): each stays `Static` unless asked.

### Tests
- `Tests/UIEngine/GlowAnimationTests.cs`:
  - `Static` is 1 at any time.
  - A pulse is 1 at 0 and at whole periods, equals the minimum at half a period, and lies strictly between the two at a quarter period.
  - It stays within [minimum, 1] across a sweep of times.
  - A 5-cycle pulse is still pulsing at 4.5 periods (at the minimum) and is 1 at 5 periods and every time after.
  - The default cycle count is 5.
  - `null` and `int.MaxValue` cycles still pulse at 1000.5 periods, with no drift at 10 h plus a quarter period.
  - These throw: a period below 1/3 s, a minimum outside 0..1, and cycles below 1.
- `Tests/UIEngine/GlowPulseClockTests.cs`:
  - A never-started clock starts at its first `ElapsedAt` (elapsed 0).
  - `Restart` takes the next `ElapsedAt`'s time as the new start.
  - Two clocks restarted before the same `ElapsedAt` time report equal elapsed times.
  - `Reset` returns to never-started.
- `WindowGlowTests`:
  - The default animation is `Static`.
  - `SetGlow` with a pulse stores it.
  - The glow turning on from off restarts the pulse, and so does changing the animation.
  - Re-calling `SetGlow` with the glow already on and the same animation doesn't restart it.
  - `RestartGlowPulse` restarts it.
  - A pooled, pulsing window comes back `Static` with its clock reset. The same checks cover `SetOverlayGlow`.
  - The elapsed time is read through what `Draw` uses, either a small `internal` accessor visible to Tests or a recording target. I'll pick
    whichever the existing Element tests already use, with no production shim.
- `SpriteDrawTargetTests`: the new overload, given a pulse at half a period, records colour × `FadeMaximumAlpha × 0.25`, and after its cycles it records colour × `FadeMaximumAlpha`.
- A notification test (beside the existing `NotificationCenter` tests, if any; otherwise a new one in `Tests/Presentation`):
  1. Adding an unread notification glows the Folder and that category's badge with a 5-cycle 2 s pulse.
  2. A second one restarts both clocks.
  3. Opening the last unread notification turns both glows off.

### In-game test checklist
1. Trigger an unread achievement notification. The Folder's border breathes bright → dim → bright every 2 s, 5 times (about 10 s), never
   fully dark, and then stays steadily lit.
2. Expand the Folder. That category's badge pulsed in step with the Folder and is now steady too.
3. While both are steady, trigger another unread notification in the same category. Both pulse 5 more times, in step, then settle.
4. Trigger one in a different category. The Folder and the new category's badge pulse, and the first category's badge stays steady.
5. Pause (Escape / menu mode) during a pulse. The pulse keeps going and ends on time.
6. Read every notification. Both glows go off at once, and nothing is left half-lit.
7. Minimize an open notification back into an empty Folder. The Folder starts glowing with a 5-cycle pulse. Minimize a second one while
   it's already glowing: there's no new pulse.
8. Check that nothing else changed: the armed hotbar slot, a lit toggle item, the inventory compare glow, the tooltip price band and a dodging entity on the
   map all glow steadily as before.
