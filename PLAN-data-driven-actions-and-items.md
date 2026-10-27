# Data-driven actions and items -- content layers, save folders, load by id, mod manifests

(Rewritten 2026-10-25 around what is left. Phases 1-2 -- codecs and the master manifest -- landed 2026-10-18; the IDE
overhaul (epic children 5-8) then added disabled records and replacement redirects, tool builds that collect content
failures, and map areas as content. This plan's remaining work is Phases 3-7 below. It establishes the save-folder
pattern TODO.md's "Data storage, starting with window locations and sizes" and "Save and load Beyond neighborhoods"
(Global) build on, and the layer stack "Workshop: edit Items" needs for drafts.)

## What is in place

- **Codecs** (`Engine.Content`): `ContentCodec<T>` with an enforced `ContentSchema` (the writer refuses an undeclared
  key, the reader fails a record holding one), `ContentTypeRegistry` filled in `IModule.DeclareContentTypes`,
  `ContentWriter` over `Utf8JsonWriter`, `ContentObject` over `JsonDocument`, `ContentGuid` (base64url), and
  `ContentFailure` (file, line, column, record id, reason). Polymorphic values carry `"$"` first. A codec for every
  entry, activator and contents type (`ContentCodecTests`).
- **Tables** (`ContentTable`): JSON Lines, header `{"fmt":1,"table":"..."}`, one compact record per line, defaults
  omitted, a truncated last line ignored. Envelope keys `off` (disabled: not decoded) and `rep` (its replacement, a
  catalog redirect that `Catalog<T>.TryGet`/`Get` follow, cycles refused). **Documents** (`ContentDocument`): one
  indented record per file with a `ContentSourceMap`, for areas.
- **The master manifest** (`Game.Content.MasterContent`): `DungeonCrawlerWorld/Content/Definitions/Master` --
  `Actions.jsonl` (9), `Items.jsonl` (16), `Auras.jsonl` (4), `AreaPalettes.jsonl`, `AreaPlacements.jsonl`,
  `Areas/*.json`. Each table read the first time it is asked for, auras first. `Open` throws on a failing record
  (the game), `OpenCollectingFailures` leaves it out (the IDE's builds, `collectsContentFailures`), `Load` reads
  everything collecting failures (tools, tests). **One layer only**: `GameModuleContext.Content` always opens
  `MasterContent.DefaultDirectory` (the build output's copy); the IDE's `ContentSession.LoadProblems` reads the source
  tree's copy a second time for the Error List.
- **Code names built-ins by Guid** (`BuiltInActionIds`/`BuiltInItemIds`/`BuiltInAuraIds`) and looks them up in the
  session's catalogs; `BlueprintContext` carries `Actions` and `Items`; shop stock holds Guids.
- **Per-session derived definitions** (`ActionCatalog.GetOrCreateDerived`, `ItemCatalog.GetOrCreateDerived`): a race's
  override of an action, a charged wand. One instance per session, rebuilt from a recipe -- never persisted.
- **The exporter** (`DungeonCrawlerWorld/Tools/ContentManifestExporter`) rewrites master canonically from a build, in
  catalog order, keeping disabled lines.
- **What does not exist:** layering (mods, saves, drafts), per-module content versions (the header's `"ver"`), save
  folders, appending to a table, creating a persistent definition at runtime, per-record validation (every check runs
  over the whole build through `EffectContent.ForEachEntry`), a definition source on `ActionCatalog`, by-id loading,
  and mod content of any kind. `RandomSingleStackContents`, `CoreActionsModule`, the content checks and the IDE's tabs
  enumerate `Catalog<T>.Definitions`.

## Research (summary; sources in the 2026-10-04 review)

- **Bethesda (Morrowind-Skyrim):** master files layered with plugins in load order; a spell, potion or enchantment the
  player makes is a new record in the *save*, in a save-local FormID range, in its own table. The master is never
  copied. Their known weakness is unbounded growth of created records -- hence dedupe (Decision 7) and room to compact.
- **Minecraft (1.20.5+):** an item stack stores a *patch* over its prototype's data components. The model for
  per-instance overrides once entity state is saved -- not for new definitions.
- **Starbound:** item = base name + parameters merged over its config; whole-file overrides conflict, keyed merges
  don't.
- **Cataclysm DDA, RimWorld:** everything loaded eagerly at startup (`copy-from`, `ParentName`). Eager is the norm;
  by-id loading pays only with large or many-sourced content.
- **Factorio:** saves name prototypes by stable key; renames are explicit JSON migrations, each applied once per save.
- **Unreal:** the Asset Registry indexes ids and searchable tags without loading assets, so whole-catalog queries
  never force a load; `CoreRedirects` rename at load.
- **Unity Addressables:** a catalog of keys to locations; loading an asset loads its dependencies, asynchronously.
- **Godot:** text resources converted to binary on export; the same path loads the same instance.
- **Path of Exile:** binary fixed-width tables -- what a binary encoding would look like if ever needed.
- **System.Text.Json:** `[JsonDerivedType]` needs a closed type list, which mods break -- hence the codec registry.

## Decisions

Confirmed 2026-10-04 (open questions answered: layer the master, compact JSON Lines, saves under LocalAppData, a
temporary Admin Mode consumer, auras in their own table, content-derived ids), with what has landed noted.

Format (landed):
1. **One file per table** -- Actions, Items, Auras; areas one file each (added by the map areas plan).
2. **Compact JSON Lines**, header line then one record per line, short keys, defaults omitted, enums and tags by name,
   Guids base64url, colours packed. The header's `"ver"` map (each module's content version) arrives in Phase 3.
3. **Hand-written codecs, no reflection.** Reading is over `JsonDocument` as built; Phase 5 measures it, and a TODO.md
   entry for `Utf8JsonReader` streaming or a binary encoding behind the same codecs is added only if the measurements
   warrant one.
4. **Polymorphic values carry a discriminator**, resolved through the content type registry; an unknown one fails that
   record.
5. **Auras have their own table**, referenced by Guid. Code-declared auras (`BuiltInTerrain.LavaAura`,
   `HealingShrine.Aura`) stay C# until terrain and blueprints are data.

Layers (confirmed 2026-10-25):
6. **One layer stack for every source of content**: master → mods in load order → the save. The IDE adds its drafts
   on top for its own builds (the IDE plan's "Saving: master and drafts"); the game never has a drafts layer. A later
   layer's line for a Guid wins over an earlier one while it is there. Redirects are merged across layers. No layer
   ever writes to another layer's files.
7. **What each layer may do, and what happens when its record fails:**
   - **Master** must load whole: a failing record throws in the game, is collected in tools (as today).
   - **A mod** may add, replace or disable records (an `off` line, with or without `rep`); a failing record drops the
     mod in `ModValidation`, like any other failing mod.
   - **The save** may add records and **override** a master or mod record: a save line with a lower layer's Guid wins
     for that save until the save removes it, and then the lower layer's record is back. The master and mod files are
     never touched. A failing save record is logged and left out -- a broken save definition never stops a game from
     loading (the fallback hierarchy in TODO.md's "Data storage").
8. **The stack is a build input.** `GameBootstrapper.Build` (and `GameBuildPass`) take the layers instead of
   `GameModuleContext` opening `MasterContent.DefaultDirectory`; GameShell passes the output's master (plus mods and
   save later), the IDE passes the source tree's master (and later its drafts), so `ContentSession` reads master once.
9. **Every loaded record knows its layer** (`ContentRecordLocation` gains it), for the IDE to mark mod content, for
   failures to name their mod or save, and for the exporter to write the master layer alone.

Save folders and runtime definitions:
10. **A save folder holds the definitions created or overridden in that game**, layered over master and mods; master is
    never copied.
    - **An override keeps the Guid it overrides** -- that is what makes it an override -- so everything naming the
      original (content, `BuiltIn*Ids`, a stack) resolves to it. Applied mid-session, it re-registers that Guid: what
      resolves through the catalog sees it from the next lookup, and an overridden aura raises
      `AuraCatalog.DefinitionChanged` (a rescan, accepted for something this rare).
    - **Removing an override** appends a removal line (`{"id":...,"rm":true}`, a new envelope key, valid only in a save
      layer) and re-registers the lower layer's definition the same way. A save may not disable (`off`) a lower
      layer's record; only master and mods disable.
11. **A new runtime definition's Guid is derived from its canonical bytes** (an override keeps its target's, Decision 10) (SHA-256 of the whole record minus its id,
    under a namespace Guid, as `LootboxCatalog.ItemIdFor` derives its ids), so making the same thing twice gives one
    record.
    - Derived once, stored in the record, never recomputed: no later feature can change an existing Guid.
    - The whole record, never chosen fields: a subset would give two different definitions one Guid, and the later
      would replace the earlier.
    - Stable across new fields by construction, since defaults are omitted. A renamed key or a changed encoding only
      makes a later identical creation a duplicate, never a broken reference.
    - Canonical bytes: the codec's fixed key order, shortest round-trip floats, tag sets sorted by name.
12. **Only what can't be re-derived is persisted.** Loot box items stay derived by `LootboxCatalog`; per-session derived
    definitions (`GetOrCreateDerived`) stay recipes; per-instance overrides are entity state, saved later as base id +
    patch.
13. **Written when made, append-only**: the line is appended (queued, flushed off the main thread, flushed and closed on
    session dispose) before anything can hold a reference to it; within a save table the last line for a Guid wins
    (a newer override, a removal). The truncated-last-line rule already covers a crash. Superseded lines and
    unreferenced records are left for the save system to compact.
14. **A created or overriding record is checked before it is registered** -- declared tags, toggle and targeting rules,
    aura references -- through per-record forms of today's whole-build checks. **One that fails is still saved, with a
    warning, as a save draft**, so partial progress on a complex entry is kept:
    - Save drafts are `<save>/Drafts/*.jsonl`, the same tables and format, which the game never loads -- the IDE's
      drafts rule (its Decisions 8-11) applied to a save.
    - A draft is kept as written, whether or not it decodes, and its problems are worked out again when it is opened.
    - A draft has a working Guid that stays put while it is worked on (random for a new definition, its target's for an
      override). Saving it once it passes moves it to `Definitions/`: a new definition then takes its derived Guid
      (Decision 11), an override keeps its target's.
    - Nothing may reference a draft, so nothing is ever left pointing at one; the warning lists every failed check.
15. **`%LocalAppData%\DungeonCrawlerWorld\Saves\<SaveName>\`** with `save.json` (format version, created time, seed,
    master content hash, each module's content version), `Definitions/*.jsonl` and `Drafts/*.jsonl`. A new game creates one;
    `--save=<name>` continues one (the only load path until "Game flow" exists -- it reloads definitions, not the
    world). The IDE's builds and every trial and staging build have no save layer.

Loading by id:
16. **Eager until Phase 5, and eager for tools always.** The IDE and the exporter need every record; by-id loading is the
    game's.
17. **One instance per Guid per session**: a second load returns the cached instance (stacks compare definitions by
    reference; a re-registered aura instance raises `AuraCatalog.DefinitionChanged`, a world-wide rescan).
18. **An index of every record in every layer** -- Guid → layer, file, offset, length, plus a summary (tags, `CanTrade`,
    has contents) -- built by a scan on a worker while the session builds; no index file. Whole-catalog questions are
    answered from the summary sorted by Guid, never from what happens to be loaded, or loot would depend on load
    history.
19. **A miss loads synchronously; a known need loads ahead on a worker.** `ActionCatalog` gains the definition-source
    hook `ItemCatalog` has; the stack is the source for both catalogs and the aura table, loading a record's references
    with it. `NeighborhoodPlan` carries the definition ids its spawn list needs.
20. **Validation follows loading**: per record when it loads (Decision 14's checks), and in full for master and each mod
    in `ModValidation`'s trial build, so a broken record still fails before play. A save record that fails its checks
    at load is logged and left out (Decision 7); it is not moved to Drafts behind the player's back.

Kept from the original:
21. **A `Redirects` table** (old Guid → new) is for records removed outright; a disabled record's `rep` covers the rest
    (IDE plan Decision 16). Reserved in the format, read in Phase 6, applied once entity saves exist.
22. **Tests keep building definitions in C#** where they need their own; catalogs take definitions from code or content
    alike.

## Format (as built)

```
{"fmt":1,"table":"Items"}
{"id":"HZo-fG9LKk6NHAAAAAAAAQ","n":"Health Potion","sp":"HealthPotion","g":"h","gc":4278222848,"t":["Effect.Healing","Targeting.Self"],"fx":[{"e":[{"$":"DirectHeal","pct":0.5}]}],"ac":{"$":"Potion","tg":{"sh":"Burst","r":3,"a":1,"ta":"MarkedOnly"},"tm":{}},"gv":5,"d":"...","su":"Heal target(s) by 50%."}
```

Phase 3 adds the header's versions -- `{"fmt":1,"table":"Items","ver":{"Inventory":1,"Actions":1}}` -- written by the
writer from the modules whose codecs the table uses; a reader accepts a missing `"ver"` as version 1 everywhere, so
master's files only gain it the next time the exporter or the IDE writes them.

## Phases

Each phase ends with the game running, a list of checks, and a stop for in-game testing before the next.

### Phase 3 -- The content layer stack

- Engine: a layer is a named source of tables and documents (a directory, or for tests text in memory) with its kind
  (Master, Mod, Save, Drafts). `ContentLayers` reads a table across layers in order, applying Decision 6's replacement
  and Decision 7's per-kind rules, merging redirects and collecting failures with their layer.
- Engine.Modules: a module declares its content version in `DeclareContentTypes` (default 1); the table writer puts the
  versions in the header; the reader exposes them per layer.
- Game: `MasterContent` becomes the game's view over `ContentLayers` (same tables, same first-ask reading, same
  `Open`/`OpenCollectingFailures`/`Load` behaviour for a master-only stack). `GameBootstrapper.Build`, `GameBuildPass`
  and `ModValidation` take the stack (Decision 8); `ContentRecordLocation` gains its layer (Decision 9).
- GameShell passes master alone (no behaviour change). The IDE builds its session over the source tree's master and
  `LoadProblems` reads the session's own failures instead of a second `MasterContent.Load`. The exporter writes the
  master layer only.
- The `rm` envelope key (Decision 10): read in a save layer, a failure anywhere else; a save layer's `off` line is a
  failure too.
- Tests: a mod layer adds, replaces and disables by Guid; a save layer adds and overrides, and its `rm` line brings the
  lower record back; within one layer the last line for a Guid wins; a redirect set in a lower layer survives a higher
  layer's override of its target; a failing record carries its layer; a missing `"ver"` reads as 1; every existing
  content test passes unchanged.
- Checks:
  - Run the game -- expect the world, items, actions and auras exactly as before (seeded headless A/B against a
    pre-change build: same fingerprint).
  - Open the Workshop -- expect every tab and the Error List as before; break a line of `Items.jsonl` in the source
    tree and restart -- expect one Error List row naming the file and line, and the rest of the Items tab intact.
  - Run the exporter -- expect master rewritten byte for byte.

### Phase 4 -- Save folders and runtime definitions

- Engine (`Engine.Persistence`): `SaveFolder` -- create, open, `save.json` (Decision 15), its `Definitions/` tables as a
  Save layer, its `Drafts/` tables (never a layer of the game's), and the table appender (Decision 13).
- GameShell: `SaveArgument` (`--save=<name>`, the `MapSizeArgument` pattern). With no argument a new game creates a
  folder named for its start time and seed; with one it opens that folder (a missing one is created). The folder's
  layer goes into the stack. The header's master hash and module versions are compared on open; a mismatch is logged,
  nothing more.
- Game: per-record forms of `ContentTagValidation`, `ToggleContentValidation`, `TargetingContentValidation` and the aura
  reference check (Decision 14), shared with the whole-build checks so the two can't disagree.
- Game: `ContentStore` for actions, items and auras, each call returning what happened (saved, already saved, or
  saved as a draft with its failures) for the caller to show:
  - `CreatePersistent(definition)` -- canonical bytes, derived Guid (Decision 11), the existing definition when that
    Guid is known, otherwise check, append, register.
  - `Override(definition)` -- the definition under a lower layer's Guid: check, append, re-register (Decision 10).
  - `RemoveOverride(id)` -- append the removal line, re-register the lower layer's definition.
  - A record that fails its checks goes to the save's Drafts with a warning and is not registered (Decision 14);
    `SaveDraft`/`TryPublishDraft` keep a draft's working Guid and move it to `Definitions/` once it passes.
- **TEMPORARY** Admin Mode context-menu options on the player, removed in Phase 6:
  - "Create runtime action": a copy of Magic Missile with double its damage, named "Magic Missile x2", created through
    `CreatePersistent`, granted to the player and bound to the first free hotbar slot. The same copy every time, so a
    second use exercises dedupe.
  - "Override Quick Attack" / "Remove Quick Attack override": Quick Attack at double damage, through `Override` and
    `RemoveOverride`.
  - "Create incomplete runtime action": a toggle action that isn't Self-targeted (refused by the toggle rules), saved
    as a draft; the warning shows as a notification listing the failed check.
  - "Publish drafts": `TryPublishDraft` on every draft, which a draft fixed by hand in its file passes.
- Startup writes one line per layer: its kind, path and how many records each table loaded, left out or holds as drafts.
- The Workshop Debug tab's Launch section gains a save name (empty = a new game), passed as `--save=`.
- Tests: the derived Guid ignores the record's own id and tag order, and is unchanged by a field at its default; the
  same definition twice appends one line; an override wins and its removal restores the lower definition, in the same
  session and after a reload; an overridden aura raises `DefinitionChanged` once; a failing record lands in Drafts,
  isn't registered and keeps its working Guid when saved again; a draft that doesn't decode is kept as written; a
  published draft takes its derived Guid and leaves Drafts; a save layer's truncated last line loads the rest; a save
  record that fails at load is logged and left out while the session builds; dispose flushes.
- Checks:
  - In Admin Mode, right-click the player, "Create runtime action" -- expect "Magic Missile x2" on the hotbar, hitting
    for double, and a new line in `%LocalAppData%\DungeonCrawlerWorld\Saves\<name>\Definitions\Actions.jsonl`.
  - Use the option again -- expect still one line in the file and one action on the hotbar.
  - Quit and relaunch with `--save=<name>` -- expect the startup line for the save layer to report 1 action loaded (the
    world itself is new; only definitions persist).
  - Launch from the Workshop's Debug tab with that save name -- expect the same.
  - "Override Quick Attack", then hit something -- expect double damage; relaunch with the save -- expect it still
    doubled; "Remove Quick Attack override" -- expect normal damage at once and after a relaunch, and the master
    `Actions.jsonl` unchanged throughout (`git status` clean).
  - "Create incomplete runtime action" -- expect a warning naming the toggle rule, nothing new on the hotbar, and a line
    in the save's `Drafts\Actions.jsonl`; relaunch -- expect the startup line to count it as a draft, not loaded.
  - Edit that draft's line to target Self, then "Publish drafts" -- expect it on the hotbar, its line gone from Drafts
    and added to `Definitions\Actions.jsonl` under a new Guid.
  - Kill the process right after creating one -- expect the next launch to load every complete line.
  - Hand-break the save's line and relaunch -- expect a logged failure and a game that starts.

### Phase 5 -- Load by id

- The index (Decision 18), scanned on a worker during the session build over every layer.
- `ActionCatalog` gets the definition-source hook; the stack is the source for both catalogs and the aura table,
  loading references with a record; one instance per Guid (Decision 17).
- `CoreActionsModule`/`CoreItemsModule` stop registering everything in the game's builds; the player's kit and the
  start neighborhood's plan's ids load during startup. Tool builds (`collectsContentFailures`) stay eager.
- `NeighborhoodPlan` gains its definition ids; the worker parses them; the streamer registers them when it applies the
  plan.
- Gauges (`Content` group): definitions loaded per table, and loads that missed the plan (synchronous loads).
- Whole-catalog readers in the game move to the summary: `RandomSingleStackContents`'s candidates and Admin Mode's
  lists. A test fails if game code (not tools) enumerates `ItemCatalog.Definitions`/`ActionCatalog.Definitions` outside
  the content store.
- The whole-build checks run in `ModValidation`'s trial build and per record at load (Decision 20).
- Measurement (benchmark skill, plus a test-only generator of a synthetic 10,000-definition master): startup, streaming
  frame cost and memory, eager vs by id, and `JsonDocument`'s share of load time. Report both. By-id loading stays
  whatever the result (saves and mods use the machinery); a TODO.md entry for streaming reads or a binary encoding is
  added only if parsing is a meaningful share (Decision 3).
- Checks:
  - A seeded headless run -- expect Phase 4's fingerprint.
  - Walk across several neighborhood shifts with `--diagnostics=gauges` and F3 open -- expect the loaded-definition
    gauges to rise only as new neighborhoods name new content, the missed-plan gauge to stay at 0, and no frame spike at
    a shift.
  - Open a fresh chest repeatedly -- expect items from the whole catalog, including ones nothing nearby has loaded.
  - Grant a save-created action by Admin Mode before anything has loaded it -- expect it found.

### Phase 6 -- Mod manifests

- A mod folder may hold `Definitions/*.jsonl`, a Mod layer in load order between master and save (Decision 7).
  `ModValidation` checks it in the mod's trial build (master plus that mod's layer); a failing record is a
  `ContentFailure` naming the mod, and the mod is dropped.
- The `Redirects` table is read and kept (Decision 21).
- The Workshop marks mod-supplied records with their mod in the Items, Actions and Auras tabs.
- `Mods.ExampleMod` ships one item and replaces one built-in by Guid; `Mods.TestFixtures` gets a mod with a broken
  record for the failure path.
- Remove the TEMPORARY Admin Mode runtime-definition options (Phase 4).
- Checks:
  - Build ExampleMod into GameShell's `Mods` and run -- expect its new item in a shop's stock and the replaced built-in's
    new numbers in its tooltip.
  - Add the broken fixture -- expect the mod dropped with its file and line on `Console.Error`, the game otherwise
    normal.
  - Open the Workshop -- expect the mod's records marked with the mod's name.
  - Right-click the player in Admin Mode -- expect none of Phase 4's temporary options.

### Phase 7 -- Mod area files

Map areas are content too: `Areas/*.json` and the `AreaPalettes`/`AreaPlacements` tables, plus `AreaPools` once TODO.md's
"Area pools" lands.
- A mod folder may hold `Definitions/Areas/*.json` and the area tables, layered like Phase 6's tables: an area with a
  master area's Guid replaces it, a new Guid adds one.
- `ModValidation` checks a mod's areas with `AreaContentValidation` in its trial build; a failing area is a
  `ContentFailure` naming the mod, its file, row and column, and the mod is dropped.
- A mod's fixed placement may not overlap master's or another mod's; the later one in load order is refused with a
  `ContentFailure` rather than stamped over the earlier.
- The Workshop's Map tab marks mod-supplied areas with their mod.
- `Mods.ExampleMod` ships one area placed in a neighborhood next to the spawn; `Mods.TestFixtures` gets a mod with an
  area holding an undefined symbol.
- Checks:
  - Run with ExampleMod -- expect its area beside the starting area.
  - Add the broken fixture -- expect the mod dropped with its file, row and column reported.
  - Give ExampleMod's placement the starting area's position -- expect it refused and the starting area intact.
  - Open the Map tab -- expect the mod's area listed and marked.

## Dependencies on other work

- **"Workshop: edit Items" (and Actions, Auras)** saves drafts layered over master. Phase 3's stack is that layering;
  if the edit work starts first, it builds its drafts layer on Phase 3 rather than a second mechanism.
- **"Content-only mods -- overriding files by path"** (Engine, Low): Phases 6-7 cover manifest tables and areas; sprites,
  fonts and whole-file overrides stay that item's.
- **"Data storage"** and **"Save and load Beyond neighborhoods"** (Global): Phase 4's `SaveFolder` and header are the
  pattern they add files to.

## Out of scope / follow-ups (each a TODO.md entry when this lands)

- **The Action and Item Builder** is now the Workshop's Items/Actions/Auras tabs ("Workshop: edit Items", etc.); in-game
  creation calls `ContentStore.CreatePersistent`. The NPC Builder follows them.
- **Terrain and blueprints as data**, after which code-declared auras move into the auras table.
- **Entity state in saves**: per-instance overrides as base id + patch, compacting unreferenced save definitions,
  applying redirects.
- **Streaming reads or binary encoding**: only if Phase 5's measurements warrant it.
- **Hot reload** of a changed table into a running game.
- **Editing mod and save manifests in the Workshop** (the IDE plan's follow-up).
