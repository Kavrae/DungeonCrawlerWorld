# Long-Term TODOs

Non-urgent architectural items worth revisiting later. Organized by layer (Engine, Game, Presentation,
Global), each split High/Medium/Low priority. Landed work lives in `IMPLEMENTATION-NOTES.md`, not here
-- cross-referenced by name where a still-open item depends on it.

## Engine

### Medium Priority

#### FrameEventBuffer double-buffering + event system cleanup

`FrameEventBuffer<T>` (`Engine/ECS/Systems/`) throws on a second same-cycle `Record` (a safety net, not
a fix). Real fix: double-buffer (swap, not clear) like Bevy's `Events<T>` -- trades same-cycle
visibility for 1-frame latency (check this is OK for `MovementSystem`'s `ContactDamageSystem`/
`StatusEffectAuraSystem` consumers). Do alongside reviewing `EventBus`'s `IBufferedEvent`/
`SubscribeOnce`/`DispatchBuffered` (`Engine/Events/`) for consistency -- a different mechanism (deferred
re-entrant handler vs. high-frequency batching), never compared side by side.

### Low Priority

#### Equipment (Engine)

Slot/equip-unequip mechanics -- move an `InventoryItemStackComponent` stack into a slot, no new storage
primitive. Companion to the Game/Presentation equipment items below.

## Game

### High Priority

#### Inventory management rules

Interaction/restriction rules: stacking beyond exact-`ItemDefinition` match, who can pick up what,
item-item interactions. Storage + exact-match merging already exist.

#### Consumable items

Needs the item-instance-divergence design (a consumable's remaining-uses count is per-slot state that
doesn't exist yet) plus an actual "use" action.

#### Torch reveal + light-weakness damage, Scroll Mastery power-scaling

- Scroll of Torch's `StatusEffectType.Light` grant is glow-only -- no `IStatusEffectAuraApplier`
  registered for `Light`. Needs a real applier (fog-of-war reveal, light-weakness damage). Also worth
  reconsidering once fog of war lands: today's grant is per-*entity*; a light source reads more
  naturally anchored to a *location* (see Torch V2 below).
- `ScrollMasteryEffects.MasteryThreshold` (flat 200) and a synthesized spell's placeholder `ManaCost: 0`
  should scale with the effect's power. Blocked on Action Effects gaining a power-scaling concept.

#### Torch V2 -- selectable attachment mode

`AuraSourceGrant` always targets `context.TargetEntityId`. Three modes worth making selectable:
- **Follow the caster** -- already works via a Self-shaped `TargetingSpec`, no code change.
- **Fixed at a location** -- needs a minimal stationary prop entity (Transform + AuraSourceComponent,
  no creature identity, cf. `Lava`), despawned on expiry.
- **Attach to a specific other entity** (companion/pet) -- not expressible today; needs a new
  `TargetShape` or an explicit target-override on the entry.

Design as one shared "attachment mode" concept reusable by any future aura-granting effect.

#### Experience module

XP for kills (`EntityDiedEvent`) and quests (blocked on quest completion existing as a mechanic).
Level-up grants stat boosts/abilities per class. Needs a current/next-threshold Experience component +
HUD bar (candidate for the shared tick-fraction HUD bar item under Presentation). Check whether its
level-up curve can share math with Skills/Spell leveling below rather than three independent copies.

#### Skills

Static bonuses via `StatModifierComponent` + new effects on top of an action -- the motivating case for
the DamageOverride generalization above. Level 0-15 normally, unlockable to 20, never decreases, up to
hundreds per player. Needs a `MultiComponentPool<SkillComponent>`-shaped store (per-entity/per-skill,
not single-instance). See Spell leveling below for the same shape at smaller scale -- share one leveling
primitive. Consumer: Player selection menu's skill-gated detail (Presentation, below).

### Medium Priority

#### Toggle item activator

`PotionActivator` always consumes a stack per activation -- wrong for a stateful toggle (Toxic Idol's
Poison aura costs a stack to turn *off* too). Needs a new `IActionActivator` (`ToggleItemActivator`)
`ConsumableActivationSystem` recognizes and doesn't consume a stack for. Open questions: does the
effect force-untoggle if the item is dropped/sold/stack empties (mirrors `DeathSystem`'s corpse-aura
cleanup, one layer up)? Is "toggled on" its own tracked state, or implicit in component existence (only
works while a toggle item drives exactly one effect kind)? Toxic Idol migrates to this once it lands.

#### Dexterity scaling ActionLockComponent.StandardLockFrames

Flat per-entity today (Goblin 54, Fairy/Ghost 48, Player 20, +Engineer 10%). Lerp
`ActionLockGate.StandardLockFrames` (1s) at Dex 1 down to 0.25s at Dex 300, off
`AbilityScoreComponent.Total` (same shape as `PotionCooldownEffects.ComputeDurationFrames`). Must
compose with, not replace, the racial baseline -- exact composition (multiply vs. replace) undecided.

#### Spell leveling

Same rules as Skills (level 0-15/20, XP with use, never decreases) -- land after Skills so both share
one leveling primitive. A spell's level would modify its `ActionEffect` magnitude/duration (bigger
heal, cheaper Magic Missile) -- exact "what changes" design still open.

#### Corpse looting rights based on damage dealt

Currently a free-for-all (anyone adjacent can loot). Needs per-entity damage-dealt tracking against a
target + a reset rule: on death (simple, but loses the record before looting starts unless kept
alongside `DeadComponent`) or on a timeout since last hit (avoids crediting an old, unrelated fight).

#### Mobs looting corpses

V1: fill own inventory from a nearby corpse until full (`InventoryCapacity.MaxNonPlayerStackCount`), no
preference. V2: preference by combat style/item rarity, once either concept exists.

#### NPCs use shops

Goblins/Fairies/Ghosts buying and selling at a `Shop`/`PotionShop`/`GeneralShop` autonomously via
`ShopActions.TryBuyFromShop`/`TrySellToShop` -- see `PLAN-shops.md` (the player-only version already
shipped). Needs actual economic decision-making (what to sell for spare Gold, what to buy when low
on supplies) -- blocked on the NPC behavior composition item (this file's own Low Priority section),
which is where that decision would live once it exists.

#### Destroyed items

A "destroyed" item state: displays as "destroyed", modified description, can still be picked up, but
can't be used. Update `ContainerDestructionSystem` (and any future container types --
`PLAN-storage-containers.md`) to mark a destroyed container's inventory items destroyed instead of
deleting them outright, once this lands.

#### Add source and target modifier checks for all actions

Only `DirectDamage` runs both an Outgoing (source) and Incoming (target)
`StatModifierMath.GetEffectiveValue` pass -- `DirectHeal`/`DirectManaRestore`/`HotkeyExpansionGrant`/
`StatusEffectGrant`/`ChainedEffect`/`AuraSourceGrant` check neither. Make both checks standard on every
effect entry's `Apply`, even with no real `StatModifierTarget` consumer yet, so a future buff/equipment
source can hook in by granting a modifier alone. Calling-convention change, not a new stat.

### Low Priority

#### Repair destroyed items

Item-type-specific Repair skills to restore a destroyed item -- blocked on Destroyed items (above) and
the not-yet-existing Skills system (this file's own Skills entry).

#### Item damage and repair

A new per-stack condition/durability concept, distinct from Destroyed items above (a damaged item
stays usable, just worth less and eventually repairable, rather than a binary destroyed/not state).
`ItemDefinition.GoldValue`/`ShopActions.ComputeBuyPrice`/`ComputeSellPrice` (`PLAN-shops.md`) would
need a per-stack damage modifier applied on top of the flat catalog value -- necessarily per-stack,
not per-`ItemDefinition`, same split Item weight below already follows for a different field. Repair
likely wants to be the same Repair skill the entry above already wants, generalized to cover
"damaged" as well as "destroyed" rather than two independent mechanics.

#### Trapped containers

Containers (`PLAN-storage-containers.md`) can be trapped. Needs a trap-effect concept triggered on
interaction/loot.

#### Trap detection and disarming skills

Skills-system consumer (blocked on Skills, same as Repair destroyed items above) for detecting/
disarming Trapped containers.

#### Show runner race

Randomly selected; affects UI appearance and biases quest/enemy selection.

#### End of level staircase (Game)

Descend/ascend logic. See the matching Presentation item for visuals.

#### Random map generation v1

`FloorBuilder.CreateMap`/`PopulateFloor` populate a fixed `TestMapBuilder` layout today.
`MathUtility`'s ctor already takes an optional seeded `Random` -- just needs to actually reach it from a
real session (`GameLoop.Initialize` currently constructs an unseeded one). Player-facing needs: a seed
input at floor-start, and a HUD/menu readout of the active seed (Minecraft precedent -- share a seed to
reproduce a layout).

#### Equipment (Game)

Slot/stat rules. Companion to the Engine/Presentation equipment items. Once Complex health exists more
broadly, some slot *counts* (not just which slots exist) should scale with an entity's active,
non-disabled body parts of the matching type (a ring per finger, shrinking if a hand is lost) -- Simple
entities keep a fixed layout.

#### Melee actions should declare which body parts perform them

Follow-up to Equipment + Limb-specific penalties (`IMPLEMENTATION-NOTES.md`). `BodyPartEffectsSystem`
currently scores a generic penalty off every Arm/Hand (correct only because nothing equips to one
specific limb yet). Once Equipment exists, `IActionActivator`/`ActionEffect` should let a melee action
declare which `BodyPartType`(s) perform it (a two-handed weapon needing both Hands; an offhand punch
caring about one arm) -- `BodyPartEffectsSystem` would then key its penalty off the acting part(s), not
a blanket aggregate.

#### Enchantment

Next consumer of `InventoryActions.AddDivergentItem` (already built generic for this): builds a
modified `ItemDefinition` `Override`, same split-into-new-stack primitive as Wand charge depletion. No
design yet for recipe/UI/materials/where performed.
- **Provenance tracking** (why/how a stack diverged): nothing records this today. Once Enchantment is
  real, an `Override`/sidecar field should carry an origin string for tooltips.
- **Acquisition provenance** (separate concept -- where a stack came from: loot, corpse, shop, craft):
  same open question (field vs. sparse component); should record the source's *name*, not id (ids
  recycle; corpses are the one exception, never destroyed).

#### ItemBindingRule for hotkey-bound consumables

Item hotkeys bind to one exact `StackInstanceId` -- once depleted, the slot just goes empty. A future
`ItemBindingRule` would bind to an item id + a preference rule (lowest/highest charges first, plain
batch first), re-resolved each time the current stack runs out.

#### Stats -- consumers

Infra landed (`IMPLEMENTATION-NOTES.md`). Remaining:
- Split hidden ability scores (Luck/Wisdom) into composites of other hidden scores -- needs more hidden
  scores to exist first.
- Wire the concrete "modifies" behaviors: Strength->melee damage (retire hardcoded `PunchDamage`
  consts), Constitution->`MaximumHealth` x10 (regen/potion-cooldown already landed), Dexterity->
  `StandardLockFrames` (own item above), Intelligence->mana (once Mana lands), Charisma->shop/charm,
  Luck->loot/AI.
- Non-player races get their own baseline scores instead of flat 5.
- Level-up modifies Core scores (Hidden excluded). See the matching Presentation stats item.

#### Item weight and carry capacity scaling with Strength

No item has weight; storage is unlimited. Add a carry-capacity limit off `AbilityScoreComponent.Total`
(Strength), gate pickup on it. Depends on the Item weight (Presentation, below) item for the weight
field itself.

#### Mana

Current/max pool + regen, `SimpleHealthComponent` as the template. Heal should cost 2 MP, Magic Missile
5 MP once this lands (both free today). Starting `MaximumMana` = Intelligence `Total`.

#### Scroll and spell durations scaling with Intelligence

Duration-based effects (buffs, DoTs) should scale with caster Intelligence, same shape as
Constitution->potion-cooldown. Needs an `ActionEffect` duration field as a real concept first.

#### Damage types

No concept exists -- every hit is undifferentiated. Starting set: Magic, Blunt, Explosive, Slashing.

#### Level collapse timer

Global per-floor countdown pressure mechanic (not a `CountdownTicker` variant -- those are
per-entity/per-effect). Needs a HUD countdown element below the mana bar.

#### Tomes

Grant a spell outright on consumption (unlike Scrolls' 200-use mastery). Either a new `SpellGrant`
effect entry, or reuse `ScrollMasteryEffects` with a threshold of exactly 1.

#### Burning status effect from touching lava

Damage over time, decreasing, stacking (multiplicatively worse), worse per movement while still in lava.

#### Petrification status effect

Beyond Paralysis: forces `ForceBlockingComponent`-blocking regardless of normal blocking state. Real
map-occupancy problem -- `Map` tracks one Blocking occupant per tile; no resolution policy yet for
turning an already-occupied-adjacent or currently-non-blocking entity into forced-blocking. Needs a
design pass through `World`/`Map` placement, not a drop-in `ParalysisEffects.Apply` extension.

#### NPC behavior composition + generalized "turn claimed" signal

Follow-ups to the temporary `TestCombatBehaviorSystem` stand-in (`IMPLEMENTATION-NOTES.md`):
- Replace the hardcoded if/else chain with composable behaviors (self-heal, engage, flee, wander)
  arbitrated by a per-race-configurable priority/utility system.
- `MovementSystem` currently checks `_pendingAbilityActivations`/`_pendingConsumableActivations`
  directly to know a turn's claimed -- doesn't scale as action types grow. Needs a single shared
  "turn claimed" marker any decision system can set/check generically.

#### User feedback for actions is missing entirely

Casting, cancelling, AOE/melee landing, status effects applied -- no player-visible feedback beyond the
state change itself. No design yet.

#### Corpse decay/destruction and destructible terrain

`DeathSystem` never calls `EntityManager.DestroyEntity` (corpse stays fully populated, non-Blocking, for
future looting). `DestroyEntity` is reserved for a real decay-timer or "loot then destroy" step, or
future destructible terrain (skips `DeadComponent` entirely, just calls `DestroyEntity` on trigger).

#### Self damage buff ability

Example FreeCast/Immediate ability raising the caster's own outgoing damage for a duration.

#### Defensive buff spell -- damage reduction + healing over time

Self-targeted, combining a timed `StatModifierGrant(IncomingDamage, ...)` (fully supported today) with
a periodic self-heal built like Burning/Poison's DoT (`TimerBasedAuraApplier<T>`, healing instead of
damaging). The regen tick can now carry `IncomingHealing`/`OutgoingHealing` through the chain the same
way DirectHeal does (`HealthHeal.ComputeAmount`).

#### FreeCast toggle-aura ability

Item side landed (Toxic Idol, `IMPLEMENTATION-NOTES.md`). Still want the actual FreeCast *ability*
version (usable during an Action Lock) for that specific coverage, and to remove the "costs a stack to
toggle off" quirk (see Toggle item activator above).

#### BodyPartType categorization -- lifting/pickup still open

Movement/melee consumption landed (`IMPLEMENTATION-NOTES.md`). Still open: `InventoryActions` pickup
gating on a disabled Arm/Hand, and carry capacity/lifting (blocked on Strength/carry-capacity infra
above). `BodyPartType.Wing` exists but isn't granted to any race yet.

#### Per-body-part vs whole-entity status effects

`StatusEffectStack`/`StatusEffectAuraApplierRegistry` apply every effect entity-wide today -- correct
for Poison (systemic), wrong for Burning on a Complex entity (a burning leg reads better, and ties to
targeted-damage above: lava burning legs should apply Burning to the legs specifically). Needs a
part-scoped vs. entity-scoped declaration on `StatusEffectGrant`/`IStatusEffectAuraApplier`, and a new
store keyed by (entityId, bodyPartId) for the part-scoped case. Feeds the HealthWindow item
(Presentation).

#### Movement System

`SeekTarget` movement mode.

#### Lootbox delivery, and moving Lootbox out of Achievements

`AchievementModule`'s unlock path describes a `Lootbox` reward in the notification but never calls
`InventoryActions.AddItem` to actually deliver it (now available, unblocked). Lootboxes can only be
*opened* in Safe Rooms once opening exists. Separately: `Lootbox`/`LootboxRarity` currently live in and
are named for Achievements, but quests/loot-drops/level-up should be able to award one too -- move into
their own module once a second real awarder exists.

#### NPC component

No direct "is this an NPC" marker -- inferred indirectly today (exclude `PlayerEntityId`, or a specific
race check). Raised by `TemporaryNpcLootGrant`, which targets Goblin/Fairy/Ghost individually.

#### In-game day/time tracking

`DeadComponent.DiedAtFrame` only shows a raw frame tick in the corpse summary. A real calendar/clock
would make that (and anything else wanting a timestamp) human-readable. Also unlocks the Crawler TV show
item below (time-gated interactions).

#### Restock shops on the day/night swap

Follow-up to In-game day/time tracking above, which this is blocked on -- no real "day/night" concept
exists yet to swap on. Once one does, reroll a shop's stock (`ShopStock.GrantRandomStock`,
`PLAN-shops.md`) on the transition, and reset its own Gold back toward its starting amount so a
shop the player has drained doesn't stay unable to buy anything forever. Today a shop's stock is
rolled once at spawn and never refreshes.

#### Preferred stock for items added to shops

`ShopStockPreferenceComponent`/`EnsurePreferredStockLevel` (`PLAN-stock-based-shop-pricing.md`) is
only ever assigned by `ShopStock.GrantRandomStock` at spawn-time stocking. A player selling a shop an
item type it has never stocked before -- via ordinary drag-sell today, or the trade window once it
lands -- silently falls back to `ShopStockPricing.DefaultPreferredStockLevel` (20) regardless of what
the item actually is, rather than a hand-tuned value the way spawn-stocked items get. Give a sold-in
item type a sensible preferred level the first time it lands in a shop that's never carried it (a flat
default scaled off the sold quantity, an item-tag-based table, or similar) instead of always silently
defaulting to 20.

#### CVS (Cosmic Value Shop) general store

A "General Store" shop blueprint themed as a CVS ("Cosmic Value Shop") parody -- larger than average
buy/sell margins vs. a normal `GeneralShop`. First use grants an achievement whose reward is a "CVS
Receipt" item, its description text unusually long and generated from the actual items/currency
traded that session (the joke being real CVS receipts). Also enrolls the player in a newsletter
delivered via floor mail each floor -- needs floor mail as a delivery channel (no mail system exists
yet) and depends on achievement rewards being deliverable (see Lootbox delivery above, mostly landed).

Also add a CVS Rewards currency, worth 10% of a Gold in trades (`ShopActions` pricing math would need
a real conversion-rate concept, not just another flat `CurrencyType` enum value). Blocked on making
`CurrencyRowContent` support more than its current hardcoded Gold/Credits pair -- it needs to become a
dynamic/expandable list before a third currency can show up in it at all.

#### Crawler TV show

Interacting with a television object at specific times of day plays an in-universe "show" -- flavor
content, no mechanical effect. Blocked on In-game day/time tracking above (needs a real clock to gate
on). See the matching Global item for the joke in-show advertisement.

#### Achievement content backlog

15 achievements ship today to prove the pipeline; rest is a deliberate incremental backlog (many
low-value early, tapering to fewer/higher-value by midgame). TODO: give each achievement a pool of
descriptions instead of one fixed string.

Design-target examples (implement once the underlying system lands): start with a cat, find a Borough
Boss, punch a slime, kill an armed enemy bare-handed, kill 20+ non-combatants in one attack, reach level
2, wear magical gear, spell level 3, first corpse loot, store 10 tons of weight.

Implemented-but-not-yet-unlockable, waiting on dependencies: `LonerAchievement`/
`UnarmedCombatAchievement`/`EmptyPocketsAchievement` unlock unconditionally today (no
companion/equipment/start-kit-selection systems exist to gate them properly yet -- revisit each once
its dependency lands). `BigMusclesAchievement`/`UnbreakableAchievement`/`ShanghaiKidAchievement`/
`RevengeOfTheNerdsAchievement`/`KillerQueenAchievement`/`MinMaxerAchievement` react to
`AbilityScoreBaseValueChangedEvent`, which nothing publishes yet (no level-up/permanent-boost system) --
all six also currently reward a placeholder "upgrade choice" with no upgrade-choice system to back it.

#### Tag.Spell can drift out of sync with the actions it describes

Hand-authored, independent of `IActionActivator` -- nothing enforces it, and `SpellCasterAchievement`
trusts it alone. Either drop `Tag.Spell` and key off `action.Activator is SpellActivator` directly, or
keep it as an independent classification but have `SpellActivator`/its registration apply it
automatically so a definition can't forget it.

#### Boundary-aware ProcessingTierSystem recompute

`ProcessingTierSystem` recomputes every movement-capable entity's tier once per its own stripe turn
regardless of whether anything changed. Targeted alternative: a coarse spatial grid (cf. `AuraGrid`) so
a player move only re-tiers entities in the thin band straddling the Local-radius ring. Real structural
addition (new spatial index, insert/remove/move bookkeeping, a genuine correctness surface around
boundary-band width) -- only worth it once profiling actually confirms this as a bottleneck (one past
pass was inconclusive, coincided with unrelated Paralysis load).

#### DelayedActionSystem polls every pending action every frame, untiered

Unlike its siblings (`ActionLockSystem`/`ActionCooldownSystem`/`StatModifierExpirySystem`/
`BurningSystem`, all `TieredEntityStripeSet`), uses a flat `StripeCount = 1` and borrows
`ActionLockComponent.LockFramesRemaining` instead of owning a countdown. Not a measured problem today
(no Delayed action has a long windup). If it becomes one: give `PendingDelayedActionComponent` its own
`ITickCountdown` via `CountdownTicker.Tick`, and a `TieredEntityStripeSet` like its siblings. (A
callback on `ActionLockComponent` itself was considered and rejected -- would pull Actions-specific
knowledge into a generic Core primitive.)

#### Entity displacement with damage

`World.MoveEntity`/`PlaceEntityOnMap` no-op when a Blocking destination is occupied -- too blunt for
knockback/forced-push. No such effect exists yet; once one does, needs its own resolution (collision
damage in lieu of moving, or redirect to nearest free cell).

#### Dungeon Anarchist's Cookbook

Rare floor-3 item, many forms (one per specialization) with recipes/hints for that build, encouraging a
different playstyle next run. Depends on floor-specific guaranteed drops (no per-floor loot table yet)
and a way to pick which specialization a run's copy targets. Each form's pages should support
player-added multiline notes (a real second `TextBox.Multiline` consumer, see Text input in
`IMPLEMENTATION-NOTES.md`).

**Meta-progression across runs** (two directions, neither designed): (1) tiny permanent Ability
Score/Skill boosts derived from a just-ended run's build -- Skills should carry over far more rarely
than Ability Scores (a skill is already a bigger power swing). (2) A shared New Game Action Pool each
run banks one action into, offered as a starting choice on a fresh run. Both need a new persistent,
save-file-level meta-progression store distinct from anything in a single `EcsContext`.

## Presentation

### High Priority

#### Global hard minimum/maximum element sizes for user resizing

`UiInputController.ComputeResize`/`ClampResizeToBounds` clamp a drag-resize to `element.MinimumSize`/
`MaximumSize` alone (`ElementLayoutOptions.MinimumSize`/`MaximumSize`, both optional per element) --
`Element.cs`'s own Build defaults an unset `MinimumSize` to `Vector2(0, 0)`, so any element without an
explicit minimum can be dragged all the way down to zero (or effectively zero) width/height. That's the
root cause class behind the TextWindow/StringUtility crash just fixed (a HealthWindow resized to a
degenerate size fed a negative wrap width into word-wrap) -- that fix only patched the one downstream
symptom, not the underlying gap. Add an engine-wide hard minimum and maximum (e.g. a constant pair on
`UiInputController` or a `WindowService`-level config) that every resize clamps against unconditionally,
regardless of whether the element sets its own `MinimumSize`/`MaximumSize`. A per-element optional
min/max should only ever narrow that global range, never escape it -- needs validation (or clamping) at
whichever point an element's own min/max gets set, so a caller can't accidentally configure one outside
the global bounds.

#### Inventory management -- grid cell reorder still open

Read-only view, tabs, drag-onto-hotbar, and click-to-inspect all landed (`IMPLEMENTATION-NOTES.md`).
Still open: dragging one grid cell onto another to reorder -- blocked on Standard widget set below.

#### Stack Controls and Partial Stacks

Supersedes the former "Manual stack splitting and merging" entry -- broader scope. Today every
stack-moving interaction (mouse drag, context menu Give/Take/Sell/Buy, the trade window's Add/Remove)
is all-or-nothing: the whole stack moves or none of it. Needs a single, consistent control scheme --
across both mouse actions (drag gestures, modifier keys) and context-menu options -- covering all
three: move the whole stack, move half (rounded down), and move a player-chosen exact amount (a
quantity-prompt UI, still not built). Whatever scheme is chosen must work identically in *any* item
grid (player inventory, corpse/chest loot, shop, trade window), not be special-cased per window.
Splitting also needs a real primitive: peeling part of a stack off into a new, separate stack instead
of only being able to move a stack as a whole. And the reverse -- combining two stacks of the same
item (respecting divergence: only stacks with equivalent Override/IsDivergent state can merge, per
`InventoryActions.AreEquivalentOverrides`) into one, which today only happens automatically as a
side effect of `AddItem`/`AddItemWithOverride`'s own capacity-driven merging, never player-initiated.
Investigate how other games solve this (Minecraft's right-click-half/right-click-drag-spread and
shift-click quick-transfer; WoW's vendor shift-click quantity popup; Path of Exile's shift-click-drag
quantity slider) before settling on this game's own scheme -- see the industry-standard interface
investigation in this session's transcript for a starting comparison. Blocked on grid drag-to-reorder
(above) and a quantity-prompt UI (needs Context menu / mouse button coverage's remaining scope).

#### Fix Compare in shop mode

`InventoryGridContent.UpdateCompareState` makes shop mode and Compare mode mutually exclusive by
design -- while a shop is open, every cell's `CompareState` reflects shop trade eligibility instead of
compare eligibility, on the reasoning that the two "never both meaningfully active." In practice nothing
actually disarms `ItemComparisonController`/clears `MapViewState.CompareRequiredActivatorType` when a
shop opens, so a player who armed Compare right before (or manages to arm it while a shop is open, since
`BuildItemContextMenu`'s "Compare" option is offered unconditionally in shop mode) ends up with Compare
still armed but every cell showing shop pricing/eligibility instead of compare highlighting -- a real
functional inconsistency, not just a cosmetic one, since the player has no visual cue for what a click
will actually do. Fix by either disarming Compare when a shop opens, or suppressing/graying the
"Compare" context-menu option while shop mode is active, whichever reads more predictably to the player.

#### Equipped-item comparison

Blocked on Equipment existing (Game + Presentation). See Item Details Comparison in
`IMPLEMENTATION-NOTES.md` for the landed non-equipped half.

#### Advanced sort control -- context-menu of sort options

Today's sort control is a blind click-to-cycle button. Replace with an icon expanding into a context
menu listing every option -- depends on Context menu / mouse button coverage's remaining scope (or some
generalized "popup a list of choices" primitive). Should also cover sorting by a stat (e.g. wand
charges) once per-slot divergence gives items stats worth sorting by -- the option list may need to be
built dynamically per tab, not a fixed list.

#### Search icon that expands into a search bar

Both the Tab search box and the item-name search box are always-visible today. Space-efficient
alternative: a small icon expanding into the box on click (ghost text/debounce unchanged), collapsing
back when empty and unfocused. Pure presentation change.

#### Inventory tab reordering + custom-tag trailing tab

Dynamic per-tag tabs landed (`IMPLEMENTATION-NOTES.md`). Still open: user-reordering the default sort,
and a trailing "+" tab for custom user-created tags.

#### Item weight (definition-only) and race weight ranges

- Weight lives on `ItemDefinition` only (never per-stack) -- same split as Name/Description/Tags. A
  stack's total weight = `definition.Weight * stack.Quantity`, computed on demand.
- Units: pounds. Potions/scrolls default 0.1 lbs today (placeholder for that item class).
- Race weight ranges (separate concept from carry capacity): Goblin 40-70 lbs, Fairy 20-40 lbs (rough
  guesses, no lore anchor). Player and Ghost still need ranges -- Player depends on
  character-customization decisions out of scope here; Ghost's "weightless/ethereal" may be the real
  answer, worth deciding deliberately.

#### Game over screen on player 0 HP

`HealthDamage.Apply`/`DeathSystem`/`DeadComponent` exempt the player from death today since there's no
end-state UI. Build this before lifting that exemption.

#### TextBox context menu wiring, and "Bind To..." sub-menu

Context menu mechanism + AdvancedMapContextMenu landed (`IMPLEMENTATION-NOTES.md`). Still open:
TextBox's Cut/Copy/Paste/Select All (currently keyboard-only) via the same `ContextMenu` mechanism; and
an inventory item's "Bind To..." (hotbar slot picker) -- needs a genuinely new capability, cascading
sub-menus (an option carrying a nested option list, opened east of the clicked row, a second managed
`ContextMenu` instance, and updating the outside-click check to cover both popups). Neither of
AdvancedMapContextMenu's five landed menus needed this.

#### Player stats v1

Persisted view of the player's active stats -- fixed set.

#### Player attack button or key

Partially addressed: Default Attack is bound to F, fires on single press (double-tap for auto-target).
Still open: making it visually distinct from the hotbar rather than just one more slot -- revisit
whether that's still wanted now the hotbar itself is fast.

#### Standard widget set

No checkbox, radio button, dropdown, slider, list box, or tree view (`Toggle` covers checkbox --
`IMPLEMENTATION-NOTES.md`). Tabs exist (`TabbedContent`). Inventory/spell hotbar and equipment/stats
windows still want list/grid controls beyond what exists.

#### Tooltips, description/stat views, context menus, click-to-arm on inventory & magic menus

Item inspection popup and hover summary both landed, inventory-only. Remaining: (1) extend both to the
future Magic Menu; (2) right-click context menus (arm/drop/inspect) on cells in either menu, blocked on
Context menu coverage's remaining scope; (3) click-to-arm/cast directly from a menu cell -- today only
the hotbar can arm an action/item.

#### Extract UiInputController.ResolveContentDrag's drag-drop resolution into something self-contained

`ResolveContentDrag` (`Presentation/Input/UiInputController.cs`) is the single dispatch point every
content-drag release goes through -- item stack, Merged Stack, action, and currency drags alike -- and
it's grown a new branch with every drop-target-aware feature added so far: plain inventory-to-inventory
transfer, shop buy/sell (`ShopActions.TryBuyFromShop`/`TrySellToShop`), hotbar binding, and now the whole
trade window, which needed two more dedicated methods on this same class
(`ResolveTradeAwareItemDrag`/`ResolveTradeAwareCurrencyDrag`) just to hold PLAN-trade-window.md's own
drag-drop eligibility table. Each of those methods already encodes another feature's business rules
(shop pricing eligibility, trade eligibility per column, hotbar bind rules) directly inside the input
layer, rather than that feature owning its own drag-resolution logic -- `UiInputController` has to know
about `ShopActions`/trade-offer entities/hotbar slots all at once instead of just recognizing "a drag
ended here" and asking someone else what that means. The next drop-target-aware feature (Magic Menu,
Equipment slots, grid cell reorder) will add yet another branch/method here rather than being
self-contained. Needs a real design pass, not a mechanical split: something like a pluggable per-
drop-target-kind resolver that `ResolveContentDrag` looks up and delegates to, so each feature registers
its own drag-resolution strategy instead of `UiInputController` accumulating every feature's rules
inline. Scope the design before touching code -- this method is load-bearing (every stack/currency/
action move in the game routes through it) and already has real regression coverage
(`UiInputControllerTests.cs`) that any refactor must keep passing.

### Medium Priority

#### TextDivider label clipping and right-line spacing

Investigate why the bottom of some `TextDivider` label letters (descenders -- g/y/p, etc.) render
clipped -- possibly `DrawContent`'s `textY` centering (`(ContentSize.Y - textSize.Y) / 2f`) against a
`MeasureString`-reported height that doesn't fully account for a descender's real glyph extent, combined
with a tight `ContentSize.Y` (e.g. `HealthWindow.RowHeight`) leaving no slack. Separately: `textEnd`
(`textStart + textSize.X`) is where the right-hand divider line starts immediately, with no gap against
the label -- move it further right (a small fixed or width-fraction-relative pad before `rightEdge >
textEnd`'s line-drawing) so the line doesn't sit flush against the text.

#### Diagonal movement input timing

`PlayerMovementController.HandleInput` only treats a move as diagonal if both keys are down in the
exact same poll -- a few-frame gap between W and D lands as cardinal. Needs a short input-buffering
window before committing to a cardinal move.

#### Targeting tile highlights extend beyond the actual spell/scroll range

`ActionTargetingController.ComputeTargetableTiles` approximates every cursor-directed shape as a
`Burst` scatter (no cursor direction yet at arm time) -- overshoots for Cone/Line, showing tiles as
targetable that the real shape could never hit. Confirmed in-game. Needs a real per-shape reachable-area
computation, at least for Cone/Line.

#### Confirming activation on an empty tile still fires the spell/scroll

`TryConfirmActivationAtTile` only checks the clicked tile is in `TargetableTiles`, never whether the
resolved footprint actually contains an occupant -- clicking empty space still consumes a charge/mana
for no effect. Needs an occupant check, at minimum for `SingleTarget`/`Adjacent`; an AOE shape landing
empty but catching something else in its footprint is a separate case to decide deliberately.

#### Minimap + Fog of War, folded into Neighborhood/Borough zoom

Collapsed minimap (bottom-right); expanding it takes over the zoom-out feature rather than living
alongside it. Shares work with `MapCamera`'s `Neighborhood` (1000x1000)/`Borough` (2000x2000, same
region sizes as `ProcessingTierSystem`'s tiers) zoom levels -- static structures + boss/landmark
sprites only, no moving entities, snapping to preset regions instead of following the player.

Fog of War: unexplored areas render blank on minimap/zoom/main viewport, revealed permanently once seen
(or re-fogged -- undecided). Needs a new per-tile (or per-region, for performance at Neighborhood/Borough
scale) visibility store, keyed like `AuraGrid`/`MapTintGrid`'s flat-index dictionaries.

#### Magic Menu

Spell-equivalent of the inventory menu, mirroring `InventoryWindowController`'s Button+pooled-Window+
`TabbedContent` pattern. "Known spells" isn't a tracked concept -- just a `MultiComponentPool<ActionInstanceComponent>`
query filtered by `Tag.Spell` (same drift risk as Tag.Spell above).

#### Comprehensive control-selection feature

`UiInputController.SetFocus`'s `NextFocusableDescendant` redirect (focusing a window with a `TextBox`
child jumps into the TextBox) was piggybacked on by both the quest composer and the Inventory search
box, causing two confirmed bugs (a resize/move drag on the Inventory window spuriously refocused its
search box) -- fixed narrowly (`HandleMousePress` only resolves focus for a plain click, never
Move/Resize), which also quietly removed the quest composer's "drag title bar to focus" convenience.
Needs a real, explicit design: click-to-focus scoped strictly to a direct hit; or a window-declared
"default control" focused on `Initialize`; or keep the redirect but gate it explicitly and give
Move/Resize an opt-in.

#### Inventory grid item badge clarity

`InventoryItemStackCell` has accumulated several badges (quantity-or-charges number, Merged-Stack "+",
expanded-group border) with no deliberate pass on how they read together. Confirmed real ambiguity: the
bottom-right number silently means "how many I have" vs. "uses left" with zero visual distinction
(`HotbarContent` has the identical ambiguity, same fix should cover both). No redesign specified --
needs a deliberate look once there's room to design against.

#### Button tooltips

No icon/symbol-only button explains itself on hover. Needs the existing `Tooltip` pattern on: every
`Window` title button, the Inventory/Ability Score folder tiles, the Notification/Inventory folder
icons. Mechanical -- reusing an existing pattern in a few more places.

### Low Priority

#### Split Presentation into Presentation + UIEngine projects

Mirrors the Engine->Game split one layer up. Dependency rule: `UIEngine` references only `Engine`;
`Presentation` references `UIEngine`/`Engine`/`Game`; never the reverse. Clearly-`UIEngine` and
clearly-stays-`Presentation` sets are both fairly obvious (generic Element/window framework vs.
game-specific concrete windows). The hard part: `UiInputController` is mostly generic but has
game-specific branches (`HotbarController`/`ActionTargetingController`/drag-payload type-matching) woven
directly into its methods -- needs those pulled behind a `UIEngine`-defined hook `Presentation`
implements, not a straight file move. `ColorPalettes`/`Chrome` need the same judgment call. No
migration plan designed -- purely a scoping note, low priority until UIEngine-shaped reuse becomes real.

#### Abstract element pool factory registration

Every `RegisterFactory<T>` call hand-writes the same `FontService`/`ElementPoolService`/`LabelRenderer`
triple with only a few type-specific extras varying. Worth a helper taking just the extras. Boilerplate,
not error-prone -- low priority.

#### Red X marker over dead entities

`MapWindow.TryDrawEntityVisual` has no `DeadComponent` check -- a corpse looks identical to a
just-motionless living entity. Draw a red X overlay when `DeadComponent` is present.

#### Folder glow blink

`Folder.SetGlow` (used by `NotificationCenter`'s unread-glow) is flat on/off. Make it pulse instead --
more noticeable, especially once more things drive glow (Magic Menu, Skills leveling).

#### "Open many" button for stacked achievement notifications

`NotificationCenter`'s per-category unread count (`_unreadByCategory`) only opens one notification at a
time (`OpenNextNotification`). Add a button that instead opens as many non-overlapping achievement
windows at once as will fit on screen (cascade/tile placement, cf. `WindowCascadePlacement`), so a
backlog of unread achievements doesn't have to be worked through one popup at a time.

#### Highlighted-tile visual redesign -- pick one of two directions

`DrawMaskedTileHighlight` draws every highlighted tile as one uniform translucent wash today (an
earlier opaque-border version was deliberately replaced so the sprite stays visible). Two competing
follow-up directions, worth deciding between rather than landing both: (1) add back a thin 100%-opacity
border ring on top of the wash; (2) make the wash fainter and replace the ring with four opaque corner
brackets instead of a full perimeter. No corner-mark geometry worked out for (2) yet.

#### Extract a shared tick-fraction HUD bar element

`PlayerHealthBarContent`/`PlayerManaBarContent` are near-duplicates (same outline+inset-fill+tick-mark
shape, differing only in backing component/palette). Tolerable at two copies -- abstract into one
generic element if a third shows up (e.g. Soul Essence). `MapWindow.DrawHealthBar` is arguably a lighter
third instance already (same fraction math, no ticks, per-any-entity) -- include it in scope if this is
ever picked up.

#### Context menu amount picker

`CurrencyRowContent`'s Give/Take (and their "All" variants -- see `PLAN-storage-containers.md`)
always move a currency's *entire* balance; a currency element dragged onto another entity's grid/row
does the same. Add a textbox popup (reusing `TextBox`, same mechanism `TextBox context menu wiring`
above wants for Cut/Copy/Paste) letting the player specify a partial amount instead, both for the
context menu options and (harder -- needs a way to intercept a drag-drop before it resolves) a
partial-amount drag.

#### Mark items as Sell ("junk"), and a bulk-sell-tab button in shop mode

A per-stack "Sell" marking -- the bulk-sale equivalent of other games' "junk" flag -- likely a new
`InventoryItemStackComponent` field alongside the existing `IsDisabled` one. While a shop is open
(`MapViewState.OpenShopEntityId`), add a button to the player's own inventory tab (`GridControl`/
`InventoryTabContent`) that sells every Sell-marked, currently-eligible item in the *active* tab
through `ShopActions.TrySellToShop` (`PLAN-shops.md`) in one action -- any tab, not only a
dedicated "Sell" tab; marking curates what a sweep picks up, it isn't itself a tab requirement.

#### Per-entity sprite scale

`SpriteRenderer.Draw` always stretches to fill the tile footprint exactly -- wrong for character
sprites (confirmed in-game: player needs to render larger, goblins smaller). Needs a per-entity/
per-`SpriteComponent` scale factor applied in `MapWindow.TryDrawEntityVisual`.

#### Multi-tile sprites

No entity's sprite spans more than one tile today -- `TransformComponent.Size` already carries a
footprint (e.g. a corpse/tiny-entity grid already reasons about it), but `MapWindow`'s draw path
always renders one sprite stretched to exactly one tile's own `CurrentTileSize`, never a single
sprite spanning the whole footprint. `Shop`'s own `Sprite = "Shop-1x1"` (`PLAN-shops.md`) is a
deliberately-named 1x1 placeholder for this -- a real multi-tile shop sprite (e.g. "Shop-2x2") is
the concrete first implementation once this lands.

#### Status effect stack count on the player's status bar

`PlayerStatusEffectsContent` shows one icon per effect type regardless of stack count. Overlay the
current count (`StatusEffectQueries.CountStacks`), same corner-text treatment `InventoryItemStackCell`
already uses.

#### Player stats v2

Let the player choose which stats to display. Follow-on to Player stats v1.

#### End of level staircase (Presentation)

Rendering/interaction for the staircase. See the matching Game item.

#### Equipment menu

Side-by-side with inventory, collapsible either direction, click-and-drag equipping. Pauses while open
-- just call `layers.OpenMenuWindow(window)`/`CloseMenuWindow(window)` (see Pause modality,
`IMPLEMENTATION-NOTES.md`), no new modality code needed.

#### Player health bar hover -- per-body-part HP dropdown

Hovering the player's health bar would show a small popup: total % first, then one line per body part
(name + current/max %), reusing the existing delay-gated `HoverPopupWindow` pattern. A `SimpleHealth`
player (today, always) has nothing to show beyond the total line until the player race is ever made
Complex. Lighter-weight than `HealthWindow` (`IMPLEMENTATION-NOTES.md`) -- a glanceable hover, not a
full window. Worth sharing one "format a body part's HP line" helper with `HealthWindow` once both
exist.

#### Text input undo/redo (Ctrl+Z/Ctrl+Y)

Deliberately left out of Text Input Enhanced Features (`IMPLEMENTATION-NOTES.md`) -- needs a real
edit-history design (edit stack or snapshots, coalescing rules, a depth cap), and it's not yet clear
whether the history should live on `TextBox` or a shared primitive a future second editable control
would also want.

#### WrapContent parent sizing collapses when a child resizes itself after attach

Discovered building the quest composer: a `WrapContent` window whose size depends on a child, paired
with a child that resizes *itself* later (not at attach time), collapses both toward `(0,0)`. Root
cause: `Window.Measure` unconditionally overwrites a child's `MaximumSize` with
`_parentWindow.ContentSize - RelativePosition` every pass -- circular for a `WrapContent` parent, whose
own `ContentSize` starts at `(0,0)` and is derived from the same children. Today's workaround (quest
composer stays `Fixed`, explicitly resized off the TextBox's `Resized` event) doesn't generalize. Real
fix likely: a child's own explicitly-authored `MaximumSize` should take precedence over a not-yet-settled
parent `ContentSize` when the parent is itself `WrapContent` mid-resolution. Touches the shared
Measure/Arrange pipeline -- worth a real design pass, not a quick patch.

#### Selectable/copyable read-only text -- move selection out of TextBox into TextWindow

`TextBox`'s selection machinery (hit-testing, double/triple-click, click-drag, Ctrl+A, Ctrl+C) is built
against `TextWindow`'s wrap/display, not anything editing-specific -- none of it needs a caret or
typing. Worth moving selection+copy up onto `TextWindow` itself (gated so a plain `TextWindow` never
shows a caret/accepts input), leaving `TextBox` to extend it with just caret/editing. Investigated
extending the same idea to `Window.TitleText`: no -- title text is a separate, simpler raw-string
mechanism not built on `TextWindow` at all; rebuilding it on shared infra to support copying mostly-short
static labels is a much bigger change for low value.

#### Scrollbars

Scrolling itself works (mouse wheel), but no visual affordance -- no thumb, no track, no click-drag.

#### Review MapWindow for properties that belong on MapViewState instead

MapWindow has accumulated its own instance fields (camera/zoom state, hotkey bookkeeping, hover
buffers) alongside `MapViewState`, the established home for state other windows/content read. Worth a
pass checking whether any should move, particularly as more Presentation work needs to read that state.

#### Window minimize completeness

Two standing gaps: minimized windows don't hide/show their children (still draw underneath); sibling
windows in a tiled parent don't retile when one minimizes/restores (same class of bug already fixed for
add/remove via `RetileChildrenFrom`, not yet extended to `SetWindowDisplayMode`).

#### Window docking / splitters

No way to resize the boundary between two adjacent panes, or dock a window to the screen/another
window's edge.

#### Window open/close/minimize animation

Everything snaps instantly. Pure polish, lowest priority UI item.

#### Options menu

No settings screen exists -- Escape currently does nothing. Wanted: Escape (global, unconditional, same
as Tab) opens it, and the game pauses while open -- just `OpenMenuWindow`/`CloseMenuWindow` (see Pause
modality, `IMPLEMENTATION-NOTES.md`), no new modality code needed.

#### Keybindings page on the options menu

Needs Options menu (above) to live in, and Standard widget set (needs at least something list-like) --
today's hotkeys are hardcoded in `MapWindow.OnHotkeysAction`/`UiInputController`. Would eventually want
persisted storage for rebinds (see Data storage under Global, which today only covers window geometry).

#### Direct menu-opening hotkeys (e.g. I for Inventory)

No keyboard shortcut opens any HUD window directly -- only folder-tile clicks toggle them. Wanted: a
global, unconditional hotkey per menu (I to start), same treatment `UiInputController` already gives
Tab/Escape.

#### Targeted key-press routing instead of a full-keyboard scan

`RouteKeyPressesToFocusedWindow` calls `KeyboardState.GetPressedKeys()` every frame a window is focused
(confirmed via reflection: FNA has no non-allocating variant) -- allocates every frame for the session.
`HandleKeyPress` has exactly one real consumer (`TextBox`, caring only about Backspace). Let the focused
content declare the small key set it actually wants checked instead of scanning/diffing the whole
keyboard.

#### Chat and speech

Glowing per-NPC speech bubbles (clickable for the full line), separate from a WoW-style configurable
chat log (Loot/Combat/Local Chat/Notifications tabs, user-routable message types). `NotificationCenter`
is the closest precedent but is popup-shaped, not a persistent scrollback -- a different, bigger widget.

#### Visual improvement pass

Dedicated sizing/placement/color pass across Presentation once the HUD stops churning -- today's values
(`HudMetrics`, scattered per-content constants) were each chosen locally.

#### Investigate mask-based recoloring for shared sprites (potions as the case)

Every potion needs a fully-authored sprite even though most differ only by liquid color.
`SpriteManifest`/`SpriteSheetService` have no tinting concept. Worth investigating a mask (grayscale/
alpha region marking recolorable pixels) + a `Color` field `SpriteRenderer` tints per-instance, instead
of a duplicate sprite per color variant. Generalizes to any other "one silhouette, many colors" case
(dyed equipment, faction banners).

## Global

### High Priority

#### Data storage, starting with window locations and sizes

No serialization/save-and-load system exists anywhere. Window layout
(`WindowRelativePosition`/`WindowCurrentSize`/`WindowDisplay`) is the first concrete use case -- every
launch starts from whatever `ShellBootstrapper` hardcodes. Once real per-window saved positions exist, a
manually-dragged window's saved position must always win over `WindowCascadePlacement`'s
always-cascade-and-clamp default (today's default exists *because* saved positions don't yet) -- keyed
per logical window/slot, not globally (3 customized comparison columns keep their spots; a 4th still
falls back to cascade placement).

Treat as the first slice of a general data-storage system (entity/world save state will eventually need
the same serialize-to-disk mechanism) -- but start narrow; window geometry has no cross-entity
references to untangle.

**Modded content must degrade gracefully, not corrupt a save.** Once entity/world state (inventory
items, granted abilities, `IActionActivator`/`ActionEffect` catalog entries -- see
`PLAN-action-effect-activator.md`) is serialized, a saved `Guid` reference to mod-defined content can go
stale if that mod changes before the save reloads (RimWorld/PoE's well-known failure mode). Fail
hierarchy, decided up front: (1) prefer a mod-supplied replacement/migration, (2) fall back to dropping
just the affected reference while the rest of the save loads, (3) last resort, drop the whole entity if
the missing content is load-bearing for it. Consider letting a mod register its own fallback id per
content id it defines.

### Low Priority

#### Debug/event logging with levels

`Game/Diagnostics/PlayerActivityLog.cs` is a narrow, single-purpose EventBus subscriber (Burning
damage/moves to a file), deliberately not a general logging facility. Worth a real design (log
levels, a generic "subscribe any event to a log line" mechanism, configurable sinks) once more than one
thing wants to log. See Entity storage below for a narrower, related need.

#### Entity storage -- suspend an entity from processing without per-system checks

`World.RemoveEntityFromMap` already carries an inline TODO: it zeroes `TransformComponent.Position`
when unregistering from Map's spatial index, losing where the entity was. Broader gap: taking an entity
out of *every* system's processing, not just map occupancy, without each system needing its own check.
`ComponentManager.RemoveAllComponents` already does the mechanical half (drops entityId from every
pool, which is what actually keeps `EntityStripeSet`/`TieredEntityStripeSet` from revisiting it) but
doesn't preserve what was removed -- usable today only for a real despawn, not "store and restore."

Needs: snapshot an entity's full component set into serializable form, remove it the same way
`RemoveAllComponents` does, later rehydrate exactly what it had (not a fresh blueprint instance). Two
open questions: does this ride the general save/load system (Data storage above) or start as a narrower
same-session freeze/thaw first; how do cross-entity references (an equipped item's owner, a pet's
bonded player) stay valid across a storage/restore cycle (same class of problem Data storage's modded-
content section already raises for saves generally). Motivating case: a tamed companion or caged NPC
that can leave and rejoin the active simulation with its exact accumulated state intact.

#### Field and property cleanup

General pass once UI/core systems stop churning -- auto-properties with no logic that could be plain
fields, or the reverse, plus consistency in when a type uses plain fields (see
`WindowGeometryState`/`WindowTitleState`/etc.'s own doc comments) vs. properties. Housekeeping, not a
bug list.

#### Solution-wide code style cleanup

Conventions clarified while building the focus/keyboard-routing system, not retroactively applied
elsewhere: comments explain WHY only when genuinely non-obvious; ternaries on three lines (condition,
`?` branch, `:` branch); one return per method except leading guard clauses.
`UiInputController.cs`/`Window.cs`/`MapWindow.cs` follow these only where actually touched by that work
-- pre-existing code in the same files, and everywhere else, predates them. Related to Field and
property cleanup above -- possibly the same pass.

#### Possible future UI gaps, likely out of scope

Tooltips (mostly landed already elsewhere), localization/IME, and accessibility (screen reader) hooks
are standard in general GUI frameworks, but this is an admin/debug UI over a game world, not a general
application shell. Noted for completeness, not expected soon.

#### DungeonCrawlerWorldAdvertisement joke website + in-show ad

Meta joke, not in-engine content: a standalone `DungeonCrawlerWorldAdvertisement` website that makes fun
of and rickrolls the visitor. The Crawler TV show (Game, above) would air an in-universe ad for it;
navigating to the advertised URL (typed or via an in-show QR code) is the payoff. Two separate
deliverables (the site itself, and the in-show ad segment) -- the site has no dependency on the game
engine at all.
