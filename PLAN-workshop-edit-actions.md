# Workshop: edit Actions -- the second editing tab, and scrolls that cast their spell

Plans the TODO.md entry "Workshop: edit Actions" (Engine, High). The machinery is `PLAN-workshop-edit-items.md`'s, built
and checked 2026-11-14, with compare (2026-11-15), filters (2026-11-15) and row symbols (2026-11-17) on top. None of it
is reopened. IMPLEMENTATION-NOTES "Workshop content editing" has the list for adding an editing tab, and this plan
follows it.

Written 2026-11-25 from a read of the code. Revised 2026-11-26 to cover:
- scrolls cast their spell as it is, so changing how items are stored is in scope;
- Dodge can later have enhanced versions;
- the ExampleMod gets actions;
- Intelligence scaling moves from scrolls to spells;
- the Torch spell;
- the Workshop always shows plain catalog values.

**Status:** complete -- Phases 0-3 built and checked, 2026-11-26. Confirmed
2026-11-26, Decisions 1-11 as written here. Decision 9 was reversed in review (the Workshop
shows plain catalog values, and Item Details changes to match). Decision 11 (Intelligence scales spells, not scrolls)
and the Torch spell in Decision 2 came from the same review.

**The measure:** the Items plan's last check said a second table should be one description, not a second copy. The
Auras trial on 2026-11-14 was about forty lines. The Actions tab should land near that. Anything bigger is generic
machinery that turned out to be Items-shaped, and it gets fixed in the machinery, not worked around in the tab. Phase 0
(scrolls) and Phase 1 (Dodge) are Game work that the tab makes reachable. They aren't part of that measure.

## What scrolls are for (the intent this plan builds toward)

A scroll lets anyone cast a spell once without paying its costs. A non-caster with no mana can cast a powerful spell
from one. Unless something modifies the cast, the scroll keeps the spell's targeting, timing and effects. What
modifies it:
- **Built now:** the caster's Intelligence. That is the spell's own scaling, the same whether the spell is cast
  directly or from a scroll (Decision 11). A scroll has no scaling of its own.
- **Later:** scroll-specific skills.

When spells have levels, a scroll will hold a spell **at a set level**: a poor scroll might hold a level-1 Heal, and a
fine one a level-15 Heal. Scrolls themselves have no level. Later still, players with crafting and scroll skills will
craft a scroll for a spell they know.

So a scroll holds **which spell (and, later, at what level of that spell)**. Everything else is the spell's. This plan
builds the first half and leaves clear seams for the rest. It builds none of the later features. Scrolls holding
effects of their own, or several spells, are TODO.md's "Advanced Scrolls" (Game, Low).

## What the code says today

### 1. Already in place, at no cost to this plan

- **Every field an action has, an item has too.** Both share `ActivatableContent.LeadingFields`/`TrailingFields`:
  tags, effects, toggle (`tog`) and activation effects (`ae`). Both hold an `IActionActivator` carrying
  `TargetingSpec` and `ActionTiming`. The only action-only field is `core`. So a spell's mana (a `ManaDrain` in `ae`),
  its timing and its toggle spec are already edited by the generated inspector on the Items tab.
- **Per-record checks exist:** `ContentChecks.ProblemsOf(ActionDefinition)` covers tags, toggles (Self on the caster,
  and only revertible grants with no end), targeting and references.
- **The preview takes a definition:** `ActionDetailsWindow.Configure(definition, width, comparedAgainst)` works for a
  definition the catalog doesn't hold, and it is already a session-bound factory.
- **Decode-time references exist:** `IContentReferences.TryResolve<T>` and `ContentObject.Reference<T>`. An aura
  grant already holds its decoded `AuraDefinition` this way. The Workshop supplies `ContentWorkspace.TryResolve`, which
  looks in the held tables first.
- **Browser sections and a Timing filter exist** (`ActionBrowserSections`, today's `ActionsTab`).

### 2. Scrolls today

| Found | Consequence |
|---|---|
| `ScrollActivator(Targeting, Timing, SpellId)`: a scroll carries its **own** targeting, timing and effects. The spell is used only for the "Casts: Heal" line (`ItemComparisonStatExtraction`) and for what mastery teaches (`ScrollMasteryEffects`) | Against the intent. Editing Heal changes nothing about the Scroll of Healing |
| The Scroll of Healing heals 50% on Adjacent and Self (Ground only). Heal heals 20% on Self, anchored on the caster | Once scrolls cast their spell, **the shipped Scroll of Healing gets weaker**: it becomes Heal as Heal is. Scroll levels are what will later let a scroll be stronger than the spell's base |
| The Scroll of Torch names a spell (`HZo-fG9LKk6NHAAAAAAAIg`) that **no action has**. Mastery synthesises one from the scroll (`SynthesizeSpellFromScroll`) | A Torch action has to be authored from the scroll's current content. After that, synthesis goes |
| `ScrollScalingEffects` scales a scroll's range, area and durations by the reader's Intelligence, at use (`TargetResolution`, `ActivationPreviews`, `ItemActivationSystem`, `EffectContext.DurationScaleMultiplier`, `ValueContributionKind.ScrollIntelligence`). Spells cast directly don't scale | Moves to spells (Decision 11). A scroll scales only because what it casts is a spell |
| `ItemCooldownContentValidation` refuses a cooldown on a scroll | A spell's cooldown is its user's. A scroll is used up, so it takes the spell's timing **without** the cooldown (Decision 1) |

### 3. Actions, references and Dodge

| Found | Consequence |
|---|---|
| `PlayerKit.UsesActions` lists all 14 shipped actions. The races grant Quick Attack, Power Attack, Dodge, Heal and Magic Missile | **Delete and Cascade are refused for every shipped action**, the way the Health Potion was. The cascade check needs made-up content |
| `ContentReferenceIndex` reads a blueprint's `UsesActions` but **not its `Actions` grants** (`ActionGrant`, including `ActionGrant.Derived`) | The race grants are missing from Used By and the disable dialog. An action granted only by a race would be deletable, and the publish's trial build would then fail |
| Presentation recognises Dodge by `BuiltInActionIds.Dodge` (`ActionTargetingController` ×4, `MapGameOverlay`). That covers the WASD confirm, the step tiles, the step queued on confirm and the overlay. `DodgeFootprint` assumes a step of exactly one tile | Any other dodge -- an authored variant, an enhanced version an NPC starts with, one the player learns -- gets none of that. Its `DodgeActivation` would grant the window but take no step (Decision 6) |
| A grant is keyed by the **granted** Guid (`ActionGrantEffects.Grant`), and `Catalog.Get` follows a disabled record's redirect | Disable Quick Attack with Power Attack as its replacement, and a Human gets two instances that both play Power Attack (Decision 7) |
| An action's activator is **required** (`ac`), unlike an item's. `ItemsTab.NewRecordFields`/`PlaceholderJsonOf` write a name and glyph only | A new action, and the stand-in for an undecodable action line, need an activator (Decision 8) |
| `GameInspectorSource.FieldsInDisplayOrder` puts the activator before tags only for `ItemDefinition` | On an action, the "Implied by the activator" note would sit above the activator it describes |
| `ContentSession` maps a load problem's file to a tab only for areas and `Items.jsonl` | A broken line in `Actions.jsonl` opens nothing from the Error List |
| The ExampleMod (`Mods/Mods.ExampleMod/Definitions`) holds items and an area, and no actions | A mod's action -- new, or replacing master's -- has never been through a build or the Workshop (Decision 10) |

## Decisions

Confirmed 2026-11-26.

1. **A scroll names its spell, and its use is the spell's, built when the scroll decodes.**
   - `ScrollActivator` becomes `ScrollActivator(ActionDefinition Spell)`. It holds the decoded spell, as
     `AuraSourceGrant` holds its aura, read through `ContentObject.Reference<ActionDefinition>("sp")`.
   - Its `Targeting` is the spell's. Its `Timing` is the spell's with the cooldown cleared, because a scroll is used up.
     Its implied tags are the scroll's (`Item.Consumable.Scroll`, `Spell`, `Magic`) plus every tag the spell has, so
     `Trait.Dodgeable` and `Effect.Healing` carry over.
   - The item codec builds a scroll's `ItemDefinition` with the spell's effects. A scroll record holds only the item's
     own fields: name, look, value, description, its own tags, and `{"$":"Scroll","sp":…}`.
   - The writer writes no `fx` for a scroll, and the reader fails a scroll holding any: "a scroll's effects are its
     spell's".
   - **Costs are waived:** a scroll's `ActivationEffects` are the item's own, empty by default, never the spell's.
   - **Not allowed:** a toggle spell on a scroll (a scroll isn't a toggle item). That is a per-record check.
   - Every call site that reads `item.Effects`, `item.Activator.Targeting` or `.Timing` keeps working unchanged. So
     do `ActivationPreviews`, the windup and Item Details. Intelligence scaling reaches a scroll through Decision 11.
   - *Alternative:* look up the spell through the catalog at each use. Rejected: dozens of call sites, and a preview, a
     use and a check could each see a different spell.
2. **The spell is fixed for the session, through layers and redirects, like any definition.**
   - The reference resolves to the record the action catalog registers: the winning layer, its checks passed (a save
     override that fails falls back, as `ContentCatalogLoader` already does), and a disabled spell's replacement chain
     followed.
   - Registering a scroll registers its spell, beside its auras.
   - Mastery teaches the spell's Guid, and `SynthesizeSpellFromScroll` goes: a scroll whose spell resolves to nothing
     doesn't decode.
   - **Torch becomes a spell** under the Guid the Scroll of Torch already names (`HZo-fG9LKk6NHAAAAAAAIg`), so the
     scroll's line needs only its own targeting and effects removed. It is the scroll's content as a spell: Single
     target, range 6, both targeting modes, an `AuraSourceGrant` of Torch Light (power 8, size 3, 600 frames), and a
     `ManaDrain` of 2. PlayerKit grants it and lists it in `UsesActions`. It is a useful test spell: an aura placed
     once per activation, on a marked entity or on the ground; a timed aura anchor; Intelligence scaling of range and
     duration; and a scroll binding.
   - A save's crafted scroll can name a crafted spell, because both are records (`ContentStore.CreatePersistent`). A
     spell held inline by a tome (`SpellGrant`) is no record, so a scroll can't name it until it is persisted. That
     rule is for crafting to use later.
3. **The future seams, recorded and not built.**
   - **The spell's level in a scroll:** when spells have levels, `ScrollActivator` gains the level of the spell it
     holds, as a field with a default (level 1), which changes no existing file. A scroll has no level of its own; its
     quality is which level of the spell it holds.
   - **Scroll skills** are stat modifiers conditional on the `Item.Consumable.Scroll` activator tag, read live at use
     like every other modifier (CLAUDE.md "Every amount an entry carries goes through stat modifiers"). Range and area
     would need targeting modifiers, which don't exist yet; they would join Decision 11's spell scaling.
   - **Crafting** makes a persistent item record naming a spell (and later its level). Its Guid is derived from the
     canonical line, so the same scroll crafted twice is one record.
   - **Advanced Scrolls** (a scroll with effects of its own, or several spells cast together or in turn) is a TODO.md
     entry of its own (Game, Low). Decision 1's "no `fx` on a scroll" is the rule that entry relaxes.
   - These go in TODO.md's existing scroll and spell entries ("Torch reveal + light-weakness damage, Scroll Mastery
     power-scaling", "Spell leveling", "Item identification and recipes") as notes.
4. **No custom editors for mana, timing or the toggle spec. Add notes instead.**
   - Under Activation effects, for actions and items alike: "Costs 2 mana, 1 health", from `ActivationCosts` (the
     hotbar badge's totals).
   - On a scroll's Spell row: "Casts Heal as it is: Self, Immediate, its costs waived".
   - *Alternative:* a "Mana cost" number row that writes a `ManaDrain`. Rejected: it is a second way to edit one value.
5. **A preview reads the other tables as held in the Workshop, edits included.** A scroll's definition is built from
   its spell, so the Items tab's scroll preview must follow Heal as Heal is being edited. This uses the decode
   reference the workspace already supplies (`TryResolve`, held tables first). New: **an edit re-evaluates every held
   record that decodes through it.** That means the records whose reference walk names it in a field the decode
   resolves: a scroll's spell, an aura grant's aura. This is the generic form of what the Auras TODO asks for.
   - Publishing Heal doesn't rewrite the scroll's line, because it names only the Guid. The trial build covers both.
   - Item Details' "Casts:" reads `scroll.Spell.Name` and no longer asks the catalog.
6. **Dodge is recognised by what it does, not its Guid. This changes the first version of this decision.** Knowing that
   enhanced dodges will exist -- longer steps, added effects, an NPC's own by default, one the player learns later -- a
   Guid check is wrong for every one of them. And the Actions tab is what lets an author make one.
   - Game answers "is this a dodge?" from the definition: it holds a `DodgeActivation`. That is
     `ActionQueries.IsDodge`, read through `TargetingView` (or `ActionStateView`). Presentation asks it of the armed
     action's effective definition, so a race's override or a learned variant counts too.
   - **The step's length is the definition's:** its targeting's `MaximumRange`, which says 1 for today's Dodge.
     `DodgeFootprint` keeps its one-tile step for now. A dodge whose range is over 1 is a check problem until
     multi-tile steps exist: "a dodge steps one tile; longer steps aren't built". So an authored variant can't silently
     do less than it says.
   - Not built, and filed as TODO entries:
     - "Dodge steps longer than one tile".
     - "Action upgrades: a learned action that supersedes another in the hotbar and the Actions window". Today's seam
       for an NPC's default enhanced Dodge is a grant under Dodge's own Guid with an override, as a race's `Derived`
       grants are. An authored record that supersedes Dodge is the content-side answer.
     - "NPCs dodge": a behavior that finds the entity's dodge by `IsDodge`.
   - With the Guid checks gone, no code names Dodge except blueprints' grants, which the index sees (Decision 7).
7. **Code-held action references and grants of a disabled action.**
   - `ContentReferenceIndex` adds each blueprint's own `Actions` grants beside `UsesActions`: a plain grant reads
     "grants it, in code" and a derived one "derives its own from it, in code".
   - `ActionGrantEffects.Grant` and the base of `ActionGrant.Derived` key the instance by the catalog's resolved id,
     the end of the replacement chain. Two grants that resolve to one action then merge into one instance.
   - *Alternative:* keep grants keyed by the old Guid. Rejected: Confirm With Replacement would hand players
     duplicates.
8. **A new action starts as a Direct activator on the caster** (`{"$":"Direct","tg":{"sh":"Self","an":"Caster"},"tm":{}}`).
   It decodes and passes every check at once. The stand-in for an undecodable action line writes the same activator.
   `ItemsTab.PlaceholderJsonOf` becomes `ActivatablePlaceholder.JsonOf` in `Builders/Editing`, shared by both tabs.
9. **The Workshop always shows plain catalog values** (reversed in review: values as used by the Workshop's player were
   proposed). Action Details already does this: it has no user in the Workshop. Item Details changes to match.
   - Today `ItemsTab` configures Item Details with the session's player (`PlayerEntityId`, no stack), so a value
     depends on that player's ability scores and modifiers.
   - It gains the same mode Action Details has: no user, always Advanced, the definition's own values. No
     Intelligence scaling, no Outgoing modifiers, no player blocker.
   - Which entities a value is worked out for is TODO.md's "Workshop Test Preview" (Engine, Low): pick a premade
     caster and target and see the values that result. It depends on an Entity Editor that makes entities with set
     ability scores, skills, items, actions and equipment.
10. **The ExampleMod gains actions** (`Definitions/Actions.jsonl`) and a scroll, each one to exercise a path:

    | Record | What it is | What it tests |
    |---|---|---|
    | **Example Spark** | A new spell: Single target, range 5, a `DirectDamage`, a `ManaDrain` of 3 | A mod's own action: listed "[ExampleMod]", read-only, Used By |
    | **Heal, replaced** | Master's Guid, healing 25% instead of 20% | A mod replacing a master action: "replaced by ExampleMod" on the Actions tab; the master Scroll of Healing casts the mod's Heal in a game with the mod |
    | **Example Long Step** | A dodge variant: `DodgeActivation`, range 1, FreeCast, plus a short Haste `StatModifierGrant`, with its own glyph | Decision 6 on content: the Actions tab shows it, and `IsDodge` is true for it |
    | **Scroll of Example Spark** | A mod item naming the mod's spell | A scroll resolving within its mod's layer, and preview from a mod |

    `ModContentTests` and the Workshop's mod tests are updated for the new counts. Nothing grants Long Step in a game
    yet; that waits on the "Action upgrades" TODO entry.
11. **Intelligence scales every spell, cast directly or from a scroll, with the formula scrolls used. A scroll has no
    scaling of its own** (added in review).
    - `ScrollScalingEffects` becomes `SpellScalingEffects`, unchanged in formula: a 1x to 4x multiplier over
      Intelligence total 1 to 300, on maximum range, area size and effect durations.
    - **What is a spell:** the use's tags hold `Spell`. That is implied by `SpellActivator` and, through Decision 1, by
      `ScrollActivator`. One rule covers a cast, a scroll, a tome's granted spell and a mastered one. A wand, a potion
      and an ability don't scale.
    - Applied where the scroll's was: `TargetResolution` (range and area), the effect context's duration multiplier
      (`ItemActivationSystem` today, now also the action path), and `ActivationPreviews`. `ValueContributionKind`
      `ScrollIntelligence` becomes `SpellIntelligence`, and Item Details and Action Details name it as such.
    - **Costs aren't scaled.** A spell's `ManaDrain` is an activation effect and not a duration. Minimum range isn't
      scaled either, as before.
    - **It applies to NPCs too:** a goblin's Heal and a fairy's Magic Missile scale with their own Intelligence.
    - **A gameplay change the in-game checks look for:** every shipped spell reaches further for a caster with high
      Intelligence. Toggle spells (Toxic Aura, Lantern) are Self-only, so only their held effects' durations would
      scale, and they hold nothing with a duration that runs out.

Settled by the code, with no choice in it:
- Mod records are read-only, and a mod-replaced master record is edited as master's.
- Disable, Enable, Replacement and Cascade are the workspace's. Nothing about them is action-specific once the index
  sees the grants.
- A published scroll can't name a spell that is only a draft. That is the existing rule, applied to the `sp` reference.

## The design

| Piece | Where | What |
|---|---|---|
| `ScrollActivator(ActionDefinition Spell)`, its codec, the item codec's scroll branch | Game (`Actions`, `Inventory`) | Decision 1 |
| Content references resolving actions | Game (`GameContent`, `ContentCatalogLoader`) | Decision 2: the registered record, redirects followed, a scroll's spell registered with it |
| `ScrollMasteryEffects` | Game | Synthesis removed |
| `Actions.jsonl` "Torch"; `Items.jsonl` scrolls rewritten | Content | A Torch spell made from the Scroll of Torch as it is; both scrolls reduced to their spell |
| `ActionQueries.IsDodge`, the dodge range check | Game | Decision 6 |
| `ActionTargetingController`, `MapGameOverlay` | Presentation | Ask `IsDodge` instead of comparing Guids |
| `ContentReferenceIndex`, `ContentLinks` | `Builders/References` | Blueprint grants (Decision 7); a scroll's spell from `Spell` |
| `ActionGrantEffects`, `ActionGrant.Derived` | Game | Decision 7 |
| `ContentEditingTable<ActionDefinition>`, `ActionsTab`, `ActivatablePlaceholder` | `Builders` | The tab (Decision 8) |
| `ContentWorkspace` | `Builders/Editing` | Re-evaluate the records that decode through a changed one (Decision 5). Drop the "no Actions table held" branch in `AllAppearances` |
| `GameInspectorSource` | `Builders/Editing` | Activator before tags for both types; the cost and "Casts" notes (Decision 4) |
| `SpellScalingEffects` (renamed), `TargetResolution`, the action and item appliers, `ActivationPreviews`, `ValueContributionKind` | Game | Decision 11 |
| `ItemsTab`, `ItemDetailsWindow` | `Builders/Items`, Presentation | Item Details with no user in the Workshop (Decision 9) |
| `ContentSession` | `Workshop/App` | `Actions.jsonl` problems open the Actions tab |
| ExampleMod `Actions.jsonl`, `Items.jsonl` | Mods | Decision 10 |

## Phases

Each phase ends with the game and the Workshop running, then stops for testing before the next.

### Phase 0 -- Scrolls cast their spell (Game and content)

Built 2026-11-26, awaiting the in-game checks. As built, and where it differs from what follows:
- `ScrollActivator(SpellId, Spell)`: the Guid as written (so a disabled spell's replacement never rewrites the line)
  and the spell it resolves to. `ItemDefinition` takes a scroll's effects from it whatever is passed; the item codec
  writes no `fx` for a scroll and fails one holding any.
- `ScrollContentValidation` (Inventory, in `ContentChecks` and the build): a scroll's spell must have a
  `SpellActivator` and must not be a toggle. The first rule wasn't in the decisions; it is what "binds a spell" means.
- Spells resolve at decode through every reference source: `GameContent`, `ContentStore` (a save's crafted scroll, by
  the session's catalog), entity snapshots and the Workshop's `ContentWorkspace` (its session's content until Phase 2
  holds the Actions table). `ContentCatalogLoader.LoadSaveLayers` settles a save's actions before decoding its items.
- `SpellScalingEffects` (was `ScrollScalingEffects`): the rule is "the activator implies `Spell`".
  `ActivationEffectResolver.Apply` works out the duration multiplier itself, so actions and items share it;
  `ValueContributionKind.SpellIntelligence` reads "Intelligence (spell)".
- Torch: `BuiltInActionIds.Torch` (was `TorchSpell`), glyph `t` in orange, no sprite of its own, 2 mana.
- Not done: `ItemComparisonStatExtraction`'s `ActionCatalog` parameter is unused now (the "Casts:" line reads the
  scroll's spell), but it is threaded through every details window's constructor. It goes in Phase 2, when the
  Workshop's previews are reworked.

1. `ScrollActivator(Spell)`, the scroll codec and the item codec's branch, the toggle-spell check and the "no `fx` on a
   scroll" rule (Decision 1).
2. Content references resolve actions to the registered record; registering a scroll registers its spell; mastery
   stops synthesising (Decision 2).
3. Content: the Torch spell under the Guid the scroll names, granted by PlayerKit. Then rewrite both scrolls (and any
   scroll in `Drafts/Items.jsonl`) to name only their spell.
4. Intelligence scales spells, not scrolls (Decision 11).
5. Item Details' "Casts:" reads the held spell. `ContentReferenceIndex` reads `scroll.Spell`.
6. TODO.md notes for Decision 3's seams.

**Tests:**
- A scroll's definition has its spell's effects, targeting and timing, no cooldown, no activation effects, and tags
  that include the spell's.
- A scroll line holding `fx` fails at that key.
- A scroll naming an unknown spell fails to decode. A scroll naming a disabled spell casts its replacement.
- A save override of the spell that fails its checks leaves the scroll casting the record beneath.
- A scroll whose spell is a toggle fails its check.
- Mastery at the threshold grants the spell's Guid, and nothing is synthesised.
- At Intelligence 300, Torch cast directly and from its scroll resolve the same range (24) and the same aura duration.
  A wand's range doesn't scale. A spell's `ManaDrain` and minimum range don't scale. An NPC's spell scales by its own
  Intelligence.
- `ContentCodecTests` round-trips both scrolls.
- Determinism and `WorldGenerationIndependenceTests` pass. Seed fingerprints may move if a shipped scroll is used in a
  recorded scenario; any that moves is named in the phase's notes.

**Check:**
- Game: read the Scroll of Healing with no mana. Expect 20% healing on yourself, no mana spent, and Item Details reading
  "Casts: Heal" with Heal's targeting.
- Read the Scroll of Torch at a tile: expect the light as before.
- Cast Torch from the hotbar at a marked creature and then at the ground: expect the light to follow the creature and
  then to stay on the tile, and 2 mana spent.
- With Intelligence raised (Admin Mode's component edit): expect Torch's highlighted range to grow when it is cast and
  when it is read from the scroll alike, and the light to last longer. Expect the Wand of Fireball's range unchanged.
- Item Details of the Scroll of Healing and Action Details of Heal, both in Advanced mode: expect the same values, a
  "Spell Intelligence" line in a breakdown tooltip, and no mana line on the scroll.
- Workshop, Items tab: expect each scroll's inspector to show only its own fields and Spell, and the preview to match
  the game.

### Phase 1 -- Dodge by what it does; references and grants

Phase 0 checked in-game 2026-11-26. Found in that check and fixed with it: details rows started at two different left
edges (rows kept the window's side padding; `ActivatableDetailsSections.CreateRowWindow` now has none), and the
activation grid showed one aim's footprint only, so a Single target spell was one tile -- it now shows every tile in
range dimmed (`TargetShapePreviewGeometry.ComputeReach`) under the landed tiles, aimed at the edge, capped at the aura
reach's 96 px. TODO.md "Details: Range and Area read the same in words and in the grid" (Presentation, Medium) is what
is still confusing about it.

Built and checked 2026-11-26. As built, and where it differs from what follows:
- `Game.Modules.Actions.Dodges` (not `ActionQueries`): `IsDodge`, `StepTiles`, `ProblemsOf` in `ContentChecks` for
  actions and items, `EnsureValid` in the build. Presentation keeps the answer beside the armed action
  (`MapViewState.ArmedActionIsDodge`, written only with `ArmedActionId` by `ActionTargetingController.SetArmedAction`),
  since `MapGameOverlay` has no action view; a queued activation asks `IsDodge` of the action it queues.
- Grants resolve where they are read, not only where they are made: `Catalog.ResolveId` (Engine) follows redirects, and
  `EntityActions` takes every Guid it is asked about and every grant it holds -- the entity's own, applied parts', its
  blueprint's, Core, cooldowns -- as the action it resolves to; among grants resolving to one action, one naming it
  directly wins. `ActionGrantEffects.Grant` stores the resolved Guid too. Blueprint grants are static and never
  stored per entity, so keying "the instance" alone would have missed them.
- TODO.md: "AdvancedDodge buff" became "Enhanced dodges: steps longer than one tile"; "Action upgrades" and "NPCs
  dodge" are new (Game, Low, beside it).

1. `ActionQueries.IsDodge`; Presentation asks it; the "a dodge steps one tile" check (Decision 6). TODO entries for the
   longer step, action upgrades and NPC dodging.
2. `ContentReferenceIndex` reads blueprint grants, plain and derived (Decision 7).
3. Grants resolve through the catalog's redirect (Decision 7).

**Tests:**
- `IsDodge` is true for Dodge and for a test definition holding `DodgeActivation` under another Guid, and false for
  Quick Attack.
- An armed test dodge offers the step tiles and takes the WASD confirm, driven through `UiInputController`.
- A dodge with range 2 fails its check.
- Quick Attack's Used By lists Goblin, Fairy and Ghost as derived and Human as a plain grant.
- A blueprint granting an action and a disabled action that it replaces builds one instance.
- A derived grant over a disabled base derives from the replacement.
- `BuiltInModulesTests` still pass.

**Check:**
- Game: arm Dodge, expect the WASD letters and step tiles as before, then dodge a Power Attack.
- Workshop: Quick Attack's Used By lists PlayerKit "builds with it", Human "grants it", and Goblin, Fairy and Ghost
  "derives its own from it".

### Phase 2 -- The Actions tab edits; previews follow; the ExampleMod

Built and checked 2026-11-26. Found in the check and fixed: a field row's tooltip mark sat on its control (the
label now leaves the mark's room, `FieldRow.LabelWidth`, for every editor kind), and a toggle given Self plus another
shape read "not Self-targeted" -- still refused, the message now names what the shape adds and opens the Shape field
(`ToggleContentValidation.SelfOnlyProblem`). As built, and where it differs from what follows:
- `ActionsTab` derives `ContentEditingTab<ActionDefinition, ActionDetailsWindow>`; its table is built in
  `BuilderTabs.Create` before the Items table, so scrolls decode against the actions held. `ActivatablePlaceholder`
  (Builders/Editing) is both tabs' stand-in and the new action's activator. `ActionBrowserSections` gained the Timing and
  Core filter values.
- Item Details already showed plain values in the Workshop -- its session never has a player -- but only by accident:
  `NoWorkshopPlayer` now says so, for Item Details' factory and `ItemsTab`.
- Notes (`GameInspectorSource.NoteFor`): "Costs 2 mana" under Activation effects for actions and items, from the new
  `ActivationCosts.Declared` (amounts as content gives them, no user); "Casts Heal as it is: Self, Immediate, without
  its 2 mana" under a scroll's Spell. `ActivatableContent.ScrollSpellKey` is the key's one name.
- Re-evaluation: `IContentEditingTable.ReevaluateRecordsNaming`, called by the workspace after any edit, undo, add or
  removal, evaluates again every held record of any table naming the changed one (one step out) and tells its tab.
  `ContentWorkspace.TryResolve` follows a held disabled record's replacement, as the game does.
- The unused `ActionCatalog` parameter is gone from the stat extraction, `ComparisonLineStyles`,
  `ActivatableDetailsSections` and the three details windows.
- ExampleMod: `Definitions/Actions.jsonl` (Example Long Step, Heal at 25%, Example Spark) and the Scroll of Example
  Spark in `Items.jsonl`; `ModContentTests` checks them, the master scroll included.
- Not added: a "publish one field is one line" test for actions -- the machinery's own tests cover it for any table.

1. `ActivatablePlaceholder`, `ActionsTab` on `ContentEditingTab`, the table in `BuilderTabs.Create`, with the read-only
   tab removed. Filters: Type, Timing, Core.
2. `GameInspectorSource`: activator before tags; the cost and "Casts" notes.
3. Records that decode through a changed record are re-evaluated (Decision 5).
4. Item Details with no user in the Workshop: plain catalog values (Decision 9).
5. `ContentSession` maps `Actions.jsonl` to the tab. `AllAppearances` loses its session-actions branch.
6. The ExampleMod's actions and scroll (Decision 10), and its tests.

**Tests:**
- Every shipped action opens as a master document and writes back to its own line byte for byte.
- A new action decodes with no problems. The stand-in for a broken line decodes.
- One changed field publishes as one changed line.
- A toggle action given a non-Self shape goes to Drafts with the toggle rule's problem.
- A scroll naming a draft-only spell stays in Drafts and names the spell.
- Editing Heal's effect re-evaluates the Scroll of Healing's document, and its definition shows the new effect before
  any save.
- The cost note reads "Costs 2 mana" on Heal.
- The Workshop opens the ExampleMod's four records, with the right labels.
- The table opens exactly the master records (15 with Torch). The test names any extra the session's catalog
  contributes.

**Check (Workshop):**
- Select Heal: expect the activator above Tags with "Implied by the activator: Magic, Spell" under them, "Costs 2 mana"
  under Activation effects, and Action Details with the definition's own values.
- Items tab, Wand of Fireball: expect Item Details' damage to be the content's numbers, with no ability-score bonus in
  its tooltip.
- Change Heal's `DirectHeal` to 30%, then open the Items tab: expect the Scroll of Healing's Item Details to read 30%
  before saving.
- Ctrl+Z: expect 20% on both tabs.
- Publish Heal at 30%: expect `git diff` to show one line of `Actions.jsonl` and none of `Items.jsonl`. Play and read
  the scroll: expect 30%. Revert and publish.
- Change Heal's `ManaDrain` to 3: expect the cost note and Action Details to read 3, and the scroll's details to show no
  cost. Ctrl+Z.
- Lantern: change its shape to Circle. Publish: expect it in `Drafts/Actions.jsonl` with the toggle problem in the Error
  List, and master unchanged. Revert All, Save All: expect the draft gone.
- New: expect "New action" to decode at once as an Ability. Ctrl+S: expect a draft with no errors. Delete it.
- Filters: Type Spells, Timing Delayed, Core: expect only matching rows, with chips.
- Hand-break a line of `Actions.jsonl` and restart: expect an Error List line that opens the Actions tab at its stand-in.
  Revert the file.
- With the ExampleMod installed (`-p:DeployToGame=true`): expect Example Spark and Example Long Step "[ExampleMod]",
  read-only, and Heal "replaced by ExampleMod" and still editable. On the Items tab, expect the Scroll of Example Spark
  previewing Spark's damage.
- Game with the mod: take the Scroll of Healing and the Scroll of Example Spark from Admin Mode's item list and read
  each. Expect 25% healing and Spark's damage, both with no mana spent.

### Phase 3 -- Disabling across tables, and the wrap-up

Built and checked 2026-11-26. No new code was needed: `Workshop.Tests/Editing/CrossTableDisableTests`
holds the checks' cases over a copy of master (a spell and its scroll refused Delete, cascaded and undone as one step;
a replacement the scroll then casts with its line unchanged; Dodge's races listed as code and Cascade refused). The
wrap-up is done: IMPLEMENTATION-NOTES "Workshop content editing" (re-evaluation, plain values, the Actions tab in the
list) and "Scrolls cast their spell; Intelligence scales spells; dodges and disabled grants"; CLAUDE.md's Workshop
paragraph and a bullet beside "A cost is an activation effect"; TODO.md's "Workshop: edit Actions" removed and "Workshop:
edit Auras" told what is already built.

**Check:**
- Disable Dodge: expect the dialog to list PlayerKit and every race under "Can't be disabled here", with Cascade off.
  Cancel.
- Actions tab: New "Test Bolt" (Spell, Single target, range 5, `DirectDamage` 3), then Publish. Items tab: New "Scroll
  of Test Bolt" naming it, then Publish. Expect Uses and Used By to link the two.
- Delete Test Bolt: expect it refused, naming the scroll.
- Disable Test Bolt with Confirm With Cascade: expect both greyed and the status line to read "1 action, 1 item". Ctrl+Z
  from the Items tab: expect both back, staying on the Items tab with the scroll selected. Ctrl+Y, then Ctrl+Z from the
  Actions tab: the same, staying there.
- Disable Test Bolt with Confirm With Replacement, picking Magic Missile. Publish, Play, and read the scroll: expect a
  Magic Missile.
- Disable Quick Attack with Confirm With Replacement, picking Power Attack. Publish, then Play: expect Power Attack once
  in the Actions window, and goblins hitting with their Power Attack. Enable Quick Attack and publish: expect `git diff`
  on `Actions.jsonl` empty.
- Delete the scroll, then Test Bolt, and publish: expect both tables as before the phase.

**Wrap-up:**
- IMPLEMENTATION-NOTES: "Workshop content editing" gains the Actions tab as its second user and anything the template
  lacked. A new section, "Scrolls cast their spell", holds Decisions 1-3.
- CLAUDE.md: the Workshop paragraph ("the Items and Actions tabs edit") and the scroll rules beside "A cost is an
  activation effect".
- TODO.md: "Workshop: edit Actions" is deleted. "Workshop: edit Auras" drops what Decision 5 already built.

## How the Items tab (the inventory editor) does it differently

Every difference is listed so it stays a decision, not drift (action/item parity).

| | Items tab | Actions tab, as planned | Why |
|---|---|---|---|
| A new record | Name and glyph; activator unset, since it is optional | Name, glyph, and a Direct activator on the caster | An action's activator is required (Decision 8) |
| Stand-in for an undecodable line | Name and glyph | The same, plus that activator | The same reason |
| Browser filters | Type | Type, Timing, Core | Timing and `core` matter to actions only |
| Browser sections | By activator kind (Potions, Scrolls, Wands, ...) | Abilities, Spells, Toggles, Other | Each table's own headings, as today |
| Preview values | Plain catalog values, no user (changed by this plan: the Workshop's player today) | Plain catalog values, no user (as today) | The same, by Decision 9 |
| Intelligence scaling | A scroll scales because it casts a spell | Every spell scales | One rule, by Decision 11; a scroll scaled on its own before |
| Notes in the inspector | Implied tags | Implied tags | Same rule; activator before tags in both now |
| Cost note | Added by this plan | Added by this plan | Same, for both |
| Code-held references | Blueprints' `UsesItems` | Blueprints' `UsesActions` and `Actions` grants, plain and derived | Actions are also granted, and derived by races |
| How many shipped records refuse Delete and Cascade | Those a blueprint's `UsesItems` names (PlayerKit alone names 16) | All 14 | `PlayerKit.UsesActions` names every action |
| What a replacement means | The catalog's redirect: a stack resolves to the replacement | The same, plus grants keyed by the resolved id | Grants are per entity, keyed by Guid (Decision 7) |
| Records that depend on another table | Scrolls (their spell), aura grants | Aura grants | A scroll's definition is built from an action (Decisions 1 and 5) |
| Fields that can't be set | A scroll's `fx`, set by its spell | None beyond the shared schema | Decision 1 |
| Mod content in the ExampleMod | A replaced item and a new one | A replaced action, a new spell and a dodge variant, plus a scroll | Decision 10 |

## Order against other plans

- **Before "Workshop: edit Auras".** Decision 5 builds the re-evaluation that entry wants.
- **Before "Spell leveling", "Item identification and recipes" and scroll crafting.** They build on Decisions 1-3.
- **Independent of "Class exceptions: runtime class grants".** Decision 7's grant rule holds for a runtime class grant
  too.
- **"Blueprints as content"** would make Decision 7's index entries `Reference` fields and lift the code-held
  refusals. Nothing here waits on it.

## Not built by this plan

- Spell levels and the level a scroll holds, scroll skills, targeting stat modifiers, scroll crafting and Advanced
  Scrolls (Decision 3 records their seams).
- The Workshop Test Preview and the Entity Editor it depends on (Decision 9).
- Multi-tile dodge steps, action upgrades that supersede another action, and NPC dodging (Decision 6 files them).
- Editing derived actions (`ActionGrant.Derived` recipes are C#) or a tome's inline spell, except as the generated
  nested rows they already are.

## Bookkeeping

- TODO.md's "Workshop: edit Actions" points here until Phase 3's wrap-up deletes it.
- Memory: a project memory once the decisions are confirmed, and again when the plan lands.
