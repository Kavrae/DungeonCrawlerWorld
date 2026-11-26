# Workshop: edit Actions -- the second editing tab on the Items machinery

Plans the TODO.md entry "Workshop: edit Actions" (Engine, High). The machinery is `PLAN-workshop-edit-items.md`'s, built
and checked 2026-11-14, with compare (2026-11-15), filters (2026-11-15) and row symbols (2026-11-17) on top; none of it
is reopened. IMPLEMENTATION-NOTES "Workshop content editing" has the list for adding an editing tab, and this plan
follows it. Written 2026-11-25 from a read of the code.

**Status:** proposed, not confirmed. Decisions 1-7 are recommendations awaiting confirmation.

**The measure:** the Items plan's last check said a second table should be one description, not a second copy. The
Auras trial on 2026-11-14 was about forty lines. Actions should land near that. Anything bigger is generic machinery
that turned out to be Items-shaped, and it gets fixed in the machinery, not worked around in the tab.

## What the code says today

### 1. Already in place, at no cost to this plan

- **Every field an action has, an item has too.** `ActionDefinition` and `ItemDefinition` share
  `ActivatableContent.LeadingFields`/`TrailingFields`: tags, effects, toggle (`tog`), activation effects (`ae`). Both
  hold an `IActionActivator` whose targeted members carry `TargetingSpec` and `ActionTiming`. The only action-specific
  field is `core`. So the TODO's "custom pieces" -- a spell's mana (a `ManaDrain` in `ae`), timing and toggle spec --
  are already edited by the generated inspector on the Items tab, with the targeting shape preview among the rows.
  Decision 1 asks whether anything more is wanted.
- **Per-record checks exist:** `ContentChecks.ProblemsOf(ActionDefinition)` covers tags, toggle rules (a toggle action
  must be Self on the caster, and it may hold only revertible grants with no end), targeting rules and references.
- **The preview takes a definition:** `ActionDetailsWindow.Configure(definition, width, comparedAgainst)` shows a
  definition that isn't in the catalog, and it colours a comparison. It is already registered as a session-bound
  factory in `BuilderTabs.RegisterElementFactories`.
- **Browser sections and the Timing filter exist** (`ActionBrowserSections`, `ActionsTab.FilterGroups`). They only need
  to move from definitions to documents.
- **The workspace already expects an Actions table:** `ContentWorkspace.AllAppearances` reads the session's actions for
  the same-look warning "while no table holds them". `SessionContentTables` and `ContentLinks` know the Actions table
  and tab.

### 2. What the code shows that the TODO entry didn't know

| Found | Consequence |
|---|---|
| A scroll uses only its spell's **Guid**: its own targeting, timing and effects, `ScrollActivator.SpellId` for the "Casts: Heal" line in Item Details (`ItemComparisonStatExtraction`) and for what mastery teaches (`ScrollMasteryEffects`) | The TODO's check "change an action's range and see it in a scroll that casts it" can't pass. Heal's range never reaches the Scroll of Healing. What reaches it is the spell's **name**, and the spell itself once the scroll is mastered. The checks below use those |
| `PlayerKit.UsesActions` lists all 14 shipped actions, and the races grant Quick Attack, Power Attack, Dodge, Heal and Magic Missile | Every shipped action has a code-held dependent. **Delete and Cascade are refused for every shipped action**, the way the Health Potion was for items. The cascade check needs made-up content, as the Items plan's Venom Crate was |
| `ContentReferenceIndex` reads a blueprint's `UsesActions` but **not its `Actions` grants** (`ActionGrant`, including `ActionGrant.Derived` recipes) | The Goblin, Fairy, Ghost and Human grants of an action are missing from Used By and from the disable dialog. Only PlayerKit's listing hides the gap today. An action granted only by a race would be deletable while the race still names it, and the trial build would then fail on the publish (Phase 0) |
| Presentation names `BuiltInActionIds.Dodge` (`ActionTargetingController`, `MapGameOverlay`) to recognise Dodge's targeting | This is a code reference that no blueprint declares. Every race grants Dodge, so it is code-held anyway. But Dodge is identified by its Guid where it should be identified by what it does (Decision 4) |
| An action grant is keyed by the **granted** Guid (`ActionGrantEffects.Grant` merges `ActionInstanceComponent(actionId, ...)`). `Catalog.Get` follows a disabled record's redirect | Disable Quick Attack with Power Attack as its replacement, and a Human gets two instances that both play Power Attack, with separate cooldowns and two hotbar entries. A derived grant (Goblin's Quick Attack) derives from the replacement, keeping the old id. Confirm With Replacement for actions needs a rule (Decision 5) |
| A new action has a **required** activator (`ac` is `isRequired` for actions; optional for items). `ItemsTab.NewRecordFields` and `PlaceholderJsonOf` write a name and glyph only | A new action, and the placeholder for an action line that doesn't decode, need an activator to decode (Decision 2) |
| `GameInspectorSource.FieldsInDisplayOrder` moves the activator before the tags for `ItemDefinition` only | On an action, the "Implied by the activator" note under Tags would sit above the activator it describes. Both types should share the rule |
| `ContentSession` maps a load problem's file to a tab only for areas and `Items.jsonl` | A broken line in `Actions.jsonl` shows in the Error List, but clicking it opens nothing |
| `ActionsTab` is a read-only `RecordBrowserTab<ActionDefinition, ...>` with its own Uses/Used By from the index | It is replaced, not extended. `Workshop.Tests/Browsing/ActionBrowserSectionsTests` stays as written; `ContentLinksTests` gets checked |

## Decisions

Proposed 2026-11-25. Not yet confirmed.

1. **No custom editors for mana, timing or the toggle spec. Add one note: the cost under Activation effects.** The
   generated rows already edit all three on items, and they will on actions too. What the inspector doesn't show is the
   total an entry list costs. `GameInspectorSource.NoteFor` gains a line under `ae` for actions *and* items: "Costs 2
   mana, 1 health", from the decoded definition through `ActivationCosts` (the hotbar badge's own totals), the way the
   implied-tags note is shown now. *Alternative:* a "Mana cost" number row that writes a `ManaDrain` into `ae`. Rejected:
   it is a second way to edit one value, and a cost is just an activation effect (CLAUDE.md, "A cost is an activation
   effect").
2. **A new action starts as a Direct activator on the caster: Self, anchored on the caster, timing left at its
   defaults** (`{"$":"Direct","tg":{"sh":"Self","an":"Caster"},"tm":{}}`). That decodes and passes every check at once,
   so a new action is never "not filled in yet" on its first save. The undecodable-line placeholder writes the same
   activator. `ItemsTab.PlaceholderJsonOf` becomes `ActivatablePlaceholder.JsonOf(record, kindName, activator?)` in
   `Builders/Editing`, beside `ActivatableAppearance`, shared by both tabs.
3. **A preview reads every other table as published.** Item Details' "Casts: Heal" names the session's Heal, so
   renaming Heal shows on the Scroll of Healing only after a publish rebuilds the session. The Action Details of an
   action shows the action as edited. That is the same rule the Items tab already has for auras. *Alternative:* route
   previews' catalog lookups through the workspace (`TryResolve`). That would give a draft view across tables, but the
   preview windows are Presentation's and take an `ActionCatalog`. It is worth doing only when there is a third case.
4. **The index reads a blueprint's action grants. Presentation's Dodge check is a TODO, not a code-held entry.**
   `ContentReferenceIndex` adds each blueprint's own `Actions` grants beside `UsesActions`: a plain grant as "grants it,
   in code", a derived one as "derives its own from it, in code" (new `ContentReferenceKind`s, with text in
   `ContentLinks`). Presentation naming Dodge by Guid is filed as a TODO.md entry, "Recognise Dodge by its entry, not
   its Guid": Presentation should ask whether the armed action holds a `DodgeActivation` (through a view). It isn't
   built here, because every race already grants Dodge, so Delete and Cascade are refused for it anyway.
5. **A grant of a disabled action resolves to its replacement when it is granted.** `ActionGrantEffects.Grant` and the
   base of `ActionGrant.Derived` key the instance by the id the catalog resolves (the end of the replacement chain). So
   two grants that resolve to one action merge into one instance, and a derived grant derives from the replacement
   under the replacement's id. This matches what disabling means for content ("every use of the Guid follows the
   replacement", CLAUDE.md "Content as data"). It is a Game change: Phase 0 pins it with a test before the tab can make
   it reachable. *Alternative:* leave grants keyed by the old Guid and accept duplicates. Rejected, because the disable
   dialog would then offer a replacement that hands players two copies of an action.
6. **Browser filters: Type (the sections' headings), Timing, and Core.** Type and Timing are any-of value groups, as
   on Items and today's Actions tab. Core is a value group of its own: "Core" and "Not core". It is read from the
   definition, because `core` decides the Actions window's Core tab in the game.
7. **The tab strip doesn't change:** Items, Actions, Auras, Map. `--tab=Actions` opens it alone.

Settled by the code, with no choice in it:
- Mod actions are read-only, and a mod-replaced master action is edited as master's (the table's existing rule).
  ExampleMod ships no actions today.
- Disable, Enable, Replacement and Cascade are the workspace's (`DisableRefusal`, `PlanDisable`). Nothing about them is
  action-specific once the index sees the grants (Decision 4).
- A scroll naming a draft-only action stays in Drafts on publish. That is the existing "published content never names a
  draft" rule, and a `Reference` field to the Actions table already takes part in the reference walk.

## The design

| Piece | Where | What |
|---|---|---|
| `ContentEditingTable<ActionDefinition>` | `BuilderTabs.Create` | Over `ActionCatalog.Definitions`: `GameContent.ActionsTable`, its name, `Origins.ModOf`, `ContentChecks.ProblemsOf`, `ActivatableAppearance`, `ActivatablePlaceholder` |
| `ActionsTab : ContentEditingTab<ActionDefinition, ActionDetailsWindow>` | `Builders/Actions` | Title, "action", `NameKey`, Decision 2's starting fields, `ActionBrowserSections`, Decision 6's filter groups, `LookOf`, `ShowDefinition`/`ShowComparedDefinition` through `Configure` |
| `ActivatablePlaceholder` | `Builders/Editing` | Item and action placeholders, one method (Decision 2) |
| `GameInspectorSource` | `Builders/Editing` | `FieldsInDisplayOrder` for both activatable types; the cost note (Decision 1) |
| `ContentWorkspace.AllAppearances` | `Builders/Editing` | The "no Actions table held" branch and `_sessionActionAppearances` go: the table is always held now |
| `ContentSession` | `Workshop/App` | `Actions.jsonl` problems open the Actions tab |
| `ContentReferenceIndex`, `ContentReferenceKind`, `ContentLinks` | `Builders/References` | Blueprint action grants, plain and derived (Decision 4) |
| `ActionGrantEffects`, `ActionGrant.Derived` | Game | Grants resolve through the catalog's redirect (Decision 5) |

Nothing in `Cadence/` should change. If something has to, it is a gap in the template. Fix it there and add it to the
IMPLEMENTATION-NOTES list.

## Phases

Each phase ends with the Workshop running (and the game, where Game changed), then stops for testing before the next.

### Phase 0 -- References and grants (no new UI)

1. `ContentReferenceIndex` adds every blueprint's own `Actions` grants: `GrantsAction`, and `DerivesAction` for a
   `Derived` recipe's base. Their text goes in `ContentLinks`.
2. `ActionGrantEffects.Grant` and `ActionGrant.Derived` resolve the granted Guid through the catalog's redirect
   (Decision 5).
3. TODO.md gains "Recognise Dodge by its entry, not its Guid" (Presentation, Medium).

**Tests:** the index lists Goblin, Fairy, Ghost and Human under Quick Attack's Used By, with the derived ones marked
derived. A blueprint granting both an action and one disabled with that action as its replacement builds one instance
of it. A derived grant over a disabled base derives from the replacement. Every `BuiltInModulesTests` and determinism
test still passes, since no shipped action is disabled.

**Check:**
- Workshop, Actions tab (still read-only): select Quick Attack, expect Used By to list PlayerKit "builds with it", Human
  "grants it" and Goblin, Fairy, Ghost "derives its own from it".
- Game: start it, expect the player's Actions window and hotbar unchanged, and goblins still attacking.

### Phase 1 -- The Actions tab edits

1. `ActivatablePlaceholder`; `ActionsTab` on `ContentEditingTab`; the table in `BuilderTabs.Create`; the read-only tab
   removed.
2. `GameInspectorSource`: activator before tags for actions; the cost note under Activation effects (both types).
3. `ContentWorkspace.AllAppearances` loses the session-actions branch; `ContentSession` maps `Actions.jsonl` to the tab.
4. Filters: Type, Timing, Core (Decision 6).

**Tests** (`Workshop.Tests/Editing`, on the real Actions table): every shipped action opens as a master document and
writes back to its own line byte for byte. A new action decodes and has no problems. A placeholder for a broken line
decodes. One changed field publishes as one changed line of `Actions.jsonl`. A toggle action given a non-Self shape
goes to Drafts with the toggle rule's problem. A scroll naming a draft-only action stays in Drafts and names the action.
Deleting Heal is refused, naming PlayerKit. The cost note reads "Costs 2 mana" on Heal and nothing on Quick Attack.
Undoing a cross-table Cascade (Phase 2's content, made in the test) stays on the open tab when it shows either record.
The table opens exactly the 14 master records: a definition the session holds that no file does would open as an
unsaved new record, so the test names any that appears.

**Check (Workshop, Actions tab):**
- Select Heal: expect browser, inspector and Action Details side by side, the activator above Tags with "Implied by the
  activator: Magic, Spell" under them, and "Costs 2 mana" under Activation effects.
- Change Heal's `ManaDrain` amount to 3 and press Enter: expect Action Details and the cost note to read 3, and `*` on
  the row and the tab. Ctrl+Z: expect 2 and no `*`.
- Publish Heal at 3 mana: expect `git diff` to show one line of `Actions.jsonl`. Press Play and cast Heal: expect 3 mana
  spent.
- Rename Heal to "Mend" and publish. Expect the Items tab's Scroll of Healing to read "Casts: Mend" once "Rebuilding"
  clears (Decision 3), and the scroll's own targeting and amount unchanged. Revert both and publish: expect `git diff`
  empty.
- Lantern: change the targeting shape to Circle. Expect a check problem in the strip above the inspector and an Error
  List line. Publish: expect it in `Drafts/Actions.jsonl`, the row marked Draft with errors, and master unchanged. Revert
  All, then Save All: expect the draft gone.
- New: expect "New action" to decode at once, Self on the caster, as an Ability. Ctrl+S: expect a draft with no errors.
  Delete it.
- Filters: Type Spells, then Timing Delayed, then Core: expect only matching rows, with the chips under the search box.
- Hand-break a line of `Actions.jsonl` (an unknown key), restart the Workshop: expect an Error List line that opens
  the Actions tab at that record, shown as a placeholder. Revert the file.
- Compare on Heal with an unsaved change: expect Master and Unsaved side by side, Unsaved coloured against Master.
- Game: expect the Actions window, hotbar and Action Details unchanged from before Phase 1.

### Phase 2 -- Disabling across tables, and the wrap-up

No new code is expected beyond what the checks find. This phase proves the cross-table steps on content that can run
them, then does the bookkeeping.

**Check:**
- Disable Dodge: expect the dialog to list PlayerKit and every race under "Can't be disabled here", Cascade disabled
  with that reason. Cancel: expect nothing changed.
- Make the dependency: on the Actions tab, New "Test Bolt" (Spell, Single target, range 5, a `DirectDamage` of 3) and
  Publish. On the Items tab, New "Scroll of Test Bolt" with a Scroll activator whose spell is Test Bolt, and Publish.
  Expect the scroll's Uses to link Test Bolt, and Test Bolt's Used By to list the scroll.
- Delete Test Bolt: expect it refused, naming the scroll.
- Disable Test Bolt with Confirm With Cascade: expect both rows greyed, and the status line to read "1 action, 1 item".
  Ctrl+Z once, from the Items tab: expect both back, the Items tab staying open with the scroll selected. Ctrl+Y, then
  Ctrl+Z from the Actions tab: the same, staying there.
- Disable Quick Attack with Confirm With Replacement, picking Power Attack. Publish, then Play. Expect the player's
  Actions window to hold Power Attack once (Decision 5), and goblins to attack with their Power Attack damage. Enable
  Quick Attack and publish: expect `git diff` on `Actions.jsonl` empty.
- Delete the scroll, then Test Bolt, and publish: expect both tables as before the phase.

**Wrap-up:** IMPLEMENTATION-NOTES "Workshop content editing" gets the Actions tab as its second user and anything the
template lacked. CLAUDE.md's Workshop paragraph: "the Items and Actions tabs edit". TODO.md: "Workshop: edit Actions"
is deleted. "Workshop: edit Auras" loses its "as edit Actions" and gains what this plan learned about cross-table
previews (Decision 3).

## Order against other plans

- **Before "Workshop: edit Auras".** Auras follows the same list and depends on this plan for one fix it would
  otherwise make first: Decision 3's rule about which records a preview reads.
- **Independent of "Class exceptions: runtime class grants"**, though both touch action grants. Decision 5 is the
  narrower change, and it holds for a runtime class grant too.
- **"Blueprints as content"** would turn Decision 4's index entries into `Reference` fields and lift the code-held
  refusals. Nothing here waits on it.

## Not built by this plan

- A draft view across tables (Decision 3's alternative).
- Editing derived actions (`ActionGrant.Derived` recipes are C#), or the spell a `SpellGrant` holds inline, other than
  as the generated nested rows they already are.
- Presentation recognising Dodge by its entry (filed as a TODO by Phase 0).
- Any change to scrolls using their spell's targeting: a scroll's own spec is deliberate (scroll scaling).

## Bookkeeping

- TODO.md's "Workshop: edit Actions" points here until Phase 2's wrap-up deletes it.
- Memory: a project memory once the decisions are confirmed, and again when the plan lands.
