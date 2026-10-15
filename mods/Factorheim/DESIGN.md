# Factorheim design

Factorio in Valheim. You start with a burner drill on a copper vein and end by riding a rocket to Valhalla. Bob asked for:
real belts and inserters (you watch items ride), science packs researched in a lab, an ending cinematic, and the whole tree at once.

## Rules every part follows

- **Ownership:** a machine works only on its ZDO owner's game. Items move between two things only when the mover owns both
  (`IPort.Ready`). This means nothing is duplicated in multiplayer, and a line on a zone boundary just waits.
- **Network budget** (see the repo's perf notes): nothing per-frame on the network. ZDO writes happen at most once a second per
  piece, and only when the value changed (`Machine.Set`). Belts send their contents compactly and are throttled, and every client
  draws items locally (`ItemView`). Never call FindObjectsOfType on a timer: use `Live<T>` and physics overlaps.
- **Items** are prefab names (`"CopperOre"`, `"FH_IronGear"`). Use `Ports.Title` for display and `Ports.Prefab` for the GameObject.
- **Tiers:** `Tech.Level` 0..5. Pieces, hand recipes and machine recipes have a `Tier` and stay hidden until it's researched.
  The config "Unlock everything" and `fh tech 5` skip research for testing.
- Unloaded zones don't run (no catch-up in 0.1). The menu reload level means changes need a game restart.
- Prefix every prefab, RPC and ZDO key with `FH_` / `fh_`.

## Tiers and research

| Tier | Name | Biome materials | Research cost (units × packs) |
|---|---|---|---|
| 0 | Burner age | wood, stone, copper, tin, bronze | (start) |
| 1 | Automation | iron (Swamp) | 30 × red |
| 2 | Logistics | iron, silver (Mountain) | 75 × red+green |
| 3 | Tar processing | tar (Plains), silver | 150 × red+green+blue |
| 4 | Eitr works | eitr, sap, soft tissue (Mistlands) | 250 × red+green+blue+purple |
| 5 | Rocket silo | flametal (Ashlands) | 500 × all five |

The research cost is multiplied by a setting. One "unit" uses one of each listed pack and takes the lab `ResearchSeconds` (10 s).

## Science packs (Science feature)

| Prefab | Name | Hand recipe (station) | Assembler recipe | Tier |
|---|---|---|---|---|
| FH_SciRed | Bronze rune | Bronze:1, Wood:2 → 1 (workbench) | same, 5 s | 0 |
| FH_SciGreen | Iron rune | FH_IronGear:1, FH_CopperWire:3 → 1 (forge) | same, 6 s | 1 |
| FH_SciBlue | Silver rune | — | FH_Engine:1, FH_SilverCircuit:1, Silver:1 → 2, 12 s | 2 |
| FH_SciPurple | Eitr rune | — | FH_EitrUnit:1, Eitr:2, FH_IronGear:4 → 3, 20 s | 3 |
| FH_SciYellow | Flametal rune | — | FH_LowDensity:1, FH_EitrUnit:1, FlametalNew:1 → 3, 25 s | 4 |

A pack's tier is when you can make it. Its research unlocks the next tier: green packs are made at tier 1 and researched for tier 2.

## Intermediate items

| Prefab | Name | Made in | Recipe | Tier | Owner |
|---|---|---|---|---|---|
| FH_IronGear | Iron gear | assembler, forge by hand | Iron:1 → 2, 1 s | 0 (hand at forge) | Production |
| FH_CopperWire | Copper wire | assembler, workbench by hand | Copper:1 → 3, 0.5 s | 0 | Production |
| FH_RuneCircuit | Rune circuit | assembler, forge by hand | FH_CopperWire:3, FineWood:1 → 1, 1 s | 1 | Production |
| FH_Engine | Engine | assembler | FH_IronGear:2, Iron:2 → 1, 10 s | 2 | Production |
| FH_Plastic | Black resin plastic | chemical plant | Tar:2, Coal:1 → 2, 2 s | 3 | Production |
| FH_SolidFuel | Solid fuel | chemical plant | Tar:3 → 1, 2 s | 3 | Production |
| FH_SilverCircuit | Silver circuit | forge by hand (tier 2, with Resin); assembler (tier 3, with plastic) | FH_RuneCircuit:2, FH_Plastic:2, FH_CopperWire:4, Silver:1 → 1, 6 s | 2 / 3 (see note) | Production |
| FH_EitrUnit | Eitr processing unit | assembler | FH_SilverCircuit:2, FH_RuneCircuit:10, Eitr:1 → 1, 10 s | 4 | Production |
| FH_RocketFuel | Rocket fuel | chemical plant | FH_SolidFuel:5, Eitr:1 → 1, 15 s | 4 | Rocket |
| FH_LowDensity | Low density structure | assembler | FlametalNew:1, Copper:5, FH_Plastic:5 → 1, 15 s | 5 | Rocket |
| FH_RocketControl | Rocket control unit | assembler | FH_EitrUnit:1, FH_SilverCircuit:1 → 1, 15 s | 5 | Rocket |
| FH_RocketPart | Rocket part | rocket silo | FH_LowDensity:1, FH_RocketControl:1, FH_RocketFuel:1 → 1, 3 s | 5 | Rocket |
| FH_ValhallaTrophy | Einherjar's horn (trophy) | the launch | — | — | Rocket |

Note: blue packs need silver circuits, which need plastic (tar, tier 3). So FH_SilverCircuit is **tier 2 with a hand recipe at
the forge** using Resin in place of plastic (FH_RuneCircuit:2, Resin:4, FH_CopperWire:4, Silver:1). The plastic recipe in the
assembler comes at tier 3 and is cheaper and automatable. This way tier 2 can make blue packs.

Fuel values (`Fuel.Seconds` in Core/Machine.cs): Wood 15 s, FineWood 20, RoundLog 25, Resin 8, Coal 40, FH_SolidFuel 160.

## Pieces (hammer, Crafting tab unless noted)

| Prefab | Name | Tier | Owner | Notes |
|---|---|---|---|---|
| FH_Belt | Bronze belt | 0 | Logistics | 1 m segment, 1.875 items/s, carries players too |
| FH_BeltFast | Iron belt | 2 | Logistics | 2× speed |
| FH_BeltExpress | Eitr belt | 4 | Logistics | 3× speed, glows |
| FH_Splitter | Splitter | 2 | Logistics | 2 m wide, alternates outputs |
| FH_BurnerInserter | Burner inserter | 0 | Logistics | burns fuel (and feeds itself from what it carries) |
| FH_Inserter | Inserter | 1 | Logistics | electric, 13 kW active |
| FH_LongInserter | Long inserter | 2 | Logistics | reaches 2 m |
| FH_FilterInserter | Filter inserter | 3 | Logistics | Shift+E picks the item it moves |
| FH_BurnerDrill | Burner drill | 0 | Production | 2×2, on/near a deposit, burns fuel, outputs at its front |
| FH_ElectricDrill | Electric drill | 2 | Production | 3×3, 90 kW, 2× speed |
| FH_Woodcutter | Woodcutter | 0 | Production | burner, gathers Wood from trees around it without felling them |
| FH_Assembler | Assembler | 1 | Production | 75 kW, speed 0.5 |
| FH_Assembler2 | Iron assembler | 2 | Production | 150 kW, speed 0.75 |
| FH_Assembler3 | Eitr assembler | 4 | Production | 375 kW, speed 1.25 |
| FH_Pumpjack | Tar pumpjack | 3 | Production | on a Plains tar pit: Tar |
| FH_ChemPlant | Chemical plant | 3 | Production | 210 kW, "chemical" recipes |
| FH_SteamEngine | Steam engine | 1 | Power | boiler and engine together: burns fuel, 900 kW, needs water nearby |
| FH_WoodPole | Small power pole | 1 | Power | wire reach 7.5 m, supply 2.5 m (Misc tab) |
| FH_IronPole | Big power pole | 2 | Power | wire reach 30 m, supply 1 m (Misc tab) |
| FH_SurtlingGenerator | Surtling generator | 3 | Power | burns Surtling cores, 3 MW |
| FH_EitrReactor | Eitr reactor | 4 | Power | burns Eitr, 10 MW |
| FH_Accumulator | Accumulator | 2 | Power | stores 5 MJ |
| FH_RuneLab | Rune lab | 0 | Science | researches; runs without power at speed 0.5, powered 60 kW speed 1 |
| FH_RocketSilo | Rocket silo | 5 | Rocket | 9×9, 4 MW, builds FH_RocketPart, launch |

## Who owns what (one agent per feature, each in its own maw workspace)

- **Core** (already written; ask before changing it, and keep changes additive): Plugin, Things, Pieces, Machine, Store, Fuel,
  Ports, Crafts, Tech, ItemView, Look, Icon, GameUi, Net, Tools, Registry (`Live<T>`), ContainerAccess, SnapPoints.
- **Logistics** — Features/Logistics.cs + Features/Logistics/*.cs
- **Production** — Features/Production.cs + Features/Production/*.cs
- **Power** — Features/Power.cs + Features/Power/*.cs. The solver sets `Machine.Powered` on every `Live<Machine>` with `PowerUse > 0`.
- **Science** — Features/Science.cs + Features/Science/*.cs. Research progress, labs, the research window (GameUi), packs.
- **Rocket** — Features/Rocket.cs + Features/Rocket/*.cs (+ Models/ for Blender). Silo, parts, launch, cinematic, credits, trophy.

Each feature adds its own `fh <command>` tests (Feature.Command/Help/Test).
