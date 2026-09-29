# Toggle Items

(Pre-implementation. Replaces TODO.md's "Toggle item activator" entry, which is deleted when
Phase 1 lands. Toxic Idol is the concrete implementation.)

## Context

A toggle item is an inventory item the holder switches on and off. While it is on, its effects
apply to whoever holds it. It is never consumed.

What exists today:
- `ToxicIdol` uses `PotionActivator` with one `AuraSourceGrant(Poison, 16, DarkGreen)` and no
  duration. That puts `AuraSourceGrant` in its flip mode (`AuraSourceEffects.Toggle`): each
  activation adds the Poison source if the target has none, and removes it otherwise.
- Activation goes through `ConsumableActivationSystem`, which consumes a stack unit before applying
  effects. Turning the aura *off* therefore also costs an idol, and the action lock is set both ways.
- The on/off state is implicit: "the holder has a Poison aura source". Nothing ties it to the idol.
  Giving the idol away leaves the aura on the giver, and a second Poison source of any origin
  (another idol) is indistinguishable from the first, so the flip turns off whichever one exists.
- `AuraSourceEffects.Apply` is already idempotent (revoke then add) and `Revoke` is unconditional.
- `DeathSystem` calls `AuraSourceEffects.RemoveAll` on a corpse, and `GameBootstrapper` clears aura
  sources in `EntityDestroying`.
- Per-unit item state already exists: a wand's remaining charges live in its stack's `Override`
  (`item with { Activator = wandActivator with { Charges = n } }`), split off with
  `InventoryActions.PeelOneIntoDivergentStack`, and the hotbar binding is repointed to the new stack.
  Equal states merge back into one stack (`AreEquivalentOverrides` compares `Activator` with
  `Equals`).
- `InventoryActions.TryTransferStack` moves a stack verbatim (`Override` included) and is the single
  path for give, take, corpse looting, shop buy/sell, trade staging and plain drag-drop.
- `MultiComponentPool` raises `ComponentChanged` on every add and update, and nothing on removal.
- Loot boxes (IMPLEMENTATION-NOTES.md "Loot boxes") set the precedent for an item activation that isn't a system:
  a direct call from the Activate click, no queue, no lock.

## Design

### The rule

**The on/off state belongs to the item, and its effects belong to whoever holds it.** A toggled-on
idol stays on through every inventory change -- trading, selling, buying, looting, giving, and
dropping once that exists. When it leaves one holder, its effects are revoked there; when it arrives
at another, they are applied there. Only the holder switching it off turns it off.

Consequences, stated so they aren't mistaken for bugs:
- Selling a lit idol to a shop makes the shop radiate Poison, and buying it back keeps it lit.
- A corpse holding a lit idol doesn't radiate (it's dead, see Eligible holders), but the idol is
  still on, so looting it puts the aura on the looter immediately.
- A lit idol staged in the trade window doesn't radiate from the trade-offer entity (it isn't on the
  map), and radiates from whichever side ends up holding it.

### State: a per-unit activator field, stored the way wand charges are

- **`ToggleItemActivator(bool IsToggledOn = false) : IActionActivator`**, in
  `Game/Modules/Actions/Activators/`. `Targeting` is a fixed Self spec (range 0, area 0) and
  `Timing` a fixed `ActionTiming(FreeCast)`, both static instances: the interface requires them, and
  every existing consumer that reads them (tooltips, comparison) gets sensible values. Neither is
  used to activate a toggle item.
- A toggled-on unit is a divergent stack whose `Override` is
  `item with { Activator = toggle with { IsToggledOn = true } }`. Nothing new goes on
  `InventoryItemStackComponent`: every stack in the game would pay for a field only toggle items use.
- Because the state is in `Override`, `TryTransferStack` carries it for free, and `AreEquivalentOverrides`
  already keeps lit and unlit units in separate stacks while letting two lit idols share one.
- **The state only ever changes by moving a unit between stacks**, never by rewriting a stack's
  `Override` in place. That is what lets the holder effects (below) key off stack adds and removals
  alone.

### Turning one on or off: `ToggleItemActions.TryToggle`

`ToggleItemActions` (static, `Game/Modules/Inventory/`, next to `InventoryActions`):
`bool TryToggle(ComponentManager, ItemCatalog, int entityId, uint stackInstanceId, out uint newStackInstanceId)`.

1. Resolve the stack and its effective definition; refuse if it isn't a `ToggleItemActivator`, or
   the entity is dead.
2. Build the flipped definition: `item with { Activator = toggle with { IsToggledOn = !toggle.IsToggledOn } }`.
3. Move one unit into it:
   - Turning **on**: `InventoryActions.PeelOneIntoDivergentStack(..., flipped)`.
   - Turning **off**: if the flipped definition is equivalent to the catalog definition (the normal
     case), take the unit off its stack (`ConsumeItemByStackInstanceId`) and return it with
     `AddItem`, so it merges back into the plain stack instead of leaving an unlit divergent stack
     beside it. Otherwise (a stack that carried some other override too) `PeelOneIntoDivergentStack`.
4. Repoint the hotbar binding from the old stack id to the new one. The wand's private
   `RepointItemHotkeyBinding` moves to a shared `ItemHotkeyBindingActions.Repoint` that both
   `ConsumableActivationSystem` and `TryToggle` call, so a hotbar slot bound to the idol keeps
   switching the same unit on and off.

One unit per call: toggling a stack of five lit idols turns one off. The aura is one aura however
many lit idols the holder carries (see Stacking), so this is only visible in the stack counts.

**Not a system, and not `ConsumableActivationSystem`.** Toggling reuses the loot box reasoning: it
has no target, no windup, and reads and writes only the holder's own always-built inventory, so a
direct call from the click is enough. `ConsumableActivationSystem` is untouched and never sees a
toggle item.

**Never gated by the action lock and never sets it.** It's a flick of a switch, not an action, and
it matches the loot box decision. (If this should cost time later, it's a lock written through
`ActionLockGate.Lock` with `SimulationClock.CurrentFrame` here, not a queue.)

### Keeping holders in sync: `ToggledItemHolderEffects`

A Game service in `Game/Modules/Inventory/`, built once in `InventoryModule.Configure` from the same
pools `ConsumableActivationSystem` builds its effect context from, plus `SimulationClock` and
`EntityManager`. It is not a system: it has no per-frame work. It observes the
`InventoryItemStackComponent` pool, the same "the pool tells you, no caller has to remember" shape
timer wheels use.

- **Engine: two new opt-in `MultiComponentPool` events.** `ComponentAdded(entityId, denseIndex)`
  (fired by `Add` only, unlike `ComponentChanged`, which also fires on every quantity update) and
  `ComponentRemoving(entityId, denseIndex)` (fired before an instance is removed, while it can still
  be read, by every removal path including `Remove(entityId)` during destruction).
- **On `ComponentAdded`** of a stack whose effective activator is lit: if the holder is eligible,
  apply the item's effects to it.
- **On `ComponentRemoving`** of a lit stack: revert its effects on the holder, then re-apply the
  effects of every *other* lit stack the holder still carries (skipping the one being removed).
  That re-assert is what makes two lit Poison idols correct: removing one turns the shared Poison
  source off and the other turns it straight back on. `AuraSourceEffects.Apply` is idempotent, so
  re-applying is safe; it only runs when a lit stack leaves, which is rare.
- What reaches it, and what doesn't: `TryToggle` on (a lit stack added, or merged into an existing
  lit stack with no event, which is correct because the aura is already on); `TryToggle` off (the
  last lit unit's stack removed; an update that only lowers a lit stack's quantity raises nothing,
  also correct); every `TryTransferStack` (removed from one, added to the other); and any future
  drop or destroy path, with no change to it.
- **Eligible holders**: alive (no `DeadComponent`) and on the map (`World.IsOnMap`). A dead holder
  already had its sources stripped by `DeathSystem`; an off-map holder (the trade-offer entities)
  would splat its aura at the far-away unplaced position. An ineligible holder's lit items are
  simply dormant; reverting on one is harmless (`Revoke` is a no-op).
- **Destruction guard**: `EntityManager` gains `IsDestroying(int entityId)`, true from
  `EntityDestroying` until `DestroyEntity` returns. The service ignores that entity's stack events.
  Without it, removing one lit stack mid-destruction would re-assert another onto an entity whose
  aura sources `GameBootstrapper` has already cleared, leaving a source on a recycled id.
- The effect context is built like `ConsumableActivationSystem.BuildContext`, with source = target
  = holder, `Now = SimulationClock.CurrentFrame`, and the item's name and tags.

Known gaps, recorded rather than built:
- A holder that becomes eligible later (a future resurrection, or an entity placed onto the map
  while already holding a lit item) doesn't pick its lit items back up. Whatever makes that
  transition must call a public `ToggledItemHolderEffects.ReassertHolder(entityId)`.
- A timed grant of the same aura type (`AuraSourceExpirySystem` revoking a Scroll of Torch Light
  source) would also revoke a lit toggle's source of that type. No toggle item uses Light today.

### Reversible effects

Switching off has to undo exactly what switching on did, so the effects a toggle item may carry are
restricted to ones that can be reverted.

- **`IReversibleEffectEntry : IActionEffectEntry`** with `void Revert(ActionEffectContext context)`.
- **`AuraSourceGrant`**: the no-duration mode becomes a plain idempotent `AuraSourceEffects.Apply`
  instead of a flip, and `Revert` calls `AuraSourceEffects.Revoke`. The timed mode is unchanged and
  `Revert` on a timed grant revokes the same way (only permanent grants are expected on toggles).
  `AuraSourceEffects.Toggle` has no callers left and is deleted, and the flip-mode paragraph in
  `AuraSourceGrant`'s doc comment goes with it.
- **Validation at registration**: `Catalog<T>` takes an optional validation delegate that `Register`
  calls; `ItemCatalog` passes one that throws when a `ToggleItemActivator` item carries any entry
  that isn't `IReversibleEffectEntry`. A mod defining an irreversible toggle fails its dry run
  instead of leaving a permanent effect on the first holder.
- Only `AuraSourceGrant` implements it now. `StatModifierGrant` with `FrameDeadline.Never` is the
  obvious next one (stances), left to that work.

### Toxic Idol

```csharp
Tags: [Tag.Toggle],
Effects: [new ActionEffect([new AuraSourceGrant(StatusEffectType.Poison, AuraAndGlowStrength, Color.DarkGreen)])],
Summary: "Toggles a Poison aura (range 4) around whoever holds it.",
Activator: new ToggleItemActivator()
```

- `Tag.Toggle` is appended to `Tag`, which also gives toggle items their own inventory tab. `Potion`
  and `Consumable` are dropped because neither is true any more, and `Self` because nothing targets.
- Dropping `Tag.Potion` means the Potion Shop can no longer trade it, so it leaves
  `PotionShopStock` and stays in `GeneralShopStock` (confirm the General Shop's tag rule accepts it).
- `PlayerKit` grants 1 instead of 5 (five was a consumable's supply) and keeps the Slot 6 binding.
- The description drops "Holding it active" wording that implies the wielder only.
- The class doc comment loses the `PotionActivator`/Self-targeting explanation.
- `TemporaryNpcLootGrant` and `TreasureChest` are unchanged: they grant unlit idols.

### Presentation

- **Activate, double-click and hotbar press** on a toggle item call `ToggleItemActions.TryToggle`
  directly instead of arming, in `InventoryGridContent.TryActivate` and at the top of
  `ActionTargetingController.HandleItemSlotPress` (before the Tag.Self double-tap shortcut and
  before arming). No window closes and nothing is armed: there is no target to pick.
- The inventory context menu option reads "Turn on" or "Turn off" instead of "Activate", and is
  enabled regardless of the action lock (`IsPlayerActionLocked` is skipped for toggles).
- **A lit stack never merges into a Merged Stack cell** (`BuildCellEntries` with
  `GroupDivergedStacks`): it always gets its own cell, so the lit idol is visible and directly
  clickable to turn off. An unlit divergent stack still groups as today.
- Phase 2 marks lit state on the inventory cell and hotbar slot, the tooltip and Item Details. The
  richer visual (the rotating inner glow in TODO.md's "Stances and Toggles") stays in that entry.

## Phases

Each phase ends with a manual in-game check before the next one starts.

1. **Toggle mechanics, Toxic Idol, input routing**:
   - Build: the two pool events, `EntityManager.IsDestroying`, `ToggleItemActivator`,
     `IReversibleEffectEntry`, the `AuraSourceGrant` change and `Toggle` deletion, the catalog
     validation, `ToggleItemActions`, `ItemHotkeyBindingActions.Repoint`,
     `ToggledItemHolderEffects`, `Tag.Toggle`, Toxic Idol's migration and shop/kit changes, the
     three input routes, "Turn on"/"Turn off", and the Merged Stack exception. Delete the TODO
     entry and remove the "costs a stack to toggle off" sentence from "FreeCast toggle-aura ability".
   - Check: Slot 6 turns the aura on and off without losing an idol or locking; while lit, give the
     idol to a corpse (no aura there), take it back (aura on you), sell it (the shop radiates), buy
     it back (still lit), stage it in a trade and cancel.
2. **Showing lit state**:
   - Build: the lit marker on inventory cells and hotbar slots, and an "Active" line in the tooltip
     and Item Details.
   - Check: visual, including a hotbar slot whose binding was repointed by toggling.
3. **Notes**: an `IMPLEMENTATION-NOTES.md` entry replacing "Toggle poison aura ability -- item side".

## Tests

- `ToggleItemActions.TryToggle`:
  - Turning on peels one unit into a lit divergent stack and leaves the rest plain; turning it off
    merges it back into the plain stack. No unit is ever consumed.
  - Two lit idols share one stack; turning one off leaves one lit.
  - The hotbar binding follows the toggled unit both ways.
  - Refused for a non-toggle item, a missing stack, or a dead entity, with no state change.
- `ToggledItemHolderEffects`:
  - Lit on the holder gives exactly one Poison source; off removes it.
  - Two lit idols, one turned off: the source stays on.
  - `TryTransferStack` of a lit stack: the source leaves the giver and arrives at the receiver
    (player to corpse, corpse to player, player to shop, shop to player), and the stack is still lit
    afterwards.
  - A dead receiver, and a trade-offer entity, get no source; transferring back out applies it to
    the next eligible holder.
  - Destroying a holder with two lit stacks leaves no aura source on the recycled id and publishes
    no `AuraSourceAddedEvent` during destruction.
  - Each case asserts `AuraGrid` too, not only the pool, since the grid is what the aura system and
    map tint read.
- `MultiComponentPool`: `ComponentAdded` fires on add only; `ComponentRemoving` fires for every
  removal path with the instance still readable.
- Catalog validation: registering a toggle item with an irreversible entry throws, and a mod doing
  so is dropped by its dry run.
- `AuraSourceGrant`: the permanent mode is idempotent (applied twice, one source) and `Revert`
  removes it. Update `AuraSourceGrantTests`' flip-mode cases.
- Presentation, driven through `UiInputController` and the real hotbar press path rather than direct
  calls: Slot 6 toggles without arming or closing windows, works while the action lock is up, and a
  lit stack keeps its own cell with `GroupDivergedStacks` on.
- `BlueprintTests` Toxic Idol assertions updated for quantity 1.
