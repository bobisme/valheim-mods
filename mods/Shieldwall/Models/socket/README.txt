The stave socket's look (Shieldwall 0.4.16+): made from scratch by socket.py in Blender.
Remake: copy socket.py to ~/.cache/warstone (the flatpak Blender can't read /tmp), `blender -b --python ~/.cache/warstone/socket.py -- render`
(previews socket_*.png there, with a stand-in stave for scale), copy ~/.cache/warstone/socket.json.gz back here. It uses the Warstone's
materials (Look.Materials: rock, iron, rune), loaded by Look.SocketMesh and put on the piece in Pieces.Ensure. The rune is cut with booleans
applied one stroke at a time into a triangulated capstone (stacked modifiers on a tidied hull ate the stone). A stave's foot is ~16 cm thick:
the iron ring is sized round it; SocketTop (where the stave stands) is the model's top less 5 cm.
