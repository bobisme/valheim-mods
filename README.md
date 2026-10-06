# Valheim mods

Bob's Valheim mods, built for native Linux and Windows and packaged for the in-game mod manager. Farmhand is the first mod.

## Farmhand

Farmhand makes tending a farm faster while keeping normal crop growth and resource costs.

Equip a **cultivator**, open its build menu, and select a crop:

| Controls | Action |
| --- | --- |
| Hold **Right Alt** | Show row positions around the normal placement ghost |
| **Right Alt + J** | Plant a spaced row of the selected crop |
| **Right Alt + U** | Harvest nearby mature crops, then replant the same crop types |
| **Right Alt + Shift + U** | Harvest nearby crops without replanting |
| **Escape** or put away the cultivator | Cancel the current batch |

Aim a little in front of your feet so the row fits within normal placement reach. Green preview dots mean the ground is within reach; actual planting additionally checks cultivation, biome, sunlight, obstacles, wards, and crop growth space. Blocked spots are skipped. Minimum spacing is 1.8 metres by default and increases for crops with larger growth radii.

Planting spends seeds/materials, stamina, and cultivator durability for each successful crop, respecting the world's normal free-build rules. Bring seeds before replanting; harvesting leaves normal drops on the ground and does not move them into your inventory. With BuildFromChests installed, its existing building integration can supply seeds from nearby accessible chests.

Harvesting supports unambiguous crop and seed-crop pieces in the cultivator's current catalog, including modded crops with the same standard Plant/Pickable structure. It excludes trees, vines, respawning berries, and unknown recipes. Protected crops are skipped. Batches default to a 3-metre radius and 40 crops. A crop is replanted only after its owner confirms harvesting; if there are no seeds or the replacement cannot grow, the spot stays empty. Concurrent harvests by friends can satisfy the acknowledgement, so the batch count does not imply every drop belongs to you.

Unhealthy plants nearby get labels while the cultivator is equipped, such as "Needs more space" or "Needs sunlight". Controls, sizes, and labels can be changed in F7 → Mod settings, or `BepInEx/config/com.bobisme.farmhand.cfg`.

Turn off BuildOrders plan mode before batch actions. If another mod snaps the crop ghost away from its checked position, Farmhand skips that spot. No extra plugin is needed on the host or other players: planting and harvesting use normal game objects and owner RPCs. Farmhand adds no prefabs or save format, and supports ScriptEngine hot reload with F6.

## Build with mise

Install [mise](https://mise.jdx.dev/) and run:

```bash
mise trust
mise install
mise run test
mise run build
mise run publish
```

`mise.toml` pins the .NET SDK. Valheim's assemblies and BepInEx are read from your game installation. Native Linux Steam and Flatpak Steam locations are detected automatically. For another library, set:

```bash
export VALHEIM_DIR="/path/to/Steam/steamapps/common/Valheim"
```

Builds and publishing leave your game untouched by default. To deploy Farmhand's DLL and PDB to your own installed ScriptEngine:

```bash
mise run install
```

Press F6 in game afterward, or restart Valheim. BepInEx and ScriptEngine must already be installed. For a published GitHub repository, add its `owner/repo` under F7 → Mod sources; the manager reads `dist/manifest.json` and installs both files.

## Validation

`mise run test` exercises centered row geometry, minimum growth spacing, invalid configuration, batch bounds, and harvest acknowledgement/timeout rules. Builds verify public API usage against the installed game. `mise run verify` additionally checks the private fields/methods and Harmony targets against the installed assembly, and checks that the published symbols are readable by the installed ScriptEngine's Cecil.

Manual checks for a first playtest:

1. Plant a row on cultivated soil. Check each crop consumes the usual seeds, stamina, and durability; aim near the edge of reach and confirm distant spots are skipped.
2. Try uncultivated soil, a wrong biome, a roof, an occupied spot, and a protected ward. No invalid crop should be planted or paid for.
3. Run out of seeds or stamina midway through a row. The batch should stop without creating free crops.
4. Harvest and replant mature crops. With no seeds, harvesting should still work and leave empty spots. Shift should harvest only.
5. With a friend hosting, harvest crops whose network owner is the friend. Confirm no duplicate drops and no replant before the harvest response.
6. Change tool, open a menu, die, move away, or press F6 during a batch. Check it stops and restores normal manual planting. Repeated F6 reloads should not duplicate actions.
7. Check seed use from nearby chests with BuildFromChests, and rejection of BuildOrders plan mode or redirected ghosts.

The first version is built and checked against local assemblies; gameplay, multiplayer latency, and F6 reload behavior still need a playtest. No game assemblies are redistributed in this repository.

## Development

Mods live in `mods/<Name>/`. `scripts/publish.py` builds the release DLL/PDB and generates `dist/manifest.json`. Commit generated `dist/` artifacts for the mod manager and bump the version in `Plugin.cs` before publishing updates. Use feature branches and pull requests for changes once a remote is configured.

The build layout follows the MIT-licensed [mod manager template](https://github.com/HardHeadHackerHead/valheim-mod-manager/tree/main/template). Farmhand uses [Harmony patches](https://harmony.pardeike.net/v2/articles/patching) around normal game methods and removes them on unload.
