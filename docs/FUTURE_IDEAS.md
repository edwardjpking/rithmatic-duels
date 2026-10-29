# Rithmatic Duels: Future Ideas

Ideas to return to once the basic features are implemented. None of these are current rules; `docs/DESIGN.md` remains the source of truth. All numbers below are placeholders.

## Removed from the base game

Features that were built and then taken out. Keep them in mind for future developments.

- **Add to drawing** (removed 2026-10-25), because it would make duels go on too long:
  - **Repairing LoW and LoF:** 2 turns per point of durability, up to the line's durability at completion, and not during chalk fatigue. The Warder and Mender archetypes (section 4) were built around it.
  - **Buffing chalklings:** 2 turns per stat buff on one stat of a chalkling already on the board, applied as if drawn in at the start, with no modifier roll.
- **Glyph cap and extra glyphs** (removed 2026-10-25): see section 5.

## Priority order

Set by the user on 2026-10-24.

| Priority | Feature | Section |
|---|---|---|
| 1 | Tutorial | [Tutorial](#1-tutorial) |
| 2 | Practice mode | [Practice mode](#2-practice-mode) |
| 3 | Chalkling (LoM) archetypes | [Chalklings](#3-chalklings) |
| 4 | Player archetypes | [Player archetypes](#4-player-archetypes) |
| 5 | More glyphs | [More glyphs](#5-more-glyphs) |
| 6 | Obstacles | [Obstacles](#6-obstacles) |
| 7 | Replays | [Replays](#7-replays) |
| 8 | Campaign | [Campaign](#8-campaign) |

Ideas not yet ranked are under [Other ideas](#other-ideas).

## 1. Tutorial

A single-player tutorial, kept separate from the campaign (section 8).

- **Guide:** characters from the novel, such as Professor Fitch, teach the player.
- **Setting:** years before the events of the novel, in an ordinary period for the school, where students duel as they have for decades. It is **not** a retelling of the novel.
- **Shape (suggestion):** short lessons that each teach one thing, for example one per line type, then rush and steady, bind points and defences, and the end game (chalk fatigue and tiebreak).
- **Scope note:** `CLAUDE.md` and `DESIGN.md` currently say story and lore are out of scope. When the tutorial is built, those notes need a carve-out for the tutorial and campaign framing.

## 2. Practice mode

A single-player sandbox for practising manoeuvres, like the training modes in fighting games such as Street Fighter.

- **Optional automated opponent** (a "dummy") whose behaviour can be set, for example: idle, always defend, always fire LoV, repeat a recorded sequence, or play as the AI opponent.
- **More freedom than a duel**, for example: skip drawing time, turn random modifiers off or fix them, set the seed, refill durability, reset the board, and load any defence preset for either side.
- **Open:** whether the dummy shares code with the AI opponent (build plan step 6). It probably should.

## 3. Chalklings

- **Combine the three stat models.** Long term, mix point buy (current rule), fixed archetypes and extra stats.
- **Archetypes** (for example Guard, Raider, Decoy) with fixed stats and draw times: useful for teaching new players and for speed in faster duels.
- **More stats** such as range, vision or attack speed, if the chalkling mechanic feels lacking in depth.
- **Looks that follow the stats (Amelia, 2026-10-27).** Chalkling designs that match how their stats were spent: a blob for high durability with low damage and speed, a knight for high durability and damage with low speed, and so on. For now every chalkling is a filled circle with its glyph on its back (DESIGN.md 4.4).
- **Tether length by scroll (Amelia, 2026-10-27).** Let scroll set a defender's tether or guard distance, as scroll sets size elsewhere. It needs a trade-off or everyone picks the longest: for example, a longer tether gives up some of the binding's free durability, or costs draw time. Possibly too much choice; the tether is fixed for now.

## 4. Player archetypes

A player picks an archetype before the duel. Each gives one strength and one cost, applied as changes to the rules numbers, so archetypes are pure data. They are called *player* archetypes to keep them separate from chalkling archetypes (section 3).

**Decided (2026-10-24)**
- Both players can see the opponent's archetype.
- One archetype per player per duel; perks and costs are not mixed between archetypes.

**Candidates for review** (30, to be cut down)

| # | Name | Theme | Perk | Cost |
|---|---|---|---|---|
| 1 | Balanced | Baseline | None | None |
| 2 | Warder | Defence | Main LoW +2 durability; repairs cost 1 turn per point (needs repairs back) | LoV deal 1 less damage |
| 3 | Bulwark | Defence | Every LoW other than the main LoW gets +2 durability | Main LoW takes 1 turn longer to draw |
| 4 | Mender | Defence | Repairs are allowed during chalk fatigue (needs repairs back) | Main LoW -1 durability |
| 5 | Anchorer | Defence | LoF bound at 2 or more bind points get +3 durability | Free-standing LoF take 1 turn longer to draw |
| 6 | Geometer | Defence | Main LoW draw time is bind points div 3 instead of div 2 (minimum 1) | Main LoW -1 durability |
| 7 | Striker | Vigor | LoV take 1 turn less to draw (minimum 1) | Main LoW -2 durability |
| 8 | Lancer | Vigor | High tier LoV get +2 speed | Cannot draw Low tier LoV |
| 9 | Skirmisher | Vigor | Low tier LoV get +1 damage | Cannot draw High tier LoV |
| 10 | Ricochet | Vigor | A LoV gains +1 damage each time it reflects off a LoF (maximum +2) | LoV deal 1 less base damage |
| 11 | Piercer | Vigor | Excess LoV damage carries through to the next line | All LoV -1 speed |
| 12 | Maker | Making | One free stat buff on every LoM | LoV take 1 turn longer to draw |
| 13 | Puppeteer | Making | Chalklings carry 1 extra glyph | Chalklings -1 speed |
| 14 | Swarmer | Making | Chalklings with 0 or 1 stat buffs get +1 durability | Chalklings with 3 or more stat buffs take 1 turn longer to draw |
| 15 | Shepherd | Making | Tether distance +2 for bound chalklings | Unbound chalklings deal 1 less damage |
| 16 | Hunter | Making | Chalklings deal +1 damage to other chalklings | Chalklings deal 1 less damage to lines |
| 17 | Sapper | Making | Chalklings deal +1 damage to LoW and LoF | Chalklings -1 durability |
| 18 | Architect | Forbiddance | LoF take 1 turn less to draw; dismissal takes 1 turn | Chalklings -1 durability |
| 19 | Mirrorwright | Forbiddance | A LoV reflected off your own LoF gains +1 speed | Your LoF -2 durability |
| 20 | Gatekeeper | Forbiddance | Dismissing a LoF completes immediately (still uses the turn's action) | Your LoF -2 durability |
| 21 | Gambler | Risk | Rush can roll a +ve modifier (1/8); steady's double odds rise | Base drawings roll -ve at 1/4 |
| 22 | Steady Hand | Risk | No random modifiers; rush and steady only change drawing time | No upside rolls, ever |
| 23 | Daredevil | Risk | Rush cuts drawing time by two thirds instead of half | Rush's double -ve odds rise to 1/4 |
| 24 | Perfectionist | Risk | Steady +ve outcomes give an extra +1 | Steady adds 100% drawing time instead of 50% |
| 25 | Opportunist | Risk | The first drawing after one of the opponent's lines is destroyed rolls with steady odds at normal time | Base +ve odds fall to 1/16 |
| 26 | Scholar | Information | Sees the spec of the opponent's drawing in progress | +ve modifier odds fall to 1/16 |
| 27 | Veiled | Information | The opponent sees only that you are drawing, not the line type | Main LoW -2 durability |
| 28 | Sprinter | Tempo | Main LoW takes 1 turn less to draw (minimum 1) | Main LoW -2 durability |
| 29 | Endurer | Tempo | Main LoW is immune to chalk fatigue's durability loss | Main LoW -2 durability |
| 30 | Patient | Tempo | Each Pass the turn banks 1 turn of progress for your next drawing (maximum 2) | Cancel drawing is unavailable |

## 5. More glyphs

**Constraint from the user:** glyphs must not let chalklings manipulate lines directly. The focus is chalkling-against-chalkling fighting and chalkling movement.

**Removed from the base game (2026-10-25):** Go (arrow, travel in its direction), Wait (horizontal line) and Turn (acute angle with arrow). The base game keeps only Attack and Defend, one per chalkling; these could come back here, along with the old glyph cap (1 free glyph plus 1 per 3 stat buffs, +1 draw turn per extra glyph).

**Ideas from the chalkling design discussion (Amelia, 2026-10-27).** The base Attack chalkling heads straight for the opponent's main LoW (DESIGN.md 4.4). These could become glyphs:

- **Seek:** goes for the nearest enemy thing, chalkling or line, instead of the main LoW.
- **Aimed:** aimed when drawn, like a LoV, at a point or line of the player's choice.
- **Pathfinder:** finds a way round obstacles instead of chewing through them.
- **Hunter:** seeks out enemy chalklings on its own half (compare Hunt below).

**Candidates for review**

| Glyph | Suggested shape | Effect |
|---|---|---|
| Hunt | Circle with a dot | Seeks the nearest enemy chalkling and deals double damage to chalklings, but cannot damage lines |
| Escort | Two parallel arrows | Follows a chosen friendly chalkling and attacks anything that attacks it |
| Rally | Star | +1 damage for each other friendly Rally chalkling within 1 unit |
| Hold | Square | Cannot be pushed by LoV |
| Sentinel | Vertical line | Stands still and attacks any enemy chalkling that comes within range, with +1 damage while stationary |
| Ambush | Hook | Waits until an enemy chalkling comes within range, then deals +2 damage on its first attack |
| Intercept | Chevron | Moves to body-block the nearest incoming LoV aimed at its own side (takes the hit itself) |
| Taunt | Spiral | Enemy chalklings with the Defend glyph in range must attack this chalkling first |
| Brace | Double horizontal line | Takes half damage (rounded up) from chalklings |
| Retreat | Arrow with a tail | Returns to the nearest friendly bind point when below half durability |
| Pack | Three dots | Moves with the nearest friendly Pack chalkling, so groups stay together |
| Duel | Crossed swords | Locks onto one enemy chalkling and deals +1 damage to it; neither can attack anything else until one is destroyed |
| Patrol | Zigzag | Moves back and forth between two points |
| Charge | Double arrow | +1 damage on the first attack after moving at full speed for a tick |

## 6. Obstacles

- Obstacles must be **mirrored** so no setup is unfair, and offered as a **pre-duel option**, not always on.
- **In-world justification:** neutral lines already chalked onto the arena floor. Duelling arenas, like sports pitches, are naturally symmetrical.
- **Examples:** neutral LoF segments (reflect LoV from either side), neutral LoW pillars (absorb LoV), and chalk-dust patches that slow chalklings.
- **Open:** can neutral lines be damaged or destroyed, and can they provide bind points?

## 7. Replays

Low priority. Because duels use a seedable random source, a duel can be recorded as its settings, seed and each turn's choices, then replayed turn by turn. Also useful for balancing.

## 8. Campaign

Separate from the tutorial, and the lowest priority. It shares the tutorial's setting: the school, years before the novel, with students duelling as they have for decades. A likely shape is a series of duels against students of rising skill. Details to be decided later.

## Other ideas

### Continuous attributes

- **LoV frequency slider.** Replace the Low / Medium / High tiers (DESIGN.md 4.3) with a continuous frequency choice, where draw time, speed and damage scale with it. Build the tier system so this swap is a data and UI change, not a rules rewrite.
- **Continuous stats in general.** Let attributes such as health (durability), damage and speed take continuous values to give players more agency.
- **Risk to watch:** decision paralysis. Players may spend too long agonising over tiny adjustments and slow the duel down. Possible mitigations: coarse slider steps, the decision timer, or saved favourites.

### Line of Vigor

- **Excess damage carry-through.** A LoV that destroys a chalkling now carries on with the leftover damage (DESIGN.md 4.3, 2026-10-27). A LoW still absorbs it completely and ignores the excess; letting excess carry through a LoW too is still an idea to try.

### Visuals

- **Damage shown as wear (Amelia, 2026-10-27).** As a chalkling, LoW or LoF loses durability, its art deteriorates (smudged, cracked or fading chalk), so health can be read from the board itself. Until then a chalkling's durability is in the hover stats box and a LoW's damage is shown in red. Damage shown this way must not rest on colour alone (`docs/ACCESSIBILITY.md`).

### Settings

Player settings to add later (the list started 2026-10-27). Accessibility settings are also tracked in `docs/ACCESSIBILITY.md`.

- **Scroll direction for turning a circle (Amelia, 2026-10-27).** Which way the scroll wheel turns a nine-point circle (Shift + scroll, DESIGN.md 4.1) should be a player setting. The default was flipped in v0.18 because the first direction felt wrong.
- **Holding Tab for the lines list (Amelia, 2026-10-27).** As well as pressing Tab to open and close the lines list (DESIGN.md section 2, Information), let the player choose to hold Tab to show it and release it to return to the menu.
- **Stop the log jumping to the newest entry (Amelia, 2026-10-27).** Today the log scrolls to the end whenever it changes. Not everyone wants that, so it should be possible to turn off.
- **Line info key (Amelia, 2026-10-29).** The key held to show a hovered line's stats box and highlight (Alt by default, DESIGN.md section 2, Information) should be configurable, like the free hand key (Ctrl, or Cmd on macOS).
- **Configurable colours (Amelia, 2026-10-27).** Every colour that carries meaning (players, highlights, hover marks, placement preview, red for invalid) should be changeable, including colour-blind presets. See `docs/ACCESSIBILITY.md`.
