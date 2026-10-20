# Byre

Livestock: sheep, cattle and geese (Meadows) and goats (Mountains) to tame, breed, shear, milk and pluck; pigs bred from tame boars
that root up truffles in the Black Forest; shears, a milk pail, a feeding trough, a hay rack and stall dividers.
GUID `cx.bob.byre`. Restart-level (`RESTART_REQUIRED.txt`): saved creatures, items and a piece. Prefab names are forever
(`Core/PrefabNames.cs`, held to `tests/Byre.Tests/prefabs.txt`).

## How it's made

- **Animals** (`Animals.cs`): copies of the game's `Boar` (grown), `Boar_piggy` (young, half size, `Growup`) and `boar_ragdoll`
  (the body). The boar is the donor for both kinds: Humanoid + MonsterAI + Tameable + Procreation is what taming by food needs
  (the deer's AnimalAI can't be tamed, and the deer is far taller). Our Blender meshes are skinned to the boar's own 34 bones
  (`Models/animals.py`), so the boar's animator moves them. Tusks, LODs and grunts go; the deer's hit, death and idle sounds come in.
  No attack items: an alerted one keeps away instead of charging.
- **S2 kinds** (0.3.0): cattle are the boar made 1.9 times over (`Species.Scale`, the ragdoll too); pigs are the boar too, keep its
  grunts, never spawn wild (`Breeding.cs`: a prefix on Procreation.Procreate swaps a tame `Boar`'s due offspring for `ByrePiglet` at
  `PigFromBoarChance`). Geese are the game's `Hen` (its 30-bone skeleton and voice; `MaterialVariation` stripped or the hen's
  colours come back), goslings a hen at 0.55 with a `Growup` added; a goose's Procreation lays `ByreGooseEgg` (the `ChickenEgg`
  copied, 1.35x, its `EggGrow` hatching `ByreGosling`), with or without a partner. Plucked by hand: `Species.Tool` null.
  Goose guard (`Livestock.Guard`, in the UpdateAI postfix): a tame grown goose goes for the nearest stranger within 8 m (`Husbandry.
  Stranger`: a player not in its ZDO `byre_known`, or an untamed non-Players creature), stops 1.6 m short, gives up after 5 s or at
  12 m, rests 10 s; no damage. Petting or plucking (Tameable.Interact) adds the player. Truffles (`Livestock.Root`, once a minute on
  the owner): a tame grown pig following someone in the Black Forest, a roll of 1 in `TruffleMinutes`, never twice in 3 min
  (`byre_rooted`).
- **Livestock** (`Livestock.cs`, a component on every animal): one ZDO long, `byre_last` (ticks of the last shearing or milking).
  The fleece (a second skinned mesh) shows while `Husbandry.Ready`. E with the tool in the bag (or the tool used on it) asks the
  owner (RPC `Byre_Harvest`), which checks again, sets the clock and drops the yield at the animal's feet. Wild ones flee players
  in a MonsterAI.UpdateAI postfix.
- **Trough** (`Trough.cs`): a `piece_chest_wood` copy, 4x1 slots, `dh_notstorage`. A prefix on MonsterAI.UpdateConsumeItem walks
  a hungry tame animal (any kind) to a trough within 12 m holding its food, takes one (ContainerAccess.TakeOver first) and runs
  the game's own "ate" path (m_onConsumedItem, the consume trigger).
- **Herds**: one SpawnSystemList of ours added to every SpawnSystem as it wakes; switched off while the server lacks the mod.
- **Core** (no Unity): `Species.cs` (the table), `Husbandry.cs` (regrowth, yields, flee and trough rules). `tests/Byre.Tests`.
- **Api** (`Api.cs`): `Byre.Api.Version` 3. 1: `IsLivestock`, `IsWool`, `Products`, `Kinds`, `Grown`/`Young`, `IsTame`, `IsReady`.
  2: `Product`/`Tool` per kind, `KindOf`, `IsYoung`, `Remaining`, `HerdsNear`, `Harvest`, `FeedersNear`. 3: new kinds; `Tool` null
  for geese (by hand), `Product` null for pigs; `CanPull` true for a tame grown cow.

## Claude Tools

`byre herds [r] | spawn <kind|young|boar> [tame] [n] | ready [r] | age <hours> [r] | feed [r] | use [kind] | breed [r] | hatch [r] |
roots | guard [r] | troughs [r] | tag <prefab> [r] | clear [r] [items]`. Spawns (and young born by `breed`) are tagged `byre_test`;
`clear` removes only tagged ones within its radius, and with `items` the 0.3.0 goods on the ground there (eggs, cow's milk, truffles).
Spawns land on the floor under the player's height (indoors, not on the roof).

## 0.3.0 live test (Creative, 2026-10-19)

Seen in the game: all 24 prefabs made, no errors; cow (red and white, horns, udder) and goose (grey-brown, orange bill and feet,
white under the tail) look right, pig seen; a tame cow milked (2 cow's milk, 24 h to wait); a goose plucked by hand (1 feather,
hungry); `byre breed`: geese laid eggs (pair and alone), a cow calved, tame boars had 3 pig piglets in 9 births; an egg under the
hall roof by the fire hatched a gosling, one outside didn't grow; a tame goose chased Bob outdoors from 6 m and stopped 1.2 m short,
then let him be; once plucked it knew him and left him alone; a tame pig following Bob in the Black Forest rooted up a truffle
(forced roll; its natural roll came up empty). Not yet seen: wild cattle and geese spawning, taming a wild cow or goose, a natural
truffle at the default rate, eating the truffle and milk, the cooked-egg recipe at the cauldron, calves and goslings growing up,
CanPull from another mod, multiplayer. Indoors among benches a goose's chase didn't get anywhere (blocked): outdoors it works.

## Playtest checklist (0.1.x; later releases list what was seen in their own notes)

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
