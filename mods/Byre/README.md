# Byre

Livestock: sheep (Meadows) and goats (Mountains) to tame, breed, shear and milk; shears, a milk pail and a feeding trough.
GUID `cx.bob.byre`. Restart-level (`RESTART_REQUIRED.txt`): saved creatures, items and a piece. Prefab names are forever
(`Core/PrefabNames.cs`, held to `tests/Byre.Tests/prefabs.txt`).

## How it's made

- **Animals** (`Animals.cs`): copies of the game's `Boar` (grown), `Boar_piggy` (young, half size, `Growup`) and `boar_ragdoll`
  (the body). The boar is the donor for both kinds: Humanoid + MonsterAI + Tameable + Procreation is what taming by food needs
  (the deer's AnimalAI can't be tamed, and the deer is far taller). Our Blender meshes are skinned to the boar's own 34 bones
  (`Models/animals.py`), so the boar's animator moves them. Tusks, LODs and grunts go; the deer's hit, death and idle sounds come in.
  No attack items: an alerted one keeps away instead of charging.
- **Livestock** (`Livestock.cs`, a component on every animal): one ZDO long, `byre_last` (ticks of the last shearing or milking).
  The fleece (a second skinned mesh) shows while `Husbandry.Ready`. E with the tool in the bag (or the tool used on it) asks the
  owner (RPC `Byre_Harvest`), which checks again, sets the clock and drops the yield at the animal's feet. Wild ones flee players
  in a MonsterAI.UpdateAI postfix.
- **Trough** (`Trough.cs`): a `piece_chest_wood` copy, 4x1 slots, `dh_notstorage`. A prefix on MonsterAI.UpdateConsumeItem walks
  a hungry tame animal (any kind) to a trough within 12 m holding its food, takes one (ContainerAccess.TakeOver first) and runs
  the game's own "ate" path (m_onConsumedItem, the consume trigger).
- **Herds**: one SpawnSystemList of ours added to every SpawnSystem as it wakes; switched off while the server lacks the mod.
- **Core** (no Unity): `Species.cs` (the table), `Husbandry.cs` (regrowth, yields, flee and trough rules). `tests/Byre.Tests`.
- **Api** (`Api.cs`): `Byre.Api.Version` 1, `IsLivestock`, `IsWool`, `Products`, `Kinds`, `Grown`/`Young`, `IsTame`, `IsReady`.

## Claude Tools

`byre herds [r] | spawn <sheep|goat|lamb|kid> [tame] [n] | ready [r] | age <hours> [r] | feed [r] | use | troughs [r] | clear [r]`.
Spawns are tagged `byre_test`; `clear` removes only tagged ones within its radius.

## Playtest checklist (nothing below has been seen in the game yet)

Load and look
- [ ] Loads clean after a restart: the log says "Made 11 prefabs", no warnings about anything not made.
- [ ] `byre spawn sheep tame 2`, `byre spawn goat tame 2`: they stand on the ground the right way up, the right size, and walk,
      run, graze (eat) and idle with the boar's moves and no torn or stretched skin (neck, legs, tail). Fleece moves with the body.
- [ ] Stars (2-star wild ones): a little bigger, colours unchanged.
- [ ] Kill one: the body falls in our model (not a boar), drops meat and scraps (a woolly sheep: a wool too); deer sounds, no grunts.
- [ ] Lamb and kid (`byre spawn lamb tame`): half size, woolly, grow up after `byre age 48`.

Wild herds
- [ ] Sheep herds appear on open Meadows, goats on Mountain slopes, not too many (default: 25% a check every 600 s, up to 3 near).
- [ ] They run from you at about 8 m (goats 10), half that crouching; hurt, they run; they never attack.
- [ ] Goats get up slopes and hop now and then.

Taming and breeding
- [ ] Drop dandelions near a wild sheep and back off: it eats, calms, tames in about 20 min (goat 30).
- [ ] Two fed tame ones breed; the young are tame and grow up.

Shearing and milking
- [ ] Hover a tame sheep: "Wool ready (needs shears)" without, "[E] Shear (wool ready)" with the shears.
- [ ] E: 2-3 wool (1 if hungry) drops, the fleece goes, the hover counts down; `byre age 48` brings it back.
- [ ] Wild ones and lambs: E pets or nothing, never shears. Two players at once: one harvest.
- [ ] Milk pail on a tame goat: 1-2 goat's milk; it eats as food.
- [ ] Multiplayer: shearing an animal another player owns works and the fleece goes on both screens.

Trough
- [ ] Build it (workbench, 10 wood); put barley in; a hungry tame sheep within 12 m walks over and eats (`byre troughs`).
- [ ] A tame boar eats its own food from it; a wolf only meat.
- [ ] Take it down: refund, its contents dropped.

Items
- [ ] Icons look like the models; shears at the forge (3 copper), pail at the workbench (6 wood); costs follow the settings.
- [ ] The baked models (0.1.1): shears, pail, milk jug, wool fleece and trough look as in the bake viewer, the trough's legs the same wood
  as its log; each icon shows its model whole and well framed.
- [ ] Hold the shears and the pail (they equip like a club, harmless): the fist round the shears' leather grips, blades forward; the pail
  hanging from its handle bar. If not, `byre grip ByreShears ex ey ez` (or ByreMilkPail) turns them live; put the numbers in Items.cs's Hand.
- [ ] Shearing and milking still work with the tool in the bag or in hand.
