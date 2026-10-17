The war staves' own looks (Shieldwall 0.4.18+): made from scratch by staves.py in Blender (lib.py is its kit, adapted from Jarldom's altars).
Since 0.4.24 what ships is BAKED (docs/model-baking.md): staves.py's detailed model stays the source; tools/bake/bake.py with bake_conf.py
makes a low-poly copy and bakes the detail into stave_<kind>_albedo/_normal/_glow.png (256 px). stave_<kind>.json.gz is that low copy.
Remake one (from the repo root; Blender wants absolute paths):
  blender -b --python $PWD/tools/bake/bake.py -- $PWD/mods/Shieldwall/Models/stave/bake_conf.py <ember|frost|thunder|blast|hearth>
  python3 tools/bake/viewer.py ~/.cache/bake/viewer/staves.html ~/.cache/staves/view     (then open the page: detailed vs baked)
The bake copies lib.py and staves.py to ~/.cache/staves (the flatpak Blender can't read /tmp; staves.py reads its lib from there) and
needs the game's wood grain in tools/bake/tex (uv run --with UnityPy python3 tools/bake/gametex.py woodpole).
Each is built at the size it stands in its socket: z up, foot at 0, the socket swallowing the bottom 0.35 m (StaveModel.Sunk); the shaft
runs straight and centred through the socket's iron ring (radius 0.1) and wanders above it. The item is the same model at 0.55.
Colours as the game draws them are in bake_conf.py (OWN, look): wood (the hewn wood_pole grain, tinted per stave), dark, iron, gold
(copper), bone, hide, rope, glow and crystal (the stave's own colours, emissive), shade, paint (the Blast stave's clay pots).
The stave's light hangs at "head" in the json: the middle of what glows in its top third.
