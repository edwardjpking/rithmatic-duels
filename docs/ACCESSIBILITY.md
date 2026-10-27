# Rithmatic Duels: Accessibility

Accessibility is considered throughout development (Amelia, 2026-10-27). Anything that adds or changes how the game looks, sounds or is controlled should be checked against this list. New friction points go here as they are found, without needing to ask first. Settings in this list are also counted among the settings to add later (`docs/FUTURE_IDEAS.md`, Settings).

Each item says what the friction is and a likely fix. None are built yet unless marked.

## Colour and contrast

- **Colours should be configurable (Amelia, 2026-10-27).** Every colour that carries meaning should be changeable in the settings, with presets for the common kinds of colour blindness (red-green and blue-yellow). Keep the colours in one place in data, not scattered through the drawing code, so a preset is a data change.
- **Meaning shouldn't rest on colour alone.** Red marks invalid spots (filled bind points, the cursor cross) and also damaged lines. Red on the dark green board is the hardest pair for red-green colour blindness. Add a second sign: a cross or hatching on blocked bind points, and a different mark for damage than for "can't pick this".
- **The same colour means two things.** Red is used for both "invalid" and "damaged", and the light blue hover dot matched the placement preview (Amelia, 2026-10-27; hover marks changed to lime in v0.20). Each meaning should get its own colour (DESIGN.md 1.1, "One colour, one meaning"), and the colour settings should keep them apart. Moving the hover marks to lime in v0.20 made the hovered LoV aim line clash with the durability dot instead, so the aim line goes back to blue (Amelia, 2026-10-27).
- **The two players differ by colour only** (off-white and amber chalk). Add a second difference, such as a small player marker on each line or a different dash or texture, so the players can be told apart in greyscale.
- **Faint marks are low contrast.** Drawings in progress and some previews are drawn at 15% opacity. Offer a high-contrast option that raises these, and check every faint mark can still be seen on the board.
- **Translucent overlays are hard to read.** The lines list over the action menu needs enough opacity to be read easily (Amelia, 2026-10-27). Text over anything translucent should meet a minimum contrast.

## Size and visibility

- **Text size.** Menus and the log use the default IMGUI skin size. Add a UI scale setting that scales text and buttons together, and keep layouts working at larger sizes without scrolling.
- **Thin lines.** A LoF is 0.14 units wide and the chalk lines are thin, so they are hard to see and to hover. Offer a thicker line option, and make the hover area wider than the drawn line.
- **Small targets.** Bind points, the hover dot and the cursor cross are small. Their size should scale with the UI scale setting.

## Controls

- **Scroll wheel is required.** Size, shape, turn and LoF length all use the scroll wheel or Shift + scroll. Some trackpads and mice make this awkward. Add keyboard alternatives (for example + and - for size, and Shift with them for shape or turn) and on-screen buttons, used consistently everywhere scroll is used.
- **Right-click to go back.** Some trackpads and one-button mice make right-click awkward. Add a key for "back one step" (for example Escape or Backspace) and an on-screen Back button.
- **Key rebinding.** Tab, Enter, Shift and right-click should all be rebindable.
- **Holding instead of pressing.** Some players find holding a key easier, others find pressing easier. Holding Tab for the lines list is already on the settings list. Any future hold-to-act control needs a press-to-toggle option, and the other way round.
- **Precise aiming.** Aiming a LoV and placing free lines need precise mouse control. Consider a fine-adjust option (keyboard nudging, or snapping to set angles as an option) for players with less precise movement.

## Information and reading

- **Information only on hover.** Line stats appear only while hovering, which needs steady pointer control. Selecting a line in the lines list (Tab) could pin its stats until dismissed, so nothing important needs a held hover.
- **Abbreviations.** LoW, LoF, LoV and LoM are used in the interface and log. Offer full names as an option, or show the full name on first use.
- **The log moves on its own.** It jumps to the newest entry whenever it changes. A setting to stop this is already on the settings list. Players should also be able to scroll back without losing their place.
- **Screen readers.** IMGUI is not read by screen readers. The log and menus could later be mirrored to a text output a screen reader can use. This is a large change, so it is noted rather than planned.

## Time pressure

- **Decision timer.** Online duels will need a timer (DESIGN.md section 2). Offer the longest setting and an "off" choice wherever both players agree, and show the time left as text as well as any bar.

## Future features

- **Sound.** When sound is added, anything a sound tells the player must also be shown on screen, with captions or visual cues.
- **Motion and flashing.** When effects or animations are added, avoid rapid flashing and offer a reduced motion option.
- **Tutorial.** The tutorial should allow any pace, and let the player re-read each step.
