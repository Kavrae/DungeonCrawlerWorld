# HP and Mana Numbers on the HUD Bars

(Pre-implementation. Replaces TODO.md's "HP and mana numbers on the HUD bars" entry, which is
deleted when this lands.)

## Context

Inspired by Dungeon Settlers: draw "15 / 20" in white, centred on the player's health and mana bars.

What exists today:
- `PlayerHealthBarContent` and `PlayerManaBarContent` (Presentation/UI/Content) each compute a
  fraction in `Update` and hand it to `ResourceBarRenderer.Draw` in `DrawContent`. Neither keeps the
  numbers it computed the fraction from.
- Health: `HealthQueries.TryGetTotals` gives current/maximum as floats (summed across body parts for
  complex health). The fraction divides by the *effective* maximum,
  `StatModifierMath.GetEffectiveValue(..., MaximumHealth, maximumHealth)`, also a float.
- Mana: `ManaComponent.CurrentMana`/`MaximumMana` are floats; `MaximumMana` is typically 2-12.
  Effective maximum goes through `StatModifierMath.GetEffectiveValue(..., MaximumMana, ...)`.
  Affordability is `CurrentMana >= manaCost` (`ActionActivationSystem`).
- `ResourceBarRenderer.Draw` is also used by `FractionBarElement` (Inspection window, HealthWindow's
  per-body-part rows) and `PlayerHealthHoverContent`. None of those want numbers on the bar.
- `PlayerHealthBarContent` already takes a `FontService`; `PlayerManaBarContent` doesn't.
- `ContrastTextRenderer.Draw` is the outlined white text primitive that `HotbarContent` and
  `PlayerStatusEffectsContent` already use for counts over busy backgrounds.
- The bar windows are `HudChrome.EntrySize.Y * 0.75` = ~16 px tall, so the content area is ~14 px
  and the font has to be small (~11 px).

## Design

### Draw the text on top; leave `ResourceBarRenderer.Draw` alone

`ResourceBarRenderer.Draw` keeps its current signature. Four callers draw bars with no numbers, and
adding optional text parameters to all of them for two callers is the wrong trade. Instead, add one
sibling method to the same class:

```csharp
public static void DrawCenteredValueText(SpriteBatch spriteBatch, SpriteFontBase font, Rectangle bar, string valueText)
```

It measures the text, centres it on `bar`, rounds the position to whole pixels (outlined text at
half-pixel offsets blurs), and draws it with `ContrastTextRenderer.Draw`. Both HUD contents call it
right after `ResourceBarRenderer.Draw`, so the centring lives in one place.

### Rounding: each number rounds the way that doesn't lie

The numbers must agree with what the game will actually do:

| Value | Rounding | Why |
|---|---|---|
| Current health | Ceiling | "0 / 20" only when the player is really at 0. At 0.3 HP you are alive, so it shows 1. |
| Current mana | Floor | Casting needs `CurrentMana >= cost`. At 2.7 mana, "3 / 5" would say a 3-cost spell is affordable when it isn't. |
| Effective maximum (both) | Nearest (`MidpointRounding.AwayFromZero`) | A modifier can make it fractional (20.4). There's no gameplay threshold on the maximum, so nearest is the most honest. `Math.Round`'s default banker's rounding would turn 20.5 into 20 but 21.5 into 22. |

The displayed current is then clamped to `[0, displayed maximum]`, so rounding can never produce
"21 / 20".

These rules live in one small static formatter, `ResourceBarValueText`, in Presentation/Rendering
next to `ResourceBarRenderer`, so tests can pin them:
- `static int DisplayedHealth(float current)` / `static int DisplayedMana(float current)` /
  `static int DisplayedMaximum(float effectiveMaximum)`.

### No string allocated per frame

Formatting `$"{current} / {maximum}"` every frame is garbage every frame, and this game's frame
spikes are GC-driven (see CLAUDE.md "Scale"). Each content keeps the last displayed integer pair and
the string built from it, and rebuilds the string only when either integer changes. Put that in
`ResourceBarValueText` as a small instance type (the last pair plus the cached string, with one
`Update(int current, int maximum)` returning the string), one instance per content, so the two
contents don't each hand-roll the cache.

### No resource, no numbers

When the bar draws its "no resource" grey fill (`_hasHealth`/`_hasMana` false), no text is drawn.
"0 / 0" over a grey bar would read as "empty" rather than "doesn't have this resource", which is the
distinction the grey fill exists to make.

### Font size

New `FontChrome.PlayerResourceBarValueFontFraction` (start at `0.8f`), applied to the bar's content
height the way `PlayerStatusEffectsContent` sizes its fonts from `Size.Y`. Both contents use it.
Settle the exact value by looking at it in-game: the outline adds 1 px on each side, so the text
plus outline must fit inside the bar without touching the border.

## Changes

1. **`Presentation/Rendering/ResourceBarRenderer.cs`**: add `DrawCenteredValueText`.
2. **`Presentation/Rendering/ResourceBarValueText.cs`** (new): the rounding rules and the cached
   string.
3. **`Presentation/UI/Chrome/FontChrome.cs`**: add `PlayerResourceBarValueFontFraction`.
4. **`PlayerHealthBarContent`**:
   - Get the font in `Initialize` from the fraction above.
   - In `Update`, after computing the fraction, feed ceiling(current)/nearest(effective max) into its
     `ResourceBarValueText`.
   - In `DrawContent`, when `_hasHealth`, call `DrawCenteredValueText` after `ResourceBarRenderer.Draw`.
   - The hover popup is unchanged; the text sits under the mouse like the bar does.
5. **`PlayerManaBarContent`**:
   - Take `FontService` in the primary constructor.
   - Same three steps as health, with floor(current).
6. **`DungeonCrawlerWorld/ShellBootstrapper.cs`**: pass `presentation.FontService` to
   `PlayerManaBarContent`.
7. **TODO.md**:
   - Delete the "HP and mana numbers on the HUD bars" entry.
   - In "Extract a shared tick-fraction HUD bar element", add the value text to the list of what the
     two contents duplicate. It's still only two copies, so extracting it isn't part of this plan.

## Tests

- **`ResourceBarValueTextTests`** (new): the rounding table above, one case per row plus the edges:
  - health 0.3 shows 1, health 0 shows 0;
  - mana 2.7 shows 2, mana 3.0 shows 3;
  - maximum 20.4 shows 20, maximum 20.5 shows 21;
  - current above the maximum after rounding is clamped;
  - the same pair returns the same string instance, and a changed pair returns a new one.
- **`PlayerHealthBarContentTests`**: after `Update`, the displayed text for the existing
  simple-health fixture (50/100) is "50 / 100". The complex-health fixture shows the body-part totals.
  A player without health shows no text. Read it through an `internal` accessor, the same kind of seam
  as `HoverPopup`.
- **`PlayerManaBarContentTests`** (new, mirroring the health fixture): fractional current mana floors,
  a `MaximumMana` stat modifier changes the displayed maximum, and a player with no `ManaComponent`
  shows no text.

## Manual verification (in game)

- Both bars show "current / max", centred, readable over every fill colour (full, mid, low), with the
  outline not touching the bar border.
- Take damage and regen: health counts up and down, and never shows 0 while alive.
- Spend mana on a spell whose cost is exactly the displayed current: it casts. At a displayed current
  below the cost, it doesn't.
- Equip or apply something that changes max health or max mana: the maximum updates.
- Complex-health player: the numbers match the sum of the hover popup's per-part rows.
- A fresh player with no mana ability: the grey mana bar has no text.
