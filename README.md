# Valheim mods

Bob's Valheim mods, built for native Linux and Windows and packaged for the in-game mod manager.

## BuildShapes

Curve, Mirror, and Repeat tools for **BuildOrders**, using normal pieces, materials, and support rules. The original Curve API has been
merged upstream. Mirror/Repeat also need the new ghost-selection/input API; use the patched planner from [upstream PR #3](https://github.com/HardHeadHackerHead/valheim-mods/pull/3) until it merges. Curve still works with the original API release (BuildOrders 1.9.3 or newer).
The add-on reconnects to the live planner after F6 or individual reloads, without referencing its assembly.

Equip a **hammer**, select a building piece, and close the piece menu:

| Controls | Action |
| --- | --- |
| **F4** | Enter/exit Curve mode (select a beam/pole first) |
| **Left Shift + F4** | Enter/exit Mirror mode |
| **Left Ctrl + F4** | Enter/exit Repeat mode |
| **Left Shift + left click** | Mark curve points or the mirror line |
| **Left Ctrl + left click** | Mirror: toggle a source piece/ghost; Repeat: copy a source piece/ghost and its orientation |
| **L** | Curve/Mirror: submit ghosts; Repeat: open/confirm options |
| **U** in any shape mode | Remove the last submitted shape's unbuilt ghosts |
| **Backspace** | Remove the last marker |
| **Left Ctrl + Backspace** | Mirror: remove the last source selection |
| **F4** or **Escape** | Exit the current shape mode and clear its preview |

**Curve:** select a beam/pole and press F4. Mark **start, bend, end**; the curve passes through the bend marker and works in 3D,
including vertical arches. The turquoise strokes show piece center lines. Pieces keep their native lengths with overlapping joints;
the first/last endpoints meet your markers. Shorter beams follow tighter bends. Beams/poles need exactly two endpoint snap points,
original prefab scale, and a span of 0.25–8 metres. Tight or degenerate bends are rejected before planning.

**Mirror:** press Shift+F4. Mark **two points** to define a vertical mirror plane; only their horizontal direction matters. Ctrl+click
built pieces or visible planner ghosts to select a group; click again to deselect. Thin wire boxes show the source selection, and heavier
boxes preview the copies. L submits the mirrored group; original pieces/ghosts stay intact. You can mark the line and select pieces in
either order. The reflection axis and pivot compensation are chosen from each piece's native snap layout, with mesh bounds as a fallback.
Triangular under-roof walls and sloped beams use their thickness axis; ordinary roofs keep their width axis, so wedge slopes and roof
pitches mirror correctly. Asymmetric carvings, lettering, or handed decorations
remain their original meshes: inspect the resulting ghosts before building those.

**Repeat:** select a post, decoration, or other building piece and press Ctrl+F4. Mark **start, bend, end** for its path. After the third
point, an options panel opens beside the wire preview with a free mouse cursor. Adjust **spacing**, **yaw**, **pitch**, **roll**, and whether
pieces **turn with the curve**; the preview updates immediately. **Confirm** submits shared ghosts, **Edit path** lets you replace the end
point, and **Cancel** exits. Escape closes the options while keeping the path; L reopens them. F4 exits the tool.

Spacing is a maximum, adjusted evenly along the curve to include both endpoints. Curve-following applies the change in horizontal
heading relative to the first point; seed tilt and initial heading are preserved, then the chosen yaw/pitch/roll offsets are applied.
An upright post stays upright with zero tilt offsets. A vertical tangent keeps the previous heading. Native placement origins sit on the
path without automatic terrain alignment. The keyboard shortcuts **[ / ]**, **Page Up / Down**, and **Home** still work outside the panel.
Ctrl+click a built piece/visible ghost outside the panel to copy its prefab and orientation; completed paths reopen the options afterward.
Mark a horizontal curve for a palisade, or an arch to repeat ribs/decorations in three dimensions.

Curves are bounded to 128 metres and all shapes to 256 output pieces. Markers and selected source origins must be within 40 metres;
BuildOrders checks every output pose against its 80-metre reach, unlocks, wards, and no-build rules before accepting anything. Ghost
selection uses visible ghost bounding boxes and respects nearer physical hits; it is approximate rather than precise mesh picking.
Selections are pose snapshots: moving/removing a source afterward does not alter your preview. Scaled and terrain-operation pieces
are excluded. No shape operation alters terrain or builds pieces for free.

Exit shape mode with F4 before selecting another hammer piece or building normally with **E**. Modes reserve normal hammer placement,
and yield to planner blueprint/bridge placement and menus. The Plans window lists generated groups with Move/Level disabled.
Only the designer needs BuildShapes; participating players should use the updated BuildOrders and chosen piece prefabs. Older planners
can receive/build ghosts but still expose terrain actions on add-on groups.

U removes only remaining ghosts; built pieces and terrain stay intact. Undo is one step across all shape tools in the current session,
and clears on death/respawn, F6, or world changes. Saved/shared ghosts remain after the add-on unloads. Settings are in
`BepInEx/config/com.bobisme.buildshapes.cfg`. Radial repeat and ornament presets are future additions.

Automated checks cover curve/station geometry, independently integrated arc spacing, reflected tilted frames, offset pivots, 33 captured native snap layouts (including triangular gables), repeat
bounds and vertical heading, plus reflection dispatch across original/extended/missing/reloaded planners. The upstream API has separate
batch, ghost-ray, input-conflict, undo, and world tests. Rendering, multiplayer delivery, and fresh-launch behavior still need playtests:

1. Curve a 1m/2m wood beam horizontally and vertically; compare center lines to submitted ghosts and normal support/material costs.
2. Mirror a roof wing, triangular under-roof wall (including inverted variants), sloped beam, and offset-pivot beam across an oblique
   line. Check native snaps, wedge slopes, roof pitch, and copied versus original pieces.
3. Repeat upright posts along a curve. Confirm the options open automatically, cursor is free, spacing/yaw/pitch/roll/follow change the
   preview, and slider clicks never move/look/attack/place. Try Edit path, Escape/L, Cancel/F4, then F6 with the panel open.
4. Build one output piece, then U: only its remaining ghosts disappear. Try protected, distant, scaled, and terrain-operation pieces.
5. F6, reload the planner independently, switch worlds, open inventory/F11, or begin a blueprint/bridge. Check input/preview cleanup.
6. Have another player view/build shapes with the updated planner alone. Save/restart and confirm ghosts persist.

For developers, `mise run verify -- --planner-assembly /path/to/BuildOrders.dll` checks the compiled public planning/query interface,
game targets, dependency declaration, catalog metadata, and loader-readable symbols.

## PolygonLeveler

Mark out a building pad and level it, or flatten uneven ground while keeping its average slope. Equip a **hoe**:

| Controls | Action |
| --- | --- |
| **Left Shift + left click** | Put a marker on the ground you aim at |
| **L** | Level the convex polygon horizontally at the **first marker's height** |
| **Shift + L** | Flatten the current ground to its closest fitted plane, keeping its average slope |
| **U** | Undo the last polygon, restoring the previous terrain |
| **Backspace** | Remove the last marker |
| **Delete** | Clear the markers |
| **Escape** or put away the hoe | Stop sending edits to further terrain tiles |

Markers can be placed in any order; their convex hull forms the boundary, so interior markers do not create dents. The turquoise outline initially shows the first marker's horizontal plane. Put the first marker at the height you want, then add at least two more corners. The HUD shows the area and first-marker height before you press L. While Left Shift is held, the normal hoe action is suppressed so placing a marker does not alter the ground. L is only handled during normal play with a hoe; it does not replace QualityOfLife's inventory item-lock key.

**Shift + L** fits a plane to the current ground heights at every selected grid vertex, rather than using the marker heights. It minimizes the sum of squared vertical height changes, removing bumps while retaining the average slope. Shared tile-edge vertices count once in the fit. The outline updates to the fitted plane when you start the action. A polygon whose selected grid vertices are nearly collinear cannot define a stable plane: mark a wider area. The fitted plane must satisfy the same terrain-height, protection, reach, and size limits as horizontal leveling; it is rejected rather than clipped into an uneven surface. U also undoes fitted-plane flattening.

The default limit is 400 m², with all selected vertices within 30 metres of you, at most 16 markers, and 2,048 grid vertices across terrain tiles. Leveling costs one normal hoe use in stamina and durability, with no stone cost. It preserves ground paint and cultivation and honors wards, no-build areas, and the game's normal ±8-metre terrain limits. A tile that fails to rebuild at the requested height is restored before saving.

The mod edits grid vertices inside the boundary, including the matching vertices on both sides of tile seams. Ground triangles blend to unchanged vertices outside the polygon, so the visible edge follows the terrain grid. Local marker previews disappear on reload; the actual terrain changes persist in the ordinary world save. Canceling or losing a multiplayer connection stops further tiles and leaves any completed tile edits in place.

**U** restores the heights, smoothing, and height-modification flags from before your last polygon, including a partially completed polygon. It preserves paint/cultivation and edits outside the selected vertices, and refuses undo if any selected vertex's height data has changed since leveling. You must still be within reach and have ward permission. Undo costs no additional stamina or durability and does not refund the original hoe use. It provides one step, with no redo; a new polygon replaces the previous undo. History is kept in memory for ten minutes after each tile is leveled, with at most 64 retained tile requests per owner. F6/reload, unloaded terrain, disconnecting, or a terrain ownership change can make undo unavailable. Ground leveled with an older version has no undo snapshot.

Undo checks all tiles first, then rechecks and restores each tile before saving. Ownership, permissions, or terrain can change between replies, so a multi-tile undo can stop partway through. Press U again to retry; completed undo tiles are acknowledged without overwriting subsequent edits.

For multiplayer, install PolygonLeveler **0.1.3 or newer** on the host and participating players: a terrain tile's network owner performs and acknowledges its edit. Every owner must confirm preparation before edits begin; an owner without the matching protocol times out without being sent a commit. Owners on 0.1.2 still support L/U but reject Shift + L without edits. Terrain ownership is not forcibly taken. Plain game saves contain only normal terrain data, so the flattened ground remains after removing the mod.

Settings and controls are in F7 → Mod settings, or `BepInEx/config/com.bobisme.polygonleveler.cfg`. Farmhand and PolygonLeveler can be installed independently through the manager.

PolygonLeveler playtest checks:

1. Mark a triangle and rectangle on sloping ground. Press L and verify the interior becomes a plane at the first marker's height, while outside vertices and cultivated/painted ground are preserved.
2. Level across a terrain-tile seam. Confirm both copies of the seam vertices match and the result survives save/reload.
3. Try collinear markers, an oversized area, protected ground, excessive height changes, and an unloaded region. Each should be rejected before edits begin.
4. Have another player own a terrain tile. With the mod installed on both sides, confirm owner replies and persistent edits; without it on the owner, confirm preparation times out without applying the polygon.
5. Press F6, change tools, or disconnect during preparation/application. Confirm local markers and RPC callbacks clean up and normal hoe controls work afterward. Already completed edits remain.
6. Level cultivated ground, then press U: verify original heights/smoothing return and cultivation stays. Change an inside vertex with a normal hoe before U and verify undo refuses; change an outside vertex and verify it remains after undo. Test interrupted undo, repeated U after a lost reply, and undo across a tile seam with another player owning a tile.
7. Mark bumpy sloping ground and press Shift + L. Confirm it forms a sloped plane rather than horizontal ground, its outline follows that plane, and U restores the bumps. Repeat across a tile seam and with an older multiplayer owner, which must reject the action without committing any tile.

## Farmhand

Farmhand makes tending a farm faster while keeping normal crop growth and resource costs.

Equip a **cultivator**, open its build menu, and select a crop:

| Controls | Action |
| --- | --- |
| Hold **Left Shift** | Show row positions around the normal placement ghost |
| **Left Shift + J** | Plant a spaced row of the selected crop |
| **Left Shift + U** | Harvest nearby mature crops, then replant the same crop types |
| **Left Shift + Left Ctrl + U** | Harvest nearby crops without replanting |
| **Escape** or put away the cultivator | Cancel the current batch |

Aim a little in front of your feet so the row fits within normal placement reach. Green preview dots mean the ground is within reach; actual planting additionally checks cultivation, biome, sunlight, obstacles, wards, and crop growth space. Blocked spots are skipped. Minimum spacing is 1.8 metres by default and increases for crops with larger growth radii.

Planting spends seeds/materials, stamina, and cultivator durability for each successful crop, respecting the world's normal free-build rules. Bring seeds before replanting; harvesting leaves normal drops on the ground and does not move them into your inventory. With BuildFromChests installed, its existing building integration can supply seeds from nearby accessible chests.

Harvesting supports unambiguous crop and seed-crop pieces in the cultivator's current catalog, including modded crops with the same standard Plant/Pickable structure. It excludes trees, vines, respawning berries, and unknown recipes. Protected crops are skipped. Batches default to a 3-metre radius and 40 crops. A crop is replanted only after its owner confirms harvesting; if there are no seeds or the replacement cannot grow, the spot stays empty. Concurrent harvests by friends can satisfy the acknowledgement, so the batch count does not imply every drop belongs to you.

Unhealthy plants nearby get labels while the cultivator is equipped, such as "Needs more space" or "Needs sunlight". Controls, sizes, and labels can be changed in F7 → Mod settings, or `BepInEx/config/com.bobisme.farmhand.cfg`.

Turn off BuildOrders plan mode before batch actions. If another mod snaps the crop ghost away from its checked position, Farmhand skips that spot. No extra plugin is needed on the host or other players: planting and harvesting use normal game objects and owner RPCs. Farmhand adds no prefabs or save format, and supports ScriptEngine hot reload with F6.

## Build with mise

Install [mise](https://mise.jdx.dev/) and run:

```bash
mise trust
mise install
mise run test
mise run build
mise run publish
```

`mise.toml` pins the .NET SDK. Valheim's assemblies and BepInEx are read from your game installation. Native Linux Steam and Flatpak Steam locations are detected automatically. For another library, set:

```bash
export VALHEIM_DIR="/path/to/Steam/steamapps/common/Valheim"
```

Builds and publishing leave your game untouched by default. To deploy both mods' DLLs and PDBs to your own installed ScriptEngine:

```bash
mise run install
```

Press F6 in game afterward, or restart Valheim. BepInEx and ScriptEngine must already be installed. For a published GitHub repository, add its `owner/repo` under F7 → Mod sources; the manager reads `dist/manifest.json` and installs both files.

## Validation

`mise run test` exercises centered crop-row geometry, minimum growth spacing, harvest acknowledgement/timeouts, convex hulls, polygon grid containment, degenerate markers, least-squares plane fitting (slopes, noisy ground, tile-seam weighting, world coordinates, and degenerate samples), native terrain-height limits including hidden saturation and legacy-modifier offsets, and selected-vertex undo snapshots/conflict detection. Builds verify public API usage against the installed game. `mise run verify` checks the private fields/methods and Harmony targets against the installed assembly, and checks all published symbols with the installed ScriptEngine's Cecil. Live multiplayer RPCs and terrain persistence still require the playtests above.

Manual checks for a first playtest:

1. Plant a row on cultivated soil. Check each crop consumes the usual seeds, stamina, and durability; aim near the edge of reach and confirm distant spots are skipped.
2. Try uncultivated soil, a wrong biome, a roof, an occupied spot, and a protected ward. No invalid crop should be planted or paid for.
3. Run out of seeds or stamina midway through a row. The batch should stop without creating free crops.
4. Harvest and replant mature crops. With no seeds, harvesting should still work and leave empty spots. Left Ctrl should harvest only.
5. With a friend hosting, harvest crops whose network owner is the friend. Confirm no duplicate drops and no replant before the harvest response.
6. Change tool, open a menu, die, move away, or press F6 during a batch. Check it stops and restores normal manual planting. Repeated F6 reloads should not duplicate actions.
7. Check seed use from nearby chests with BuildFromChests, and rejection of BuildOrders plan mode or redirected ghosts.

The first version is built and checked against local assemblies; gameplay, multiplayer latency, and F6 reload behavior still need a playtest. No game assemblies are redistributed in this repository.

## Development

Mods live in `mods/<Name>/`. `scripts/publish.py` builds the release DLL/PDB and generates `dist/manifest.json`. Commit generated `dist/` artifacts for the mod manager and bump the version in `Plugin.cs` before publishing updates. Use feature branches and pull requests for changes once a remote is configured.

The build layout follows the MIT-licensed [mod manager template](https://github.com/HardHeadHackerHead/valheim-mod-manager/tree/main/template). Farmhand uses [Harmony patches](https://harmony.pardeike.net/v2/articles/patching) around normal game methods and removes them on unload.
