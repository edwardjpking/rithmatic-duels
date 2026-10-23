# Rithmatic Duels

A two-player, simultaneous-choice, turn-based strategy duel game built on the chalk-line magic system from Brandon Sanderson's *The Rithmatist*. Built in **Unity** (C#).

## Source of truth

`docs/DESIGN.md` is the authoritative design document. Read the relevant section before implementing or changing any game rule. If code and the design doc disagree, flag it to the user rather than silently picking one. When a design decision is made in conversation, update `docs/DESIGN.md` in the same change.

Notion is no longer used for design notes; do not sync to it.

## Key rules at a glance

- Four line types only: Warding (LoW), Forbiddance (LoF), Vigor (LoV), Making (LoM, creates chalklings). Silencing and Revocation are out of scope.
- One drawing in progress per player. Each turn both players secretly pick one action, then one tick resolves.
- Tick resolution order (DESIGN.md section 3): completion/dismissal → movement/attacks → damage.
- Lose when your main LoW is breached. Turn limit → draw.
- Completed drawings roll a random modifier; rush/steady change draw time and odds (section 5.2).
- **All numbers are placeholders.** Keep them in data (e.g. ScriptableObjects or config), never hard-coded in logic, so balancing doesn't require code changes.

## Scope

- Gameplay only. Story and lore from the novel are out of scope.
- Many rules are still open (DESIGN.md section 7). When implementation hits one, ask the user instead of inventing an answer, or implement a clearly marked, easily changed default and say so.
- The user has asked to be reminded to fill in the blank line stats once the overarching design themes are pinned down.

## Unity conventions

- Keep the core rules engine (turn resolution, lines, actions, modifiers) in plain C# with no `UnityEngine` dependency where practical, so it can be unit-tested with the Unity Test Framework (EditMode tests) and reused by UI and AI.
- MonoBehaviours handle presentation and input, and call into the rules engine.
- Use a seedable random source for modifier rolls so duels and tests are reproducible.
