# Workshop: compare a draft with master

Plans the TODO.md entry "Workshop field compare: a record against its origin" for the case that exists today: a record
the Workshop edits (a draft, or unsaved edits) against its published line in master. Mods are shown, never edited, so
"origin" is master for every editable record; the diff is still built layer-agnostic, because the mods plan's "every
layer's version of a record" needs the same one.

**Status:** complete -- Phases 0-4 built and checked in the Workshop by 2026-11-15. Written 2026-11-15 from research
and a read of the code. Decisions 1-9 were confirmed the same day.
Decision 10 (the three-way preview window) was confirmed that day too. The browser filter and areas are deferred
(see under the decisions). Phase 0 built 2026-11-15 (`ContentRecordDiff`, `ContentRecordDiffTests`). Phase 1 built and
checked in the Workshop 2026-11-15 (`ContentRecordRevert`, the inspector's markers, Revert All; removed rows are
label-only, `FieldEditorDescription.IsLabelOnly`; row labels are cut to their room with the whole label in the tooltip).
Phase 2 built and checked in the Workshop 2026-11-15: the bottom panel's Changes (`WorkshopChanges`, columns
Field, Change, Master, Unsaved; Revert on a line's right-click), a "Changes only" checkbox right-aligned on the command
bar (`RecordViewOption`), values read through `ContentDifferenceText`, and "(changed from master)" on an Error List line
at a changed field. Field rows have no Unset button any more (2026-11-15): revert, Delete and editing the value cover it.
Phase 3 built and checked in the Workshop 2026-11-15: Decision 7's list is a bottom-panel tab of its own, To
Publish (`WorkshopPendingRecord`, `IWorkshopEditingTab.PendingPublish`), since an editing tab always has a record
selected; Publish refuses a record whose master line moved on disk (`ContentSaver.RefuseMovedMaster`), and Reload
Master (`ContentEditingTable.ReloadMaster`) takes the line found as the baseline.
Phase 4 built and checked in the Workshop 2026-11-15: Compare Previews (`ContentEditingTab.OpenComparePreviews`),
a DynamicHud window opened by the Compare command and the preview's "Compare" title button, refilled on every
selection and edit, reopened when the tab's windows are rebuilt. Changed in testing (2026-11-15): the third column is
"Unsaved", not "Now" (and so is Changes' column); a version the same as one to its left reads "Same as Master." /
"Same as Draft." in place of its preview; Draft and Unsaved are coloured better/worse against master through Item
Details' own comparison (`ShowComparedDefinition`), master never; the window fits its contents (WrapContent, so not
resizable); the Compare title button is content-button size, and title buttons are centred in their bar.

## What other editors do, and what their users say

Direct user reviews of this one feature are thin: most of what follows is vendor documentation, issue trackers and
forum threads, and it says so where a claim rests on a single or a vendor source.

| Tool | How it compares | What users report |
|---|---|---|
| **Unity** prefab overrides | A changed property is bold with a blue bar at the row's left edge. An Overrides dropdown on the root lists what changed; clicking an entry opens a side-by-side of asset and instance with Revert and Apply. Per-property revert/apply is on the row's context menu; Revert All / Apply All on the dropdown. | Forum threads (2018-2024): layout-driven values show as overrides nobody made and bury the real ones; the dropdown exists only on the root, so a nested prefab has no way in; which target "Apply" writes to is unclear; "default overrides" are bold but get no bar and are left out of the list, so the two markers disagree. |
| **Blender** library overrides | An overridden property gets a teal outline; Reset / Remove Override on the context menu. | Issue #95064: the icon beside an overridden property looks like a reset button and does something else. A marker that looks clickable must be the control it looks like. |
| **Unreal** | Blueprints get a visual diff; DataTables a row diff; "Diff Against Depot" needs a connected source control provider. Custom data assets fall through to an external text diff of an export (one vendor blog; unverified). No merge for DataTables. | A CSV re-import is a whole overwrite, "the second import simply wins"; a Dolt write-up says several developers gave the missing diff/merge as their reason to avoid DataTables. |
| **xEdit** (Bethesda plugins) | One column per plugin holding the record, load order left to right, the rightmost wins; rows are fields; a filter hides rows without conflict. Colours classify the kind of conflict. | The most-used structured compare in modding, and its column model is praised; every beginner guide has to say "red does not mean bad, green does not mean good" -- colours that look like verdicts are misread. The conflict filters are what people call indispensable. |
| **Contentful** | Pick a version in the sidebar, "Compare with current version", a "Show only differences" checkbox. | Rich text was shown as changed without showing what changed (rich-text issue #785), fixed later by a separate app. Linked entries are compared by reference only, never their content. |
| **Contentstack** | Compare drafts and versions before save, publish or discard; changes grouped under their field, covering text, references, files and block structure. | (Docs only.) Placed at the decision: compare is offered where the user chooses to publish. |
| **Sanity** | A side-by-side of draft and published with differences highlighted, and since 2025 opt-in inline diffs at each field against the upstream version; a Review Changes pane with revert per change. | Changelog: after a publish the pane showed every field as erased, because the baseline was wrong. The baseline's hand-over at publish is where this breaks. |
| **Strapi 5** | Draft and Published tabs; Content History shows snapshots. | Its own guides say there is no computed diff or field highlight: the user compares two tabs by eye. |
| **WordPress** revisions | A slider over versions, two panes. | Trac tickets: testers didn't know what the slider was for, and which side was older was unclear. |

What this plan takes from them:

1. **Mark the change where the value is edited, and roll it up** (Unity, Blender, Sanity). A separate compare screen is
   the second way in, not the only one.
2. **One marker, one rule.** No field is changed-but-unmarked (Unity's default overrides).
3. **Only real changes are changes.** A value written out that equals its default, or keys in another order, is not a
   difference (Unity's layout noise). A draft is saved "as edited" while master is the codec's line, so this is a live
   risk here.
4. **Every kind of value shows what changed, never only that it changed** (Contentful's rich text): nested objects,
   lists and polymorphic entries included.
5. **Changed-only is the default view** (Contentful, xEdit's filters).
6. **Columns in a fixed order, each named** (xEdit; WordPress's unlabelled sides). Master left, the draft right.
7. **No red and green for old and new** (xEdit). Changed, Added, Removed are words; one accent colour means "differs
   from master".
8. **Revert at the field and for the whole record**, each through the same path as its menu option (Unity; and this
   repo's one-path rule).
9. **Show what a publish will write before it writes** (Contentstack, Unreal's changelists).
10. **Never overwrite a master that moved without saying so** (Unreal's "second import wins").

## What the code says today

- `ContentRecordDocument<T>` already holds all three versions: `MasterLine` (master's line as saved, null when master
  doesn't hold it), `DraftJson` (Drafts' copy as saved) and `Record` (`EditableContentRecord`, the JSON tree being
  edited; `OriginalJson` is what it was opened or last saved as). Nothing reads `MasterLine` for comparison.
- The inspector (`Cadence/Workshop/Editing/ContentInspector`) builds rows per schema field by `ContentPath`, already
  colours rows for problems (`ApplyProblems`), shows an omitted field's default dimmed with a reset button on a set
  one, and has `FocusField(path)`.
- Edits are `ContentInspectorEdit`s applied by `ContentWorkspace` as one undo step each; record commands are
  `ContentEditingTab.CommandsFor` (Publish, Save as Draft, Disable, Duplicate, Delete).
- The bottom panel holds the Error List and Dependents; a problem carries `TabTitle`, `RecordId` and a field path, and
  navigates through `RequestOpenRecord`.
- A publish reads master from disk once (`ContentSaving.ReadMasterFiles`) and replaces the record's line in place. It
  does not check that the line on disk is still the `MasterLine` the document was opened with.
- The browser row already marks a draft (`DraftMarker`), unsaved edits (`ChangedMarker`) and a pending deletion.

## Decisions

Confirmed 2026-11-15.

1. **The baseline is master's line, always.** What is compared with it is the record as it is now (which is the draft
   as saved when nothing is unsaved). "Since my last save" is not a second baseline: undo already answers it, and two
   baselines is Unity's apply-target confusion. The baseline is named on screen ("Master") wherever a value from it is
   shown.
2. **One diff, in the engine, over JSON trees.** `ContentRecordDiff` in `Cadence/Workshop/Editing`: two JSON objects
   and the `ContentTypeRegistry` in, a list of `ContentFieldDifference(ContentPath, kind, baseline value, current
   value)` out. It names no layer, so the mods plan's layer-against-layer view calls it unchanged. Rules:
   - a missing key equals its schema default; key order never matters; numbers compare by value;
   - a key no schema declares, or a value of the wrong kind, compares as raw JSON;
   - a polymorphic value whose `$` changed is one difference (the whole value replaced), not one per key;
   - the envelope (`off`, `rep`) is compared too: Disabled is a change from master.
3. **Lists are aligned by content, then by position.** Elements deep-equal on both sides are matched first (longest
   common subsequence), giving Added, Removed and Moved; what is left unmatched between two matched elements pairs up
   in order as one element Changed, and the diff goes inside it -- in a polymorphic list only two of the same type
   (found in testing, 2026-11-15: a DirectHeal replaced by a DirectDamage read as one entry changed, whose revert
   turned the damage back into a heal; it is a removal and an addition). Effect lists have no stable key, so this is the best available;
   it is the rule most likely to need tuning in testing.
4. **In the inspector: one accent bar at the left edge of a changed row**, and of every heading above it, with the
   count on the heading when it is closed. The bar is not a button. The row's tooltip adds "Master: <value>" (a
   reference by name, not Guid). A row for something master doesn't have reads Added; a removed list element or key
   gets a dimmed, read-only row in its place reading Removed, so nothing that changed is invisible.
5. **Revert is on the row and on the record.** "Revert to master" on a changed row's context menu and as a button
   beside the existing reset-to-default; "Revert all to master" among the record's commands. Each is one undo step
   through `ContentWorkspace`, and the option's Enabled is that path's own answer (nothing to revert; no master line).
   Reverting a Removed row puts it back; reverting an Added one takes it out.
6. **The compare view is a "Changes" tab in the bottom panel, not a new column or dialog.** Three columns: Field,
   Master, Draft; one row per difference of the selected record; selecting a row focuses the field in the inspector;
   Revert on the row. The tab's screen is full at 1600 already (edit-Items Decision 1), and the inspector with its
   markers is the "all fields" half of the TODO's "changed only or all". A "Changed only" toggle on the inspector
   hides unchanged rows for a long record. Alternative: an Edit | Compare switch that turns the inspector into two
   value columns -- closer to xEdit, but it doubles the inspector's row types.
7. **With no single record in question, Changes lists what a publish would write**: every record changed, new,
   deleted or disabled against master, with its count of changed fields; selecting one opens it. No confirmation
   dialog is added to Publish.
8. **A new record and a deleted one say what they are** rather than listing every field: "New: not in master" and
   "Deleted: master's line will be removed". A record that doesn't decode still compares, since the diff reads JSON.
9. **Master moved on disk.** A publish that finds the record's line on disk differing from the `MasterLine` it was
   opened with refuses that record ("master changed since it was opened"), keeps it in Drafts, and offers to reload
   the baseline; the compare then shows the new master. Noticing it earlier (watching the folder) is left out until
   it is wanted.

10. **The previews are compared three across, in a window of their own** (confirmed 2026-11-15). The tab has no room for it and a switch would mean flipping back and forth, so "Compare
    Previews" opens a floating window holding three of the tab's own preview (Item Details, Action Details, Aura
    Details) side by side, left to right: **Master** (the published line), **Draft** (Drafts' copy as saved) and
    **Now** (the record as edited, unsaved changes included).
    - It is an ordinary window, not a `ModalDialog` and not light-dismissed: it stays open while the inspector is
      edited, and Now follows every edit. It moves and resizes, opens at three preview widths (3 x 460 fits a 1600
      window) and scrolls sideways when narrower. Escape or its title button closes it.
    - It follows the browser's selection. Opened from the record's commands and from a button on the preview's
      title bar -- one path, both ways in.
    - The three columns are always there, in that order, each headed with its name. A version that doesn't exist or
      doesn't decode says so in its column ("Not in master", "No saved draft", "Doesn't decode: ..."); one equal to
      the column on its left is still drawn, with "same as Master" / "same as Draft" in its heading. Nothing moves
      or disappears as the record is edited.
    - Each column is the game's own window showing a decoded definition (`ContentEditingTab.ShowDefinition`), so it
      is generic over `TPreview` and needs nothing per tab. The windows are session-bound, so it is rebuilt on
      `SessionReplaced` like the tab's preview.
    - This does not reopen Decision 1: the bars, Revert and the Changes tab still compare with master alone. The
      Draft column is a preview only.
    - Not in this plan: marking the changed lines inside a preview. The field diff is the Changes tab's.

Deferred (2026-11-15):
- **A "differs from master" browser filter** -- built 2026-11-15 as the Show only choice "Differs from master"
  (`PLAN-workshop-browser-filters.md`), reading the same state as the To Publish list (`ContentRecordState`).
- **Areas** (one JSON file per record, rows of symbols) need a grid diff of their own. Deferred until "Workshop: edit
  map areas" makes that tab edit at all.

## Phases

Each phase stops for testing in the Workshop before the next starts.

### Phase 0: the diff

`ContentRecordDiff` and `ContentFieldDifference` in `Cadence/Workshop/Editing`, with the rules of Decisions 2 and 3.
Tests in `Workshop.Tests` over real master records: identical lines give nothing; an explicit default against an
omitted key gives nothing; reordered keys give nothing; a scalar, a nested object, a changed `$`, an unknown key, a
list add / remove / move / in-place change each give exactly the expected differences.

Check: tests only -- nothing to see yet.

### Phase 1: markers and revert in the inspector

`ContentInspector.Show`/`Refresh` take the differences beside the problems; the bar, heading roll-up, tooltip,
Added and Removed rows; "Revert to master" on a row and "Revert all to master" on the record. The tab computes the
diff once per `Record.Version`, never per frame.

Checks:
- Change a potion's name and heal amount: both rows and the headings above them show the bar; hover either and read
  master's value.
- Type the old name back: the bar goes without reverting.
- Set a field to the value its default already had: no bar.
- Revert one row from its context menu, then Ctrl+Z: the edit returns, bar and all.
- Add an effect entry, remove another, move a third: one Added, one Removed (dimmed, read-only), one Moved.
- "Revert all to master": the record reads as published, the draft marker stays until saved, one Ctrl+Z undoes it.
- New record: no bars, and Revert all is disabled saying it isn't in master.
- Save as Draft, close and reopen the Workshop: the same bars are there.
- Publish: every bar goes.

### Phase 2: the Changes tab and Changed only

The bottom panel's Changes tab for the selected record; row selection focuses the field; the inspector's "Changed
only" toggle; the Error List's entry for a problem at a changed field says so.

Checks:
- With three fields changed, Changes lists three rows, master's value left, the draft's right, references by name.
- Click a row inside a collapsed section: the section opens and the field is focused.
- Revert from a Changes row: the row leaves the list and the bar leaves the inspector.
- Changed only on: only changed rows and their headings; edit another field and it stays visible until toggled.
- Select a published, unedited record: Changes says it is as published.

### Phase 3: what a publish will write, and a master that moved

Changes with no record selected lists every record differing from master (Decision 7); new and deleted records read
as Decision 8; the publish-time check of Decision 9.

Checks:
- Edit two items, create one, delete one: four rows, each of the right kind; selecting one opens it in its tab.
- Publish all: the list is empty.
- With an edit unsaved, change the same record's line in `Items.jsonl` by hand, then Publish: that record is
  refused with the reason, stays a draft, and after reloading the baseline the compare shows the hand edit as master.

### Phase 4: Compare Previews

The floating window of Decision 10: three previews (Master, Draft, Now), opened from the record's commands and the
preview's title bar. Master and Draft are decoded from `MasterLine` and `DraftJson` through the table's own decode,
once per change of either; Now is the document's `LastGood`, as the tab's preview is.

Checks:
- Edit a potion's heal, Save as Draft, edit it again: three columns, three different heal values, each headed.
- Keep typing in the inspector with the window open: Now follows, Master and Draft don't move.
- A record with no draft: the Draft column says so; a new record: the Master column says it isn't in master.
- A published, unedited record: all three drawn, Draft and Now headed as the same as the one beside them.
- Break the record so it doesn't decode: Now shows it as it last did, labelled as the tab's preview is.
- Select another record in the browser: the window follows. Publish: all three agree.
- Drag the window off the inspector, resize it narrower than three previews: it scrolls sideways, nothing overlaps.
- Works unchanged on the Actions and Auras tabs once they edit.

## Sources

- Unity: [Prefab instance overrides](https://docs.unity3d.com/Manual/PrefabInstanceOverrides.html),
  [nested override threads](https://forum.unity.com/threads/could-someone-clarify-how-prefab-overrides-work-with-nesting.598273/),
  [nested prefab annoyance](https://forum.unity.com/threads/nested-prefab-annoyance.625297/),
  [layout false overrides](https://forum.unity.com/threads/prefab-overrides-and-ui-recttransforms-in-layouts.646684/),
  [prefab workflow enhancement](https://discussions.unity.com/t/prefab-workflow-enhancement/1500762)
- Blender: [Library overrides](https://docs.blender.org/manual/en/4.2/files/linked_libraries/library_overrides.html),
  [issue #95064](https://projects.blender.org/blender/blender/issues/95064)
- Unreal: [UE Diff Tool](https://dev.epicgames.com/documentation/en-us/unreal-engine/ue-diff-tool-in-unreal-engine),
  [Diffing Unreal assets](https://www.unrealengine.com/en-US/blog/diffing-unreal-assets),
  [Dolt plugin announcement](https://www.dolthub.com/blog/2024-03-11-dolt-plus-unreal/),
  [DataTable Merge devlog](https://csaf.itch.io/datatable-merge/devlog/1644355/datatable-merge-100-is-out),
  [Data-driven design in Unreal](https://unreal-garden.com/tutorials/data-driven-design/),
  [diff without source control (vendor blog)](https://www.strayspark.studio/blog/unreal-diff-assets-without-source-control)
- xEdit: [Conflict detection and resolution](https://tes5edit.github.io/docs/5-conflict-detection-and-resolution.html),
  [Unofficial xEdit guide](https://stepmodifications.org/wiki/Guide:XEdit),
  [colours and what to really look for](https://stepmodifications.org/forum/topic/7356-tes5edit-colors-what-to-really-look-for-in-conflict-resolution/),
  [Basics of xEdit](https://moddinglinked.com/xedit.html)
- Contentful: [Versions](https://www.contentful.com/help/content-and-entries/versions/),
  [Versioning FAQ](https://www.contentful.com/help/faq/versioning/),
  [rich-text issue #785](https://github.com/contentful/rich-text/issues/785)
- Contentstack: [Compare drafts and versions](https://www.contentstack.com/docs/content-managers/author-content/compare-drafts-and-versions)
- Sanity: [Compare document versions](https://www.sanity.io/docs/studio/compare-document-versions),
  [inline field diffs changelog](https://www.sanity.io/changelog/5b62ace6-d0bf-44da-b862-fb17e04dfd2e),
  [Review Changes fixes](https://www.sanity.io/changelog/447d6ab6-ac0f-40b7-be13-8a4e3bcedd7a)
- Strapi: [Draft & Publish and Content History in Strapi 5](https://strapi.io/blog/draft-publish-content-history-strapi-5),
  [Content versioning guide](https://strapi.io/blog/content-versioning-strapi-setup)
- WordPress: [Trac #23899](https://core.trac.wordpress.org/ticket/23899), [Trac #23935](https://core.trac.wordpress.org/ticket/23935)
