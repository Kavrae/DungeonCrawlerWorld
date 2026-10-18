# The engine IDE shell and its Items, Actions and Auras tabs

(Child 5 of `EPIC-sprites-ui-engine-and-dev-tools.md`. Started 2026-10-16. All decisions confirmed 2026-10-16. Phase 0 landed 2026-10-17 (tested in-game). Phase 1 landed 2026-10-17 (tested in-game). Phase 2 landed 2026-10-17 (tested in-game). Phase 3 landed 2026-10-17 (tested in-game). Phase 4 landed 2026-10-18 (tested in-game). **Complete at Phase 4, 2026-10-18:** view-only tabs are enough for this child; Phases 5-7 (editing) moved to TODO.md as "Workshop: edit Items", "Workshop: edit Actions" and "Workshop: edit Auras" (Engine, High), and this plan's Design and Decisions sections stay their design. Settled in review: Items,
Actions and Auras are separate tabs; phase order read-only Items → Actions → Auras, then edit Items → Actions → Auras;
`ContextMenu` generalised into a `Dropdown` element; the codec plan as described (codecs declare their fields); a drafts
manifest the IDE layers over master and the game never loads; any master record can be disabled, with an optional load
replacement and a Replacement/Cascade/Cancel confirmation when it has dependents; a Visual Studio-style bottom panel
(Error List, Dependents) and docked windows over popups; no in-game Action Details yet; separate game and IDE test
projects; the engine is named Cadence and the IDE Cadence Workshop; the folder structure (Phase 0) is approved.
Pairs with `PLAN-data-driven-actions-and-items.md`, not started --
see "Sequencing".)

## Context

What exists today:
- **No IDE.** The only dev tool is `DevTools/SpriteManifestBuilder` (WinForms, on SpriteFormat). The epic decides the IDE
  is one FNA executable on UIEngine + Presentation + a Game build, each tool a tab, and that it never edits C#.
- **Every project sits at the repository root** (`Engine`, `UIEngine`, `SpriteFormat`, `Game`, `Presentation`, `Content`,
  the `DungeonCrawlerWorld` exe, `Tests`, `Mods/*`, `DevTools/*`), so nothing on disk says which are the reusable engine
  and which are this game. Only the architecture tests (`UIEngine_DoesNotReferenceGame`, `Game_DoesNotReferenceSpriteFormat`,
  ...) hold the line.
- **Actions, items and auras are C# only.** 7 actions, 13 items, auras living with what radiates them, registered by
  `CoreActionsModule`/`CoreItemsModule`. The data-driven plan (codecs, the content type registry, the master manifest in
  `Content/Definitions/Master/*.jsonl`) supplies the format these tabs edit -- **none of it exists yet**: no
  `Engine.Content`, no `DeclareContentTypes`, no `DeclaredTags`, no `BuiltIn*Ids`.
- **What the tabs can reuse, and what they can't:**
  - `ItemDetailsWindow` (Presentation) shows an `ItemDefinition`: name row, Effects, Activation (with
    `TargetShapePreviewElement` over `TargetShapePreviewGeometry`), Description, gold, Tags. It already shows an item from
    its definition alone (`NoStackInstanceId`: no blocker, no Compare) -- the IDE's case. It takes `ActionCatalog`,
    `GameplayTagRegistry`, `ActionStateView` and `IPlayerQuery`, i.e. a `GameSession`'s catalogs and views.
  - **No details view exists for an action or an aura.** Actions are described only by the hotbar's hover summary
    (`HotbarContent.TryGetSlotSummary`); auras only by their effects in an item's text. `ItemComparisonStatExtraction`,
    `EffectFormatting`, `CostText` and `ToggleText` already work from definitions.
  - UIEngine has `Window`, `TextWindow`, `TextBox`, `Button`, `Toggle`, `GridControl`, `TabbedContent`, `Folder`,
    `ContextMenu`/`ContextMenuController`/`ContextMenuOption` (headers, submenus, hotkey column, edge-aware placement at
    the cursor), `Tooltip`, scrollbars, `DebouncedTextFilter`, `UiLayerHost`, `UiInputController`, `KeyPressInputHandler`.
    It has **no dropdown, numeric field, list editor, split pane or colour field**, and nothing handles the OS window being
    resized (the game is a fixed 1600x900).
- **A Game build without a world exists:** `ModValidation` trial-builds each mod with `GameBuildPass.Run(...,
  GameBuildPass.CreatePlaceholderMap(), ...)`; the epic decides the IDE gets its catalogs that way, so mods' content shows
  with no new mechanism.
- `GameLoop` is the pattern for an FNA host: `UiEngineBootstrapper.Build` → `UiEngineContext.LoadContent` → a shell that
  owns the `UiLayerStack`, `UiLayerHost` and `UiInputController`.

## Research

### How editors edit content

**Unity -- the Inspector edits the serialized form, not the object.** The default Inspector draws a `SerializedObject`
as a tree of `SerializedProperty` values; edits go to the serialized data and are applied back. A `PropertyDrawer` or
`CustomEditor` replaces the generic drawing for one type. Polymorphic fields (`[SerializeReference]`) long had no type
picker in the default Inspector, so every project wrote its own -- the picker for "which subtype goes in this slot" is
core, not optional. Undo is recorded against the serialized object.

**Unreal -- the Details panel from reflection plus metadata.** `UPROPERTY` reflection builds the panel; metadata shapes it
(`ClampMin`/`ClampMax`, `EditCondition`, `DisplayName`, categories); `Instanced`/`EditInlineNew` give an inline subobject a
class picker; `IDetailCustomization`/`IPropertyTypeCustomization` override per class or struct. Edits run inside an
`FScopedTransaction`, which undo replays. Data Tables are edited as rows of one row struct.

**Godot -- the Inspector from a property list with hints.** `_get_property_list()` (name, type, `PropertyHint`: range,
enum, resource type); `EditorInspectorPlugin` overrides one property's editor; a Resource slot offers "New <Type>"
filtered by the slot's base type. Undo through `EditorUndoRedoManager`.

**Bevy -- a runtime type registry.** `bevy_reflect`'s `TypeRegistry` describes types at runtime; inspectors
(`bevy-inspector-egui`) build their UI from the same registry that serializes scenes.

What carries over: edit the data and rebuild the object (definitions are immutable records whose constructors derive
state, and a mod's entry types are unknown to the IDE at compile time); one generic editor from the schema the format
already knows, with custom editors only where they pay; a type picker for every polymorphic slot; undo as a log of edits
to the data; preview with the game's own views.

### Work-in-progress content: what the industry does

The proposal: records that are incomplete or fail validation go to a separate manifest that the IDE unions with master
and the game never loads.

- **Bethesda Creation Kit -- work lives in a plugin the game doesn't load.** Edits are saved into a plugin (`.esp`)
  layered over the masters; the game loads only the plugins enabled in its load order. A plugin with errors still saves
  (the CK warns); a half-built quest sits in a plugin nobody has enabled. This is the proposal almost exactly: a layer
  over master, same format, excluded from the game by not being loaded rather than by a flag inside it.
- **Unreal -- save always, validate separately, exclude by folder.** Any asset saves whatever its state; a Blueprint that
  fails to compile saves too and is marked broken. Data Validation (`IsDataValid`, the Data Validation plugin) reports on
  save and before submit but never blocks saving. What ships is decided at cook time: "Directories to never cook", and the
  `Developers/<user>` folder, whose assets are cooked only if shipping content references them. WIP lives in an excluded
  folder until it is moved into content.
- **Unity -- excluded by location or group.** Assets under an `Editor` folder never go into a player build, and a scene
  ships only if it is in Build Settings; Addressables groups carry an "Include in Build" switch. Again: the asset saves
  freely, and inclusion is decided by where it lives.
- **Godot -- export filters.** An export preset can export only selected resources or exclude by filter; the project
  itself holds everything.
- **Content management systems (and some games' data, e.g. records with an `enabled`/`draft` field)** keep a draft state
  on the record itself. That only works when a draft is still a valid record the runtime can parse and skip.
- **Version control** is the other answer: WIP on a branch or a Perforce shelve. It isolates whole change sets, not
  records, and needs no tooling -- it's complementary, not a replacement.

**Verdict: the proposal matches the industry norm** -- always save, report validity separately, and keep unfinished
content out of the game by where it lives (a separate layer) rather than by blocking the save. A separate file beats a
draft flag here for one specific reason: a failed record may not decode at all (a missing field, an unknown
discriminator from a mod that's gone), so it can't sit in a manifest the game's strict loader reads. Four refinements
the research points to, folded into Decisions 8-11:
1. **A draft is stored as the editor's document, decoded or not**, so a record that fails to parse is still opened,
   shown with its errors and fixed -- Bethesda saves a broken plugin, Unreal a broken Blueprint.
2. **"Incomplete" can't be detected, so a draft is also something the author chooses.** A record goes to master only when
   it passes validation *and* isn't held as a draft; Publish is the explicit move (Unreal's "move out of Developers").
3. **A draft of an existing record has the master record's Guid** and overrides it in the IDE only -- the game keeps the
   last published version, as a Bethesda plugin overrides a master record only where it's loaded.
4. **Published content may never reference a draft** -- Unreal's cook-by-reference rule from the other side. The trial
   build of master alone catches it, and Publish carries along (or refuses on) drafts the record needs.

### Removing content that something references

- **Unreal's Delete Assets dialog** lists everything that references the asset being deleted and offers **Replace
  References** (pick another asset of the same type; referencers are re-pointed and a redirector is left behind),
  **Force Delete** (referencers keep a broken reference) and Cancel. `CoreRedirects` map old names to new at load, so
  content and saves made before the change still resolve.
- **Factorio** migrations rename one prototype to another for existing saves; a prototype removed with no migration drops
  everything that referred to it.
- **Databases** give a foreign key `ON DELETE RESTRICT`, `CASCADE` or `SET DEFAULT`, the same three answers as Cancel,
  Cascade and Replacement.
- **Bethesda** marks a record deleted in a plugin rather than erasing it from the master, and the deletion can be undone by
  removing that plugin.

What carries over: disabling a record is a flag on it, not an erasure, so it can be turned back on. The dialog lists the
referencers before anything happens. Replacement is a redirect that every load path resolves, saves included. With no
replacement, the reference is dropped, which is Factorio's behaviour.

## Naming

What sets Engine and UIEngine apart, taken together:
- **Deterministic.** A seed reproduces the world: seeded rolls per entity (`BlueprintContext.Rolls`), spawn records that
  rebuild an entity exactly (`SpawnRecordRebuilder`), cell looks derived from position and a layout seed, streaming
  applied after a fixed 30-update delay instead of when a worker happens to finish, and no wall-clock budgets.
- **An unusually large number of entities.** About 660k in the default world, made affordable by skeleton entities,
  flyweight terrain, pools paged by id range, and work striped across frames.
- **Flat cost, spread over frames and distance.** Four processing tiers tick at different rates by distance from the
  player; timers are deadlines on wheels, so nothing counts down; the UI rebuilds text only when what it shows changes.

**Chosen 2026-10-17: the engine is Cadence and the IDE is Cadence Workshop.** The name stands for the steady update
cadence the engine keeps across a very large entity count: every system does a flat share of its work each frame,
and every tier ticks on a fixed rhythm. The engine's root folder is `Cadence/`, and the IDE's projects are named
`Workshop` (`Cadence/Workshop`, `DungeonCrawlerWorld/Workshop`, `Workshop.Tests`). The assembly names Engine,
UIEngine and SpriteFormat are unchanged.

The first choice, Murmuration / Murmuration Studio (2026-10-16), was replaced on 2026-10-17 after polling received it
poorly. Laplace, Orrery, Myriad and Abacus were dropped because the names are already taken; Antikythera, Clockwork
Legion/Legion, Seedfall and Tessera were the other options.

## File structure (approved 2026-10-16)

The rule it encodes: the engine folder is reusable by another game and references nothing outside itself; the game
folder builds on it. Both shells start the game; builders start from the IDE.

```
DungeonCrawlerWorld.sln            solution folders mirror the two roots
Directory.Build.props
Cadence/                       the engine -- reusable, references nothing outside this folder
  Engine/Engine.csproj             (assembly names unchanged: Engine, UIEngine, SpriteFormat)
  UIEngine/UIEngine.csproj
  SpriteFormat/SpriteFormat.csproj
  Workshop/Workshop.csproj             Cadence Workshop's shell as a library: tab host, IIdeTab, launching the game,
                                   the document/inspector over Engine.Content's schemas, drafts layering
  Tools/SpriteManifestBuilder/     WinForms, until it becomes the Sprites tab (child 7)
DungeonCrawlerWorld/               the game
  Game/Game.csproj
  Presentation/Presentation.csproj
  Content/Content.csproj           Definitions/Master shipped; Definitions/Drafts never copied to output
  GameShell/GameShell.csproj       today's DungeonCrawlerWorld exe (GameLoop, ShellBootstrapper...) -- playing
  Workshop/DungeonCrawlerWorld.Workshop.csproj
                                   the game's IDE exe -- developing: the Workshop library + the game's builders
  Builders/Builders.csproj         the Items, Actions and Auras tabs (one library, three IIdeTab types)
  Tools/ContentManifestExporter/   the data-driven plan's one-shot exporter
  Mods/Mods.ExampleMod, Mods/Mods.TestFixtures
Tests/Tests.csproj                 the game's tests (Engine, UIEngine, Game, Presentation, GameShell) -- today's project
Workshop.Tests/Workshop.Tests.csproj
                                   the IDE's tests: the Workshop library, the game's Workshop exe, the Builders
```

- **Two test projects: the game's and the IDE's.** `Tests` stays as it is: Engine, UIEngine, Game and Presentation
  tests, plus the architecture tests saying the game's assemblies never reference either Workshop project. These read
  assembly metadata, so `Tests` never references the IDE. `Workshop.Tests` references the Workshop library, the game's
  Workshop exe and Builders. Its tests cover the shell, the content session, the document and inspector, drafts,
  disabling and the panel. A game-only change runs one project and an IDE-only change the other. Splitting the engine's
  tests from the game's is a separate question (Open questions).

- **Starting the game from either shell:** `GameShell` is the game. The IDE's Play command launches the built `GameShell`
  exe as its own process (with the IDE's seed/arguments), so playing never depends on the IDE and a crash in one doesn't
  take the other. Hosting the game in-process is child 8's research question; nothing here prevents it.
- **Builders only from the IDE, standalone as the easy secondary:** builders are tabs in a library, so they have no exe of
  their own. `DungeonCrawlerWorld.Workshop --tab=Items` opens the IDE with only that tab -- standalone in effect, with no
  extra projects to keep building. A separate exe per builder is possible later, but costs a project each for nothing the
  flag doesn't give.
- **Generic vs the game's:** the shell, tab host, Play, the schema-driven inspector, the field editors and the drafts
  layer know nothing about items -- they work from `Engine.Content`'s registry -- so they are Cadence's. What the game adds
  is its tabs (which tables, which previews: Item Details, Action Details) and its build (`GameSession` for catalogs).
- **Enforced:** an architecture test that no project under `Cadence/` references one under `DungeonCrawlerWorld/` (it
  generalises `UIEngine_DoesNotReferenceGame`), and that Game, Presentation and GameShell never reference either
  Workshop project.
- Paths that move: `GameLoop.FindProjectRoot` (finds the `.sln`, unaffected), the sprite builder's `RepoPaths`, the
  phase-performance-testing skill's `dotnet run --project` path, CLAUDE.md's Commands, `Tests.csproj`'s project references,
  and the mods' `Private="false"` references.

## Sequencing with the data-driven plan

These tabs edit the master manifest, which the data-driven plan creates. Only its **Phases 1-2** are needed (codecs and
the content type registry with field schemas; the master manifest loaded eagerly). Its Phases 3-5 (save folders, load by
id, mod manifests) are not.

1. This plan's Phases 0-3 (structure, shell, read-only tabs) -- no dependency on the data-driven plan.
2. Data-driven Phases 1-2. That plan needs three amendments:
   - its codecs declare their fields, as it already describes;
   - `Content.csproj` copies `Definitions/Master/**` only, never `Definitions/Drafts/**`;
   - the loader honours a record's `disabled` flag and `replacement` (see "Disabling a master record"). A disabled record
     is read up to its id and flags and then skipped. Its replacement goes into the catalogs' Guid redirects, which every
     by-Guid lookup resolves. Those lookups are the catalogs' `TryGet`, a scroll's spell, an aura grant, and the
     `BuiltIn*Ids` lookups in blueprint steps, and later the save system's entity references.
3. This plan's Phases 4-7 (Dropdown and controls, then editing).
4. Data-driven Phases 3-5 whenever wanted.

## Design

### The shell (Cadence Workshop)

- **`WorkshopHost : Microsoft.Xna.Framework.Game`** (Workshop library) -- the `GameLoop` pattern without a simulation:
  `UiEngineBootstrapper.Build`, then the tabs the game exe hands it. `DungeonCrawlerWorld.Workshop`'s `Program` builds the
  content session (below), `MissingSpriteCheck`, and passes its tabs. No `EcsContext.Update`, no `BeginSession`.
- **Resizable.** `Window.AllowUserResizing`; `ClientSizeChanged` resizes the root window and updates the input
  controller's screen size and the layer host. UIEngine work in Phase 1, since every later host needs it.
- `WorkshopShell` owns the `UiLayerStack`, `UiLayerHost` and `UiInputController`. Its own handlers go through
  `KeyPressInputHandler`: Ctrl+S, Ctrl+Z, Ctrl+Y, Ctrl+Tab, F5 Play, and Ctrl+\ to show or hide the bottom panel. One
  root window fills the client area, laid out like Visual Studio: a tab strip (`TabbedContent`) over the active tab, and
  under it a **bottom panel** (below) behind a splitter.
- `IIdeTab`: `Title`, `Element BuildContent(...)`, `Update`, `HasUnsavedChanges` (the title shows `*`), `Save`,
  `Rebind(...)` after a rebuild, `Diagnostics` (what goes in the Error List) and `SelectedRecord` (what Dependents
  shows). Quitting with unsaved changes asks first.
- **Panels, not popups.** The IDE shows what it knows in docked, informative panels in the Visual Studio style:
  errors, warnings, dependents, why a save went to Drafts, why a Publish was refused. A modal dialog is used only where
  the user has to decide before anything happens: the disable confirmation, quitting with unsaved changes, and Play with
  unsaved changes. Even a dialog lists what it is about.

### The bottom panel (Cadence Workshop)

This panel is generic. It knows `IdeDiagnostic` and `IdeRecordReference`, and nothing about items.
- **Error List** tab, like Visual Studio's. Each row has a severity icon, a description, the table, the record and the
  field path (`Effects[0][1].Count`), and whether the record is in Master or Drafts. Toggles above the rows show
  Errors, Warnings and Messages with their counts, and a scope switch chooses between all records and the current
  record. Columns sort. Double-clicking a row opens the record in its tab and focuses the field. Rows come from every
  tab's `Diagnostics`: per-edit validation, decode failures, and the last trial build's failures, each tied to a record
  where the build can name one. They refresh as the user edits.
- **Dependents** tab: the records that reference the one selected in the active tab. Each row has the table, the
  record, the referencing field, and its state (Master, Drafts or Disabled). It is the same list the disable
  confirmation shows. Double-clicking opens the dependent. Draft dependents are listed for information.
- An `IdeDiagnostic` is (severity, message, location: table, record Guid, field path). An `IdeRecordReference` is
  (from table/record/field, to table/record). Both are computed generically from the schemas: every field whose kind is
  a Guid reference names its target table. So a **reference index** over master and drafts, rebuilt on load and kept
  current on every edit, answers Dependents, the disable check, the publish-never-references-a-draft rule and Delete,
  with no per-table code.
- References made from C# (`BuiltIn*Ids` in blueprint steps) are invisible to the index. They show up as trial-build
  errors in the Error List, tied to the record they name.
- **Play** launches `GameShell` as a process (above), after asking to save if anything is unsaved.

### The content session (the game's)

- `ContentSession` (DungeonCrawlerWorld.Workshop) builds `ModValidation.Validate` then `GameBootstrapper.Build` over
  `GameBuildPass.CreatePlaceholderMap()` with tiny capacities -- a real `GameSession` with every catalog and view, no
  population, no player, never simulated. Item Details' views hold up with no player because the tabs always pass
  `NoStackInstanceId` (Phase 1 verifies `IPlayerQuery`/`ActionStateView` with no player entity).
- **Rebuilt after every save**, so a saved manifest is re-read the way the game reads it; tabs rebind.

### Three tabs

Each tab has the same three columns over its own table:
- **Browser** (left): the table's records, with master and drafts unioned. Drafts are marked, with an error badge when
  they fail. Disabled records are greyed and struck through, and show their replacement if they have one. A search box
  filters by name and tags (`DebouncedTextFilter`); a toggle shows or hides disabled records. Each name has its
  sprite or glyph beside it, and mod-supplied records are marked with their mod. The edit phases add New, Duplicate,
  Delete, Publish/Hold as draft, and Disable/Enable.
- **Inspector** (centre, edit phases): the selected record's fields, generated from its schema.
- **Preview** (right): **Items** -- Item Details. **Actions** -- Action Details (below). **Auras** -- Aura Details: glow
  colour swatch, falloff and magnitude, effects (through `EffectFormatting`), tags, and which actions/items grant it.

Cross-table references open the other tab at that record (a scroll's spell → Actions; an aura grant → Auras).

### Details views (Presentation)

The tabs show each record the way the game would, and no view exists for actions or auras. Item Details' section
builders (Effects, Activation with the shape preview, cost/toggle lines, Description, Tags) move out of
`ItemDetailsWindow` into a shared builder over `ActivatableDefinition`; `ItemDetailsWindow` keeps the item-only rows
(gold, contents, Compare); `ActionDetailsWindow` (Phase 2) and `AuraDetailsWindow` (Phase 3) are the new consumers.
Only the IDE uses them; the game gets no Action Details yet. Item Details must look exactly as it does now in the game.

### Dropdown (UIEngine)

`ContextMenu` becomes the first use of a general **`Dropdown`**:
- `Dropdown` (from today's `ContextMenu`): a pooled, top-level popup list of `DropdownOption`s (today's
  `ContextMenuOption`: label, hotkey/secondary text, enabled, headers, submenus, `OnSelect`) anchored either **at a point**
  (the cursor) or **to an element** (below it, flipping above at the screen edge, at least the anchor's width), with
  scrolling past a maximum height, keyboard navigation (Up/Down, Enter, Escape) and an optional selected-option marker.
- `DropdownController` (from `ContextMenuController`): one open at a time; Escape and outside-press close it -- the
  framework rules the context menu already has.
- **The context menu is a dropdown opened at the cursor on a right-click**: `ContextMenuController` keeps only the
  right-click gesture and the option-gathering (`DynamicHudContextMenus`), and every existing menu (corpse Loot, TextBox
  Cut/Copy/Paste, Admin "Spawn here >"/"Apply >", the inventory menus) works unchanged.
- **`DropdownField`**: a control showing the current value that opens its dropdown under itself -- the inspector's enum,
  type-picker and Guid-reference editor.

### The editor

- **The inspector edits a document, not the definition.** A `ContentDocument` is the record as the codec reads and
  writes it: a tree of fields (scalar, enum, tag set, Guid reference, list, polymorphic value with its discriminator).
  After each edit the tab decodes the document through the real codec into a definition for the preview; a document
  that doesn't decode keeps the last good preview and shows the failure at the field.
- **The schema comes from the codecs** (the data-driven plan, as described): key, display name, kind, default,
  constraints (min/max, which enum, which table a Guid points into). A mod's entry type gets an editor by registering its
  codec.
- **Generic field editors**, one per kind: text, number (int/float with limits), enum (`DropdownField`), bool (`Toggle`),
  tag set (declared tags only, from the session's `GameplayTagRegistry`), Guid reference (`DropdownField` over the target
  table, drafts included), colour (packed `uint`), list (add, remove, move up/down), polymorphic slot (a `DropdownField`
  over the content type registry's names for that base).
- **Custom editors only where the generic one is worse:** `TargetingSpec` (shape, range, area, with the shape preview
  inline) and appearance (glyph, colour, sprite name with the sprite drawn).
- **Declared tags are what is edited**; the activator's implied tags show read-only beside them (data-driven Decision 17).
- **Validation per edit**: the per-record checks that exist (undeclared tags, toggle rules, targeting modes) on the
  decoded definition. **On save**: a trial build of master alone with the record published, which decides where it goes.
- **Undo/redo**: a per-tab log of document edits.

### Saving: master and drafts

- **Two manifests, same tables, same format**: `Content/Definitions/Master/{Items,Actions,Auras}.jsonl` (the game's) and
  `Content/Definitions/Drafts/{Items,Actions,Auras}.jsonl` (the IDE's), both in the **source tree**, found from the
  repository root. Drafts are checked in (shared work in progress, like Unreal's never-cook folders) and never copied to
  the game's output; the game has no code that reads them.
- **The IDE layers drafts over master** (master → drafts, replacement by Guid, the data-driven plan's Decision 16 rule):
  a draft with a master record's Guid is an in-progress edit of it; the game keeps the published version.
- **Save never fails.** Ctrl+S writes each changed record to where it belongs:
  - **Master** when it decodes, passes the per-record checks, the trial build of master with it succeeds, and it isn't
    held as a draft. Its draft, if any, is removed.
  - **Drafts** otherwise. A draft is stored as the editor's document. A record that doesn't decode is kept as written,
    never dropped, and its errors are worked out again when it opens. Why a record went to Drafts is in the Error List.
- **Publish** clears "hold as draft" and saves. If the record would fail, nothing is published and the reasons go to the
  Error List. The same happens when its published form would reference a draft-only record, because published content
  never references a draft. The Error List row names that draft, and Dependents shows the chain.
- **New** gives a fresh random Guid and starts as a draft, since an empty record is incomplete by definition.
  **Duplicate** copies a record as a draft under a new Guid and name. **Delete** removes a draft freely. A master record
  can be deleted only while nothing references it, master or drafts, and the Dependents tab says why when it can't. A
  master record someone may still use is disabled instead (below), which can be undone and keeps saves loading.

### Disabling a master record

- **Any master record can be disabled.** The record keeps its place in master with `disabled` set and an optional
  `replacement` (the Guid of another record in the same table), both omitted when unset, so an enabled record's bytes
  don't change. The game skips loading it (Sequencing, amendment 3); the IDE still shows and edits it, greyed. **Enable**
  clears both and makes it load again.
- **Replacement is a load redirect.** When a replacement is set, every by-Guid lookup of the disabled record resolves to
  the replacement: content references, `BuiltIn*Ids` in C#, and saves once they hold entity references. An old save's
  stack of a disabled item becomes a stack of its replacement. A replacement must be an enabled master record in the
  same table, never a draft. If the replacement is later disabled too, the redirect follows the chain, and a cycle is
  refused at the field.
- **No replacement is the default.** The record simply isn't loaded. Anything that names it, whether a content
  reference or a stack or action in a save, is skipped, even when that leaves a gap in an inventory or an action list.
- **Disabling with no master dependents** just happens, and can be undone with Ctrl+Z. The replacement can be set or
  changed afterwards in the record's inspector, where Disabled and Replacement are a section of their own.
- **Disabling a record that master dependents reference asks first.** A dialog lists every dependent: each table and
  record, the referencing field, and for Cascade everything it would disable in turn. It offers three choices:
  - **Confirm With Replacement** turns the dialog into a replacement picker. The picker is a searchable list of the
    table's enabled master records, with a preview of the selected one. Choosing one disables the record with that
    replacement, and the dependents keep their references, now resolved to it. Cancelling the picker cancels the disable.
  - **Confirm With Cascade** disables the record and every master dependent, following their dependents until none are
    left, all with no replacement. One undo step reverses the whole cascade.
  - **Cancel** changes nothing.
- **Drafts are never asked about.** A draft is not loaded, and published content may not reference one, so disabling a
  draft has nothing to protect. A draft is held or deleted instead, and has no Disable command. Draft dependents of a
  master record are listed in the dialog for information. They don't block anything and aren't cascaded, but once
  published they will name a disabled record and show an Error List warning if it has no replacement.
- **Validation:** published content that references a disabled record with no replacement is an error. That is what
  Cascade or a replacement resolves. A C# reference to such a record fails the trial build and lands in the Error List.
- Records in a fixed order (by Guid) through the codecs' canonical writer, so a change shows in git as the changed lines
  only.
- Only master and drafts are edited; mod and save manifests are later work.

## Decisions

Settled 2026-10-16:
1. **Items, Actions and Auras are separate tabs**, each over its own table, with the same three-column layout.
2. **Phase order**: read-only Items, Actions, Auras; then edit Items, Actions, Auras.
3. **`ContextMenu` is generalised into `Dropdown`**; the context menu becomes a dropdown at the cursor.
4. **Codecs declare their fields** (the data-driven plan as described); the inspector is generated from that, with a type
   picker for every polymorphic slot and custom editors only for targeting and appearance.
5. **A drafts manifest**: incomplete and failed records are saved there; the IDE unions it with master; the game never
   loads it. (Reviewed against the industry above; refinements in 8-11.)

Also settled 2026-10-16:
- **Any master record can be disabled**, which stops it loading. An optional replacement redirects every load of it,
  saves included. No replacement is the default, and skips it even when that leaves a gap.
- **Disabling a record that master dependents reference asks first:** Confirm With Replacement (which opens the
  replacement picker), Confirm With Cascade, or Cancel. Drafts are never asked about.
- **An Error List in a Visual Studio-style bottom panel**, with a Dependents tab beside it for the record being edited.
- **Informative docked windows over popups** throughout the IDE.
- **No in-game Action Details yet.**
- **Separate test projects** for the game (`Tests`) and the IDE (`Workshop.Tests`).

Also confirmed 2026-10-16 (proposed in review, approved as written):
6. *(Settled 2026-10-17 (replacing Murmuration / Murmuration Studio, 2026-10-16): the engine is **Cadence** and the IDE is **Cadence Workshop**. See "Naming".)*
7. *(Approved 2026-10-16.)* **File structure** as above (Phase 0): an engine root and a game root; builders as tabs in a library, `--tab=` for
   standalone; Play launches `GameShell` as a process.
8. **A draft is the editor's document**, stored whether or not it decodes.
9. **Drafts are also chosen** ("hold as draft"); Publish is the explicit move to master.
10. **A draft of a master record shares its Guid** and overrides it in the IDE only.
11. **Published content never references a draft**, enforced by the trial build of master alone.
12. **Resizable IDE window from Phase 1**, in UIEngine.
13. **Action Details and Aura Details in Presentation**, sharing Item Details' section builders.
14. **The inspector edits the codec's document and rebuilds the definition** for preview, never the C# object.
15. **No hot reload into a running game** (the data-driven plan's follow-up); Play starts a fresh process.
16. **`disabled` and `replacement` live on the record itself**, not in the data-driven plan's `Redirects` table, which
    stays for records removed outright. A disabled record still holds its definition and can be enabled again.
17. **Redirects follow chains and refuse cycles**, and a replacement must be an enabled master record in the same table.
18. **Cascade disables with no replacement**, and one undo step reverses the whole cascade.
19. **One reference index from the schemas** answers Dependents, disabling, Delete and the draft rule. C# references are
    found only by the trial build.
20. **Delete of a master record is allowed only with no dependents at all**; otherwise the answer is to disable it.

## Phases

Each phase ends with the IDE (and, where Presentation or UIEngine changed, the game) running and a stop for testing
before the next.

### Phase 0 -- Engine root and game root (landed 2026-10-17, tested in-game)

- Move the projects into `Cadence/` and `DungeonCrawlerWorld/` per the file structure (with `mv`, never `git mv`); the
  exe becomes `DungeonCrawlerWorld/GameShell`; solution folders mirror the roots. No namespace or assembly renames.
- Update every moved path: project references, `Tests.csproj`, mods' references, the sprite builder's `RepoPaths`,
  CLAUDE.md Commands, the phase-performance-testing skill.
- The architecture test: nothing under `Cadence/` references anything under `DungeonCrawlerWorld/`.
- Verify: build, all tests, the game runs, a seeded benchmark run works through the skill, the sprite builder opens and
  saves the manifest.

### Phase 1 -- The shell and the Items tab, read-only (landed 2026-10-17, tested in-game)

Where the implementation differs from the design above:
- **The tab strip is the shell's own window, not `TabbedContent`.** `TabbedContent` swaps one body window's content,
  which can't hold a tab of several free-standing windows. The shell draws a strip across the top (tabs, a status
  line, Play), and the open tab adds its own top-level windows to the Base layer in the area beneath it
  (`IIdeTab.Open(context, area)` / `Resize` / `Close`). This is how Item Details is already hosted in the game.
- **`IIdeTab` has only what Phase 1 uses:** `Title`, `Open`, `Resize`, `Close` and `Update`. `HasUnsavedChanges`,
  `Save`, `Rebind`, `Diagnostics` and `SelectedRecord` are added by the phases that need them.
- **Two columns for now.** The browser and Item Details; the inspector column arrives with Edit Items.
- **No mod marker in the browser yet:** the item catalog doesn't record which mod registered a record. It comes with
  the manifest layers, which know their source.
- **Groups** are by how an item is used: Potions, Scrolls, Wands, Toggles, Other (`ItemBrowserSections`). Search matches
  names and tag display names.
- **Play** finds GameShell's build output from the repository root (the IDE's own configuration), starts one game at a
  time, and reports on the strip's status line. F5 does the same.
- **Command line:** an unknown `--tab=` name prints the known tabs to standard error and exits with code 2.

- `Cadence/Workshop` (`WorkshopHost`, `WorkshopShell`, `IIdeTab`, Play); `DungeonCrawlerWorld/Workshop` (the exe,
  `ContentSession`, `--tab=`); `DungeonCrawlerWorld/Builders` with the Items tab: browser (search, selection) and Item
  Details preview. Records come from the session's `ItemCatalog` (no manifest yet).
- UIEngine: host resizing.
- `Workshop.Tests` is created. The content session builds with the built-ins and with `Mods.ExampleMod`, and `--tab=`
  opens one tab. In `Tests`: neither Workshop project is referenced by Game, Presentation or GameShell, and resizing is
  driven through the real `UiInputController`.
- Verify: launch the IDE and `--tab=Items`, resize, search, select every item and compare with the game's Item Details,
  Play starts the game.

### Phase 2 -- The Actions tab, read-only (landed 2026-10-17, tested in-game)

What was built, and where it differs:
- **`ActivatableDetailsSections`** (`Presentation/UI/Details`) holds every section Item Details and Action Details share:
  name row, effects ("While on" for a toggle), cost, activation with the shape preview, description, tags, and an
  icon-and-text row. `ItemDetailsWindow` builds the same rows in the same order through it and keeps only its own
  parts: the stack's blocker, the gold row, comparison colouring and Compare. `ItemComparisonStatExtraction.ExtractEffectStats`
  takes any `ActivatableDefinition`.
- **`ActionDetailsWindow`** shows an action from its definition alone: Item Details' order without gold, blocker or
  comparison. Only the IDE uses it.
- **`RecordBrowserTab<TRecord, TPreview>`** (Builders/Browsing) is the browser-and-preview tab the Items tab had, made
  generic, so Items, Actions and (next) Auras each say only how records are grouped, drawn and previewed.
  `RecordBrowserSections` does the grouping, sorting and search for all of them. Selection is by reference.
- **Action groups:** Abilities (direct actions), Spells, Toggles (any action with a toggle, first), Other.
- **Tests:** `DetailsWindowsTests` (Tests) builds Item Details for every built-in item and Action Details for every
  built-in action and checks the shared sections appear in order (plus gold for items, none for actions);
  `ActionBrowserSectionsTests` (Workshop.Tests).
- **Not done: a scroll's spell opening in the Actions tab.** Item Details shows the spell as a plain line, and making
  it a link means changing the game's window or adding a link to the IDE's preview, plus a shell call to switch tab and
  select a record. Phase 3 needs the same mechanism for aura grants opening the Auras tab, so it moved
  there for both (decided 2026-10-17).

- The shared section builder over `ActivatableDefinition`; `ItemDetailsWindow` on it; `ActionDetailsWindow`; the Actions
  tab. (A scroll's spell opening in the Actions tab moved to Phase 3.)
- Verify in the game: Item Details for every item unchanged (Comparison included). In the IDE: every action.

### Phase 3 -- The Auras tab, read-only (landed 2026-10-17, tested in-game)

What was built, and where it differs:
- **`AuraDetailsWindow`** (Presentation, IDE only): the name row (a swatch of the glow colour, since auras have no
  sprite), "Strength" (whether effects scale with strength, and how strength falls off), "Each tick" (its effects, or
  "None" for glow only), then tags. Built on `ActivatableDetailsSections`, which gained overloads taking an effect
  list, a tag set and a bare name.
- **`EffectEntries`** (Game.Effects, public): the walk over every effect entry an action or item holds, nested ones
  included. `EffectContent` now uses it, so there's still one list of effect holders.
- **`ContentReferenceIndex`** (Builders/References): every reference between actions, items, auras, terrain and
  blueprints. A scroll casts its spell, an entry naming an aura grants it, terrain or a blueprint radiates its aura.
  It answers uses and used-by. A scroll whose spell id names no action (Scroll of Torch's, a synthesised spell) casts
  nothing. This is the live-definition precursor of Phase 4's schema-driven index.
- **The Related window** (`RecordBrowserTab`): a third window beside the preview with "Uses" and "Used by". A line
  naming an action, item or aura is a link; terrain and blueprints are plain lines. The plan's "the preview offers
  each reference as a link" became this window, so the game's own Item Details stays unchanged.
- **`TabNavigator`** (Workshop): a link queues a request, and the shell opens the tab and selects the record at the
  start of its next update, never inside the click (opening a tab closes the windows the click came from). The newest
  request wins. A tab not in the session (`--tab=`) or an unknown record is reported on the status line. Tabs select
  by id through `IIdeTab.TrySelectRecord`; `WorkshopTabContext.Navigator` carries it.
- **The Auras tab** groups by what puts each aura in the world: Terrain, Fixtures (blueprints), Items and actions,
  Unused. An aura with several sources goes under the most permanent one.
- **Tests:** `ContentReferenceIndexTests`, `AuraBrowserSectionsTests`, `TabNavigatorTests` and `ContentLinksTests`
  (Workshop.Tests); Aura Details for every aura (`DetailsWindowsTests`, Tests).

- `AuraDetailsWindow`; the Auras tab over the session's `AuraCatalog` (every aura the content names, with who grants it);
  aura grants in Items and Actions open it. A scroll's spell opens in the Actions tab (moved from Phase 2): the
  shell gains a call to open a tab with a record selected, and the preview offers each cross-table reference as a link.
- Verify: every aura (lava, Holy Ground, the shrine's, the Toxic auras, Scroll of Torch's glow) matches what it does in
  the game.

**Gate: data-driven Phases 1-2 land here** (with the drafts exclusion in `Content.csproj`). Landed 2026-10-18 (tested in-game): codecs with enforced schemas, the master manifest, disabled/replacement records and the Drafts exclusion are in place (see the data-driven plan's "Phases 1 and 2 as built").

### Phase 4 -- Dropdown and the editor controls (UIEngine) (landed 2026-10-18, tested in-game)

Landed 2026-10-18 (tested in-game). What was built, and where it differs:
- **`Dropdown`/`DropdownOption`/`DropdownController`** are the renamed `ContextMenu`/`ContextMenuOption`/the popup half of
  `ContextMenuController`. The controller opens at a point (`OpenAt`) or under an element (`OpenBelow`: at least its
  width, flipped above when there's no room below, scrolling past a maximum height, the current option marked and
  highlighted). `UiInputController` takes the `DropdownController` and owns its rules: Escape and an outside press close
  it (a press on the element that opened it doesn't count as outside, so the element closes its own list), and Up/Down/
  Enter go to it before the focused element. `ContextMenuController` is now only the at-cursor gesture over the same
  dropdown (`ContextMenuController.Dropdowns`), so a context menu and a field's list are never open together. The layer
  keeps its name, `UiLayer.ContextMenu`. The existing context-menu tests pass with only the type renames.
- **`DropdownField`**: draws its value and an arrow; a press opens the list, a second press closes it; Space, Enter or
  Down when focused opens it.
- **`NumericField` and `ColorField`** derive from `TextBox`, which is no longer sealed and gained `AcceptsCharacter` (a
  filter for typed and pasted characters) and `SetText`. Both read their text on Enter or when focus leaves, and put the
  last value back when it doesn't parse. The number field clamps to its limits and rounds to its places; the wheel
  steps it, through a new `Element.OnMouseWheelAction` offered before any scrollable ancestor scrolls. The colour field
  reads `RRGGBB` or `RRGGBBAA` and draws a swatch.
- **`SplitPane`**: two pane windows and a splitter, dragged through a new pointer-drag interaction
  (`Element.IsPointerDragHandleAt`, `OnPointerDragAction`, `PointerDragCursor`), hit-tested ahead of an element's children.
  One pane keeps its size when the split pane is resized.
- **`ListView`**: draws its own rows (no child per row), so a long list costs only what's on screen. Sortable headers
  (numbers compare as numbers), an icon column, selection that survives a sort or new rows that still hold it,
  double-click and Enter activate, Up/Down/Home/End move. Cells are cut to their column once, when rows or widths change.
- **`ListEditor`**: rows of the owner's content, each with move up, move down and remove, and an add button. A button
  raises its request on the next update, never inside the click, since the owner answering it rebuilds every row.
- **`ModalDialog`**: a `Window` opened as a menu window (everything behind it dimmed and unreachable), with a message or
  a body the caller fills and a row of buttons. Exactly one button runs; Escape or the close button counts as the cancel
  button.
- **The bottom panel** is its own window along the IDE's bottom edge, not a `SplitPane` around the tabs: the tabs add
  their own top-level windows, and a split pane raised by a click would sit over them. Its top edge drags through the
  same pointer-drag hook, and the open tab is resized to what it leaves. Error List: mods dropped, settings refused, and
  every record of the source tree's master manifest that doesn't load (`ContentSession.LoadProblems`; the host returns
  them with its tabs as a `WorkshopTabSet`). Dependents: what names the record selected in the open tab
  (`ContentLinks.DependentsOf`, through `WorkshopTabContext.ShowDependents`); activating a line opens its record. No
  scroll casts Fireball in the shipped content (the Wand of Fireball holds its own effects), so the check below becomes
  Heal listing the Scroll of Healing.
- **A "Controls" tab** (`ControlGalleryTab`, Workshop) holds one of each control, wired to the status line -- the scratch
  panel for verifying them. Remove it or keep it once the edit phases use the controls.
- **Tests:** `DropdownTests`, `NumericAndColorFieldTests`, `SplitPaneAndListEditorTests`, `ListViewAndModalDialogTests`
  (Tests/UIEngine, through `UiInputController` with `ControlTestHarness`); `ContentLinksTests` and `ContentSessionTests`
  gained dependents and load-problem cases (Workshop.Tests). The reference index's own tests (a scroll names its spell, an
  item its aura) were already there from Phase 3.

The plan as written:

- `Dropdown`/`DropdownOption`/`DropdownController` from `ContextMenu`/`ContextMenuOption`/`ContextMenuController`; the
  context menu on top of it; `DropdownField`.
- Numeric field (on `TextBox`, with limits and the mouse wheel), list editor rows, colour field, and a split pane for the
  tabs' columns and the bottom panel.
- **List view**: rows under column headers, sortable, with an icon column, selection and double-click. The Error List,
  Dependents and the replacement picker are all list views.
- **A modal dialog** built on `Window` and a menu-mode layer, with a body of its own and buttons. Used only for the
  disable confirmation and the unsaved-changes prompts.
- The Workshop shell gets the bottom panel with its two tabs, filled from the read-only tabs for now. The Error List
  shows load failures (records that fail to decode, mods dropped) and Dependents shows references from the reference
  index.
- Tests in `Tests/UIEngine` drive each control through `UiInputController` (memory: shortcut tests miss what live
  testing catches), and the existing context-menu tests pass unchanged. In `Workshop.Tests`: the reference index over
  the built-in content, so a scroll names its spell and the Toxic Idol its aura.
- Verify in the game: every context menu (corpse, TextBox, Admin "Spawn here >"/"Apply >" with submenus, inventory). In
  the IDE: a scratch panel of each control, and the bottom panel's Dependents for Fireball listing the Scroll.

### Phases 5-7 -- Editing (moved to TODO.md 2026-10-18)

Edit Items, Edit Actions and Edit Auras are TODO.md items now (Engine, High): "Workshop: edit Items", "Workshop: edit
Actions", "Workshop: edit Auras". Their design is this plan's "The editor", "Saving: master and drafts" and "Disabling a
master record" sections and Decisions 8-20.

## Open questions

- None.

## Out of scope / follow-ups

- Editing mod manifests and save manifests; drafts for mods.
- Low-priority TODO.md items: "IDE bottom panel: an Output tab" and "IDE Error List: error codes" (Engine), and "Engine
  tests in a project of their own" (Global).
- Hot reload of a saved table into a running game; hosting the game inside the IDE (child 8).
- Other content as data (terrain, blueprints) and their tabs; the Map, Sprites and Diagnostics tabs (children 6-8).
- An NPC Builder (the data-driven plan names the Builder as its prerequisite).
