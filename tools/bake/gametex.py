#!/usr/bin/env python3
# Pull a game material's textures out of Valheim's data into tools/bake/tex (for a bake's conf.look to lay the game's own grain on).
#   uv run --with UnityPy python3 tools/bake/gametex.py <material name>...      e.g. woodpole
# Writes <material>_<property>_<width>.png (MainTex, BumpMap, ...). Find a piece's material names with Claude Tools `hall meshes <prefab>`.
# The game's own images are Iron Gate's: tex/ is git-ignored, so pull them again on a new machine. Slow (it reads every bundle).
import UnityPy, os, glob, sys
D = os.path.expanduser('~/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Valheim/valheim_Data')
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'tex') + '/'
os.makedirs(OUT, exist_ok=True)
want = set(sys.argv[1:])
for f in [D + '/level0', D + '/resources.assets'] + glob.glob(D + '/StreamingAssets/SoftRef/Bundles/*'):
    if not want: break
    try: env = UnityPy.load(f)
    except Exception: continue
    for o in env.objects:
        if o.type.name != 'Material': continue
        try: t = o.read_typetree()
        except Exception: continue
        if t['m_Name'] not in want: continue
        want.discard(t['m_Name'])
        for k, v in t['m_SavedProperties']['m_TexEnvs']:
            p = v['m_Texture']
            if not p['m_PathID'] or p['m_FileID']: continue
            tx = o.assets_file.objects[p['m_PathID']].read()
            name = f"{t['m_Name']}{k}_{tx.m_Width}.png"; tx.image.save(OUT + name); print(name, 'scale', v.get('m_Scale'))
if want: print('not found:', sorted(want))
