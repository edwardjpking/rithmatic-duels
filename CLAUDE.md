# Rithmatic Duels

A two-player, simultaneous-choice, turn-based strategy duel game built on the chalk-line magic system from Brandon Sanderson's *The Rithmatist*. Built in **Unity** (C#).

## Source of truth

`docs/DESIGN.md` is the authoritative design document. Read the relevant section before implementing or changing any game rule. If code and the design doc disagree, flag it to the user rather than silently picking one. When a design decision is made in conversation, update `docs/DESIGN.md` in the same change.

Notion is no longer used for design notes; do not sync to it.

## Key rules at a glance

- Four line types only: Warding (LoW), Forbiddance (LoF), Vigor (LoV), Making (LoM, creates chalklings). Silencing and Revocation are out of scope.
- One drawing in progress per player. Each turn both players secretly pick one action, then one tick resolves.
- Tick resolution order (DESIGN.md section 3): dismissal → rushed lines finish → animated lines act (and deal damage) → other lines finish.
- Lose when your main LoW is breached. Turn limit → draw.
- Completed drawings roll a random modifier; rush/steady change draw time and odds (section 5.2).
- **Invalid choices can't be selected** (DESIGN.md section 1.1, applies to every feature): never let the player pick an option, spot or size the rules would refuse and only reject it at Lock in. Block it in the interface and show why visually (greyed out, red fill, a red cross under the mouse, sizes capped to what fits).
- **Consistency everywhere** (DESIGN.md section 1.1, applies to every feature): the same kind of thing always works and looks the same across the game, in controls, menus, visual signs, wording and rules. For example, scroll changes a drawing's size, Shift + scroll its shape or turn, and right-click goes back one step. Before adding or changing anything, check it against the patterns already in use. If it would break one, or an existing part already does, say so unprompted and suggest the consistent option before building.
- **Accessibility throughout** (DESIGN.md section 1.1, applies to every feature): consider accessibility in everything that changes how the game looks, sounds or is controlled. Check new work against `docs/ACCESSIBILITY.md`, and add any new friction points you find there without asking. Don't let meaning rest on colour alone, and keep colours in one place so they can be made configurable.
- **All numbers are placeholders.** Keep them in data (e.g. ScriptableObjects or config), never hard-coded in logic, so balancing doesn't require code changes.

## Scope

- Gameplay only. Story and lore from the novel are out of scope.
- Many rules are still open (DESIGN.md section 7). When implementation hits one, ask the user instead of inventing an answer, or implement a clearly marked, easily changed default and say so.
- The user has asked to be reminded to fill in the blank line stats once the overarching design themes are pinned down.

## Unity conventions

- Keep the core rules engine (turn resolution, lines, actions, modifiers) in plain C# with no `UnityEngine` dependency where practical, so it can be unit-tested with the Unity Test Framework (EditMode tests) and reused by UI and AI.
- MonoBehaviours handle presentation and input, and call into the rules engine.
- Bump `GameVersion` (Assets/Scripts/Presentation/GameVersion.cs) in every PR that changes the game: number, date and a few-word summary. It is shown in the game's top bar so the user can tell whether Unity has picked up the latest changes.
- Use a seedable random source for modifier rolls so duels and tests are reproducible.
