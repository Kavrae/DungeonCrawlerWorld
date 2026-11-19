# Generated component property access: one description per component for inspection, editing, saves and diagnostics

Replaces the TODO.md entry "Research generated component property access -- Workshop, saves and diagnostics" (Engine,
High), removed from TODO.md on 2026-11-12 when its decisions were settled.

**Status:** decisions 1-10 settled 2026-11-12, from a read of the code. Phase 0 built and measured 2026-11-17 (see
"Phase 0 results" and the appendix): decisions 1, 5 and 6 confirmed; its five open questions settled 2026-11-18 as
decisions 11-15. Phases 1-4 built and checked 2026-11-18 (see their results; Phase 4 settled decisions 16-19). Phase 5 only when wanted. Names of new types are
provisional. `PLAN-npc-behavior-composition.md` is complete (2026-11-13), so
nothing blocks Phase 1.

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

Settled 2026-11-18 from Phase 0's findings (its open questions 1-5, adopted as proposed):

11. **Two components are hand-written, through an `Indexed` kind.** `BodyPartStateComponent` and
    `AbilityScoresComponent` implement the table and traversal by hand (decision 3). `Indexed` carries an element
    descriptor; the visitor gets `BeginIndexed(property, count)`, one element call per row, `EndIndexed`; a write's path
    is the property index then the row. Rows are named by a meaning: `BodyPartId` (the game names them from the race's
    templates) and `AbilityScoreType` (the enum). An ability score row is present only when its granted bit is set.
12. **The kinds grow to what the components hold.** An `Optional` flag on any kind (`Nullable<T>` and nullable
    references; read as `Absent`, written as `Absent` to clear; a leaf under an empty optional is refused); `Tag` and
    `TagQuery`, as `ContentFieldKind` has them, saved by name; `EntityKey` as an integer with the built-in meaning
    `EntityKey`; and `ActionSource` as one hand-written nested type in Game, read-only, saved as its kind, key, the Guid
    of its terrain or aura, and its identity's name and crawler number -- never the interned handle.
13. **A described value type's public instance fields are members**, as its public properties are (`Vector3Int`,
    `Vector2Byte`).
14. **Read-only by rule, and by attribute where a value indexes something else.** A get-only member that names the
    instance -- a Multi pool's timer or instance key (`HeldGrantKey`, `Key`, `InstanceKey`, `StackInstanceId`), or what
    a Multi pool holds one of per entity (a cooldown's `ActionId`, a binding's `Slot`) -- stays `{ get; }` and
    read-only; every other get-only member becomes `{ get; init; }` (the appendix's "G id" and "G -> init"). A settable
    member something else is indexed by or rebuilt from takes `[ReadOnlyProperty]` (the appendix's "RO*": keyed timers'
    keys, aura sources and their expiry, `TransformComponent`, `ProcessingTierComponent`, `SpawnRecordComponent`,
    `AppliedBlueprintComponent`); such a value changes only through its owner's API.
15. **No private state drops out unnoticed.** `CAD0002` also fails a private instance field that no described
    property's accessor reads and that isn't `[NotAProperty]`; the backing field of `IScheduledTimer.TimerWheelMark` is
    recognized as engine bookkeeping and needs no attribute. A struct implementing the table by hand (decision 3) is
    exempt.

Settled 2026-11-18 at the start of Phase 4 (adopted as proposed):

16. **Every saved member can be restored.** A get-only member that names its instance becomes `{ get; init; }` with
    `[ReadOnlyProperty]` (Admin edits still refuse it); a restore writes it with `with`. The generator fails (`CAD0002`)
    a get-only auto-property, so a new one can't silently stop round-tripping. A restore writes read-only members, a
    definition (through its codec) and a tag query; a computed (derived) member is never saved or restored.
17. **Session-scoped ids are remapped within the snapshot.** Entity keys, item stack ids (a process-wide counter) and
    toggle keys (a per-session counter) are written as the snapshot's own ordinals and issued fresh on restore, every
    reference inside the snapshot rewritten. A key naming an entity outside the snapshot restores as none (credit
    shows nobody); a relationship link to one is left off and listed in the restore's result.
18. **Frames are saved relative to the capture frame** and restored relative to the restore frame, so a burn due in
    2 s is still due in 2 s; `FrameDeadline.Never` stays never, and a past frame never goes below 0.
19. **A snapshot is a set of entities**, so links inside it (an anchor and its owner) restore with remapped keys --
    the unit a neighborhood save needs.

## The design

- **`Engine.ECS.Properties`** (new): `DescribedComponentAttribute` and the member attributes;
  `ComponentPropertyKind` (Integer, Number, Boolean, Text, Enum, Flags, Guid, Color, Tag, TagQuery, Nested,
  Definition, Indexed; flags ReadOnly, NotSaved, NotShown, Optional, Derived -- decisions 11-12);
  `[ReadOnlyProperty]` (decision 14);
  `ComponentPropertyDescriptor` (with an optional minimum and maximum, as `ContentField` has, from
  `[PropertyRange]`), `ComponentPropertyTable`, `ComponentPropertyMeaning`;
  `ComponentPropertyVisitor` (abstract: one method per kind, plus `Absent`, `BeginNested`/`EndNested` and
  `BeginIndexed`/`EndIndexed`), so widened values
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

### Phase 0 results (2026-11-17)

**What was built** (all of it spike code, deleted or moved by Phase 1):
- `Mods.TestFixtures/ComponentPropertyPrototype` (its own project, not in the solution, referenced by Tests,
  Analyzers.Tests and the fixture mod below): the runtime -- kinds, flags, meaning, descriptor, table, visitor, value,
  `ComponentPropertyAccess<T>` and the slot `ComponentPropertyAccessOf<T>`. Its own assembly because a fixture mod must
  register into the host's copy of the slot from a collectible load context.
- `Analyzers.Tests/ComponentPropertyPrototype/PrototypeComponentPropertyGenerator`: an `IIncrementalGenerator` run
  through the generator driver. Phase 0 may not change Game, so assembly attributes stand in for the struct and member
  attributes (component, nested value type, hand-written type, definition type, meaning, `NotAProperty`, `NotSaved`),
  it reads Game's metadata rather than its source, and it emits a separate access class per type (public members only).
  Its output for the five components is checked in as `Tests/ComponentProperties/Prototype/GeneratedComponentPropertyAccess.g.cs`;
  a test fails and rewrites it when the generator's output changes. Tests: the snapshot, `CAD0002` naming the member,
  and every shape (each integer width, float/double, bool, string, enum, flags, Guid, nested, optional nested, optional
  enum, nullable definition, get-only, derived, public field, readonly record struct) generating code that compiles.
- `Tests/ComponentProperties/Prototype`: the hand-written `ActionSource` and `BodyPartStateComponent` access; round
  trips (read every leaf of each of the five and `BodyPartStateComponent`, write each back, read again: equal),
  nested writes through the parent, optional clear/refuse, refusals (out of range, undefined enum, undeclared flag,
  read-only, definition) leaving the value unchanged, a readonly record struct written whole; the measurement; the
  unload test.
- `Mods.TestFixtures/DescribedComponentMod` (its own project, not in the solution; Tests depends on it for build order
  only): a described struct with the generator's output written by hand, its `[ModuleInitializer]`, and an empty
  module. Not part of `Mods.TestFixtures.dll`, which must still load in a real game that has no prototype DLL.

**Timings** (step 3; Release, 10,000 components = 2,000 each of the five, median of 25 passes, three runs):

| Read every property through | ns a component | bytes allocated a component |
|---|---|---|
| The generated traversal, a visitor that sums values (no strings) | 32-34 | 0 |
| The generated traversal, a visitor formatting "Name : value" rows into one reused `StringBuilder` | 346-410 | 51 |
| `ToString` (today's inspection) | 582-681 | 371 |
| Cached reflection (`PropertyInfo`/`FieldInfo.GetValue`, recursing into nested structs) | 770-815 | 312 |

About 4 ns a leaf (the five average about eight). Formatting costs ten times the traversal and is the consumer's,
which is decision 1's point: the link and a save never format. The test asserts traversal < reflection and
formatting through it < `ToString`.

**Unload** (step 4): passes. `ModuleLoader.LoadFromDirectory` over a copy of `DescribedComponentMod.dll` constructs its
module, which runs the assembly's module initializer, which fills `ComponentPropertyAccessOf<DescribedFixtureComponent>`;
the table reads back from the default context; after `AssemblyLoadContext.Unload` and collections the context is
gone. The static of a generic instantiated over a collectible type lives with that type, so the slot doesn't pin it.

**Codecs** (step 5), `AuraContentCodecs` (`AuraDefinition`, `AuraSourceGrant`) and `DirectDamage`'s/`DirectHeal`'s:
each field appears three times -- a schema line, a write line and a read argument -- and only the write line is
mechanical. The schema line carries what was chosen by hand: a short key (`gc`), a label ("Glow colour"), a default
that is also what's omitted (`AuraMagnitude.Power`, `0.5f`), a range, a tag domain, multiline, required, a reference
table. The read argument maps onto a constructor whose order isn't the schema's (`Description` last), narrows with a
named check (`ContentNumbers.UInt16(..., "p")`) and resolves references (`Reference<AuraDefinition>`), and the write
side is asymmetric with it on purpose (`value.Aura.Id` out, a decoded definition in). Field groups are shared code
(`BodyPartTargetingContent` spans keys). Generating codecs would move every one of those choices into attributes of
the same length and save one line a field. **Decision 6 holds**: codecs stay hand-written; the vocabulary and
editors are shared.

**Decisions:** 1 confirmed by the timings. 5 confirmed, with one precision: a module initializer runs when the
assembly's code is first used, not when it is loaded -- `ModuleLoader` constructs every module type as it loads, so a
mod's tables are in place before any build, but an assembly with components and no module would have to be touched
(`RuntimeHelpers.RunModuleConstructor`). CA2255 warns on a hand-written `[ModuleInitializer]` and exempts generated
code. 6 confirmed (above).

**What Phase 1 has to do differently from the plan as written** -- settled 2026-11-18 as proposed, as decisions
11-15:
1. **Two indexed components, not one.** `AbilityScoresComponent` has the same shape as `BodyPartStateComponent`:
   private inline arrays and a mask, read through methods taking an `AbilityScoreType`. Its rows are named by the enum
   (the game needn't name them) and a row is present or not by the granted mask. Proposed: both hand-written, an
   `Indexed` kind with an element descriptor (as the prototype does), rows named by a meaning (`BodyPartId`, resolved
   from the race; `AbilityScoreType`, by the enum). Decision 3 expected one or two; it is two.
2. **Kinds beyond the plan's list.** An `Optional` flag (seven members are `Nullable<T>` or nullable references; the
   prototype passes `Absent` and writes it to clear); `Tag` and `TagQuery` like `ContentFieldKind` (`StatModifierComponent.ConditionTag`;
   `DisabledActivationComponent.Query`, `ShopComponent.AcceptedItems` -- tag ids vary per run, so they save by name);
   `EntityKey` as an integer with the built-in meaning `EntityKey` (the prototype special-cases the engine type);
   `ActionSource` as one hand-written nested type, read-only, in seven components (it is a packed `ulong` whose parts
   are derived and whose identity is an interned handle -- a save writes the name and crawler number, not the handle).
3. **Public fields count.** `Vector3Int` and `Vector2Byte` are public fields, not properties; a described value type's
   public instance fields are members (the prototype does this).
4. **Get-only auto-properties are read-only to the generator.** On a non-record struct there is no `with` without an
   `init` accessor, so `{ get; }` is constructor-only. 40 members are that today (the appendix's "G"): 21 name the
   instance, 19 are values. Proposed rule: a member that names the instance -- a Multi pool's timer or instance key
   (`HeldGrantKey`, `Key`, `InstanceKey`, `StackInstanceId`), or what a Multi pool holds one of per entity (a
   cooldown's `ActionId`, a binding's `Slot`) -- stays `{ get; }` and read-only; any other get-only member becomes
   `{ get; init; }` in Phase 1 step 2. And 14 **settable** members need a `[ReadOnlyProperty]` member attribute,
   because something else is indexed by their value or rebuilt from it (the appendix's "RO*"): a keyed timer's key
   (`AuraExposureComponent.AuraId`, `BodyPartBurningTimerComponent.PartId`), an aura source or its expiry (never changed in place,
   CLAUDE.md), `TransformComponent` (the map's occupant index -- a move is `World.MoveEntity`), `ProcessingTierComponent`
   (the stripe sets), and `SpawnRecordComponent`/`AppliedBlueprintComponent` (a rebuild reads them; editing would make
   the record lie about what built the entity).
5. **Private state can drop out silently.** Decision 2 makes a private member join only by `[ComponentProperty]`, so
   `AbilityScoresComponent` and `BodyPartStateComponent` would describe nothing if anyone forgot to hand-write them.
   Proposed: `CAD0002` also fails a private instance field that no described property's accessor reads and that isn't
   marked `[NotAProperty]` -- with `IScheduledTimer.TimerWheelMark`'s backing field recognized as bookkeeping, so the
   16 timer components need no attribute.

Also found: `RelatedSourceComponent<TLink>` is generic, so no module initializer can name its closed types --
`RegisterRelationship<TLink>` registers its table (one hand-written generic access) for each. `InventoryItemStackComponent`'s
`StackInstanceId` and `AcquiredSequence` come from static counters: a stack id is session state referenced from four
other components (`ItemHotkeyBindingComponent`, `PendingItemActivationComponent`, and through `ActivatableReference`
`PendingWindupComponent` and `ActiveToggleComponent.Owner`), so `StackInstanceId` is a meaning a save remaps like
`EntityKey`. Eight marker components have no members; each still gets an empty table (decision 9's second test).

### Phase 1 -- Tables and the generator

1. `Engine.ECS.Properties`, the pool interface, the generator and its diagnostics (decisions 11-15 included).
2. Describe every built-in component (attribute, `partial`, member attributes from the appendix): the 19 "G -> init"
   members gain `init`, the 14 "RO*" members `[ReadOnlyProperty]`; `BodyPartStateComponent`, `AbilityScoresComponent`
   and `ActionSource` by hand; `RelatedSourceComponent<TLink>`'s table registered by `RegisterRelationship`. `ToString`
   stays until Phase 2. Delete the Phase 0 spike (Bookkeeping).
3. The two architecture tests of decision 9; generator tests (one snapshot per kind, each diagnostic).

**Check:**
- `dotnet test Analyzers.Tests/Analyzers.Tests.csproj` and `dotnet test Tests/Tests.csproj` pass.
- Add a public property of an undescribed struct type to a component: the build fails with `CAD0002` naming it.
- Remove `partial` from a described component: `CAD0003`.
- Add an unread private field to a described component: `CAD0002` naming it.
- Edit a "G id" or "RO*" member through the table: refused, nothing written.
- A test reads every property of every component of a built world and writes each settable one back unchanged: no
  pool's version count differs from the number of writes, and no observer throws.
- Headless A/B against the saved baseline with the `phase-performance-testing` skill: within run-to-run spread, same
  world fingerprint (nothing on the frame changed).

### Phase 1 results (2026-11-18)

- **Built:** `Engine.ECS.Properties` (attributes, kinds and flags, descriptor, table with a shape hash, meaning names,
  visitor, value, `ComponentPropertyAccess<T>`, `ComponentPropertyAccessOf<T>`, `IDescribedComponentPool` on all three
  pool kinds); `Cadence/Analyzers/ComponentProperties` (the generator, `CAD0002`, `CAD0003`); every built-in component
  described -- 62 generated, `AuraAnchorOwnerLink`, two hand-written in `Game/ComponentProperties` with `ActionSource`,
  and `RelatedSourceComponent<TLink>` registered by `RegisterRelationship` -- plus `Vector3Int`, `Vector2Byte`,
  `TargetSelection`, `ActivatableReference` as values and `ActivatableDefinition`, `LootboxReward` as definitions; the
  19 "G -> init" members took `init`, the 14 "RO*" members `[ReadOnlyProperty]`; `Game.GamePropertyMeanings`. The
  Phase 0 spike is deleted; `Mods.TestFixtures` holds a described component of its own for the unload test.
- **Tests:** `Analyzers.Tests` -- a sample of every kind compiled, run (registered, read, every write and refusal) and
  snapshotted (`Snapshots/EveryShape.PropertyTable.g.cs.txt`), each diagnostic, and what doesn't report. `Tests` --
  `ComponentPropertyAccessTests` (round trips, nested, optional, refusals, flags, records, both hand-written tables),
  `ComponentPropertyWorldRoundTripTests` (neighborhood (0,0) populated, the player made, 180 frames run, then every
  instance of every pool read and every value written back through the pool: values unchanged, one version a write,
  none for a refusal), `ComponentPropertyArchitectureTests` (decision 9's two), `ModComponentPropertyTableTests`
  (registration through `ModuleLoader`, then unload), `ComponentPropertyMeasurementTests` (Performance).
- **Checks:** all three suites pass (Tests 4554, Analyzers.Tests 26, Workshop.Tests 391). On `ManaComponent`, a
  `List<int>?` property and an unread private field each failed the build with `CAD0002` at their line, and removing
  `partial` with `CAD0003`. Headless A/B, Release, against HEAD's own build (`git archive` of 7afde954, saved as
  `baseline-build-release-pre-property-tables`): the same world fingerprint, `EcsContext.Update` 1.509 against 1.502
  ms/frame (-0.4%), no row beyond A's run-to-run range (`Log/phase-benchmarks/ab-20261118-140000.json`).
- **Differs from the plan as written:** the generated access is a nested `PropertyAccess` class inside each struct
  (static `Description`, `ReadProperties`, `WriteProperty`; a component's adds `Instance`), which is how a holder calls
  a nested value's; a `[DescribedByHand]` attribute names a hand-written access so the generator can call or register
  it; `TagQuery` is read-only until a query has a written form; `[PropertyRange]` (a descriptor's minimum and maximum) is
  not built -- no component needs one yet -- and the `NotShown` flag exists but nothing sets it.

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

### Phase 2 results (2026-11-18; in-game checks above passed)

- **Built:** `ComponentPropertyMeanings` (Engine: the registry, `IComponentPropertyMeaningResolver` with display only --
  integer, Guid, an indexed row's name -- and the Frame/Frames resolvers); `GameComponentPropertyMeanings`
  (`Game/ComponentProperties`: EntityKey, EntityId, BlueprintId, AuraId, ItemId, ItemDefinitionId, ActionId,
  AchievementId, TerrainTypeId, BodyPartId, AbilityScoreType, UtcTicks, Floor; MapPosition, Tiles, SpriteName,
  StackInstanceId, HeldGrantKey, BehaviorSetId and BehaviorId show their raw value); `ComponentInspectionView`
  (`GameViews.ComponentInspection`); `InspectionWindowContent`'s dump on it. Deleted: the 60 component `ToString`s,
  `IInspectableComponentPool`, `InspectedComponentEntry`, `ComponentInspector`, `ReplaceHealthEntriesWithEffectiveMaximum`
  and their tests (and `SimpleHealthComponentTests`, which tested only `ToString`); the skeleton and rebuilder tests
  compare entities through `Tests/ComponentProperties/ComponentSnapshots` instead.
- **Tests:** `ComponentInspectionViewTests` (health's derived row, body part rows by the race's names and none for
  unused slots, a frame in seconds and Never as "never", an `ActionSource` as one row and a key as its entity, one
  block per instance sorted, a write changing only its instance's version, the window's row text),
  `DescribedComponentPoolTests` (each pool kind's table reads and writes). Tests 4553, Analyzers.Tests 26,
  Workshop.Tests 391, all passing.
- **Differs from the plan as written:** the resolvers are registered by `GameComponentPropertyMeanings` beside the
  view, not by modules in `Configure` -- a module naming the registry would break decision 9's test, and nothing yet
  needs a mod to add a meaning. Derived rows are the view's (health only), not a module contribution. A block per
  instance replaces the old grouping of equal Multi instances ("x3"). `InspectionWindowContent` keeps its store
  exemption: its subject block still reads the store. An untouched creature has no `BodyPartStateComponent` (state is
  added when a part is first hurt), so its dump shows no per-part rows until it is hit.

### Phase 3 -- Editing in Admin Mode

1. `ComponentPropertyEdits`, its refusals, the edit list and revert (decisions 7 and 8), reached through `AdminTools`.
2. Editable rows in the inspection window: a field editor per kind, a picker per meaning, read-only rows greyed.
3. The editors are built in UIEngine/Presentation so the Workshop's content inspector can reuse them (decision 6).

**Check (in game, Admin Mode):**
- Set the player's `ManaComponent.CurrentMana` to 0: the HUD bar empties at once and a spell is refused for mana.
- Set a burning part's next tick to a frame 10 seconds out: no burn damage for 10 seconds, then it resumes on cadence.
- Set a goblin's `NpcDecisionComponent.WaitUntilFrame` 10 seconds out: it stops deciding until then, without being
  re-inspected. (Written as "change a goblin's `MovementMode`" before the NPC behavior plan removed that member.)
- Try to edit a derived or bookkeeping row: it can't take focus.
- Edit, then revert while paused: the old value is back.
- Kill a creature, then edit its corpse's `CurrencyComponent`: it applies, and looting the corpse gives the new amount.
- Edit a creature, "Regenerate" its neighborhood from the map's Admin menu (destroying it), then revert: refused with a
  message, nothing written, and whatever now holds that id is untouched.
- Edit an entity that is destroyed while its field is focused: the window releases it and nothing throws.
- Point an anchor's owner link at itself: refused with the relationship's own reason; the anchor is unchanged.

### Phase 3 results (2026-11-18; in-game checks above passed)

- **Built:** `ComponentPropertyEdits` (Engine.ECS.Properties): `Apply(target, path, value)` and `Revert(edit)`, the
  `Applied` list, `LatestFor` and `FramesHavePassedSince`; a `ComponentPropertyTarget` names the entity by key and the
  instance by keyed-timer key where it has one, else, among several (a Multi pool), by its value -- one identity shared
  by every edit of that instance and moved along by each, so it is found wherever its position moves and refused once
  anything else changed it (`ComponentPropertyEdits.TargetOf`; review fix 2026-11-19, replacing position plus the
  instance count it was read with, which wrote the wrong instance when others came and went); refusals
  (`ComponentPropertyRefusals`): destroyed, during simulation, instances changed, value refused (read-only, out of range,
  not a choice), no such instance, and a relationship's own reason. `IDescribedComponentPool.WriteProperty` now reports
  its refusal and asks a pool's owner before storing (`PropertyWriteCheck`, which `Relationship<TLink>` sets on its
  links -- a refused link is never written, where a write straight to the pool is stored, then removed and thrown), and
  finds keyed timers (`TimerKeyOf`, `TryFindInstanceByTimerKey`). `ComponentPropertyMeanings` resolvers offer choices
  (`TryGetChoices`): blueprints, auras, terrain, items (by handle and by Guid) and actions. `ComponentPropertyFields`
  (UIEngine.UI.Fields) maps a descriptor onto a `FieldRow` and a committed field value back; `FieldRow` gained
  `ShowValue` (an outside change, never under an editor that has focus) and `SetHint`. `AdminTools.ComponentPropertyEdits`.
  In Admin Mode the window shows each component instance as a section header and a field row per property --
  editable where the table writes it, greyed text where it doesn't -- with an edited row marked and its revert button
  ("Undo" while no frame has passed, "Revert ... the simulation has moved on" after), and a refusal on the row's label.
  Detail mode keeps the text blocks. The map's Admin menu offers "Revert last edit" (the same revert), which shows a
  refusal at the cursor -- the way to revert once the entity is gone and the window has let it go.
- **Tests:** `ComponentPropertyEditsTests` (apply and revert, a destroyed entity's edit and revert refused while its
  id's successor is untouched, a keyed timer found by its key after the chain moved, an unkeyed instance refused once
  the count changed, a read-only value refused, a self-link refused and never written), `ComponentPropertyFieldsTests`
  (integer limits, a frame's hint, an optional enum's "none", flags as bits, a meaning's searchable choice, read-only
  and heading rows, a Guid that must parse). Tests 4566, Analyzers.Tests 26, Workshop.Tests 391, all passing.
- **Differs from the plan as written:** an unkeyed Multi instance is refused when the entity's count of that component
  changed since the read (a narrower test than "the chain changed", which every timer write would trip). Clearing an
  optional nested value (a movement target) isn't offered in the window; an optional enum can be set to "none".
  Choices for an entity key (pick an entity) aren't offered: a key is typed, with its entity's name as the hint.
- **Found in testing, fixed:** the editable view crashed on open (an instance was looked up before it was recorded).
  While the game ran, the view rebuilt whenever a component instance came or went (a cooldown, an exposure, a status
  effect), which sent the scroll to the top and moved focus to whatever pooled row replaced the focused one. Fields now
  appear only in an explicit **Edit mode**: the inspector's "Edit" title button (enabled for an entity inspected in
  Admin Mode) holds the simulation paused while it is on (`MapViewState.IsEditingComponents`, OR'd into
  `MapWindow.IsPaused` beside the player's own Space pause, which it leaves as it was) and ends with the Admin
  inspection or when the window is minimized; out of it, Admin shows the text blocks. A rebuild of the same entity or
  tile keeps its scroll position.
- **Second round of testing:** the Edit button is on the title bar only while Admin Mode is on
  (`Window.SetTitleButtonShown`: hidden, never detached, so it still closes with the window). A search box above the view (kept through rebuilds, the view now a scrolling body below
  it) filters an inspected entity's component blocks by name. A text box set from outside shows the start of a long
  value (`TextBox.SetText` puts the caret at the start). A read-only value wraps and its row grows to fit
  (`FieldRow`, the Workshop's rows too). An ability score base edit left its total stale: the total is the one value a
  component caches from others (every other stat is read live from base and modifiers), so `ComponentPropertyEdits`
  takes refreshes per component type (`RefreshAfterWrite<T>`), run after each edit and revert, and Admin's edits
  recompute the entity's totals (`ComponentPropertyEditCaches`, `AbilityScoreEffects.RecomputeAll`); a stat modifier's
  amount is read-only in its table, and its expiry recomputes through its own path. A raw base edit still publishes no
  `AbilityScoreBaseValueChangedEvent`, so it unlocks no achievement.

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

### Phase 4 results (2026-11-18; reviewed and accepted)

- **Decisions 16-19** settled at the start of the phase (above).
- **Restoring** (Engine.ECS.Properties):
  - `ComponentPropertyAccess<T>.Restore` writes what an edit refuses: read-only members, a definition and a tag query
    (`ComponentPropertyValue.FromDefinition`/`FromTagQuery`, its new `Reference`). A value under an absent optional
    starts that optional from its default. A derived member is still refused.
  - The generator emits one `WriteProperty(..., bool restoring)` behind `Write` and `Restore`, and a read-only member
    is now separate from one with no setter. Hand-written accesses implement `Restore`:
    - `BodyPartStateComponent`: its writes.
    - `AbilityScoresComponent`: a base grants the score; the total is recomputed by the host.
    - `ActionSource`: rebuilt part by part, its identity interned again. Its table gained the identity's cause, and
      its terrain and aura rows are now optional, absent unless the kind holds one, because aura id 0 is a real aura.
    - `RelatedSourceComponent`: refused, since the relationship rebuilds it.
  - `CAD0002` fails a get-only auto-property that is saved. The 20 get-only members became `{ get; init; }` with
    `[ReadOnlyProperty]`.
- **Pools:** `IDescribedComponentPool.Restore(entityId, instances)` builds every instance from its leaves
  (`ComponentPropertyLeaf`), asks the owner (a relationship's link) before adding any, and adds them through `Add`.
  A Multi pool adds last first, so they read back in the saved order.
- **Snapshots** (Engine.ECS.Properties):
  - `EntitySnapshots.Capture`/`Restore`/`Check`, over `ComponentPropertyPersistence`. It holds each meaning's
    persistent form (`IComponentPropertyMeaningPersistence`) and each definition type's
    (`IComponentDefinitionPersistence`). The engine's meanings:
    - frames relative to the capture, "never" kept;
    - entity keys as the entity's position in the snapshot, null outside it;
    - durations, tile counts and map positions as stored;
    - `RegisterRemapped` for a counter's values: snapshot ordinals, issued fresh on restore.
  - Anything else is saved as itself (`EntitySnapshotValues`): enums and flags by name, Guids as content writes them,
    tags and tag queries by name.
  - JSON has one object per instance, keyed by property name. Nested values are objects, indexed rows are lists, and
    an absent optional is left out; a restore writes it as absent, since a default isn't always absent
    (`MovementComponent` keeps a sentinel).
  - A restore creates every entity first (`IEntitySnapshotHost.CreateEntity`), restores links after every other
    component, then finishes each entity. A component it can't restore is left off and listed.
- **Game** (`Game/ComponentProperties/GameEntitySnapshots`):
  - Blueprints and behavior sets are saved by Guid; a composed blueprint (`BlueprintRegistry.IsComposed`) is saved as
    its includes and composed again.
  - Auras by Guid, loaded from content and registered if nothing has loaded them; item handles by item Guid; terrain
    by key; behaviors by Guid.
  - Stack ids and toggle keys are remapped (`InventoryItemStackComponent.NextStackInstanceId`, `Toggles.IssueKey`).
  - Actions, items and windup definitions go through their codecs. `LootboxReward` gained one.
  - The host creates each entity tiered where it stood, adds its transform unplaced, places it through
    `World.PlaceEntityOnMap`, records its spawn move and recomputes its ability score totals. A skeleton isn't
    captured.
- **Tests:** `EntitySnapshotTests`:
  - the game's check is empty, and a meaning with no persistent form fails by name;
  - the members left out are exactly the appendix's list;
  - every built entity of a populated neighborhood after 3 s of play (over 1,000) restores into a second build and
    reads back byte-equal;
  - every spawnable blueprint, built and with Tank applied, after 1 s, does the same;
  - an anchor and its owner restore linked, and an anchor alone restores unlinked with the link listed;
  - a burn due in 2 s is due in 2 s after restore, and never stays never.
  - Generator tests cover `Restore` and the get-only error. Tests 4575, Analyzers.Tests 27, Workshop.Tests 392, all
    passing.
- **Differs from the plan as written:**
  - A skeleton isn't captured: its spawn record is what a neighborhood save keeps.
  - Where an entity stands and its tier are the host's, so a restored entity is placed and tiered as a spawn is.

### Phase 5 -- The Workshop's live inspector (only when wanted; could be its own plan)

1. Table descriptions and values as link messages; the game applies requests between frames (`DiagnosticsLink`'s
   receiver stops discarding).
2. A Debug tab view: entities by neighborhood and tier, filtered by component, the selected entity's components
   editable with Phase 3's editors.

**Check:** set when this phase is scheduled; it settles "Remote inspection of a running game" with it.

Tracked in TODO.md as "Workshop live inspector" (Engine, Low Priority), the only item there from this plan.

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
- Phase 1 deletes the Phase 0 spike: `Mods.TestFixtures/ComponentPropertyPrototype`, `Mods.TestFixtures/DescribedComponentMod`,
  `Analyzers.Tests/ComponentPropertyPrototype`, `Tests/ComponentProperties/Prototype`, and their project references --
  keeping the unload test and the measurement, pointed at the real tables.

## Appendix: the component inventory (Phase 0, 2026-11-17)

Every member of every component a built-in pool holds (64 in Game, plus `BehaviorCommitment`, the relationship link
and `RelatedSourceComponent<TLink>` from Engine), and the nested value types they hold. This is the generator's
specification and the first list of what a save leaves out.

Key -- **Kind**: Int (any integer width), Num (float/double), Bool, Text, Enum, Flags, Guid, Color, Key (an
`EntityKey`: an integer with the meaning EntityKey), Nested (a described value type, below), Def (a shared reference
type, shown as a summary), Tag/TagQuery, Indexed (hand-written rows); "?" = Optional. **Edit**: yes (a setter), with
(written whole through `with`, a record struct), G (`{ get; }` today: constructor-only; "G id" names the instance and
stays read-only, "G -> init" becomes `{ get; init; }` under open question 4), RO (read-only: derived, a definition, or
`ActionSource`), RO* (settable today, needs `[ReadOnlyProperty]`: something is indexed by or rebuilt from it).
**Saved**: yes, Guid (a session-local id saved as the Guid its meaning resolves), remap (a key or stack id remapped on
load), name (a tag, by name), codec (through the definition's `ContentCodec`), no. Every member is shown unless
marked otherwise. Private `_timerWheelMark` fields (16 timer components) and explicit interface members
(`IScheduledTimer.NextTickFrame`/`TimerWheelMark`, `IKeyedScheduledTimer.TimerKey`, `IHotkeySlotBinding.BoundId`) are
engine bookkeeping or aliases: not members, not saved (a wheel rebuilds its mark on `Add`).

### Markers (no members; an empty table -- presence is the state)

`ContainerComponent`, `ForceBlockingComponent` (Multi, counted), `InventoryComponent`, `ItemPileComponent`,
`LootedComponent`, `MeleeDisabledComponent`, `MovementDisabledComponent`, `UnlimitedStackCountComponent`.

### Components

| Component (pool) | Member | Type | Kind | Meaning | Edit | Saved |
|---|---|---|---|---|---|---|
| AbilityScoresComponent (Packed) | base score per type | ushort[7] | Indexed (hand) | AbilityScoreType | yes | yes |
| | total per type | ushort[7] | Indexed (hand) | AbilityScoreType | RO (modifiers' result) | no |
| | granted mask | byte | -- (a row's presence) | | yes (grant or ungrant a row) | yes |
| AchievementUnlockedComponent (Multi) | AchievementId | Guid | Guid | AchievementId | G id | yes |
| | EarnedAtUtcTicks | long | Int | UtcTicks (shown as a date) | G -> init | yes |
| UnclaimedAchievementLootboxComponent (Multi) | AchievementId | Guid | Guid | AchievementId | G id | yes |
| | Reward | LootboxReward (record class) | Def | | RO | codec |
| PotionCooldownComponent (Packed, timer) | TotalFrames | ushort | Int | Frames (a duration) | yes | yes |
| | ExpiresAtFrame | uint | Int | Frame | yes | yes |
| ActionCooldownComponent (Multi) | ActionId | Guid | Guid | ActionId | G id | yes |
| | ReadyAtFrame | uint | Int | Frame | yes | yes |
| ActionHotkeyBindingComponent (Multi) | Slot | HotkeySlot | Enum | | G id | yes |
| | ActionId | Guid | Guid | ActionId | yes | yes |
| ActionInstanceComponent (Multi) | ActionId | Guid | Guid | ActionId | G id | yes |
| | Override | ActionDefinition? | Def? | | RO | codec |
| ActiveToggleComponent (Multi, keyed timer) | Key | uint | Int | HeldGrantKey | G id | remap |
| | Owner | ActivatableReference | Nested | | G id | yes |
| | NextTickFrame | uint | Int | Frame | yes | yes |
| DisabledActivationComponent (Multi, keyed timer) | Query | GameplayTagQuery | TagQuery | | G -> init | name |
| | ExpiresAtFrame | uint | Int | Frame | yes | yes |
| | InstanceKey | int | Int | | G id | yes |
| DodgingComponent (Packed, timer) | ExpiresAtFrame | uint | Int | Frame | yes | yes |
| HotkeyExpansionUnlockComponent (Packed) | UnlockedSlotCount | byte | Int | | yes | yes |
| PendingActionActivationComponent (Packed) | ActionId | Guid | Guid | ActionId | yes | yes |
| | Selection | TargetSelection | Nested | | yes | yes |
| PendingWindupComponent (Packed, timer) | Activatable | ActivatableReference | Nested | | yes | yes |
| | Definition | ActivatableDefinition | Def | | RO | codec |
| | Selection | TargetSelection | Nested | | yes | yes |
| | ReadyAtFrame | uint | Int | Frame | yes | yes |
| | ActivatedFromSlot | HotkeySlot? | Enum? | | yes | yes |
| ScrollMasteryComponent (Multi) | SpellId | Guid | Guid | ActionId | G id | yes |
| | UsageCount | ushort | Int | | yes | yes |
| AuraAnchorComponent (Packed) | PlacedBy | ActionSource | Nested (hand) | | RO | yes |
| AuraExposureComponent (Multi, keyed timer) | AuraId | byte | Int | AuraId | RO* (timer key) | Guid |
| | Refused | bool | Bool | | yes | yes |
| | NextTickFrame | uint | Int | Frame | yes | yes |
| AuraSourceComponent (Multi) | AuraId | byte | Int | AuraId | RO* | Guid |
| | Size | byte | Int | | RO* | yes |
| | Power | ushort | Int | | RO* | yes |
| | HeldGrantKey | uint | Int | HeldGrantKey | G id | remap |
| AuraSourceExpiryComponent (Packed, timer) | AuraId | byte | Int | AuraId | RO* | Guid |
| | ExpiresAtFrame | uint | Int | Frame | yes | yes |
| BodyPartBurningTimerComponent (Multi, keyed timer) | PartId | byte | Int | BodyPartId | RO* (timer key) | yes |
| | StackCount | byte | Int | | yes | yes |
| | NextTickFrame | uint | Int | Frame | yes | yes |
| | Source | ActionSource | Nested (hand) | | RO | yes |
| BurningTimerComponent (Packed, timer) | NextTickFrame | uint | Int | Frame | yes | yes |
| | StackCount | byte | Int | | yes | yes |
| | Source | ActionSource | Nested (hand) | | RO | yes |
| ClassMembershipComponent (Multi) | ClassId | ushort | Int | BlueprintId | G id | Guid |
| | GrantedBy | ClassGrantKind | Enum | | G -> init | yes |
| | AcquisitionOrder | byte | Int | | G -> init | yes |
| | ExpiresAfterFloor | ushort | Int | Floor (0 = never) | G -> init | yes |
| | IsPermanent | bool | Bool | | RO (derived) | no |
| ClassSlotsComponent (Packed) | Class1, Class2 | ushort | Int | BlueprintId (0 = empty) | yes | Guid |
| | Primary | ushort | Int | BlueprintId | RO (derived) | no |
| ActionLockComponent (Packed) | CurrentLockTotalFrames | ushort | Int | Frames | yes | yes |
| | UnlockedAtFrame | uint | Int | Frame | yes | yes |
| DisplayTextComponent (Packed) | Name, Description | string | Text | | yes | yes |
| GlyphComponent (Packed) | Glyph | string | Text | | yes | yes |
| | GlyphColor | Color | Color | | yes | yes |
| NonBlockingComponent (Multi, counted) | Kind | NonBlockingKind | Flags | | G -> init | yes |
| SpriteComponent (Packed) | SpriteName | string | Text | SpriteName | yes | yes |
| | Seed | uint | Int | | yes | yes |
| TransformComponent (Direct, skeleton) | Position | Vector3Int | Nested | MapPosition | RO* (map index) | yes |
| | Size | Vector2Byte | Nested | | RO* (map index) | yes |
| CrawlerComponent (Packed) | CrawlerNumber | int | Int | | G -> init | yes |
| CurrencyComponent (Packed) | Gold, Credits | int | Int | | yes | yes |
| DeadComponent (Packed, record) | KilledBy | ActionSource | Nested (hand) | | RO | yes |
| | DiedAtFrame | long | Int | Frame | with | yes |
| | LootOwnerEntityKey | EntityKey | Key | EntityKey | with | remap |
| BodyPartStateComponent (Packed) | current health per part | float[16] | Indexed (hand) | BodyPartId | yes | yes |
| | regen locked until per part | uint[16] | Indexed (hand) | BodyPartId, Frame | yes | yes |
| | disabled per part | a bit of ushort | Indexed (hand) | BodyPartId | yes | yes |
| | DisabledMask | ushort | Int | | RO (derived), not shown | no |
| DamageContributionComponent (Multi) | Source | ActionSource | Nested (hand) | | G id | yes |
| | TotalDamageDealt | float | Num | | yes | yes |
| | FirstHitFrame | uint | Int | Frame | G -> init | yes |
| DamageLedgerExpiryComponent (Packed, timer) | LastDamagedFrame | uint | Int | Frame | yes | yes |
| | ExpiresAtFrame | uint | Int | Frame | yes | yes |
| SimpleHealthComponent (Packed) | CurrentHealth | float | Num | | yes | yes |
| | BaseMaximumHealth | float | Num | | yes | yes |
| InventoryDisabledComponent (Packed) | IsDisabled | bool | Bool | | yes | yes |
| InventoryItemStackComponent (Multi, keyed timer) | ItemId | ushort | Int | ItemId | G id | Guid |
| | ItemDefinitionId | Guid | Guid | | RO (derived) | no |
| | StackInstanceId | uint | Int | StackInstanceId | G id | remap |
| | AcquiredSequence | uint | Int | | yes | yes (its counter is session state) |
| | Quantity | ushort | Int | | yes | yes |
| | Override | ItemDefinition? | Def? | | RO | codec |
| | IsDivergent | bool | Bool | | yes | yes |
| | CooldownReadyAtFrame | uint | Int | Frame (Never = none) | yes | yes |
| ItemHotkeyBindingComponent (Multi) | Slot | HotkeySlot | Enum | | G id | yes |
| | StackInstanceId | uint | Int | StackInstanceId | yes | remap |
| MaxStackSizeComponent (Packed) | Value | ushort | Int | | G -> init | yes |
| PendingItemActivationComponent (Packed) | StackInstanceId | uint | Int | StackInstanceId | yes | remap |
| | Selection | TargetSelection | Nested | | yes | yes |
| | ActivatedFromSlot | HotkeySlot? | Enum? | | yes | yes |
| ManaComponent (Packed) | CurrentMana | float | Num | | yes | yes |
| | BaseMaximumMana | float | Num | | yes | yes |
| MovementComponent (Packed) | TargetMapPosition | Vector3Int? (over a private sentinel) | Nested? | MapPosition | yes | yes |
| | NextMapPosition | Vector3Int? (over a private sentinel) | Nested? | MapPosition | yes | yes |
| CorpseLootRetryComponent (Packed, record) | RetryAfterFrame | uint | Int | Frame | with | yes |
| NpcDecisionComponent (Packed) | BehaviorSetId | ushort | Int | BehaviorSetId | yes | Guid (or rebuilt from grants) |
| | WaitUntilFrame | uint | Int | Frame | yes | yes |
| ParalysisTimerComponent (Packed, timer) | ExpiresAtFrame | uint | Int | Frame | yes | yes |
| | StackCount | byte (`=> 1`) | Int | | RO (derived) | no |
| PoisonTimerComponent (Packed, timer) | NextTickFrame | uint | Int | Frame | yes | yes |
| | StackCount | byte | Int | | yes | yes |
| | RemainingDurationTicks | ushort | Int | | yes | yes |
| | Source | ActionSource | Nested (hand) | | RO | yes |
| ProcessingTierComponent (Direct, record, skeleton) | Tier | ProcessingTierLevel | Enum | | RO* (stripe sets) | no (resolved on load) |
| RaceSlotsComponent (Packed) | Race1, Race2 | ushort | Int | BlueprintId (0 = empty) | yes | Guid |
| | Primary | ushort | Int | BlueprintId | RO (derived) | no |
| ShopComponent (Packed) | AcceptedItems | GameplayTagQuery? | TagQuery? | | G -> init | name |
| | BuyMultiplier, SellMultiplier | float | Num | | G -> init | yes |
| ShopStockPreferenceComponent (Multi) | ItemDefinitionId | Guid | Guid | ItemDefinitionId | G id | yes |
| | PreferredStockLevel | byte | Int | | G -> init | yes |
| ExpiringStatModifierComponent (Packed, timer) | NextTickFrame | uint | Int | Frame | yes | yes |
| StatModifierComponent (Multi) | Target | StatModifierTarget | Enum | | G -> init | yes |
| | Operation | StatModifierOperation | Enum | | G -> init | yes |
| | Polarity | StatModifierPolarity | Enum | | G -> init | yes |
| | CanModify | bool | Bool | | G -> init | yes |
| | Magnitude | float | Num | | G -> init | yes |
| | ExpiresAtFrame | uint | Int | Frame (Never = permanent) | yes | yes |
| | Source | ActionSource | Nested (hand) | | G id | yes |
| | ConditionTag | GameplayTag | Tag | | G -> init | name |
| StatusEffectImmunityComponent (Multi, keyed timer) | EffectType | StatusEffectType | Enum | | G id | yes |
| | HeldGrantKey | uint | Int | HeldGrantKey | G id | remap |
| | ExpiresAtFrame | uint | Int | Frame | yes | yes |
| TerrainContactExposureComponent (Packed, timer) | NextTickFrame | uint | Int | Frame | yes | yes |
| | TerrainTypeId | ushort | Int | TerrainTypeId | yes | Guid (terrain key) |
| | Refused | bool | Bool | | yes | yes |
| AppliedBlueprintComponent (Multi, record) | BlueprintId | ushort | Int | BlueprintId | RO* (rebuilds) | Guid |
| | Order | ushort | Int | | RO* (rebuilds) | yes |
| SpawnRecordComponent (Direct, record, skeleton) | BlueprintId | ushort | Int | BlueprintId | RO* (rebuilds) | Guid |
| | Flags | SpawnFlags | Flags | | RO* (rebuilds) | yes |
| | Seed | uint | Int | | RO* (rebuilds) | yes |

### Engine types a built-in pool holds

| Type (pool) | Member | Type | Kind | Meaning | Edit | Saved |
|---|---|---|---|---|---|---|
| BehaviorCommitment (Packed, NpcBehavior's) | BehaviorSessionId | ushort | Int | BehaviorId (0 = none) | yes | Guid |
| | CommittedUntilFrame | uint | Int | Frame | yes | yes |
| | Target | EntityKey | Key | EntityKey | yes | remap |
| AuraAnchorOwnerLink (relationship link, record) | TargetKey | EntityKey | Key | EntityKey | with (checked by the relationship; may be refused) | remap |
| | HeldGrantKey | uint | Int | HeldGrantKey | G id | remap |
| RelatedSourceComponent&lt;TLink&gt; (Multi, kept by the relationship) | SourceEntityId | int (an id, not a key) | Int | EntityId | RO | no (rebuilt from the links) |

### Nested value types

| Type | Member | Type | Kind | Meaning | Edit |
|---|---|---|---|---|---|
| Vector3Int (Engine) | X, Y, Z | int (public fields) | Int | | yes |
| | Volume | int | -- | | not a member (`[NotAProperty]`) |
| Vector2Byte (Engine) | X, Y | byte (public fields) | Int | | yes |
| TargetSelection (record) | Mode | TargetingMode | Enum | | with |
| | AimedTile | Vector3Int | Nested | MapPosition | with |
| | MarkedEntity | EntityKey | Key | EntityKey | with |
| | MarkedFootprintOffset | Vector2Byte | Nested | | with |
| | MaximumRange, AreaSize | ushort | Int | Tiles | with |
| | HasMarkedEntity | bool | Bool | | RO (derived), not saved |
| ActivatableReference (record) | Kind | ActivatableKind | Enum | | with |
| | ActionId | Guid | Guid | ActionId | with |
| | StackInstanceId | uint | Int | StackInstanceId | with (remapped when saved) |
| ActionSource (record, hand-written) | Kind | ActionSourceKind | Enum | | RO |
| | Key | EntityKey | Key | EntityKey | RO (remapped when saved) |
| | TerrainTypeId | ushort | Int | TerrainTypeId | RO (a Guid when saved) |
| | AuraId | byte | Int | AuraId | RO (a Guid when saved) |
| | Identity (name, crawler number) | EntityIdentity (an interned handle) | Text | | RO (saved as the name and number, never the handle) |

**Meanings the game resolves** (decision 4), from the tables above: Frame, Frames, EntityKey, EntityId, MapPosition,
Tiles, UtcTicks, Floor, BlueprintId, AuraId, ItemId, ItemDefinitionId, ActionId, AchievementId, TerrainTypeId,
BodyPartId, AbilityScoreType, BehaviorSetId, BehaviorId, StackInstanceId, HeldGrantKey, SpriteName. Session-local and
saved through a Guid: BlueprintId, AuraId, ItemId, TerrainTypeId, BehaviorSetId, BehaviorId. Remapped on load:
EntityKey, StackInstanceId, HeldGrantKey.

**What a save leaves out** (Phase 4's list): the derived members (`ItemDefinitionId`, `IsPermanent`, both `Primary`s,
`DisabledMask`, `ParalysisTimerComponent.StackCount`, `TargetSelection.HasMarkedEntity`), the ability score totals,
`ProcessingTierComponent` (resolved again where the entity loads), `RelatedSourceComponent<TLink>` (rebuilt from the
links), the timer wheels' marks, and `ActionSource`'s identity handle (its name and number are saved instead).
