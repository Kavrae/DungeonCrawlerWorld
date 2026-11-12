# Workshop: edit Items -- the editing machinery every Workshop tab will use

Plans the TODO.md entry "Workshop: edit Items" (Engine, High): Phase 5 of `PLAN-ide-shell-and-actions-items-tab.md`.
The design confirmed there on 2026-10-16 ("The editor", "Saving: master and drafts", "Disabling a master record",
Decisions 8-20) stands and is not reopened. This plan checks it against the code as it is on 2026-11-12, settles what
it left open, and splits it into phases.

**Status:** written 2026-11-12 from a read of the code. Nothing is built. Decisions 1-11 and 14 were confirmed
2026-11-12 (3, 4, 6 and 11 with the changes written into them). Decisions 12 and 13 were confirmed the same day as the interim
answer: terrain and blueprints become content afterwards, as TODO.md entries of their own (see "Code-held references:
the interim and the end state"). Starts after `PLAN-npc-behavior-composition.md` (see "Order against other plans").

**Why it is the template:** "Workshop: edit Actions", "edit Auras", "edit map areas" and "edit sprites" are all written
as "on edit Items' machinery". So everything here is built game-agnostic in `Cadence/` unless it names an item, and the
Items tab is its first user. The measure of success is the last check of Phase 3: what a second table needs is one
description, not a second copy.

## What the code says today

### 1. In place

- **Schemas are enforced.** Every codec's `ContentSchema` is the whole of what it reads and writes
  (`ContentWriter.CheckKey`, `ContentObject.ReadWith`), and `ContentCodecTests` round-trips every built-in action, item
  and aura to the same bytes. Nothing in the Workshop reads a schema yet.
- **Layers already have a Drafts kind** (`ContentLayerKind.Drafts`, on top of the stack, "for an IDE's own builds only"),
  and an in-memory layer (`ContentLayer.FromText`), so a trial build of a master that isn't on disk yet needs no temp
  files.
- **Redirects follow chains** (`ContentIndex.TryResolve`, bounded by the redirect count, so a cycle ends quietly rather
  than being refused).
- **Per-record checks exist** (`ContentChecks.ProblemsOf` for an action, item and aura: the per-record forms of the
  whole-build checks).
- **The controls** (`DropdownField`, `NumericField`, `ColorField`, `ListView`, `ListEditor`, `SplitPane`, `ModalDialog`),
  the bottom panel (`SetProblems`, `ShowDependents`) and `ContentReferenceIndex`.

### 2. Gaps the confirmed design didn't know about

| Found | Consequence |
|---|---|
| `Engine.Content.ContentDocument` is taken: it is the one-record-per-file format areas use | The editor's record tree needs another name (Decision 2) |
| `ContentField` has no default; each codec passes its own to the writer and reader (`CanTrade` true, `Quantity` 1, optional fields that mean "none") | The inspector can't show what an omitted field means (Phase 0, Decision 7) |
| `IIdeTab` is `Title`/`Open`/`Resize`/`Close`/`Update`/`TrySelectRecord`. No save, undo, unsaved state or problems of its own | The shell needs an editing contract and Save/Undo/Redo commands (Decision 10) |
| Tabs are built once (`WorkshopHostOptions.CreateTabs`), each holding the session's catalogs; pooled windows (`ItemDetailsWindow`) hold them too, and `ElementPoolService` has only `RegisterFactory` | "The session is rebuilt after every save" needs a session holder and a way to drop pooled windows built on the old one (Decision 11) |
| The Error List is one static list plus what the shell adds (`_loadProblems`, launch options, leaks) | Tabs need to publish problems that change per edit |
| `RecordBrowserTab` rows are decoded records, selected by reference | A draft that doesn't decode has no record. Rows become Guids with an optional decoded value |
| `ContentReferenceIndex` is built from decoded catalogs, not from schemas (the IDE plan's Decision 19) | It can't see an unsaved or undecodable document. It does see what Decision 19 said it couldn't: blueprints and terrain (Decision 13) |
| `GameContent.WriteMaster` (the exporter) rewrites a whole table in catalog order with disabled lines moved to the end | Saving through it would reorder the file. The editor replaces lines in place instead (Decision 3) |
| GameShell reads the master copied into its `bin` (`GameContent.DefaultMasterDirectory`); the Workshop edits the source tree's | Play (F5) after a save shows the old content unless the game is rebuilt (Decision 8) |
| `WorkshopHost` has no hook on the window closing | The quit prompt needs one (Decision 14) |
| Four columns today -- browser 340, preview 460, an optional details pane, Related 320 -- already fill a 1600 window | There is no room for an inspector without moving something (Decision 1) |
| Health Potion is named from C# (blueprints' `UsesItems`) and replaced by ExampleMod | The TODO's own disable check runs into code-held dependents, which Cascade can't disable (Decision 12) |
| No shipped item names another item (no `Set` contents in `Items.jsonl`) | With only the Items tab editing, a content dependent has to be made to test Delete and Cascade (the Phase 2 and 3 checks do) |

## Decisions

Confirmed 2026-11-12 unless marked.

1. **Layout: Browser | Inspector | Preview over Related.** Three columns: the browser (left, fixed), the inspector
   (centre, takes the spare width, at least 440), and on the right one scrolling column holding the preview with Related
   under it. Related's "used by" half is already the bottom panel's Dependents; its "uses" half is short. Alternative: keep
   four columns and let the tab scroll sideways -- rejected, the preview is what an edit is checked against and must stay
   on screen. A read-only tab (Map) keeps today's layout. Whether Related's "used by" half should leave the column for
   the bottom panel's Dependents is decided by testing Phase 1, not before.
2. **The editor's tree is `EditableContentRecord`** (nodes: `EditableContentValue`), in `Cadence/Workshop/Editing`.
   A lossless JSON tree with each node matched to its `ContentField` where one exists, so a key the schema doesn't know
   and a value of the wrong kind are kept, shown and flagged, never dropped.
3. **Master is only ever written by the codec, one line at a time, in Guid order.** A record reaches master as: tree ->
   JSON -> decoded by the real codec -> `ContentTable.WriteLine`. So master bytes are canonical by construction, and an
   unchanged record can't change. A changed record's line is replaced where it sits and every other line is kept
   byte-for-byte, which is what gives the one-line `git diff`. Today's order isn't required, so the IDE plan's fixed
   order is adopted: Phase 0 sorts each master table by Guid once, as a change of its own, and from then on a new record
   is inserted at its Guid's place and a disabled one keeps its place. A record's position then depends on nothing but
   its Guid -- not on who added it or when -- so two people adding records rarely touch the same lines. A game loads by
   id, so file order should change nothing in play; Phase 0 proves it with the world fingerprint, and a fingerprint that
   moves is an order dependence to fix rather than a reason to keep the old order.
4. **Problems are field-level where the schema can say so, record-level otherwise.** Three layers, each feeding the Error
   List: (a) schema validation of the tree, with exact paths (`Effects[0][1].Count`): missing required field, wrong
   kind, not a member of the enum, outside minimum/maximum, unknown key or discriminator, a reference to nothing, an
   undeclared tag; (b) the real decode, whose `ContentFormatException` names a rule but no path; (c)
   `ContentChecks.ProblemsOf`. (b) and (c) show in a strip above the inspector and against the record. **An error at
   any layer fails the whole record**: where a problem is shown changes nothing about what it costs, so a record with
   one field-level error is never published and goes to Drafts on save, exactly as one that fails its decode. Only a
   warning (a draft dependent naming a disabled record, say) leaves a record publishable. A test holds (a) and (b)
   together both ways: no tree that passes (a) fails the decode for a reason (a) covers, and (a) raises no error on a
   tree the decode and the checks accept, so the Workshop never holds back a record the game would load.
5. **"Hold as draft" is an envelope key, `"hold": true`, legal only in a Drafts layer.** Without a stored flag a held
   draft that passes would be published by the next Ctrl+S. `ContentLayers` refuses it anywhere else, as it refuses
   `rm` outside a save. **New and Duplicate both start held**: a duplicate passes every check the moment it is made,
   and "copies a record as a draft" only means something if a save doesn't publish it.
6. **One undo log for the content workspace, not one per tab.** Cascade and Confirm With Replacement change records in
   several tables as one step; with a log per tab that step has no single place. Sprites and map painting keep logs of
   their own later: the log belongs to a set of documents that can reference each other. (The IDE plan said "a per-tab
   log"; this is the one place this plan departs from it.) **Where undo and redo take the user is one rule, never a
   guess:** every step records its *origin*, the one record the user acted on (the field's record, the record
   disabled), however many others it changed.
   - If the open tab shows any record of the step, the tab stays and that record is selected (the origin when it is
     among them, else the step's first record in that tab).
   - Otherwise the origin's tab opens with the origin selected.
   - Either way the status line names the whole step: "Undid Disable Health Potion: 1 item, 2 actions, 1 aura".
   So a step that changed three tabs' data jumps to at most one place, the record that caused it, and never away from
   a tab where its effect is already on screen.
7. **The schema holds the default; the writer checks the codec's against it always, the reader in Debug.**
   `ContentField.Default` (a value, or "none" for an optional field). `ContentWriter`'s keyed methods already look the
   key up, so the comparison is free there; the reader's is `[Conditional("DEBUG")]`, so loading costs nothing in
   Release and every test run proves both sides. Alternative: codecs stop passing defaults and both sides read the
   schema's -- one source, but a dictionary lookup per field on the load path, for no gain the check doesn't give.
8. **The game the Workshop starts reads the source tree's master**: Play passes `--master-content=<directory>`, which
   `WorldSessionBootstrapper` uses in place of `DefaultMasterDirectory`. Save, F5, see it -- no build. Alternative:
   Play builds when master is newer than the output's copy -- slower, and a failed build is a second thing to explain.
9. **Field editors live in UIEngine behind a description that names no content type.** `UIEngine.UI.Fields`: a
   `FieldEditorDescription` (label, value kind, minimum/maximum, choices, default text) and one row control per kind.
   The Workshop maps a `ContentField` to it; `PLAN-generated-component-property-access.md` Phase 3 maps a
   `ComponentPropertyDescriptor` to it. That is its decision 6 ("one field editor per kind for content inspectors and
   component inspectors alike") made concrete, and it is what lets the two plans land in either order.
10. **The shell's editing contract is its own interface, `IWorkshopEditingTab`**, never default members on `IIdeTab`
    (a default-bodied hook that drifts compiles as an unrelated method; CLAUDE.md, `InputHookImplementationTests`):
    `HasUnsavedChanges`, `Save`, `Problems` with a version. Undo and redo are the workspace's (Decision 6), handed to
    the shell as commands. The shell adds Save (Ctrl+S), Undo (Ctrl+Z), Redo (Ctrl+Y) as `WorkshopCommand`s, the `*` on
    a tab's tile, and the two prompts.
11. **The Workshop's own session is rebuilt on a worker and swapped between the Workshop's frames.** This is the build
    the tabs read their catalogs and previews from (`ContentSession`, never simulated), not a running game. A game
    started with Play is its own process, is never told about a save and never changes: definitions are fixed per
    session, so it shows saved content the next time it is started. A `ContentWorkspace` (the game's Workshop) owns
    the current `GameSession`; tabs and element factories read it through the workspace, never hold a catalog. A build
    shares nothing with another (CLAUDE.md, `GameBuildPass`), so building off the main thread is safe; the status line
    says "Rebuilding" and the tabs show the old session until the swap. `ElementPoolService` gains `ReplaceFactory<T>`,
    which drops pooled instances of `T`. Documents are keyed by Guid and outlive every rebuild, so selection, unsaved
    edits and the undo log are untouched by one.
12. *(Interim; see "Code-held references" below.)* **A code-held dependent can't be cascaded.** A blueprint's `UsesItems`/`UsesActions` and a terrain's aura are C#:
    the dialog lists them marked "code", and while any is listed Confirm With Cascade is disabled with that as its
    reason. Confirm With Replacement still works (the redirect covers `BuiltIn*Ids` lookups), and so does Cancel.
13. *(Interim; see "Code-held references" below.)* **Two reference sources, one question.** A schema-driven walk over every document (master lines and drafts, saved or
    not: each `Reference` field and list names its table) in `Cadence/Workshop/Editing`, plus the game's catalog index
    for what only C# holds, which gains blueprint `UsesActions`/`UsesItems`. Dependents, Delete, the disable dialog and
    "published content never references a draft" all ask the union.
14. **Quitting asks, and if the window can't be held open, nothing is lost anyway.** Phase 2 first finds out whether
    FNA lets the host refuse a close. If it does: the confirmed prompt (Save, Discard, Cancel). If it doesn't: unsaved
    documents are written to Drafts, held, on the way out -- "save never fails" already allows it -- and the next start
    says so on the status line.

Also settled by the code, no choice in it:
- **Mod records are read-only.** A record a mod wins is shown with its mod's label and no inspector edits; when a mod
  replaces a master record, the inspector edits master's and says "Replaced in the game by <mod>".
- **Drafts sit above mods in the IDE's session** (the layer order is fixed: master, mods, save, drafts).
- **`id` is never edited**; the inspector shows it with a copy button.

### Code-held references: the interim and the end state (settled 2026-11-12: keep the interim)

Decisions 12 and 13 exist only because blueprints and terrain are C#. If both were content with codecs:
- their references would be `Reference` fields, so the schema walk would be the **only** reference source (the IDE
  plan's Decision 19 as first written) and the catalog index would leave the question;
- Cascade could disable them like any record, so the "code" marker and Cascade's refusal would have nothing to apply to;
- a broken reference would be found per edit with a path, instead of only by the trial build.

So yes, data-driven is the cleaner end state. It is not a small step, and the two halves differ:

| | What it takes | Size |
|---|---|---|
| **Terrain as content** | `TerrainDefinition` is already declarative (key, sprite, link group, `TerrainAura`, `TerrainContact` with its `Effect` lists, which have codecs). A table, a codec, a Guid beside the key, and `TerrainRegistry` filled from content | Small: comparable to the Auras table |
| **Blueprints as content** | The facets are declarative (`Includes`, `Race`, `Class`, `Appearance`, `Actions`, `Auras`, `UsesActions`/`UsesItems`, soon `Behaviors`). The `Build` step is code (`Action<BlueprintContext>`), so either it is named from the record (a registered step by Guid) or what each step does becomes declarative entries. `PLAN-npc-behavior-composition.md` adds grants to blueprints and already defers their codecs to "when blueprints become content" | Large: its own plan, after NPC behavior |

Settled: **this plan isn't gated on either.** What the interim costs is one list the game supplies
(`ICodeHeldReferences`: who in code names which record) and one disabled button; it is built so that a kind which
becomes content simply stops appearing in that list, and when the list is empty the marker, the refusal and the
catalog index's part in Decision 13 are deleted with no change to the dialog or the walk. Terrain as content is a good
next plan after this one (it is the first new table and so the first real test of the template beyond Actions and
Auras); blueprints as content follows NPC behavior. If either lands before Phase 3 here, Phase 3 builds less.

## The design

### Where each piece lives

| Piece | Project | Why there |
|---|---|---|
| `ContentField.Default`, the `hold` key, `ContentTableLines` (a table file as raw lines by Guid: replace, append, remove, written whole through a temp file) | `Cadence/Engine` (`Engine.Content`) | The format's own rules |
| `UIEngine.UI.Fields`: row controls per kind, a collapsible section, a searchable choice list, a multi-choice field | `Cadence/UIEngine` | A non-game tool wants them unchanged; shared with the component inspector |
| `EditableContentRecord`, schema validation, `ContentInspector` (schema -> rows), `ContentEditLog`, `ContentTableDocuments` (one table's master and drafts as documents, the dirty set, where a save sends each), the document reference walk, the disable dialog, `IWorkshopEditingTab`, the shell's commands and prompts | `Cadence/Workshop/Editing` | Knows schemas and codecs, no game type. This is the template |
| `ContentWorkspace` (session holder and rebuild, the trial build of master alone, `ContentChecks`), the custom editors, `ItemsTab`'s description | `DungeonCrawlerWorld/Workshop` | Names `GameSession`, `ItemDefinition`, targeting |

What the game hands the engine side is a `ContentTableDescription<T>`: table and file name, how to summarise a raw line
for a browser row (name, sprite, glyph, tags), the per-record checks, the preview, the browser's sections, and any
custom editors. The Items tab is that description plus `RecordBrowserTab`.

### Editing

- **The tree.** Read from a line's JSON against the record's schema, recursively through `ContentTypeRegistry`
  (`GetValueCodec` for an `Object`, `MembersOf` for a `Polymorphic` slot, by its `$`). A field is **set** (present) or
  **unset** (absent: the row shows the default greyed, and a reset button unsets a set one). Envelope keys (`off`,
  `rep`, `hold`) are the record's state, not fields.
- **The inspector** is generated: one row per `ContentField`, in schema order, built only when the tree's shape changes
  (a list grows, a type is picked), never per value edit.

  | `ContentFieldKind` | Editor |
  |---|---|
  | Text | `TextBox` (multi-line for a description) |
  | Integer, Number | `NumericField` with the field's limits |
  | Boolean | toggle |
  | Enum | `DropdownField` over the enum's names |
  | Color | `ColorField` |
  | Guid | read-only text, copy |
  | Reference | searchable choice over the referenced table's documents (icon, name, Draft/Disabled marked); a link opens it in its tab |
  | Tag, TagSet | choice over declared tags only (`GameplayTagRegistry`), by the field's domain once `PLAN-npc-behavior-composition.md` Phase 1 gives fields one, each with its description as a tooltip |
  | Object | a collapsible section of its own rows; an unset optional one is an "Add" button |
  | Polymorphic | `DropdownField` over the family's registered names, then that member's rows. Changing type keeps the keys both schemas declare with the same kind and drops the rest, as one undo step |
  | List | `ListEditor`: add, remove, move up/down, each element by its kind |

- **Custom editors only where the generic one is worse** (confirmed): `TargetingSpec` (its rows beside
  `TargetShapePreviewElement`) and appearance. Appearance is three flat keys of the record (`sp`, `g`, `gc`), not an
  object, so a custom editor claims a **set of keys of one schema type**; the game registers it. The sprite name is a
  searchable choice over the manifest's entries with each drawn.
- **Declared tags are edited; implied tags are shown read-only** beside them (from the decoded activator).
- **Each committed edit** (Enter, focus leaving, a wheel step, a pick -- never a keystroke): the log takes it, schema
  validation runs on the tree, the tree is decoded through the real codec, `ContentChecks` runs on the result, the
  preview shows it. A tree that doesn't decode keeps the last good preview, dimmed, with the reason above the
  inspector.
- **Undo/redo** (`ContentEditLog`): a step is one or more (record, path, JSON before, JSON after), or a record added,
  removed or re-enveloped. Wheel steps and repeated commits on one field merge until focus leaves it. Ctrl+Z with
  uncommitted text in a field puts the field's value back and takes nothing from the log. Saving doesn't clear the log.

### Saving

- **Files:** `Content/Definitions/Master/Items.jsonl` and `Content/Definitions/Drafts/Items.jsonl`, both in the source
  tree; drafts are checked in and never copied to the game (`Content.csproj` already excludes them).
- **A draft line is the tree as JSON**, whether or not it decodes, with `hold` when chosen. A draft of a master record
  shares its Guid.
- **Ctrl+S** takes every dirty document of every content tab and sends each where it belongs:
  1. One that is held, fails schema validation, doesn't decode, fails its checks or references a draft-only record goes
     to Drafts.
  2. The rest are candidates. A trial build of master alone runs over an in-memory layer holding master with the
     candidates' lines in place (the built-ins only, `EcsBuildPurpose.Trial`, failures collected).
  3. A candidate a failure names goes to Drafts and the trial runs again without it. A failure naming no record sends
     every remaining candidate to Drafts with that failure against each.
  4. What is left is written to master in place; each one's draft, if it had one, is removed.
  Why each draft is a draft is in the Error List. Then the session rebuilds (Decision 11).
- **Publish** clears `hold` and saves that record; refused with its reasons when it would land in Drafts anyway.
  **Hold as draft** sets it and saves: a master record keeps its published line and gains a draft.
- **New**: a fresh random Guid, required fields empty, held. **Duplicate**: a copy under a new Guid and "<name> copy",
  held. **Delete**: a draft, freely; a master record only with no dependents of any kind, and Dependents says why not.
- **Markers:** `*` on a dirty row and on its tab; Draft, Held, Disabled and an error badge on the row.
- **Prompts:** quitting (Decision 14) and Play with unsaved changes (Save and Play, Play Anyway, Cancel).

### Disabling

As confirmed, plus Decision 12:
- Disable/Enable are browser commands on a master record; Disabled and Replacement are a section of their own at the top
  of the inspector. The line keeps its definition and place, with `off` and optional `rep`.
- With no dependents it just happens (one undo step). With master dependents the dialog lists every one (table, record,
  field, and for Cascade everything it would disable in turn; drafts and code for information) and offers Confirm With
  Replacement (a searchable `ListView` of the table's enabled master records with a preview), Confirm With Cascade (one
  undo step for all of it) and Cancel.
- A replacement must be an enabled master record of the same table. A chain is followed. **A cycle is refused at the
  field**: the engine's resolver ends one quietly today, so the refusal is new, in schema validation and in the layer
  merge (a cycle in a file is a `ContentFailure`).
- Published content naming a disabled record with no replacement is an error against the dependent.

## Phases

Each phase ends with the Workshop (and where Engine, UIEngine or Presentation changed, the game) running, and a stop
for testing before the next.

### Phase 0 -- Format groundwork (no UI)

1. `ContentField.Default` on every factory method, set in every codec's schema; the writer's check and the reader's
   Debug check (Decision 7). Optional fields declare "none".
2. The `hold` envelope key, refused outside a Drafts layer; a replacement cycle is a failure.
3. `ContentTableLines`.
4. `--master-content=` in GameShell (Decision 8); the Workshop's Play passes the source tree's.
5. Sort `Actions.jsonl`, `Items.jsonl` and `Auras.jsonl` by Guid, as a change of its own (Decision 3);
   `ContentManifestExporter` writes in that order and keeps a disabled line in its place.

**Tests:** writing every built-in record trips no default check; a codec whose writer default disagrees with its schema
throws naming the key; `hold` in a master layer is a failure of that line and in a Drafts layer is ignored by the
decode; replacing one line of each master table with itself leaves the file byte-identical; an insert lands at its
Guid's place and it and a remove touch no other line; every master table is in Guid order; a three-record replacement
cycle fails each of its lines.

**Check:**
- Start the game and the Workshop: expect no new failures, every tab as before.
- After the sort: headless on seed 1 before and after, expect the same world fingerprint; in the game, expect the
  inventory tabs, shop stock and Admin Mode's item list to read as before. A fingerprint that moved is an order
  dependence to find and fix before going on.
- Run `ContentManifestExporter` after the sort: expect `git diff` to show nothing.
- Hand-edit the Wand of Fireball's name in the source `Items.jsonl`, press Play without building: expect the new name in
  the game. Revert it.
- Headless A/B with the `phase-performance-testing` skill, `--diagnostics=startup`: expect startup and every row within
  run-to-run spread (the reader's check is Debug-only).

### Phase 1 -- Editing in memory (nothing is written)

1. `UIEngine.UI.Fields` and the missing controls (collapsible section, searchable choice, multi-choice), each added to
   the Controls tab.
2. `EditableContentRecord`, schema validation, `ContentInspector`, `ContentEditLog`.
3. `ContentWorkspace` holding today's session; `ContentTableDescription<ItemDefinition>`; `RecordBrowserTab` rows by
   Guid; the layout of Decision 1; the custom targeting and appearance editors.
4. `IWorkshopEditingTab`, the shell's Undo and Redo commands, per-edit problems into the Error List (double-click opens
   the record and focuses the field).

**Tests** (`Tests/UIEngine` through `UiInputController`; `Workshop.Tests` with a fake two-codec table and no game type,
then the Items table): every built-in item reads into a tree and writes back to its own line; every entry, activator
and contents type has rows for every field of its schema; an unknown key and a wrong-kind value survive a round trip
and are flagged at their path; a polymorphic type change keeps shared keys; undo and redo restore the exact JSON; wheel
steps on one field are one step; schema validation and the decode agree over mutated built-ins (Decision 4).

**Check (Workshop, Items tab):**
- Select the Wand of Fireball: expect browser, inspector and preview side by side at 1600 wide, Related under the
  preview, nothing clipped.
- Change its damage and press Enter: expect Item Details to show the new number at once, `*` on the row and the tab.
- Ctrl+Z: expect the old number and no `*`. Ctrl+Y: expect it back.
- Clear the name: expect an Error List line for the Wand at `Name`; double-click it and expect the field focused.
- Set Gold value to -5: expect it clamped to 0.
- Reset "Can trade" on an item that sets it false: expect the row greyed and reading its default, true.
- Change a potion's activator type to Scroll: expect the Scroll's rows, an error for its missing spell, and the preview
  dimmed with the reason above the inspector. Pick Heal as the spell: expect the preview back.
- Add a tag: expect only declared tags offered. Add an effect entry and move it up: expect the preview's effect order
  to follow.
- Change the targeting shape: expect the shape preview beside the rows to change.
- Close and reopen the Workshop: expect every edit gone (nothing is written yet) and `git status` clean.
- In the game: expect Item Details, the inventory and every context menu unchanged.

### Phase 2 -- Saving

1. `ContentTableDocuments` over master and drafts; Ctrl+S and its routing; the trial build of master alone.
2. Publish, Hold as draft, New, Duplicate, Delete; row and tab markers.
3. The session rebuild on a worker, `ReplaceFactory<T>`, tabs and previews reading the workspace (Decision 11).
4. The document reference walk joined with the catalog index (Decision 13); Dependents and Delete ask it.
5. The quit prompt (Decision 14) and the Play prompt.

**Tests:** saving an unchanged table is byte-identical; one changed field is one changed line; a record that doesn't
decode is in Drafts as written and reopens with its errors; a held record stays in Drafts though it passes; a record
naming a draft-only aura goes to Drafts and its Error List line names the aura; a record the trial build fails goes to
Drafts while the others in the same save reach master; Publish of a failing draft changes no file; Delete of a
referenced master record is refused; a rebuild keeps the selection, the unsaved edits of other records and the undo log;
a record whose only problem is one field-level error goes to Drafts; a new record is written at its Guid's place.

**Check:**
- Change the Wand of Fireball's damage, Ctrl+S: expect `git diff` to show one line of `Items.jsonl`, the `*` gone, and
  the status line saying where it went.
- Press Play: expect the wand to do the new damage in the game.
- Clear a potion's name, Ctrl+S: expect it in `Drafts/Items.jsonl`, the row marked Draft with an error badge, master
  unchanged, and the game still showing the old potion.
- Fix the name, Ctrl+S: expect the draft line gone and master's line changed.
- Duplicate the Health Potion, Ctrl+S: expect a held draft; Play and expect no second potion in the game. Publish it,
  Play: expect it in Admin Mode's item list.
- New, then Ctrl+S at once: expect a draft with its required fields listed in the Error List.
- No shipped item names another item, so make the dependency: New, name it "Venom Crate", give it Set contents holding
  the Draught of Venom, Publish. Then Delete the Draught of Venom: expect it refused with the crate in Dependents. Keep
  the crate for Phase 3.
- Edit an item, press Play: expect the Save and Play prompt. Edit an item, close the window: expect the quit prompt (or
  Decision 14's fallback, and the status line on the next start).
- Save twice in quick succession while "Rebuilding" shows: expect both saved and one final session.

### Phase 3 -- Disabling, and the template check

1. Disable/Enable, the Disabled and Replacement section, the dialog with its replacement picker, Cascade as one step,
   code-held dependents (Decision 12).
2. Cycle refusal at the field; the error for published content naming a disabled record with no replacement.
3. `ContentReferenceIndex` gains blueprint `UsesActions`/`UsesItems`.
4. Wrap-up: IMPLEMENTATION-NOTES "Workshop content editing" with an "adding an editing tab" list; the Controls tab kept
   or removed; "Workshop: edit Actions/Auras/map areas/sprites" in TODO.md re-pointed at what was built.

**Tests:** disable with no dependents writes `off` and nothing else; Enable restores the line byte-for-byte; Cascade
over a three-deep chain disables all of it and one undo restores all of it; a replacement chain resolves to its end in
a built session; a cycle is refused at the field and as a file failure; a draft dependent neither blocks nor is
cascaded; a code-held dependent disables Cascade; undoing a step that changed records of three tables stays on the open
tab when it shows one of them and otherwise opens the origin's, and the status line counts all of them.

**Check:**
- Disable an item nothing names: expect it greyed and struck through, one changed line after Ctrl+S, and gone from the
  game. Enable it: expect `git diff` empty.
- Disable the Health Potion: expect the dialog listing what holds it, the blueprints marked "code", and Cascade
  disabled with that reason. Cancel: expect nothing changed.
- Disable it again with Confirm With Replacement, pick the Regular Mana Potion, save, Play: expect goblins and the
  player's kit holding Mana Potions where Health Potions were.
- Disable the Regular Mana Potion and set its replacement to the Health Potion: expect it refused at the field as a
  cycle.
- Disable the Draught of Venom (held by Phase 2's Venom Crate) with Confirm With Cascade: expect the crate disabled too;
  Ctrl+Z once and expect both back. Then delete the crate.
- **Template check:** in a scratch branch of the work, give the Auras tab a `ContentTableDescription<AuraDefinition>`
  and nothing else: expect its inspector, save and disable to work. Expect that to be one file. Don't keep it -- it is
  "Workshop: edit Auras", done properly later.

## Order against other plans

**Recommended list: NPC behavior composition, then this, then generated component property access, with
multithreading's remaining phases last.**

| Plan | Against this one |
|---|---|
| `PLAN-npc-behavior-composition.md` | **Before.** It is in flight, and its Phase 1 and this plan's Phase 0 edit the same lines: Phase 1 gives `ContentField.Tag`/`TagSet` a domain and touches every codec's tag fields; Phase 0 here gives every `ContentField` a default and touches every codec's schema. Doing both at once is two sweeps of ~45 codecs merged by hand. Its Phase 1 also supplies what the tag editor needs (domains to filter by, descriptions for tooltips). The hard dependency is only that Phase 1: its Phases 2-6 touch no Workshop file and no codec until Phase 6's one new key (`StatModifierGrant.FixedDuration`), which the generated inspector picks up with no code. So this plan can run between NPC phases if a break from it is wanted, but not before its Phase 1. After it, the Dread Idol and Dread Aura are the first records worth authoring in the tab rather than by hand |
| `PLAN-generated-component-property-access.md` | **After this.** The two share one thing, the per-kind field editors (its decision 6, its Phase 3 step 3). Content goes first because its descriptions exist today, enforced and tested, with the richer set of kinds (lists, polymorphic slots, references, tag sets); component tables don't exist until that plan's Phase 1, and its Phase 0 may still move its own decisions. Built here behind `FieldEditorDescription` (Decision 9), the editors are waiting when its Phase 3 arrives, which then adds only what a component needs beyond content (flags, nested structs, frame deadlines). Nothing else overlaps: it edits entities in a running game through pools, this edits definitions in files, and its decision 8 already says so. If it were to go first instead, its Phase 3 would build the same UIEngine rows and this plan's Phase 1 step 1 would shrink to the content-only kinds -- no rework either way, which is the point of Decision 9 |
| `PLAN-multithreading.md` | **Independent.** It forks read-only sections inside ECS systems; this builds whole sessions on a background task, which shares nothing with a frame and uses no `WorkerGroup`. Its Phase 0 steps 2-3 can run any time; its Phase 1 wants NPC Phase 4 done; its Phases 3-4 wait on measured triggers. None of that waits on, or is waited on by, this plan |

Other items this one unblocks or touches:
- **"Workshop: edit Actions", "edit Auras"** follow at once, each a description and its custom editors. **"Edit map
  areas"** and **"edit sprites"** reuse the workspace, log, save routing and shell contract but bring their own
  documents (a file per area; the sprite manifest) and their own drafts answer, as their entries already say.
- **"Workshop Debug tab: launch options"** (planned) is separate; Decision 8's argument is passed beside its arguments.
- **"Main Menu and Mod menu"**, **"Content-only mods"**: editing mod and save layers stays out of scope; nothing here
  assumes master is the only editable layer beyond the two file paths in `ContentTableDocuments`.
- **"Data tables for content"**: a table with a codec gets this editor for free. Worth saying in that entry.
- **"Common game elements in Engine"**: if the action/item/aura codecs move to Engine, `ContentTableDescription`s move
  with them; nothing here blocks it.
- **"Text input undo/redo"** (Presentation, Low) stays separate: Ctrl+Z here undoes whole committed edits, never
  keystrokes inside a field.

## Not built by this plan

- Editing mod or save manifests; drafts for mods.
- Hot reload into a running game (the IDE plan's Decision 15; definitions are fixed per session).
- Paths on codec failures (`ContentFormatException` carrying where it failed). Schema validation covers what has a path;
  revisit only if record-level messages prove hard to act on.
- Multi-select editing, copy and paste of fields or entries between records, and search across field values.
- An Output tab and error codes for the bottom panel (their own TODO entries).

## Bookkeeping

- TODO.md's "Workshop: edit Items" points here and stays until Phase 3 lands, then is deleted; the "Found while
  planning" and "Worth splitting" notes are answered by Phase 0 and the phase split.
- `PLAN-ide-shell-and-actions-items-tab.md`'s "Phases 5-7" note gains this plan's name, and its Decision 19 a pointer to
  Decision 13 here (the index is not purely schema-driven) and its "per-tab log" one to Decision 6.
- `PLAN-generated-component-property-access.md` "Order against other plans" names this plan and `UIEngine.UI.Fields`.
- CLAUDE.md after Phase 2: the Workshop paragraph (tabs edit; `ContentWorkspace`; Play reads the source master), the
  UIEngine layer bullet (`UIEngine.UI.Fields`), and "Content as data" (`ContentField.Default`, `hold`).
- `ContentManifestExporter` follows Decision 3 from Phase 0 (Guid order, disabled lines in place); once Phase 3 lands it
  can be retired, since the Workshop then writes master.
- Done 2026-11-12: TODO.md gained "Terrain as content" and "Blueprints as content" (Game, Medium), each naming
  `ICodeHeldReferences` as what it removes.
