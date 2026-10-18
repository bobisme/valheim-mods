Byre's models, made in Blender (5.x, the flatpak: run from a folder under ~/.cache, it can't read /tmp).

animals.py  the sheep (body + fleece) and the goat, on the boar's own skeleton:
            blender -b --python animals.py -- sheep <out dir>     (goat likewise)
            writes <kind>.json.gz (+ sheep_fleece.json.gz), <kind>_albedo.png, a .blend and preview renders.
props.py    shears, pail, wool, milk, trough: blender -b --python props.py -- <out dir> [names]
byrelib.py  the boar's skeleton as an armature, and the frames: Unity (x,y,z) <-> Blender (x,z,y), the boar mesh stored Z-up.
export_rig.py  reads the boar's bones, bind poses and mesh out of the game's bundle (UnityPy, offline):
            python export_rig.py <valheim_Data/StreamingAssets/SoftRef/Bundles/c4210710> 2885373863042822346 "Poly Art Boar" boar-rig.json
            boar-rig.json and the boar's texture are Iron Gate's: kept in ~/.cache/byre-rig, never committed. animals.py reads
            $BYRE_RIG or ~/.cache/byre-rig/boar-rig.json.

The models are made low-poly to begin with (the game's Poly Art animals: the boar is 2,078 triangles with a 128 px texture), flat
shaded, coloured by region and baked into one small atlas with gentle ambient occlusion, so docs/model-baking.md's high-to-low bake
has nothing to add here: sheep 1,500 + fleece 900 triangles, goat 1,810, props 170 to 560, atlases 64 to 256 px.
Skinned json: v, n, uv, t (one list), bi/bw (four bone indices into the boar renderer's 34 bones, weights heaviest first).
Static json: v, n, uv, t (a list per submesh). Both in Unity's frame, triangles wound for Unity.
