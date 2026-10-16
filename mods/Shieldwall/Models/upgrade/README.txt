The Warstone upgrades' own looks (Shieldwall 0.4.19+): made from scratch by upgrades.py in Blender (lib.py is its kit).
Remake: copy lib.py and upgrades.py to ~/.cache/upgrades (the flatpak Blender can't read /tmp), then for each of banner totem brazier obelisk:
`blender -b --python ~/.cache/upgrades/upgrades.py -- <kind> render` (previews prev_<kind>_*.png there), and copy up_<kind>.json.gz back here.
Built facing +y in Blender (game +z); Pieces turns them 180 degrees, as the game's pieces face -z. Foot at z 0.
Materials (UpgradeModel.Materials, in lib.SLOTS order): wood, dark, iron, gold (bronze), bone, hide, rope, glow (embers), cloth (red), paint
(black), shade, rock and rune (rock4_forest's textures in the pieces' shader: the rock's own shader lays moss on every upward face).
The obelisk's runes are boolean cuts applied one at a time; a cut that leaves anything standing proud of the faces is retried nudged by a hair
(edges lining up exactly made shelves of rune paint stick out), and strokes stop short of each other (sharp wedges between cuts broke away).
The brazier's fire (piece_brazierfloor01's flames, light and sound; not its burn or warmth) and the banner's narrow collider are added in Pieces.
