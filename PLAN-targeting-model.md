# Targeting model: shapes, paths, hit types, walls and ranges

(Drafted 2026-10-30, after one distance measure and the tabletop cone landed. Replaces the plan's own earlier phases 2 and 3
-- Wall of Fire and Ring of Lava are Phase 2 below. Every question was decided on 2026-10-30, listed at the end.)

## Verdict in one paragraph

Today a `TargetingSpec` is one `Shape` (a [Flags] set of Self, Adjacent, SingleTarget, Line, Cone, Burst), one `Range`, one
`AreaSize`, and the two Target/Ground settings; the effect appears on the shape's tiles at once, and nothing knows about
walls. Every game below separates **how an effect gets there** from **what shape it has where it lands**, and treats
**walls** as a rule of their own. **Recommendation: split `TargetingSpec` into six independent settings -- Path, Hit count,
Anchor, Shape, Walls (three rules) and Ranges (minimum, maximum, area) -- where every combination is valid and means
something, and a combination that would be redundant just resolves the same as a simpler one.** Today's content becomes
Path `Placed`, walls ignored, with no change in what it hits. Paths are traced one tile at a time even when they take no
time, so travel time later is a projectile entity that runs the same tracer a few tiles a frame, not a second
implementation.

## How it works today

- `TargetingSpec(Shape, Range, AreaSize, Modes, TargetModeAffects)` (`Game/Modules/Actions/TargetingSpec.cs`), content
  keys `sh`, `r`, `a`, `mo`, `ta` (`ActionContentCodecs.TargetingSpecCodec`).
- `TargetShapeResolver.Resolve` (Engine.Tiles) turns a shape, the caster's footprint, an aimed tile, range and area into
  tiles; `ResolveReachable` gives the tiles that can be aimed at. One rounded-circle measure for every range and area
  (`GridDistance.IsWithinRoundedRadius`), from the caster's nearest footprint tile.
- `TargetResolution.Resolve` (Game) is the only caller that decides what lands: at activation, at a windup's end, every
  frame for the preview and telegraph, and for NPCs.
- Line and Cone are already path-like (a direction from the caster), SingleTarget and Burst anchor on the aimed tile, Self
  and Adjacent on the caster. Line and Cone measure their length with `Range`; Burst uses `Range` for the aim and
  `AreaSize` for its radius.
- Walls: none. Effects reach through walls, and any tile in range can be aimed at.

## Compared with similar games

| Game | Paths | Hit types | Shapes | Walls | Ranges | Travel time |
|---|---|---|---|---|---|---|
| Dungeon Crawl Stone Soup | Bolts from the caster; placed areas | Bolt (stops at the first creature), beam (passes through), explosion where a bolt stops (Fireball) | Explosion radius, cones are rare | Aim needs line of sight; bolts stop at walls; lightning bounces off them | Circular range per spell | None -- resolves on the turn it is cast |
| Brogue | Bolts from the caster; thrown items | Bolt, lightning passes through every creature, thrown items land at the tile | Bursts, gas spreading | Aim needs line of sight; bolts stop at walls | Per bolt | None |
| Cogmind | Projectiles; launchers | Penetration (pierce N), explosions with falloff | Explosion radius | Explosions and projectiles damage and destroy walls | Per weapon | Visible flight, resolved before the next turn |
| Tales of Maj'Eyal | Bolts, beams, balls, cones, walls | Bolt, beam, ball at the end of a bolt | Ball (radius), cone, wall, beam | Line of sight for aim; balls do not pass walls | Range and radius per talent | Some projectiles are real objects that fly over several turns |
| Into the Breach | Projectile, artillery (lobbed), laser | Projectile stops at the first object; artillery ignores everything between; laser passes through | Line pushes, small crosses | Projectile stops at mountains and buildings; artillery passes them | Artillery has a minimum range (not adjacent) | None, but every enemy attack telegraphs a turn ahead |
| XCOM 2 | Shots, thrown and launched grenades | Shot hits its target or misses; grenade lands at the tile | Circle, cone (flamethrower), line | Line of sight for shots; cover; grenades blocked by what is overhead | Throw range, launcher range | Grenades fly, but land before anyone acts |
| D&D 5e / Baldur's Gate 3 | Point of origin chosen in range; ranged attacks | Attack rolls per target; areas hit all inside | Sphere, cone (width equals distance), cube, cylinder, line | "A clear path": total cover blocks an area from reaching a creature; aim needs to see the point | Range, long range | None |

What they share, and this plan takes:
- **Path and shape are separate.** A fireball is a bolt with an explosion at its end (DCSS, ToME); a grenade is a lob with
  an explosion (XCOM, Into the Breach); a summoned firestorm has no path (DCSS Fire Storm, D&D).
- **Walls are three different questions**: can you aim there, does the path get through, does the area spread around
  the corner. Every game answers them separately (D&D's clear path is the third).
- **Minimum range** exists where a path would hit its user (Into the Breach artillery, rockets that arm after a distance).
- **Travel time** is either none (most) or real objects that fly (ToME, and every real-time game). Into the Breach shows
  that a telegraph can carry the same tactics without flight.

Left out: scatter and misses (not part of this), ceiling height (not modelled), attack rolls and cover as a percentage
(Health and damage decide what a hit does, not targeting).

### Waiting on other work

Gaps against these games that this plan does not build, each with its own TODO.md entry:
- **Friend or foe** -- which entities an action affects (everyone, hostiles only, allies only, everyone but the caster),
  and whether an ally soaks a projectile's hit count. Waits on party mechanics ("Parties").
- **Lingering areas** (High) -- shapes that stay on their tiles for a duration; Wall of Fire is its test.
- **Flood fill** (High) -- explosions that walk a distance through open tiles, and gases and liquids that fill to
  equilibrium or dissipate, each at a fill speed.
- **Falloff in targeting shapes** (High) -- strength that weakens across a shape, reusing `AuraFalloff`.
- **Fast projectile tile sweep** (High) -- needed by Phase 5, so a projectile and a creature cannot pass through each
  other between frames.
- **Push and pull effects with a direction** (Medium) -- every hit carries a direction from the resolver.
- **Several aims in one action** (Medium) -- Baldur's Gate 3's Magic Missile, manual and automatic.
- **Bouncing actions** (Medium) -- chain lightning.
- **MapLayer reach rules for actions** (Low) -- which map layers each path reaches.

## The model

Every activation resolves in the same order: **aim** (is the aimed tile allowed), **path** (from the caster toward the aim,
tile by tile, deciding who it hits and where it stops), **anchor** (where the shape goes), **shape** (its tiles), **spread**
(which of those tiles the shape reaches past walls). Each setting below is independent.

### 1. Path -- how the effect gets from the caster to where it lands

| Path | What it is | Examples |
|---|---|---|
| `Placed` (default) | The effect appears on a designated area. No path to it is needed: nothing between the caster and the aim is crossed or met. | Today's content, Fire Storm, a summoned wall |
| `Projectile` | A straight path from the caster's footprint tile nearest the aim, through the aim. What it meets is the hit count's to decide. | Magic Missile as a bolt, Lightning Bolt, DCSS Fireball |
| `Lobbed` | Arcs over every entity and lands on the aimed tile. No entity on the way is met; walls still stop it (`PathWalls`), since walls reach the ceiling. | Grenade, thrown potion, artillery |

- **Not timing.** The path says nothing about when the effect happens. Activation timing (Immediate, FreeCast, a Delayed
  windup) is `ActionTiming`'s, unchanged; a `Placed` effect behind a three-second windup is still `Placed`. Travel time
  (below) is the path's own, and a `Placed` effect never has any.
- **Path end** (Projectile only): `AtAimedTile` (stops on the aimed tile if nothing stopped it earlier) or
  `AtMaximumRange` (keeps going past the aim to maximum range -- Lightning Bolt today).
- **Equivalences, not errors:** `Lobbed` resolves the same as `Projectile` + hit count `0` with the same wall rule while
  it takes no time. It is its own path for what the player sees (an arc), for throwing range that will grow with Strength
  and skills ("Lobbed paths: the arc and throwing range", TODO.md, Low), and, with travel time, because no entity can
  step in front of a lob. The hit count does nothing for `Placed` and `Lobbed`, and `PathWalls` nothing for `Placed`,
  since they cross no entity or no tile.
- **Walls reach the ceiling.** There is no ceiling height: nothing passes over a wall, Lobbed included. Passing through
  one is only ever `PassesThroughWalls`, a deliberate choice for a ghostly or scrying effect.
- **The line itself:** keep the current Bresenham from the nearest footprint tile. Note for later: DCSS searches for a
  ray that actually passes through the aimed tile; Bresenham always does by construction, so this is fine until walls
  make some lines blocked where a neighbouring one is clear.

### 2. Hit count -- how many entities a Projectile hits on the way

Only a `Projectile` crosses tiles, so only a `Projectile` reads this setting. It is one number, `ProjectileHitCount`: the
projectile hits each entity it meets, in order, and stops on the one that reaches the count.

| `ProjectileHitCount` | Entities on the way | Where the projectile stops | Game terms |
|---|---|---|---|
| `0` (default) | None is hit; it flies past them all | Path end | -- |
| `1` | The first is hit | At the first entity | Bolt |
| `N` | The first N are hit | At the Nth | Pierce N |
| `All` | Every one is hit | Path end | Beam |

- One setting rather than a list of named kinds, so no two settings can say the same thing: a bolt is a count of 1, not
  its own option. `All` is the count never being reached.
- "Hit" means the activation's `OnEachTarget` effects apply to that entity, exactly as if it stood in the shape. The shape
  at the anchor adds its own tiles; an entity both on the way and in the shape is hit once.
- **Every entity met counts, whatever its effects did.** An entity immune to the action (a refused `EffectOutcome`), one
  it does nothing to, and one it hurts all count toward the hit count and stop the projectile the same. So a boss can
  shield itself from an attack it fears with minions or shields immune to that attack: they soak the bolt.
- `0` is for a projectile that only matters where it ends: the shape there does the hitting. It differs from `Lobbed`
  only in that walls still stop it (unless `PassesThroughWalls`).
- A projectile that is stopped by the first entity but leaves the hitting to its shape (DCSS's Fireball: the bolt does no
  damage, the explosion does) is a count of `1` with a shape that covers the tile it stopped on -- the entity there is hit
  once either way.
- What counts as meeting an entity: a living Blocking occupant. Tiny and Phasing entities never stop or count toward a
  projectile -- in the game's words, they are too hard to hit with one; an area action is how to reach them.
- The caster is never met by its own path: see "The caster's own tiles" below.
- Count `1` with shape `None` is a classic single-target projectile; with `Circle` it is DCSS's Fireball; `All` with `None`
  and path end `AtMaximumRange` is today's Lightning Bolt.
- Bouncing between targets is not a hit count: "Bouncing actions" (TODO.md, Medium).

### 3. Anchor -- where the shape is placed

The path decides who is hit on the way and where it finishes; the anchor decides where the shape's tiles are laid out.
Directional shapes (Line, Cone, Wall, Sweep) always face from the caster toward the aimed tile.

| Anchor | The shape is centred on | With each path |
|---|---|---|
| `PathEnd` (default) | Wherever the path finished | `Placed`: the aimed tile. `Lobbed`: the landing tile (the aimed tile). `Projectile`: where it stopped -- the entity that reached the hit count, the tile before a wall, the aimed tile, or maximum range. |
| `Caster` | The caster's own footprint, wherever the path went | The path still hits what it crosses; the shape is laid out around the caster instead of at the far end. |

- **A directional shape at `PathEnd` starts at the end tile and extends past it**, away from the caster: its origin is
  the end tile, its direction the caster's toward the aimed tile, its length `AreaSize`. A `Cone` at `PathEnd` is a blast
  that fans out behind the point of impact -- a projectile that bursts into a spray when it strikes, or a `Placed` cone
  that starts where you aimed instead of at your hands. A `Line` at `PathEnd` carries on past the impact; a `Wall` at
  `PathEnd` is centred on the end tile, across the direction (Wall of Fire).
  - The end tile itself is part of the shape: it is where the blast starts. At `Caster` the shape starts from the
    caster's footprint, which is left out (see "The caster's own tiles").
  - The total reach is the distance to the end tile plus `AreaSize`, so it can reach past `MaximumRange` -- the same as a
    `Circle` going off at maximum range today. `MaximumRange` limits the aim, not the shape.
  - With the end tile on the caster (a `Placed` effect aimed at your own tile), there is no direction and a directional
    shape covers nothing -- as a Cone aimed at its own caster does today.
- **Non-directional shapes are centred on the anchor**: a `Circle`, `Ring`, `Square` or `Cross` at `PathEnd` surrounds
  the end tile; at `Caster`, the caster.
- `Self` is the anchor's footprint: at `Caster`, the caster's tiles; at `PathEnd`, the end tile (or the whole footprint of
  the entity the projectile stopped on). `Adjacent` is the ring around that same footprint.
- Today: Self, Adjacent, Line and Cone behave as `Caster`; SingleTarget and Burst as `PathEnd`. Both become content.
- Examples: Fire Fan is a `Cone` at `Caster` (it starts at your hands); Fireball is a `Circle` at `PathEnd` (it goes off
  where it lands); a `Circle` at `Caster` is a nova around you; a `Projectile` with hit count `All` and a `Circle` at
  `Caster` is a beam fired while a shockwave goes off around you.

### The caster's own tiles

The tiles the caster stands on **when it releases the action** are never affected by it: its path does not meet them,
and a shape anchored on the caster leaves them out. The rule is about those tiles at that moment, not about the caster,
so a caster can still be caught by its own action in two ways:
- **On purpose:** aiming in Ground mode onto your own tiles -- lobbing an action at your feet -- places the shape there,
  and you are in it.
- **By moving into it:** with travel time, a caster that moves after release (a teleport while a slow shot is in the
  air) can stand where it lands. The tiles left out are the ones it released from, not the ones it stands on later.

Both will be rare for the player. For bosses they are a mechanic: "Boss mechanics" (`BOSS-MECHANICS.md`).

### 4. Shape -- the tiles affected around the anchor

`AreaSize` is the shape's size (radius, length or arm, below). Directional shapes face the path's direction.

| Shape | Tiles | Size means | Exists |
|---|---|---|---|
| `None` | No tiles: only what the path hits | -- | New |
| `Single` | The anchor tile | -- | SingleTarget |
| `Self` | The caster's footprint | -- | Self |
| `Adjacent` | The ring around the anchor's footprint | -- | Adjacent |
| `Circle` | Rounded circle | Radius | Burst, renamed |
| `Ring` | The circle's outermost band, one tile thick | Outer radius (an inner radius setting comes when content needs a thicker band) | New -- Ring of Lava |
| `Line` | A ray from the anchor along the direction | Length | Line (today measured with Range) |
| `Cone` | Tabletop cone (width equals distance) | Length | Cone (today measured with Range) |
| `Wall` | A line one tile thick across the direction, centred on the anchor; an odd length is centred exactly | Length | New -- Wall of Fire |
| `Square` | Square centred on the anchor | Half-side | New |
| `Cross` | Plus sign centred on the anchor | Arm length | New |
| `Sweep` | The adjacent tiles on the facing side (three for a 1x1) | -- | New -- the Strike/Sweep melee TODO |

- Shapes stay a [Flags] set so they still combine by union (Adjacent | Self).
- A `Wall` across a diagonal aim is a diagonal line of tiles touching at corners. Nothing slips between them: a diagonal
  step needs at least one of its two flanking tiles open (`MovementCandidates.IsDiagonalMoveClear`), so the same rule
  holds for a lasting wall of this shape.
- Migration: Line and Cone content moves its `Range` into `AreaSize`, keeping `Range` as how far away it may be aimed.

### 5. Walls -- three rules

Each rule is named for the one thing it governs, so none shares a name with a path or a hit count.

| Setting | Options | Governs |
|---|---|---|
| `AimLineOfSight` | `NotRequired` (default), `Required` | Whether the caster must be able to see the aimed tile to aim there. Affects `ResolveReachable` and the highlight. Applies to every path. |
| `PathWalls` | `StopsAtWall` (default), `PassesThroughWalls`, `BouncesOffWalls(N)` | What a `Projectile` or `Lobbed` path does on reaching a wall: stops on the last open tile before it, goes on through it, or reflects up to N times (DCSS lightning). `Placed` has no path to read it. |
| `ShapeLineOfSight` | `NotRequired` (default), `Required` | Whether each tile of the shape must be visible from the shape's centre (its anchor) -- with `Required`, a wall shadows the shape's tiles behind it (D&D's clear path, ToME's balls). Applies to every shape. |

- The user's example: `Placed` path, `Circle` shape, `AimLineOfSight: Required` -- no path, but it cannot be aimed through
  a wall. Add `ShapeLineOfSight: Required` and the circle also does not reach around a corner.
- A wall tile inside a shape: entity effects find nobody there, and terrain effects leave it alone -- Ring of Lava turns
  floor into lava, never a wall. Changing structures waits for destructible terrain.
- **Line of sight** is one function in Engine.Tiles over a wall query the caller passes in (walls are structures on
  `Map`, per `MapLayer`), and it is the same rule "Field of view and perception" (TODO.md, High) uses, so what you see
  and what you can target never disagree. The rule is symmetric shadowcasting, and sight never passes between two
  walls that touch only at their corners (see "Line of sight -- the options considered").
- Defaults reproduce today exactly (neither line of sight required); `StopsAtWall` is the default for every path that
  reads it, because a path that ignores walls should be the deliberate choice.

### 6. Ranges

| Setting | Meaning | Default |
|---|---|---|
| `MinimumRange` | The aimed tile must be at least this far from the caster's footprint; with anchor `Caster`, shape tiles nearer than this are left out; a Projectile meets nothing nearer than this (it arms after that distance). | 0 |
| `MaximumRange` | Today's `Range`: the farthest the aimed tile may be; for a Projectile with path end `AtMaximumRange`, how far it flies. | -- |
| `AreaSize` | The shape's size (table above). | 0 |

- All three are rounded circles from the nearest footprint tile (`GridDistance.IsWithinRoundedRadius`).
- Aimable tiles become a band, MinimumRange to MaximumRange -- the highlight shows exactly that.
- Scroll scaling (`ScrollScalingEffects`) scales MaximumRange and AreaSize, as today; MinimumRange is never scaled.
- Target mode's clamp (`TargetResolution.ClampToRange`) keeps a marked target inside the band, not just inside the maximum.

## Travel time (later, planned for now)

Nothing travels yet. Build the pieces so it can:
- **One tracer.** A `PathTracer` (Engine.Tiles, pure) walks a path one tile at a time and reports each step -- the tile,
  whether it is a wall, and lets the caller say "stop here", "bounce" or "go on". Resolution with no travel time runs it to the end in
  one call; a projectile entity later runs it a few steps a frame. Hit counts, wall rules and minimum range are written
  once, against the tracer.
- **Speed** on the path (tiles per second, 0 = arrives the moment it is released). `Placed` has no speed.
- **A projectile is an entity**, like an aura anchor: it carries the `TargetSelection`, the tracer's state (tile,
  direction, distance flown, entities already hit, bounces left), the activation's source and what it will apply. A
  system advances them every frame and applies effects as the tracer reports hits. It is never tiered: a projectile
  outside Local resolves at once (the same choice as "Simplify non-local combat").
- **What is fixed when.** Direction and aimed tile are fixed at release (the end of the windup for Delayed). Who is hit is
  decided as it flies, against where entities are then. Stat modifiers are read when an effect applies, as everywhere
  else. A Lobbed shot's landing tile is fixed at release and telegraphed until it lands, so a target can step out.
- **Target mode** aims at the marked entity's tile at release; it does not home. Homing is a later path, not part of this.
- **Dodge** is checked when a projectile reaches an entity, not at release.
- **The caster** may die while it flies; the projectile goes on, credited to the snapshot `ActionSource` it carries. The
  tiles left out are the ones the caster released from; one that moves into the landing area is hit (see "The caster's
  own tiles").
- **Presentation** draws a projectile sprite moving along the path (a sprite name on the path) and, for Lobbed, the landing
  telegraph.

## Phases

### Phase 1 -- The new spec, nothing changes in play
- `TargetingSpec` gains `Path`, `PathEnd`, `ProjectileHitCount`, `Anchor`, `AimLineOfSight`, `PathWalls` (with bounce
  count), `ShapeLineOfSight`, `MinimumRange`; `Range` is renamed `MaximumRange`; `Burst` is renamed `Circle`. Codec keys added
  with defaults, so existing records still read; the master manifest is rewritten (Line and Cone `Range` into `AreaSize`,
  anchors set) and the exporter shows it canonical.
- The resolver takes the anchor and size per the tables; MinimumRange works for aim, highlight and clamp.
- Tests: every current action resolves to the same tiles as before; MinimumRange band.

### Phase 2 -- New shapes, with Wall of Fire and Ring of Lava
- `Wall`, `Ring`, `Square`, `Cross`, `Sweep`, `None` in the resolver, each with tests.
- Wall of Fire: `Placed`, `Wall`, range 10, length 9 (centred on the aimed tile), 10 fire damage and 10 Burning stacks to
  each entity in it.
- Ring of Lava: `Placed`, `Ring`, turns each tile's terrain into lava. Needs a per-tile placement for effect entries
  (`IEffectEntry.Placement` gains `OnEachTile`) and a terrain-change entry that goes through the announced path
  (`TerrainChangedEvent`), so contact and the aura field follow. This is also the terrain-change test.
- Both bound on the hotbar for testing, like Lightning Bolt and Fire Fan.

### Phase 3 -- Walls
- Line of sight in Engine.Tiles over a wall query: symmetric shadowcasting for a whole area, its matching line test for
  one pair, and no sight between two walls that touch only at their corners. `AimLineOfSight`, `ShapeLineOfSight` in the
  resolver, the highlight and NPC checks. Shared with field of view.

### Phase 4 -- Paths and hit counts, with no travel time
- `PathTracer`; `Projectile` and `Lobbed`; `ProjectileHitCount`; `PathWalls`; path end. The preview and telegraph show
  where a path stops.
- Content: Magic Missile becomes a `Projectile` with hit count `1`, Fireball a `Projectile` with hit count `1` and a
  `Circle`, Lightning Bolt a `Projectile` with hit count `All`. Every action and item today is test content, so changing
  how one plays is expected.

### Phase 5 -- Travel time (when wanted)
- Projectile entities, speed, the flight system, Lobbed landing telegraph, projectile sprites.

## Decided (2026-10-30)

1. **Ring thickness:** one tile thick; an inner radius setting when content needs a thicker band.
2. **Ring of Lava and walls:** walls are never turned into lava; only floor tiles change.
3. **Wall length:** Wall of Fire is length 9, centred on the aimed tile.
4. **Diagonal walls:** no gap filling. An entity can step diagonally only when at least one of the two flanking tiles is
   open, which `MovementCandidates.IsDiagonalMoveClear` already enforces, so a diagonal line of wall tiles cannot be
   slipped through.
5. **Tiny and Phasing entities** never stop or count toward a projectile: they are too hard to hit accurately; an area
   action is how to reach them.
6. **MinimumRange** is never scaled; Scroll Mastery scales MaximumRange (and AreaSize) only.
7. **Projectiles:** Magic Missile (hit count `1`), Fireball (hit count `1` with a `Circle`) and Lightning Bolt (hit count
   `All`) become projectiles in Phase 4. Every current action and item is test content.
8. **Line of sight:** symmetric shadowcasting, with walls as full tiles, and sight never passes between two walls that
   touch only at their corners. Shared by aiming, shape spread and field of view. Why: "Line of sight -- the options
   considered", below.
9. **Walls reach the ceiling:** nothing passes over one, Lobbed included.
10. **Splash over your own tiles:** a shape at `PathEnd` that covers the caster's tiles hits the caster -- aiming a
    splash that overlaps you is aiming at yourself. Only the path and caster-anchored shapes leave the caster's tiles
    out, as today's Burst already does.
- **Immunity counts as a hit** for the hit count (see "Hit count").
- **The caster's own tiles** at release are never affected; a caster can be caught on purpose (Ground mode onto its own
  tiles, or a splash aimed to cover them) or by moving into its own attack (see "The caster's own tiles").

No questions are open.

## Line of sight -- the options considered

Line of sight answers "can a straight, unobstructed line be drawn from this tile to that one",
where walls fill their whole tile. It is used three ways here (aiming, a shape spreading from its anchor, and later
field of view for fog of war and NPC sight), and all three must use the same rule, or the player sees a goblin they
cannot target, or a goblin shoots a player who cannot see it. The options differ in where the line is allowed to
start and end within a tile, which decides who sees around corners and whether sight is mutual:

| Rule | How it decides | Symmetric (A sees B exactly when B sees A) | Around corners and pillars | Cost |
|---|---|---|---|---|
| **Bresenham, centre to centre** | One grid line from A's centre to B's centre; blocked if it crosses a wall tile | No -- the line from A to B and from B to A can take different tiles, so one sees and the other does not | Strict and uneven: some tiles beside a pillar are hidden for no visible reason | Cheapest per pair |
| **Permissive field of view** | Visible if *any* straight line from *anywhere* in A's tile reaches *anywhere* in B's tile | Yes | Very generous: you see far down past a corner and around pillars | Expensive; awkward to compute for a whole area |
| **Symmetric shadowcasting** (Albert Ford, 2021) | Visible if a line from A's *centre* reaches *anywhere* in B's tile, computed for a whole area at once by sweeping each eighth of the circle | Yes, for floor tiles | In between: walls cast clean shadows, a pillar hides a wedge behind it, a corridor shows its walls | Cheap for a whole area at once; a single pair can use a matching line test |
| **Precise permissive** | Like permissive, computed exactly | Yes | Generous | Expensive |

Things that matter here:
- **Symmetry is fairness.** NPCs will use the same rule to decide what they can see and shoot (field of view, NPC
  decisions). With an asymmetric rule, an enemy can hit you from a tile you cannot see or target back.
- **Whole-area cost matters most.** Aiming needs the caster's view of every tile in range once per arm (cached until
  the caster moves), fog of war needs it for the player every move, and NPCs need it often. Shadowcasting computes a
  whole area in one sweep; the pairwise rules repeat work for every tile.
- **Corners.** Two walls touching only at their corners: sight between them should match movement, which cannot pass
  a diagonal gap whose flanking tiles are both walls -- sight should not either, or a player sees and targets
  somewhere no one can walk.
- **Pillars.** A lone pillar should hide something standing right behind it, not the whole room behind it.

Chosen: **symmetric shadowcasting**, with walls as full tiles and a diagonal gap closed when both flanking tiles are
walls. It is symmetric, cheap for a whole area, gives the most readable shadows, and is the common choice in recent
roguelikes. A single tile-to-tile check (an NPC asking about one target) uses the same rule's line test, so it
agrees with the area result.
