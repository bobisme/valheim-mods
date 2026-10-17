# Baking models into the game's style

Valheim's own models are low-poly with small textures of their own, and ours should match. We still keep the detail: the detailed
Blender model stays the source, and its detail is baked into textures on a low-poly copy. The war staves (Shieldwall 0.4.24) were
the first done this way. Use this for every custom static model (pieces, props, item looks).

## What the game does (measured 2026-10-17 with UnityPy over `valheim_Data`)

- **Triangles.** Half the game's meshes have under 100 triangles and 9 in 10 under about 520. Staffs have 240 to 1,600, the altar
  300, the oak table 1,050. Only bosses, ships and the big Ashlands towers go over 10,000. Aim for about 300 to 1,000 for a small
  prop and 1,500 to 3,000 for a centrepiece.
- **Textures.** Each object has its own small atlas: `_MainTex` and `_BumpMap`, and sometimes a metallic/gloss map or an emissive
  map. They're 64 to 256 px (the Dundr staff is 64 px, the guard stone 256). Our 256 px for a 4 m stave is at the top of that range.
- **Shader.** `Custom/Piece` takes `_MainTex`, `_BumpMap`, `_EmissionMap`/`_EmissionColor` and `_MetallicTex`, so a baked model
  needs no shader of its own. The game's normal maps are DXT5nm (they look pink). A plain RGB normal map with alpha 1 unpacks
  correctly too.
- **Looking things up.** Claude Tools `hall meshes <prefab>` names a prefab's materials and shaders.
  `tools/bake/gametex.py <material>` pulls a material's textures out of the game.

## The tools (`tools/bake/`)

| File | What it does |
|---|---|
| `bake.py` | Builds the detailed scene with the model's own script, makes the low copy, unwraps, bakes, and writes the shipped files and the viewer files. Run by Blender. |
| `viewer.py` | Writes one HTML page showing detailed and baked models side by side. |
| `gametex.py` | Pulls a game material's textures into `tools/bake/tex/` (git-ignored: they're Iron Gate's). Needs UnityPy. |

Each model family has a small **conf** next to its scripts, for example `mods/Shieldwall/Models/stave/bake_conf.py`. A conf defines:

- `WORK`: a work directory under `~/.cache`. The flatpak Blender can't read `/tmp`.
- `COPY`: files to copy there before running.
- `SCRIPT`: the model script to run.
- `ARGS(kind)`: its arguments.
- `NAME(kind)`: the output name.
- `SHIP`: where the shipped files go, normally next to the scripts.
- `BUDGET` (triangles) and `PX` (texture size).
- `DECALS`: name prefixes of the parts that only lie on a surface and become texture alone.
- `look(mats, kind, plain, grained)`: colours each Blender material the way the game draws it:
  - `plain(m, colour, emit=None)`, where colours are the game's (sRGB) values;
  - `grained(m, 'woodpole_MainTex_128.png', scale, tint)`, which lays a game texture on in metres, the way `Look.Build`'s uvs times
    the material's `mainTextureScale` do.

Optional, added while baking the other families:

- `SCRIPT` may be a function of the kind, when each model has its own script (Jarldom's markers).
- `BUDGET` and `PX` may be dicts keyed by kind.
- `grained(..., offset=, on_uv=True)` lays the texture on the part's own uvs instead of a box projection.
- `uvmapped(m, texture, tint)` is a texture on the mesh's own uvs, as for Camping's hides. Confs reach it as the global `uvmapped`.
- `prepare(high)` runs on the detailed parts before the bake. Bathhouse uses it to give boards the grain direction its game export
  uses. Mark parts that should keep their own uvs on the viewer side with `o['bake_uv'] = 1`.
- `SMOOTH` may be a set of names, a tuple of name prefixes or a set of material slot numbers.

The model script must define `mats` (its material slots, in order) and may define `SMOOTH` (the names of objects drawn smooth), as
the altar/stake/stave `lib.py` kits do. Its own `export()` at the end is harmless.

## Running it

From the repo root. Blender is the flatpak, so give it absolute paths:

```
blender -b --python $PWD/tools/bake/bake.py -- $PWD/mods/Shieldwall/Models/stave/bake_conf.py ember
python3 tools/bake/viewer.py ~/.cache/bake/viewer/staves.html ~/.cache/staves/view
```

`bake.py` prints `BAKE <name> low tris N from M` and `shipped ... maps [...] head y`. It writes:

- `SHIP/<name>.json.gz`: `v`, `n`, `tan` (Blender's tangents, which the normal map was baked against), `uv` (the atlas), `t` (one
  triangle list), and `head` (the middle of what glows in the top third, where a light goes);
- `SHIP/<name>_albedo.png`, `_normal.png` and, if anything glows, `_glow.png`;
- `WORK/view/<name>_high.glb`, `_low.glb` and `_view.json` for the viewer;
- `WORK/<name>.blend`, for opening the result in Blender.

## How the bake works (and why)

1. **Low copy, per part.** Swept or lathed parts (tubes, shafts, rings: all quads, one connected piece) get Blender's Un-Subdivide,
   as many passes as the budget needs (each roughly halves the part). That keeps a clean chunky tube, where a plain collapse decimate
   kinks thin tubes. A part made of separate pieces (a bucket's boards) goes through the collapse path instead, or it comes out gappy.
   Sheets thickened by a modifier are thinned before they're thickened, so their two faces don't cross. Other parts get a planar dissolve,
   then a collapse to the budget's ratio, but never under 12 triangles each. Parts named with a `DECALS` prefix are left out of the
   mesh (cracks, carved runes, inlays, wraps, lashings, scorch). Give such parts names like that in the model script.
2. **One atlas.** The low parts are joined, auto-smoothed at 40° (hard where it turns sharply, soft where it rounds, as the game's
   own are) and unwrapped with Smart UV Project. UV islands thinner than 3 baked texels are widened (otherwise thin threads, like
   Frigg's loom, bake black), and then everything is packed.
3. **Bake with the parts pulled apart.** Each part, its low copy and the surface marks that lie on it move 40 m apart, so no part
   bakes its neighbour onto itself. Without this, Blast's cage was baked onto its pots. Each mark goes with the part nearest its own
   surface, measured over points along it: a wrap's centre is inside the shaft. The bake uses selected-to-active with a cage
   extrusion of 0.06 and a ray of 0.2. Shorter rays left black holes where the low copy sits far inside the detail. Colour (the
   diffuse colour pass), a tangent normal map and emission are baked at twice `PX`.
4. **Creases.** Ambient occlusion is baked from the assembled low copy alone, with the detailed parts hidden (otherwise they wrap
   the low copy and darken everything). The colour is multiplied by `0.45 + 0.55·AO`.
5. **Halve** every map to `PX` (that's the smoothing) and save. The mesh goes out in the game's frame (x, z, y), which mirrors it:
   triangles are wound the other way and the tangent's w is `-bitangent_sign`.

## In the game

- **Loading.** Shieldwall's `Look.Build` sets the mesh's tangents from `tan` when present, and `Look.Texture(resource, linear)` loads
  an embedded PNG. `ImageConversion.LoadImage` is called by reflection, because the build can't reference that module directly.
- **Material.** `StaveModel.Baked` makes one material from a copy of a game `Custom/Piece` material (`wood_pole`'s):
  - `mainTexture` is the albedo at scale 1, colour white;
  - `_BumpMap` is the normal map with `_NORMALMAP`;
  - `_EmissionMap` is the glow with `_EMISSION` and `_EmissionColor` white;
  - gloss is 0.12 and metallic 0.
- **Shared code.** `mods/Shared/Baked/Baked.cs` (`SharedBaked.Baked`) does this for every mod: `Tangents(mesh, json)`,
  `Texture(asm, resource, linear)` and `Material(asm, resourcePrefix, fromMaterial, name, owned)`. The last returns null when the mod
  has no albedo map, so the old slot materials stay as the fallback. Compile it in with
  `<Compile Include="../Shared/Baked/*.cs" Link="Shared/Baked/%(Filename)%(Extension)" />`.
- **Embedding.** Add the PNGs as embedded resources in the csproj, for example
  `<EmbeddedResource Include="Models/stave/stave_*.png" LogicalName="Shieldwall.%(Filename)%(Extension)" />`.
- **Other mods.** Copy that small loader and material code into the mod, or move it into `mods/Shared`.
- **Checking.** Claude Tools `render <prefab> yaw= size= focus=x,y,z dist=` shows the result in the game's light. A forced reload
  shows new models only on newly made objects, and it breaks the hammer menu until a restart (see the Shieldwall memory notes).

## The viewer

`python3 tools/bake/viewer.py <out.html> <view dir or <name>_view.json>...` writes one self-contained page with every model given.
The models are embedded, so it opens from disk; three.js comes from unpkg, so it needs the internet. Open it with
`xdg-open <out.html>`.

- The detailed model is on the left and the baked one on the right, each labelled with its triangle count. Both views share one
  camera: drag to rotate, scroll to zoom, right-drag to pan.
- The buttons along the top switch models. "head" and "whole" frame the glowing head or the whole model. There are also
  wireframe and turntable toggles.
- The left side approximates today's look: flat colours, and the game's grain (tinted from the conf) where `grained` was used. The
  right side is exactly the shipped textures.
- To check a page without a screen: `agent-browser open file://...`, `agent-browser eval "<js clicking a #kinds button>"`,
  `agent-browser screenshot <png>`.

Show Bob the viewer before shipping a new family: he judged the staves from it.

## Done

All baked on 2026-10-17 and checked in the game with `render`. Triangles before → after:

| Mod | Models | Conf |
|---|---|---|
| Shieldwall 0.4.24 | war staves, 6,500–11,000 → 1,600–2,150 | `Models/stave/bake_conf.py` |
| Shieldwall 0.4.25 | Warstone upgrades: banner 28,330 → 2,830, totem 14,062 → 3,700, brazier 5,528 → 1,705, obelisk 3,658 → 878 | `Models/upgrade/bake_conf.py` |
| Hallwright 0.12.8 | stake, 1,126 → 574 (128 px) | `Models/stake/bake_conf.py` |
| Demolisher 0.1.5 | stake, 2,022 → 562 (128 px) | `Models/stake/bake_conf.py` |
| Camping 0.1.6 | bedroll 2,032 → 538, lean-to 17,564 → 2,200, tent 3,672 → 1,430 | `Models/bake_conf.py` |
| Bathhouse 0.1.4 | stove 8,958 → 2,766, bench 1,232 → 448, bucket 2,750 → 798, plunge tub 2,022 → 1,117, locker 2,196 → 1,062 | `Models/bake_conf.py` |
| Jarldom 0.1.199 | eight shrines (6,000–17,922 → 1,540–2,721), marker staff 11,222 → 1,594, watch post 4,788 → 1,108, ore claim 4,172 → 1,516, memorial 7,698 → 756 | `Models/altar/bake_conf.py`, `Models/marker/bake_conf.py` |

**Left unbaked, on purpose**

- Shieldwall's Warstone (1,606) and stave socket (1,278) are already within the budget and draw with the game's own tiled rock and iron.
  The Warstone's runes, marks and cracks also change in play: through a MaterialPropertyBlock on the rune submesh, and with
  dressings that are added and removed.
- Jarldom's carvings (`Models/carve`) are the game's own shaft meshes with grooves cut in, drawn with the game's wood material, so
  they already match.
- Hel's bones and skulls are the game's own prefabs, kitbashed at runtime (`HelKit`).
- Freyja's leaves and flowers stay geometry: painted on as texture, they vanished.

**Known differences**

- The Bathhouse bucket's water is matte now: it's part of the single baked material. The plunge tub still gets the game's water
  surface on top.
- The totem (3,700) and the stove (2,766) are over budget, because many small parts each keep a 12-triangle minimum.
- The staves and upgrades were baked before the later tool fixes (separate boards, thickened sheets, thin islands). A re-bake would
  only come out better.

**Viewer:** `python3 tools/bake/viewer.py ~/.cache/bake/viewer/all.html ~/.cache/{staves,upgrades,jarldom-altars,jarldom-stake,stakes,camping,bathhouse}/view`
puts all 31 models on one page.
