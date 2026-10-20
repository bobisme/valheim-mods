Byre's models, made in Blender (5.x, the flatpak: run from a folder under ~/.cache, it can't read /tmp).

animals.py  the sheep (body + fleece), the goat, the pig and the cow on the boar's own skeleton; the goose and gosling on the hen's:
            blender -b --python animals.py -- sheep <out dir>     (goat likewise)
            writes <kind>.json.gz (+ sheep_fleece.json.gz), <kind>_albedo.png, a .blend and preview renders.
goods.py    shears, pail, milk (the jug), wool (a rolled fleece), trough, made detailed: copied to ~/.cache/byre-goods and baked from the repo
            root: blender -b --python $PWD/tools/bake/bake.py -- $PWD/mods/Byre/Models/bake_conf.py <kind> (docs/model-baking.md), which
            writes <kind>.json.gz (with tangents), <kind>_albedo.png and <kind>_normal.png here. Previews of the detailed ones:
            blender -b --python ~/.cache/byre-goods/goods.py -- render <kind>. The viewer: python3 tools/bake/viewer.py
            ~/.cache/bake/viewer/byre.html ~/.cache/byre-goods/view.
byrelib.py  the boar's skeleton as an armature, and the frames: Unity (x,y,z) <-> Blender (x,z,y), the boar mesh stored Z-up.
export_rig.py  reads the boar's bones, bind poses and mesh out of the game's bundle (UnityPy, offline):
            python export_rig.py <valheim_Data/StreamingAssets/SoftRef/Bundles/c4210710> 2885373863042822346 "Poly Art Boar" boar-rig.json
            boar-rig.json and the boar's texture are Iron Gate's: kept in ~/.cache/byre-rig, never committed. animals.py reads
            $BYRE_RIG or ~/.cache/byre-rig/boar-rig.json (hen-rig.json beside it for the birds: the same exporter, the Hen mesh in
            that bundle, its 30 bones; the hen is 0.41 m tall in that frame, its Visual scaled 2 in the game).

The animals are made low-poly to begin with (the game's Poly Art animals: the boar is 2,078 triangles with a 128 px texture), flat
shaded, coloured by region and baked into one small atlas with gentle ambient occlusion, so docs/model-baking.md's high-to-low bake
has nothing to add here: sheep 1,500 + fleece 900 triangles, goat 1,810, pig 1,768, cow 2,192, goose 948, gosling 700 (birds: 128 px atlas). The goods are baked: shears 628 triangles, pail 1,028, jug 617,
wool 1,030, trough 854, truffle 380; 128 px maps (wool and trough 256, truffle 64).
Skinned json: v, n, uv, t (one list), bi/bw (four bone indices into the boar renderer's 34 bones, weights heaviest first).
Static json (the bake's): v, n, tan, uv, t (one submesh). Both in Unity's frame, triangles wound for Unity.
