# Show "Self" when an entity is its own source

(From TODO.md "Show "Self" as the source when an entity is its own source". Investigated 2026-11-01. One phase;
decisions at the end.)

## Verdict in one paragraph

Half of the TODO entry is already done. The two copies of the Entity-vs-`ToString()` switch it names
(`ModifierDisplayFormatting.DescribeSource`, `PlayerActivityLog.DescribeSource`) were folded into one Game view,
`ActionSourceNaming` (`Game/Views/ActionSourceNaming.cs`), which every reader now goes through, except the corpse's
"Slain by" line. What's left is the rule itself: a source is "Self" when its entity is the entity the line describes.
`ActionSourceNaming` is the one place to put it. It gets the session's `EntityKeys` and a required "described entity"
argument, and compares keys there (`ActionSource.IsEntity(key)`), so no caller handles a key or an id comparison.
Presentation keeps passing entity ids, as it does to every other view. The corpse line moves onto the same view, which
also fixes it showing "Unknown" for a death caused by terrain or an aura.

## How it works today

### The naming view (`Game/Views/ActionSourceNaming.cs`)
- Built in `GameViews` from `context.Terrain` and `context.Auras` only. It has no key table.
- `Describe(source)`: an entity source is named by `source.Identity.DisplayName` (the name and crawler number it had
  when the source was made). A terrain or aura source is named by its definition. `Admin`/`AI` use the kind's name.
- `DescribeByName(source)`: the same, but an entity by its name alone, for a row with little room.

### Who names a source
| Where | Call | The entity the line describes |
|---|---|---|
| Ability Score window, row text (`AbilityScoreModifierFormatter.FormatModifierLine`) | `DescribeByName` | the window's entity (`entityId` argument) |
| Ability Score window, hover popup title (`AbilityScoreWindow.UpdateHover`, cached in `_sourcePopup` keyed on source, effect text, seconds) | `SourcePopupShowingSeconds` -> `Describe` | `_entityId` |
| Health window, hover popup title (`HealthWindow.BuildSourcePopup`, cached on `SourcePopupKey`) | `SourcePopupShowingSeconds` -> `Describe` | `_entityId` |
| Player activity log, `DAMAGE` and `BLOCKED` lines (`Game/Diagnostics/PlayerActivityLog.cs`) | `Describe` | the event's `EntityId` (the target) |
| Player activity log, `HEAL` line | `DescribeEntity(healed.SourceEntityId)`, not an `ActionSource` | `healed.EntityId` |
| Corpse window, "Slain by" (`SecondaryInventoryWindow.ResolveKillerName`) | `killer.Identity.DisplayName` directly; any non-entity killer reads "Unknown" | the corpse, `EntityId` |

### Self-sourced effects that exist now
`Tank` (MaximumHealth/HealthRegen bonuses), `Boss`, `HealingShrine` and `PlayerKit`'s ability-score seeds all build
their source with `ActionSource.FromEntity(..., entityId)` on the entity they modify. Today the player's own
modifiers read "Player1 : +N" in the Ability Score window.

## Phase 1: the rule in `ActionSourceNaming`, every reader through it

### Game
- `ActionSourceNaming(TerrainRegistry terrain, AuraCatalog auras, EntityKeys entityKeys)`. `GameViews` passes
  `context.EntityKeys`.
- `public const string SelfName = "Self";`
- `public bool IsOwnSource(ActionSource source, int describedEntityId) => source.IsEntity(entityKeys.GetKey(describedEntityId));`
  A key comparison, never an id one: an id is recycled the moment its entity is destroyed, so a source left by a dead
  entity must never read "Self" on whoever gets its id next.
- `Describe(ActionSource source, int describedEntityId)` and `DescribeByName(ActionSource source, int describedEntityId)`:
  `SelfName` when `IsOwnSource`, otherwise what they return today. The subject is a required argument. Every
  reader has one, and a version without it would let a new reader skip the rule without noticing.
- `PlayerActivityLog`: `DAMAGE` and `BLOCKED` pass the event's `EntityId`. `HEAL` writes `Self` when
  `healed.SourceEntityId == healed.EntityId`. That compares two ids from the same event and frame, so recycling can't
  mix them up. `ActionSource.ToString()` is unchanged: it is the debug form and has nothing to be relative to.

### Presentation
- `AbilityScoreModifierFormatter.FormatModifierLine` takes `entityId` and calls `DescribeByName(source, entityId)`. Row
  text is already cached on the drawn string (`AbilityScoreModifierRow._drawnText`), so it needs nothing more.
- `ModifierDisplayFormatting.SourcePopup`/`SourcePopupShowingSeconds` take `describedEntityId` and pass it to `Describe`.
- `AbilityScoreWindow` and `HealthWindow` popup caches add `IsOwnSource(source, _entityId)` (a bool) to their keys. It
  is an array read, made each frame the popup is up. Keying on the bool rather than the entity id keeps the cache exact
  when the window is pointed at another entity, or one whose id was recycled.
- `SecondaryInventoryWindow.ResolveKillerName` takes `ActionSourceNaming` (from `views.ActionSourceNaming` where its
  controller builds it) and returns `Describe(killer, EntityId)` for any `KilledBy`. A suicide reads "Slain by: Self",
  lava reads "Slain by: Lava", and "Unknown" is left only for no `DeadComponent`.

### Tests
- `ActionSourceNamingTests`: own source -> "Self" through both methods; another entity's source -> its name; a source
  whose entity was destroyed, with the id reused by a new entity -> the old name, never "Self"; terrain and aura
  unchanged. `TestActionSources.Naming` gains the key table.
- `AbilityScoreModifierFormatterTests`: a modifier whose source is the entity reads "Self : +N", and another entity's
  still reads its name.
- `HealthWindowControllerTests`: a self-sourced modifier's popup title is "Self".
- `PlayerActivityLogTests`: self-damage writes `source=Self`, and damage from an NPC writes its name.
- Corpse line: a self-kill reads "Slain by: Self" and a terrain kill reads the terrain's name (in the item-window tests
  that already build a corpse window, or a new small test).
- Existing calls to `Describe(source)` in tests get a described entity.

### Test in game
- Start a game, press O. The player's ability-score seed rows read "Self : ..." instead of "Player1 : ...". Hover one,
  and the popup title reads "Self".
- Admin Mode (F12): right-click your own tile, Apply > Tank. Press U and hover the MaximumHealth and HealthRegen
  bonus rows: each popup title reads "Self".
- Stand on Holy Ground and hover its blessing's row in the Health window: it still reads the terrain's name, not
  "Self".
- Stand in lava until you die, then look at your corpse's window (or kill a goblin with lava): "Slain by: Lava", not
  "Unknown".
- `Log/` player activity log: a hit from a goblin reads `source=Goblin ...`. A self-inflicted one (a Self-targeted
  damaging spell, or a placed aura that reaches you) reads `source=Self`.

## Decisions to confirm

1. **"Self" for every viewed entity, not only the player** (the TODO's own rule). An NPC's self-buff reads "Self" when
   you inspect it. Recommended.
2. **No version of `Describe` without a subject.** Recommended: every current reader has one, and requiring it keeps
   a new reader from skipping the rule.
3. **The corpse line names any killer through the view** ("Slain by: Lava" instead of "Unknown"). This is beyond the
   TODO but is the same one-line change. Recommended.
4. **The `HEAL` log line** compares the two ids on the event, since `EntityHealedEvent` carries a source entity id
   rather than an `ActionSource`. Moving heals to `ActionSource` is a separate change and isn't proposed here.
