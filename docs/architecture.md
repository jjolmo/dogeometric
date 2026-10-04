# Architecture

## Spaces and units

- **Model space**: Z-up, millimetres, `double` (`Dogeometric.Core.Geometry.Vec3`). Red = X, green = Y, blue = Z, as in SketchUp.
- **Godot space**: Y-up, metres, `float`. `app/src/Viewport/Space.cs` converts: model `(x, y, z)` → Godot `(x, z, -y) × 0.001`.
- All modelling maths runs in model space. Godot only renders and collects input.

## Core (`src/Dogeometric.Core`)

- `Geometry/` — `Vec3`, `Bounds3`, `Tolerance` (points within 0.001 mm are the same point).
- `View/ViewCamera` — SketchUp navigation: orbit with gravity (no roll, stops before flipping), pan that keeps the
  grabbed depth under the cursor, zoom toward a point (perspective and parallel), zoom extents, standard views,
  nearest iso. `CameraHistory` backs Camera › Previous/Next.
- `Units/Length` — metric parsing/formatting for the Measurements box.

The topology engine (vertices/edges/faces, auto faces, splitting), inference engine, components and file formats will
live here too, so they can be tested without Godot.

## App (`app/`)

- `UI/MainWindow` — menu bar, `ModelViewport`, `StatusBar`; registers commands.
- `Commands/` — `CommandRegistry` loads SketchUp's real menus, descriptions and shortcuts from
  `data/sketchup_commands.json` and keeps SketchUp's command ids (`CommandIds`). Commands without behaviour show
  disabled, so the menus match SketchUp from day one. Menu accelerators fire the commands; secondary shortcuts are
  aliases handled by `MainWindow`.
- `Viewport/ModelViewport` — 3D world in a `SubViewport`, camera sync, picking, input routing. Middle-drag orbit,
  Shift+middle pan, wheel zoom to cursor and middle double-click re-centre work over every tool.
- `Tools/` — `Tool` base class (mouse, keys, status text, Measurements label, 2D overlay) and `ToolManager`.
  Navigation tools return to the previous tool on Esc.

## Fidelity data taken from SketchUp

| Value | Source |
|---|---|
| Menus, command ids, descriptions, shortcuts | `SketchUp.exe` resources (`research/extract_resources.py`) |
| Status bar texts | strings in `SketchUp.exe` (`research/status_texts.txt`) |
| Tool behaviour and modifiers | Instructor help pages (`docs/feature-inventory.md`) |
| New-model camera: eye (5449, −5345, 2310) mm, 35° FOV | least-squares fit to a SketchUp screenshot |
| Orbit speed ≈ 0.072°/px | measured by dragging the same distance in SketchUp |
| Sky/ground/axes/UI colours | sampled from screenshots |
