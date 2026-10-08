# Data-driven actions and items -- definition manifests, save folders, load by id

(Pre-implementation. Prerequisite for the Action and Item Builder dev tool, which is itself the
prerequisite for the NPC Builder. Establishes the save-folder pattern that TODO.md's "Data storage,
starting with window locations and sizes" and "Save and load Beyond neighborhoods" (Global) build on;
takes the "Data tables for content" (Game, Medium) approach for the first two tables, without taking
over that item.)

## Context

What exists today:
- **Every action and item is C#**: one static class per definition (`Game/Modules/Actions/Definitions/**`,
  `Game/Modules/Inventory/Definitions/*`) with a `Build()` that returns a new record each call. 7
  actions and 12 items. `CoreActionsModule`/`CoreItemsModule` call each `Build()` in `Configure` and
  register into `ActionCatalog`/`ItemCatalog` (`Catalog<T>`, keyed by Guid).
- **Blueprints hold definitions, not ids**, in places: `PlayerKit`, `PotionShopStock`,
  `GeneralShopStock`, `TreasureChest`, `TemporaryNpcLootGrant` and `TestDummyBlueprint` call
  `X.Build()` directly. `BlueprintContext` carries no catalog.
- **The shape to serialize** (all immutable records):
  - `ActivatableDefinition` (Id, Name, SpriteName, Glyph, GlyphColor, Tags, Effects, Description,
    Summary, Toggle) → `ActionDefinition` (+ Activator) and `ItemDefinition` (+ optional Activator,
    GoldValue, MaximumShopStock, Contents, CanTrade, SpriteTint).
  - `Tags` already includes the activator's implied tags (unioned in the constructor): **the declared
    tags are not kept anywhere**, so they can't be written back out as authored.
  - Activators: `DirectAction`, `SpellActivator` (ManaCost), `PotionActivator`, `ScrollActivator`
    (SpellId), `WandActivator` (Charges, MaxCharges), `ToggleItemActivator` (IsToggledOn). Each holds
    `TargetingSpec` (Engine) and `ActionTiming`.
  - `ToggleSpec` (ActivationEffects, Periodic: Effects + IntervalFrames).
  - `Effect` = list of `IEffectEntry`. Entries: `DirectDamage`, `DirectHeal`, `DirectManaRestore`,
    `ManaDrain`, `StatModifierGrant`, `StatusEffectGrant`, `StatusEffectImmunityGrant`,
    `StatusEffectRemoval`, `ChainedEffect` (nested Effects, chance), `AuraSourceGrant` (an inline
    `AuraDefinition`), `DodgeActivation`, `HotkeyExpansionGrant`. Feature-owned entries live in their
    feature's namespace, and a mod can add its own.
  - `IItemContents`: `SetItemContents`, `RandomSingleStackContents` (a singleton).
  - `AuraDefinition` (Id, Name, GlowColor, Effects, Tags, Magnitude) lives with what radiates it; the
    same Guid in two places must be the same definition or the build fails.
- **Definitions made at runtime today, none of them persistent:** loot box items
  (`LootboxCatalog` through `IItemDefinitionSource`, Guid a pure function of type + rarity), and
  per-instance overrides -- `InventoryItemStackComponent.Override` (a wand's charges, a lit toggle) and
  `ActionInstanceComponent.Override` (a race's flat damage). Planned runtime-made content: "Spell
  customization at the time of casting", "An ability that turns a direct-target spell into a
  limited-duration aura", crafting, and the Builder tools.
- **Whole-content checks run at build** over `EffectContent.ForEachEntry` (every action, item, terrain
  contact and aura): `ContentTagValidation`, `AuraContentRegistration`, `ToggleContentValidation`.
  `RandomSingleStackContents.Roll` enumerates every registered item to pick a candidate.
- **No save system exists.** The only JSON is `Content/SpriteManifest.json` (System.Text.Json,
  reflection) and the diagnostics reports.

## Research

How other games and frameworks store definitions and runtime-made content, and what each contributes
here.

**Bethesda (Morrowind through Skyrim) -- master files plus "created objects" in the save.** Content
ships as master files (`.esm`) and plugins layered over them in load order, each record a FormID. A
spell, potion or enchantment the player makes at runtime is a new record written into the *save*,
with a FormID in a reserved range (`0xFF......`) that marks it save-local; Skyrim's save keeps
separate tables of created weapon enchantments, armour enchantments, potions and poisons. The master
is never copied into the save; the save names which plugins it was made with and refers to their
records. What this contributes: the master/save split the request describes is the established
shape, a save-local definition is an ordinary definition in its own table, and the save layers over
an unmodified master. Bethesda's own trouble spot is unbounded growth -- created records nobody
references any more -- so this plan dedupes identical creations and leaves room to drop unreferenced
ones when the save system exists.

**Minecraft (1.20.5+) -- prototype plus patch.** An item type has a prototype set of data components;
an item stack stores only a *patch*: the components that differ from the prototype, including
removals. Saves and network packets carry the patch, never the whole item. This is the right model
for per-instance overrides (a wand's charges, a race's damage on Punch) once entity state is saved:
store the base id and what differs, not a cloned definition. It is not what this plan stores for a
genuinely new definition, which has no prototype to be a patch of.

**Starbound -- item descriptor = base item name + parameters**, merged over the base config with
`__merge` rules (strings and numbers overwrite, arrays append, objects merge). Same lesson as
Minecraft from the JSON side, and its merge rules are the cautionary tale for mods: whole-file
overrides conflict, keyed merges don't.

**Cataclysm: DDA and RimWorld -- every definition loaded at startup.** CDDA loads all JSON under
`data/json` breadth-first at startup and supports `copy-from` (a definition copies another and
overrides fields); RimWorld's XML Defs inherit with `Name`/`ParentName`/`Abstract`. Both load
everything eagerly and resolve cross-references after loading -- which works because their content is
a few tens of MB at most. Lesson: eager loading of shipped content is the norm, and a by-id path pays
for itself only when content is large or comes from many places (runtime-made, mods).

**Factorio -- names in the save, migrations for renames.** A save refers to prototypes by name, keeps
a name ↔ id table for compactness, and a mod ships JSON migrations that rename one prototype to
another; each save remembers which migrations it has applied. A prototype that disappears invalidates
references to it. Lesson: references are by stable key, never by a session id; renames are an
explicit table, not guessed.

**Unreal Engine -- Asset Registry and Primary Assets.** Every asset's id and a set of searchable
tags (`AssetRegistrySearchable`) are indexed without loading the asset, so "every item tagged X" is
answered from the registry and the asset itself is loaded only when used (`FPrimaryAssetId`, soft
references, `LoadPrimaryAsset` async). `CoreRedirects` maps old names to new at load. Lesson: by-id
loading needs a *summary index* for whole-catalog questions, or any "pick from every item" query
forces a full load (and here would make loot depend on what happened to be loaded).

**Unity Addressables -- a catalog of keys to locations.** `catalog.json` (binary since 1.21 for load
speed) maps keys to locations; `LoadAssetAsync(key)` loads an asset *and its dependencies*
asynchronously, reference-counted. Lesson: loading a definition loads what it references (an item's
aura), and loading runs off the main thread.

**Godot -- text authored, binary shipped.** `.tres` text resources are converted to binary `.res` on
export (`convert_text_resources_to_binary`, on by default) because binary is smaller and loads
faster; loading the same resource path twice returns the same instance. Lesson: readable and fast
formats can be two encodings of one schema, and one instance per definition is a rule, not an
optimisation (this project depends on it -- see Decision 12).

**Path of Exile -- binary tables.** `.dat64`/`.datc64` are fixed-width row tables with a variable data
section for strings and lists, read with an external schema. The extreme of "efficiency over
readability"; it is what a later binary encoding of this plan's schema would look like if one were
ever needed.

**System.Text.Json.** `Utf8JsonReader`/`Utf8JsonWriter` read and write UTF-8 directly with no
intermediate strings; `[JsonPolymorphic]`/`[JsonDerivedType]` handle polymorphism with a
discriminator, but only for a type list closed at compile time. That rules attributes out for
`IEffectEntry`: a mod adds entry types Game can't name. Hence a codec registry (below) -- the same
reason Bevy has a runtime `TypeRegistry` rather than derive-time lists.

## Recommendation on the master manifest: layered, not copied

**Load the master separately and layer the save's manifest over it; don't copy it into the save.**
- A save refers to master definitions by Guid, the way Bethesda saves refer to their masters. A game
  update that rebalances Fireball reaches every save; a copy would freeze every save on the balance
  it started with, and every fix would need a per-save migration.
- A copy multiplies disk use by the number of saves and makes the save manifest exactly as large as
  the request is trying to avoid.
- Mods sit between the two (master → mods in load order → save), and a copy would bake in whichever
  mods were loaded when the save started.
- The cost of layering is that the master can change under a save. The save's header records the
  master's content hash and each module's content version, so a load can tell, and a removed or
  renamed definition goes through the fallback hierarchy already decided in TODO.md ("Modded content
  must degrade gracefully"): a redirect, else drop the reference, else drop the entity.
- A save-local definition can't depend on that changing silently in a way that matters: it is a whole
  definition (Decision 6), so it names other content only by Guid -- an aura, a spell id -- exactly as
  a master definition does.

## Decisions

Confirmed in this plan's review: layer the master, compact JSON Lines, saves under LocalAppData, a
temporary Admin Mode consumer, auras in their own table, content-derived ids.

Format:
1. **One manifest = a set of tables, one file per table**: `Actions.jsonl`, `Items.jsonl`,
   `Auras.jsonl`. Splitting by table is what helps loading (one record type per file, one codec per
   line). Splitting further by kind (spells, potions) doesn't: by-id loading never scans a table.
2. **JSON Lines, compact.** Line 1 is a header (`{"fmt":1,"table":"Items","ver":{...}}`), every other
   line one record. UTF-8, no indentation, short keys, fields at their default omitted (Minecraft's
   "ignore default components"), enums and tags by name, Guids as 22-character base64url, colours as
   a packed `uint`. One record per line is what lets a save manifest be appended to without rewriting
   it, and what lets the index find a record's bytes. Named keys rather than positional arrays: a new
   field with a default needs no migration, and the size difference is small once defaults are
   omitted.
3. **Hand-written codecs over `Utf8JsonReader`/`Utf8JsonWriter`**, no reflection and no
   `JsonSerializer`: each serialized type has one codec that reads and writes it, keeping parse cost
   predictable and the format owned by the code that owns the type. A later binary encoding (Godot's
   `.res`, PoE's tables) is a second writer behind the same codecs: Phase 4 adds a TODO.md entry for
   it if its measurements warrant one, and nothing otherwise.
4. **Polymorphic values carry a discriminator first** (`"$":"DirectDamage"`), resolved through a
   **content type registry**: entry types, activator types and contents types each register a stable
   name and a codec. Built-ins register theirs beside the type; a mod registers its own. Modules
   register in a new no-context phase, `DeclareContentTypes(ContentTypeRegistration)`, beside
   `DeclareTags`, so every codec exists before any `Configure` reads a manifest. An unknown
   discriminator fails that record (reported, not thrown), never silently drops an entry.
5. **Auras get their own table**, referenced by Guid from `AuraSourceGrant` (and later from terrain
   and blueprints when they become data). Inline auras would repeat the definition in every item that
   radiates it, which is the "two definitions sharing a Guid" error waiting to happen. Code-declared
   auras (`BuiltInTerrain.LavaAura`, `HealingShrine.Aura`) stay C# until terrain and blueprints are
   data; they register into the same catalog, so a reference resolves either way.

Save folders:
6. **A save folder holds its own manifest of definitions created in that game**, layered over master
   and mods. A runtime definition is always a *whole* definition with its own Guid -- never a patch
   over a master one -- so it means the same thing if the definition it was made from changes.
7. **A runtime definition's Guid is derived from its canonical bytes** (SHA-256 of the whole record
   minus its id, under a namespace Guid, the way `LootboxCatalog.ItemIdFor` and
   `BlueprintRegistry.Compose` derive theirs). Making the same thing twice yields the same id and no
   second record -- the dedupe Bethesda's created-potion tables lack.
   - **Derived once, stored, never recomputed.** The id is written in the record and read back as
     data; nothing rehashes a loaded definition, so a new feature can never change an existing
     definition's Guid or break a reference to it.
   - **The whole record, not chosen fields.** Hashing a subset (only the effects, say) would give two
     definitions that differ outside it the same Guid, and the later would replace the earlier -- a
     wrong definition, which is worse than a duplicate.
   - **Stable across new fields by construction:** defaults are omitted (Decision 2), so a field added
     with a default leaves every existing definition's bytes, and its derived id, unchanged. A new
     field with a non-default value is genuinely different content.
   - **What can change it:** renaming a key or changing how a value is encoded. Then making "the
     same" definition again derives a new id beside the old one -- a duplicate, never a broken
     reference. Dedupe is best effort; correctness never depends on it.
   - **Canonical bytes:** keys in the codec's fixed order, floats in shortest round-trip form, tag sets
     sorted by name (a `GameplayTagSet`'s order follows tag ids, which vary between runs).
8. **Only what can't be re-derived is persisted.** Loot box items stay derived from their Guid by
   `LootboxCatalog`; per-instance overrides (stack and action-instance `Override`) are entity state,
   saved later as base id + patch (the Minecraft model) by the entity save, not as definitions.
9. **Written when made, append-only.** Creating a persistent definition appends its line to the
   save's table at once (through a writer that flushes off the main thread), before anything can hold
   a reference to it -- so no save can ever name a definition its folder lacks. A truncated last line
   (a crash mid-write) is ignored on load. Records nothing references any more are left for the save
   system to compact.
10. **A save folder is `%LocalAppData%\DungeonCrawlerWorld\Saves\<SaveName>\`** with `save.json` (format version, created time,
    seed, master content hash, each module's content version) and `Definitions/*.jsonl`. This is the
    layout every later save feature adds files to. A new game creates one; `--save=<name>` continues
    one (the only load path until "Game flow" exists -- it reloads the definitions, not the world).

Loading:
11. **Shipped master content loads eagerly first (Phase 2); by-id loading comes after (Phase 4).**
    At today's 19 definitions an eager load costs well under a millisecond, and every build-time check
    keeps working unchanged. By-id loading is built as specified, but measured against a synthetic
    large manifest so its benefit is shown, not assumed.
12. **One instance per Guid per session.** Loading a Guid twice returns the cached instance. Stacks
    and overrides compare definitions by reference in places, and an `AuraDefinition` re-registered
    as a new instance raises `AuraCatalog.DefinitionChanged` -- a world-wide rescan.
13. **By-id loading keeps an index of every record in every layer**: Guid → (layer, file, offset,
    length) plus a *summary* (tags, `CanTrade`, has contents) built by a scan of the files at session
    start on a worker. No index file to keep in sync: the scan reads each line's leading fields and
    skips the rest. Whole-catalog questions ("every tradeable item that isn't opened", Admin's lists)
    are answered from the summary, sorted by Guid, never from what happens to be loaded -- otherwise
    loot would depend on load history.
14. **A miss loads synchronously, a known need loads ahead on a worker.** `ActionCatalog` gains the
    definition-source hook `ItemCatalog` already has; the content store is that source for both, so
    nothing can fail to find a definition that exists. `TestMapBuilder.Plan` adds the definition ids
    its spawn list will need (blueprint grants, shop stock, loot) to the `NeighborhoodPlan`, its worker
    parses them, and the main thread registers them when the plan is applied. The player's kit loads
    at startup.
15. **Validation follows loading.** A record is checked when it loads (declared tags, toggle rules,
    aura Guid agreement with what is registered). The whole master and every mod's manifest are
    checked in full in `ModValidation`'s trial build, which already runs at game start, so a broken
    record still fails before play.
16. **Layer order and replacement by Guid**: master → mods in load order → save. A later layer's
    record with the same Guid replaces the earlier one, the rule `ModuleSet.Combine` and
    `BlueprintRegistry` use. A manifest may carry a `Redirects` table (old Guid → new), the Unreal
    `CoreRedirects`/Factorio migration idea, reserved in format 1 and applied once the save system
    loads entity references.

Model changes the format needs:
17. **Definitions keep their declared tags** (`DeclaredTags`), with `Tags` still the union with the
    activator's implied tags. Writing `Tags` would bake implied tags into the data, where they'd
    outlive a change of activator in the Builder.
18. **Blueprints and code refer to definitions by Guid** and look them up in the catalog. The static
    definition classes go; their Guids stay as constants (`BuiltInActionIds`, `BuiltInItemIds`,
    `BuiltInAuraIds`). `BlueprintContext` (and `EntityBuilder`, which `SpawnRecordRebuilder` uses)
    gains the action and item catalogs. Shop stock entries hold ids.
19. **Tests keep building definitions in C#** where a test needs a definition of its own; catalogs
    take a registered definition from code or from a manifest alike.

## Format sketch

Illustrative, not final key names. Items table, Wand of Fireball and Toxic Idol:

```
{"fmt":1,"table":"Items","ver":{"Inventory":1,"Actions":1,"Auras":1}}
{"id":"fD6cHU9rKk6NHAAAAAAAMQ","n":"Wand of Fireball","sp":"Wand","g":"w","gc":4278208255,"t":["Delivery.Ranged","Item.Consumable","Damage.Fire"],"fx":[[{"$":"DirectDamage","mn":25,"mx":35},{"$":"StatusEffectGrant","ty":"Burning","c":5}]],"d":"A wand that hurls...","su":"Deals fire damage in a burst and inflicts Burning.","gv":20,"ac":{"$":"Wand","tg":{"sh":"Burst","r":10,"a":3},"tm":{"c":"Immediate"}}}
{"id":"86jB1itOSp-MbR57OaX5wg","n":"Toxic Idol","sp":"HealthPotion","g":"i","gc":4278211584,"fx":[[{"$":"AuraSourceGrant","au":"xKH22S6LOkqcHQAAAAADAg","st":16}]],"d":"A squat stone idol...","su":"Toggles a Poison aura (range 4) around whoever holds it.","gv":15,"ac":{"$":"ToggleItem","tm":{"c":"Delayed"}},"tog":{}}
```

Auras table:

```
{"fmt":1,"table":"Auras","ver":{"Auras":1}}
{"id":"xKH22S6LOkqcHQAAAAADAg","n":"Poison","gc":4278211584,"fx":[[{"$":"StatusEffectGrant","ty":"Poison","m":"TopUpTo"}]]}
```

- `fx` is a list of Effects, each a list of entries. `ChainedEffect` nests `fx` the same way; its depth
  is checked on load against `ChainedEffect.MaxChainDepth`.
- `tog` present = a toggle; `{}` is `ToggleSpec.HoldsEffectsOnly`; `"on"` holds ActivationEffects,
  `"per":{"fx":...,"i":60}` Periodic.
- Activator discriminators are their own names (`Direct`, `Spell`, `Potion`, `Scroll`, `Wand`,
  `ToggleItem`); `Wand` omits Charges/MaxCharges at 0, as authored.
- Enum values by name, so reordering an enum (`StatusEffectType`, `BodyPartType`,
  `StatModifierTarget`) never changes what a file means. Tags by name: tag ids vary between runs.

## Phases

Each phase ends with the game running and a stop for in-game testing before the next.

### Phase 1 -- Content type registry and codecs

- Engine (`Engine.Content`): the manifest file layer -- header, JSON Lines reading and appending,
  tolerant of a truncated last line; `ContentTypeRegistry<TBase>` (name → codec, codec → name);
  base64url Guid and packed-colour helpers; `ContentFailure` (file, line, record id, reason), reported
  like `ModuleFailure`.
- Engine.Modules: the `DeclareContentTypes` phase on `IModule` (default no-op), run for every module
  before `RegisterComponents`, the way `DeclareTags` runs.
- Game: a codec beside every serialized type -- each entry, activator and contents type, `Effect`,
  `ToggleSpec`, `TargetingSpec`, `ActionTiming`, `BodyPartTargeting`, `AuraDefinition`,
  `ActionDefinition`, `ItemDefinition`. The owning module registers each (Core for `Game.Effects`
  entries, Actions for its entries and activators, Auras for `AuraSourceGrant`, Inventory and
  Lootboxes for contents).
- `DeclaredTags` on `ActivatableDefinition` (Decision 17).
- Tests: every built-in action, item and aura round-trips -- written, read back, written again, the two
  byte sequences identical (records holding lists compare by reference, so bytes are the comparison).
  Defaults are omitted; an unknown discriminator and a truncated line are reported, not thrown; every
  registered entry type has a codec (a test over the registry, so a new entry without one fails).
- No gameplay change; nothing reads a manifest yet.

### Phase 2 -- The master manifest, loaded eagerly

- Export: a one-shot DevTools command (`DevTools/ContentManifestExporter`) writes today's C#
  definitions to `Content/Definitions/Master/{Actions,Items,Auras}.jsonl` through the Phase 1 codecs.
  It also writes an indented copy on request for reading -- the only way to read the compact form
  until the Builder exists.
- `Content.csproj` copies `Definitions/**` to output, as it does fonts.
- `CoreActionsModule`/`CoreItemsModule` register from the master manifest in `Configure` instead of
  their `Build()` lists. A new `Content` service on `GameModuleContext` (the layered manifest stack)
  holds the auras table; an `AuraSourceGrant` resolves its aura through it.
- The static definition classes are deleted; their Guids move to `BuiltInActionIds`/`BuiltInItemIds`/
  `BuiltInAuraIds` (Decision 18). Blueprint steps (`PlayerKit`, shop stock, `TreasureChest`,
  `TemporaryNpcLootGrant`, `TestDummyBlueprint`) look definitions up by id through `BlueprintContext`.
  `ActionOverrideEffects`/`WandGrantEffects` take the catalog definition.
- Checks unchanged (everything is loaded, so `EffectContent.ForEachEntry` still sees all of it).
- Verification: the same seed builds the same world before and after (`SpawnRecordRebuilderTests`,
  and a headless seeded run compared against a pre-change one); all tests pass; in-game, every item
  and action looks and works as before (tooltips, toggles, the Toxic Idol, Scroll of Torch, wands).

### Phase 3 -- Save folders and runtime definitions

- Engine (`Engine.Persistence`): `SaveFolder` -- create, open, `save.json` header read/write
  (Decision 10), the save's own `Definitions/` tables, and the append writer (Decision 9: queued,
  flushed on a worker, flushed and closed when the session is disposed).
- The exe decides `SavesRoot` and the save name, creates the folder for a new game, and opens one for
  `--save=<name>`; `GameBootstrapper.Build` takes the folder and layers its manifest over master.
  `ModValidation`'s trial builds and the staging pass get no save folder; tests use a temp folder.
- `ContentStore.CreatePersistent(definition)` (actions, items, auras): derives the content Guid
  (Decision 7), returns the existing definition when the Guid is known, else appends and registers.
  Its first caller is a **TEMPORARY** Admin Mode context-menu option on the player ("Create runtime
  action"): it builds a new action from a built-in one with changed numbers, persists it and grants it
  to the player. It exists only to exercise this phase and is removed in Phase 5; real callers arrive
  with crafting and the Builder.
- The header's master hash and module versions are written now and compared on open; a mismatch is
  logged, nothing more, until the save system has entity references to repair.
- Verification: create a runtime action from the player's context menu and use it from the hotbar;
  quit, relaunch with `--save=<name>` and find its record loaded; create the same action twice and see
  one record; kill the process mid-session and reopen.

### Phase 4 -- Load by id

- The index (Decision 13), built by a scan on a worker while the session is being built, over every
  layer.
- `ActionCatalog` gets the definition-source hook; the content store becomes the source for both
  catalogs and the aura table, loading a record's references with it (Unity's dependency loading).
  One instance per Guid (Decision 12).
- `CoreActionsModule`/`CoreItemsModule` stop registering everything; the player's kit and the
  definitions the start neighborhood's plan names load during startup.
- `NeighborhoodPlan` gains the definition ids its spawn list needs; the worker parses them; the
  streamer registers them when it applies the plan.
- Whole-catalog readers move to the summary: `RandomSingleStackContents`'s candidates, Admin Mode's
  grant/spawn lists. A test fails if any game code enumerates `ItemCatalog.Definitions`/
  `ActionCatalog.Definitions` outside the content store.
- Validation moves per record, and `ModValidation` checks every record in full (Decision 15).
- Measurement (benchmark skill, plus a test-only generator for a synthetic 10,000-definition master):
  startup time, streaming frame cost and memory with eager vs by-id loading. Report both. If by-id
  loading buys nothing at realistic sizes, that is the result -- it stays, since the save and mod
  layers need the same machinery, but nothing is tuned for it. If JSON parsing is a meaningful share
  of load or streaming cost, add a TODO.md entry for a binary encoding behind the same codecs
  (Decision 3); if not, add nothing.
- Verification: a seeded run identical to Phase 3's; walk across several neighborhood shifts with
  Admin Mode's diagnostics showing loaded definition counts; loot from a fresh chest picks among all
  items, not only loaded ones.

### Phase 5 -- Mod manifests

- A mod folder may hold `Definitions/*.jsonl`, layered in load order between master and save
  (Decision 16). `ModValidation` checks them in the mod's trial build; a failing record is a
  `ContentFailure` naming the mod, and the mod is dropped like any other failing mod.
- `Mods.ExampleMod` ships one item and replaces one built-in by Guid; `Mods.TestFixtures` gets a mod
  with a broken record for the failure path.
- The `Redirects` table is read and kept; applying it waits for entity saves (Decision 16).
- Remove the TEMPORARY "Create runtime action" Admin Mode option (Phase 3).

## Out of scope / follow-ups (each to become a TODO.md entry when this lands)

- **The Action and Item Builder** (DevTools): edits the master manifest (and later a save's) through
  these codecs; in-game Admin entry points can call `ContentStore.CreatePersistent`.
- **Terrain and blueprints as data**: the same codecs serve `TerrainContact` effects and blueprint aura
  grants; code-declared auras move into the auras table then.
- **Entity state in saves**: per-instance overrides as base id + patch; compacting unreferenced save
  definitions at save time; applying redirects.
- **Binary encoding** behind the same codecs: a TODO.md entry only if Phase 4's measurements warrant it.
- **Hot reload** of a changed manifest table in Debug (Bevy's asset watcher).
