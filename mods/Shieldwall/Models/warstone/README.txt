The Warstone's model (Shieldwall 0.4.14+): menhir, horns, plinth, iron bands with spikes and the runes (their own material, glowing),
made by warstone.py in Blender. The shields, blades and skull around it are the game's own items, set by Look.cs.
Remake: copy warstone.py to ~/.cache/warstone (the flatpak Blender can't read /tmp), `blender -b --python ~/.cache/warstone/warstone.py -- render`
(previews preview_*.png there), copy ~/.cache/warstone/warstone.json.gz back here. Materials are tried in game with `siege look`.
