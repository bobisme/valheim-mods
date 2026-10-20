# Throwaway measuring probes (not mods: never in dist)

Build one with `dotnet build -c Release` in its folder. Copy the DLL into `BepInEx/scripts` and run `reload <Name>` (Claude Tools'
dev loader). ScriptEngine won't load them at game start without a pdb. Take the DLL out of scripts when you're done.

- **staticprobe** (bn-3uwk, world-load leaks): `echo <tag> > BepInEx/claude/staticprobe.req` writes `staticprobe-<tag>.txt`. After a
  full GC it lists every static and DontDestroyOnLoad collection in loaded mod and game assemblies, with its size and destroyed Unity
  entries. Take one snapshot, logout + play twice, snapshot again, and diff: what grows each load is held across worlds.
- **assetprobe** (bn-3tne, Unity asset leaks): `echo "<tag> gc" > BepInEx/claude/assetprobe.req` writes `assetprobe-<tag>.txt` (Unity
  objects by type and name, root holders, broken renderers, heap). `diff.py a b` compares two.
