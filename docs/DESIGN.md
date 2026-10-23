# Rithmatic Duels: Design Document

A two-player, simultaneous-choice strategy duel game, loosely in the spirit of Pokémon-style duels, built on the chalk-drawing magic system from *The Rithmatist*. Every action takes time to complete, and both players commit to actions each turn without knowing what the other has chosen.

This document consolidates the design notes held in Notion (under the top-level "Rithmatic duels" page: a **Lines** database, an **Actions** database, a **Structure of the game** page, and a **Defences** page nested under Line of Warding) plus decisions made in conversation since. Where the two differ, this document reflects the latest decisions and the difference is flagged.

**Scope notes**
- All numbers are placeholders to be tuned through balancing.
- Only gameplay-relevant material belongs here. Story and lore from the novel are out of scope.
- Only four lines exist in the game (Warding, Forbiddance, Vigor, Making). The novel's Line of Silencing and Line of Revocation have no duelling relevance and are excluded.
- Older Notion material (a 2023 "Game development hub" / "Rithmatism" page) is deliberately ignored.

---

## 1. Core concept

- Two players duel by drawing chalk lines. Lines either defend, attack, or create autonomous units (chalklings).
- Every drawing takes a number of **turns** to complete. There is a single unit of time (the tick, equal to one turn); different actions simply cost different numbers of turns.
- A player can only have **one drawing in progress** at a time, so what to commit time to is the central decision.
- **Win condition:** a player loses if their *main Line of Warding* is breached.
- **Turn limit:** default 60 turns, configurable per duel from 30 to 120 before it starts. The turn count starts on the first turn after **both** main LoW are complete; setup turns are not counted or capped.
- **End of the duel:** if nobody has breached the other's main LoW by the turn limit, a tiebreak decides it (section 2.1). Only an exact tie is a draw.
- **Double breach:** if both main LoW are breached in the same tick, the duel is a draw. *(Default proposed in conversation, not explicitly confirmed.)*
- Stalling needs no special rule: with a turn limit and a tiebreak, mutual stalling benefits nobody, so at least one player is making a mistake by doing it.

### 1.1 The board

The board is **continuous 2D**, with snapping to help placement.

- Lines are real shapes: a LoW is a circle (centre and radius) or an ellipse, a LoF is a straight segment, a LoV travels along a direction vector.
- **Snapping (default mode):** a player's selections snap to bind points and to cardinal directions where applicable. For example, a LoF drawn on the circumference of a LoW snaps to being a tangent.
- **LoV aim is free:** a LoV can be angled in any direction, never snapped.
- **Free hand mode:** holding a key turns snapping off, so lines can be placed wherever the player wishes. Lines that are straight (such as LoF) are still drawn straight.
- Collision is computed geometrically (segment, circle and ellipse intersection), so reflections off LoF use the true angle.

## 2. Turn structure

Each turn:

1. **Frozen time.** Time is paused while both players choose.
2. Each player is given a menu of available actions and chooses one **without knowing the other's choice**.
3. Choices are locked, time resumes for one tick, and the tick resolves (section 3).
4. The next turn begins.

**Decision timer.** Each turn has a time limit for choosing, configured by the players before the duel starts: default 30 seconds, settable from 10 to 120 seconds, or off (for hotseat and practice against the AI; online duels require a value). On timeout, the player's action is **Continue drawing** if a drawing is in progress, otherwise **Pass the turn**.

**Duel start.** Both players start by working on a defence (see section 6). The turn count begins once both main LoW are complete. Players can pick a saved defence preset (section 6.8) so they do not have to select each line every duel. (Note: the Notion "Structure of the game" page only contains the first line; the details in this document come from conversation.)

### 2.1 End game: chalk fatigue and tiebreak

The end game is pressured in two ways, similar to tiebreaks and overtime pressure in games like Clash Royale.

- **Chalk fatigue:** during the last 15 turns of the duel, repairs are not allowed, and every main LoW loses 1 durability every 3 turns.
- **Tiebreak:** if the turn limit is reached with no breach, the player whose main LoW has the higher share of its maximum durability left wins. An exact tie is a draw.

### Information

- Players can **see what the opponent is drawing in progress**, but not its exact specifications.
- They must infer as much as they can from what is visible on the ground and from knowledge of the lines and tactics.
- Defence choice is not announced. A player learns the opponent's defensive strategy only from what has been drawn.

## 3. Tick resolution order

While players choose, time is frozen. LoV that have been drawn and are not yet destroyed will each move a distance determined by their speed during the tick. When time resumes, the following happen in order:

1. **Completion and dismissal.**
   - Any LoF that is being dismissed and completes dismissal this tick disappears instantly.
   - Any LoF, LoW or LoM that finishes drawing this tick (including repairs to such lines) gains its durability.
2. **Movement and attacks.** Any actions taken by LoV or LoM (chalklings) take place. These include movement and attacking.
3. **Damage.** Damage from collisions that occurred this tick is processed:
   - **LoV hits LoW:** damages the LoW at the LoV's current position.
   - **LoV hits LoF:** the LoV is reflected according to the laws of reflection for EM waves.
   - **LoM hits LoW / LoF / LoM:** an attacking LoM deals its damage to the line at its current position.
   - Any line left with no durability loses effect.

Consequence worth noting: because dismissal (step 1) resolves before movement and attacks (step 2), a LoF whose dismissal completes in a tick no longer blocks a LoV arriving in that same tick.

## 4. Lines

Four line types. Each completed drawing receives a random modifier (section 5). Stat field names below match the Notion "Lines" database; fields marked *blank* are intentionally empty for now.

### 4.1 Line of Warding (LoW)

| Stat | Value |
|---|---|
| Durability | 12 |
| Modifier (+ve / -ve) | +2 durability / -2 durability |
| Damage, movement speed | blank |

**Time to draw**
- Main LoW: (number of bind points) div 2 (integer division). So 2 bind points = 1 turn, 4 = 2, 6 = 3, 9 = 4.
- Mark's Cross: 1 turn.

**Features**
- The duel is lost if the *main* LoW is breached.
- A LoW is **fixed in place** once constructed. Nothing, including a LoV, can move it.
- An elliptical LoW has its durability scaled by the steepness of its curvature (section 6.1).
- Provides bind points for LoF and defensive chalklings.
- Any subsequent LoW must be bound to the main LoW.
- Strength depends on evenness and curvature: steeper curvature means a stronger line. Circles have even curvature all the way round. Ellipses are strongest at their bind points and weakest along their sides.
- A larger LoW is weaker per unit, though it takes more total force to destroy a given fraction of a large circle than of a small one.
- A typical LoW can take about 6 hits from a LoV.
- Defensive layouts are covered in section 6.

### 4.2 Line of Forbiddance (LoF)

| Stat | Value |
|---|---|
| Durability | 3 + (number of bind points) x 3 |
| Modifier | +2 durability / -2 durability |
| Time to draw | 4 - (number of bind points), minimum 1 |

**Features:** reflects LoV. LoF also act as anchors and structural supports within defences. LoF can be dismissed (action costing 2 turns). A *Mark's Cross* is a small four-point circle containing a crossed pair of LoF, used to brace a larger circle.

### 4.3 Line of Vigor (LoV)

| Stat | Value |
|---|---|
| Damage, movement speed, time to draw | set by frequency tier (below) |
| Modifier | +1 damage / -1 damage |

**Frequency tiers.** When starting a LoV the player picks a frequency tier. Higher frequency means a faster line that takes longer to draw and carries more energy, so it deals more damage on impact.

| Tier | Time to draw (turns) | Speed (units per tick) | Damage |
|---|---|---|---|
| Low | 1 | 2 | 1 |
| Medium | 2 | 4 | 2 |
| High | 3 | 6 | 3 |

All tier values are placeholders and live in data. The tier system should be built so it can later be replaced by a continuous frequency slider (see `docs/FUTURE_IDEAS.md`).

**Features**
- The main offensive line. Drawn as a sine wave; once complete it moves ("shoots out") each tick at its speed until it hits an obstacle.
- Can be aimed at any angle (no snapping).
- Most chalklings are easily destroyed by a LoV. A LoV can push a chalkling (LoM) it hits; it can never move a LoW.
- On hitting a LoW it creates a pockmark; enough hits to the same point can breach the line.
- Behaves like a light wave: reflects off LoF and refracts on a change of medium.
- *Supersedes the earlier formula "time to draw = movement speed / frequency", which contradicted the intent that higher frequency takes longer. The Notion Lines database still says "1 / movement" and needs updating.*

### 4.4 Line of Making (LoM)

Creates chalklings (autonomous units).

A chalkling has three stats: **Durability**, **Damage** and **Speed**. When the player starts a LoM they give its stat specification by spending stat buffs, one at a time, on any of the three. The specification sets how long the drawing takes.

| Stat | Base | Per stat buff |
|---|---|---|
| Durability | 1 | +2 |
| Damage | 0 | +1 |
| Speed (units per tick) | 0 | +1 |

| Rule | Value |
|---|---|
| Durability when bound | +2 per bind point the chalkling is bound to (binding gives free durability) |
| Modifier | +1 to a stat of choice / -1 to the highest stat |
| Time to draw | 1 per stat buff + 1 per glyph (minimum 1) |

**Binding.** A chalkling can be bound to a bind point. A bound chalkling cannot move further than a set tether distance (placeholder, in data) from its bind point, and gains free durability as above.

More stats (such as range or attack speed) and fixed archetypes for teaching and fast duels may be added later; see `docs/FUTURE_IDEAS.md`.

**Glyphs** are instructions given to a chalkling once complete. A chalkling may carry **1 glyph for free, plus 1 more per 3 stat buffs**. (So the cheapest defensive chalkling is 0 buffs + the Defend glyph = 1 turn.)

| Glyph | Shape | Effect |
|---|---|---|
| Go | Arrow | Travel in the arrow's direction |
| Attack | "X" | Attack |
| Wait | Horizontal line | Wait |
| Turn | Acute angle with arrow | Turn in the arrow's direction |
| Defend | Diamond | Bound chalklings patrol around the bind point they are tethered to and attack any chalkling not bound to the same circle |

A chalkling with the Defend glyph and bound to a bind point is a **defensive chalkling**.

## 5. Actions and modifiers

### 5.1 Action menu

Each turn a player picks one of these (availability depends on state):

| Action | Rules |
|---|---|
| **Start drawing** | Only available with no drawing in progress. Choose one of the 4 line types. Optionally **rush** or **steady** (below). |
| **Continue drawing** | Advance the current drawing. Uses the action for the turn. |
| **Cancel drawing** | Abandon the current drawing and lose all progress. Uses the action for the turn. |
| **Add to drawing** | Buff LoM: 2 turns per stat buff. Repair LoW / LoF: 2 turns per point of durability. |
| **Dismiss LoF** | Duration 2 turns. Select one LoF to dismiss. |
| **Pass the turn** | Do nothing. |

### 5.2 Completion modifier

When a drawing completes, a modifier is applied at random. The base odds are P(-ve) = 1/8, P(+ve) = 1/8, otherwise no modifier.

When starting a drawing the player may choose:

- **Rush:** drawing time reduced by 50%, **rounded up**. **Exception:** rushing a 1-turn drawing makes it complete **immediately**, in the same tick it is started (step 1 of section 3).
- **Steady:** drawing time increased by 50%, **rounded up** (1 → 2, 2 → 3, 3 → 5, 4 → 6), so steady always costs at least one extra turn.

Each figure below is a **separate outcome**; "none" takes the remainder.

| Mode | 2x -ve | -ve | None | +ve | 2x +ve | Expected modifier |
|---|---|---|---|---|---|---|
| Base | 0 | 1/8 | 3/4 | 1/8 | 0 | 0 |
| Rush | 1/6 | 1/4 | 7/12 | 0 | 0 | -7/12 |
| Steady | 0 | 1/12 | 5/12 | 1/3 | 1/6 | +7/12 |

Rush can never give a positive modifier. Steady keeps a slim chance of a negative one. These choices should feel impactful: players should feel the weight of the risk. The double-effect odds were raised from 1/12 to 1/6 for that reason (see the balance watchlist in section 7).

## 6. Defences

A **defence** is the arrangement of lines a player draws around their main LoW. It should be thought of as free-form: the defences from the novel exist only as presets to help new players, and players are expected to experiment with their own layouts.

### 6.1 Bind point framework

**Elliptical LoW**
- Always has exactly **2 bind points**, at the two points where its curvature is greatest.
- Its durability is scaled by the steepness of that curvature. It is strong near its bind points and weak along its sides, so attachments should not touch the sides.

**Circular LoW**
1. The player places the three vertices of a triangle around the LoW circle.
2. One set of bind points is placed where the **edges of the triangle** intersect the circumference.
3. More bind points are placed where the **altitude through each vertex, extended to a full line**, intersects the circumference.
4. This gives a **maximum of 12** bind points (3 edges and 3 altitude lines, each crossing the circle at up to 2 points).

A set of default bind-point presets gives the standard circles (4, 6 and 9 points) used by the defences below. Players can also create and save their own triangle constructions as presets.

### 6.2 Vocabulary

- **Bind point:** a spot on a LoW where lines or chalklings can attach.
- **Mark's Cross:** small four-point circle holding a crossed LoF pair, used to brace a larger circle.
- **Defensive chalkling:** chalkling with the Defend glyph, bound to a bind point.
- **Anchor line:** a line crossing a bind point perpendicular to the curve, to hold the LoW in place.

### 6.3 Preset overview

| Base shape | Bind points | Presets |
|---|---|---|
| Four-point circle | 4 | Ballintain, Sumsion |
| Six-point circle | 6 | Eskridge, Matson |
| Nine-point circle | 9 | Easton, Shoaff, Taylor |
| Ellipse | 2 | Jordan, Osborn |

Main LoW draw time is bind points div 2 (1 turn for an ellipse, 2 for four-point, 3 for six-point, 4 for nine-point). Everything attached is drawn on top of that. Diagrams below are copied from the Notion Defences page (images in `docs/images/defences/`). Defences too thinly described in the novel to draw (such as Hill) have been removed.

### 6.4 Four-point circles

<img src="images/defences/four-point-circle.png" alt="Four-point circle bind points" width="260">

**Ballintain** (simple, quick to set up, little internal support)
- Two LoF, each joining a pair of neighbouring bind points.
- A circular LoW at each of two opposite bind points.
- One defensive chalkling bound to one of the two remaining bind points.

<img src="images/defences/ballintain.png" alt="Ballintain defence" width="260">

**Sumsion**
- A long LoF lying tangent to the front bind point.
- A smaller circle with a Mark's Cross bound at that front bind point, on the side facing away from the main LoW. *(Interpretation of an ambiguous source; to be confirmed.)*
- Defensive chalklings bound to the bind points on either side.
- A line crossing the rear bind point perpendicular to the curve as an anchor.

<img src="images/defences/sumsion.png" alt="Sumsion defence" width="260">

### 6.5 Six-point circles

Six bind points at the corners of a regular hexagon within the circle. More versatile and sturdier than two- or four-point circles.

<img src="images/defences/six-point-circle.png" alt="Six-point circle bind points" width="260">

**Eskridge**
- Three internal LoF, each joining two neighbouring bind points, leaving three open gaps to draw in.
- Defensive chalklings at the top and bottom bind points.
- Circular LoW at the remaining bind points, each with an internal LoF pointing at the opponent to help turn aside LoV.

<img src="images/defences/eskridge.png" alt="Eskridge defence" width="260">

**Matson** (chalkling-heavy)
- Two parallel LoF, each joining a pair of neighbouring bind points.
- The two remaining bind points (opposite each other) each carry a circular LoW with a Mark's Cross.
- A defensive chalkling at every bind point across all three circles except where the small circles join the large one: ten chalklings in total.

<img src="images/defences/matson.png" alt="Matson defence" width="260">

### 6.6 Nine-point circles

Nine bind points derived from a triangle: the midpoints of its sides plus the points where its altitudes meet the circle.

<img src="images/defences/nine-point-circle.png" alt="Nine-point circle construction" width="260">

**Easton**
- A circular LoW at every bind point.
- LoF forming a nine-sided outline with three sides missing, supporting the central circle.
- Drawbacks: the cost of a nine-point circle, and the LoF restrict the player's own chalklings.
- Variants: add defensive chalklings to the outer circles. An advanced version adds a Mark's Cross to each outer circle, cuts the internal LoF from six to three, and binds chalklings to some outer bind points.

<img src="images/defences/easton.png" alt="Easton defence" width="260">

**Shoaff**
- Elliptical LoW at each bind point, with a defensive chalkling bound at the opposite end of each ellipse.
- Only two short LoF, as anchors, so it is vulnerable to LoV.
- Strong against chalkling-based offence; suits LoV-focused players.

<img src="images/defences/shoaff.png" alt="Shoaff defence" width="260">

**Taylor**
- Two concentric circular LoW.
- LoF radiate outward from the inner circle's bind points, through the outer circle's bind points, and on through two smaller circles, each with a Mark's Cross.
- Further LoF join neighbouring outer circles, parallel to the radiating ones, to reflect LoV.
- Defensive chalklings bound to many of the remaining bind points.
- A very large investment of drawing time.
- If the **outer** concentric circle is breached, that counts as a loss.

<img src="images/defences/taylor.png" alt="Taylor defence" width="260">

### 6.7 Ellipses

<img src="images/defences/ellipse.png" alt="Elliptical LoW bind points" width="260">

**Jordan**
- A large cage of LoF surrounds the defence.
- Many offensive chalklings are drawn inside and released in waves by dismissing the front LoF, which is redrawn once they have moved out.
- A line through each of the two bind points acts as an anchor.

<img src="images/defences/jordan.png" alt="Jordan defence" width="260">

**Osborn**
- The only standard ellipse-based preset.
- A defensive chalkling at the upper bind point.
- A single anchor line through the rear bind point.
- Optionally, a circular LoW with a Mark's Cross on each side. These must not touch the ellipse, since there are no bind points on its sides.

<img src="images/defences/osborn.png" alt="Osborn defence" width="260">

### 6.8 Saved defence presets

Players can save their own defences as presets and pick one at the start of a duel, so they do not have to select each line every time. The game ships default presets for the standard defences above. *Default, not yet confirmed: a preset only queues the drawings; each line still costs its normal drawing time, one drawing in progress at a time.*

## 7. Open questions and to-do

**Pending data entry (reminder requested)**
- Fill in the intentionally blank stats (damage, durability, movement speed and similar fields on each line) once the overarching design themes and issues are pinned down. The user asked to be reminded of this at that point.

**Balance watchlist (monitor closely in testing)**
- **Instant rush on 1-turn drawings** (section 5.2). The user is suspicious of the balance of this mechanic.
- **Double-effect odds of 1/6** for rush and steady (section 5.2). Check whether this feels too swingy.
- **Turn limit** default and range (section 1).
- **Chalk fatigue** length and rate, and the tiebreak (section 2.1).
- All placeholder numbers, including LoV tiers and chalkling stats.

**Questions raised but not yet answered**
1. **Dismissing a LoF.** Dismissals resolve before LoV movement, so a LoV arriving in the same tick that a dismissal completes passes through. Intended?
2. **Multi-object paths.** If a LoV crosses several objects in one tick, does it resolve each in order along its path, and does it continue along the reflected path after a reflection? *(Suggested default: yes to both.)*
3. **Defence validity.** With free-form defences, is any layout illegal, or do drawing time and cost naturally constrain what people build?
4. **Presets for new players.** Which of the defences below ship as presets for new players.
5. **Preset drawing time.** Does picking a saved defence preset still cost the normal drawing time for each line (current default), or is any of it skipped?
6. **Minimum LoV damage.** A Low tier LoV (damage 1) with a -1 modifier deals 0 damage. Allowed, or floor at 1?
7. **LoV pushing chalklings.** How far a LoV pushes a chalkling it hits, and whether the LoV is spent by the hit.
8. **Double breach.** Draw is the current default (section 1); confirm.

**Settled 2026-10-23:** board model, LoV frequency tiers, chalkling stats, glyph cap, binding, steady rounding, rush on 1-turn drawings, rush/steady odds, turn limit, decision timer, end-game pressure and the bind point rules. The earlier questions on rush on 1-turn drawings and bind point geometry are answered in sections 5.2 and 6.1.

**Notion sync status**
- The latest decisions have **not yet been written to Notion**: section 1 (board, turn limit, end of duel), section 2 (turn structure, timer, end game), section 3 resolution order, the information model, LoV frequency tiers, chalkling stats, glyphs and binding, rush/steady rounding and odds, free-form defences, the bind point rules and saved presets. Planned Notion edits: expand the "Structure of the game" page; add the bind point rules to the top of the Defences page and strip story-flavoured phrasing from it; update the rush/steady pages under Actions; update the LoV and LoM entries in the Lines database; remove Hill from the overview table on the Defences page (its section is already gone).
- Notion hierarchy to preserve: Rithmatic duels > { Lines (database; Line of Warding > Defences), Actions (database; Add to drawing > {Buff LoM, Repair LoW / LoF}), Structure of the game }.
