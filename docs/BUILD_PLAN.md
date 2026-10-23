# Build plan

The order the game is being built in. Each step ends with something you can open and try in Unity.

1. **Rules engine.** Plain C# rules core with EditMode tests (`Assets/Scripts/Rules`, `Assets/Tests/EditMode`).
2. **Rough playable duel.** Hot-seat duel on one screen with text menus and a turn log. A placeholder lane stands in for the board. *(Current step.)*
3. **The board.** Continuous 2D space with bind points, LoV movement, reflection off LoF and hits on LoW. It replaces the lane through `IMovementResolver`.
4. **Drawing input.** Placing lines with snapping and free-hand mode, plus chalk visuals.
5. **Chalklings and glyphs**, then saved defence presets.
6. **AI opponent**, then polish. Online play last.

## Playing the step 2 prototype

1. Open the project in Unity and press **Play** in any scene, including a new empty one. The duel screen appears in the Game view.
2. Player A chooses an action and locks it in, then hands over to Player B. When both have locked in, the tick resolves and the log shows what happened.
3. To change any rules number while playtesting, add a **Duel Controller** component to an object in a scene (GameObject > Create Empty, then Add Component) and edit its fields in the Inspector. Set **Seed** to replay a duel exactly.

### What the placeholder lane does

It is not a rule from `DESIGN.md` and will be replaced in step 3.

- The board is one straight lane between the two main LoW (14 units by default, from the board config).
- A completed LoV flies down the lane at its speed. Opposing LoV that meet collide using the real LoV collision rules.
- A LoV that reaches the far end is reflected away if the opponent has **any** LoF in effect, otherwise it damages their main LoW. This makes a single LoF a full shield, so turn off **Forbiddance Reflects Vigor** on the lane settings to test an open race.
- Chalklings stand idle. Bind points, attachments and positions are not modelled.
