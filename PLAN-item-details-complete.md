# Plan: Item Details in two modes, simple and advanced, showing everything an item does

From TODO "Item Details for toggle items: show what the toggle holds". It was widened twice:
- to every fact about an item that Item Details (`ItemDetailsWindow`, `ActivatableDetailsSections`) leaves out today,
  checked against Final Fantasy XIV's and World of Warcraft's item details (FF14 preferred);
- then, on purpose, to **two modes**:
  - **Simple** (the default): the item's stats as they apply to the player, at a glance.
  - **Advanced**: every line, the modifiers behind each value, and the tooltips.

Status: **complete 2026-11-20.** Phases 1-5 are built and the wrap-up is done:
- IMPLEMENTATION-NOTES "Item Details: simple and advanced modes, values as used";
- the Presentation clause in CLAUDE.md;
- the TODO entry removed, and "Clean up item and action tooltips" updated.

The one-screen findings stand without adjustment: every pane fits, and side-by-side comparison panes are the fix if a
stacked comparison ever overflows.

Phase 5 as built:
- **Restrictions.** `ItemRestrictions.Of(item)` (Game.Modules.Inventory) lists an item's `ItemRestriction`s, today
  only `Untradable` (`CanTrade` false); a later restriction is added there.
  - Item Details shows each, by `ItemFactsText.NameOf`, on its own line between the divider and the tags, with
    `ItemFactsText.DescriptionOf` as its tooltip.
  - There is no line when nothing applies. `BuildTagsSection(..., withDivider: false)` lets the restrictions share the
    tags' divider.
- **"Opens into"** after Resources for an item with `Contents`, in both modes, from `ItemFactsText.Contents`:
  - `RandomSingleStackContents` reads "1-10 of one random item";
  - `SetItemContents` reads "Health Potion x3", by name through `EffectTextNames.Items`, which `EffectTextNames.Of`
    now fills from the catalogs;
  - `ItemFactsText.DescribedContentsKinds` lists the kinds described, and a test fails any `IItemContents` missing
    from it.
- **Buttons on the name row** (review follow-up). Stop Comparison, Compare and the mode button sit top-right on the
  name's row (`ItemDetailsWindow.BuildNameRowWithButtons`; `BuildNameRow` returns its row and leaves
  `reservedRightWidth`). The bottom row is the tags alone, with their descriptions as a tooltip.
- **Tests:** `ItemDetailsModeTests` covers:
  - a loot box's "Opens into" line and "Untradable" above its tags, in both modes;
  - a tradable item showing neither;
  - every contents kind being described;
  - set contents read by name.
  `ItemWindowCoordinatorTests` checks the buttons sit right of the name.

**One-screen evaluation** (2026-11-20). Every built-in item and a Bronze Adventurer Box were measured in both modes at
the game's defaults: a 1600x900 window, and Item Details at the Inventory window's width (a third of the screen, 520
px of content).
- **Tallest single pane:** Toxic Idol and Scroll of Torch at 456 px (advanced), then Dread Idol at 444 and Wand of
  Fireball at 440 (both modes). The shortest real item is the Hotkey Expansion Potion at 294 (simple). Every pane fits
  with about half the screen to spare.
- **Stacked comparison** (simple, the popup below the pinned pane): the tallest pair, two Wands of Fireball, is about
  886 px with the gap. That is just under 900, and only if the pinned pane starts at the top of the screen, so a
  wand-to-wand comparison can run off the bottom.
- **No adjustment made.** The evaluation's options in order are: the two-column grid, the Activation section's empty
  third zone, side-by-side comparison panes (FF14's), then cutting the description. Side by side is the one that
  fixes the comparison case.

Phase 4 as built:
- **The aura section.** The effects section builds entry by entry (`ItemComparisonStatExtraction.ExtractEntryStats`).
  Each `AuraSourceGrant` is followed by `ActivatableDetailsSections.BuildAuraSection`, in Item Details and Action
  Details; nested grants (inside a chained effect) get none. The section holds:
  - a heading: the glow swatch "●" in the aura's colour, then "Poison aura";
  - Advanced only: "Strength 16 at the source, 4 at the edge" ("Strength 8 to the edge" when they're equal), then
    `AuraDetailsWindow.StrengthLines`;
  - "Each second:", then the aura's effects at the grant's as-used strength (`ActivationPreviews.PreviewAura`, passed
    to the sections as an `AuraEffectPreviews` delegate). In advanced mode, a Power aura whose edge differs adds the
    same lines at the edge strength, the two sets ending "at the source" and "at the edge". These lines carry no
    change in parentheses, since an aura's amounts are per point of strength; their tooltips keep the breakdown;
  - the reach: a diamond of the as-used size (`TargetShapePreviewGeometry.ComputeDiamondOffsets`, over
    `ManhattanDiamond`), each cell shaded toward the glow colour by `AuraFalloff.ValueAt`, through
    `TargetShapePreviewElement`'s new `cellFill`. It sits right of the lines and is capped at 96 px tall;
  - Advanced only: the aura's description;
  - a glow-only aura (Light) shows its heading and reach alone.
- **Toggles.** `BuildActivationSection(..., isToggle)` draws no grid for a toggle and has no Shape, Range or Area Size
  line in either mode (`ExtractActivatorStats(..., showsTargeting: false)`). An Immediate toggle in simple mode has no
  Activation section at all.
- **Action Details** takes an optional `ActivationPreviews`; the Workshop passes its session's, so an action's aura
  lines are at its strength.
- **Not done as planned:** `AuraDetailsWindow` keeps its own rows. The section needs a grant's strength and size,
  which an aura alone doesn't have. The two share `StrengthLines` and the effects section.
- **Tests:** `ItemDetailsModeTests`:
  - the Toxic Idol in both modes: the aura section after its grant, "Each second", source and edge lines, one reach
    grid and no Self shape;
  - Scroll of Torch's glow-only aura after its grant;
  - the Dread Idol reading the same at source and edge.

Phase 3 as built:
- **Mode state.** `ItemDetailsModeSetting` (Presentation.UI.Inventory) is owned by `ItemWindowCoordinator`
  (`ItemDetailsMode`). It is handed to both `ItemDetailsWindowController`s, so it lasts the session and survives
  closing.
  - The window reads it through `ChosenMode`, and the mode button calls `OnModeToggleRequested`.
  - `ShownMode` is Simple while comparing, and always Simple on the comparison popup (`AlwaysSimple`, from the
    controller's `isComparisonPane`).
  - With no `ChosenMode` (the Workshop, tests) the window is advanced.
  - The window rebuilds when the shown mode changes.
- **The mode button** follows Compare in the tags row, labelled with the mode it switches to
  (`ItemDetailsWindow.ModeButtonLabel`). It is hidden while comparing and on the popup.
- **Activation lines** (`ExtractActivatorStats(..., mode, names)`):
  - Simple shows no Shape, Range or Area Size lines, and the timing as one word ("Delayed", "Freecast"), nothing for
    Immediate.
  - Advanced shows "Timing: X", with `TimingText.Tooltip` over it, using the as-used lock
    (`ActivatablePreview.ActionLockFrames`: the activator's own lock, else the player's standard one).
  - A lock override and a cooldown show in both modes, and so does "Cancels your current windup"
    (`ReleasesActionLock`).
  - Item Details Comparison extracts in simple mode.
- **Advanced values.** `ItemComparisonStat` carries `ChangeText`, `ChangeIsBuff`, `TooltipText` and a `StatUnit`.
  - `PreviewedValueText.Change` gives "(+7)": one changed value's difference; several values' shared difference, else
    their shared multiplier ("(x2)"), else each difference. A base-and-result pair shows once, so a damage range with
    equal ends is one value.
  - `PreviewedValueText.Tooltip` gives "Base", one line per step (a modifier's source through
    `ActionSourceNaming`, carried by `EffectTextNames.Sources`; an ability score; aura strength; a scroll's
    Intelligence), then "Total".
  - Every value is better higher, so a rise is green and a fall red.
- **Rendering.** A line is a `LineStyle`: text colour plus an optional suffix in its own colour, drawn as its own
  element after the text's measured width (`ActivatableDetailsSections.AddStatLine`).
  - Advanced mode's suffix is the change.
  - Comparison's suffix is the difference from the best of the others, on a better line only
    (`ItemDetailsWindow.ComparisonDifferenceText`, in the stat's unit).
  - The colours are `ActivatableDetailsSections.BuffColor`/`DebuffColor`.
- **Follow-ups from review:**
  - A wand's charges are in their own "Resources" section after Activation (`ExtractResourceStats`,
    `BuildResourcesSection`), which ammunition will use too.
  - The compared item's pane has no Compare button. Windows are pooled, so the controller now always assigns
    `OnCompareRequested`, null on the comparison pane.
  - The shape preview numbers its cells from 2, the caster's own cell counting as the first.
- **Currency.** Simple shows "Sell price NG", advanced "Buy price NG, sell price NG".
  - The prices come from `IItemShopPricingSource.ReferencePricesFor`: the open shop's flat prices through
    `ShopActions`.
  - Without a shop, both are `GoldValue`.
- **Tests:**
  - `DetailsWindowsTests`: every built-in item in both modes, simple's left-out lines and timing word, advanced's
    timing tooltip.
  - `ItemWindowCoordinatorTests`: the mode button, the choice surviving a reopen, comparison forcing simple and
    hiding the button.
  - `ItemDetailsModeTests`: change and tooltip rules, difference units, and the difference on the better side only.
  - `ActivationPreviewTests`: an advanced line's change and breakdown from a real preview.

Phase 2 as built:
- **Each entry previews its own amounts.** `IEffectEntry.PreviewAmounts(in EffectContext, EffectAmountPreview)` sits
  beside `Apply` and calls the same helpers.
  - `DirectDamage` shares `ScaleBySource` with its `Apply`.
  - Drains use `AmountFor`.
  - Everything else uses `EffectModifiers`, `EffectDurations` and `HealthHeal.ScaleByHealingModifiers`.
  - An entry with no amount keeps the default, which does nothing.
- **Steps are recorded, not recomputed.**
  - `StatModifierMath.GetEffectiveValue` and `AbilityScoreTagBonus.Compute` take an optional
    `List<ValueContribution>`, filled in the same loop that sums them.
  - `EffectContext.AmountPreview` carries it, so `EffectModifiers` records while previewing and passes null otherwise.
  - `EffectDurations` records a scroll's multiplier.
  - `ValueContribution` (Game.Modules.StatModifiers) is kind (Modifier with its `ActionSource`, AbilityScore,
    Strength, ScrollIntelligence), operation and amount.
- **Percentages of the target's maximum** (damage, healing, mana restored) are previewed as `scale(f) - scale(0)`:
  their multipliers alone (`EffectAmountPreview.EndFraction`). A flat additive modifier adds health or mana, not
  percent, so it is left out of the percentage.
- **The view.** `GameViews.ActivationPreviews` (Game.Views) has three members:
  - `Preview(entity, definition)` gives an `ActivatablePreview`: each entry's amounts, keyed by entry reference and
    nested ones included, plus the as-used `Targeting` and a scroll's `Range`/`AreaSize`.
  - `PreviewAura(aura, power)` gives an aura's effects at a strength, sourceless, through
    `AuraDefinition.MagnitudeAt`, which the aura tick now uses too.
  - `ChangeStampOf(entity)` combines the entity's stat modifier chain version and its ability scores' version.
- **Shared helpers.** `ScrollScalingEffects.MultiplierFor` replaces the two copies of the Intelligence lookup in
  `TargetResolution` and `ItemActivationSystem`.
- **Presentation.**
  - `EffectFormatting.FormatEntry` and `ItemComparisonStatExtraction` take the previews and show and compare the
    as-used numbers. Health and mana amounts show to one decimal place.
  - Item Details previews for the player, including the compared items and the shape grid's targeting, and rebuilds
    when the stamp changes.
  - The Workshop has no player, so it shows the definitions' own values.
- **Not yet shown:** the contributions, the as-used action lock and the timing tooltip. They are Phase 3's.
- **Other changes in this phase:**
  - A flat-only heal reads "Heals N health" (was "Heals 1 + 0% of max health").
  - A tome's description puts the spell's description on its own line.
- **Tests:** `ActivationPreviewTests`, each comparing a preview against applying the entry to a test dummy:
  - damage;
  - aura power placed, and its contribution's source;
  - a scroll's range;
  - the Healing aura at its shrine's strength 16 ("Heals 16 health");
  - the Poison aura at the Toxic Idol's strength;
  - a Flat aura at any strength;
  - the change stamp;
  - a preview changing nothing.

Phase 1 as built:
- `EffectFormatting.FormatEntry(entry, EffectTextNames, DetailsMode)`.
  - `EffectTextNames` (Presentation.UI) carries the tag registry, the terrain registry and the status-effect displays,
    each optional with a raw-name fallback, so the hover and hotbar summaries needed no new dependencies.
  - The details windows and sections take it in place of the tag registry (`EffectTextNames.Of(GameCatalogs)`).
  - `DetailsMode` (Presentation.UI) is `Simple`/`Advanced`. Until Phase 3, Item Details is simple; the Workshop's
    Action and Aura Details are advanced. `CostText` is always simple.
- Plural tag names: `pluralDisplayName` on `DeclareRoot`/`Declare`, `GameplayTagRegistry.GetPluralDisplayName`, and the
  built-ins' plurals in `GameTags.PluralDisplayNames`.
- A status grant's duration in seconds needs the tick length, which only the effect knows:
  `IStatusEffectDisplay.FramesPerDurationTick` (Poison: 1s; null for effects that take no duration).
- Every stat modifier target has a player-facing name (`EffectAmountModifierText.NameOf`).
- Durations read in the largest whole unit, rounded up (`EffectFormatting.FormatDuration`: "90s" is "2m").
- Every line is a plain sentence with its duration last, with no parentheses or colons. Examples:
  - "Poison immunity for 5m"
  - "x4 Caution for 2s"
  - "50% Fire Damage Reduction for 10m"
  - "x2 Strength permanently"
- Aura grants are one line everywhere: "Grants a size 4 Poison aura with strength 16", plus "for 10s" when timed. Item
  Details Comparison compares the line by strength (key `effect:aura:{id}`).
- Advanced wording:
  - Damage and healing land "to" a body part ("to a random body part", "split across every body part").
  - A status effect is held "on" one.
- Advanced qualifier lines under an entry (`EffectFormatting.AdvancedQualifiers`, keyed under the entry):
  - "Locked Duration" for a fixed duration, with the tooltip "This duration cannot be modified."
  - `ItemComparisonStat.TooltipText` carries the tooltip, and the Effects section sets it on the line.
  - No "Stackable" line. A buff or debuff never stacks with one from the same source: see the high-priority Game TODO
    "Buffs and debuffs from the same source don't stack".
- Aura Details' "Strength" section (`AuraDetailsWindow.StrengthLines`) describes the aura's strength, never its glow:
  - "Effects scale with its strength", for a Power aura only.
  - "Strength fading to the edge of its reach" or "Full strength to the edge of its reach".
  - A glow-only aura (no effects) has no Strength section.
- Tests: `EffectFormattingTests` (every case, both modes, and no built-in entry formatting to its type name) and plural
  tags in `GameplayTagRegistryTests`.

Out of scope, because each has its own TODO:
- **Targeting settings** ("Item Details for the targeting model"): path, walls, the minimum range, the anchor, and the
  preview for the new shapes. This plan changes the shape preview only for toggles.
- **The hover summary and hotbar summary** ("Clean up item and action tooltips"). Phase 1 fixes `EffectFormatting`,
  which they read, so their effect text improves too. Their layout does not change.
- **Default Item Details mode as a user setting**: a new low-priority TODO beside "Options menu".

## The two modes

| | Simple (default) | Advanced |
|---|---|---|
| Values | As used by the player, every bonus applied, the number alone | As used, then the total change in parentheses: green for a buff, red for a debuff ("Range: 13 (+7)") |
| Tooltips | None | On every value that differs from its base: the base, then one line per modifier, then the total |
| Shape, range, size | No lines; the targeting preview shows them | "Shape", "Range" and "Area Size" lines beside the preview |
| Timing | One word, only for Delayed or Freecast ("Delayed"), no label; nothing for Immediate | "Timing: Immediate/Delayed/Freecast", with a tooltip explaining it and giving the lock or windup in seconds |
| Auras | Strength at the source only; Each-second effects at that strength | Strength at the source and at the edge; Each-second effects at both; falloff and magnitude lines; the aura's description |
| Effect qualifiers | The effect and its amounts | Also its body part, and a "Locked Duration" line under it (tooltip "This duration cannot be modified.") |
| Currency | Base sell value | Base buy value and base sell value |
| Restrictions ("Untradable") | Shown | Shown |
| Description, Cost, Shop section, tags, blocker, "Active", "Cooling down" | Shown | Shown |
| Comparison | Always simple, with FF14's difference on the better side | Not used for comparing |

**The mode button** sits beside Compare, labelled with the mode it switches to: "Advanced" in simple mode, "Simple" in
advanced mode.
- The mode is Presentation state (`ItemDetailsWindowController`), kept for the session and reset to Simple at the next
  start. A user setting for the default comes later (the new TODO).
- It applies to the hover popup and the pinned window alike.
- While comparing, both panes are simple and the button is hidden. Stop Comparison brings back the chosen mode.

The Workshop's `ActionDetailsWindow` and `AuraDetailsWindow` are always advanced, with no button. They have no player,
so their values are the base values and nothing is in parentheses.

## Rules for every phase

- **One screen.** Item Details fits on one screen in both modes, with no scrolling and nothing more to open. In
  advanced mode, tooltips carry only the breakdown behind a value, never a fact that isn't on the window. Measured, and
  adjusted if needed, at the end of Phase 5.
- **Values as used.** Every number is worked out as if the player used the item now. **The player is the source, and
  the target has no modifiers**:
  - The player's Outgoing modifiers, ability-score bonuses and scroll scaling apply.
  - Incoming modifiers never apply, even where the target is the player (costs, a toggle's held effects).
  - Some numbers will differ from the final result, depending on the target. That is expected, and it keeps every
    number unambiguous.
- **One source of lines.** Every line is an `ItemComparisonStat` from `ItemComparisonStatExtraction`. A line carries its
  key, its comparable value, and what each mode shows: its simple text, its advanced text, the modifier total with its
  direction, and its tooltip. It also names the modes it appears in. Both modes and comparison read the same list, so
  they can't disagree about what exists.
- **Action/item parity.** `ActivatableDetailsSections` builds both Item Details and Action Details, so toggle actions
  (Toxic Aura, Lantern) get the same sections. Any line that applies only to items is placed so that this is clear.
- **Effects text has one home**, `EffectFormatting`. It is given the mode, and only the qualifiers depend on it.
- **Game decides, Presentation shows.** A value as used comes from a `GameViews` view built on the same functions the
  effects apply with, never a copy of their arithmetic in Presentation.
- **General rules are not repeated per item.** Item Details doesn't say that turning a toggle off costs nothing, that a
  toggle goes out when its upkeep can't be paid or its holder dies, or that a potion starts the potion cooldown.

## Compared with FF14 and WoW

Sources: the two screenshots (FF14's Item Comparison, a WoW weapon tooltip), plus the wikis on WoW's green effect lines
([Chance on Hit](https://warcraft.wiki.gg/wiki/Chance_on_Hit), [Itemtip](https://warcraft.wiki.gg/wiki/Template:Itemtip)).
FF14's tooltip layout isn't documented outside the game, so its column relies on the screenshot.

| | FF14 | WoW | This plan |
|---|---|---|---|
| Restrictions | Header flags, only those that apply ("UNTRADABLE") | "Binds when picked up", second line | Only those that apply, in negative wording ("Untradable"), above the tags. Like FF14. |
| Values for the player | The item's own values | Spell tooltips apply the character's stats | As used, in both modes |
| Comparison | Side by side, **the difference beside the better value in green, "156(+53)"** | Separate "currently equipped" tooltip | Simple mode on both panes, the difference beside the better value in green |
| Detail level | One fixed level | One fixed level (addons add more) | Simple by default; Advanced on a button |
| Stat block | "Bonuses" in a two-column grid | Paired left/right lines | One line each. A two-column grid is the first option at the Phase 5 evaluation. |
| Effects | n/a for gear | Green text with its trigger first ("Use:", "Chance on hit:") | Section headings say the trigger ("Effects", "While on", "Each second") |
| Economy | Footer: shop price, "Sells for" | "Sell Price" with coins | Footer: sell value (simple), buy and sell values (advanced), then the Shop section |
| Requirements | Job and level, green when met | Red when not met | The blocker line in red |
| Headline numbers, quality colour, derived DPS | Headline bars | Quality colour, DPS | Not taken |
| One panel | Yes | Yes | The one-screen rule |

## What is missing today

Already done: the "While on" heading, "To turn on" and the upkeep line
(`CostText`), and the Ground/Target placement line for toggle items.

Missing:
1. **Toggle items show meaningless activation lines** ("Shape: Self", "Range: 0", a one-tile preview, from
   `ToggleItemActivator.SelfTargeting`).
2. **What an aura does is never shown in game.** `AuraDetailsWindow` shows it, but only in the Workshop. This applies
   to every aura grant, Scroll of Torch included.
3. **Four entries print their type name**: `StatusEffectRemoval` (the Cure Poison Potion reads "StatusEffectRemoval"),
   `ActivationDisableGrant`, `TerrainChange` and `DodgeActivation`.
4. **Entries leave fields out:**
   - Immunity durations.
   - `StatusEffectGrant`'s `TopUpTo`, `DurationTicks` and body part.
   - `DirectDamage`'s `PercentageDamage`, body part and mode; `DirectHeal`'s body part.
   - `StatModifierGrant`'s raw target names, its `ConditionTag` (except on damage reduction), stacking and fixed
     duration.
   - Aura size and power folded into one line (`FormatAuraSourceGrant`).
5. **Every value is the definition's**, never the player's.
6. **Timing never says how long**, and `ReleasesActionLock` is never mentioned.
7. **Nothing says that a loot box can't be traded, or what it holds.**

Not added on purpose: `Summary` (the hover's text), `MaximumShopStock` (the Shop section shows its effect), and
`IsToggledOn` (already "Active").

## Phase 1 -- every effect entry says everything it carries

`EffectFormatting.FormatEntry(entry, gameplayTags, mode)` and `ItemComparisonStatExtraction.ExtractEffectStat`:
- New cases:
  - `StatusEffectRemoval`: "Cures Poison".
  - `ActivationDisableGrant`: "Prevents spells for 2s".
    - Each tag in its query is named by its **plural display name**, lower-case in the sentence, and several are joined
      with "and".
    - The plural is a new optional name beside a tag's display name in `DeclareTags`
      (`GameplayTagRegistry.GetPluralDisplayName`). It falls back to the display name, so "Magic" stays "magic".
    - None-of tags, if any, follow as "except ...".
  - `TerrainChange`: "Turns the ground to Lava".
  - `DodgeActivation`: "Dodges most attacks with a step".
- `StatusEffectImmunityGrant`: "Poison immunity for 5m", or "while held" for a toggle's grant.
- `StatusEffectGrant`: "Tops Poison up to 5 stacks" for `TopUpTo`, and "lasting 6s" for `DurationTicks`. Advanced
  mode adds the body part.
- `DirectDamage`: "+ N% of max health" for `PercentageDamage`. Advanced mode adds the body part and mode, and
  `DirectHeal`'s body part.
- `StatModifierGrant`:
  - Player-facing target names ("Caution"), extending `EffectAmountModifierText` or a table beside it.
  - `ConditionTag` for every target ("Fire damage taken x0.5").
  - Advanced mode adds a "Locked Duration" line for a fixed duration.
- `FormatAuraSourceGrant`: "Grants a size 4 Poison aura with strength 16", one line wherever it shows.
- New stat keys for the new lines.

Tests:
- One formatting test per case, in both modes.
- **No built-in master record's entry formats to its type name**: walk every action, item and aura through
  `EffectContent.ForEachEntry`.
- Plural tag names: declared, and the fallback.

In-game check:
- Open the Cure Poison Potion: "Cures Poison", "Poison immunity for 5m".
- Open Vial of Warding: both immunities read "for 10m".
- Stand in the Anti-Magic zone and open a spell: the disable reads "Prevents magic for ...".

## Phase 2 -- values as used

**Game:** a view, `GameViews.ActivationPreview`, worked out for the player and a definition. For each amount it
returns a `PreviewedValue`:
- the value as used;
- the base;
- an ordered list of `ValueContribution`s: a modifier named by its source through `ActionSourceNaming`, an ability
  score, or the scroll multiplier, each with its operation and amount;
- the net direction, buff or debuff.

It is built on the functions that apply them:
- `EffectModifiers.Scale` gains a sibling that records its steps, sharing its code path, and runs Outgoing only
  (`EffectContext.NoTargetEntity`).
- Scrolls use `ScrollScalingEffects`.
- The action lock uses `Activations`' standard lock.

Values covered:
- Damage, healing, mana restored and drained, status stacks, durations, chance.
- Aura strength and size.
- An aura's effects at a strength: each amount multiplied by it, as `EffectContext.Magnitude` does when the aura ticks.
  A Power aura's effects are given per point of strength in content ("Heals 1 health" for the Healing shrine's aura), so
  this is what Phase 4's Each-second lines read.
- A scroll's range and area.
- The action lock.
- A cooldown, if a modifier ever targets one.

**Presentation:**
- `ItemComparisonStat` carries the `PreviewedValue`. Simple mode shows the value; the advanced text and tooltip come in
  Phase 3.
- Comparison compares the as-used values. Both items are worked out for the same player, so it stays fair.
- Item Details rebuilds when the player's modifiers or ability scores change: the view exposes a change counter that
  `Update` compares, like the blocker today, so values aren't recomputed every frame.

Tests: the preview's values equal what applying the entry does, for a player holding Outgoing modifiers:
- damage dealt to a dummy that has no modifiers;
- aura power placed;
- a scroll's resolved range;
- **aura values**: the Healing aura's heal at its shrine's strength equals what one tick heals a wounded dummy standing
  at the shrine. It reads "Heals N health" for that strength, not the per-point "Heals 1 health" (before Phase 1's fix,
  "Heals 1 + 0% of max health"). The Poison aura's top-up at the Toxic Idol's strength equals the stacks a tick gives.
  A Flat aura (Dread) reads the same at any strength.

In-game check:
- Drink Draught of Radiance, then open the Toxic Idol: the aura's strength and size are doubled.
- Open a scroll, raise Intelligence in Admin Mode: its range grows while the window is open.

## Phase 3 -- simple and advanced modes, and the comparison difference

- **The mode button** beside Compare, as described in "The two modes"; `ItemDetailsWindowController` holds the mode.
  The tags-and-buttons row lays out the buttons that apply: Stop Comparison while comparing, otherwise Compare and the
  mode button.
- **Line selection:** `ActivatableDetailsSections` builds each section for a mode.
  - The Activation section in simple mode keeps the shape preview, has no Shape, Range or Area Size lines, and shows
    "Delayed" or "Freecast" as a word on its own (nothing for Immediate).
  - Advanced mode keeps every line, and "Timing: X" gets its tooltip:
    - Immediate: "Instant, then locks you for 0.5s".
    - Delayed: "0.5s windup, can be cancelled".
    - Freecast: "Instant, no action lock".
    The seconds are the as-used lock. `ReleasesActionLock` adds "Cancels your current windup" in both modes.
- **Advanced values:** after each value that differs from its base, its total change in parentheses ("(+7)",
  "(x2)", "(-0.5s)"):
  - green for a buff, red for a debuff, by the net direction the view gives;
  - for a lower-is-better value (a cooldown, a cost), the direction follows the effect on the player, so a shorter
    lock is green;
  - a line can't colour part of its text (one `TextColor` per `TextWindow`), so the parenthesis is its own text element
    after the value, in the row the line already is.
  - The tooltip on a changed value: "Base: 6", one line per contribution ("Intelligence 13: x2.1"), "Total: 13".
- **Currency:**
  - Simple: the base sell value, beside the gold icon.
  - Advanced: the base buy and base sell values.
  - With no shop open, both values are `GoldValue` (a 1.0x reference). While a shop is open, they are that shop's flat
    prices (`ShopActions.ComputeBuyPrice`/`ComputeSellPrice`).
  - Item Details already rebuilds when the shop pricing source changes, so opening or closing a shop updates them.
  - The Shop section, while a shop is open, shows in both modes.
- **Comparison:**
  - Both panes are always simple.
  - On a line coloured better, the difference from the best of the others follows it in green, FF14's "156 (+53)".
  - Nothing is added on the worse side or on a tie.
  - A line only one item has stays green, with no difference.

Tests:
- `DetailsWindowsTests`: every built-in item in both modes, checking the lines each mode leaves out.
- A comparison of two damage values shows the difference on the better pane only.
- The mode survives closing and reopening Item Details, and comparison forces simple.

In-game check:
- Open the Wand of Fireball: simple mode shows its damage and the preview, no Shape, Range or Area Size lines, and no
  timing word.
- Press "Advanced": the button now reads "Simple", and the Shape, Range, Area Size and Timing lines appear.
- Drink a damage buff: advanced mode shows "(+N)" in green, and hovering it shows the base and the buff.
- Compare two potions: both panes are simple, and only the better heal shows "(+N)".

## Phase 4 -- a toggle shows what it holds; every aura grant shows its aura

**An aura section per `AuraSourceGrant`**, in Item Details and Action Details, after the line that grants it:
- A subheading: the aura's name with its glow swatch.
- **Simple:** "Each second" with the aura's effects at its source strength, which the grant line already gives ("Tops
  Poison up to 16 stacks"), and its reach as a preview.
- **Advanced:** adds:
  - the aura's description;
  - its strength at the edge (the edge value from the field's falloff function in `Engine.Tiles`);
  - the Strength lines (`AuraDetailsWindow.StrengthLines`, none for a glow-only aura);
  - the Each-second effects at both ends ("Tops Poison up to 16 stacks at the source, 4 at the edge").
  A `Flat` aura shows its amounts once.
- **The reach preview**: a diamond of the as-used size (`ManhattanDiamond`), each ring shaded by its strength.
  `TargetShapePreviewElement` gains an optional per-tile shade beside its highlight set. It sits beside the strength
  lines in the section's row, so it adds no height.

The rows `AuraDetailsWindow` builds move into `ActivatableDetailsSections.BuildAuraSection`, which the Workshop's Auras
tab builds in advanced mode with base values.

**A toggle's Activation section:**
- No shape preview for a toggle item, in either mode, and no Shape or Range lines in advanced mode. Its reach is its
  aura's.
- A toggle that holds no aura has no preview.
- Timing follows Phase 3's rules.

Comparison: aura sections key by aura id; two toggle items compare without shapes.

Tests: the Toxic Idol has an aura section with Each-second lines and no Self shape, in both modes. Scroll of Torch has
its aura section after its grant line.

In-game check:
- Open the Toxic Idol, simple mode: no Self preview; "Grants a size 4 Poison aura with strength 16", then a Poison aura
  section with "Tops Poison up to 16 stacks" and a 4-tile diamond fading outward.
- Switch to advanced: the edge strength, "Strength fading to the edge of its reach", and the description appear.
- Open the Dread Idol: a solid diamond, "x4 Caution for 2s"; in advanced, a "Locked Duration" line under it.
- The Workshop's Auras tab looks as it does now.

## Phase 5 -- restrictions and contents, then the one-screen evaluation

- **Restrictions row**, above the tags, in both modes: only the restrictions that apply, in negative wording. Today
  that is only **"Untradable"**, when `CanTrade` is false.
  - The row is left out when nothing applies.
  - Indestructible, Unsellable and Undroppable are added only when an item needs them.
  - Read through a view over the definition (`InventoryView.IsTradable`), so a later rule changes only the view.
  - Comparison key `item:untradable`, without a direction.
- **"Opens into"** for an item with `Contents`, in both modes, one line per contents kind:
  `RandomSingleStackContents` reads "1-10 of one random item", and `SetItemContents` lists its entries. A contents
  kind without a case fails a test, as in Phase 1.

In-game check:
- Open a loot box: "Untradable" above the tags, and the "Opens into" line.
- Open a potion: no restrictions row, no "Opens into".

### One-screen evaluation

Open every built-in item (and, in the Workshop, every action), in both modes, and measure Item Details' height at the
game's default window size. Measure the comparison pair stacked too. Adjust only what doesn't fit. The options, in
order:
1. A two-column grid for short label-and-value lines (advanced mode's Activation lines, the aura's strengths), as
   FF14's Bonuses and WoW's paired lines do.
2. The Activation section's empty third zone holding lines.
3. Side-by-side comparison panes (FF14's), if the stacked pair doesn't fit.
4. The description cut to a set number of lines.

## Wrap-up

- IMPLEMENTATION-NOTES: Item Details' two modes and what each shows; values as used (source is the player, target
  unmodified) and their breakdown; the comparison difference; the aura section; restrictions; the one-screen rule; and
  the no-type-name-fallback test.
- CLAUDE.md: a clause under Presentation for the two modes, values as used, and the one-screen rule.
- TODO: remove "Item Details for toggle items", and trim the lines this plan made obsolete from "Clean up item and
  action tooltips".

## Decisions (settled 2026-11-20)

1. **Base buy and sell values.**
   - While no shop is open, the reference multiplier is 1.0x for both, so the buy and sell values are the item's
     `GoldValue`. Shops keep their own multipliers (General 1.2x and 0.8x, Potion 1.1x and 0.9x).
   - While a shop is open, the values are that shop's flat prices (`ShopActions.ComputeSellPrice`/`ComputeBuyPrice`,
     the Normal band, before stock pricing).
   - Both modes follow this rule: simple shows the sell value, advanced shows buy and sell.
2. **The Shop section stays in simple mode.**
3. **`FreeCast` timing shows as "Freecast"**, not "QuickCast", which is the planned hotbar Activation Style.
4. **The item description stays in simple mode.** The aura's description is advanced only.

Earlier answers:
- Aura amounts are shown at the source and the edge (advanced), and at the source alone (simple).
- The reach preview is in the aura section.
- The player is the source, and the target has no modifiers.
- FF14's difference appears only on the better side.
- The targeting-model TODO stays separate.
- The restrictions row shows only "Untradable", and only when it applies.
