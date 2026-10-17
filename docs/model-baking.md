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

1. **Low copy, per part.** Swept or lathed parts (tubes, shafts, rings: mostly quads) get Blender's Un-Subdivide, which drops every
   other ring and side and keeps a clean chunky tube. A plain collapse decimate kinks thin tubes. Other parts get a planar dissolve,
   then a collapse to the budget's ratio, but never under 12 triangles each. Parts named with a `DECALS` prefix are left out of the
   mesh (cracks, carved runes, inlays, wraps, lashings, scorch). Give such parts names like that in the model script.
2. **One atlas.** The low parts are joined, auto-smoothed at 40° (hard where it turns sharply, soft where it rounds, as the game's
   own are), unwrapped with Smart UV Project and packed.
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

## Done and to do

- **Done:** Shieldwall war staves (0.4.24).
- **Not yet baked** (detailed models with flat colours, 1,000 to 28,000 triangles):
  - Shieldwall: Warstone upgrades (`Models/upgrade`, the banner is 28k), Warstone, stave socket;
  - Jarldom: shrines (`Models/altar`), marker staff, watch post, ore claim, memorial, carvings;
  - Hallwright and Demolisher stakes;
  - Camping (the lean-to is 17.6k);
  - Bathhouse.

  Each needs a conf whose `look` matches its mod's materials code, uses the rock or iron textures via `gametex.py`, and gets loader
  changes in its mod.
