# Build plan

The order the game is being built in. Each step ends with something you can open and try in Unity.

1. **Rules engine.** Plain C# rules core with EditMode tests (`Assets/Scripts/Rules`, `Assets/Tests/EditMode`).
2. **Rough playable duel.** Hot-seat duel on one screen with text menus and a turn log. *(Done.)*
3. **The board.** Continuous 2D space with bind points, LoV movement, reflection off LoF and hits on LoW, drawn with simple shapes. Lines are placed by clicking the board. *(Current step.)*
4. **Drawing input.** Placing lines with snapping and free-hand mode, plus chalk visuals.
5. **Chalklings and glyphs**, then saved defence presets.
6. **AI opponent**, then polish. Online play last.

## Playing the prototype

1. Open the project in Unity and press **Play** in any scene, including a new empty one. The duel screen appears in the Game view.
2. Player A chooses an action and locks it in, then hands over to Player B. When both have locked in, the tick resolves and the log shows what happened.
3. New drawings are placed by clicking the board, on the right. Bind points are the small squares on your main Line of Warding.
   - **Warding:** your first one is your main LoW and goes in your home spot. Later ones attach to a bind point you click.
   - **Forbiddance:** between two bind points, tangent at one, or a free line (click its two ends).
   - **Vigor:** click where to draw it, then where to aim. It flies at its speed each turn, bounces off Forbiddance and stops at Warding or a chalkling.
   - **Making:** bound to a bind point you click, or free at a point you click. Chalklings stand still for now.
4. A damaged Line of Warding turns red where it was hit. Faint trails show where each Line of Vigor went last turn.
5. The first time you open the project after the Input System package was added, Unity asks whether to enable its backends. Choose **Yes**; the editor restarts.
6. To change any rules number while playtesting, add a **Duel Controller** component to an object in a scene (GameObject > Create Empty, then Add Component) and edit its fields in the Inspector. Set **Seed** to replay a duel exactly.

The placement rules that `DESIGN.md` doesn't settle yet (sizes of attached circles, tangent length and so on) are listed under "Rules core defaults" in its section 7.
