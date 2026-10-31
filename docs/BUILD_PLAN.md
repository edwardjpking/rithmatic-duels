# Build plan

The order the game is being built in. Each step ends with something you can open and try in Unity.

1. **Rules engine.** Plain C# rules core with EditMode tests (`Assets/Scripts/Rules`, `Assets/Tests/EditMode`).
2. **Rough playable duel.** Hot-seat duel on one screen with text menus and a turn log. *(Done.)*
3. **The board.** Continuous 2D space with bind points, LoV movement, reflection off LoF and hits on LoW, drawn with simple shapes. Lines are placed by clicking the board. *(Done.)*
4. **Drawing input.** Placing lines with snapping and free-hand mode. *(Done, v0.30. Chalk visuals moved to step 6 polish.)*
5. **Chalklings and glyphs**, then saved defence presets, a defence editor, and a bind point editor for custom layouts (DESIGN.md 6.1, 6.8). Behaviour as in `DESIGN.md` 4.4 (settled 2026-10-27): Attack chalklings walk straight at the opponent's main LoW, Defend chalklings guard their anchor, all move then all attack at once in stage 3; new chalkling looks and previews; the 2-turn minimum and LoV carrying through chalklings. *(Current step. Chalklings were mostly built in rounds v0.24 to v0.30; presets remain.)*
6. **AI opponent**, then a main menu (holding the editors), then polish (including chalk visuals for lines). Online play last.

## Playing the prototype

1. Open the project in Unity and press **Play** in any scene, including a new empty one. The duel screen appears in the Game view.
2. Player A chooses an action and locks it in, then hands over to Player B. When both have locked in, the tick resolves and the log shows what happened.
3. Each player first sets up their main Line of Warding: pick circle or ellipse (scroll the mouse wheel over the board to make an ellipse flatter or rounder), then pick its bind points. It always goes at the same spot near your edge. For the first 10 turns a grey Line of Forbiddance stands across the centre. Once both players lock in, both outlines appear and the bind points start drawing; nothing else can be drawn until they are done.
4. Hover over any line to see its stats. Cancelling a drawing lets you pick something else in the same turn, and you dismiss a LoF by clicking it.
5. After that, new drawings are placed by clicking the board, on the right. Bind points are the circles on your main Line of Warding. Drawing far from your main LoW costs extra turns; the menu shows how many.
   - **Warding:** a Mark's Cross, or another LoW with 2 (an ellipse), 4, 6 or 9 bind points, attached to a bind point you click. Scroll to make an ellipse rounder or flatter, or a circle bigger or smaller.
   - **Forbiddance:** between two bind points, tangent at one, or a free line (click its start, then its direction). Scroll to change the length of a tangent or free line.
   - **Vigor:** click where to draw it, then where to aim. It flies at its speed each turn, bounces off Forbiddance (damaging it) and stops at Warding or a chalkling.
   - **Making:** bound to a bind point you click, or free at a point you click. Chalklings stand still for now.
6. A damaged Line of Warding turns red where it was hit. Faint trails show where each Line of Vigor went last turn.
7. The first time you open the project after the Input System package was added, Unity asks whether to enable its backends. Choose **Yes**; the editor restarts.
8. To change any rules number while playtesting, add a **Duel Controller** component to an object in a scene (GameObject > Create Empty, then Add Component) and edit its fields in the Inspector. Set **Seed** to replay a duel exactly.

The placement rules that `DESIGN.md` doesn't settle yet (sizes of attached circles, tangent length and so on) are listed under "Rules core defaults" in its section 7.
