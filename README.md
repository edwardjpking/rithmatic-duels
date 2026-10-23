# Rithmatic Duels

A two-player, simultaneous-choice strategy duel game based on the chalk-drawing magic system from Brandon Sanderson's *The Rithmatist*. Built in Unity.

Players commit to drawing lines (Warding, Forbiddance, Vigor, Making) without knowing the opponent's choice. Every drawing takes turns to complete, and you lose when your main Line of Warding is breached.

- Design document: [docs/DESIGN.md](docs/DESIGN.md)
- Build plan and how to play the current prototype: [docs/BUILD_PLAN.md](docs/BUILD_PLAN.md)

*This is an unofficial fan project. The Rithmatist is the work of Brandon Sanderson.*

## Opening the project

1. In Unity Hub, choose **Add > Add project from disk** and select this folder. The project targets Unity 6 (see `ProjectSettings/ProjectVersion.txt`); Hub will offer to open it with whichever Unity 6 editor you have installed.
2. On first open Unity generates the remaining project settings and a `Packages/packages-lock.json`. Commit those.
3. Run the tests from **Window > General > Test Runner > EditMode > Run All**.

## Layout

| Folder | Assembly | Purpose |
|---|---|---|
| `Assets/Scripts/Rules` | `RithmaticDuels.Rules` | Plain C# rules engine (no `UnityEngine`): turns, lines, actions, modifiers. |
| `Assets/Scripts/Presentation` | `RithmaticDuels.Presentation` | MonoBehaviours for input and visuals; calls into the rules engine. |
| `Assets/Tests/EditMode` | `RithmaticDuels.Rules.Tests` | EditMode unit tests for the rules engine (Unity Test Framework / NUnit). |
