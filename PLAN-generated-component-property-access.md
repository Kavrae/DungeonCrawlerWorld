# Generated component property access: one description per component for inspection, editing, saves and diagnostics

Replaces the TODO.md entry "Research generated component property access -- Workshop, saves and diagnostics" (Engine,
High), removed from TODO.md on 2026-11-12 when its decisions were settled.

**Status:** decisions 1-10 settled 2026-11-12, from a read of the code. Nothing is built or measured; Phase 0's spikes
may still move decisions 5 and 6. Names of new types are provisional. Starts after
`PLAN-npc-behavior-composition.md` (see "Order against other plans").

## What the research found

### 1. Today every consumer is hand-built, and the only shared shape is a string

- **64 `*Component` structs in Game** (65 pool registrations, plus the engine's `RelatedSourceComponent<TLink>` and
  relationship links such as `AuraAnchorOwnerLink`). **60 of them hand-write `ToString()`**, and that string is the
  inspection API: `IInspectableComponentPool.CopyInspectionDataForEntity` calls it and hands back
  `InspectedComponentEntry(Type, string, version)`.
- **A string can't be edited, compared, sorted or sent as data.** `InspectionWindowContent` already shows the cost:
  `ReplaceHealthEntriesWithEffectiveMaximum` removes the generic health entries and rebuilds them by hand, because "there's
  nothing to edit". It also rebuilds every component's text every 10 updates whether or not the component changed,
  though the entry carries the pool's version.
- **Diagnostics reflects at run time already.** `PoolMemoryReport` builds a `PoolSampler<T>` per pool with
  `MakeGenericType` and a field-by-field comparer with expression trees (`ValueComparer<T>`), to count distinct values.
- **Nothing saves a component.** `ContentStore` and `SaveFolder` persist definitions only. "Data storage", "Entity
  storage", "Save and load Beyond neighborhoods" and "Remote inspection of a running game" each say they need "a
  per-pool serializer registered with the pool, generated or hand-written".
- **Content is the model of what works**: one hand-written `ContentCodec<T>` per type (45 in Game), and a
  `ContentSchema` of `ContentField`s (`ContentFieldKind`: Text, Integer, Number, Boolean, Enum, Guid, Reference, Color,
  Tag, TagSet, Object, Polymorphic, List) that is the whole of what the codec reads and writes. No Workshop code reads
  a `ContentSchema` yet, so the schema-driven field editors are still to be built -- once, for both.

### 2. A component is not a bag of fields, so "reflect every field" is the wrong generator

Reading the 64 structs, a member is one of these, and each needs different treatment per consumer:

| Member | Examples | Show | Edit | Save |
|---|---|---|---|---|
| Plain value (number, bool, enum, `Guid`, text) | `ManaComponent.CurrentMana`, `MovementComponent.MovementMode` | as is | yes | as is |
| Nested value struct | `Vector3Int`, `TargetSelection`, `ActivatableReference`, `ActionSource` | its own properties | per property | its own properties |
| Frame deadline (`uint`, `FrameDeadline.Never`) | `WaitUntilFrame`, `CooldownReadyAtFrame`, every `IScheduledTimer` | frame, plus seconds from now | yes (re-schedules through observation) | absolute, with the clock saved beside it |
| Session-local id | `SpawnRecordComponent.BlueprintId`, `AuraSourceComponent.AuraId`, `InventoryItemStackComponent.ItemId`, terrain and class/race ids | the name it stands for | pick from the catalog | the Guid, never the id |
| Entity reference | `EntityKey`, `DeadComponent.KilledBy`, `IRelationshipLink.TargetKey` | the entity's name | pick, checked (a link write can be refused) | the key, remapped on load |
| Shared definition reference | `InventoryItemStackComponent.Override`, `PendingWindupComponent.Definition`, `DisabledActivationComponent`'s `GameplayTagQuery` | a summary | no | through its `ContentCodec` |
| Derived, read-only | `ItemDefinitionId` (from `ItemId`), `DisabledMask` | yes | no | no |
| Sentinel-backed | `MovementComponent.TargetMapPosition` (a `Vector3Int?` over a private sentinel) | the property | the property | the property |
| Indexed by a key the component doesn't hold | `BodyPartStateComponent` (inline arrays by part id; the parts are on the race) | needs the game to name the rows | per row | the arrays |
| Engine bookkeeping | `TimerWheelMark`, `StackInstanceId`'s static counter | no | no | no (rebuilt by `Add`; the counter is session state) |

What follows:
- **The unit is a declared property with a kind and a meaning**, not a field. The public auto-property is the right
  default (CLAUDE.md "Component shape" already makes mutable state auto-properties), with opt-out and a hand-written
  escape for the few odd ones.
- **Meaning is the game's, not the engine's.** The engine's table says "this `ushort` is a `BlueprintId`"; Game
  registers what a `BlueprintId` is called, how it is picked and what Guid it saves as. That is the same split as
  Bevy's `MapEntities` and Unreal's redirects, and it keeps Engine free of game knowledge.
- **A readonly record struct is written whole** (read, `with`, `TrySet`); a mutable one through a write scope. Both
  notify, so an edited deadline is rescheduled by its timer wheel with no extra code.

### 3. "Workshop live inspector" means out of process

Play (F5) starts GameShell as its own process, and the Workshop's own build (`ContentSession`) is never simulated. So
the live inspector the TODO names is a remote one, over the diagnostics link:
- The link is one-way in practice: the game's `DiagnosticsLink.RunReceiver` reads and discards everything the IDE sends.
- The engine's Debug tab (`Cadence/Workshop`) can't reference Game, so it can't hold a `MovementComponent`. It needs
  the description as data (names, kinds, meanings) and the values as a stream it decodes from that description.
- That is exactly "Remote inspection of a running game" (Global), and the same encoding a save needs.

So the design must not hand out typed getters only. **One traversal over a component's properties, driven by a
visitor, serves all of them**: the in-game window formats, the link writes bytes, a save writes JSON, a snapshot copies,
a diff compares, an editor applies one value.

### 4. Cost

- Access never runs unless a consumer asks: no pool, system or write path changes. The description is static data per
  type.
- Generated traversal is direct property reads and virtual calls on a visitor: a few nanoseconds a property, no boxing,
  no strings until a consumer formats. Runtime reflection is the 50-500 ns per access the TODO names; `ToString` today
  allocates a string per component per refresh.
- Build cost: an incremental generator keyed on one attribute (`ForAttributeWithMetadataName`) reruns only for a
  struct that changed. `Cadence/Analyzers` is already loaded by every project.

## Decisions (settled 2026-11-12)

1. **Shape: a generated description plus a generated traversal, not a table of delegates.** Per component type:
   - a static `ComponentPropertyTable` (ordered `ComponentPropertyDescriptor`s: stable name, display name, kind,
     meaning, flags `ReadOnly`/`NotSaved`/`NotShown`, enum or nested type), and
   - `Read(in T, ComponentPropertyVisitor)` and `Write(ref T, int propertyIndex, in ComponentPropertyValue)`, emitted
     as straight code over the struct's members.
   A delegate per property (getter/setter returning `object`) is simpler to consume but boxes every value and can't
   feed a byte or JSON writer without a second path. *Recommended: the traversal.*
2. **What is a property: opt in per struct, opt out per member.** `[DescribedComponent]` on a `partial` struct; every
   public instance property with a getter is a property, settable when it has a setter (or, on a readonly record
   struct, through `with`); `[NotAProperty]`, `[NotSaved]`, `[PropertyMeaning(...)]` adjust a member; a private
   member joins with `[ComponentProperty]`. A member whose type is neither a known kind, a described nested struct nor
   excluded is a **build error** naming it (`CAD0002`), so a new field can't silently drop out of saves. *Recommended.*
   The alternative, every field by default, drags in bookkeeping and sentinels (finding 2).
3. **Odd components hand-write the same interface.** `BodyPartStateComponent` (and anything else the generator can't
   express) implements the table and traversal by hand; the generator skips a struct that already does. Expected: one
   or two. Why this one: it has no properties to find. Its state is two private inline arrays and a bit mask, read and
   written through methods that take a part id (`CurrentHealthOf(partId)`, `SetDisabled(partId, ...)`), and how many
   rows are real and what each is called is on the race's `BodyPartTemplate`s, not in the component. Teaching the
   generator "an indexed member, its accessor methods and its count" is a feature with one user; if a second
   component takes that shape, the hand-written one becomes the specification for it.
4. **Meanings are named in Engine and resolved by the game.** A `ComponentPropertyMeaning` is a name (`Frame`,
   `EntityKey`, and game-declared ones: `BlueprintId`, `AuraId`, `ItemId`, `TerrainTypeId`, ...). A consumer asks a
   `ComponentPropertyMeanings` registry, filled by modules in `Configure`, for: display text, the choices to pick from,
   and the persistent form (Guid) and back. A meaning with no resolver shows the raw value and **fails a save build
   check**, not a load.
5. **How a table is found: a module initializer fills a static generic slot.** The generator emits one
   `[ModuleInitializer]` per assembly that sets `ComponentPropertyTableOf<T>.Table` for each described struct. A pool
   reads its slot when asked; null means undescribed (test structs need nothing). No constraint on `Register*Pool`, no
   reflection, no per-component registration line, and a mod's assembly registers itself on load. To verify in Phase
   0: the slot doesn't pin a collectible `AssemblyLoadContext`. *Recommended.* Alternatives: a `where T :
   IComponentProperties<T>` constraint on registration (strongest guarantee, but every test struct must be described),
   or one cached reflection lookup per type at first ask.
6. **Codecs stay apart; the vocabulary and the editors are shared.** Not generated: a `ContentCodec`'s keys, omitted
   defaults, discriminators and validation are a hand-chosen file format over classes. Shared: the property kinds map
   onto `ContentFieldKind` (extended where a component needs more), so the Workshop builds one field editor per kind
   (`NumericField`, `ColorField`, `DropdownField`, ...) for content inspectors and component inspectors alike; and a
   component's `Definition` property saves through the definition's codec. Revisit generating codec boilerplate only
   if Phase 0's comparison of two codecs against generated output shows the hand-written half is mostly mechanical.
7. **Writes go through one entry point, by key, with a result.** `ComponentPropertyEdits.Apply(entityKey, component
   type, instance, property, value)` resolves the key, writes through `TrySet` or a write scope so observation fires,
   catches a relationship's refusal, and returns the previous value. **Destroyed is refused; dead is not.** A
   destroyed entity is gone and its id may already belong to something else, which is why an edit names the
   `EntityKey` and never the id: a key that no longer resolves is a refusal. A dead entity is a corpse -- still an
   entity, holding `DeadComponent` and whatever else it had -- and is edited like any other (its loot, its status
   effects, `DeadComponent` itself). A Multi pool instance is named by its `IKeyedScheduledTimer.TimerKey` where it has one, else by
   position at the time of the read (refused if the entity's chain changed). Edits apply between frames, never inside
   `SystemManager.Update`. A skeleton is never built by an edit: its skeleton-held components are editable, the rest
   are shown as today's "Unsimulated" rebuild, read-only.
8. **Undo is a list of applied edits, and only promises what it can.** Each `Apply` yields (key, property, previous,
   new); undo re-applies the previous value through the same entry point and is refused if the entity was destroyed.
   It restores the value, not the world -- the simulation may have moved on -- so the inspector offers it freely while
   paused and labels it "revert" when frames have passed.
   - **Scope: entity state in a running game, never definitions.** These edits change one entity's components: in
     Admin Mode's inspection window (Phase 3) and, later, from the Workshop's live inspector over the link (Phase 5).
   - **The Workshop's content editors are a different thing and change nothing live.** They edit definitions in
     content files, with an undo of their own over `ContentSchema`; a running game never sees the change, because no
     definition changes during a session (CLAUDE.md "Content as data"): it takes effect when the next session loads.
     So editing a spell so that its next cast behaves differently is not part of this plan, and is ruled out by that
     decision, not by anything here.
   - **What would allow live tuning without breaking that rule** (a follow-up, not built here): a tuned definition is
     an addition under a new Guid, registered at once like a crafted one, and the entity's own grant
     (`ActionInstanceComponent`'s override, a stack's `Override`) is pointed at it -- an entity edit. A windup already
     under way keeps the definition it captured.
9. **Hot path: its own namespace and an architecture test.** Everything lives in `Engine.ECS.Properties`. A test in
   the style of `WholeCatalogReadsArchitectureTests` fails if anything under `Game.Modules`, a system or an effect
   entry references it; allowed: `Game.Admin`, `Game.Views`' inspection view, the save code when it exists, diagnostics.
   A second test fails a built-in pool with no table.
10. **The generator lives in `Cadence/Analyzers`**, beside `CAD0001`, tested in `Analyzers.Tests` with the generator
    driver. One project is already wired into every build as an analyzer; a mod outside the repository gets it the way
    it gets Engine (shipped beside the reference assemblies).

## The design

- **`Engine.ECS.Properties`** (new): `DescribedComponentAttribute` and the member attributes;
  `ComponentPropertyKind` (Integer, Number, Boolean, Text, Enum, Flags, Guid, Color, Nested, Definition, Opaque);
  `ComponentPropertyDescriptor` (with an optional minimum and maximum, as `ContentField` has, from
  `[PropertyRange]`), `ComponentPropertyTable`, `ComponentPropertyMeaning`;
  `ComponentPropertyVisitor` (abstract: one method per kind, plus `BeginNested`/`EndNested`), so widened values
  (`long`, `double`) cross with no boxing and a `float` round-trips exactly; `ComponentPropertyValue` (a small
  discriminated struct for one written value); `ComponentPropertyTableOf<T>`.
- **Pools** gain `IDescribedComponentPool`: the table (or null), `ReadProperties(entityId, visitor)` once per instance
  the entity holds, with each instance's version, and `WriteProperty(...)` used only by `ComponentPropertyEdits`. It
  replaces `IInspectableComponentPool` and `InspectedComponentEntry`.
- **The generator** (`ComponentPropertyTableGenerator`): for each `[DescribedComponent] partial struct`, the table,
  `Read`, `Write` and the assembly's module initializer; diagnostics `CAD0002` (member type not describable), `CAD0003`
  (not `partial`, or nested in a non-partial type), `CAD0004` (a property name changed without `[FormerlyNamed]`, once
  saves exist -- noted, not built here).
- **Game** declares its meanings beside what they name (`BlueprintRegistry`, `AuraCatalog`, `ItemIds`,
  `TerrainRegistry`, `EntityNaming`), and keeps the one display rule that isn't a property: health against the
  effective maximum becomes a derived row a module contributes, replacing `ReplaceHealthEntriesWithEffectiveMaximum`.
- **Presentation** reads through a view (`GameViews.ComponentInspectionView`) and writes through a command
  (`AdminTools`), so `InspectionWindowContent` can lose its store exemption in
  `PresentationStoreAccessArchitectureTests` if its remaining direct reads move with it.
- **Stable names are a contract from the first save on**: a property's name is what a save and the link key on, so
  renaming one needs `[FormerlyNamed]`; the table carries a per-type hash for a cheap "same shape" check, and the
  per-module content version (`ContentTypeDeclarations.ContentVersion`) is the migration hook "Data storage" asks for.

## Phases

Each phase ends with its checks; stop after each for review and in-game testing.

### Phase 0 -- Spikes and the inventory (no production code)

1. The inventory: every member of every component and relationship link classified by finding 2's table, written into
   this plan as an appendix (kind, meaning, shown, editable, saved). This is the generator's specification and the
   first list of what a save excludes and why.
2. A prototype generator in a test project over five structs chosen to break it: `MovementComponent`,
   `InventoryItemStackComponent`, `DeadComponent`, `SpawnRecordComponent`, `PendingWindupComponent`; and
   `BodyPartStateComponent` by hand.
3. Measure, as a `TestCategory=Performance` test: read every property of 10,000 components through the prototype,
   through `ToString`, and through cached reflection.
4. Verify decision 5: a described struct in a `Mods.TestFixtures` DLL registers itself, and its load context still
   unloads.
5. Decision 6's comparison: generated output beside two existing codecs (`AuraContentCodecs`, one effect entry).
6. Confirm decisions 5 and 6 against the results, or reopen them.

**Check:**
- The appendix lists all 64 components with no member unclassified.
- The plan holds the three timings and the unload result.
- No file outside this plan, `Tests`, `Mods.TestFixtures` and `Analyzers.Tests` changed.

### Phase 1 -- Tables and the generator

1. `Engine.ECS.Properties`, the pool interface, the generator and its diagnostics.
2. Describe every built-in component (attribute, `partial`, member attributes from the appendix). `ToString` stays
   until Phase 2.
3. The two architecture tests of decision 9; generator tests (one snapshot per kind, each diagnostic).

**Check:**
- `dotnet test Analyzers.Tests/Analyzers.Tests.csproj` and `dotnet test Tests/Tests.csproj` pass.
- Add a public property of an undescribed struct type to a component: the build fails with `CAD0002` naming it.
- Remove `partial` from a described component: `CAD0003`.
- A test reads every property of every component of a built world and writes each settable one back unchanged: no
  pool's version count differs from the number of writes, and no observer throws.
- Headless A/B against the saved baseline with the `phase-performance-testing` skill: within run-to-run spread, same
  world fingerprint (nothing on the frame changed).

### Phase 2 -- Inspection reads the tables

1. `ComponentInspectionView` and the meanings' display resolvers (frames as "frame N (in 2.3 s)", ids as names, keys
   as entity names).
2. `InspectionWindowContent`'s dump: one block per component, a row per property, rebuilt only when that component's
   version changed; health's derived rows.
3. Delete the 60 `ToString` overrides, `IInspectableComponentPool`, `InspectedComponentEntry`, `ComponentInspector`.

**Check (in game, Admin Mode, F12):**
- Inspect a goblin: every component it holds is listed alphabetically with named rows; `SpawnRecordComponent` shows the
  blueprint's name, not a number.
- Hit it: health rows change; components that didn't change don't flicker or re-wrap.
- Set it on fire in lava: `BodyPartBurningTimerComponent` appears per part with a deadline that counts down in seconds.
- Kill it: `DeadComponent.KilledBy` reads as the killer's name; the window keeps showing the corpse.
- Inspect a creature in an unsimulated neighborhood: the "Unsimulated -- spawn defaults" section still appears and
  nothing is built (its tier stays what it was).
- Inspect the player with a lit torch and a potion cooling: `ActiveToggleComponent` and the cooling stack are separate
  instances, each with its own rows.

### Phase 3 -- Editing in Admin Mode

1. `ComponentPropertyEdits`, its refusals, the edit list and revert (decisions 7 and 8), reached through `AdminTools`.
2. Editable rows in the inspection window: a field editor per kind, a picker per meaning, read-only rows greyed.
3. The editors are built in UIEngine/Presentation so the Workshop's content inspector can reuse them (decision 6).

**Check (in game, Admin Mode):**
- Set the player's `ManaComponent.CurrentMana` to 0: the HUD bar empties at once and a spell is refused for mana.
- Set a burning part's next tick to a frame 10 seconds out: no burn damage for 10 seconds, then it resumes on cadence.
- Change a goblin's `MovementMode`: it changes behaviour without being re-inspected.
- Try to edit a derived or bookkeeping row: it can't take focus.
- Edit, then revert while paused: the old value is back.
- Kill a creature, then edit its corpse's `CurrencyComponent`: it applies, and looting the corpse gives the new amount.
- Edit a creature, "Regenerate" its neighborhood from the map's Admin menu (destroying it), then revert: refused with a
  message, nothing written, and whatever now holds that id is untouched.
- Edit an entity that is destroyed while its field is focused: the window releases it and nothing throws.
- Point an anchor's owner link at itself: refused with the relationship's own reason; the anchor is unchanged.

### Phase 4 -- Snapshot round trip (the proof for saves; test consumer only)

1. `EntitySnapshot`: capture an entity's described components through the tables with the persistent resolvers
   (Guids for ids, keys for entities, codecs for definitions), written as JSON through `Engine.Json`; restore onto a
   fresh entity through `Add`, so timers schedule and relationships check.
2. Tests over a built world: every built entity round-trips to equal tables; the excluded members are exactly the
   appendix's list; a snapshot restored into a second build of the same content resolves the same blueprints and auras
   though the session-local ids differ.

**Check:**
- The round-trip test passes for every blueprint `BlueprintsModule` registers, built and with a class applied.
- A component with a meaning that has no persistent resolver fails the snapshot build check by name.
- Nothing ships: no command, window or file format reaches the game. "Data storage", "Entity storage" and "Save and
  load Beyond neighborhoods" take over from here and are updated to say so.

### Phase 5 -- The Workshop's live inspector (only when wanted; could be its own plan)

1. Table descriptions and values as link messages; the game applies requests between frames (`DiagnosticsLink`'s
   receiver stops discarding).
2. A Debug tab view: entities by neighborhood and tier, filtered by component, the selected entity's components
   editable with Phase 3's editors.

**Check:** set when this phase is scheduled; it settles "Remote inspection of a running game" with it.

## Not built by this plan, and why

- **A save file format, slots, versioned migration**: "Data storage". This plan proves the per-pool half and stops.
- **Generated `ContentCodec`s** (decision 6), unless Phase 0 says otherwise.
- **Change detection across entities** ("which entities changed since frame N"): `PLAN-multithreading.md` Phase 2. The
  inspector needs only the per-component version it already has.
- **Debug drawing and the AI decision recorder**: their own item. What this gives them is the inspection rows for
  whatever components NPC behavior adds, with no per-type code.
- **`PoolMemoryReport`'s expression-tree comparer**: it works and runs once per report. Replacing it with generated
  equality is a small follow-up once tables exist, not a reason to build them.

## An in-game runner that changes values during play (asked 2026-11-12; not planned here)

A system inside the game that watches play and changes values as part of it is gameplay, so decision 9 applies to it:
- **Reading: yes, this is its stepping stone.** The tables give it, and the decision recorder, any component of any
  entity by name with no per-type code, and the descriptors (kind, meaning, range) are a ready list of what exists to
  look at. A recorder reads at frame end, outside the systems, which decision 9's allowed list covers as diagnostics.
- **Writing: not through `ComponentPropertyEdits`.** A raw property write notifies the pool and nothing else: setting
  health to 0 that way raises no damage event, credits nobody, and the entity isn't dead. It also isn't an input a
  replay or a seeded benchmark knows about. A runner acts through what gameplay already has -- effects, stat
  modifiers, commands, spawns -- so its changes follow the rules, draw from its own random stream and can be recorded
  as inputs ("Replay by recording input").
- **What it would add to this plan if it is ever wanted:** nothing in Phases 0-4. A runner that picks *what* to
  change from data would want the descriptors' meanings and ranges, which are already there.

## Order against other plans

- **After `PLAN-npc-behavior-composition.md`, all of it.** That plan is in flight and changes the components this one
  inventories (`NpcDecisionComponent` added, `TestDummyComponent` and `MovementMode` removed) and the same window
  (its decision block goes between the description and the dump). Phases 1 and 2 here sweep every component file and
  rewrite the dump, so running both at once means doing the inventory twice and merging the window twice. Its decision
  block reads a view and needs nothing from this plan. Phase 0's spikes (steps 2-5) could run earlier; the inventory
  (step 1) should not.
- **Before `PLAN-multithreading.md`'s later phases.** The two don't touch: this plan adds nothing to a frame, and its
  edits apply between frames on the main thread, which is that plan's rule already. Multithreading Phases 3-4 wait on
  measured triggers that are far off; saves and inspection are wanted sooner. Its Phases 0-1 can go either side.
- **After `PLAN-workshop-edit-items.md`** (written and confirmed 2026-11-12). The two share
  decision 6's field editors and nothing else. That plan builds them first, in `UIEngine.UI.Fields`, behind a
  description that names no content type (its Decision 9), because content schemas exist and are enforced today and
  hold the richer set of kinds. Phase 3 step 3 here then maps `ComponentPropertyDescriptor` onto those rows and adds
  only what a component needs beyond content (flags, nested structs, frame deadlines). If this plan goes first
  instead, Phase 3 builds the same rows there and that plan reuses them.
- Phase 4 should land before "Data storage" grows past window layout, so entity saves start from tables.

## Compared with Unity, Unreal and Godot

| | Unity | Unreal | Here |
|---|---|---|---|
| Where the description comes from | Serialized fields, found by reflection (`SerializedObject`); `Unity.Properties` generates property bags with a source generator and walks them with visitors | `UPROPERTY` macros read by Unreal Header Tool, generating `FProperty` objects with field offsets | A source generator over `[DescribedComponent]` structs: a table and a visitor traversal. Closest to `Unity.Properties` |
| Generic access | A property path into a serialized copy, written back with `ApplyModifiedProperties` | A pointer plus an offset, straight into the object | Through the pool, so every write is versioned and observed |
| Does a generic write run the type's own code? | No (setters are skipped; `OnValidate` after) | Only with `Setter`/`BlueprintSetter` or `PostEditChangeProperty` | Always: a property's setter, then the pool's notification |
| Per-field options | Attributes (`Range`, `HideInInspector`, `NonSerialized`, `FormerlySerializedAs`) and `PropertyDrawer`s | Specifiers (`EditAnywhere`, `SaveGame`, `Transient`, `meta=(ClampMin)`), `CoreRedirects` | Member attributes, and meanings the game resolves (display, choices, persistent form) |
| Saves | Field names in YAML or binary; renames by `FormerlySerializedAs` | Name-tagged properties in an `FArchive`, so added and removed fields load; opt-in by `SaveGame` | Name-keyed, opt-out by `[NotSaved]`, renames by `[FormerlyNamed]`; format is "Data storage"'s |
| Undo | Automatic for anything written through `SerializedObject` | `Modify()` inside a transaction | A list of applied edits; restores a value, not the world |
| Used by gameplay | No (editor); DOTS uses its own typed access | Yes: Blueprint, replication and GC all run on it | Never: an architecture test keeps gameplay off it |

- **Not taken from them:** prefab overrides and multi-object editing (blueprints are code, and one entity is inspected
  at a time), and reflection as a gameplay runtime.
- **Godot** (`get_property_list`, `set(StringName)`) is the delegate-per-name shape decision 1 turns down; its
  remote scene tree is the model for Phase 5's viewer, as "Remote inspection of a running game" already says.

## Bookkeeping

- Done 2026-11-12: the entry left TODO.md. Still to do when Phase 1 starts: point "Data storage", "Entity storage",
  "Save and load Beyond neighborhoods", "Remote inspection of a running game" and "Debug drawing and an AI debugger"
  here.
- IMPLEMENTATION-NOTES gains "Component property tables" as phases land; "Component shape" gains the described-struct
  rules.
- CLAUDE.md's ECS section gains one bullet after Phase 1 (describe a new component; the namespace gameplay may not
  use) and the Presentation section loses the dump's description after Phase 2.
