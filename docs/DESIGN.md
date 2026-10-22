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
- **Draw:** the duel has a turn limit (value not yet decided). If nobody has breached the other's main LoW by then, it is a draw. Stalemates are considered undesirable and should be mitigated wherever possible.

## 2. Turn structure

Each turn:

1. **Frozen time.** Time is paused while both players choose.
2. Each player is given a menu of available actions and chooses one **without knowing the other's choice**.
3. Choices are locked, time resumes for one tick, and the tick resolves (section 3).
4. The next turn begins.

**Decision timer.** There will be a time limit for deciding within a turn. It is flexible and can be configured by the players before the duel starts.

**Duel start.** Both players start by working on a defence (see section 6); the game turn tracker begins after that. (Note: the Notion "Structure of the game" page only contains this one line; the details in this document come from conversation.)

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
| Damage | 2 |
| Movement speed | 0 (Notion value, appears to be a placeholder) |
| Modifier | +1 damage / -1 damage |
| Time to draw | movement speed / frequency (see below) |

**Features**
- The main offensive line. Drawn as a sine wave; once complete it moves ("shoots out") each tick at its speed until it hits an obstacle.
- Most chalklings are easily destroyed by a LoV.
- On hitting a LoW it creates a pockmark; enough hits to the same point can breach the line.
- Reflects off LoF.
- Behaves like a light wave: reflects off LoF and refracts on a change of medium.
- Frequency trade-off: higher-frequency LoV penetrate better, lower-frequency LoV transfer more energy (for example displacing a LoW). A tighter curve (more wave in less area) is harder to draw but stronger.
- **Draw time (latest clarification):** a LoV takes a number of turns equal to *movement speed / frequency*, where frequency is directly tied to movement speed and higher frequency means longer to draw. *Note: the Notion Lines database still says "1 / movement"; it has not been updated.*

### 4.4 Line of Making (LoM)

Creates chalklings (autonomous units).

| Stat | Value |
|---|---|
| Damage | 0 |
| Movement speed | 0 |
| Durability | 1 + (number of bind points) x 2 |
| Modifier | +1 to a stat of choice / -1 to the highest stat |
| Time to draw | 1 per stat buff + 1 per glyph |

**Stat specification.** When the player starts a LoM they provide their stat specifications (the stats the chalkling will have). That specification determines how long the drawing takes. How stats are represented in play is still to be fleshed out.

**Glyphs** are instructions given to a chalkling once complete. A chalkling may have at most **one glyph per 3 stat buffs**.

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

- **Rush:** drawing time reduced by 50%, **rounded up**. Odds change to P(-ve) = 1/4, P(2x -ve) = 1/12.
- **Steady:** drawing time increased by 50% (rounding not yet specified). Odds change to P(-ve) = 1/12, P(+ve) = 1/3, P(2x +ve) = 1/12.

Notes: because rush rounds up, a 1-turn drawing is not shortened by rushing (only the modifier odds worsen). The Notion page lists the rush and steady odds as written above; whether the "-ve" figures are inclusive of the "2x -ve" case is not stated.

## 6. Defences

A **defence** is the arrangement of lines a player draws around their main LoW. It should be thought of as free-form: the defences from the novel exist only as presets to help new players, and players are expected to experiment with their own layouts.

### 6.1 Bind point framework

All defences use the same structure for bind points:

1. Place a triangle around the Line of Warding. Its vertices determine the bind points.
2. Bind points are placed where the **edges of the triangle** and the **perpendiculars extended from each vertex** intersect the LoW's circumference (where such intersections exist).
3. This framework gives a **maximum of 12** possible bind points (3 edges and 3 perpendiculars, each crossing the circumference at up to 2 points).

Common cases: an ellipse has 2 bind points (at its tightest curves), and the standard circle shapes have 4, 6 or 9. An elliptical LoW is strong near its bind points and weak along its sides, so attachments should not touch the sides.

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
| Nine-point circle | 9 | Easton, Hill, Shoaff, Taylor |
| Ellipse | 2 | Jordan, Osborn |

Main LoW draw time is bind points div 2 (1 turn for an ellipse, 2 for four-point, 3 for six-point, 4 for nine-point). Everything attached is drawn on top of that. The Notion Defences page includes a diagram for each preset.

### 6.4 Four-point circles

**Ballintain** (simple, quick to set up, little internal support)
- Two LoF, each joining a pair of neighbouring bind points.
- A circular LoW at each of two opposite bind points.
- One defensive chalkling bound to one of the two remaining bind points.

**Sumsion**
- A long LoF lying tangent to the front bind point.
- A smaller circle with a Mark's Cross bound at that front bind point, on the side facing away from the main LoW. *(Interpretation of an ambiguous source; to be confirmed.)*
- Defensive chalklings bound to the bind points on either side.
- A line crossing the rear bind point perpendicular to the curve as an anchor.

### 6.5 Six-point circles

Six bind points at the corners of a regular hexagon within the circle. More versatile and sturdier than two- or four-point circles.

**Eskridge**
- Three internal LoF, each joining two neighbouring bind points, leaving three open gaps to draw in.
- Defensive chalklings at the top and bottom bind points.
- Circular LoW at the remaining bind points, each with an internal LoF pointing at the opponent to help turn aside LoV.

**Matson** (chalkling-heavy)
- Two parallel LoF, each joining a pair of neighbouring bind points.
- The two remaining bind points (opposite each other) each carry a circular LoW with a Mark's Cross.
- A defensive chalkling at every bind point across all three circles except where the small circles join the large one: ten chalklings in total.

### 6.6 Nine-point circles

Nine bind points derived from a triangle: the midpoints of its sides plus the points where its altitudes meet the circle.

**Easton**
- A circular LoW at every bind point.
- LoF forming a nine-sided outline with three sides missing, supporting the central circle.
- Drawbacks: the cost of a nine-point circle, and the LoF restrict the player's own chalklings.
- Variants: add defensive chalklings to the outer circles. An advanced version adds a Mark's Cross to each outer circle, cuts the internal LoF from six to three, and binds chalklings to some outer bind points.

**Hill**
- Uses LoF, but can be adapted to work without them. No further detail available.

**Shoaff**
- Elliptical LoW at each bind point, with a defensive chalkling bound at the opposite end of each ellipse.
- Only two short LoF, as anchors, so it is vulnerable to LoV.
- Strong against chalkling-based offence; suits LoV-focused players.

**Taylor**
- Two concentric circular LoW.
- LoF radiate outward from the inner circle's bind points, through the outer circle's bind points, and on through two smaller circles, each with a Mark's Cross.
- Further LoF join neighbouring outer circles, parallel to the radiating ones, to reflect LoV.
- Defensive chalklings bound to many of the remaining bind points.
- A very large investment of drawing time.
- If the **outer** concentric circle is breached, that counts as a loss.

### 6.7 Ellipses

**Jordan**
- A large cage of LoF surrounds the defence.
- Many offensive chalklings are drawn inside and released in waves by dismissing the front LoF, which is redrawn once they have moved out.
- A line through each of the two bind points acts as an anchor.

**Osborn**
- The only standard ellipse-based preset.
- A defensive chalkling at the upper bind point.
- A single anchor line through the rear bind point.
- Optionally, a circular LoW with a Mark's Cross on each side. These must not touch the ellipse, since there are no bind points on its sides.

## 7. Open questions and to-do

**Pending data entry (reminder requested)**
- Fill in the intentionally blank stats (damage, durability, movement speed and similar fields on each line) once the overarching design themes and issues are pinned down. The user asked to be reminded of this at that point.

**Undecided numbers and rules**
- The turn limit for a duel, and how to mitigate stalemates.
- Default and permitted range for the per-turn decision timer.
- Rounding rule for the steady option (+50%).
- Whether the rush/steady "-ve" odds include the "2x -ve" case.
- How chalkling stats are represented in play (stat specification, movement, damage).

**Questions raised but not yet answered**
1. **Rush on 1-turn drawings.** Rounding up means rushing a 1-turn drawing has no time benefit, only worse modifier odds. Intended?
2. **Dismissing a LoF.** Dismissals resolve before LoV movement, so a LoV arriving in the same tick that a dismissal completes passes through. Intended?
3. **Multi-object paths.** If a LoV crosses several objects in one tick, does it resolve each in order along its path, and does it continue along the reflected path after a reflection?
4. **Bind point geometry.** Is the triangle chosen by the player when drawing the LoW, or are bind points precomputed?
5. **Defence validity.** With free-form defences, is any layout illegal, or do drawing time and cost naturally constrain what people build?
6. **Presets for new players.** Which of the novel's defences ship as presets, and how thin sources (Hill) are filled in.

**Notion sync status**
- The latest decisions (section 2 turn structure, section 3 resolution order, information model, win/draw conditions, rush rounding, free-form defences and the bind point framework) have **not yet been written to Notion**. An attempted update did not go through (no approval received). Planned Notion edits: expand the "Structure of the game" page; add the bind point framework to the top of the Defences page and strip story-flavoured phrasing from it; update the rush rounding on the "Start drawing" page; update the LoV draw-time formula in the Lines database.
- Notion hierarchy to preserve: Rithmatic duels > { Lines (database; Line of Warding > Defences), Actions (database; Add to drawing > {Buff LoM, Repair LoW / LoF}), Structure of the game }.
