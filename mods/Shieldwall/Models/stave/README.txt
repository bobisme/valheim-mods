The war staves' own looks (Shieldwall 0.4.18+): made from scratch by staves.py in Blender (lib.py is its kit, adapted from Jarldom's altars).
Remake: copy lib.py and staves.py to ~/.cache/staves (the flatpak Blender can't read /tmp), then for each of ember frost thunder blast hearth:
`blender -b --python ~/.cache/staves/staves.py -- <kind> render` (previews prev_<kind>_*.png there), and copy stave_<kind>.json.gz back here.
Each is built at the size it stands in its socket: z up, foot at 0, the socket swallowing the bottom 0.35 m (StaveModel.Sunk); the shaft
runs straight and centred through the socket's iron ring (radius 0.1) and wanders above it. The item is the same model at 0.55.
Materials (StaveModel.Materials, in lib.SLOTS order): wood (the hewn wood_pole grain, tinted per stave), dark, iron, gold (copper), bone,
hide, rope, glow and crystal (the stave's own colours, emissive), shade, paint (the Blast stave's clay pots). The stave's light hangs at the
middle of what glows in its top third.
