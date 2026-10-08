### Combat Overhaul : Dodge

**Core mechanic landed** -- see `IMPLEMENTATION-NOTES.md`'s own "Combat Overhaul: Dodge" section for
what's built. Still open below: Block, Counterspell, the AdvancedDodge buff (Low Priority, this file),
and independently validating the 0.5s-1.0s window against Dark Souls 3/other games' dodge timings.

# Clean these up
Keep Movement, QuickAttack, and magic missle as immediate actions, Dodge as the only FreeCast action, and change all others to delayed. Immediate and FreeCast actions should be rare. 
All delayed actions keep the target shape drawn on the map (so enemies show their attack target area) until they activate. Adjust the colors to make it clear which are enemy vs player. Red for enemy target shapes that cannot be dodged and yellow for ones that can be dodged. Light green for player arm and dark green for player target.
While an entity is charging an action/item, put that action/item's sprite as a badge above their sprite on the map.
This allows FreeCast actions like dodge, block, parry, counterspell, etc to have a purpose and timing.
This makes combat slower and more deliberate instead of spamming actions. Shifting to more of a 2d souls-like game
The windup path now carries items as well as actions (`PendingWindupComponent` with `Kind` Item, started by `Windups.Begin`, resolved through `WindupResolvers`), so making a potion, scroll or wand Delayed is its timing plus a resolver that applies it: today's item resolver (`ToggleItemWindupResolver`) only lights toggle items, and `ItemActivationSystem` only winds up a toggle.
Lower enemy count to make this more deliberate and punishing combat style work.
Give every entity three default core actions. QuickAttack, PowerAttack, and Dodge.
	QuickAttack is an immediate adjacent-target action (so can't be dodged) with low damage. No cooldown besides global. This replaces Punch, defaulting to the R key.
	PowerAttack is a delayed adjacent-target action with a 1 second delay (so can be dodged) with high damage. No cooldown besides global. Default to the Q key.
	Dodge is a FreeCast action to avoid attacks. Its own flat 4 second cooldown (not an increased global cooldown) prevents Dodge from being used as a safer form of movement. Default to the F key. Not all attacks can be dodged; such as auto-targeting attacks like Magic Missile or AOE attacks like explosions. Mark PowerAttack as CanBeDodged (find a better name for this). After dodge is armed (adjacent+self), it can be activated with the same key, click, or directional movement key. Same key or clicking on the player will dodge in-place. Directional movement or clicking an adjacent tile will dodge while moving to that tile (if not occupied. If occupied, the entity will dodge in place). After activating Dodge, any action or item with CanBeDodged will not affect the dodging entity.
Dexterity increases dodge's activation time from 0.5 seconds at dexterity 1 to 1 second at dexterity 300. Check the values used by games like Dark Souls 3, and other games with a dodge mechanic, to validate these activation times.
Block (such as via a shield) will be added later as another FreeCast action with a longer duration but does not fully block damage and effects.
Counterspell will be added later as another FreeCast action that will attempt to cancel delayedAction spells. Determine a way to make this fair for both the source and target.

### Stances and Toggles
Stances: a set of toggle actions that boost one specialty while weakening another. 
Add a visual element to toggle actions/items to indicate when they're toggled on. Compare a rotating inner-fade glow to industry standard.
Stance 1 = Power Stance = Lower charge up times for delay actions in exchange for longer global cooldowns.
Stance 2 = Mage Stance = Improved magic effects at the cost of melee. 
Stances wait on a reversible `StatModifierGrant`: a toggle may only hold what it can take back when it is switched off (`ToggleContentValidation`), and a permanent stat modifier can't be yet.

# Long-Term TODOs

Non-urgent architectural items worth revisiting later. Organized by layer (Engine, Game, Presentation,
Global), each split High/Medium/Low priority. Landed work lives in `IMPLEMENTATION-NOTES.md`, not here
-- cross-referenced by name where a still-open item depends on it.

## Engine

### High Priority

#### Class exceptions: runtime class grants

NPC classes come from spawn rules and fit two fixed class slots. The player's don't: a first class on
floor 3 and a derivative subclass on floor 6, with exceptions --
- *Four Seasons* (Ice/Fire/Earth/Wind Mage): the floor-6 choice may be another element instead of a
  derivative, a third element on floor 9, the fourth automatically on floor 12. Combination spells
  depend on the **set** of classes held at the time, not the order.
- *Former Child Actress*: no permanent subclass on floor 6; a temporary subclass every floor. At the
  end of the floor a seeded roll keeps some of its effects permanently and removes the rest.
- *Oak Fell* (and similar): granted by an achievement, alongside or instead of a lootbox, in addition
  to existing classes.

`ClassSlotsComponent` (2 x `ushort`, permanent) and the sparse `ClassMembershipComponent` Multi pool
(class id, lifetime -- permanent or expires at the end of floor N -- source kind, acquisition order),
read only through `ClassQueries`, are in place and unit-tested. `EntityFactory.Apply(entity, classBlueprint,
grantedBy)` is the runtime grant path: it builds the class blueprint onto a live entity, fills a
slot or adds a membership (`ClassEffects.Grant`) and skips a class already held; the class's actions
then resolve through `EntityActions` and its name follows the entity's (`EntityNaming`). Admin Mode's
"Apply >" menu drives it for testing. Nothing in gameplay calls it yet. Class
content is either **derived** from current membership (shared, never stored per entity; removing a
membership removes it) or an **explicit** per-instance grant carrying a class-grant `ActionSource` and
a lifetime.

What is left, once something grants a class at runtime: the class-grant `ActionSource` kind, the
advancement-rule hook on class definitions, and a combination-rule registry mapping a required class
**set** to derived grants, evaluated on membership change. Former Child Actress's floor end: roll each
effect, turn survivors into explicit permanent grants whose source names the residue, remove the
membership. Dependencies, out of scope here: a floor-end event (see EndOfLevelStairs),
class-selection UI, the advancement rules themselves, the residue roll. Ends with an in-game test and
a `phase-performance-testing` A/B.

### Medium Priority

#### FrameEventBuffer double-buffering + event system cleanup

`FrameEventBuffer<T>` (`Engine/ECS/Systems/`) throws on a second same-cycle `Record` (a safety net, not
a fix). Real fix: double-buffer (swap, not clear) like Bevy's `Events<T>` -- trades same-cycle
visibility for 1-frame latency (check this is OK for `MovementSystem`'s `TerrainContactSystem`/
`AuraSystem` consumers). Do alongside reviewing `EventBus`'s `IBufferedEvent`/
`SubscribeOnce`/`DispatchBuffered` (`Engine/Events/`) for consistency -- a different mechanism (deferred
re-entrant handler vs. high-frequency batching), never compared side by side.

**Bevy reference:** Bevy 0.17 split the two mechanisms by name. Buffered, double-buffered events are
now *Messages* (`MessageWriter<T>`/`MessageReader<T>`): each reader keeps its own cursor, a message
lives for two frame updates so a reader running earlier in the next frame still sees it, and nothing
is ever dropped for a reader that runs every frame. Immediate, handler-style events are *Events*,
delivered to observers when triggered (see "Component lifecycle hooks and observers", above). That
split is a reasonable target for `FrameEventBuffer` vs `EventBus` here. Pairs with "Deferred
structural changes" (above), which removes the other reason for same-cycle recording hazards.

#### Console variables and console commands

Settings come only from the command line (`CommandLineSettingsSource`, the one `ISettingsSource`), are
read once at startup, and can't be changed while the game runs. Admin/debug tools are the command sets in
`Game.Admin.AdminTools`, offered only through the map's context menu (`AdminContextMenuOptions`). Add:
- **Console variables:** a `SettingDefinition` that opts in can be read and changed at runtime, with a
  change notification for the code that caches it. Every change records who set it, so a lower-priority
  source never overwrites a higher one (a config file never overwrites something typed at the console).
- **Console commands:** named commands with arguments and help text, registered by modules the way
  settings are (`SettingsDeclarations`), so a mod can add its own. Admin Mode's tools ("Spawn here",
  "Apply", "Teleport here", "Regenerate") become commands, with the context menu one way to call them.
- **A console window:** a text box with history and completion that runs commands and sets variables,
  available only in Admin Mode (F12).
- **Command-line parity:** `--exec="cmd; cmd"` runs commands at startup, so a benchmark or bug
  reproduction can set up its scenario without new flags.

**Unreal Engine reference:** `IConsoleManager` owns every console variable (`TAutoConsoleVariable<T>`,
declared next to the code that reads it, with help text) and command (`FAutoConsoleCommand`,
`UFUNCTION(Exec)` on a `UCheatManager`). Each variable tracks the priority of whoever last set it
(`ECVF_SetByConstructor` < `SetByScalability` < `SetByGameSetting` < `SetByProjectSetting` <
`SetByDeviceProfile` < `SetByCommandline` < `SetByCode` < `SetByConsole`), and a lower-priority write is
ignored. Variables can be set from `.ini` sections (`[ConsoleVariables]`, `[SystemSettings]`), the command
line (`-ExecCmds=`) or the `~` console. `ECVF_Cheat` variables and the whole Cheat Manager are compiled
out of shipping builds; the equivalent here is gating on Admin Mode. `FConsoleVariableDelegate` is the
change callback.

**Bevy reference:** Bevy has no console in core (`bevy-console` is third-party), but its one-shot
systems are a good shape for commands: `world.register_system(spawn_here)` returns a `SystemId`, and
`commands.run_system_with(id, input)` runs it on demand with the same access to the world as any
system. A console command would be a registered one-shot system plus parsed arguments, so commands
and systems share one way of reaching pools.

#### Relationships -- links between entities that maintain both sides

Every link from one entity to another is a one-way `EntityKey` in a component (`ActionSource`,
`DeadComponent.KilledBy`, aura sources), and nothing can ask the other direction ("which auras is this
entity the source of", "what's in this container") without a scan or a hand-kept index. Cleanup is by
hand too: `GameBootstrapper` clears aura sources, map footprint and tier membership at
`EntityDestroying`, and each new link needs its own line there. Planned features add many links:
equipment and its wearer, container contents, companions and their leader, an aura anchor and its
owner (`AuraAnchors`, already wired), claimed spots ("Spatial queries for NPC decisions", Game), an NPC's current
target, a pet's bonded player ("Entity storage", Global).
- **A relationship is a pair of component types:** the source side (on the item: "equipped by X") is
  the one code writes; the target side (on the wearer: "items equipped") is maintained by the engine
  whenever the source is added, changed or removed. Code never writes the target side.
- **Cleanup policy per relationship:** when the target is destroyed, either remove the source side
  from every linked entity (an aura's source dies, the aura stays), or destroy the linked entities too
  (a container's contents go with it). Declared once on the relationship.
- **Storage:** the target side is a Multi pool (many sources per target), so it costs nothing on
  entities with no links.
- **Across frames and saves** the source side holds an `EntityKey`, as today. Saving and loading remap
  both sides ("Save and load Beyond neighborhoods", Global).
- **Skeletons:** a link to a skeleton is allowed; building it doesn't touch its links.

**Bevy reference:** Relationships (0.16). A component marked `#[relationship(relationship_target =
Children)]` (the built-in `ChildOf`) automatically keeps the matching `#[relationship_target]` component
(`Children`) on the target up to date, through component hooks. `linked_spawn` on the target side makes
despawning the target despawn everything related to it. Custom relationships use the same attributes,
so equipment or containers would be two small components. Bevy allows only one target per source
component (an entity is `ChildOf` exactly one parent); a many-to-many link is several relationship
types or an intermediate entity.

#### Component lifecycle hooks and observers

Code that must react when a component is added or removed on any entity has two routes today: pool
`ComponentChanged` events (Packed and Multi pools only, change-shaped, subscribed by timer wheels and
stripe sets) and `EntityManager.EntityDestroying` (every entity, whatever it holds). State kept outside
the pools about an entity (map footprint, aura sources, UI selections, tier membership) has to remember
to let go at `EntityDestroying`, and CLAUDE.md carries that as a rule because it's easy to miss.
- **Hooks per component type:** on add, on insert (add or replace), on replace (before the old value
  goes), on remove, and on destroy, registered with the pool. Cleanup lives next to the component:
  the map footprint clears in `TransformComponent`'s remove hook, whoever removes it and however the
  entity is destroyed. The `EntityDestroying` handlers in `GameBootstrapper` and `ShellBootstrapper`
  move into hooks.
- **Observers:** handlers subscribed to lifecycle events of a type (every entity) or of one entity
  (this corpse's contents change, update the open loot window). Presentation subscribes to
  observers rather than Game pushing to it.
- **Order and cost:** hooks run synchronously during the write, so they must be cheap and not make
  structural changes directly (see "Deferred structural changes", below). A pool with no hooks pays
  one null check.
- **Components that can only be replaced, not edited in place:** a component marked this way can only
  change through a whole-value write, so its insert/replace hooks see every change. Candidates:
  `ProcessingTierComponent`, occupancy markers, spawn record.
- **Required components:** a component can declare others that must exist alongside it (with default
  values); adding it adds them. This catches pairs that must always come together, which blueprints
  and the skeleton list handle by convention today.

**Bevy reference:** component hooks (`#[component(on_add = ..., on_insert = ..., on_replace = ...,
on_remove = ..., on_despawn = ...)]` or `register_component_hooks`) run synchronously for every
entity with the component, and receive a `DeferredWorld` that can read and queue commands but not make
structural changes directly. Observers (`app.add_observer(|event: On<Add, Burning>| ...)`, or
`commands.entity(e).observe(...)` for one entity) are the subscribable version of the same lifecycle
events, and also handle custom events, with optional propagation up a relationship (a click bubbling
from a child to its parent). `#[component(immutable)]` (0.16) makes a component replace-only, so hooks
see every change. `#[require(B)]` (0.15) inserts `B` with its default whenever `A` is added.

#### Change detection

Knowing that a component changed today means subscribing to a pool's `ComponentChanged` event, which
fires on every write, costs a delegate call per write per subscriber, and exists only on Packed and
Multi pools. Things that only need "did it change since I last looked" pay for more than they use or
compare values by hand: `HealthWindow` and HUD refresh, Presentation caches (`MapTileLayerCache`),
and perception that could skip recomputing when nothing nearby moved.
- **Per-component change ticks:** each pool stores, per entity, the frame the component was added and
  the frame it last changed. Every write through the pool's update path stamps it; no subscriber
  exists.
- **Queries:** "added since frame N", "changed since frame N", and a reader for "removed since frame
  N" (a per-pool list of removals, kept for a frame or two). A system or window remembers the frame
  it last ran and asks about everything since.
- **Write only when different:** an update helper that compares and skips the write (and the stamp)
  when the value didn't change, so a system that rewrites the same value every visit doesn't count as
  a change.
- **Cost:** one `uint` per entity per pool that opts in, written on every write. Measure it on the
  hottest pools with the `phase-performance-testing` skill before turning it on everywhere; opt-in
  per pool is the fallback.
- Events stay the tool for "something happened" (damage, death), and for the timer wheels, which need
  every write.

**Bevy reference:** every component stores `added` and `changed` ticks. `Mut<T>` marks a component
changed when it's dereferenced mutably, `Ref<T>::is_added`/`is_changed` and the `Added<T>`/`Changed<T>`
query filters compare against the tick the system last ran at, and `RemovedComponents<T>` reads a
per-type buffer of removals. `set_if_neq` writes (and marks) only when the value differs. Bevy pays the
tick cost on every component by default and considers it cheap relative to the systems it lets skip
work.

#### Named schedules, system sets and run conditions

System order is the module order (`RunsAfter`/`RunsBefore`) plus registration order within a module,
with `SystemManager.RegisterFirst` for the systems that must lead the frame (`NeighborhoodStreamer`,
`SpawnMoves`). `BuiltInModulesTests` pins the resulting order. Whether a system runs at all is decided
inside it (pause is a `GameLoop` skip of the whole ECS; tier gates are in `TieredSystemRunner`). Two
things are hard to express: ordering a single system against another module's system, and "this
system doesn't run in this state" (menus, game over, a frozen map).
- **Named phases** in a fixed order (for example: frame start, input, streaming, decisions, actions,
  movement, resolution, cleanup). A system declares its phase, and a mod adds systems into a phase
  without caring about module order. `RegisterFirst` becomes the frame-start phase.
- **System sets:** a named group of systems (every damage-over-time system, every NPC decision system)
  that other systems or sets order against, so a mod's DoT runs with the built-in DoTs without naming
  each one.
- **Per-system ordering:** `before`/`after` against systems and sets, on top of module order,
  checked for cycles the same way `RunsAfter`/`RunsBefore` already are.
- **Run conditions:** a declared predicate on a system or set ("in state Playing", "session not
  paused", "any events of type X this frame") evaluated by `SystemManager` before running it. A
  condition on a set is checked once for the whole set.
- Module order stays for what it's for (registering components and configuring); phases and sets
  decide system order.

**Bevy reference:** the main schedule runs `First`, `PreUpdate`, `StateTransition`, `FixedUpdate` (as
many times as the fixed clock needs), `Update`, `PostUpdate`, `Last`, and games add their own schedules.
Systems join sets (`.in_set(DamageSet)`), sets are ordered with `configure_sets(...).chain()`, and any
system or set can be ordered `.before()`/`.after()` another. `.run_if(condition)` takes a system that
returns `bool` (`in_state(GameState::Playing)`, `on_message::<T>`, `resource_changed::<R>`); conditions
on a set are evaluated once per frame for the set. Plugins add systems into the app's schedules
without knowing about each other, which is the property wanted for mods here.

#### Deferred structural changes

Spawning, destroying and adding or removing components happen immediately, wherever they're called
from, and each hazard this causes is handled separately: `SpawnMoves` defers a spawn's move to "this
frame if the moves are still unread, otherwise next frame", `FrameEventBuffer` throws on a second
same-cycle `Record`, `NeighborhoodStreamer` orders evictions ahead of promotions, and a system
iterating a stripe set has to be careful about entities it destroys mid-iteration.
- **A command queue:** structural changes requested during a system go into a queue and are applied
  at defined points (between phases, or at the end of the system), in the order requested. Plain
  value writes to existing components stay immediate.
- **Ids immediately:** spawning through the queue reserves the entity id at once, so the caller can
  refer to the new entity (write further components into the queue for it) before it exists.
- **Deterministic:** application order is request order, so seeds and benchmarks still reproduce.
- **Outside systems** (Presentation between frames, bootstrapping) changes can still apply directly,
  or queue and apply at frame start.
- Needed before "Parallel system execution" (Low): parallel systems can't all make structural changes
  directly. Pairs with "FrameEventBuffer double-buffering" (above).

**Bevy reference:** `Commands` queue spawns, despawns, inserts and removes, applied at `ApplyDeferred`
sync points that the scheduler inserts automatically between a system that queues commands and any
system ordered after it. `commands.spawn(...)` reserves the `Entity` immediately (`reserve_entity`).
Exclusive systems (`&mut World`) are the escape hatch that applies changes directly.
`DeferredWorld` (what hooks get) can queue commands but not change structure.

### Low Priority

#### Partial module replacement

A mod that replaces a built-in module by `Id` currently replaces all of it and must register every
component the built-in did (the replacement contract, see CLAUDE.md's Modding section). Let a mod
replace only the systems, only the components, or both. Whatever it doesn't replace comes from the
original module. For example, a mod could swap `SimpleHealthRegenSystem` for its own regen rule and
keep `HealthModule`'s pools and every other system. Open questions: how finely systems can be
replaced (per system or the whole set), how a replaced system keeps its place in the run order, and
how a component replacement with a different merge action is checked against what depends on it.

**Bevy reference:** a `PluginGroup` (`DefaultPlugins`) is built by a `PluginGroupBuilder` that can
replace one plugin (`.set(WindowPlugin { ... })`), remove one (`.disable::<LogPlugin>()`) or insert
one before or after another (`add_before`/`add_after`). That's replacement at the plugin level only,
the same granularity as today. Finer replacement in Bevy comes from system sets: a plugin puts its
systems in a named set, and another plugin can order against, or add a run condition to, that set
(see "Named schedules, system sets and run conditions", above). A set that a run condition turns off
while a replacement system runs in the same slot is one way to replace a single system.

#### Reload mods without restarting

`ModuleLoader` already loads each mod DLL into its own collectible `AssemblyLoadContext`, so unloading
is possible, but nothing uses it: a changed mod means restarting the game. Let Admin Mode reload one
mod: end the session (or return to a start menu once one exists), unload the mod's context, reload the
DLL, and rebuild the session through `GameBootstrapper.Build`. Rebuilding the session avoids carrying
live entities across a changed component layout; reloading inside a running session is out of scope.
The collectible context only unloads if nothing still references the mod's types: event subscriptions,
static caches, registered blueprints and `SettingsCatalog` entries all have to go when the session
does. `WeakReference` on the context is how to test that it actually unloads.

**Unreal Engine reference:** Unreal plugins are modules with an explicit lifecycle
(`IModuleInterface::StartupModule` / `ShutdownModule`) that must undo everything they registered.
Live Coding patches changed functions into the running process, but it can't change class layouts
(new members need a restart), and the older Hot Reload, which swapped whole DLLs, was deprecated
because leftover references to old types corrupted state. That's the case for reloading at a session
boundary rather than in place. A `Shutdown`/unregister hook on `IModule`, mirroring Unreal's
`ShutdownModule`, would make "a mod releases what it registered" explicit.

**Bevy reference:** Bevy doesn't reload code either; plugins are compiled into the app, and
`bevy_dynamic_plugin` (loading plugins from dynamic libraries) was deprecated and removed. Its hot
reloading is for assets (see "Data tables for content", Game) and, experimentally (0.17's `hotpatching`
feature, built on Dioxus's `subsecond`), for changed system function bodies -- not for new components
or changed layouts. Both point the same way as Unreal: reload data
freely, reload code only at a boundary.

#### Parallel system execution

`SystemManager.Update` runs every system one after another on the main thread; only neighborhood
planning uses workers. Systems that touch different pools (regen and NPC decisions, say) could run at
the same time, and a heavy per-entity loop could split across cores. Only worth it if a profile shows
the simulation, not rendering or GC, is what's over budget.
- **Declared access:** each system declares which pools it reads and which it writes. The scheduler
  runs two systems at once only if neither writes what the other touches, and keeps the declared order
  otherwise.
- **Ambiguity detection:** report pairs of systems with conflicting access and no defined order. Even
  single-threaded that's useful: such pairs are where a harmless-looking registration change alters
  results.
- **Parallel loops inside a system:** split a stripe's entity list across workers when per-entity
  work only writes that entity's own components.
- **Determinism:** results must not depend on thread timing, so seeds and benchmarks still reproduce.
  Parallel loops can't draw from a shared random sequence (per-entity reseeding already solves this
  for builds) or append to shared lists in completion order.
- **Prerequisites:** "Deferred structural changes" (Medium, above), because systems running at once
  can't spawn or destroy directly; event publishing that is safe from several threads; and the Debug
  `SkeletonAccessGuard` becoming thread-safe.

**Bevy reference:** the `MultiThreadedExecutor` reads each system's parameters (`Query<&A, &mut B>`,
`Res<R>`, `ResMut<R>`) to know its access and runs non-conflicting systems in parallel on the
`ComputeTaskPool`; exclusive systems (`&mut World`) run alone. `ScheduleBuildSettings {
ambiguity_detection: LogLevel::Warn }` reports conflicting unordered systems. `query.par_iter_mut()`
splits one query across threads in batches. Bevy's results can still depend on the order of
ambiguous systems, which is why the ambiguity report matters as much as the speedup.

#### Content-only mods -- overriding files by path

Mods are DLLs only: `ModuleLoader` looks for `*.dll` and constructs `IModule` types. Replacing a
sprite, a sound or (once "Data tables for content", Game, exists) a table row means writing and
compiling a C# project, which excludes most would-be modders, and there's no way to ship art alone.
- **A mod folder can hold content:** files under a mod's `Content/` override the game's file at the
  same relative path (`Spritesheets/Wall_Tiles.png`, `SpriteManifest.json`, a loot table), in mod load
  order, the last mod winning. A mod may contain content, code or both.
- **Merging for structured files:** a whole-file override is right for images and sounds; for
  manifests and tables, a mod should add or replace entries by key rather than replace the file, so
  two mods adding sprites don't erase each other (the same rule as the Data tables item).
- **One lookup:** every content read goes through a single path resolver that knows the mod stack, so
  `SpriteSheetService`, fonts, audio and tables don't each learn about mods.
- **Reported like code mods:** a content file that fails to load is a `ModuleFailure`-style entry with
  the mod's name, and the game falls back to the base file.
- Pairs with "Reload mods without restarting" (above): content can reload in place, since it has no
  type identity to leak.

**Godot reference:** resource packs. `ProjectSettings.load_resource_pack("mod.pck", replace_files =
true)` mounts a `.pck` or `.zip` into the virtual `res://` file system, and its files replace the
game's at the same paths for every later load. That's the standard way Godot games take mods, and it
works for any file type because everything loads through `ResourceLoader` by path. Its weakness is the
one noted above: two packs that both replace the same file conflict wholesale, so games with many mods
add their own merge layer for data files.

#### Equipment (Engine)

Slot/equip-unequip mechanics -- move an `InventoryItemStackComponent` stack into a slot, no new storage
primitive. Companion to the Game/Presentation equipment items below.

## Game

### High Priority

#### Attribute effects to the action or item that applied them

An effect from an action or item is credited to the entity that used it and nothing else
(`ActionSource.FromEntity`). So a Draught of Thrift's buff shows "Player1 (crawler #)" as its source
in the Health window's hover popup, and a poison from Toxic Strike reads the same as one from a
Toxic Potion. Name the action or item as well: "Draught of Thrift (Player1)".
- **Every recorded source:** stat modifiers, status effect timers (poison, burning, body-part
  burning), immunities, `DeadComponent.KilledBy` and the floating-text/activity log lines read the same
  `ActionSource`, so all of them gain it at once.
- **Storage stays 8 bytes** (`ActionSource`'s remarks: a 16-byte source made BurningSystem ~50%
  slower). The entity's 24-bit detail is already an interned handle (`EntityIdentities`), so intern
  (identity, cause) pairs instead of identities alone, rather than widening the struct. Interning per
  use would be per application -- intern per (entity, action or item) once, on first use.
- **Where it is set:** whoever builds the `EffectContext` for an action or item activation
  (`ActionEffectResolver`, the item activators, `Toggles` for held and periodic effects) knows the
  definition; terrain, auras and admin keep their own kinds. An aura an anchor places credits its
  placer today (`AuraSystem.AttributionOf`); it could name the action that placed it the same way.
- **Display:** `ActionSourceNaming.Describe` prints "{cause} ({entity})" for an entity source with a
  cause, the entity alone without one.

#### AOE vs targeted melee attacks (experimental)

Every melee attack today is an Adjacent ring (`QuickAttackAction`, `PowerAttackAction`): it hits
everyone around the attacker, and an NPC queues the whole ring (`TestCombatBehaviorSystem`). Try
splitting melee into two kinds and see whether it plays better:
- **Targeted melee:** one adjacent tile, chosen like Dodge's (`TargetShape.SingleTarget`, Range 1,
  `DistanceMetric.Chebyshev`) -- a stab or a jab that only hits who it's aimed at.
- **AOE melee:** the Adjacent ring as now, or a Cone -- a sweep or a cleave, usually slower or
  weaker per target.
- Melee stays Ground only (`TargetingModes.GroundOnly`, enforced by `TargetingContentValidation`):
  a targeted melee aims at a tile, not an entity that could step away.
- Which of QuickAttack/PowerAttack becomes which, and whether NPCs pick the target tile (the
  adjacent hostile) or keep swinging the ring.
Separate from the rest of the melee work because it is an experiment: if it doesn't feel better in
play, revert to the all-ring melee.

#### ProcessingTierResolver.PromotionsHeld set after the session is assembled

`ProcessingTierResolver.PromotionsHeld` is a settable `Func<bool>?`. `WorldSessionBootstrapper` sets
it to `() => neighborhoodStreamer.IsEvictingBuiltCreatures` once the streamer exists, and
`ProcessingTierSystem` invokes it every frame. Null means "never held", a silent fallback: a session
assembled without it lets promotions build creatures while evictions are still freeing storage,
which is the pool growth the hold exists to prevent (IMPLEMENTATION-NOTES "Evictions before
promotions").

Cause: the streamer is built in the exe after population, and it needs the resolver, so the resolver
can't take the streamer in its constructor.

Direction:
- Build `NeighborhoodStreamer` inside the build pass, not the exe. After register-first, everything
  it needs (World, pools, EventBus, resolver, factory, terrain, definitions, a neighborhood-record
  source) exists by `RegisterBehavior`. It stays first in the frame, and
  `BuiltInSystems_RunInThePinnedOrder` pins it.
- Break the cycle with state that has one owner. Either the streamer pushes "evicting built
  creatures" into a small non-null gate created with the context and read by
  `ProcessingTierSystem`, or `ProcessingTierSystem` takes the streamer directly. No settable
  delegate, and no null default.
- Keep new-game and load separate: whatever the streamer needs per session (records, seed,
  window centre) arrives as session input, not by setting properties afterwards.

#### Paralysis V2 -- body-part-scoped status effects

Paralysis can be applied to an entire entity or to individual body parts. A paralyzed body part
behaves exactly as if that part were disabled (`BodyPartStateComponent.IsDisabled`), so every existing
consequence -- `BodyPartEffectsSystem`'s movement and melee penalties included -- applies with no
paralysis-specific rules.

Burning is the only body-part-scoped effect today, and its per-part path is Burning-specific end to
end (`BodyPartBurningTimerComponent`, `BodyPartBurningSystem`, `BurningApplier`'s part path,
`HealthWindow.BuildBurningPartIds`). Refactor it into a generic body-part-scoped status effect that
any effect plugs into, with Paralysis as the second concrete implementation.

Poison follows as the third, restricted to the `Internal` body part -- or, for an entity
with simple health, the entire entity.

#### Merging body plans when an entity gains a second race

Bug: `EntityBodyParts.Update` seeds a body plan (`CreateState`) only when the entity has no
`BodyPartStateComponent` yet. Once one exists it bounds-checks `partId` against the fixed
`BodyPartStateComponent.MaximumParts` (16), not the entity's current `Count`. If a damaged entity
then gains a race (`EntityFactory.Apply`, e.g. Admin Mode "Apply > Human"; `RaceSlotsComponent`
merges it into the next empty slot), the new race's parts are never seeded: they read as 0 health,
and Damage/Heal writes to them go through without any error. Two 11-part races also give 22 parts,
more than `MaximumParts` allows, so `CreateState` throws for an undamaged hybrid and any damaged
entity's parts past index 16 can't be written at all.

A bounds-check fix alone isn't enough. The real gap is that a second race's body plan is just
concatenated onto the first (`TemplatesOf` returns first then second, and a part's id is its index
in that list). Plan the merge before fixing it:

- **Overlapping plans.** Human + Goblin both have a Head, Torso, Arms, Legs and so on. Decide whether
  a hybrid gets one of each overlapping part (whose template wins -- the first race, like appearance?
  the larger maximum health?) or both. A second Head or a third and fourth Arm has to mean something
  to `BodyPartEffectsSystem`'s movement/melee penalties and to whether an entity dies from a vital
  part.
- **Different plans.** Parts only one race has (wings, a tail) get added. Whatever the final count
  is, it must fit `MaximumParts`, or `MaximumParts` needs revisiting against the component size
  (see "Component size audit").
- **Position.** `VerticalPosition` only means something relative to the same race's other parts.
  Concatenating two races mixes two scales, so `BodyPartSelection.PickTopmost/PickBottommost` (and
  anything else using `BodyPartTargetRule`) can pick the wrong part. The merge has to produce one
  consistent ordering, e.g. positions normalized per race, or merged parts placed relative to the
  part they attach to.
- **Part ids.** Ids are indices stored in `BodyPartBurningTimerComponent` and anything else that
  names a part. Gaining a race on a live entity must not shift an existing part's id, or must remap
  every holder.
- **Live state.** Gaining a race while the entity has a state component must seed the added parts at
  full health and keep existing parts' damage, disabled flags and regen lockouts. Losing a race (if
  that ever becomes possible) needs the reverse.
- The merged plan belongs in `BlueprintRegistry.Resolve` / `ResolvedBlueprint` (cached, spawn record +
  applied list), not re-derived per read in `EntityBodyParts.TemplatesOf`.

Cover it with a test: damage a Goblin, apply Human, then check every part's health, the part count,
the topmost/bottommost picks, and that a burning part keeps its id.

#### Real map generation -- neighborhood templates, replacing TestMapBuilder

The sliding window of 1024x1024 neighborhoods is in place: `NeighborhoodStreamer` generates each neighborhood on demand from its own
`NeighborhoodRecord` seed through `TestMapBuilder.GenerateNeighborhood`, which is still a stand-in --
random terrain and flat per-tile population rolls, with the hallway cross, shops and spawn only in
neighborhood (0, 0). Replace it with a generator where each neighborhood is built from a random
combination of four templates:

- **Layout template:** walls, corridors, terrain palette, landmarks.
- **MOB population template:** races and classes, placement rules, and a per-neighborhood population
  budget (replacing the flat 3%/2%/2% rolls that make creature count scale with area).
- **Neighborhood interaction template:** what joins adjacent neighborhoods (aligned corridors,
  faction borders, shared features, raids). Decided per pair of neighbors. Runtime behavior is the
  low-priority "Neighborhood interactions -- Outbound and Inbound" below.
- **Quest line template:** quest hooks placed into the chosen layout and population, possibly
  spanning neighborhoods.

Neighborhood placement is **randomized**: a given kind of neighborhood won't always appear at the
same coordinates, so relationships between neighbors must be generated dynamically rather than read
from a fixed world layout. Population density is defined per population template (dense goblin
warrens, sparse ones elsewhere).

Starting points from the plan's research (section 6): keep a rolling plan one ring outside the loaded
window, and roll each neighborhood's record (template choices plus its own seed) against its
already-assigned neighbors when the window shifts. By the time a neighborhood's tiles are generated,
every edge it shares is decided (the rolling equivalent of CDDA's overmap or Qud's world map).
Interaction templates should be constraints on a single shared edge, since a record is rolled with
only some of its neighbors known. Generation reads only the record's own seed, never a shared
sequence, so contents don't depend on generation order or thread timing. Spelunky's role-based room
templates are a model for composing authored pieces randomly. The record, per-record seeding and
planning a neighborhood as data on a worker already exist (`NeighborhoodRecords`,
`TestMapBuilder.Plan` producing a `NeighborhoodPlan`, which the streamer applies under its per-frame
budget); the new generator produces the same plan. Subsumes the seed
plumbing in "Random map generation v1" below. Needs its own plan.

**Spawn order decides entity ids, and ids decide what the pools cost.** Packed and Multi pools index
entities through pages of 1024 ids, allocated on first write, so a component only some entities hold
costs a page for every id range one of its holders falls in. `TestMapBuilder` spawns a neighborhood
row by row, which interleaves every kind of entity through the neighborhood's whole id range. Random
Healing Shrines spawned that way touched every page of four pools: 31 MB of pools for 9,000 shrines,
and the aura system 18% slower. Spawning each neighborhood's shrines first, in batches, so their ids
are consecutive, brought that to 7.5 MB and 1.6% (`IMPLEMENTATION-NOTES.md`, "Auras, terrain contact, Healing Shrine, Holy Ground").

The generator should apply the same rule to everything it spawns: order a neighborhood's spawn list
so entities that hold the same components get consecutive ids -- grouped by blueprint, or by what the
blueprint builds (always-built props, then creatures, each race together), rather than by where they
stand. What that has to keep:
- **Placement priority.** Spawn order is also who gets a contested cell: an entity that doesn't fit
  where it was rolled is destroyed. Grouping changes which one loses, so the order of groups is a
  rule of the generator (fixtures and props before creatures, large footprints before small).
- **Spreading over frames.** The streamer applies a plan a batch at a time under its budget. Groups
  need the same batching the shrines got (a few dozen spawns a batch), not one batch per group.
- **Determinism.** The order must come from the plan alone, as now.
- **What it doesn't fix.** Skeletons hold only skeleton components, so this matters for what is built
  at spawn and for what a build adds later: creatures built on promotion keep the ids they were
  spawned with, so their built-only pools page by spawn order too. Measure with the memory report's
  per-pool MB against holder count; a pool far above (holders x component size) is paging.

**Unreal Engine reference:** two Unreal features cover this ground.
- *PCG framework:* generation is a graph of nodes (sample points, filter, pick from weighted lists,
  spawn), seeded per component so the same seed always gives the same result. At runtime, "runtime
  generation" builds cells on a partitioned grid around generation sources (the player) within a radius
  and discards them when they leave -- the same shape as `NeighborhoodStreamer`, and it also runs
  generation off the game thread. The ideas worth taking are hierarchical grid sizes (big features
  decided on a coarse grid, detail on a fine one, like templates first and then tiles) and a seed per
  cell, never a shared sequence. That's already the rule here.
- *Level Instances / Packed Level Actors:* an authored chunk of level (a room, a shop, a goblin camp) is
  saved once and placed many times as a unit. That's the authored-piece half of the Spelunky model:
  layout templates built from authored room chunks, stored as data (see "Data tables for content",
  Game), with the generator choosing and placing them.
- Unreal also ships an experimental Wave Function Collapse plugin, another way to fill a layout template
  from authored tile adjacency rules.

**Bevy reference:** Bevy's scenes (`DynamicScene`, saved as `.scn.ron`) are its prefabs: a set of
entities with their reflected components, spawned into the world as a unit, with entity references
inside the scene remapped to the new ids. An authored room or camp as a scene-like data file (tiles
plus spawn requests by blueprint Guid) is the same idea as Level Instances. Bevy has no procedural
generation framework in core.

**Godot reference:** Godot has no generation framework either, but ships `FastNoiseLite` (Simplex,
Perlin, cellular/Worley, value noise, fractal layering, domain warping), which is the usual building
block for cave shapes, terrain patches (lava pools, moss) and population density maps. Nothing
provides noise here yet; a small seeded noise type in `Engine.Math`, sampled from the neighborhood's
own seed so generation stays order-independent, is enough. Godot's terrain autotiling ("Autotiling
with terrain sets", Presentation) is the other half: the generator decides *what* each cell is, and
autotiling decides which edge or corner art it shows.

#### Simplify non-local combat for performance

A Delayed action's windup/telegraph/badge exist for player-visible tactical value -- an off-screen
entity's own windup buys it nothing (nothing is watching it) but still costs the same
`ActionLockComponent`/`PendingDelayedActionComponent`/`DelayedActionSystem` bookkeeping as an
on-screen one. Confirmed expensive at this game's real population scale: a live diagnostics capture
showed over 10,000 entities map-wide simultaneously mid-windup during ordinary NPC-vs-NPC combat
(`DelayedActionSystem` alone cost ~79ms of a 1000ms/sec budget before it was tiered -- see
`IMPLEMENTATION-NOTES.md`'s "Enemy Attack Indicator + follow-up combat/performance work" for that
fix, already landed; tiering cut the per-frame *visit* cost, this entry is about shrinking the
*population* that ever needs a windup in the first place).

For an entity outside Local processing tier (`ProcessingTierLevel.Local` -- `ActionTargetingController
.AllPendingDelayedActionTargets` already treats this as the visibility boundary for its own
telegraph), resolve its actions immediately instead of queuing a Delayed windup at all: skip
`ActionLockGate.Lock`/`PendingDelayedActionComponent` and call `ActionEffectResolver.Apply` right
away, the same as an `Immediate` action. Shrinks `DelayedActionSystem`'s/`ActionLockSystem`'s own
per-frame population directly, without touching on-screen combat's telegraph/timing at all. Open
question: check tier at decision time (`TestCombatBehaviorSystem.TryDecideMeleeAttack`, cheaper --
never queues a windup for an already-non-Local entity) vs. at activation time
(`ActionActivationSystem.TryActivateDelayed`, also covers an entity that starts Local and drifts out
mid-windup). Whether Dodge/Block eligibility should get the same non-Local-entities-skip-it
treatment is a related, separate question worth deciding alongside this.

#### Inventory management rules

Interaction/restriction rules: stacking beyond exact-`ItemDefinition` match, who can pick up what,
item-item interactions. Storage + exact-match merging already exist.

#### Consumable items

Needs the item-instance-divergence design (a consumable's remaining-uses count is per-slot state that
doesn't exist yet) plus an actual "use" action.

#### Torch reveal + light-weakness damage, Scroll Mastery power-scaling

- Scroll of Torch's light aura (`ScrollOfTorch.Aura`) is glow-only -- its `AuraDefinition` has no effects.
  Needs a real effect (fog-of-war reveal, light-weakness damage). Also worth
  reconsidering once fog of war lands: a Torch now lights the entity it is read at, or anchors on the
  tile read at (`AuraAnchors`). The per-tile light level in "Field of view
  and perception" (High, above) is where the reveal belongs: a Torch writes light into tiles, fog of war
  and NPC sight read it, and light-weakness damage checks the light level on the victim's tile.
- `ScrollMasteryEffects.MasteryThreshold` (flat 200) and a synthesized spell's placeholder `ManaCost: 0`
  should scale with the effect's power. Blocked on Action Effects gaining a power-scaling concept.

#### Experience module

XP for kills (`EntityDiedEvent`) and quests (blocked on quest completion existing as a mechanic).
Level-up grants stat boosts/abilities per class. Needs a current/next-threshold Experience component +
HUD bar (candidate for the shared tick-fraction HUD bar item under Presentation). Check whether its
level-up curve can share math with Skills/Spell leveling below rather than three independent copies.
Show XP gained (and Level Up) as floating text above the player -- new `FloatingTextKind` values on the
Floating Text feature (`IMPLEMENTATION-NOTES.md`, "Floating Text"), with the notification carrying the full details.

#### Skills

Static bonuses via `StatModifierComponent` + new effects on top of an action -- the motivating case for
the DamageOverride generalization above. Level 0-15 normally, unlockable to 20, never decreases, up to
hundreds per player. Needs a `MultiComponentPool<SkillComponent>`-shaped store (per-entity/per-skill,
not single-instance). See Spell leveling below for the same shape at smaller scale -- share one leveling
primitive. Consumer: Player selection menu's skill-gated detail (Presentation, below).

#### Complex health for every race, or none -- decide before body parts spread further

Goblin and Human (so the player) grant `BodyPartComponent`; Fairy, Ghost and every non-creature
holder use `SimpleHealthComponent`. That split is the order the races happened to be converted in,
not a decision. Both extremes costed against Human's 11 parts (struct sizes from `Unsafe.SizeOf`,
per-system costs from a headless benchmark -- seed 1, frames 600-3600, `EcsContext.Update` at 1.489
ms/frame, 2026-09-17):

**Memory is the whole argument.** A part costs 48 B (32 B struct + 16 B of `MultiComponentPool` dense
side arrays) against `SimpleHealthComponent`'s 16 B (8 B + 8 B packed) -- **528 B vs 16 B per NPC,
33x** -- and it is paid for the whole loaded window (~660k race NPCs: 9 neighborhoods x ~73k, at 3%
ground / 2% underground / 2% flying of 1024²), not just the simulated part of it:

- All Simple: 660k instances, ~18 MB including `WorldSessionBootstrapper`'s 1.467x reserve.
- Today (~21% complex -- goblins are 49% of the ground roll only): 1.53M parts + 522k simple, ~116 MB.
- All Complex: **7.27M parts, ~520 MB** -- roughly +400 MB over today.

Two multipliers on top of that. Dense growth is linear (`_denseGrowthAmount` = 220k), so filling
7.27M takes ~33 `Array.Resize` passes -- ~5.7 GB copied, ending on a ~350 MB LOH array -- during
initial population, before the startup reserve exists to help. And `BodyPartComponent` holds a
`string Name`, so all ~520 MB is traced by every gen-2 GC, where the reference-free
`SimpleHealthComponent[]` is skipped outright. See "Component size audit" and "Gen-1 GC frames during
a window shift" below.

**CPU is the cheap half.** Only Local plus the centre neighborhood simulate
(`SimulatedTierCount = 2`), so the health systems see ~10.8k entity-visits/sec whatever the world
size. Measured per visit: `SimpleHealthRegenSystem` ~135 ns; `ComplexHealthRegenSystem` ~550 ns plus
`BodyPartEffectsSystem` ~400 ns, which has no Simple counterpart -- **~7x per visit**. Extrapolated
to the whole population: all Simple ~0.024 ms/frame, today 0.060, all Complex ~0.17, so +8% of the
simulation budget at the top end. What rides along with it is worth more than the regen systems
themselves: `TestCombatBehaviorSystem.TryDecideSelfHeal` and `MapViewQuery.GetHealthBarFraction` (per
visible entity, every Draw) turn `HealthQueries.TryGetTotals` into an 11-node chain walk;
`ComplexHealthRegenSystem.AccrueWhileFrozen` walks every part and sorts lockouts on tier promotion,
which lands on the window shift, already the spikiest frame; and a neighborhood load becomes 807k
`MultiComponentPool.Add` calls instead of 73k, inside `NeighborhoodStreamer`'s per-frame budget.

All of it scales linearly in part count -- 11 is Human's number, 6 parts halves everything above.

If every race should have body parts, these come first, then measure again:

1. Replace `BodyPartComponent.Name` with a `byte` id into a shared part-template table: 32 B -> 24 B,
   and the dense array becomes reference-free so gen-2 stops tracing 10M+ references.
2. Materialize parts lazily -- Simple until first damage, or on entry to a simulated tier -- so
   memory tracks the ~73k simulated instead of the 660k loaded.
3. Cache an entity's summed current/maximum so `HealthQueries.TryGetTotals` stops being a walk.

Not yet verified as an A/B: the figures above extrapolate one mixed-population benchmark rather than
two runs with every race converted each way. Do that A/B before committing to (2), which is the
expensive one to build.

#### Grid pathfinding and navigation

No pathfinding exists. Creatures move by random adjacent steps (`MovementMode.Random`), and the NPC
behavior plan's Engage/Flee (`PLAN-npc-behavior-composition.md`) step one tile toward or away from a
hostile, which gets stuck on the first wall. Every planned feature that sends a creature somewhere needs
this: `SeekTarget` ("Movement System"), NPCs using shops, mobs looting corpses, corpse-cleanup NPCs,
companions following through `EndOfLevelStairs`, NPCs avoiding stairs, quest NPCs, and the plan's
deferred errands.
- **Path queries over `IMapQuery`:** A* with an octile heuristic (diagonals cost x√2, matching the
  movement lock), or Jump Point Search on uniform-cost areas. Blocking comes from `IMapQuery.IsBlocking`
  and structures. Other creatures are dynamic obstacles handled at step time (wait, repath or swap),
  not baked into the path.
- **Per-agent cost filters:** one query, different rules per agent -- flyers ignore ground terrain,
  diggers can pass walls at a cost on `UnderGround`, a lava-immune creature doesn't avoid lava, a
  cowardly one weights tiles near hostiles (see "MapLayer interaction", Game). Costs come from
  `TerrainCell` flyweights, so no per-tile nav data is stored.
- **Flow fields for crowds:** many creatures chasing the player share one Dijkstra map centred on the
  player (Local radius only, recomputed when the player moves a tile), instead of an A* each. Flee uses
  the same map, inverted and rescaled (Brogue's "safety map"), which avoids fleeing into corners.
- **Hierarchical search for long routes:** routes across neighborhoods (a shop 400 tiles away, quest
  NPCs) search an abstract graph of region entrances first (HPA*), computed per neighborhood when it
  loads, as part of the worker's `NeighborhoodPlan`. Only Local and Neighborhood tier creatures ever
  refine it to tiles.
- **Budgeted and cached:** path requests go through a queue with a per-frame node budget, like the
  streamer's unit budget, deterministic so benchmarks and seeds reproduce. A path is followed until it
  is blocked or its target moves more than a set distance, not recomputed every step. A committed path
  fits the plan's commitment deadlines.
- **Tier-aware:** Borough and Beyond don't path at all (frozen). A path is invalidated when its
  neighborhood unloads.
- **Admin Mode:** draw the selected creature's current path and the flow field (see "Debug drawing
  and an AI debugger", Presentation).

**Unreal Engine reference:** Unreal's `UNavigationSystemV1` builds a Recast navmesh and searches it with
Detour. Most of it is 3D geometry work that a tile grid doesn't need, but these ideas carry over:
- *Nav areas and query filters:* `UNavArea` subclasses give regions a travel and entry cost (or make them
  impassable), and a `UNavigationQueryFilter` per agent overrides those costs per query. That is the
  per-agent cost filter above.
- *Nav links:* `ANavLinkProxy` / smart links join places the mesh doesn't (jumps, ladders, doors), with
  custom traversal logic. These are stairs and layer changes (Land, Take Off, Burrow) here.
- *Navigation invokers:* `UNavigationInvokerComponent` builds navmesh only around chosen actors in huge
  worlds. That's the Local/Neighborhood-only rule here.
- *Async queries:* `FindPathAsync` runs pathfinding off the game thread and hands back results later.
  This is the queue above; a worker-thread version is possible if the map can be read safely.
- *Crowd avoidance:* `UCrowdFollowingComponent` (Detour Crowd) and RVO steer agents around each other.
  On a grid this reduces to swap/wait rules at step time.
- *Path following:* `UPathFollowingComponent` owns "walk this path", reports blocked/finished, and
  requests a repath when the goal actor moves past a tolerance. It's worth keeping this a separate
  concern from deciding where to go.
- *Mass AI's ZoneGraph* (lane graphs for thousands of agents) is the analogue of the flow fields and
  hierarchical graph above.

**Godot reference:** `AStarGrid2D` is the closest built-in model of the three engines. Set a `region`
and `cell_size`, call `update()`, mark cells with `set_point_solid` and `set_point_weight_scale`, and
query `get_id_path(from, to)` (with `allow_partial_path` to get as close as possible when the target is
unreachable). `diagonal_mode` chooses between always, never, at least one walkable neighbour, and
only if no obstacles (no corner cutting -- the rule wanted here), and the heuristic is selectable
(Euclidean, Manhattan, octile, Chebyshev). `jumping_enabled` switches on Jump Point Search, but Godot
ignores weight scales while it's on, which is the same trade-off here: JPS only on uniform-cost areas.
`AStar2D` is the general-graph version, which fits the hierarchical graph of neighborhood entrances.
Godot's `NavigationServer2D` also supports navigation layers per tile in a TileSet and
`NavigationAgent2D` with RVO avoidance, covering the same ground as the Unreal notes.

#### Field of view and perception, shared by fog of war and NPCs

What a creature can see is currently computed ad hoc: the NPC plan's `NpcDecisionContext` counts
hostiles within a Chebyshev radius regardless of walls, and the player sees the whole map. Three
planned features need real sight: Fog of War (Presentation), Torch reveal + light-weakness damage
(above), and NPC behavior, where a goblin shouldn't engage through a wall. Digger detection needs a
non-visual version. Build one perception facility for all of them:
- **Field of view:** symmetric shadowcasting over `IMapQuery` (walls and sight-blocking terrain block;
  the Bresenham helpers in `TargetShapeResolver` are a start for single lines). Symmetric means that if
  A sees B, B sees A, so neither the player nor an NPC gets an unfair ambush.
- **Light:** a tile is seen if it's in view and lit, or within a creature's own darkvision radius. Light
  sources (Torch, lava, later day/night) add to a per-tile light level. This settles Torch's "per-entity
  grant vs. a location" question: light is a per-tile quantity that sources write into.
- **Senses as a list:** sight (field of view plus light), hearing (noise events with a radius, raised
  by combat and movement, blocked or reduced by walls), and touch/damage (anything that hit me). A race
  declares which senses it has and their ranges; Diggers have tremorsense instead of sight.
- **Memory:** each perceiving creature keeps its last-known location of what it has perceived, with an
  age. Losing sight doesn't make a hostile vanish; the NPC goes to the last-known location. This is the
  "tracking" state in the plan's DCSS/Brogue research.
- **Factions:** perception reports hostile/neutral/friendly relative to the perceiver, so behavior code
  asks "hostiles I can see" rather than filtering by race.
- **Budget:** only Local creatures get real field-of-view checks. Neighborhood tier falls back to a
  cheap radius, and frozen tiers don't perceive. Computed on the creature's decision visit, not every
  frame, and cached for that visit like the plan's lazy perception.
- **Player's view:** the player's field of view is the visible set for Fog of War.

**Unreal Engine reference:** `UAIPerceptionComponent` on the AI controller, fed by
`UAIPerceptionStimuliSourceComponent` on anything that can be sensed. Senses are separate classes:
`UAISense_Sight` (sight radius, a larger lose-sight radius for hysteresis, peripheral angle, auto-success
range from the last seen location), `UAISense_Hearing` (fed by `ReportNoiseEvent` with loudness and
range), `UAISense_Damage`, `UAISense_Touch` and `UAISense_Team`. Each stimulus has a max age, and the
component remembers the last sensed location after the stimulus is lost. Attitude comes from
`IGenericTeamAgentInterface` / `FGenericTeamId` (Friendly/Neutral/Hostile). Sight is budgeted: the
sense runs a capped number of line traces per tick (`MaxTracesPerTick`) and time-slices the rest, which
is the same idea as the Local-only budget here. Unreal has no built-in fog of war or 2D light map;
Paper2D sprites are lit by ordinary 3D lights. For those two, roguelikes are the reference (Brogue's
light and field of view, "symmetric shadowcasting" by Albert Ford).

**Godot reference:** Godot has the 2D lighting the other two lack (`PointLight2D`, `LightOccluder2D`,
`CanvasModulate`; see "Visual 2D lighting", Presentation), but it is purely visual -- a light's
reach isn't queryable by gameplay. Keep the split: the per-tile light level here is the gameplay
truth (what's lit, what's seen, what burns a light-weak creature), and the visual lighting pass reads
the same light sources to draw it. The two must agree on radius and falloff, so both read one light
source definition. Godot TileSets can carry occluder shapes per tile (occlusion layers); here that is
a "blocks sight" and "blocks light" flag on `TerrainDefinition` (beside `BlocksMovement`), read by both.

### Medium Priority

#### Dropping items on the ground

Let the player put items down on the map, and pick them up again. Besides being expected, it is how
a light is set down: a carried light item stays lit in a dropped pile and radiates from it
(`ToggleItemHolderSync` already lets any holder, on the map or not, alive or dead, hold a lit unit),
which replaces using Ground mode to anchor an item's light. Plan it as one rule set, so every way an
item leaves an inventory treats it the same.

**What a drop is**
- An item pile: an entity on the dropper's tile (a blueprint like `TreasureChest`, but non-blocking,
  with no health and no `ContainerComponent` destruction), holding ordinary `InventoryItemStackComponent`s.
  One pile per tile and layer: dropping where a pile already is adds to it. It shows the loot bag badge
  (`MapViewQuery`'s `LootBagState`) and opens in the loot window, where Take/Take All pick items up, as
  for a corpse or a chest. An empty pile is destroyed.
- The moved unit keeps everything that is the unit's: a diverged stack's `Override` (a wand's
  charges), its lit state (`ToggleItemActivator.IsToggledOn`), `FirstAcquired`. A lit stack never
  joins a Merged Stack cell, here as anywhere. Moving goes through the existing transfer path
  (`InventoryActions`), never a new one, so `ToggleItemHolderSync` (lit units) and
  `ItemHotkeyBindingActions.RepointAfterUnitMoved` (slots on a stack that is now gone) follow with no
  caller remembering.

**Where it is offered**
- Inventory grid context menu: "Drop" (one unit) and "Drop All" (the stack), named like
  "Give"/"Give All" (`InventoryGridContent`). Offered for the player's own inventory only, not a
  corpse's, a shop's or a trade offer.
- Hotbar item slot context menu: "Drop" -- one unit of the stack the slot is bound to, the same unit
  an activation from that slot would use (`ActivatedFromSlot`). The slot follows the rule
  `RepointAfterUnitMoved` already applies: it moves with the unit only if the stack it named is gone.
- Possibly dragging an item out of the inventory onto the map (an `IDragDropResolver`, priority order
  with Trade/Shop/Plain). Decide whether that is wanted or only the menu.

**Decide**
- Refused, with the disabled cursor and a reason, while the stack is winding up (`PendingWindupComponent`
  with Kind Item) or has a pending activation, during a trade, and while dead.
- What an unloaded neighborhood does to a pile (today every entity in it is destroyed): lose the
  items, or keep piles in the neighborhood record ("Entity storage", Global).
- Whether NPCs pick piles up ("Mobs looting corpses") and whether a pile has loot rights like a
  corpse ("Corpse looting rights based on damage dealt").
- Currency: a "Drop" on the currency row, or never.
- Destroying an item outright stays "Destroyed items", not a drop.

#### Fuzzy targeting

Version 2 of "an area effect that attaches to one entity attaches to the entity on its center tile"
(aura stage 3, `PLAN-aura-stage-3.md`). In Target mode only: prefer the entity on the center tile; if
the center tile is empty, take an entity on a tile adjacent to it instead, so a slightly missed click
on a crowd or a large entity still lands on someone. Decide the order among several adjacent
candidates (the same Blocking, then Tiny, then Phasing priority Target mode uses on one tile is the
likely start). Ground mode never does this: it is exactly the tiles aimed at.

#### Anchored effects: a maximum distance from the caster

Version 2 of "an anchored effect is cancelled when it is too far from its caster" (aura stage 3).
Version 1 cancels it only when the neighborhood holding it, or its caster, unloads. Version 2 gives
each cast an explicit maximum distance, from the action and the caster's modifiers (a new
`StatModifierTarget`), checked as the caster moves; past it the anchor ends, and a toggle holding it
switches off, the same as version 1's cancellation. Decide whether the check runs on the caster's
moves (cheap, exact) or on the anchor's own timer.

#### Spell customization at the time of casting

A spell's numbers chosen as it is cast: Magic Missile picks a strength, with a mana cost that scales
with it; a light spell picks a radius. Only spells for now. Wands and potions get theirs when they are
made (crafting), not when used. Needs: a per-spell declaration of what can be chosen and its range,
how the choice scales the cost, a casting UI (likely a step after arming, or a modifier key with the
mouse wheel), and the choice carried on the activation request and the windup like the target
selection is. A light's radius is its aura size (`AuraSourceGrant.Size`); see also
"Spell leveling".

#### Body part disablement that lasts -- injury states, and thresholds instead of 0 HP

A body part is disabled only while its `CurrentHealth` is exactly 0 (`BodyPartStateComponent.IsDisabled`,
set in `EntityBodyParts` when a part hits 0 and cleared the instant any heal or regen tick raises it).
Passive regen (`ComplexHealthRegenSystem`) starts again as soon as the regen lockout ends, so a destroyed
arm or leg is usable again a few frames later. `BodyPartEffectsSystem` also only re-scores an entity once
a second (`StripeCount` = frames per second), so the `MeleeDisabledComponent`/`MovementDisabledComponent`
hard blocks often never appear at all, and the disabled-action work (IMPLEMENTATION-NOTES.md "Disabled actions") has almost
nothing to show. Destroying a limb should matter.

Re-plan it from the state model up:
- **Split "disabled" into injury states**, each with its own rule for healing, e.g.:
  - *Impaired* -- below a threshold, heals normally.
  - *Disabled* -- at 0, passive regen paused for a while (longer than the current lockout).
  - *Broken/Crippled* -- passive regen can't lift it out of the state; an active heal or a specific item
    (splint, bandage, healing potion tier) is needed.
  - *Severed/Destroyed* -- nothing heals it; needs regeneration magic or a resurrection-tier effect.
  How a part enters each state (overkill damage beyond 0, damage type -- crushing breaks, slashing
  severs, a crit, a status effect like Paralysis V2 above) is part of the design. Health owns the states;
  other features read them through Health's API (Poison/Burning are the model, CLAUDE.md "Modding").
- **Stop actions at thresholds, not only at 0 HP.** An action's requirement is a percentage or a state
  per body part type, so a badly damaged arm can refuse Power Attack before it refuses Quick Attack. This
  folds into "Melee actions should declare which body parts perform them" (Low Priority) and the
  disabled-action reasons (`ActivationBlocker`), whose melee text becomes body-plan specific then.
- **Score on change, not on a timer.** The hard-block markers (and the penalties) should update when a
  part's state changes, not once a second, so a block and its tooltip appear the frame the arm breaks.
- Keep regen and heal paths going through one state check (`ComplexHealthHeal`, `ComplexHealthRegenSystem`,
  `BodyPartSelection` already skip lockouts together), so a spell, a potion and a regen tick agree on
  what can be healed. "Player-selected healing priority" (Low Priority) must skip parts that can't be healed.
- Presentation: the Health window shows each part's state, and the HUD/tooltips name it.

Open questions: how many states, and whether they're a ladder (each worse than the last) or independent
flags; whether Simple-health entities get any of it; how a state interacts with Maximum Health changes
(`MaximumHealthShift`).

#### Gen-1 GC frames during a window shift

While `NeighborhoodStreamer` populates newly loaded neighborhoods, gen-1 collections occasionally
produce frames of ~17-19 ms (headless Release, seed 12345, 3072², with the 1 ms timer the windowed
game has). Gen-1 frames during a shift run 12-17 ms, varying run to run; the over-budget ones show up
mainly in the first shift of a session, while the population grows to its settled size. Measured by
teleporting the player 70 tiles into the next column of neighborhoods at frames 700 and 1400 of a
headless run (3 loads, then 3 loads plus 3 evictions); see "World scaling" in `IMPLEMENTATION-NOTES.md`.
Named gauges now record GC collections, pause time, allocation, streamer queues and built/skeleton
counts per frame: the benchmark skill's `-Gauges` lists them on the slowest frames.

What's known:
- **Pause cost tracks what population keeps.** Before the 7d allocation cuts, each gen-1 pause was
  8-25 ms and promoted 10-16 MB, nearly everything population had allocated. Sharing wand loot
  definitions and race attack overrides, caching `AbilityScoreType` values and removing a capturing
  closure in `InventoryActions.AddItem` took steady-state population to 469 bytes allocated and ~340
  kept per entity, and shift 2 from 9 frames over budget to 0.
- **Remaining per-creature allocations** (sampled with a `GCAllocationTick` listener): occupant
  `List<int>`s in `MapNeighborhood` (one per occupied cell, new for every loaded neighborhood),
  `EntityStripeSet`/`TieredEntityStripeSet` dictionary growth, and `TimerWheel` slot lists.
- **Per-frame gameplay garbage** in the same window, short-lived but part of every gen-0/gen-1:
  `EffectContext`, strings. The `ManhattanCellVisitor` closures and the boxed enumerators over a
  cell's occupants and the frame's moves are gone (2026-10-02; steady allocation down 11%, 229.7 to
  204.2 MB over frames 600-3600), leaving about 68 KB a frame whose sources have not been sampled since.
- **Tried and worse:** Server GC, and a 256 MB gen-0 budget (55-120 ms pauses). Pre-sizing every
  stripe-set and aura-grid dictionary didn't change the spike frames.

Options to measure:
- Occupant lists: keep a single occupant inline per cell and spill to a list only for a second one, or
  recycle an unloaded neighborhood's `MapNeighborhood` (arrays and lists) for the next load.
- Stripe sets: an entity-indexed location array shared across a tiered set's tiers, instead of five
  dictionaries per set.
- Remove the per-frame allocations above.

Acceptance: no frame over 16.67 ms during either shift of that teleport measurement, other than the
teleport frame itself.

#### Component size audit

Measured with `Unsafe.SizeOf`. Every loaded entity pays for these, and a neighborhood streams in about
73k creatures at a time, so bytes per instance are memory, cache misses and streaming cost at once.
Most of the original list landed with the deferred-build work (2026-09-23): `RaceComponent`/
`ClassComponent` became two-`ushort` slot components, `BodyPartComponent` became race templates plus a
per-entity state component, `ActionInstanceComponent` became per-definition grants, and
`InventoryItemStackComponent` went 56 -> 24 B (interned item id, counter stack id, counter acquisition
sequence) with `MovementComponent` 40 -> 32 B. What is left:

- **`InventoryItemStackComponent`, 24 B, still the largest pool at ~75 MB.** 8 of those bytes are the
  `ItemDefinition? Override` reference, set on a small minority of stacks and scanned by every gen-2
  GC. Moving it to a side table keyed by stack instance id would take the struct to 16 B and drop the
  reference, but that table has to be cleaned up when a stack is removed -- including when an entity
  is destroyed with its stacks still in it -- so it needs a real owner, not a static dictionary.
- **`PendingDelayedActionComponent`, 32 B with references.** A `Guid` action id where a catalog index
  would do, plus a target-tile array per pending action.

Components holding references are scanned by every gen-2 GC as well as being larger, so moving
strings and overrides out is worth more than the byte count alone suggests. Measure pool memory
before and after (the diagnostics memory report lists each pool) and fix the largest total first.

#### Quest NPC simulation tickets

No quest system exists yet beyond the TEMPORARY quest composer demo and the "quest line template"
in "Real map generation" above. Note this before quests land:
Borough is frozen, so a quest NPC that walks out of the middle neighborhood freezes mid-quest. A
courier stops, a fleeing target stands still, a rival party never arrives.

Minecraft's precedent is **chunk tickets**: a ticket holds an area at a chosen load level
regardless of the player's position (portals, spawn chunks, chunk loaders). A quest variant has to
satisfy two constraints: it must not reintroduce the cost frozen Borough removed, and **the NPC
must never walk through a frozen chunk**. An NPC moving through frozen territory can't be
attacked, can't fight, can't be blocked by anything that would move, and nothing around it reacts,
so a live NPC in a frozen world is wrong in every direction.

First, most quest NPCs don't need a ticket. An escort or companion stays near the player, so it is
already Local. Tickets only matter for NPCs acting **independently** of the player.

Three shapes, from cheapest:

1. **Entity-only ticket** (simulate just the NPC): rejected. This is exactly the "walks through a
   frozen chunk" case.
2. **Abstract transit** (recommended default): when an independent quest NPC's route enters frozen
   territory, remove it from the map and move it along the route on the neighborhood-record layer
   ("arrives at X at frame F"). It reappears when its position is simulated again: at F if the
   destination is active by then, otherwise frozen at the destination and caught up on its next
   promotion. It never occupies frozen tiles in transit, and it costs one record timer. Its
   reappearance goes through the smeared transition queue. If its route leaves the loaded window
   entirely, abstract transit is the only option.
3. **Area ticket** (only when the quest needs the NPC to be interceptable live): a small moving
   bubble of radius r around the NPC runs at Neighborhood speed, reusing Local's machinery: O(boundary)
   per step, promotion with catch-up on entry, freezing on exit, all smeared. At r = 32 that's
   ~4,200 tiles, about 0.4% of a neighborhood. Keep r larger than the NPC's own engagement range plus
   a buffer, so it never tries to act across its bubble edge (the same seam rule as the
   Neighborhood/Borough border). **Cap concurrent area tickets** (e.g. 2-4) and give each one an
   expiry. When the cap is hit, extra tickets fall back to abstract transit rather than degrading
   the frame.

Open: the ticket cap and radius (measure the per-bubble cost), whether an area ticket may extend
into the dropped ring when the window shifts (probably not: fall back to transit), and how ticket
cost shows up in the diagnostics engine so it's visible when budgets are tuned.

#### Dexterity scaling ActionLockComponent.StandardLockFrames

Flat per-entity today (Goblin 54, Fairy/Ghost 48, Player 20, +Engineer 10%). Lerp
`ActionLockGate.StandardLockFrames` (1s) at Dex 1 down to 0.25s at Dex 300, off
`AbilityScoreComponent.Total` (same shape as `PotionCooldownEffects.ComputeDurationFrames`). Must
compose with, not replace, the racial baseline -- exact composition (multiply vs. replace) undecided.

#### Spell leveling

Same rules as Skills (level 0-15/20, XP with use, never decreases) -- land after Skills so both share
one leveling primitive. A spell's level would modify its `Effect` magnitude/duration (bigger
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
`ShopActions.TryBuyFromShop`/`TrySellToShop` (the player-only version already shipped). Needs actual economic decision-making (what to sell for spare Gold, what to buy when low
on supplies) -- blocked on the NPC behavior composition item (this file's own Low Priority section),
which is where that decision would live once it exists.

#### Destroyed items

A "destroyed" item state: displays as "destroyed", modified description, can still be picked up, but
can't be used. Update `ContainerDestructionSystem` (and any future container types) to mark a destroyed container's inventory items destroyed instead of
deleting them outright, once this lands.
An item with `ItemDefinition.CanTrade` false (a loot box) can never be destroyed or dropped.

#### Item activator behaviour on the activators, not in ItemActivationSystem

Investigate. `ItemActivationSystem` switches on `item.Activator`'s concrete type and holds each kind's
rules as private methods: `PeelWandCharge`, `ActivatePotion`/`ApplyPotionToTarget` (cooldown and the
cooldown-abuse Poison), `ActivateScroll`/`ApplyScrollToTarget` (Intelligence scaling, mastery),
`ActivateWand`, the toggle branch. A new activator kind -- a mod's included -- means editing the system.
Look at whether each activator (or a per-kind handler registered by activator type, as `WindupResolvers`
and `IToggleOwner` are) can own what using it does: how the unit is spent (`RemoveOneUnit`, a charge
via `MoveOneUnit`, nothing), what gates it (`TryBeginActivation`/`TryBeginWandActivation`, the lock or
FreeCast), and how its effects are applied per target. The system would keep what every kind shares:
the request queue, the blocker check, activation effects, the action lock. Decide where the per-kind
state a handler needs (pools, catalogs, `EffectServices`, the clock) comes from, since activators are
immutable content records today.

#### Spatial queries for NPC decisions, and a per-NPC blackboard

The NPC plan's behaviors (`PLAN-npc-behavior-composition.md`) decide *whether* to engage, flee or wander
through dual utility, but not *where*: Flee picks "away", Wander picks a random adjacent tile, and
nothing can ask for "a tile out of melee reach but within spell range", "a tile behind an ally", "the
nearest shop that buys potions" or "a corpse I have rights to loot". Add a small query facility:
- **A query is data:** a generator (tiles in a radius, tiles on a ring, entities of a kind nearby, a
  path's tiles) plus a list of tests that filter or score each candidate (distance to X, can see X, path
  length, danger from the flow field, occupied, terrain cost), plus a pick rule (best, weighted random
  among the top N%). Behaviors and grants name a query rather than writing the search.
- **Evaluated in the decision visit** with a candidate cap, on the same cached perception, so it costs
  what the plan already budgets.
- **Claimable spots:** things NPCs use (a shop counter, a corpse being looted, a bed, a Dread Idol's
  worship spots) advertise slots that an NPC claims before walking over and releases after. Two
  goblins don't both walk to loot the same corpse, and "NPCs use shops" queues rather than piling up.
  Also the natural home for the plan's deferred errands.
- **Blackboard:** the cached perception and commitment state in `NpcDecisionContext` is a per-decision
  blackboard today. Anything that must outlive a decision (the current target's `EntityKey`, the
  last-known location from perception, a claimed slot) needs a small per-NPC component rather than
  per-creature objects (the plan's GC point about Caves of Qud's goal stacks).
- **NPC ranged attacks at any hostile:** the first NPC ranged attack (a Fairy's Magic Missile, aura
  stage 3) is cast only at the player, because the player is one distance check away; finding the
  nearest hostile within a spell's range needs an "entities of a kind nearby" query. The same query
  should pick the NPC's targeting mode (Target or Ground) on purpose -- Ground to catch something
  walking into a choke point, Target to chase -- instead of the stage-3 coin flip.

**Unreal Engine reference:**
- *EQS (Environment Query System):* `UEnvQuery` assets made of a generator (`EnvQueryGenerator_SimpleGrid`,
  `_OnCircle`, `_ActorsOfClass`, `_PathingGrid`) and tests (`Distance`, `Trace`, `Pathfinding`, `Dot`,
  `GameplayTags`) that each filter, score or both, with contexts (the querier, its target) to measure
  from. Run modes: single best, random among the best 5% or 25%, or all matching. Queries run
  time-sliced over several frames. Behavior trees and StateTree call them as a task.
- *Smart Objects:* `USmartObjectComponent` defines slots on an object; an AI finds one with
  `USmartObjectSubsystem::FindSmartObjects` (filtered by tags), claims it, uses it (the slot supplies the
  behavior to run) and releases it. This is exactly the claimable-spot idea above, and it's designed to
  work with Mass for large crowds.
- *Blackboard:* `UBlackboardComponent` is a typed key/value store per AI; behavior tree decorators
  watch keys and abort a running branch when one changes (observer aborts). The plan's utility
  arbitration replaces the tree, but "re-decide immediately when my target key changes" is a useful
  interrupt rule to keep in mind.
- Unreal has no built-in utility AI. The utility arbitration the plan already chose stays; EQS and
  Smart Objects are complements, not replacements.

#### Gameplay Cues -- presentation feedback raised by gameplay

"User feedback for actions is missing entirely" (Low, below) and every effect that should be seen or
heard need gameplay to announce *that something happened* without Game knowing how Presentation will
show it. Floating text (`IMPLEMENTATION-NOTES.md`, "Floating Text") already works this way for damage
taken, but through its own event. Generalize it:
- A cue is a `GameplayTag` (`Cue.*`, IMPLEMENTATION-NOTES "Gameplay tags") plus a small payload: where (entity or
  tile), who caused it, a magnitude, and whether it's a one-shot or a start/stop pair (a Burning loop
  starts when the status is added and stops when it's removed).
- Game raises cues from the chokepoints that already exist (`HealthDamage`, status effect grant and
  expiry, action windup start, activation, cancel, Dodge success, Immune). One cue event type instead of
  one event per feature.
- Presentation maps cue tags to handlers: floating text, a sprite flash, a particle effect (see
  "Particles", Presentation), a sound (see "Audio", Presentation). A tag with no handler does nothing,
  and a parent tag's handler serves children without their own (`Cue.Damage` for any damage type until
  `Cue.Damage.Fire` gets its own).
- Culled like floating text: only Local, only in the viewport, so off-screen fights raise cheaply or
  not at all.
- Mods add cue tags and Presentation handlers for their own effects.

**Unreal Engine reference:** GAS Gameplay Cues. A cue is identified by a `GameplayCue.*` tag and fired
from a gameplay effect or ability (`ExecuteGameplayCue` for one-shots, `AddGameplayCue`/`RemoveGameplayCue`
for looping ones) with `FGameplayCueParameters` (location, instigator, effect causer, magnitude).
`UGameplayCueManager` maps tags to handlers: `UGameplayCueNotify_Static` (stateless, for one-shots)
and `AGameplayCueNotify_Actor` (spawned and kept alive for a looping cue, receiving
OnActive/WhileActive/Removed). A tag with no exact handler falls back to its nearest parent's. Cues are
cosmetic by contract -- gameplay never waits on or reads them -- which is the same boundary as
Game → Presentation here.

**Bevy reference:** Bevy has no cue system; the idiomatic version is an entity-targeted event
triggered from gameplay and handled by observers in the rendering and audio plugins (see "Component
lifecycle hooks and observers", Engine). A looping cue maps onto a marker component whose add and
remove hooks start and stop the effect, which keeps start/stop pairs from getting out of step.

**Godot reference:** the handlers Godot games typically hang off such events are cheap and effective:
a white flash on the hit sprite (a `CanvasItem` shader parameter or `modulate` tweened back -- see
"Sprite shaders and tinting", Presentation), a short screen shake (`Camera2D.offset` jittered and
decayed by a tween), and hit-stop (a few frames of reduced `Engine.time_scale` -- here, a presentation
freeze, never a simulation one). `MapCamera` has zoom levels but no offset or smoothing to shake with;
adding a decaying offset is the smallest piece.

#### Data tables for content

Content that is really a table is written as C# today: loot box contents (`RandomSingleStackContents`),
shop stock, prices, per-race baseline scores, achievement criteria. Several planned items are tables or
curves: "Lootbox drop tables", "Preferred stock for items added to shops", "CVS (Cosmic Value Shop)
general store", "Achievement content backlog", Dexterity → `StandardLockFrames` ("Dexterity scaling
ActionLockComponent.StandardLockFrames"), Intelligence → durations, falloff shapes. Add a data table
facility:
- A table is a list of rows of one struct type, keyed by name or Guid, loaded from JSON in `Content/`
  the way `SpriteManifest.json` is. Rows reference other content by Guid (items, blueprints, tags).
- Curves: a list of (x, y) keys with an interpolation mode, sampled by a stat (Dexterity 1..300 →
  0.5 s..1 s). This replaces hand-written formula constants and makes scaling tunable without a
  rebuild.
- Mods add rows, or add a table that overrides rows of a built-in one by key, in load order -- the same
  replace-by-Id rule `ModuleSet.Combine` and `BlueprintRegistry` already use.
- Validated at load, as part of each mod's dry run: a row referencing a missing Guid fails the load.
- Blueprints stay C# (their `Build` steps are code); this is for rows of numbers and references.

**Unreal Engine reference:** `UDataTable` (rows of one `FTableRowBase`-derived struct, keyed by
`FName`, imported from CSV or JSON), `UCurveTable` and `UCurveFloat` (keyed curves with per-key
interpolation, sampled with `Eval`). `UCompositeDataTable` stacks several tables, and a later table's
row replaces an earlier one's with the same key -- exactly the mod-override rule above.
`UPrimaryDataAsset` plus the Asset Manager give content stable ids (`FPrimaryAssetId`) and let it be
found and loaded by type. Designers edit these without touching code, which is the goal here for loot,
shops and scaling.

**Bevy reference:** the asset server adds what Unreal's tables need the editor for: hot reloading
while the game runs. `asset_server.load("loot/alchemist.ron")` returns a `Handle<T>` at once and loads
in the background through an `AssetLoader` for that file type; with the `file_watcher` feature, editing
the file reloads it and raises `AssetEvent::Modified`, and systems holding the handle see the new
data next frame. For here: tables load through one loader per table type, a file watcher in Debug
reloads a changed table between frames, and anything that caches derived data from a table listens
for the reload. Tuning a drop table or a scaling curve then needs no restart.

**Godot reference:** custom `Resource` classes (saved as `.tres`) are Godot's data definitions, and
their default is the rule this project already follows for GC reasons: loading the same resource path
twice returns the *same* object, shared by everything that uses it, and a per-instance copy is an
explicit opt-in (`resource_local_to_scene`, `duplicate()`). Tables should load the same way -- one
immutable instance per definition, referenced, never copied per creature. `ResourceLoader.load_threaded_request`
loads in the background and is polled for completion, which fits loading a neighborhood's tables on
the streamer's worker.

#### Game flow -- start menu, session states, floor transitions

The game goes straight into a world session at launch, and several planned items need defined states
around it: a start menu ("Show module load failures on the start menu", Presentation), a game over
state ("Game over screen on player 0 HP", Presentation), the level collapse timer, floor changes
through `EndOfLevelStairs`, and the long-range teleport's "pause and reload the map". Today's pause
modalities (see Pause modality, `IMPLEMENTATION-NOTES.md`) cover pausing within a session only.
- **States:** start menu, loading, playing, paused, floor transition, game over. Each defines what
  updates (simulation, UI, streaming) and what input does. `GameLoop.Update` asks the current state
  instead of checking flags.
- **Session vs. run:** a run spans floors; a world session is one floor. State that lives across floors
  (the player's entity and inventory, crawler number, achievements, meta-progression) belongs to a
  run-level object that outlives any `EcsContext`, not to a session. The multi-floor seam
  (`FloorBuilder.CreateMap`'s `floorNumber`) and "Save and load Beyond neighborhoods" (Global) both
  need this split.
- **Transitions:** tearing down a session and building the next one is one operation with a loading
  state in between, also used by "Reload mods without restarting" (Engine) and a future "load game".

**Unreal Engine reference:** Unreal's gameplay framework separates these roles:
- `UGameInstance` lives for the whole process and survives map changes -- the run-level object.
  `UGameInstanceSubsystem`s hang run-level services off it.
- `AGameModeBase` holds the rules of the current map; `AGameMode` adds a match state machine
  (`WaitingToStart`, `InProgress`, `WaitingPostMatch`, `LeavingMap`). `AGameStateBase` holds the state
  everyone can see (time left, the level collapse timer).
- `APlayerController` (input and UI for a player) is separate from the `APawn` it controls, so the
  player's body can die, be replaced or be possessed while the controller and its UI stay.
  `APlayerState` holds per-player data that survives the pawn.
- Map changes are `OpenLevel` (a hard load) or seamless travel through a transition map; either way,
  anything not on the Game Instance is destroyed. That's the rule the session/run split above enforces.
- `UWorldSubsystem` is per-map, the equivalent of per-session services.

**Bevy reference:** States. `init_state::<GameState>()` adds an enum state; systems in `OnEnter(S)` /
`OnExit(S)` / `OnTransition { exited, entered }` schedules run once on each change; `.run_if(in_state(S))`
limits ordinary systems to a state; `NextState<S>` requests a change, applied at a fixed point in the
frame (`StateTransition`). `SubStates` exist only while a parent state is active (Paused only inside
Playing), and `ComputedStates` are derived from others. `DespawnOnExit(S)` (formerly `StateScoped`) on
an entity despawns it when the state is left, which is a tidy rule for UI and per-state entities (the
game-over screen, a floor's contents on a floor transition). Bevy has no run-level object like
`UGameInstance`; resources simply persist across states unless removed. Pairs with "Named schedules,
system sets and run conditions" (Engine), where "in state X" is a run condition.

### Low Priority

#### Terrain contact as a range-0 aura -- revisit if contact needs to come from entities

Terrain contact (`TerrainContactSystem`) and auras (`AuraSystem`) now hold the same `Effect` lists,
apply them with no source entity, keep a per-entity exposure on a timer wheel with the same
refused-once-per-stay bit, and read their definition fresh at every application. A strength-1 aura
already reaches only its own cell. What still separates them:

| | Terrain contact | Aura |
|---|---|---|
| First application | At once on stepping on, and again on every step between such cells | Never on entry; only a tick, the first staggered within a second |
| Repeat | Per contact (`RepeatEveryFrames`), or none | One second for every aura |
| Strength | None; effects apply at their own amounts | Scales amounts; overlapping sources add |
| Body part | Hands the ground-contact part to entries that ask | Hands over nothing |
| Reaches | Whoever stands on the cell | Everything in range but the source's own entity |
| Found by | Reading the terrain under each mover (an array read) | The aura grid (a coverage bit, then a hash lookup per aura) |

The first row is the real difference: an entity crossing an aura between ticks is untouched, by
design, while lava hits on every step.

**Revisit when any of these is wanted:**
- **An entity that carries a contact effect** -- a creature that burns whatever touches it, a trap
  that is an entity rather than terrain. That is an entity-sourced, range-0, apply-on-entry effect,
  which contact can't express (it is terrain's) and an aura can't either (it never applies on entry).
- **An aura that applies on entry**, or one with its own tick interval, or one that never repeats.
- **An aura that hands a body part to its effects**, the way a contact hands over the ground part.
- **A contact whose strength should add** where sources overlap or fall off with distance.

Unifying means an `AuraDefinition` gaining three options -- apply on entry as well as on the tick,
an interval of its own including "never", and a body part for its effects -- with a terrain's
contact becoming a second, strength-1 aura on its cells.

**Costs to weigh then:**
- Every contact cell goes into the aura grid: lava is about 1% of ground cells, a lot of single-cell
  entries in a structure built for overlapping falloff.
- Contact detection goes from one array read per mover to a coverage-bit test and a hash lookup per
  aura.
- "On every step" has to be added to a system whose one rule is that only a tick applies.

If none of the conditions comes up, the cheaper improvement is sharing more of the exposure code
between the two systems and leaving detection as it is.

#### Generalize the aura field's pieces when Explosions and Traps land

Explosions and traps both need things the aura stack already has, but only for auras today. When
either is designed, explore which pieces should become general rather than building a parallel set:
- **Reach and falloff** -- `ManhattanDiamond` and `AuraFalloff` (Engine.Math) already give a shape and
  a value per distance. An explosion is a one-shot reach with falloff; it likely wants these directly,
  not the persistent per-cell totals.
- **Per-cell totals** -- `AuraGrid`/`AuraTotals` hold a lasting field keyed by aura id. Decide whether
  anything else (a lingering blast zone, a trap's trigger area, a danger map for NPCs) wants a lasting
  per-cell field, and if so whether it is keyed by something other than a `byte` aura id.
- **Spatial index of entity sources** -- `AuraSourceIndex` (32x32 chunk buckets, fixed attribution,
  strongest-contributor lookup) is a general "which entity sources reach this cell" structure.
  Entity traps need "which trap is on or near this cell" on every move; see whether this index, or
  `Map`'s non-blocking occupant index, serves that.
- **Attribution** -- an explosion's or trap's damage needs kill credit to whoever set it, the same
  rule as an anchor's placer (`AuraSystem.AttributionOf`).
- **Apply on entry** -- a trap is an entity-sourced, range-0, apply-on-entry effect: the first bullet of
  "Terrain contact as a range-0 aura" above. Settle that entry's question at the same time.

Keep what stays aura-specific (exposures, the once-a-second tick, glow) in Auras; move only what a
second consumer actually uses.

#### An ability that turns a direct-target spell into a limited-duration aura

An ability (or item, or class feature) that takes a spell normally cast at a target and makes the
caster radiate it instead for a while: Magic Missile becomes a damaging aura, Heal a healing one.

The pieces exist. An aura definition lives with whatever radiates it and is only a name, a glow
colour and a list of `Effect` -- the same lists an action holds -- and `AuraCatalog.Register` takes a
definition at any time, so one can be made at runtime from an action's own effects and granted with
a timed `AuraSourceGrant`. To settle when designing it:
- **Which spells qualify, and what the aura costs** (mana per tick or up front, cooldown).
- **Scaling:** an aura applies once a second with no source entity, so no crit, no ability bonus and
  no Outgoing modifiers, and its strength scales amounts. Decide whether the converted aura keeps the
  caster's stats (which needs a source entity on an aura's effects; today they are only credited to
  the strongest contributor, `AuraField.Attribute`) and what strength and reach it gets.
- **Identity:** each converted spell needs a stable Guid (derived from the action's and the
  ability's), so two casters' auras of the same spell add up as one aura and the catalog doesn't
  grow per cast. The catalog holds at most 256 definitions in a session.
- **Who it affects:** an aura reaches everything in range but its own sources, allies included.

#### Player-selected healing priority

Complex-health healing always goes to the lowest body part: HP regen (`ComplexHealthRegenSystem`) and
`LowestPercentage` heals both pick through `BodyPartSelection.PickLowestPercentage`, which skips
regen-locked and full parts and breaks ties by body-plan order, with no preference for Vital parts. The
player can't steer it, so a regen tick can go to a scratched foot while the torso is one hit from death.

Let the player choose a healing priority in the Health window. It decides which part every regen tick
and every single-part heal not aimed at a specific part goes to. Heals that split evenly across every
part (`BodyPartTargetMode.All`) and heals aimed at a part bypass it.
- **Vital First (default):** heal Vital parts (lowest % first) until every Vital part is full, then fall
  back to Lowest First for the rest. The same as Custom with the Vital parts selected automatically.
- **Lowest First:** the part with the lowest %. Ties go to a Vital part, then to the first in body-plan
  order.
- **Custom:** the player selects one or more parts. Heal those by Lowest First until they're all full,
  then everything else by Lowest First.

Direction:
- **Game owns the rule.** One selection method in `BodyPartSelection` takes the entity's priority and
  replaces `PickLowestPercentage` at every single-part heal and regen call site, so a spell and a regen
  tick can't disagree. Parts that are regen-locked out (Burning's lockout) or full are skipped at every
  step, as today, so a locked prioritized part falls through to the next.
- **Stored per entity:** a small Packed component holding the mode and, for Custom, the selected part ids
  (a bitmask over the body plan -- part ids are stable for the entity's lifetime). An entity without one
  uses Vital First. The player sets it through a command (`HealthCommands` or `PlayerCommands`), and the
  Health window reads it through `HealthView`.
- **Health window:** a mode selector, and an icon on every body-part row whose part is prioritized for
  healing -- the Vital parts under Vital First, the selected parts under Custom, none under Lowest
  First. Under Custom, clicking a part's icon area toggles it.
- **NPCs use Vital First too.** Every Complex-health entity without the component gets it, so NPC regen
  and heals change from today's Lowest First with no Vital preference. NPCs never hold the component
  unless something sets it.
- **An aimed heal keeps its target.** A single-part heal that names a part (`SingleTarget` with a
  `BodyPartTargetRule`) goes where it's aimed; the priority only decides where an unaimed heal goes.
- Custom with nothing selected behaves as Lowest First.

#### Organize blueprints, and replace testing composites with long-term ones

`Game/Blueprints` grew by feature rather than by design, and several of its blueprints exist only to
exercise a mechanic on the test map.

**Organization.**
- The infrastructure (`BlueprintDefinition`, `BlueprintRegistry`, `ResolvedBlueprint`, the appearance
  types, `BlueprintContext`, `BlueprintsModule`) sits in the folder root beside content (`PlayerKit`)
  and build helpers (`DisplayNameCache`, `StartingCurrencyGrant`). Separate infrastructure from content.
- Content folders mix kinds. `Composites/` holds real blueprints (`Player`, `GoblinEngineer`) next to
  test-map fixtures. `Parts/` holds a real trait (`Boss`), occupancy parts (`Tiny`, `Phasing`) and a
  temporary one (`LongDescriptionPart`). `NPCs/Generic/` holds only the test dummy, and
  `NPCs/TemporaryNpcLootGrant` is a helper, not a blueprint. `Objects/` mixes whole entities (shops,
  chest) with pieces never spawned alone (`Shop`, the stock parts, `ShopStock`).
- `TestDummyBlueprint` is the only blueprint named with a `Blueprint` suffix.
- Decide the layout once -- for example by what a definition is (race, class, creature, object,
  trait, piece) or by content area -- and apply it everywhere, `BlueprintsModule.BuiltIns` included.

**Testing composites to replace.** Each exists to show a mechanic on the test map, not as content:
- `TinyGoblin`, `PhasingFairy`, `StationaryFairyEngineer`, `GoblinFairy`, `GoblinEngineerTank`: the
  occupancy, stationary, multi-race and multi-class fixtures `TestMapBuilder.BuildFixtureEntities`
  places in the starting neighborhood.
- `LongDescriptionGoblin` / `LongDescriptionPart` (marked TEMPORARY): a description long enough to test
  word wrap.
- `GoblinForeman` and `Boss`: added to prove composites of composites.
- The test dummy (TEMPORARY in `FloorBuilder`), `TemporaryNpcLootGrant`, and the temporary grants in
  `PlayerKit` (test wand, test potions, permanent test modifiers).

For each, either design the long-term version it stands in for (real hybrid races, real bosses and
elites, real small creatures, real starting kits), or delete it once nothing needs it. Keep a fixture
only where a unit test needs it, and move that one into the test project rather than shipping it in
`BlueprintsModule`. Coordinate with "Real map generation -- neighborhood templates, replacing
TestMapBuilder", which removes the fixtures' placement, and with the Admin Mode "Spawn here >" /
"Apply >" menus, which list every registered blueprint and replace most of the fixtures' reason to be
on the map.

#### Neighborhood interactions -- Outbound and Inbound

Only the middle neighborhood is simulated; the 8 around it (Borough)
are loaded but frozen, nothing may target across the simulated/frozen boundary, and an entity
promoted out of Borough catches up its active timers. So an interaction between two neighborhoods
(the canonical case: goblins raiding fairies) can never be simulated live on both sides at once.
It runs in one of two modes, named by direction relative to the **active** neighborhood:

**Outbound -- initiated by the active neighborhood, resolved abstractly.** Example: the active
goblin neighborhood raids the frozen fairy neighborhood.

1. The goblins' interaction template (or later, faction AI) launches a raid party of real goblin
   entities. They path to a crossing point on the shared edge and walk across it.
2. Each raider freezes on arrival, per the normal Borough rule. Because nothing targets across the
   boundary, there's no half-live skirmish at the border first. The party is recorded on the
   neighborhood-record layer: members, target neighborhood, crossing point, departure frame, and
   return frame (departure + the template's raid duration).
3. At the return frame the raid is **resolved abstractly** from the party's strength against the
   defenders: damage per raider, which raiders died, loot taken. The fairy side takes the matching
   losses on concrete frozen entities (casualties chosen nearest the crossing point become corpses;
   loot is removed from their inventories and containers). Writing to frozen entities is fine, and
   it is smeared across frames like any other bulk change.
4. Survivors are moved back across the border at the crossing point with their damage and loot
   applied (a promotion, so catch-up runs). Dead raiders stay on the far side as corpses.

- **Calibrate so an abstract raid is never better than the same raid fought live.** X4: Foundations'
  out-of-sector combat outperformed in-sector combat, and players learned to look away to win (see
  the plan, section 2). Use the real damage formulas at a pessimistic hit rate, and tune against
  headless Inbound battles of the same composition.
- **The target becomes active mid-raid** (the window shifts): cancel the pending abstract
  result. Raiders thaw where they stand, with catch-up, and the raid continues live as ordinary
  combat.
- **The player's Local bubble reaches the frozen raiders** (standing near the border): same as
  above for the raiders and defenders inside Local. The raid goes live there.
- **The origin freezes while raiders are away:** resolve when the origin next becomes active,
  with the result as of the return frame. It's closed form, so waiting costs nothing.
- **The target is unloaded (Beyond) mid-raid:** resolve immediately and return the survivors
  early.

**Inbound -- initiated by a frozen neighborhood, fully simulated.** Example: a frozen goblin
neighborhood raids the active fairy neighborhood.

1. The frozen neighborhood's record carries its own schedule. That is a timer on the record, not on
   any entity: a handful of records, so it's cheap to keep ticking while every entity is frozen.
2. When it fires, real goblins are chosen from the frozen neighborhood, so population stays
   consistent. They are promoted (catch-up) and placed at crossing points **on the active side of
   the border**, a few per frame so they arrive in waves rather than in one frame.
3. They go through full simulation: combat, looting, everything the active neighborhood normally
   runs.
4. On a template-defined retreat condition (loot carried, casualties taken, or elapsed time) they path
   back across the border and freeze on arrival, per the normal Borough rule.

- **Pursuit stops at the border.** Nothing can target across it, so fleeing raiders are safe once
  they cross. Behavior should not chase into frozen territory, since a pursuer that follows just
  freezes.
- **Stranded raiders** that can't get back (blocked, or cut off by the player) stay in the active
  neighborhood as ordinary hostiles and become its members after a timeout.
- **The origin becomes active mid-raid:** nothing special, both sides are simply live.
- **The target freezes mid-raid** (the player leaves): raiders freeze where they stand, like
  everything else, and resume when it's next active.
- **The origin is unloaded mid-raid:** raiders re-home to the neighborhood they're standing in.
  Deletion follows current position, so they are not deleted with their origin.

Shared notes:

- **Crossing points** come from the layout templates' edge constraints (corridors that line up), so
  the interaction template and layout agree on where a border can be crossed.
- **Cost:** an Outbound raid costs nothing while away (frozen members plus one record timer). An
  Inbound raid adds its raiders to the already-budgeted active neighborhood. Only the placement and
  return steps need smearing.
- **Raid state lives on the neighborhood record,** so it's serialized with the record when Beyond
  neighborhoods start being saved.
- Depends on frozen Borough and catch-up (plan phase 6) and on real map generation above.

#### Borough end-of-day summaries

The four processing tiers each get a distinct job: Local is fully simulated, Neighborhood is
simulated slowly, Borough is **updated by periodic mathematical results**, and Beyond is frozen.
Borough entities are never visited by systems. Instead, at the end of each in-game day, every
Borough neighborhood gets an estimated outcome for what happened there, applied to its frozen
entities and records. Examples: the result of a raid between two Borough neighborhoods, or how many
crawlers died during a boss fight.

- **Estimate from the neighborhood, not by replaying entities:** aggregate strength, population and
  scheduled events on the neighborhood record, resolved in closed form. Apply the results to
  concrete entities (casualties become corpses, loot moves) through `NeighborhoodMembershipIndex`,
  smeared across frames like any other bulk change.
- **Run after a time buffer.** Borough <-> Beyond tier changes drain last after a window shift, so
  each summary waits long enough for every entity's tier to have settled before it reads them.
- **Share the calibration rule of the Outbound raid above:** an estimate is never better than the
  same event fought live.
- **Neighborhoods that change tier mid-day:** decide whether a neighborhood promoted to Neighborhood
  before the day ends gets a partial-day estimate, and whether one demoted to Beyond keeps its
  pending one.
- Depends on "In-game day/time tracking". The Outbound raid's abstract resolution above is one case
  of this; the "Unsimulated-tier time" item decides what Beyond does.

#### AdvancedDodge buff

A buff/upgrade that increases Dodge's movement distance beyond one adjacent tile and extends the
duration of its dodging-immunity window. Follow-up to Combat Overhaul: Dodge (this file's own entry
above).

#### Repair destroyed items

Item-type-specific Repair skills to restore a destroyed item -- blocked on Destroyed items (above) and
the not-yet-existing Skills system (this file's own Skills entry).

#### Item damage and repair

A new per-stack condition/durability concept, distinct from Destroyed items above (a damaged item
stays usable, just worth less and eventually repairable, rather than a binary destroyed/not state).
`ItemDefinition.GoldValue`/`ShopActions.ComputeBuyPrice`/`ComputeSellPrice` would
need a per-stack damage modifier applied on top of the flat catalog value -- necessarily per-stack,
not per-`ItemDefinition`, same split Item weight below already follows for a different field. Repair
likely wants to be the same Repair skill the entry above already wants, generalized to cover
"damaged" as well as "destroyed" rather than two independent mechanics.
An item with `ItemDefinition.CanTrade` false (a loot box) never takes damage.

#### Trapped containers

Containers can be trapped. Needs a trap-effect concept triggered on
interaction/loot.

#### Trap detection and disarming skills

Skills-system consumer (blocked on Skills, same as Repair destroyed items above) for detecting/
disarming Trapped containers.

#### Show runner race

Randomly selected; affects UI appearance and biases quest/enemy selection.

#### Teleport countdown with a window anchor

**Depends on a teleport visual effect** (a charge-up or dissolve the countdown plays over), which
doesn't exist yet; no countdown without something to watch during it.

A teleport into a Borough neighborhood lands the player in a frozen neighborhood that takes ~9.5 s to
thaw (skeleton builds at `ProcessingTierSystem.DefaultTransitionsPerFrame`, 256 per frame, held until
the evicted neighborhoods' built creatures are destroyed), while loading the new ring and the thaw cost
frame rate. Hide both with a countdown before the move, during which the window already sits on the
destination:

1. **Countdown start:** anchor the window on the destination neighborhood
   (`ProcessingTierResolver.ShiftWindowTo`). The streamer evicts and loads, and the destination
   thaws, while the player is still at the origin. The origin's Local circle stays live because Local
   follows the player, not the window.
2. **Window anchor:** while anchored, `ProcessingTierSystem` must not recompute the centre from the
   player's position (`NextWindowCenter`), or the player's next step during the countdown shifts it
   straight back. Released when the teleport completes or is cancelled; a cancel shifts the window
   back to the player's neighborhood.
3. **Countdown end:** teleport through the shared teleport path. Only the Local square walks remain
   (~10 ms measured in Release).

Open: countdown length (the simulation settles ~568 frames, 9.5 s, after a shift; the
countdown could instead end on `ProcessingTierResolver.Transitions.HasPendingSimulatedChanges` going
false, with a minimum), whether the player can act or move during it, what cancels it (damage, stagger, moving), and whether
it scales with distance. Teleports outside the loaded window are "Long-range teleports" below.

#### Long-range teleports -- pause and reload the map

World streaming relies on the one-neighborhood buffer between the player and Beyond to load
and save neighborhoods asynchronously, since walking 1024+ tiles takes far longer than a load. A
late-game teleport can land outside the loaded 3x3 window, where the buffer gives no protection.
Handle it as a special case: pause the game, unload (later: save) the current window, generate or
load the 3x3 around the destination, then resume. It reuses the window's load/unload path rather
than adding a second one.

#### EndOfLevelStairs (Game)

A one-way teleport from one floor to the next -- never back up, and never between MapLayers
(UnderGround, Ground and Flying are all the same floor). Floors are entirely separate maps: nothing on
one interacts with another except the entities carried through. When the player takes them, the carried
entities are unplaced (`TransformComponent.UnplacedOn`), everything else is destroyed through the normal
`EntityDestroying` cleanup, and the next floor's map is built and the carried entities placed onto it.
First real consumer of `FloorEnteredEvent`. Open: modules capture `IMapQuery` at `Configure`, so either
`World` swaps its `Map` in place or modules are reconfigured per floor; and which MapLayer the stairs sit
on (likely Ground, so flyers land and diggers surface to use them). See the matching Presentation item
for visuals.

#### NPCs avoid EndOfLevelStairs, and are destroyed entering one

NPCs can never use EndOfLevelStairs. NPC movement avoids a staircase tile, but an NPC that ends up on one
anyway -- lured, or pushed there by forced movement (see Entity displacement with damage) -- is instantly
destroyed rather than blocked. Getting a monster onto the stairs is a known crawler tactic from the source
material, so avoidance is a movement preference that forced movement overrides, never a hard block.
Check it where movement resolves (every placement or move onto the tile, at any processing tier), not in
each NPC behavior. Companions and followers are the exception -- see the next item. Open: whether a
multi-tile footprint overlapping the stairs counts as entering; whether destruction leaves a corpse, loot,
or kill credit.

#### Companions and followers travel through EndOfLevelStairs

A standalone feature, larger than the stairs themselves. Unlike every other NPC, companions and followers
can use EndOfLevelStairs. No companion/follower concept exists yet. Needs:
- Temporary NPC storage: an NPC that takes the stairs before or after the player is held off-map with its
  exact state and restored on the next floor. See Entity storage -- suspend an entity from processing
  without per-system checks (Global).
- Timers: how long a stored NPC waits and what happens when that runs out, as `FrameDeadline`s -- and
  against which clock while no floor holds it.
- Unlock conditions: what makes an NPC eligible to follow through the stairs at all.

#### MapLayer interaction (overview)

UnderGround, Ground and Flying are three layers of one floor (floors themselves are separate maps -- see
EndOfLevelStairs (Game)). Today the layers are sealed from each other: nothing changes an entity's Z, and
movement, target shapes, auras and the Local tier all stay on one layer. Wanted: flyers and diggers
(shorthand for UnderGround entities) are privileged -- they reach Ground more easily than Ground reaches
them -- but for playability they mostly come to Ground to interact, with specific exceptions (dropping
items from the air, earthquakes). The player changes layer only through rare dedicated abilities (a
flying race, a burrow spell). How flyers appear to a Ground player is still undecided.

Connected items, in dependency order:
1. Local processing tier across every MapLayer
2. MapLayer reach rules for actions
3. Layer-change actions -- Land, Take Off, Surface, Burrow
4. Auras and terrain effects per MapLayer
5. Corpses across MapLayers
6. Digger detection (Game), then Digger detection (Presentation)

#### Local processing tier across every MapLayer

Part of MapLayer interaction (overview); comes first, since every cross-layer attack depends on it.
`ProcessingTierResolver.ComputeTier` grants Local only on the player's own MapLayer, so a flyer directly
overhead or a digger directly below runs at Neighborhood tier while attacking the player. Local should
cover every layer of the floor. `ProcessingTierSystem.RetierSquare` retiers only the player's Z, and a
player layer change retiers the old and new layers separately -- both change, and the layer-change retier
mostly goes away. At current spawn rates (Ground 3%, UnderGround 2%, Flying 2%) the Local population
roughly doubles, so benchmark before and after (phase-performance-testing skill).

#### MapLayer reach rules for actions

Part of MapLayer interaction (overview). Every action declares the exact MapLayers it can affect from each
caster layer -- not relative "above/below", which would let a digger's ranged attack reach Ground the same
way a Ground ranged attack reaches Flying. Starting proposal:

| Caster / Target | Flying | Ground | UnderGround |
|---|---|---|---|
| Flying | all | tagged only (drop from air, some ranged) | none |
| Ground | ranged only | all | tagged only (bombs) |
| UnderGround | none | tagged only (earthquake) | all |

Touch points: `TargetShapeResolver` (Engine) builds every shape on the origin's Z;
`ActionTargetingController` builds hovered/clicked tiles on the player's Z; `TestCombatBehaviorSystem`
must only pick targets it can reach. An action refused for reach gets clear feedback (disabled cursor),
never a silent no-op. An undetected digger is never a valid target (see Digger detection (Game)).
Related: every effect amount already runs Outgoing and Incoming stat modifiers (`EffectModifiers`).

#### Layer-change actions -- Land, Take Off, Surface, Burrow

Part of MapLayer interaction (overview). Land/Take Off (Flying <-> Ground) and Surface/Burrow
(UnderGround <-> Ground) as ordinary actions with an action lock, so the transition is a window where
Ground entities can hit back. Movement never changes Z (`MovementCandidates` keeps it), so these are the
only path. Refused with feedback when any tile of the destination footprint can't be occupied -- reuse
`MovementCandidates.CanOccupyCell` against the destination Z (Blocking occupant, structure,
movement-blocking terrain). `World.MoveEntity` takes a full `Vector3Int`; verify a Z-only move keeps the
occupant index, the per-column occupied-layer mask and `NeighborhoodMembershipIndex` (keyed by Z) in
sync. NPCs get them by race (Fairy and Ghost populate Flying and UnderGround today); the player only
through rare dedicated abilities (a flying race, a burrow spell).

#### Auras and terrain effects per MapLayer

Part of MapLayer interaction (overview). `AuraField` spreads every aura on its centre's Z
only, and terrain auras (`TerrainAuraSources`) skip Flying, which has no floor. Decide per effect which
layers it reaches, with the same exact-layers rule as MapLayer reach rules for actions: a Ground fire
aura probably doesn't burn a flyer overhead, while an earthquake is exactly an UnderGround effect reaching
Ground.

#### Corpses across MapLayers

Part of MapLayer interaction (overview).
- A flyer's corpse falls to Ground. `DeathSystem` converts a dead entity to NonBlocking where it stands, so
  the fall is a Z move after that; a NonBlocking corpse can share a tile with a Ground Blocking occupant.
  Open: a wall or lava below, a multi-tile footprint partly over a wall, and whether the fall damages what
  it lands on (see Entity displacement with damage).
- A digger's corpse stays UnderGround, lootable only while the looter is UnderGround. Bug today:
  `MapWindow.IsAdjacentToPlayer` (Loot and Shop) uses `GridDistance.ChebyshevDistance`, which ignores Z,
  so a Ground player standing over an UnderGround corpse can loot it. Needs a same-layer check for every
  corpse and shop, not just diggers.

#### Digger detection (Game)

Part of MapLayer interaction (overview). Diggers are hidden from the player until detected, through
future skills and actions (none exist yet -- see Skills), at two levels: low (something is there) and
high (what it is). Hidden means hidden everywhere, not just undrawn: inspection and selection, tooltips,
targeting (never a valid target), and the per-column occupied-layer mask behind
`MapWindow.DrawLayerBadges`, which today would give a digger away with a `v` badge. Needs a per-digger
detection level against the player, and when it lapses (a deadline, per the `FrameDeadline`
convention). Open: player-only or NPCs too; whether surfacing reveals a digger. See Digger detection
(Presentation).

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
specific limb yet). Once Equipment exists, `IActionActivator`/`Effect` should let a melee action
declare which `BodyPartType`(s) perform it (a two-handed weapon needing both Hands; an offhand punch
caring about one arm) -- `BodyPartEffectsSystem` would then key its penalty off the acting part(s), not
a blanket aggregate.

The same declaration replaces `ActivationQueries.GetBlocker`'s hard-wired "`Delivery.Melee` + every
Arm/Hand disabled" rule (IMPLEMENTATION-NOTES.md "Disabled actions"): an action is blocked when the parts
it needs are, and `ActivationBlockerText`'s melee reason ("No usable arms or hands") becomes body-plan
specific, naming the entity's own parts ("Right hand is broken").

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

#### Scroll and spell durations scaling with Intelligence

Duration-based effects (buffs, DoTs) should scale with caster Intelligence, same shape as
Constitution->potion-cooldown. Needs an `Effect` duration field as a real concept first.

#### Damage types

Only Burning (`Damage.Fire`), Poison (`Damage.Poison`), the Fireball wand (`Damage.Fire`) and Magic
Missile (`Damage.Energy`) carry a damage type; every other hit is undifferentiated. Starting set to
add: Blunt, Explosive, Slashing.

Damage types are `Damage.*` gameplay tags (IMPLEMENTATION-NOTES "Gameplay tags"), and follow its
combine-don't-nest rule: `Damage.*` names only what the hit is made of (`Damage.Blunt`,
`Damage.Slashing`, `Damage.Fire`), and magic is the separate top-level `Magic` tag carried alongside --
a fireball is `Damage.Fire` + `Magic`, a torch `Damage.Fire`. So there is no `Damage.Magic`; a
"magic resistance" conditions on `Magic`, a fire resistance on `Damage.Fire`, and a mod adds
`Damage.Void` without touching Game. A grouping that really is always-true can still nest
(`Damage.Fire.Lava`). Resistances and vulnerabilities are stat modifiers conditioned on a tag, applied
in the Incoming pass every effect amount runs (`EffectModifiers`).

**Unreal Engine reference:** Unreal's old `UDamageType` classes (passed to `ApplyDamage`) were one class
per type with no hierarchy or data; GAS projects replaced them with damage-type gameplay tags on the
effect spec, read by the damage execution calculation to look up a matching resistance attribute.
The tag approach is the one to follow.

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
- `MovementSystem` currently checks `_pendingAbilityActivations`/`_pendingItemActivations`
  directly to know a turn's claimed -- doesn't scale as action types grow. Needs a single shared
  "turn claimed" marker any decision system can set/check generically.

Planned in `PLAN-npc-behavior-composition.md`. Behaviors that move need "Grid pathfinding and
navigation" (High) -- today's Engage/Flee steps get stuck on walls -- and "can I see it" needs "Field
of view and perception" (High). "Where to go" (flee to which tile, which shop, which corpse) is
"Spatial queries for NPC decisions" (Medium).

**Unreal Engine reference:** Unreal's decision layers are Behavior Trees (with a Blackboard) and the
newer StateTree; neither is utility-based, and the plan's dual utility stays. What maps across:
- *The "turn claimed" marker* is how GAS gates abilities with tags: an active ability adds tags to its
  owner (`ActivationOwnedTags`), and others list tags that block them (`ActivationBlockedTags`) or that
  they cancel (`CancelAbilitiesWithTag`). "A turn is claimed" is then a tag any system can check,
  rather than a list of queues. The same mechanism would express Stagger locking actions while
  allowing Dodge.
- *StateTree* keeps a small hierarchy of states with enter conditions and transitions, evaluated when
  something changes rather than every tick. It's a model for the plan's "small activity slot later if
  needed" (errands: go to shop, buy, return).
- *Mass AI* (`MassEntity`, Unreal's ECS) runs crowds with LOD processors that update distant agents less
  often -- the same idea as processing tiers, confirming the tier design rather than adding to it.

#### User feedback for actions is missing entirely

Casting, cancelling, AOE/melee landing, status effects applied -- no player-visible feedback beyond the
state change itself. The mechanism is "Gameplay Cues -- presentation feedback raised by gameplay"
(Medium, above): Game raises a tagged cue at each of these points, and Presentation chooses what to
show (flash, floating text, particles, sound). What's still undesigned is the feedback itself, per
event. Floating text already covers damage taken.

#### Corpse decay/destruction and destructible terrain

`DeathSystem` never calls `EntityManager.DestroyEntity` (corpse stays fully populated, non-Blocking, for
future looting). `DestroyEntity` is reserved for a real decay-timer or "loot then destroy" step, or
future destructible terrain (skips `DeadComponent` entirely, just calls `DestroyEntity` on trigger).

#### Self damage buff ability

Example FreeCast/Immediate ability raising the caster's own outgoing damage for a duration.

#### Defensive buff spell -- damage reduction + healing over time

Self-targeted, combining a timed `StatModifierGrant(IncomingDamage, ...)` (fully supported today) with
a periodic self-heal built like Burning/Poison's DoT (`TimerBasedStatusEffectApplier<T>`, healing instead of
damaging). The regen tick can now carry `IncomingHealing`/`OutgoingHealing` through the chain the same
way DirectHeal does (`HealthHeal.ComputeAmount`).

#### Aura follow-ups

Left over from the healing aura work (`IMPLEMENTATION-NOTES.md`, "Auras, terrain contact, Healing
Shrine, Holy Ground").

- **Worst frame about 2 ms higher with random shrines** in the steady headless benchmark (6.5-8.8 ms
  against 4.6-6.0 ms), not GC frames. Cause not found. Looked at again 2026-10-02 (Release, headless,
  gauges): 7.1-10.2 ms. Of the ten slowest frames, seven are gen-0/gen-1 collections (2-6 ms
  pause each); the rest, including the slowest, moved no gauge. Ruled out: the streamer and tier
  transitions (no activity in the range) and the aura source-move scans (the worst frame is the same
  with the changed-sides scan). Which system's row those frames land in is not recorded per frame.
- **`DeathSystem` up about 50%** (0.0037 to 0.0057 ms/frame) in the final A/B; 0.0071 on 2026-10-02.
  Ruled out: the removal scan a dying source pays (per-source reach and the changed-sides scan left it
  at 0.0069) and the death count (corpses at the end of the range are 6% up on the pre-aura build,
  7,693 against 7,252). Not looked at: what `OnEntityDied` does per death since the aura work.

#### BodyPartType categorization -- lifting/pickup still open

Movement/melee consumption landed (`IMPLEMENTATION-NOTES.md`). Still open: `InventoryActions` pickup
gating on a disabled Arm/Hand, and carry capacity/lifting (blocked on Strength/carry-capacity infra
above). `BodyPartType.Wing` exists but isn't granted to any race yet.

#### Per-body-part vs whole-entity status effects

`StatusEffectStack`/`StatusEffectApplierRegistry` apply every effect entity-wide today -- correct
for Poison (systemic), wrong for Burning on a Complex entity (a burning leg reads better, and ties to
targeted-damage above: lava burning legs should apply Burning to the legs specifically). Needs a
part-scoped vs. entity-scoped declaration on `StatusEffectGrant`/`IStatusEffectApplier`, and a new
store keyed by (entityId, bodyPartId) for the part-scoped case. Feeds the HealthWindow item
(Presentation).

#### Movement System

`SeekTarget` movement mode. Needs "Grid pathfinding and navigation" (High): seeking a target that
isn't in a straight line needs a path, plus a path-following step that repaths when blocked or when
the target moves (Unreal separates these as `UPathFollowingComponent` and the navigation query).

#### Loot boxes can only be opened in safe rooms

Refuse loot box opening (Activate/double-click, see IMPLEMENTATION-NOTES.md "Loot boxes") unless the player is standing
in a Safe Room, with clear feedback (disabled "Activate" with a reason) rather than a silent no-op.
Blocked on first creating Safe Rooms and zones -- no zone concept exists yet.

#### Lootbox drop tables

Every loot box currently drops a single stack of 1-10 of one item picked uniformly from the whole item
catalog, regardless of type or rarity (the placeholder `RandomSingleStackContents`, see IMPLEMENTATION-NOTES.md "Loot boxes").
Replace it with real drop tables: type decides which items can appear (Alchemist -> potions, Weapon ->
weapons, ...), rarity decides their value (e.g. a Gold value budget per rarity), and a box can be either
set contents (specific rewards) or a random pull from its table. Needs higher-value items to exist
before rarities above Gold mean anything -- today every item is worth 1-20 Gold.

Author the tables as data ("Data tables for content", Medium): one row per entry (item Guid, weight,
stack range, rarity), a table per box type, and mods adding rows or overriding a table by key. Unreal
projects typically do drop tables as a `UDataTable` of weighted rows, with a `UCompositeDataTable` to
layer expansion or mod rows over the base table.

#### Advanced boss loot box awards

A boss currently grants at most one box, and only when the player lands the killing blow
(`BossLootboxAwarder`, see IMPLEMENTATION-NOTES.md "Loot boxes"). Award boxes per contribution instead -- different
boxes for landing the killing blow, dealing the most damage, starting the fight, etc. -- so one fight
can grant the player several. Needs per-fight damage/participation tracking; shares that need with
"Corpse looting rights based on damage dealt". Also decide how an active quest's own award for killing a
specific boss (a `LootboxReward` with a content override, e.g. an item thematic to that boss and quest)
combines with the boss's own box -- replaces it or adds to it.

#### NPC component

No direct "is this an NPC" marker -- inferred indirectly today (exclude `PlayerEntityId`, or a specific
race check). Raised by `TemporaryNpcLootGrant`, which targets Goblin/Fairy/Ghost individually.

#### In-game day/time tracking

`DeadComponent.DiedAtFrame` only shows a raw frame tick in the corpse summary. A real calendar/clock
would make that (and anything else wanting a timestamp) human-readable. Also unlocks the Crawler TV show
item below (time-gated interactions).

#### Restock shops on the day/night swap

Follow-up to In-game day/time tracking above, which this is blocked on -- no real "day/night" concept
exists yet to swap on. Once one does, reroll a shop's stock (`ShopStock.GrantRandomStock`) on the transition, and reset its own Gold back toward its starting amount so a
shop the player has drained doesn't stay unable to buy anything forever. Today a shop's stock is
rolled once at spawn and never refreshes.

#### Preferred stock for items added to shops

`ShopStockPreferenceComponent`/`EnsurePreferredStockLevel` is
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
yet).

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

#### Boundary-aware ProcessingTierSystem recompute

`ProcessingTierSystem` recomputes every movement-capable entity's tier once per its own stripe turn
regardless of whether anything changed. Targeted alternative: a coarse spatial grid (cf. `AuraGrid`) so
a player move only re-tiers entities in the thin band straddling the Local-radius ring. Real structural
addition (new spatial index, insert/remove/move bookkeeping, a genuine correctness surface around
boundary-band width) -- only worth it once profiling actually confirms this as a bottleneck (one past
pass was inconclusive, coincided with unrelated Paralysis load).

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

#### Read the player's effective actions, not the bare catalog

`HotbarContent` (slot drawing, `TryGetSlotSummary`) and `ActionTargetingController` (the toggle check,
the Self-targeting check, arming, the targeting preview) look the player's actions up with
`actionCatalog.TryGet(actionId, ...)` -- the catalog entry, not the action the player actually has. An
override (`ActionInstanceComponent.Override`, read through `EntityActions.TryGetEffectiveAction`) can
change targeting, timing, glyph, tags or effects, so these can disagree with what Game does: the player's
Magic Missile already has one (damage only, so nothing shows yet), and Fairy's changes timing and range.
- Add a view method returning the player's effective `ActionDefinition` (on `ActionStateView` or
  `HotkeyBindingView`; Presentation can't touch the store) and use it at every one of those sites.
- The same drift was fixed for windups by `ActivatableLookup` (`MapViewQuery`'s charging badge read the
  catalog action; the windup telegraph read the catalog action's tags) -- see CLAUDE.md's
  `ActivatableReference` bullet.
- A UI change: check it in game.

#### Item Details for toggle items: show what the toggle holds

Item Details (`ItemDetailsWindow`) describes a toggle item (`ToggleItemActivator`, e.g. Toxic Idol)
as if it were a self-targeted activation. Fix:
- **Effects** are the effects held while the toggle is on (`ItemDefinition.Effects`); say so.
- **Shape**: show the shape of the toggled effect -- an aura's diamond of its size -- instead of the
  activation's "Self" shape preview.
- **Activation** still shows, as it carries the turn-on costs and timing (`ToggleSpec.ActivationEffects`,
  the windup) and the upkeep (`ToggleSpec.Periodic`).
- **Aura size and power** are separate lines on the effect, not folded into its text: today's
  "Grants a Poison aura (size 4, power 16)" (`EffectFormatting.FormatAuraSourceGrant`) and the
  Summary's "(range 4)" lose them. The Item Details comparison already keys them separately
  (`ItemComparisonStatExtraction`, `effect:aura:{id}:size` and `:power`).

#### Hotbar re-press always activates, even past the double-tap window

Pressing the key of an already-armed hotbar slot should activate the item/action whether or not the
second press lands inside the double-tap window (`ActionTargetingController.DoubleTapWindowFrames`).
Today the two cases differ (`HandleActionSlotPress`/`HandleItemSlotPress`):
- **Within the window**: an action fires at an auto-picked target (`TryActivateWithAutoTarget`); a
  `GameTags.TargetingSelf` item fires on the player (`TryActivateItemOnSelf`).
- **Past the window**: the press only confirms against `MapViewState.HoveredTile`
  (`TryConfirmActivationAtTile`), so it does nothing when the cursor is off the map or over a tile
  that isn't targetable. Only a `TargetingSelf` action is exempt; a `TargetingSelf` item is not.

Make the slow re-press activate too: confirm at the cursor when it is over a valid target, otherwise
fall back to what the double-tap does (auto-target for an action, self for a `TargetingSelf` item).
Decide what a slow re-press of a non-self item does with no valid cursor target, since the double-tap
has no auto-target path for items either. Keep one rule for actions and items, and keep cancel on
right-click/Escape only.

#### Bug: open achievement popups fall behind the pause mask when a menu window opens

Whenever a menu window opens, achievement popups that were already open stop being active and are
drawn behind the pause window mask, so they can't be read, closed or minimized until menu mode ends.
Popups opened after the menu window is already open work correctly: `UiLayerStack.Add` promotes
anything added during menu mode into the open menu-window set, but nothing does the same for popups
that were already open when `OpenMenuWindow` ran. Decide whether already-open notification popups join
menu mode (the way later ones do) or stay usable above the mask as menu-mode-exempt, and fix it in
`UiLayerStack`/`NotificationCenter` rather than per menu window.

#### Global hard minimum/maximum element sizes, and horizontally scrolling title text

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

Title-style text that runs too long should grow its window up to that maximum and then scroll
horizontally within its line, rather than word-wrapping into the space below. Two live cases:
- **Corpse summary lines** (`SecondaryInventoryWindow.BuildSummary`): a long "Slain by: ..." line
  word-wraps and overlaps the "Died at tick" line under it, since each line is a fixed
  `SummaryLineHeight` tall.
- **Item Details name** (`ItemDetailsWindow`'s name row): a long item name word-wraps and overlaps the
  rows below it. The window should widen as needed, up to its maximum size, and only then make the name
  scroll.

Solve it once, as a single-line text mode on `TextWindow` (or a title element) that measures its text,
asks its host to grow within the global/per-element maximum, and scrolls horizontally past it -- not a
per-window fix for each case.

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

Per-category tabs landed (one per `Item.*` tag, IMPLEMENTATION-NOTES "Inventory tabs"). Still open:
user-reordering the default sort, and a trailing "+" tab for custom user-created tags -- a user tag
would be a player-owned grouping, not a `GameplayTag`, since those are declared by modules at startup.

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
end-state UI. Build this before lifting that exemption. Game over is one state of "Game flow -- start
menu, session states, floor transitions" (Game, Medium): the simulation stops, the map stays drawn
behind the screen, and "new run" goes through the same session teardown and rebuild as a floor change.

#### TextBox context menu wiring, and "Bind To..." sub-menu

Context menu mechanism + AdvancedMapContextMenu landed (`IMPLEMENTATION-NOTES.md`). Still open:
TextBox's Cut/Copy/Paste/Select All (currently keyboard-only) via the same `ContextMenu` mechanism; and
an inventory item's "Bind To..." (hotbar slot picker) -- needs a genuinely new capability, cascading
sub-menus (an option carrying a nested option list, opened east of the clicked row, a second managed
`ContextMenu` instance, and updating the outside-click check to cover both popups). Neither of
AdvancedMapContextMenu's five landed menus needed this.

#### Player stats v1

Persisted view of the player's active stats -- fixed set.

#### Standard widget set

No checkbox, radio button, dropdown, slider, list box, or tree view (`Toggle` covers checkbox --
`IMPLEMENTATION-NOTES.md`). Tabs exist (`TabbedContent`). Inventory/spell hotbar and equipment/stats
windows still want list/grid controls beyond what exists.

#### Tile count and size closer to Dungeon Settlers

Inspired by Dungeon Settlers. Match its on-screen tile size and how many tiles are visible at once
more closely. Today `MapCamera.BaseTileSizePixels` is a fixed 36px at `ZoomLevel.Team` (18px
Neighborhood, 9px Borough, all derived from it), and the visible tile count isn't set directly --
`UpdateTileSizes` derives it from the map window's content size divided by tile size (+2 for partial
edge tiles). So "tile count" is a function of tile size and the map window's layout in
`ShellBootstrapper`, and both may need to change. Start by measuring Dungeon Settlers' visible
columns x rows and tile pixel size at a common resolution, then decide whether to match size, count,
or both. Things that scale with tile size and need a visual check afterward: sprite legibility (pairs
with AI-generated sprites and "SpriteDefinition" sprite scale), map fonts/badges, `HudMetrics`, and
per-frame cost -- larger tiles means fewer visible tiles and a cheaper `DrawOccupants`; smaller means
the reverse.

#### Walking and action animations

Inspired by Dungeon Settlers. Supersedes the former Medium "Lerp movement animation between tiles"
entry. Two parts:
- **Walking**: entities teleport between tiles on-screen today. Lerp the sprite's rendered position
  across the move's `ActionLockComponent`-driven frame count, plus a walk-cycle frame sequence where
  a sprite has one. Purely visual -- grid position/occupancy still change instantly on the same
  frame as today.
- **Actions**: a short per-action animation (windup during a Delayed action's charge, a strike on
  activation). Could drive off `IMapViewQuery.GetChargeFraction`, which the charge fill already
  reads.

Needs a frame-sequence concept `SpriteComponent`/`SpriteManifest` don't have yet (one cell per
entity today, chosen once at build). Scope to Local tier -- nothing off-screen should pay for
animation state. Pairs with the AI-generated sprite item (Medium, below), which would be the natural
point to author walk/action frames. The frame sequence and the position lerp are "Animation
primitives -- flipbooks, tweens and curves" (Medium, below).

**Unreal Engine reference:** Paper2D's `UPaperFlipbook` is a list of sprite keyframes, each held for a
number of frames, played at a frames-per-second rate by a `UPaperFlipbookComponent` that can loop, play
once or be scrubbed to a position -- the "drive the windup off the charge fraction" idea is scrubbing
(`SetPlaybackPositionInFrames`). A character switches flipbooks by state (idle, walk, attack); Paper2D
leaves that switch to game code, and PaperZD (a popular plugin) adds an animation state machine for
2D. Paper2D's own flipbook-per-direction convention (one flipbook each for up/down/left/right) is the
usual answer for facing.

**Bevy reference:** the walking lerp is where Bevy's fixed timestep matters. Simulation runs in
`FixedUpdate` at a set rate while rendering runs every display frame, and `Time<Fixed>::overstep_fraction()`
says how far the renderer is between two simulation steps; drawing a moving sprite at
`lerp(previous, current, overstep)` makes movement smooth on a 144 Hz display while the simulation
stays at 60. Here the simulation and rendering share FNA's fixed step, so this only matters if the
two are ever separated -- but keeping "previous position" and "current position" as the inputs to the
walking lerp keeps that door open. Sprite-sheet animation in Bevy is a `TextureAtlas` index advanced by
game code (an example, not a built-in); `bevy_animation`'s curves and animation graphs target 3D.

#### SpriteDefinition: tint, scale and multi-tile footprint on the sprite itself

A sprite is only a name today: whatever draws it decides its tint, size and footprint. Introduce a
`SpriteDefinition` (the natural home is the data-driven `Content/SpriteManifest.json` /
`SpriteManifest`) that carries everything about how a sprite draws, so it's self-contained and works
wherever the sprite is used -- items, entities, terrain, UI icons -- rather than being re-declared by
each kind of thing that shows one.

- **Tint**: move `ItemDefinition.SpriteTint` off the item and onto the sprite. It's on the item today
  only because loot boxes needed one (each rarity tints the chest sprite its color, see
  IMPLEMENTATION-NOTES.md "Loot boxes"), and it only works for items: `InventoryItemStackCell` and
  `ItemIconElement` read it, nothing else does. A tinted variant becomes its own sprite definition
  (e.g. "Chest-Bronze" pointing at the chest cells with a bronze tint), and `LootboxCatalog` names it
  instead of setting a tint. Remove `SpriteTint` from `ItemDefinition` and `AreEquivalentOverrides`.
- **Scale** (was "Per-entity sprite scale"): `SpriteRenderer.Draw` always stretches to fill the tile
  footprint exactly -- wrong for character sprites (confirmed in-game: player needs to render larger,
  goblins smaller). A scale factor on the sprite definition, applied in `MapWindow.TryDrawEntityVisual`
  and every other sprite draw.
- **Multi-tile footprint** (was "Multi-tile sprites"): no sprite spans more than one tile today --
  `TransformComponent.Size` already carries a footprint (a corpse/tiny-entity grid already reasons
  about it), but `MapWindow`'s draw path always renders one sprite stretched to exactly one tile's own
  `CurrentTileSize`, never a single sprite spanning the whole footprint. `Shop`'s `"Shop-1x1"` is a
  deliberately-named 1x1 placeholder; a real "Shop-2x2" is the concrete first implementation.

Every draw site (`SpriteOrGlyphRenderer`, `MapWindow`, the inventory/hotbar/details icons, the drag
ghost) then reads the definition instead of taking tint/size as separate parameters. Pairs with
"Sprites taller than one tile" just below, which needs the same footprint/scale information.

#### Sprites taller than one tile, and two-tile walls (front + top)

Inspired by Dungeon Settlers. Two related wants: character sprites that extend above their own tile,
and walls drawn as a front face on their own tile plus a top face on the tile above. Both need the
map drawn top-down (row-outer), so a lower row's sprite overlaps the row above it rather than the
other way round.

Two things in `MapWindow` stand in the way today:
- `DrawOccupants` walks column-outer, row-inner. Within a column that's already top-down, but column
  c+1's row r-1 draws after column c's row r, so anything wider than one tile (see the SpriteDefinition
  item's scale, above) gets overdrawn by its upper-right neighbour. Its own remarks say row-major measured no
  performance difference and was only left alone because it would change overlap order for no gain.
  This is that gain.
- Walls are terrain (`Map`'s separate `TerrainLayer` array), rendered into the `MapTileLayerCache`
  texture before any occupant. A wall's top face covering the tile above has to occlude an entity
  standing behind it (north), so wall tops can't live in that cache -- they need to draw in the
  row-ordered occupant pass, after the row above. That cache's remarks explain why terrain was pulled
  out of the per-tile loop in the first place (multi-tile footprints), so this needs a real design,
  not just moving the wall draw.

Tall terrain goes semi-transparent while the player or the inspected entity is behind it: when
either stands on a tile that a wall's top face (or any other tall terrain sprite) draws over, render
that sprite at reduced alpha so the entity stays visible. "Inspected" is
`MapViewState.InspectedEntityId` (Detail/Admin inspection's followed entity, the same one
`DrawFollowedEntityHighlight` tracks), and it should cover every tile of that entity's footprint
(`TransformComponent.Size`), not just its origin tile. The occupant draw path already carries an
alpha multiplier (`TryDrawEntityVisual`'s `alphaMultiplier`, used for Phasing), so this is a
per-tile "is the player or inspected entity under this sprite's overhang" check at draw time, once
tall terrain draws in the occupant pass at all -- at most two entities to check, so no spatial index
needed.

Related: "SpriteDefinition: tint, scale and multi-tile footprint" (High, just above) -- a larger player sprite is the
first real consumer of this.

**Godot reference:** already researched in `PLAN-tall-sprites-and-wall-tops.md`, which cites Godot's
Y-sort (`y_sort_enabled`: siblings draw in order of their origin's Y, so the sprite's origin must be its
feet, not its top-left). The other Godot pieces that apply: `z_index` for things that must break the
row order on purpose (a flying creature's shadow under everything, a spell effect over everything),
and `CanvasLayer` for layers that never sort with the world (the HUD). "Autotiling with terrain sets"
(below) extends the plan's Phase 5 linked wall tops beyond walls.

#### Autotiling with terrain sets

`PLAN-tall-sprites-and-wall-tops.md` Phase 5 joins wall tops with a 4-bit neighbour mask (16
variants, N/E/S/W). Everything else is still one sprite per `TerrainCell` regardless of what's next
to it: a lava pool is a grid of identical squares, and where floor meets water there's no edge. Real
map generation ("Real map generation -- neighborhood templates", Game) will make this much more
visible, since generated shapes are irregular.
- **Terrain sets:** a terrain type (water, lava, moss, dirt) declares a set of variants keyed by which
  neighbours share its terrain. Sides only (16 variants, the Phase 5 case), or corners and sides (the
  47-variant "blob" set, needed for concave corners to look right).
- **Transitions between terrains:** several terrains in one set (floor, dirt, water), with variants
  for where two of them meet, so a water edge is drawn from the water side onto the floor.
- **Chosen when the cell changes, not every frame:** the variant is computed when a neighborhood
  loads (on the worker, in the plan) and recomputed for a cell and its 8 neighbours when a cell
  changes (destructible terrain), then stored in `TerrainCell.Variant` the way random variants are
  today. Drawing stays a lookup, and `MapTileLayerCache` invalidates as it does now.
- **Across neighborhood edges:** a cell on the border needs its neighbour's terrain. Either plan the
  border row with the adjacent record's decided edge (the rolling plan already decides shared
  edges), or re-tile border cells when the neighbour loads.
- **Per-cell data on the flyweight:** movement cost, blocks sight, blocks light, flammable -- as
  fields on the terrain definition rather than code checks by type, read by pathfinding and
  perception. `TerrainDefinition` already carries `BlocksMovement`, `Contact` and `Aura`;
  this is making it the rule for every per-terrain property.
- **Tile animation:** a terrain can declare frames with durations (lava, water, torches on walls),
  advanced by presentation time with a per-cell random start so a pool doesn't pulse in unison.
  Needs `MapTileLayerCache` to redraw animated cells, or to draw them in a separate pass.
- `DevTools/SpriteManifestBuilder` needs an editor for these sets, the way Phase 5 adds linked sets.

**Godot reference:** a Godot `TileSet` has *terrain sets*, each in one of three modes -- match corners
and sides, match corners, or match sides -- containing one or more terrains. Each tile is painted with
*peering bits* saying which terrain it expects at each side and corner, and `set_cells_terrain_connect`
/ `set_cells_terrain_path` pick, for every changed cell and its neighbours, the tile whose bits match.
Several terrains in one set give transitions. TileSets also have *custom data layers* (typed per-tile
fields read with `TileData.get_custom_data`), *physics, navigation and occlusion layers* per tile, and
*animated tiles* (frame columns, per-frame durations, and a random-start mode so neighbours don't
animate in sync). RimWorld's linked atlas, cited in the plan, is the sides-only case of the same idea.

#### Show "Self" as the source when an entity is its own source

A `ActionSource` created from the entity the effect landed on renders as that entity's own name
today -- the player's self-granted modifiers read "Player1" in the Ability Score window's line prefix
and hover title, and in `PlayerActivityLog`'s `source=` field, as though something else did it to them.
It should read "Self" instead. There are real self-sourced modifiers to see this on right now:
`Tank`'s MaximumHealth/HealthRegen bonuses and `PlayerKit`'s multiplicative ability-score seeds
all pass `ActionSource.FromEntity(..., player)`.

The rule is per-viewer, not player-global: the source is "Self" when its entity is the same entity the
line is describing. That way inspecting an NPC's own self-buff reads "Self" too, rather than only ever
special-casing the player. Compare with `ActionSource.IsEntity(key)` against an `EntityKey` --
never an entity id, which is recycled the moment an entity is destroyed (`IPlayerQuery.PlayerEntityKey`
is the player's). That means the describe call needs the subject entity's key threaded in, which it
doesn't take today.

Fold the two copies of the logic into one while doing it: `ModifierDisplayFormatting.DescribeSource`
(`Presentation/UI/ModifierDisplayLine.cs`) and `PlayerActivityLog.DescribeSource` are the same
Entity-vs-`ToString()` switch written twice, and `SecondaryInventoryWindow.ResolveKillerName` is a
third partial copy (the corpse "killed by" line -- suicide/self-kill should read "Self" there as well).
`ActionSource.ToString()` stays as-is: it's diagnostics with no subject to be relative to.

#### Input actions and mapping contexts

Keys are hardcoded where they're handled: around 50 `Keys.*` checks across `UiInputController`,
`PlayerMovementController`, `ActionTargetingController`, `MapWindow` (hotkeys), `HotkeySlotLayout` and
`TextBox`. What a key means in which situation (typing in a text box, a context menu open, targeting,
normal play) is decided by the order of those checks. Several items need one input layer: "Keybindings
page on the options menu", "Targeted key-press routing instead of a full-keyboard scan", "Diagonal
movement input timing", and the input buffer (`PlayerCommands`), which is built by hand for movement
and activations.
- **Input actions:** named, typed actions (`Move` as a 2D vector, `Hotkey3`, `Dodge`, `ToggleInventory`,
  `Cancel`, `ToggleAdminMode`) that code binds to. Code never names a key.
- **Mapping contexts:** sets of key → action bindings with a priority, pushed and popped as the
  situation changes (Gameplay, Targeting, Menu, TextEntry, Admin). A higher-priority context that
  maps a key consumes it, so a focused text box swallows `I` without every window checking focus. The
  focused element or controller pushes its context instead of scanning the keyboard.
- **Triggers:** when an action fires -- pressed, released, held for N frames, tapped (released within
  N frames), double-tapped, a chord (Shift+click) -- declared on the binding, not reimplemented per
  handler. Today's double-tap auto-target and re-press-confirms rules become triggers.
- **Buffering:** `PlayerCommands`'s newest-wins 0.25 s window becomes a property of an action,
  applying to any action that opts in, not just movement and activations.
- **Rebinding:** bindings are data (defaults in `Content/`, user overrides persisted through "Data
  storage" and "Layered config files", Global). The keybindings page edits the user's layer, and
  conflicts are reported per context (the same key in Gameplay and Targeting is fine; twice in
  Gameplay isn't).
- **Mouse and later gamepad** go through the same actions, so a gamepad needs bindings, not new code.

**Unreal Engine reference:** Enhanced Input. `UInputAction` assets have a value type (bool, 1D, 2D,
3D axis). `UInputMappingContext` assets map keys to actions, and are added to or removed from the local
player (`UEnhancedInputLocalPlayerSubsystem::AddMappingContext`) with a priority; a higher-priority
mapping of the same key consumes it by default. Each mapping has *modifiers*, which transform the raw
value (`Negate`, `Swizzle` -- how WASD becomes one 2D `Move` vector -- dead zones, scaling), and
*triggers*, which decide when it fires (`Pressed`, `Released`, `Hold`, `HoldAndRelease`, `Tap`, `Pulse`,
`ChordedAction`, `Combo` with per-step time windows). Handlers bind to trigger events (`Started`,
`Ongoing`, `Triggered`, `Completed`, `Canceled`). Rebinding is `UEnhancedInputUserSettings` with
player-mappable key profiles (UE 5.1+), saved per user, each mapping named so a rebind survives changes
to the default contexts. Unreal has no built-in input buffer; Souls-like Unreal games add one on top of
the `Triggered` events, which is where `PlayerCommands` would sit.

**Bevy reference:** core Bevy input is key-level only (`ButtonInput<KeyCode>` with `pressed` /
`just_pressed` / `just_released`), like FNA's today. The action layer is the third-party
`leafwing-input-manager`: an `Actionlike` enum of actions, an `InputMap` binding keys, chords and
virtual D-pads (four keys → one 2D value) to them, and an `ActionState` component queried for
`just_pressed(Action::Dodge)` and hold durations. Several `InputMap`s can be active on different
entities, and clashes between a chord and its parts (Shift+S vs S) are resolved by a clash strategy.
It's closer in size to what's needed here than Enhanced Input, and a smaller model to copy.

**Godot reference:** Godot's contribution is event routing rather than binding. Every input event goes
through a fixed chain -- `_input` → GUI (`Control._gui_input`, topmost control first) →
`_shortcut_input` → `_unhandled_key_input` → `_unhandled_input` -- and any handler calling
`set_input_as_handled()` stops it. Windows get first refusal, and gameplay reads only what no
window consumed. That's a simpler way to get what mapping-context priority gives, and it matches how
`UiInputController` already hands events to UI before gameplay; the missing piece is making "consumed"
explicit instead of implied by check order. Actions are named in `InputMap` and queried with
`Input.is_action_just_pressed`, and `Input.get_vector(left, right, up, down, deadzone)` combines four
actions into the 2D `Move` value. `Input.parse_input_event(InputEventAction)` injects a synthetic action
as if a key were pressed, a ready shape for "Replay by recording input" and "In-world scenario tests"
(Global).

### Medium Priority

#### Clean up item and action tooltips

Items and actions are described in several places, each assembled its own way:
- The hotbar's hover summary (`HotbarContent.TryGetSlotSummary`): name, "Active" for a toggle that is on,
  `Summary`, the cost lines (`CostText`) and the blocker.
- The inventory's hover tooltip (`ItemHoverSummary`): "Active", `Summary`, "Target: <shape>", a wand's
  charges, the cost lines.
- Item Details (`ItemDetailsWindow`): sections for effects ("While on" for a toggle), "Cost", "Activation"
  (`ItemComparisonStatExtraction`), description, value and tags. Actions have no equivalent.
Bring them to one structure: the same order and wording for the same facts (targeting appears for items
but not actions, charges only in the inventory), one place that builds the lines for both items and
actions, and a decision on what belongs in a hover summary versus Item Details. Costs now appear on the
hotbar badge and in text: decide whether the hover summary repeats them.

#### Telegraph whether an incoming attack is aimed at the ground or at you

An enemy's windup is drawn on the tiles it will hit, red or yellow by whether it can be dodged
(`CombatTargetPalette`, the `Trait.Dodgeable` tag). It doesn't say how it was aimed, which decides
what avoids it:
- **Ground** (`TargetingMode.Ground`): it lands on the tiles aimed at, so stepping off them avoids it.
- **Target**: it follows you to the end of the windup (`TargetResolution`), so stepping away does
  nothing; only a Dodge, where allowed, does.
Show the difference, for example a solid fill for a Target-mode windup and a hatched or outlined one
for Ground, or a marker on the entity a Target-mode windup is following. Both kinds are in play
already: Fairies roll the mode for each Magic Missile (TEMPORARY content).
- **Gated behind a Skill** eventually ("Skills", Game): without it the player sees only the tiles, as
  today; with it, the mode too. Build the display first, behind a plain check that reads the
  player, so the Skill only replaces that check.
- The windup's selection already carries the mode (`PendingWindupComponent.Selection`), so this is a
  view method (`TargetingView`) and a draw change in `MapWindow`, nothing new in the simulation.

#### Buff and debuff icons below the mana bar

The row under the player's mana bar (`PlayerStatusEffectsContent`) shows status effects only
(Burning, Poison, Paralysis). Stat modifiers -- Holy Ground's blessing, a resistance potion, a
body-part penalty -- are visible only as text rows in the Health window.

- **Icons for stat modifiers** in the same place, in two rows: buffs on top, debuffs on the bottom
  (`StatModifierPolarity`). Decide where the existing status-effect icons go: each is a buff or a
  debuff too, so they most likely join the matching row rather than keep a row of their own.
- **Tooltips** on every icon, buff and debuff alike, through the shared `TooltipController`: what it
  is, what it does (the modifier's target, operation and magnitude, as the Health window already
  formats them), how long is left, and what gave it (`ActionSource` -- an entity, a terrain, an
  aura). The existing status-effect icons get tooltips too.
- **An icon per modifier needs a glyph or sprite**, which a `StatModifierComponent` doesn't have.
  Either the grant names one (`StatModifierGrant`), or it is derived from the modifier's target and
  polarity. Modifiers that are the same thing from one source should share an icon with a count,
  as stacks do.
- `HudChrome` positions everything below this row (the inspection window) from its height, so a
  second row moves those with it.

#### Player status window

A window for the player's own status that sits alongside the Health window (its own HUD button and
hotkey, the same shape as Health/Inventory/Ability Scores), for things about the player that aren't
health, inventory or ability scores. First consumer: "Boss and crawler kill icons" (Low Priority, this
section).

#### Shift+click to move a whole stack when looting and shopping

Shift+click a stack to move all of it in one click, with no drag:
- **Looting**: take the whole stack from a corpse or container into the player's inventory.
- **Shopping, buying**: buy the whole stack from the shop's inventory.
- **Shopping, selling**: sell the whole stack from the player's inventory to the shop.

Applies to currency and items alike. Dragging a stack already moves all of it, so this is a click
shortcut for the same transfers: route it through the same calls the drag-drop resolvers make
(`ShopActions.TryBuyFromShop`/`TrySellToShop`, `ShopActions.TryGiveCurrencyToShop` for currency
given to a shop, the loot transfer for corpses and containers), so rules like shop eligibility,
pricing, inventory capacity and the Angel Investor trigger can't diverge from dragging.

- **Trade window**: shift+click adds the whole stack to the trade offer, the same as dragging it in.
- **Not enough money**: transfer as many units as the buyer can afford, and leave the rest.
- **Not enough inventory room**: transfer as many units as fit, until the inventory is full.

Partial transfers need a way to move part of a stack: `TryBuyFromShop`/`TrySellToShop` move one
exact stack today, all or nothing. The affordable quantity comes from `ShopStockPricing`'s bulk
bracket pricing, where each unit's price depends on the shop's stock, so find it by pricing
quantities against it rather than dividing by a unit price. Room is counted in stacks
(`InventoryCapacity.HasRoomForNewStack`), so as much as fits means topping up existing stacks of
the item to their max stack size, then filling free slots.

#### Windows exempt from close-on-input (Diagnostics first)

Arming an action closes the Diagnostics window (F3), which is the moment its live gauges are most
worth watching. Movement leaves it open; arming an action, an item or anything else that goes through
`ActionTargetingController` calls `UiLayerStack.CloseAllClosableWindows`, which sweeps every closable
window so none blocks targeting on the map.

Generalize rather than special-casing Diagnostics: a window declares that it stays open when input is
consumed, and the sweep skips it. First consumer: `DiagnosticsWindow`. Other monitoring windows (the
debug window, a future AI debugger) opt in the same way.

- Put the exemption on the window (a property set where the window is built), read in
  `CloseAllClosableWindows`, so every caller of the sweep gets it and no call site names a window.
- Decide per sweep whether the exemption applies: arming an action should respect it; Escape-hold
  (`UiInputController`) and the HUD context menu's "Close All" (`DynamicHudContextMenus`) are the
  player asking for everything closed, and probably should not.
- An exempt window still closes from its own close button, its hotkey and a single Escape.

#### Draw neighborhood borders and the Local radius in Admin Mode

The map gives no sign of where one 1024x1024 neighborhood ends and the next begins, or where the
player's Local bubble ends, so the tier a creature is running at -- and whether a crossing did what it
should -- can only be inferred from behaviour. Under Admin Mode (F12, `GlobalState.IsAdminModeOn`),
draw both over the map, **in two different colours**:

- **Neighborhood borders:** a line on each 1024-tile boundary the viewport crosses, ideally with the
  neighborhood's coordinate (`Neighborhoods.CellOf`) labelled somewhere unobtrusive.
- **The Local radius:** the square of Chebyshev radius `ProcessingTierResolver.LocalRadiusTiles` (80)
  around the player, on the player's own layer. Local overrides the neighborhood tier, so where the
  bubble reaches into another neighborhood *this* line, not the neighborhood border, is the actual
  simulated/frozen seam -- the place a simulated creature stops attacking a frozen one. Found testing
  phase 6b: goblins and fairies fighting 75 tiles away in the neighborhood across the border looked
  wrong until the distance was worked out by hand. Consider a fainter second square at the exit radius
  (`LocalExitRadiusTiles`, 96), since a creature already Local only leaves past that.

Worth extending to the tier itself once the lines exist -- tinting or outlining the player's own
neighborhood against the frozen ring would make a crossing, and phase 6's smeared thaw, directly
visible while testing rather than something to infer.

Draw both through the debug drawing service in "Debug drawing and an AI debugger" (Medium, below)
rather than as `MapWindow` special cases, so later overlays (paths, flow fields, perception ranges) use
the same mechanism.

#### AI-generated sprites with a hovered state

Inspired by Dungeon Settlers. Generate a consistent sprite set with AI tools, replacing today's
mixed assets and glyph fallbacks (Fairy/Ghost/Lava have no sprite -- see `SpriteManifest`). Every
sprite also needs a "hovered" look: a white border around its silhouette. Better generated at draw
time from the sprite's alpha (draw it offset in white on each side, then the sprite on top) than
authored as a second cell per sprite. That keeps the manifest at one cell per sprite and works for
anything added later. Same family of problem as mask-based recoloring (Low, below) -- one base
sprite, many runtime variants. If walk/action frames get authored at the same time (see Walking and
action animations, High), plan the sheet layout for them up front.

#### Vertical status effect list under HP and mana

Inspired by Dungeon Settlers. Supersedes the former Low "Status effect stack count on the player's
status bar" entry. `PlayerStatusEffectsContent` draws one icon per effect type in a horizontal row
under the health bar, with Poison/Burning stack counts (and the potion cooldown's seconds) drawn
*below* each icon. As the number of effect types grows, switch to a vertical list below HP and mana:
one row per effect, icon on the left, number to its right. That frees the number from the
below-the-icon position and gives room for every effect's count, not just Poison/Burning's. The host
window's fixed `Size` (one icon row tall) would need to grow with the active effect count.

#### Party inventory UI -- compare Dungeon Settlers and Elden Ring

Research item. Look at Dungeon Settlers' party inventory UI and Elden Ring's inventory/equipment
screens, and note what's worth adopting for the Inventory window and the Equipment menu (Low,
below). Check it against the still-open Stack Controls and Partial Stacks (High, above), which
already calls for a similar industry comparison.

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
window before committing to a cardinal move. Part of "Input actions and mapping contexts" (High,
above): movement becomes one 2D-vector action built from the four keys, and a trigger holds a lone
cardinal for a few frames to see if the second key arrives.

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
alongside it. Shares work with `MapCamera`'s `Neighborhood`/`Borough` zoom levels, which should match
the processing-tier regions (a 1024x1024 neighborhood, and the 3072x3072 3x3 window as Borough --
today's 1000x1000/2000x2000 predate them) -- static structures + boss/landmark
sprites only, no moving entities, snapping to preset regions instead of following the player.
Fog of war is its own item below.

**Godot reference:** a Godot minimap is a `SubViewport` sharing the main world (`world_2d`) with its
own zoomed-out camera, shown through a `ViewportTexture`; everything the main view knows how to draw
appears in it for free. Here, the equivalent is rendering into a `RenderTarget2D`, which
`MapTileLayerCache` already does for terrain. Since the minimap shows static content only, it can be
a low-resolution render target per neighborhood, drawn once when the neighborhood loads (one pixel per
tile, colour from the terrain definition) and patched when a cell changes, then composed and cropped
per frame. That is far cheaper than re-rendering a 1024² area, and the same textures serve the
Neighborhood/Borough zoom levels.

#### Fog of War

Hides what the player can't currently see. Beyond its gameplay value, it's the visual half of
the frozen Borough. Without it, scrolling the camera into Borough shows frozen
creatures, which is Minecraft's well-known visual complaint. RTS games (StarCraft, Age of Empires)
are the standard model.

- **Three states per tile:** *unexplored* (blank), *explored but not visible* (terrain and static
  structures/landmarks, no creatures), and *visible* (everything). Explored stays explored, and only
  visibility is recomputed. (This settles the earlier "re-fog, undecided" note from the minimap
  item, unless play says otherwise.)
- **Visible is always a subset of Local.** A sight radius or line of sight around the player, never
  larger than Local's radius, so nothing visible is ever throttled or frozen. Recomputed on player
  move, and on terrain changes that block sight.
- **Explored-but-not-visible shows current terrain, not a last-seen snapshot,** to start. Flyweight
  terrain (plan section 1) makes that a definition lookup. A snapshot (terrain changed in fog still
  looks unchanged) is the RTS behavior. Revisit if hiding changes matters for gameplay (e.g. a wall
  destroyed out of sight).
- **Storage:** explored is a bitset per chunk per MapLayer, 1024x1024 bits = 128 KB per layer and
  ~384 KB per neighborhood. That's cheap enough to be flat rather than the per-region approximation
  the old note worried about, and it's saved with the chunk. Visible is small and only exists around
  the player.
- **No information leaks through the UI:** hidden creatures can't be hovered, selected, inspected
  (`SelectionWindowContent`, tooltips) or targeted. Targeting outside Local is already refused under
  the plan; fog extends the same refusal to "inside Local but not visible".
- **Rendering:** `MapWindow` skips occupants outside the visible set, and the terrain/background
  caches (`MapTileLayerCache`) draw unexplored tiles as blank and invalidate on reveal. The minimap
  and zoom-out levels read explored only.
- **Admin Mode (F12) bypasses fog.**
- **Light sources:** Scroll of Torch's Light grant is meant to become a fog-of-war reveal (see "Torch
  reveal + light-weakness damage" above). Design the visible set so a light source can add to it.
- **The visible set comes from "Field of view and perception" (Game, High),** the same shadowcasting
  and light levels NPCs use, so the player and a goblin see by the same rules. This item is the
  rendering and UI half. Unreal has no built-in fog of war; RTS-style fog in Unreal games is custom
  (typically a visibility texture sampled by a post-process), so the RTS games above remain the model.

#### Magic Menu

Spell-equivalent of the inventory menu, mirroring `InventoryWindowController`'s Button+pooled-Window+
`TabbedContent` pattern. "Known spells" isn't a tracked concept -- just a `MultiComponentPool<ActionInstanceComponent>`
query filtered by `Action.Spell`, which `SpellActivator` implies, so it can't drift from what the action is.

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

#### Ability score buffs name where they come from

Each modifier line in the Ability Score window (`AbilityScoreModifierFormatter`) shows its source as
an entity, e.g. "#1234". A modifier granted by a blueprint part should name that part and its kind
instead, e.g. "Race(Human)" or "Class(Tank)". When a line has a part to name, that replaces the
entity-number source entirely.

#### Animation primitives -- flipbooks, tweens and curves

Nothing on screen animates except the charge fill, the glows and floating text, each timed by its own
code. Planned items that need shared primitives: "Walking and action animations" (High), "Window
open/close/minimize animation" (Low), "Folder glow blink" (Low), taller sprites, and HUD transitions.
- **Flipbooks:** a sprite manifest entry can be a sequence of cells with per-cell durations and a loop
  mode (loop, once, ping-pong), played by time or scrubbed to a fraction (a Delayed action's windup
  driven by `IMapViewQuery.GetChargeFraction`). Per-entity playback state only in Local and the
  viewport.
- **Tweens:** animate a value (position, size, alpha, color) from A to B over a duration with an easing
  function, with completion callbacks. Used for tile-to-tile movement, window open/close and toasts.
  Driven by presentation time, not simulation frames, so a paused game can still animate its UI.
- **Curves:** a small keyed-curve type (shared with "Data tables for content", Game) for anything tuned
  by hand: a hit flash, a bounce, glow pulses.

**Unreal Engine reference:** `UPaperFlipbook` for sprite sequences (see "Walking and action
animations"). `UTimelineComponent` plays float/vector/color/event tracks over time, each track a
`UCurveFloat`-style asset, with play, reverse, set-position and finished events. UMG widgets have
their own keyframed widget animations (`UWidgetAnimation`) for UI transitions. Easing is a set of
helper functions (`FMath::InterpEaseInOut`, `InterpTo` for framerate-independent smoothing, and the
`EEasingFunc` set used by `Ease`). Those correspond to the three primitives above; Unreal keeps them
separate by domain (sprites, gameplay objects, UI), but one tween and curve type serves all of them here.

**Godot reference:** `AnimationPlayer` keyframes *any* property of any node, plus *method-call tracks*
that call a function at a given frame. A strike animation calls "spawn hit particles" on frame 3,
so timing lives with the animation rather than being duplicated in code. Worth copying as flipbook
events: a frame can name a presentation event, raised when playback crosses it. `Tween`
(`create_tween().tween_property(node, "position", target, 0.2).set_trans(TRANS_QUAD).set_ease(EASE_OUT)`,
with `chain`, `parallel` and `tween_callback`) is the code-driven version and the model for the tween
API above. `AnimationTree` state machines switch animations by state (idle, walk, windup, strike),
which the walking/action item will need once a sprite has more than one sequence.

#### Audio

No audio exists. FNA includes FAudio (`SoundEffect`, `SoundEffectInstance` with volume, pitch and pan),
so the playback layer is there; what's missing is everything around it:
- **Sound definitions as data:** a named sound is one or more files, picked randomly with pitch and
  volume variation so repeated hits don't sound identical, referenced from cue handlers (see "Gameplay
  Cues", Game) and UI events.
- **Positional sound:** volume and pan from the source tile's distance and direction relative to the
  camera centre, with a max audible distance. Nothing outside Local plays.
- **Concurrency limits:** at most N instances of a sound (or of a group like "hits") at once, dropping
  the quietest or oldest -- otherwise a 20-goblin fight is 20 overlapping hit sounds.
- **Volume categories:** master, music, effects, UI, ambient -- each a slider on the Options menu,
  persisted with the other settings. Ducking (lowering music during a boss roar) is a later nicety.
- **Music:** looping tracks with crossfades, chosen by context (exploring, combat, shop, boss).
- Headless benchmark runs never load or play audio.

**Unreal Engine reference:** `USoundCue` and MetaSounds combine and randomize sounds (random node,
modulator for pitch/volume variation). `USoundAttenuation` defines distance falloff shape and spatial
panning. `USoundConcurrency` sets max instances per sound or group and a resolution rule (stop oldest,
stop quietest, stop farthest, prevent new). `USoundClass` and `USoundMix` give volume categories and
ducking (a mix pushed while a boss roars), and are what an options menu's sliders adjust. Audio is
triggered from Gameplay Cue notifies in GAS projects, the same route suggested above.

**Godot reference:** the same concepts at a size worth copying directly. A *bus layout* (Master,
Music, SFX, UI, Ambient) where each bus has a volume, mute, an effect chain (`AudioEffectLowPassFilter`,
`Reverb`, `Compressor`) and a send to another bus -- the volume categories above, plus "muffle
everything but UI while paused or in a menu" as one low-pass effect toggled on a bus.
`AudioStreamRandomizer` holds variants with random pitch and volume offsets (the "sound definition"
bullet), and `AudioStreamPlayer2D` has `max_distance`, an attenuation curve and `max_polyphony`
(per-sound concurrency). FNA's FAudio has submix voices with effect chains, so buses map onto it
directly.

#### Particles

No particle effects exist. Planned consumers: "Blood pool under dead entities" (a splash on the killing
blow), spell and aura effects, burning and lava, Torch, level-up, loot box opening, and cue handlers
("Gameplay Cues", Game).
- A pooled CPU particle system in Presentation: an emitter definition (spawn rate or burst, lifetime,
  velocity, gravity, color and size over lifetime, sprite or flipbook), instances attached to an entity
  or a tile.
- A global particle budget with viewport and Local culling, like floating text: an emitter off screen
  doesn't simulate, and new emitters beyond the budget are dropped.
- Drawn in the map's draw order (under or over occupants per emitter).
- Emitter definitions are data, so mods add effects without code.

**Unreal Engine reference:** Niagara. Systems are made of emitters, each a stack of modules (spawn,
update, render) with CPU or GPU simulation. The parts that apply here are scalability and pooling:
`UNiagaraEffectType` sets per-type budgets (max instances, cull distance, what to do when over budget),
significance handlers decide which instances to cull, and `UNiagaraComponentPool` reuses finished
components (`ENCPoolMethod::AutoRelease`) instead of allocating new ones -- the same GC concern as
everywhere else here.

#### Debug drawing and an AI debugger

Admin Mode shows inspection data in windows, but nothing can be drawn on the map for debugging, and
there's no way to see *why* a creature did something after the fact. Coming work needs both: "Draw
neighborhood borders and the Local radius in Admin Mode", pathfinding (paths, flow fields),
perception (sight cones, last-known locations), NPC behavior (utility scores per behavior, commitment
deadlines), and spatial queries (candidate tiles and scores).
- **Debug draw service:** any code in any layer can request a line, tile outline, filled tile, text
  label or circle at world coordinates, for one frame or a duration, in a category. `MapWindow` draws
  the queue over the map in Admin Mode, and a category can be toggled. Requests from Game go through an
  Engine-level interface, so Game doesn't reference Presentation. Compiled or gated out of release
  builds.
- **AI debugger:** with a creature selected in Admin Mode, show its current decision (each behavior's
  utility and the winner), its perception (what it sees and remembers), its path and its claimed spots,
  both on the map and in the inspection window.
- **Decision recorder:** keep the last N seconds of a selected creature's (or every Local creature's)
  decisions and debug shapes, so after something odd happens the timeline can be scrubbed back to see
  what it was thinking. Saved to `Log/` with the seed and frame, so it can be attached to a bug report.

**Unreal Engine reference:**
- *Debug drawing:* `DrawDebugLine`, `DrawDebugBox`, `DrawDebugSphere`, `DrawDebugString`, each with a
  duration and a persistent flag, stripped from shipping builds.
- *Gameplay Debugger:* toggled with the apostrophe key. It shows categories (AI, Behavior Tree, EQS,
  Perception, Navmesh, Abilities) for the actor under the crosshair, drawn in the world and as text,
  each category toggled with the number keys. Projects add categories (`FGameplayDebuggerCategory`).
- *Visual Logger:* `UE_VLOG` (text) and `UE_VLOG_LOCATION` / `UE_VLOG_SEGMENT` / `UE_VLOG_BOX` (shapes)
  record per-object entries each frame into a timeline. The Visual Logger window scrubs the timeline
  and redraws the shapes in the world at that moment, and recordings save to `.vlog` files. This is the
  decision recorder above, and the most useful tool for tuning utility AI.

**Bevy reference:** `Gizmos` is an immediate-mode debug-drawing system parameter (`gizmos.line_2d`,
`rect_2d`, `circle_2d`, text via a separate label) callable from any system, redrawn each frame, with
retained gizmos (`GizmoAsset`, 0.16) for shapes that don't change. Drawing is grouped by
`GizmoConfigGroup` types, each with its own enabled flag, line width and depth settings, toggled at
runtime through `GizmoConfigStore` -- the per-category toggle above. For inspection, the third-party
`bevy-inspector-egui` shows every entity and its reflected components live and lets them be edited,
which is roughly Admin Mode's inspection window generalized to any component; see "Remote inspection
of a running game" (Global) for the out-of-process version.

#### Visual 2D lighting

The map is uniformly lit. Torch, lava, darkness and (later) day/night change nothing visually, and
fog of war's explored-but-not-visible state needs a dimmed look. "Field of view and perception" (Game,
High) decides the gameplay light level per tile; this is drawing it.
- **Ambient level:** a floor or neighborhood sets a darkness colour multiplied over everything.
- **Light sources** (the same definitions gameplay reads -- Torch, lava, glowing creatures, wall
  torches) add light in a radius with a falloff and colour.
- **Occlusion:** walls block light, using the same "blocks light" flag as perception, so the visual
  shadow and the gameplay shadow match.
- **Implementation options:** (a) per tile -- compute a light colour per visible tile from the gameplay
  light map and tint each tile and occupant by it (cheap, blocky, fits pixel art); (b) a light render
  target -- draw each light's gradient into a texture, cut out shadows, and multiply the scene by it
  (smooth, more GPU work, needs a shader). Start with (a), since the gameplay light map already has
  the numbers.
- **Local and viewport only;** off-screen lights cost nothing.
- **Glow overlaps:** today's glow cache (`GlowRenderer`) draws light-like auras; decide whether glows
  become light sources or stay a separate effect.

**Godot reference:** `PointLight2D` (a texture-shaped light with energy, colour, range and optional
shadows), `DirectionalLight2D`, `LightOccluder2D` with an `OccluderPolygon2D` (TileSets can carry
occluders per tile), and `CanvasModulate` for the ambient colour. Lights are additive or subtractive
blend modes over the canvas, and sprites can take normal maps (`CanvasTexture`) for per-pixel shading.
It is purely visual; gameplay can't ask a light what it lights, which is why the gameplay light map in
the perception item stays the source of truth.

#### Sprite shaders and tinting

No custom shaders exist; every sprite draws with `SpriteBatch`'s default effect, tinted at most by a
colour multiply. Several items want per-sprite effects: "Investigate mask-based recoloring for shared
sprites" (Low -- palette swaps), hit feedback ("Gameplay Cues", Game -- a white flash), selection and
highlight ("Highlighted-tile visual redesign", Low -- outlines), death (dissolve or fade), Phasing
(already alpha), and petrification or poison (desaturate or tint).
- **A small set of sprite effects,** each an FNA `Effect` (HLSL compiled to FNA's format) with
  parameters: palette swap (a lookup texture maps greyscale mask values to colours), flash (mix toward
  a colour by an amount), outline (sample neighbouring texels), desaturate, dissolve (threshold against
  a noise texture).
- **Batched by effect:** `SpriteBatch` can only use one effect per `Begin`/`End`, so draws are grouped
  by effect, which fights the row-ordered occupant pass in the tall-sprites plan. Options: per-sprite
  parameters packed into the vertex colour (one "uber" effect reads flash amount and palette row from
  the colour channels, so one batch serves everything), or a few effects and accept extra batches.
  The packed-colour approach keeps draw order intact.
- **Content pipeline:** FNA loads effects compiled with `fxc` (DirectX 9-style effect binaries,
  translated at runtime by MojoShader); the `Content` project doesn't compile anything today, so this
  adds a shader build step.

**Godot reference:** every `CanvasItem` can take a `ShaderMaterial` with a `canvas_item` shader
(`fragment() { COLOR = texture(TEXTURE, UV) * ...; }`), and `modulate`/`self_modulate` feed a colour
through to it. Palette swaps, flashes and outlines are the standard examples. Godot batches items
that share a material, so games that want many per-sprite variations use the same trick as above --
parameters through `modulate` or vertex colour into one shared material -- to keep batching.

#### Rich text

All text is plain strings drawn in one colour per label (`LabelRenderer`, `ContrastTextRenderer`).
Wanted by: the chat log and speech ("Chat and speech", Low), item tooltips and details (rarity-coloured
names, green/red stat deltas in "Equipped-item comparison"), notifications and achievements, "Ability
score buffs name where they come from", floating text variants, and the Health window's per-target
formatting.
- **Markup** in `DisplayText`: colour, bold (a second font weight), inline icons (an item, a status
  effect, a currency coin, a key glyph for "press F"), and links (hover for a tooltip, click to open
  an item or entity).
- **Parsed once** when the text is set, into runs (text, style, icon) measured with the existing
  `ITextMeasurer`; drawing iterates runs, never re-parses.
- **Wrapping** across runs, with icons as unbreakable glyphs sized to the line height.
- **Localization-safe** ("Localization", Global): markup lives in the translated string, so a
  translator can move a coloured word, and arguments are formatted before markup is parsed.
- Later: per-run effects (shake, wave, a rainbow for legendary items) for flavour text.

**Godot reference:** `RichTextLabel` with BBCode: `[color=red]`, `[b]`, `[i]`, `[img]` for inline
textures, `[url=...]` with a `meta_clicked` signal for links, `[table]`, and `fit_content`. Custom tags
come from `RichTextEffect` subclasses (`_process_custom_fx` moves or recolours each character per
frame), and `visible_characters` gives a typewriter reveal. Text shaping, BiDi and font fallback are
handled by the `TextServer`, which is the part hardest to replicate; for Latin-script languages,
FontStashSharp's measuring is enough.

### Low Priority

#### Change the cursor to a targeting mode indicator while armed

While an action or item is armed, the cursor over the map should show the player's targeting mode
(`MapViewState.TargetingMode`) instead of the plain arrow, so the mode is visible before confirming
rather than only for the second after Left Alt switches it (`TargetingModeSwitch`). The cursor is
already chosen in one place each frame (`UiInputController`, `CurrentCursor`, set through
`MouseCursorEXT.SetCursor`); the armed-confirm-refused case already uses `MouseCursor.No` there.
- **Candidates from the system set** (FNA's `MouseCursor`): `Crosshair` for Target (aiming at
  someone), `Hand` or `SizeAll` for Ground (placing on a spot). No asset needed, but the set is small
  and looks like an OS cursor.
- **Custom cursors** (`MouseCursor.FromTexture2D`): a reticle for Target and a ground marker (a
  ring or an X on the floor) for Ground, drawn in the game's sprite style. Possibly also a
  difference between single-target and area effects -- a small reticle for SingleTarget, a wider
  ring for Burst/Cone -- and Ground-only activations (melee, Dodge) keeping the plain arrow, since
  their mode can't change.
- Decide whether the hover footprint already says enough about single target vs area, so the cursor
  only needs to carry the mode.

#### Boss and crawler kill icons

Show an icon for each boss and each crawler an entity has killed, in two places: the Inspection window
(for whichever entity is inspected) and the player's status window. Needs a per-entity record of kills
by kind (boss/crawler), fed from `EntityDiedEvent` the same way `BossLootboxAwarder` reads a slain
boss's blueprint. Blocked on "Player status window" (Medium Priority, this section).

#### Health Window: status effects in a third column

The Health Window has two columns today (see IMPLEMENTATION-NOTES.md, "HealthWindow"). Move status
effects into a third column of their own.

#### Health bar hover popup text in white

The player health bar's hover popup (`PlayerHealthHoverContent`) should draw its text in white.

#### Compare input hit-testing against a unified picking pipeline

Pointer handling is split: `UiInputController` hit-tests windows and elements, the map resolves tiles
and entities in `MapWindow`/`ActionTargetingController`, and drag-drop resolves through
`IDragDropResolver`s. Live testing has turned up hit-test, popup and tap-versus-drag bugs repeatedly,
each fixed in the shared layer. Worth a design review, not a rewrite: would one pipeline -- every
pointer position resolved once per frame to a stack of hits (topmost UI element, then map entity, then
tile), with standard pointer events (over, out, down, up, click, drag start, drag, drop) sent to the
hit and bubbling up the element tree until handled -- remove whole classes of those bugs? Compare
against how `UiLayerStack`, the tap threshold and the drag-drop resolvers work today before deciding.

**Bevy reference:** `bevy_picking` (0.15). *Backends* (sprites, UI nodes, meshes, or custom) each report
what's under each pointer with a depth; a single pass sorts the hits and decides which entities are
hovered, respecting `Pickable { should_block_lower, is_hoverable }`. It then emits `Pointer<Over>`,
`Pointer<Out>`, `Pointer<Press>`, `Pointer<Click>`, `Pointer<DragStart>`, `Pointer<Drag>`,
`Pointer<DragDrop>` and friends as entity-targeted events that bubble up `ChildOf` to parents through
observers, where a handler can stop propagation. Map entities and UI elements go through the same
events, so a drag from the inventory onto a map tile is one drag, not two systems handing off.

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

#### Blood pool under dead entities

A blood pool drawn under a corpse on every tile of its footprint (`TransformComponent.Size`, same
per-tile loop `DrawFollowedEntityHighlight` uses), colored per entity. Nothing carries a blood color
today -- add one per race blueprint (a field or small component, e.g. red for Human/Goblin), and
decide what a Ghost leaves (none, or ectoplasm). Draw it under the corpse in the occupant pass
(`DrawUnderlayOccupants`, before the corpse sprite), not after. Pairs with Red X marker above (both
make a corpse read as dead) and Corpse decay/destruction (Game) -- the pool should go when the
corpse does.

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
A third option once "Sprite shaders and tinting" (Medium) exists: outline the highlighted *sprite*
rather than the tile (the common Godot approach, a `canvas_item` outline shader on the selected
sprite), which also stays correct for tall sprites that overhang their tile.

#### Circle selection under the entity instead of a tile border

Inspired by Dungeon Settlers. `MapWindow.DrawSelectedTileGlow` marks the selected/followed tile with
an interior-fade glow over the whole tile. Replace it with a circle (ellipse) on the ground under the
entity, drawn *before* the entity sprite rather than on top. That needs it inside the occupant pass,
not after it the way highlights draw today. Pairs with the tall-sprite item (High, above): once
sprites extend past their own tile, a tile-shaped highlight stops lining up with the character. May
settle the Highlighted-tile visual redesign above for selection specifically, but not for ability
targeting. There's no circle primitive yet -- `unitRectangle` plus `GlowRenderer` are all the map has.

#### Extract a shared tick-fraction HUD bar element

`PlayerHealthBarContent`/`PlayerManaBarContent` are near-duplicates (same outline+inset-fill+tick-mark
shape and centred "current / maximum" value text via `ResourceBarValueText`, differing only in
backing component, palette and how the current value rounds). Tolerable at two copies -- abstract into one
generic element if a third shows up (e.g. Soul Essence). `MapWindow.DrawHealthBar` is arguably a lighter
third instance already (same fraction math, no ticks, per-any-entity) -- include it in scope if this is
ever picked up.

#### Hotbar insufficient-mana indicator

A small blue fill bar on a hotbar slot for an action/spell whose `ManaCost` exceeds the player's
current `ManaComponent` pool -- a glanceable "you can't afford this right now" without opening a
tooltip. Precedent: `RadialFillRenderer`'s existing cooldown-sweep mask, already drawn on
`HotbarContent`'s ability slots (`ActionLockContent`'s own HUD wheel is the other consumer today) --
this would be a second, independent fill/tint on the same slot, not a replacement, since cooldown and
affordability are two separate reasons a slot can't be used right now and both are worth signaling at
once. Blocked on Mana costs actually being enforced on activation -- this file's own "Mana" entry
(Game, Medium Priority) notes every `ManaCost` is unenforced today ("both free today"); this
indicator has nothing real to check until that lands.

#### Context menu amount picker

`CurrencyRowContent`'s Give/Take (and their "All" variants)
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
through `ShopActions.TrySellToShop` in one action -- any tab, not only a
dedicated "Sell" tab; marking curates what a sweep picks up, it isn't itself a tab requirement.

#### Player stats v2

Let the player choose which stats to display. Follow-on to Player stats v1.

#### EndOfLevelStairs (Presentation)

Rendering/interaction for EndOfLevelStairs. See the matching Game item.

#### Digger detection (Presentation)

Part of MapLayer interaction (overview, Game); needs Digger detection (Game) first. On the Ground view,
low-level detection draws a "disturbed earth" marker over the digger's footprint, and high-level detection
draws its sprite partially transparent (`TryDrawEntityVisual`'s `alphaMultiplier`, already used for
Phasing). Undetected draws nothing, including no `v` layer badge, and viewing the UnderGround layer
directly (Page Down) must not bypass it outside Admin Mode. Needs a disturbed-earth sprite in
`Content/SpriteManifest.json`.

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

**Godot reference:** Godot has both. `LineEdit`/`TextEdit` own their history internally and coalesce
consecutive typing into one undo step (a new step starts on a pause, a cursor jump or a
different kind of edit). Separately, the general `UndoRedo` class records actions --
`create_action(name, merge_mode)`, `add_do_method`/`add_undo_method` (or `add_do_property`/`add_undo_property`),
`commit_action` -- with a step cap (`max_steps`), and `MERGE_ENDS` folds repeated actions of the same
name into one (dragging a slider). A shared primitive shaped like `UndoRedo` answers the open
question: `TextBox` records its edits into it with its own coalescing rule, and later users (Admin Mode
spawn/apply, inventory rearranging) record theirs.

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

#### Scroll buttons for narrow content

For narrow scrollable content with no room for a scrollbar (see IMPLEMENTATION-NOTES.md "Scrollbars"), e.g.
TabbedContent's tab strip, which is only `TabHeaderHeight` tall: give up a little content space at each
end for a pair of scroll buttons (◀ ▶ / ▲ ▼) instead. Such an element would use
`ScrollbarVisibility.Hidden` today. Show the buttons only while that axis overflows, disable each one at
its end of the range, and scroll a step per click, repeating while held.

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

Everything snaps instantly. Pure polish, lowest priority UI item. Use the tweens from "Animation
primitives -- flipbooks, tweens and curves" (Medium); Unreal does this with UMG widget animations
(`UWidgetAnimation`, played on open/close from the widget itself).

#### Show module load failures on the start menu

Depends on a start menu, which doesn't exist yet. A mod that fails to load (it throws, is missing a
built-in's components, or has an unmet `Requires`) is dropped and reported only through
`Console.Error` (`WorldSessionBootstrapper`). The game is a WinExe, so a normal launch shows nothing.
List `GameBootstrapResult.Failures` on the start menu: each mod's type name and the exception's
message, with the full exception available on demand. The start menu itself is a state in "Game flow
-- start menu, session states, floor transitions" (Game, Medium). Failures should also go to the log
("Debug/event logging with levels", Global) rather than only `Console.Error`.

#### Options menu

No settings screen exists -- Escape currently does nothing. Wanted: Escape (global, unconditional, same
as Tab) opens it, and the game pauses while open -- just `OpenMenuWindow`/`CloseMenuWindow` (see Pause
modality, `IMPLEMENTATION-NOTES.md`), no new modality code needed. Settings it changes are saved to
the user layer of "Layered config files" (Global); Unreal's equivalent is `UGameUserSettings`, one
object holding user-changeable options, loaded from and saved to the user's `GameUserSettings.ini`
with `ApplySettings`/`SaveSettings`.

#### Floating text settings on the options menu

Needs Options menu (above). Settings for the Floating Text feature (`IMPLEMENTATION-NOTES.md`, "Floating Text"): a master
on/off, per-kind toggles (damage taken, healing, regen, status stacks, Dodge/Immune, and later XP/Level
Up/Skill Up), and possibly text size and duration. Persisted through Data storage (Global) once it
covers more than window geometry -- specifically its "Layered config files" user layer.

#### Keybindings page on the options menu

Needs Options menu (above) to live in, and Standard widget set (needs at least something list-like) --
today's hotkeys are hardcoded in `MapWindow.OnHotkeysAction`/`UiInputController`. Would eventually want
persisted storage for rebinds (see Data storage under Global, which today only covers window geometry).
Depends on "Input actions and mapping contexts" (High): the page lists input actions per mapping
context and edits the user's binding layer, the way Unreal's player-mappable keys
(`UEnhancedInputUserSettings`) work -- rebinds are stored per named mapping, not as a copy of the whole
context, so a later default change doesn't wipe them.

#### Targeted key-press routing instead of a full-keyboard scan

`RouteKeyPressesToFocusedWindow` calls `KeyboardState.GetPressedKeys()` every frame a window is focused
(confirmed via reflection: FNA has no non-allocating variant) -- allocates every frame for the session.
`HandleKeyPress` has exactly one real consumer (`TextBox`, caring only about Backspace). Let the focused
content declare the small key set it actually wants checked instead of scanning/diffing the whole
keyboard. Falls out of "Input actions and mapping contexts" (High): the focused content pushes a
mapping context, and only keys bound in active contexts are polled (Enhanced Input likewise only
evaluates the mappings of its active contexts).

#### Chat and speech

Glowing per-NPC speech bubbles (clickable for the full line), separate from a WoW-style configurable
chat log (Loot/Combat/Local Chat/Notifications tabs, user-routable message types). `NotificationCenter`
is the closest precedent but is popup-shaped, not a persistent scrollback -- a different, bigger widget.
NPC lines should be localizable text from the start (see "Localization", Global). Unreal's
`UDialogueWave` holds one line with per-speaker/listener variants and its subtitle text, which is a
reasonable shape for a line definition: speaker, text key, optional sound, display duration.

#### Visual improvement pass

Dedicated sizing/placement/color pass across Presentation once the HUD stops churning -- today's values
(`HudMetrics`, scattered per-content constants) were each chosen locally.

#### Investigate mask-based recoloring for shared sprites (potions as the case)

Every potion needs a fully-authored sprite even though most differ only by liquid color.
`SpriteManifest`/`SpriteSheetService` have no tinting concept. Worth investigating a mask (grayscale/
alpha region marking recolorable pixels) + a `Color` field `SpriteRenderer` tints per-instance, instead
of a duplicate sprite per color variant. Generalizes to any other "one silhouette, many colors" case
(dyed equipment, faction banners). The GPU route is the palette-swap effect in "Sprite shaders and
tinting" (Medium); Godot games do this with a `canvas_item` shader that maps greyscale mask values
through a palette texture, one palette row per variant, selected per sprite.

#### Aura glows pulse outward from the source

An aura's glow is a static tint today: `AuraField.TryGetGlow` hands `MapWindow` one colour per cell
(the aura colours there, weighted by strength) through `GameViews.AuraGlow`. Animate it instead as a
pulse that travels outward from the source, drawn as a gradient within each tile rather than one flat
colour per tile, so the wave reads as continuous across tile edges.

- The pulse's phase at a tile comes from its distance to the source (the same Manhattan distance the
  strength falloff uses), so the wave front moves outward ring by ring.
- Each aura has its own pulse interval, so several auras on one entity pulse out of step with each
  other. The interval belongs on the `AuraDefinition` beside the glow colour, never on the source.
- That needs the glow per aura at a cell, not the single blended colour `TryGetGlow` returns today --
  each aura's contribution has to be drawn (or blended) at its own phase.
- Purely visual: it is driven by render time and changes nothing about when an exposure ticks.

Related: "Animation primitives -- flipbooks, tweens and curves" and "Visual 2D lighting" (Medium).

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
items, granted abilities, `IActionActivator`/`Effect` catalog entries) is serialized, a saved `Guid` reference to mod-defined content can go
stale if that mod changes before the save reloads (RimWorld/PoE's well-known failure mode). Fail
hierarchy, decided up front: (1) prefer a mod-supplied replacement/migration, (2) fall back to dropping
just the affected reference while the rest of the save loads, (3) last resort, drop the whole entity if
the missing content is load-bearing for it. Consider letting a mod register its own fallback id per
content id it defines.

**Unreal Engine reference:**
- *What gets saved is marked, not listed:* `USaveGame` subclasses hold the data, saved with
  `UGameplayStatics::SaveGameToSlot` / `AsyncSaveGameToSlot` (serialization on a worker, the disk write
  off the game thread). For whole objects, properties tagged `UPROPERTY(SaveGame)` are written by an
  `FArchive` with `ArIsSaveGame` set, so a component opts its fields into saves where they're declared.
  The equivalent here is a per-component serializer registered with the pool, so a mod's component
  saves without the save system knowing about it, and a component with no serializer is rebuilt
  rather than saved (most skeleton data already is, from the spawn record).
- *Versioning:* `FCustomVersion` registers a Guid plus version number per subsystem. The number is
  written into the save, and loading code branches on it (`Ar.CustomVer(...)`) to migrate old data.
  One version per module (keyed by `IModule.Id`) fits the module system, so a mod bumps its own version
  without a global save format change.
- *Renames:* `CoreRedirects` in config map old class, property and asset names to new ones at load.
  That's tier (1) of the fallback hierarchy above -- a mod-supplied redirect from an old content Guid
  to its replacement.
- *Slots and user index:* saves are named slots per user, with a small header (version, timestamp,
  floor, playtime) readable without loading the whole save, for a load menu.

**Bevy reference:** reflection is the generic half. `#[derive(Reflect)]` plus `#[reflect(Component)]`
registers a component's fields in the `TypeRegistry`, and from then on any code can read, write,
serialize and deserialize it without knowing the type -- saves, the inspector, the remote protocol and
scenes all use that one registry. A component that isn't registered is simply not saved, and
`DynamicSceneBuilder`'s allow/deny lists choose which registered components go into a given save. Here,
an equivalent is a per-pool serializer registered alongside the pool, generated or hand-written, so a
mod's component joins saves by registering one. Unlike Unreal, Bevy has no built-in save versioning;
migration is left to the game, so the per-module version in the Unreal note above is still needed.

**Godot reference:** one detail from Godot's `RandomNumberGenerator`, which exposes both `seed` and
`state`: restoring a seed restarts a sequence, restoring the state continues it. A save must store
the *position* of every live random sequence (the factory's runtime sequence, the crawler-number
allocator, any per-system generator), not just the session seed, or a loaded game diverges from the
one that was saved. `SeededRandom` needs a readable and restorable state for this. Godot's
`ConfigFile` and `ResourceSaver` are simpler than either other engine's and don't add anything beyond
the notes above.

#### Save and load Beyond neighborhoods

Today a neighborhood evicted from the window's cache
is deleted: its `NeighborhoodRecord` survives, so returning regenerates the same layout with a fresh
population. Instead, write the neighborhood to disk on unload and read it back on return: the record
plus its contents (entities with their full component sets, and any terrain or structure changes
since generation), so a revisited neighborhood holds the same creatures, corpses and loot.

- **Depends on** "Data storage" above (the serialize-to-disk mechanism) and "Entity storage" below
  (snapshot and rehydrate an entity's exact component set).
- **References:** entities come back with new ids; anything that points at another entity already
  holds its `EntityKey`, which is saved with it (plan decision 17). Crawler numbers are kept, never
  re-minted.
- **Time:** reloading catches up active timers the same way a Borough promotion does; whether more
  than that advances with elapsed time is the open question in "Unsimulated-tier time".
- **Format:** "record plus changes" for neighborhoods the player never touched, a full snapshot
  otherwise. Parsing runs on the streamer's worker, the way generation does (`TestMapBuilder.Plan`; see
  "Asynchronous neighborhood generation" in `IMPLEMENTATION-NOTES.md`).
- **Spawn records** hold a session-local blueprint id: persist the blueprint's Guid instead, and for
  a composite interned at runtime (`BlueprintRegistry.Compose`) its ordered include Guids, so loading can
  re-intern it.
- **Applied blueprints** (`AppliedBlueprintComponent`): persist each entry's blueprint Guid and order
  beside the spawn record -- together they are everything the entity was built from. A crawler's number
  is persisted once assigned; an unbuilt crawler has only its `SpawnFlags.Crawler` flag.

**Unreal Engine reference:** World Partition streams cells but never saves runtime changes to them; a
cell that unloads and reloads comes back as authored. Games that persist per-cell changes build it
themselves, usually as a per-cell save record keyed by cell and written when the cell unloads -- the
"record plus changes" format above. Unreal's async package loading (`LoadPackageAsync`) keeps that read
off the game thread, which the streamer's worker already does here.

**Bevy reference:** `DynamicScene` is close to this item's full-snapshot case: `DynamicSceneBuilder`
extracts chosen entities with their reflected components into a serializable scene, and spawning it
creates new entities. References between entities are fixed up by `MapEntities`: a component
implements it (or marks fields `#[entities]`, 0.16) to say which of its fields are entity references,
and loading rewrites each one through an old-id → new-id map. That's the mechanism the **References**
bullet above needs for anything that holds an entity id rather than an `EntityKey` -- and for
`EntityKey`s themselves if keys are re-issued on load instead of kept. Relationships ("Relationships",
Engine) restore their target side automatically once the source side is remapped.

#### CI step for the performance-filtered tests

`Tests.csproj` defaults `VSTestTestCaseFilter` to `TestCategory!=Performance`, so an unfiltered `dotnet
test` no longer runs `AbilityScorePerformanceTests`. Nothing runs them automatically now, and a real
scaling regression -- per-call cost growing with population, which is the only thing those two tests
exist to catch -- would land unnoticed. They need their own step: `dotnet test Tests/Tests.csproj
--filter "TestCategory=Performance"`.

There is no CI anywhere in the repo today (no `.github/workflows`, no pipeline file), so this starts
with standing one up: build the solution, run the ordinary suite, then the performance filter.

- **Separate step, not parallel with the ordinary suite.** Contention between the two is exactly why
  the filter default exists -- the ratio assertions flaked when both ran at once on this machine.
- **If it flakes on a shared runner, raise the ceiling, don't drop the step.**
  `MaxGrantDefaultsScalingRatio`/`MaxExpiryRecomputeScalingRatio` are 20 against a linear ~10; a noisy
  neighbour can push a 10x run past that. The assertion's job is failing quadratic cost, not measuring
  absolute speed, so a looser CI ceiling still does that job.
- Unrelated to the `phase-performance-testing` skill's whole-game per-system benchmark, which needs a
  real run and a fixed seed rather than a test filter.
- The same pipeline should later run "In-world scenario tests" (Low, below) and a short headless
  benchmark as a smoke test. Unreal's equivalent is Gauntlet, which launches built game instances
  headless on CI, runs automation tests or scripted scenarios in them, and collects results and
  performance data.

### Low Priority

#### Debug/event logging with levels

`Game/Diagnostics/PlayerActivityLog.cs` is a narrow, single-purpose EventBus subscriber (Burning
damage/moves to a file), deliberately not a general logging facility. Worth a real design (log
levels, a generic "subscribe any event to a log line" mechanism, configurable sinks) once more than one
thing wants to log. See Entity storage below for a narrower, related need. More than one thing now
does: module load failures (`Console.Error` only, invisible in a WinExe launch), settings failures,
and the planned AI decision recorder ("Debug drawing and an AI debugger", Presentation).

**Unreal Engine reference:** `UE_LOG(LogCategory, Verbosity, ...)`. Categories are declared per system
(`DECLARE_LOG_CATEGORY_EXTERN(LogAI, Log, All)`) with a default verbosity and a compile-time maximum, so
`VeryVerbose` lines cost nothing in shipping builds. Verbosities: Fatal, Error, Warning, Display, Log,
Verbose, VeryVerbose. A category's verbosity can be changed at runtime with the `log LogAI Verbose`
console command or at launch with `-LogCmds="LogAI Verbose"`. Output goes to a set of output devices
(file under `Saved/Logs`, the console, the debugger). `UE_LOGFMT` (5.2+) adds structured fields.
For here: a category per module (named from `IModule`), levels settable through "Console variables and
console commands" (Engine), a file sink in `Log/`, and cheap-when-off call sites -- the same "one check
when disabled" rule as diagnostics.

**Bevy reference:** `bevy_log` is the Rust `tracing` crate. Categories are just module paths, so
nothing is declared: a filter string (`LogPlugin { filter: "wgpu=error,game::ai=debug" }`, or the
`RUST_LOG` environment variable) sets levels per module prefix. Spans (`info_span!("npc_decide")`)
group everything logged inside them and double as profiler zones (Tracy via the `trace_tracy`
feature). Taking the "category = namespace or module name, levels set by a prefix filter" idea saves
declaring categories by hand, and it composes with the Unreal-style runtime `log` command.

**Godot reference:** Godot's own logging is minimal (`print`, `push_warning`, `push_error` with a
script stack trace, a rotating file log under `user://logs`). Two useful details: 4.5's `Logger`
class, registered with `OS.add_logger`, receives every engine message and error, so a game can route
them into its own log window or crash report; and errors surface in the editor's debugger with the
stack that raised them. For here: route unhandled exceptions and `Console.Error` output into the same
sink, and keep the last N lines in memory for an in-game log window in Admin Mode.

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

**Bevy reference:** Bevy 0.16 solved the same-session half with entity disabling. A `Disabled`
marker component is excluded from every query by default (`DefaultQueryFilters`), so adding it takes
the entity out of every system with no per-system check and nothing removed, and removing it puts the
entity back with its exact state. Queries that must see disabled entities opt in (`With<Disabled>`,
`Has<Disabled>`). Here, the equivalent is a marker pool that `EntityStripeSet`/`TieredEntityStripeSet`
treat like removal (drop the entity from their buckets when the marker is added, re-add it when the
marker goes), with `SkeletonAccessGuard`-style Debug checks for anything that reads a disabled entity.
That gives the freeze/thaw half without serializing anything; the snapshot half (for saves and leaving
the session) is `DynamicScene`, see "Save and load Beyond neighborhoods" above.

#### Layered config files

Settings come only from the command line (`CommandLineSettingsSource`), so nothing the player changes
survives a restart, and there's nowhere to put project defaults other than code. The Options menu,
floating text settings, keybindings, audio volumes and window layout all need persistence. Add
`ISettingsSource`s for files, applied in layers where each later one overrides an earlier one:
1. Code defaults (`SettingDefinition`).
2. Game defaults shipped in `Content/` (one file, or one per module).
3. Each mod's own defaults, in mod load order.
4. The user's file (written by the Options menu, in the user's app data folder, not the install).
5. The command line, last, as today.
- The Options menu writes only what the user changed, into layer 4, so a later default change still
  reaches settings the user never touched.
- A malformed file is reported and skipped, never fatal, the way `MalformedEntries` already works.
- Console variables ("Console variables and console commands", Engine) record which layer last set
  them, so a console change during play isn't written to the user's file unless asked.

**Unreal Engine reference:** Unreal's config system is layered `.ini` files: engine `Base*.ini`, the
project's `Config/Default*.ini`, platform folders (`Config/Windows/WindowsGame.ini`), then the user's
`Saved/Config/<Platform>/*.ini`, each overriding the last, with command-line overrides on top
(`-ini:Game:[/Script/Module.Class]:Key=Value`). Classes marked `UCLASS(config=Game)` read their
`UPROPERTY(config)` fields from the matching section automatically, and `SaveConfig()` writes only
values that differ from the layers below. Array edits use `+`/`-` prefixes so a lower layer's list can
be extended or trimmed rather than replaced -- useful for mods adding to a list setting.

#### Replay by recording input

The simulation is deterministic -- seeded worlds (`--seed`), per-entity reseeding in `BuildComplete`,
streaming applied at fixed update counts, the unit budget instead of wall-clock -- so recording the
seed, settings, mod list and the player's input per simulation frame is enough to replay a run exactly.
Uses: reproducing a bug from a recording instead of a description, a benchmark driven by real play
rather than an idle player, a regression test that replays a recording and checks the end state, and
later the Crawler TV show ("Crawler TV show", Game) replaying highlights.
- Record at the boundary between Presentation and Game (the requests Presentation queues:
  moves, activations, UI commands that change the simulation), not raw keys, so UI layout changes
  don't break old recordings.
- A recording stores the build version and fails clearly on a mismatch; determinism only holds for the
  same code and mods.
- Scrubbing backwards needs periodic snapshots (checkpoints) of the whole world, which needs "Data
  storage" (above). Without them, replay only plays forward from the start.
- A determinism check (replay twice, compare a world hash every N frames) catches accidental
  nondeterminism -- iteration over a hash set, unseeded `Random` -- as soon as it's introduced.

**Unreal Engine reference:** Unreal's replay system (`UDemoNetDriver`, `UReplaySubsystem`) records the
*replicated network stream* rather than input, because Unreal's simulation isn't deterministic, and
writes periodic checkpoints (full snapshots) so playback can jump to any time by loading the nearest
checkpoint and playing forward from it. Input recording is cheaper and only works because this
simulation is deterministic; the checkpoint-plus-forward-play scheme is the part to copy for scrubbing.

#### In-world scenario tests

Unit tests build pools and systems directly, and live testing has repeatedly caught input, hit-test
and measurement bugs they missed. There's nothing in between: a test that builds a real world
session from a seed, sets up a scenario (spawn a goblin two tiles from the player, give the player a
potion), runs N frames headless and asserts on the outcome (the goblin engaged, the potion was drunk,
nothing threw).
- Built on `HeadlessBenchmark`'s session setup, run as a separate MSTest category so the default suite
  stays fast.
- Scenario setup uses the same commands as Admin Mode ("Console variables and console commands",
  Engine), so a scenario found by hand in-game can be written down as a test.
- A replay ("Replay by recording input", above) is a scenario whose inputs come from a recording.

**Unreal Engine reference:** Functional Tests: an `AFunctionalTest` actor placed in a test map runs a
scenario with `PrepareTest` / `StartTest` / `FinishTest(Result)`, reports through the automation
framework, and runs from the Session Frontend or the command line. The Automation Spec framework
(`BEGIN_DEFINE_SPEC`, `Describe`/`It` with latent steps spanning frames) covers the "run N frames then
assert" shape in code. Gauntlet then runs them against a built game on CI (see "CI step for the
performance-filtered tests", High).

**Bevy reference:** Bevy makes this the default way to test: build an `App` with `MinimalPlugins` (no
window, no renderer) plus the game's own plugins, spawn the scenario through `app.world_mut()`, call
`app.update()` N times, then query the world. The same plugins as the real game, so nothing is wired
differently for tests. The lesson for here is the same one the register-first bootstrap applied to
module builds (IMPLEMENTATION-NOTES, `GameBuildPass`): a scenario test should build the session
through exactly the path the game uses (`WorldSessionBootstrapper` in headless mode), never a
test-only assembly.

#### Remote inspection of a running game

Inspecting a running game means Admin Mode's windows, by hand. There's no way for an external tool --
a script, a test harness, Claude during live testing -- to ask the running game what's going on or to
set up a situation, so live verification is a person reading the screen.
- **A local-only endpoint** (off by default, enabled by a setting or Admin Mode) that answers
  requests: list entities matching components, read an entity's components, write or add a
  component, spawn from a blueprint, run a console command ("Console variables and console commands",
  Engine), step N frames while paused, capture the frame's diagnostics.
- Requests are applied between frames on the main thread, never mid-system.
- Components are read and written through the same serializers as saves ("Data storage", above), so a
  component registered for saving is inspectable for free.
- Uses: scripted checks against a real windowed session ("spawn a goblin next to the player, advance
  120 frames, is it engaging?"), an external inspector window, and letting Claude verify gameplay
  changes in the running game without screenshots.

**Bevy reference:** the Bevy Remote Protocol (`bevy_remote`, 0.15): a JSON-RPC server (over HTTP with
`RemoteHttpPlugin`) exposing the world. Built-in methods query entities by component, get, insert and
remove components, spawn and despawn entities, and list registered types; games register their own
methods, which run as systems with world access. Everything goes through reflection, so any registered
component is visible. The editor-style inspectors in the Bevy ecosystem are built on top of it rather
than inside the game.

**Godot reference:** when a game runs from the Godot editor, the editor's *Remote* scene tree shows
the running game's live node tree, and selecting a node shows and edits its properties in the
inspector while the game runs; the debugger adds the profiler, custom monitors (see the Diagnostics
hooks item, Engine) and a "pick an object in the running game" button. It's tied to the editor rather
than being an open protocol, so Bevy's remote protocol is the better model for scripting and tests;
Godot's is the better model for the *viewer*: a live tree of entities grouped by neighborhood and tier,
filterable by component, with an editable component view.

#### Localization

All text is English string literals in code and in blueprint/item definitions (`DisplayText` formats
it but doesn't look it up). Nothing to translate yet, but retrofitting is expensive once content grows,
and mods will add text too.
- Player-facing text goes through a key → string table per language, with English as the fallback.
  Blueprint names, descriptions, item text, UI labels and notifications refer to keys.
- Formatting with named arguments and plural rules ("1 goblin", "3 goblins", and languages with more
  than two forms), and number formatting per culture.
- Mods ship their own tables and can override a built-in key.
- Crawler names and generated names stay untranslated.

**Unreal Engine reference:** `FText` is the localizable text type (vs `FString` for everything else),
created with `LOCTEXT`/`NSLOCTEXT` (a namespace, a key and the English source) or from String Tables
(CSV-backed key → text assets). The Localization Dashboard gathers every `FText` from code and assets
into `.po` files for translators. `FText::Format` uses named arguments with ICU plural and gender forms
(`{Count}|plural(one=goblin,other=goblins)`), and `FText::AsNumber`/`AsCurrency` format per culture.
The key lesson is Unreal's separation of display text from every other string at the type level, so
nothing player-facing can skip localization by accident.

**Godot reference:** `TranslationServer` with CSV or gettext `.po` tables, `tr()`/`tr_n()` (plural
forms) and automatic translation of `Control` text. The piece worth taking *now*, before any
translation exists, is pseudolocalization: a project setting that rewrites every string on the fly
-- accented characters, vowels doubled to lengthen text by a set ratio, optional brackets around each
string and fake right-to-left. It shows at once which windows clip or overflow with longer text, and
which strings aren't going through the translation path at all (they come out unaltered). A
debug-only pseudolocalization switch on whatever text lookup exists would catch layout problems in
windows that already exist.

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

## Accessibility

### Low Priority

#### UI scale option, and screen reader support later

UI sizes are fixed pixels (`HudMetrics`, per-window Chrome constants, `FontChrome` sizes), so the UI is
small on a 4K display and large on a small laptop, and there's no player control over it. Add a UI
scale setting (for example 75%–200%) on the Options menu that scales window geometry, fonts and hit
areas together, persisted with the other settings ("Layered config files", Global). Font sizes must
scale by re-rasterizing at the new size (FontStash already rasterizes on demand), not by stretching
glyphs. Saved window positions and sizes need storing in unscaled units so changing the scale doesn't
push windows off screen. Screen reader support is much larger (every element exposing a role, name
and state) and only worth it once the UI stops churning.

**Godot reference:** `Window.content_scale_factor` (and the project's stretch mode and aspect
settings) scales the whole UI by one factor, with fonts re-rendered at the scaled size, and Godot
exposes it at runtime so games offer it as a setting. Godot 4.5 added screen reader support through
AccessKit: `Control`s report a role, name and description to the OS accessibility API. AccessKit has
C bindings, so the same library is an option here when the time comes.

#### Read the OS double-click speed instead of a hardcoded window

`InventoryGridContent.DoubleClickWindowFrames` (inventory item double-click-to-activate) is a fixed
constant, not the user's actual configured Windows double-click speed (Control Panel/Settings ->
Mouse, default 500ms) -- unlike WinForms/WPF apps, which read this via
`SystemInformation.DoubleClickTime`. Retrievable without a `System.Windows.Forms` dependency (which
would force retargeting to `net10.0-windows`) via a direct `user32.dll` P/Invoke of
`GetDoubleClickTime()`, callable at startup since it's a system-wide query with no window-handle
dependency. Windows-only -- FNA itself is cross-platform, so this needs an `OperatingSystem.IsWindows()`
guard with today's hardcoded value as the non-Windows fallback. Scoped to the mouse double-click only;
`ActionTargetingController`'s separate keyboard hotbar double-tap window is a different gesture and
wouldn't read from this. Open question if picked up: does the current +25% buffer on top of the base
value still make sense once the base is the user's own real OS setting rather than a fixed guess.

### MEDIUM PRIORITY : Unsimulated-tier time -- research alternatives to freezing

Decided 2026-09-11 as a temporary position: when P2 stops simulating Borough and beyond, entities
there are **frozen** -- their timers stop and resume where they left off when they are next
simulated. That is the cheapest correct option, not
necessarily the right one: from the player's point of view, time does not pass for anything they
walked away from.

**Partly superseded 2026-09-15 by the world-scaling work:** Borough is frozen, but when an entity
is promoted out of it, every effect with an active timer **catches up** to now rather than resuming
where it left off. Interacting state (combat) doesn't catch up. What remains open here is **saved
Beyond neighborhoods**: whether reloading one from disk uses the same catch-up, and whether anything
beyond active timers (regen to full, crop-like growth, population drift) should advance with
elapsed time.

Research how other games handle time for things outside the simulated area before committing to
anything else. Starting points to verify, not settled facts:
- Minecraft: chunks beyond simulation distance are frozen outright -- nothing ticks there.
- Stardew Valley: much off-screen state advances in bulk at the day boundary rather than live.
- Animal Crossing: catches up to the real-time clock on load.
- Dwarf Fortress: off-site world simulated at a coarser, abstracted level ("world activity").
- RimWorld: world-map caravans and settlements simulated abstractly, maps unloaded otherwise.
- Catch-up on load: advance a region by the elapsed time in one bulk step when it comes back into
  range (the approach that runs into the bulk-resolution problem: bulk ticks resolve in isolation,
  so an entity can die to a lump of damage that fine-grained simulation would have offset with regen).

Things to decide from that research: whether time passes at all, whether catch-up is exact, bulk,
or statistical, and whether lethal outcomes may resolve while unobserved. Overlaps with
"Third Pause modality" -- both are "what happens to time where the player isn't". Under the 3x3
neighborhood window,
this item governs saved Beyond neighborhoods (frozen, or caught up on reload), not Borough.

### MEDIUM PRIORITY : Third Pause modality -- per-map pause

Today pause is global: `GameLoop.Update` skips `EcsContext.Update` entirely when
`MapWindow.IsPaused` or menu mode is active, so every map stops together.

Wanted: pause everything on a specific map *without* pausing the sub-map the player is currently
on. The player explores a sub-map at full speed while the map they came from is frozen rather than
running unobserved.

Notes for whoever picks this up:

- This is a third state, not a boolean. The existing two are "everything runs" and "nothing runs";
  the new one is "this map runs, those maps are frozen", which means pause stops being a property
  of the game loop and becomes a property of a map.
- It overlaps heavily with the tier rework and with P2's
  spawn-in-and-wait direction: a frozen map and a Beyond-tier region are close to the same idea
  expressed at different granularity, and it would be a shame to build two mechanisms for it. The
  tier system already carries "this entity is on a different MapLayer, therefore Beyond".
- Interacts with the off-map player reference point: while the player is on a sub-map, other maps'
  entities are tiered against the player's last on-map position. If those maps are frozen anyway,
  the reference point matters less -- but a frozen map still needs a defined tier state for when it
  unfreezes.
- Decide what "frozen" means precisely: no `ISystem.Update` visits at all (Minecraft's simulation
  distance model), or visits that are skipped per-entity. The former is cheaper and easier to
  reason about.
- Clocks: the timer wheel uses one simulation clock, passed explicitly to everything that reads
  it, so this is a wiring change. A frozen map needs its own clock (or its deadlines parked the
  same way unsimulated-tier entities are); decide which when the second map exists.
- **Bevy reference:** Bevy separates `Time<Real>` (wall clock) from `Time<Virtual>` (game time, which
  can be `pause()`d or run at `set_relative_speed(0.5)`), and `Time<Fixed>` advances from virtual
  time. Pausing is a property of a clock, not of the loop: UI and presentation read real time and keep
  animating while game time stands still. A clock per map, each pausable and with its own speed, is
  the per-map version of that, and would also give slow motion and a fast-forward for testing. Bevy
  itself has one world clock; per-map clocks would be this project's extension.
- **Godot reference:** Godot makes pause a property of a subtree. Each node has a `process_mode`
  (Inherit, Pausable, WhenPaused, Always, Disabled), inherited down the tree, and `SceneTree.paused`
  stops every Pausable node. A map's root set to Pausable and the UI set to Always gives global pause
  today; per-map pause is the same flag moved from the tree to each map's root (Disabled on the frozen
  map, Inherit on the active one). Here that's a pause state per map (or per session) checked by
  `SystemManager` when it decides which systems and tiers run, with presentation always running --
  which also argues for pause being a run condition ("Named schedules, system sets and run
  conditions", Engine) rather than `GameLoop` skipping the whole ECS.
