# Valheim mods

Bob's Valheim mods, built for native Linux and Windows and packaged for the in-game mod manager.

## Bob's Pipes

A reusable **Carved Pipe**, crafted at a **workbench** from **4 wood, 2 core wood and 1 leather scraps**. It depends on
[Quad's Cigars](https://github.com/HardHeadHackerHead/valheim-mods) (any version with the tobacco chain), and uses its tobacco growing,
drying, aging, rolling table and humidor. Cigars with **smoking API v1 or newer**, proposed in
[upstream PR #4](https://github.com/HardHeadHackerHead/valheim-mods/pull/4), also lets pipes and cigars share one active smoke. Bob's Pipes supplies the pipe and three blend tins:

| Tobacco | Recipe at the Cigar Rolling Table | Bonus while lit |
| --- | --- | --- |
| Meadow | 2 dried meadow leaves → 2 tins | +5% stamina regeneration |
| Honeywood | 2 aged forest leaves + 1 honey → 2 tins; level 2 table | +10% health regeneration |
| Cloudberry | 2 aged plains leaves + 2 cloudberries → 2 tins; level 2 table | 5% less running stamina |

The humidor upgrades the table for the aged blends. One tin packs one five-minute bowl. **Use a tin** from inventory or hotbar to
choose its blend and pack an empty pipe. Put the pipe on your hotbar, close the inventory and **use the pipe near a burning fire or
holding a torch** to light it. An empty pipe packs the first available tin when used; use it again to light. Use a lit pipe again to tamp
out the ember and save its remaining tobacco. The pipe is never consumed. Finish a bowl before changing blends.

The pipe has a hollow wooden bowl, bent stem and leather wrap, a tiny ember and quiet smoke. While lit it rests in the character's mouth,
with its bowl hanging below the stem and occasional quiet exhalations at rest.
With use, the smoking pipe gradually darkens to a seasoned finish. Fighting, blocking, swimming, bed and exposed rain put it away;
its unfinished bowl stays with the actual inventory item through saves, transfers and deaths. Smoking does not consume stamina or
force you to stay seated. With the smoking API, cigars and pipes share **one active smoke**, preserving unrelated status effects. Set **EffectStrength = 0**
in F7 → Mod settings for cosmetic smoking; bowl duration, smoke and rain behavior are adjustable too. There are no new function keys.

**Install both mods on the host and every player, and restart before loading your world.** The pipe and tins are new saved item prefabs.
For later F6 reloads, the add-on resolves the live Cigars instance and registers its effect names again; it never binds its reloadable
assembly identity. Recipes wait for Quad's actual resources and station and reconnect after a reload. A reload snuffs and saves a lit bowl.
Without the API (Quad's released Cigars), pipes work on their own: lighting a pipe cannot put out a cigar, or the other way round.

Checks cover saved bowls, accounting, malformed data, reloads and dependency failure, both directions of the shared smoking rule,
and the exact native inventory/tooltip/registration hooks. First playtest:

1. Craft a pipe and all three blends. Try packing from the inventory and hotbar; confirm one tin is spent, and a filled bowl cannot be overwritten.
2. Try lighting without fire, in rain, by a campfire and holding a torch. Check the model and inventory icons, ember, idle puffs and subtle smoke.
3. Snuff halfway, save/restart, drop and retrieve the pipe, and move it through a chest or tombstone. Confirm its remaining bowl is kept.
4. Light a cigar while smoking the pipe, and reverse the order. Check only one smoke/bonus remains and unrelated rested/food effects stay.
5. Fight, swim, die and press F6. Check the pipe goes out and its inventory item is kept. Reload Cigars as well and confirm recipes and smoking reconnect.
6. Have a friend with both mods watch the pipe and puffs; check the mouth pose while walking, holding a torch, and sitting at a campfire.

The release is compiled and checked against the native Linux installation; visual poses and live multiplayer still need this playtest.

## TrophyHall

Hang your trophies and your hall remembers what you have slain. Creature trophies on **item stands you have built inside a base** give
everyone in that base small themed perks, shown as a **Trophy hall** status effect. Each kind counts once, however many heads you hang:

| Trophy | Perk |
| --- | --- |
| Troll (forest, frost) | +30 carry weight |
| Draugr | Slight poison resistance |
| Wolf, Ulv, Fenring | +10% stamina regeneration |
| Neck, serpent | +10% swim speed |
| Greydwarf (any) | +10% woodcutting skill gain |
| Boar | +10% health regeneration |
| Deer | +5% movement speed |

At most **six** perks apply, rarest first. Every kind of trophy also counts toward **comfort under a roof**: four kinds add +1, eight add +2.
Walking into a hall names it after the ward's owner, counts its trophies and lists the creatures fallen there, at most every ten minutes per
hall. **Boss trophies stay on their altars**, where the game uses them for Forsaken powers. The perks are deliberately small: flavour felt over
time, not a change to combat balance. Only the player in the hall needs the mod; it reads the item stands everyone already sees. Range,
readings and the whole mod are in F7.

Checks cover themes, boss exclusion, one perk per kind, the six-perk cap, comfort steps and the reading's wording, plus native hooks. First
playtest: hang a troll, wolf and boar head inside a base and walk in; check the reading, the status effect's tooltip and carry weight. Add a
fourth and eighth kind and check comfort under a roof. Leave the base; the effect goes. Check boss altars and stands outside bases are ignored.

## DualWield

Fight with a weapon in each hand. **Pairs of one-handed axes, knives, maces and swords** are supported. Two knives fight like the game's
**Skoll and Hati**; two axes, two maces or two swords swing like the **Berserkir axes**, the game's only other dual move set. Equip a
one-handed weapon, then a second one of the same kind (any two maces, any two swords): it goes into your off hand, replacing your shield
or torch. You need **20 in that weapon skill** (Clubs for maces); for axes, **half of your Woodcutting counts too** (chopping teaches the
axe, not fighting with two). Below it you are told how your total is made up. Hold **Left Alt** while equipping to replace your main weapon instead. Putting away the main weapon moves the off-hand one across;
equipping a shield, torch or another kind of weapon ends dual wielding as usual.

| Balance lever | Value | Why |
| --- | --- | --- |
| Damage | Each hit is 62% of both weapons combined (about 1.24× one weapon), normalized against the dual combo's own multipliers | Stronger than one weapon, below a two-hander's burst and the tier's own dual weapon |
| Stamina | 1.3× the main weapon's per swing | A burst style, not a constant one |
| Blocking | Only the main weapon's weak block, shown with both weapons raised | The shield stays the defensive choice |
| Wear | Both weapons lose durability | Two weapons' upkeep |
| Unlock | 20 in that weapon skill (maces: Clubs; axes: Axes + half of Woodcutting) | Earned, not day-one; lumberjacks get partial credit |

Install for every player: others see the off-hand weapon and the dual stance through the game's own equipment and animation sync. The damage
share, stamina, skill requirement, swap key, and each kind's off-hand position and rotation are in F7. Mixed pairs are future work. Checks cover pairing, the skill gate, the swap key, chain-normalized damage and stamina, plus every native hook. First playtest:

1. With 20+ Axes, equip a one-handed axe, then a second axe. Check it sits in the left hand (adjust Looks in F7 if not), the dual stance and the
   Berserkir moves, primary and secondary. Block: both weapons up, weak block. Below 20, check the message and the normal swap.
2. Repeat with two knives, two maces and two swords. Hold Left Alt while equipping to replace the main weapon instead.
3. Put away the main weapon; the off-hand one moves across. Equip a shield, a torch, a two-hander, then die and respawn: no weapon stuck in a hand.
4. Check GearSlots' shield-follows-weapon does not fight the off-hand weapon, and that a friend sees both weapons and the stance.
5. Compare kill speed and stamina with one axe, the pair, and the Berserkir axes at the same tier.

## Omens

Signs appear in the world that foretell good or bad things, and what they foretell really happens. Every **1.5 in-game days** or so, the
host places an omen 45–85 metres from a player, away from buildings. Walk near it to read it: an on-screen message for players nearby,
a chat line for everyone, and a pin on your map. An omen nobody finds fades after a day without effect.

| Omen | Where | Reading | What happens |
| --- | --- | --- | --- |
| **Dead troll** | Black Forest | "The forest is angry. Perhaps fire would calm it." | Trolls raid the nearest base that night, unless you **use the carcass with 5 resin** to burn it first. |
| **Scattered cairn** | Meadows, Black Forest, Swamp | "The dead are restless. Lay them to rest." | Skeletons raid the nearest base that night, unless you **use it with 5 bone fragments**. |
| **Abandoned camp** | Meadows, Black Forest | "Others passed this way, and did not leave." | Greydwarfs raid the nearest base that night. |
| **Drained deer** | Meadows, Black Forest | "Something hunts in the night." | At night, once someone is home, a hunting pack led by a stronger creature comes for the nearest base. It suits the base's biome: greydwarfs, draugr, wolves, goblins or seekers. |
| **Blood-soaked circle** | Most land | "The moon will bleed tonight. An offering of meat might sate it." | That night the sky turns red and night creatures spawn **2×** as often, up to **1.5×** as many at once, and level up **2×** as often. **Leave 4 raw meat** at the circle to soften it (1.5×, 1.25×, 1.5×); it cannot be averted. It wanes at daybreak. |
| **Gnawed carcass** | Mountains | "The pack is hungry. Meat might turn them away." | Wolves raid the nearest base that night, unless you **leave 3 raw meat** for the pack. |
| **Fuling war banner** | Plains | "They mean to march on you. Tear it down, if you are ready to fight." | Fulings raid the nearest base that night. **Tear the banner down** (free) to avert it, but its three guards attack on the spot. |
| **Drowned man** | Shores | "The drowned do not rest without it." | At night, once someone is home, draugr come ashore for the nearest base, stronger in harder lands, unless you **pay his passage with 10 coins**. |
| **Cursed hoard** | Most land | "What is buried with the dead belongs to the dead." | Leave it and nothing happens. **Take it** for real coins and gems (richer in harder lands), and that night a ghost leads the dead to **whoever took it**, wherever they are. |
| **Unlit grave candles** | Black Forest, Swamp, Mountains | "Without light, the dead walk. Relight them." | Ghosts haunt the nearest base that night, unless you **relight the candles with 3 resin**. |
| **Scorched circle** | Black Forest, Swamp, Plains | "Fire is coming." | Surtlings raid the nearest base that night. |
| **Ship with black sails** | Shores | "It is waiting for the dark. A beacon fire might warn it off." | A dark longship rides offshore with a green light aboard. At night, once someone is home, its draugr crew comes for the nearest base, unless you **light a warning beacon with 20 wood**: the beacon blazes and the ship turns away into the mist. |
| **Lightning-split oak** | Most land | "Thor is angry, and he is coming." | That night **Thor's storm** breaks for 8 minutes for everyone: thunder, rain, and lightning striking near anyone under the open sky. A crackle of light on the ground warns of each strike (18 lightning damage within 2.5 m). **Bury 20 coins** at the oak to keep the storm but stop the strikes. |
| **Circling ravens** | Open sky on most land | "Odin is watching." | Players nearby become rested, and 250 metres of land is revealed on their maps. The ravens circle above the treetops for two more minutes. |
| **Gulls over the shore** | Shores | "The sea is generous." | Real fish are stranded on the shore for the taking. |
| **Dancing lights** | Meadows, Black Forest, Swamp, Mistlands | "They want to show you something." | A light drifts off low over the ground to a **real treasure chest** of that land 40–70 m away, also pinned on the map. |
| **Shed antler** | Meadows, Black Forest | "A great stag walks these woods." | A **great stag** (two-star, larger than life, pinned on the map) appears nearby. It drops a **hard antler** and a deer trophy along with its usual loot. |
| **Fallen star** | Most land | "The sky has sent a gift." | Ore lies cooling in the ash: flint and copper, copper and tin, iron scrap, silver and obsidian, or black metal, depending on the land. |
| **Cloaked wanderer** | Anywhere | "Then he is gone." | He vanishes as you approach. Players nearby learn **every skill 50% faster for 20 minutes**. |
| **Forgotten shrine** | Most land | "Those who remember the gods are remembered by them." | **Leave 3 honey** in its bowl: everyone nearby is rested and sees the land around, and the gods' favour rises by two. Left alone, it is forgotten again. |
| **Humming rune stone** | Anywhere | "Tonight the sky will dance." | That night the **northern lights** fill the sky for everyone, shimmering green to violet: half as many night creatures, and players heal 25% faster and learn every skill 25% faster while they last. |

**The gods' favour.** Every world has a standing with the gods, from −5 to +5, and everyone hears when it changes. Answering an omen
(burning, laying to rest, paying, relighting, tearing down, softening) raises it by one, and an offering at a shrine by two. Letting an
answerable warning come to pass lowers it by one, and robbing a cursed hoard by two. **Favoured** worlds see fewer bad omens (5% fewer per
point). **Beloved** worlds (+3 or more) get half as much again from every gift. **Forsaken** worlds (−3 or less) see omens a quarter more
often, and every pack and hunter comes a level stronger.

**Omens that return.** A bad omen left to come to pass can come back half a day later, worse, near the home it struck, announced as
"The omen returns, worse than before.": a scattered cairn's dead return as unlit grave candles, a drained deer or a gnawed carcass feeds a
blood moon, the drowned man's ship comes looking for him, a dead troll's forest calls down Thor's storm, and an abandoned camp's attackers come
back with fire. How likely depends on the gods' favour: certain for the forsaken, even odds when they only watch, never for the beloved.
A returning omen never returns again.

**The rune bones.** Carve them at a **workbench** from **6 bone fragments and 2 resin**, then use them (hotbar or inventory). The host
reads them for you alone: which way the nearest sign lies and roughly how far, whether it feels warm (good) or cold (bad), how the gods
see you, and what tonight holds (a blood moon, Thor's storm, the northern lights, something gathering, an old omen stirring). A rough
"Omen?" pin marks the region, not the spot. They are never used up; cast them again after 10 seconds.

Readings vary: most omens have two or three ways of saying what they show, so a second dead troll does not read like the first.

About **60%** of omens are bad. A bad omen's raid comes at nightfall when seen by day, or a few minutes later when seen at night. It goes
to the nearest workbench or bed within **1,500 metres** of the sign and uses the game's own raid events, messages and music. It never
interrupts a raid already running, gives up after a day and a half if one keeps it waiting, and a world with **raids turned off** gets only
good omens. After a response, the sign stays 20 seconds so everyone sees it: the troll burns, the candles light, the banner falls, coins rest
on the drowned man's eyes. Signs are visual copies of the game's own models (ragdolls, a cold fire pit, crows, candles, a Fuling banner, a
grave-chest, the wanderer), so they cannot be mined, looted or counted as a base.

**Install on the host and every player.** The host decides everything and keeps a small ledger per world in `BepInEx/config/omens/`;
signs are saved world objects tagged with their own random id. Frequency, the bad chance, how long signs wait, the most open at once, raid
reach and each omen are in F7 → Mod settings (only the host's values count). Later versions can add omens beyond the game's raids.

For testing on the host with Quad's Claude Tools installed, its `omen` command lists omens (`omen list`), places one ahead of the player
(`omen place troll|camp|cairn|deer|bloodmoon|wolves|banner|drowned|hoard|candles|scorched|ship|storm|ravens|catch|wisps|stag|star|wanderer|shrine|aurora [metres]`), lists the game's raid events (`omen events`), brings one to pass at once, night or not (`omen now <id|last>`; a hoard is taken by the host first), responds to one as a player would (`omen avert <id>`), removes one without effect (`omen clear <id>`), shows or sets the gods' favour (`omen fate [-5..5]`), brings returning omens due now (`omen chains`), or schedules the next natural one
in 5 seconds (`omen soon`).

Checks cover omen choice and the 60/40 split, biome fallbacks, intervals, raid timing, the omen state machine, packs, gifts, chests and
nearest-base search, plus native hooks. First playtest:

1. Set IntervalDays to 0.25 on the host. Walk around the Black Forest and Meadows; check signs appear out of sight, away from buildings,
   and look right: a collapsed troll, a cold camp with bones, three crows circling high.
2. Walk up to each. Check the reading on screen nearby, the chat line for a friend far away, and one map pin each.
3. See a dead troll by day; check the troll raid starts at your nearest base at nightfall. See one and burn it with 5 resin instead:
   no raid, the averted message, the sign disappears. Try without enough resin.
4. See a camp at night; the greydwarf raid follows a few minutes later. See ravens; check rested and the revealed map.
5. Leave a sign unseen for a day; it fades. Save/restart the host mid-omen; seen omens still come to pass, unseen ones still wait.
6. Try a world with raids off, a seen bad omen with no base within reach, and an omen seen while another raid is running.

## Spyglass

A bronze spyglass, crafted at the **forge** from **3 bronze**. While it is in your inventory, press **Shift+Z** to raise it to your eye;
**Shift+Z** or **Escape** lowers it. The view narrows to **4×** optical magnification, and the **mouse wheel** changes it from 2× to 8×.
Mouse turning slows to match, and a round eyepiece darkens the screen edges. The view is from your eyes, so your character never
blocks it, and you can keep walking. Opening the inventory, map, menu or chat, dying, or losing the item lowers it.

Where you gaze, the map fills in a little: every quarter second, a **30-metre** circle around the point you look at is uncovered, up to
**600 metres** away. The gaze stops at loaded buildings, trees and ground, then at the world's generated terrain shape or sea level
beyond them; the sky uncovers nothing. Uncovered map is saved with your character like normal exploring. Magnification, radius, range
and the eyepiece are in F7 → Mod settings.

GearSlots binds quick slot 1 to plain **Z**, and its key helper ignores extra held keys. While the whole Shift+Z is held, Spyglass makes
any mod's `Pressed(KeyboardShortcut)` helper report a shorter shortcut on the same key as not pressed, including mods hot-reloaded later.
Plain Z keeps working.

Only the player using it needs the mod for looking and crafting. A spyglass **dropped on the ground** is a world object, so the host and
nearby players need the mod too: a host without it deletes the dropped item when its area loads. Removing the mod removes spyglasses from saved
inventories. The item is a bronze-bar clone with its own prefab name (`BobSpyglass`), model built from cylinders, and a rendered icon.

Checks cover optical magnification, wheel limits, turn scaling, gaze/terrain/sea intersection and shortcut priority, plus native hooks.
First playtest:

1. Check that the recipe appears at a forge once you know bronze. Craft one, then look at its icon, its model when dropped, and its tooltip.
2. Shift+Z with and without the item. Check the view comes from your eyes, the wheel changes magnification, turning feels steady, and Escape
   lowers it without opening the menu.
3. Put something in GearSlots quick slot 1. Shift+Z must not use it, and plain Z must.
4. Gaze at distant hills, the sea, a nearby wall and the sky. Open the map: only the hills, sea and wall areas are uncovered. Save, restart and recheck.
5. Quit fully, restart, and check the log for `Missing prefab hash` and `Failed to find item prefab` with a spyglass in your inventory and one dropped.
6. F6 with it raised, in your inventory, and dropped. The camera must return to normal, and the inventory icon must survive the reload.

## ChestSearch

Press **Ctrl+F** to open your inventory and a nearby-chest search window. Type part of an item's name; multiple words all need to match.
Each slot represents an actual stack from one chest, with its count and quality. Hover for its item tooltip and source chest/distance.
Use **‹ / ›** to page through matches, then **drag a stack onto your inventory**. Empty slots accept the stack; a matching stack takes
only what fits. A different occupied slot refuses the move. Right-click or dropping elsewhere cancels. Escape, Ctrl+F, or × closes search.

The default range is **20 metres**, configurable from **5–40 metres** in F7 → Mod settings. Only loaded, accessible storage containers
are searched, including carts and boat storage. Chests in use, inaccessible wards/private storage, creature bags, tombstones and recyclers
are excluded. Limits: 64 nearest containers, 512 matching stacks, 24 slots per page. Searches localized item names rather than chest names.

Search previews never hold real items. On a drop, the source chest's current owner must approve a normal open request. ChestSearch then
waits for ownership, reloads the actual contents, rechecks range/access and the original stack's metadata, and performs a native inventory
move. A stack changed by another player is rejected and refreshed. It does not force ownership, swap items into the source chest, or drop
preview items into the world. Closing, dying, teleporting, timing out, or F6 before completion moves nothing. A pending native grant arriving
after the mod unloads can open the ordinary chest window, but cannot run a ChestSearch transfer.

Only the player using the search window needs this mod; the host uses normal chest RPCs. Native Linux/Windows, no custom prefab/save data
or DLL dependencies on Quad's mods. This first version supports keyboard search and mouse dragging; controller/touch operation is future work.

Checks cover search/range rules, stack capacity/conservation, asynchronous permission gating, duplicate/late replies and cancellation, plus
native methods and loader-readable symbols. These do not exercise the running UI or live multiplayer. First playtest:

1. Ctrl+F near a mix of chests. Type `wood`, `iron scrap`, and an unmatched name; check slots/counts/source labels, paging and empty results.
2. Drag to an empty inventory slot and a nearly full matching stack; confirm the corresponding chest loses exactly the moved amount.
3. Drop on a different item, another panel, or outside; right-click during a drag. The source and player inventories must stay unchanged.
4. Type words containing E/F/Tab while searching; check typing doesn't activate powers, close the inventory, move, attack or click underlying panels.
5. Have a friend own/open/change the source chest. Check normal permission, busy/private/ward rejection and stale-stack refusal without duplicates.
6. Save/restart after moving items, and F6/close/teleport while dragging or awaiting permission. Check source persistence and cancellation.
7. Check UI scaling and compatibility with GearSlots, QualityOfLife and AICompanion bag panels; opening a normal chest closes search.

## Gary the Greydwarf

A purple greydwarf friend with native creature AI. No API key is needed. Gary jogs at 4 m/s, runs at 7.5 m/s, and starts
running when more than 5 metres behind you. Players and Quad’s AICompanion characters can walk through Gary; terrain and enemy collisions remain active.

- **F3:** summon your Gary, or recall the same Gary beside you.
- **Shift+F3:** tell him to stay where he is. F3 resumes following.

Gary follows you and fights a hostile creature **after it actually hurts you or him**. He also defends himself while waiting,
then resumes waiting when the threat ends. He does not start fights with nearby creatures or players. At **20% health**, he stops
fighting and tries to run **40 metres away** to recover; he returns at **90%**. Damage has a positive health floor and Gary cannot die. Healing continues slowly while escaping if the route is blocked. A persisted recovery
deadline also lets him recover after three minutes of world time while his zone is unloaded. Calling him cannot skip that retreat.

Gary also **rides boats**. Board while he is following nearby (within **12 metres**, **5 metres** of vertical reach), and he hops to a
clear spot on deck. **F3 works aboard** to summon/recall him onto a free deck spot. He rides through turns/waves, pauses fighting/foraging,
and leaves player seats and steering alone. When you get off onto nearby dry ground or a dock, he follows after a short grace period. Falling into
water, dying, teleporting away or telling him to wait leaves him aboard; return or use F3 to recall him. Injured passengers rest/heal aboard.
Toggle **Companion → RideBoats** in F7. Boarding state survives F6/saves; physics and collision settings are restored when released or
reloaded. He remains a separate saved creature, so destroying the boat releases Gary alive. Boat geometry, waves and multiplayer need playtesting.

Gary also has these playful activities, each adjustable in F7:

| Activity | How to try it |
| --- | --- |
| **Fetch** | Carry Wood and press **F8** while looking toward clear ground about nine metres ahead. Gary carries that actual Wood back and waits proudly for an **E** pat. |
| **Emote copying** | Wave, cheer, dance, sit or relax near him; he tries his own version. |
| **Home nest** | **Left Shift+E** on a **wood pile you built**. He makes a leaf/branch nest beside it and curls up when you pause nearby for eight seconds. Repeat on the same pile to forget it. |
| **Rain antics** | Settle down near shelter in rain. He seeks a nearby reachable roof and shakes dry afterward. |
| **Sailing personality** | He prefers a clear forward deck spot, watches ahead, chirps at visible seabirds and crouches during rough waves. |
| **Forest accessories** | A small matte twig-and-feather crown, pouch and ear feather; toggle each under **Appearance**. |
| **Quad friendship** | He greets your nearby AICompanion characters and watches or imitates their building/gathering work. |
| **Show-and-tell** | Before a gift, he holds up a visual preview for two seconds, then tosses the real stash item. |

Fetch uses **one Wood**, follows within **16 metres**, and expires after **35 seconds**. Combat, healing, waiting, boats, portals,
F6 or an ownership split interrupt play; the actual Wood remains an ordinary dropped item and can be picked up. Fetch takes priority over emotes, foraging and crypt guiding; an accepted throw pauses guiding for a minute. Gary chases the ground landing
during flight and retries temporary navigation failures for up to five seconds. Fetch never claims item
ownership or stores Wood in an invisible inventory. The nest references an ordinary saved wood pile and preserves its normal costs/support;
removing the pile removes the nest visual. Emotes baseline on load instead of replaying old player commands. Poses and props are cosmetic:
no armor, skill buffs, extra stash slots or free resources. Gary's native rig lacks wave/sleep/crouch animations, so these use small bone poses.

Your Gary has a **purple marker on the minimap and full map** that follows him while loaded. If only his known saved position is available,
it shows **Gary (last seen)** there; it disappears when no position is known. Toggle **Companion → MapMarker** in F7. The marker is local,
never saved/shared through the cartography table, and cleaned up on F6 or world/player changes.

Gary gathers actual wild **raspberries, blueberries, mushrooms, and loose feathers** into a saved six-item stash. Every roughly **3–5 minutes**,
he can toss you one as a normal ground item. At most two pockets hold feathers, and food gets **80%** of gifts when he has both. An empty stash means no gift until he gathers more. He stays nearby,
skips protected plants and plants close to players, and leaves bushes/mushrooms on their normal regrowth timers. In multiplayer,
he gathers only plants owned by his current simulator; other players' harvests never become his food.

Look at Gary and press your normal **Use key (E)** to pet him: a friendly pat, quiet forest chirp, purple sparkles, and a short happy
dance. Petting has no stat bonus. He greets you after returning from a dungeon/absence and celebrates foes he fought and your nearby victories over huge enemies.
Occasionally, he chirps and looks toward a nearby **visible** danger, sounding nervous around huge creatures. He still starts combat
only after an enemy hurts you or him.

When you stop near a **burning campfire or hearth**, Gary finds a clear spot beside it and relaxes, with an occasional contented wiggle.
While you pause to **build**, he watches from the side, away from your placement ghost. Moving away resumes following. His injury
retreat now searches for reachable, dry ground away from both you and danger, preferring cover; blocked paths retain his slow healing.
Combat and healing override social activities.

Near a loaded **burial chamber, sunken crypt, troll cave, or frost cave**, he may lead ahead and wait for you to catch up. He skips interiors
confirmed fully looted and can revisit unfinished ones after a ten-minute shown-entrance cooldown. Saved chest contents, loose treasures,
cores, scrap piles, collectible cave materials, and removable valuables count; enemies, regrowing mushrooms, and ordinary furniture salvage do not.
He reads saved chest item counts without opening chests, spawning items, or rolling loot. Incomplete/unloaded interiors stay **unknown** and
remain eligible. He remembers up to 512 confirmed cleared entrances per player/world, saved with your character and shared with Gary's simulator.
Older visits have no automatic history: Gary must get a complete nearby scan or observe you inside. Guiding uses nearby loaded entrances;
it does not reveal distant locations or add map pins.

Calls require clear, dry ground. Gary waits outside dungeons and while you are dead. After a portal trip, call him with F3.
He stays in the world when you log out. Food cooldowns, retreat state, and his player identity survive F6 and world saves.
**Install on the host/dedicated server and participating players**: only the creature's network owner runs AI/healing/gifts, and the
server authenticates summons and reuses an existing Gary even when his zone is unloaded. All behavior is scoped to Gary; wild
greydwarfs keep their normal behavior. His save uses the original Greydwarf prefab, so removing the mod leaves a normal tamed
creature without deleting a missing custom prefab.

Settings and keys: F7 → Mod settings, or `BepInEx/config/com.bobisme.gary.cfg`. Campfire companionship, building companionship, reactions, and danger warnings each have a toggle.
The native AI and hot-reload patterns were informed by [Quad's AICompanion](https://github.com/HardHeadHackerHead/valheim-mods/tree/main/mods/AICompanion).

First-playtest checklist (automated policy checks and assembly verification do not exercise the running game):

1. F6, then F3 on clear ground. Check Gary's purple appearance, following and purple pin on both maps; repeatedly call him and confirm one Gary and one marker. Toggle MapMarker in F7, F6 again, switch worlds and leave Gary waiting out of range; check cleanup and the last-seen label.
2. Walk past peaceful creatures. Let a hostile creature hit you, then Gary alone; he should retaliate in both cases, including while waiting. Player/pet hits should not hurt him.
3. Let Gary take heavy/lethal damage. He must flee alive, rest away from you, and return healed; a call during recovery must wait.
4. Walk past wild berries/mushrooms and loose feathers at least four metres from players. Feathers must be within two metres of Gary and already owned by his simulator; he skips player-dropped items, bases, custom/placed items and his own gifts. Check Gary harvests once, then gifts only from his stash. Check normal regrowth, wards (including your own), resource-rate scaling, and F6/save preservation. An empty stash must produce no food.
5. Pet Gary with E while following and waiting. Check one pat/chirp/sparkle/dance per cooldown, normal interactions with other objects, and rejection while healing/fighting. Check petting/reactions on two clients without duplicated sounds.
6. Pause by a burning fire, extinguish it, then walk away. Build and rotate your ghost toward Gary; check he watches from clear ground and resumes following when you move. Danger and actual hits must interrupt every idle activity.
7. Try a raft, karve and longship: board nearby, steer, sit and walk on deck, turn in waves, fall into the water, disembark onto dry ground/a pier and call F3 from aboard/ashore. Check waiting/injury behavior, F6/save/restart aboard, boat destruction, and owner changes with a friend. Gary must not push the boat, fall off, duplicate or remain frozen after release.
8. Return from a dungeon, defeat a foe Gary was fighting, and approach a visible large hostile. Check greetings/celebration/warnings, subdued volume, cooldowns, and no unsolicited attacks.
9. Approach an untouched, partly looted, and fully looted crypt/cave. Check only complete empty scans suppress guiding, unreadable/unloaded interiors remain eligible, and unopened chests are untouched. Leave a core, loose gem, scrap pile, or cave material behind and confirm it remains unfinished. Clear it, wait a few seconds inside, exit, then F6/save/restart and check the cleared mark persists. Gary waits outside; unfinished entrances can be shown again after ten minutes.
10. Walk directly through Gary while following, waiting, retreating, and after F6/respawn. All players and Quad’s AICompanion characters should pass through him, while terrain and enemies still collide and E still pets him. Repeat after reloading each mod independently.
11. Save/restart and F6 with Gary following, waiting, and retreating. Recall from an unloaded zone and check identity/recovery.
12. Repeat on a server with Gary installed everywhere; transfer ownership and check one companion, one simulation, and normal wild AI.
13. F8 with one/multiple Wood, no Wood, blocked aim, a protected target, and another Wood stack nearby. Check exactly one Wood leaves inventory and the same item returns; pet him afterward. Interrupt with damage, waiting, boat boarding, portal, item pickup, F6 and ownership transfer; no duplicates, frozen drops or hidden losses.
14. Use wave/cheer/dance/sit/relax, then F6 and move away. Check pose axes, duration, movement/combat interruptions, and observer agreement. Verify crown/feather placement and independent Appearance toggles.
15. Shift+E on your own wood pile with open space; pause eight seconds, move away, mark another pile, then forget/remove it. Try someone else's pile and a ward. Check F6/save/restart home persistence and ordinary pile materials/support/removal.
16. Pause in rain near a roof and walk away again; watch shelter choice and the dry-off shake. Sail through calm and rough water near birds. Let your Quad companions build/gather and check greetings/imitation. Watch a due gift's preview/toss and interrupt presentation; stash counts must change only when a real gift appears.

## BuildShapes

**Live draft sharing:** players with **BuildShapes 0.5.8+** see your corner posts, outline, roof/storey guides and piece previews in cyan, labeled with your name. Sharing is on by default in all five modes. Hallwright options let you share the draft and include piece meshes; **F7 → Shared drafts** also controls receiving and viewing meshes. Layout sends guides alone; hidden roofs stay hidden. Draft visuals have no collision, terrain effects or build actions. They clear when you submit/exit, and expire after disconnects. Viewers show the nearest four drafts within 120 m, with at most 4,096 shared pieces visible. These limits affect shared visuals, not the plan.

**Hallwright** designs a complete Viking timber shell from your floor plan. Equip a hammer, press **F4 → Hallwright**, and **Shift+click** each corner in boundary order. The first edge sets a **2 m grid**; later edges follow its square directions. **L** opens the live settings. Concave L/U shapes work.

Choose wall height (2–4 m), native roof pitch (26°/45°), entrance type and edge, foundation lift, and **Simple → Crafted → Ornate → Grand → King’s hall** intricacy. Floors, walls, entrance, joined roof wings, gable infill, trusses and terrain-reaching foundations update together. On raised sites, steps descend from the entrance. Ornate styles add patterned timberwork, curved darkwood braces and raven crest ornaments as they unlock. Roof visibility and materials/ghost preview buttons make the framing easy to inspect. Small odd-width gable peaks are intentional vents.

**Entrances:** Auto chooses an unlocked native **gate** at wall heights of 3 m or taller; choose Door/Gate explicitly in the panel. The header clears the full opening. Gates need 3 m walls.

**Tiered longhouse roofs:** choose **Tiered longhouse** under Roof silhouette. Wings at least 6 m wide get low side aisles, a central roof raised by 1 m, enclosed roof steps, matching gable infill and rafters, and a structural inner colonnade. Narrow wings stay gabled; the normal material bill and support checks cover the entire design.

**Longhouse details** adds independent switches for deep **2 m overhangs**, a covered **4 × 2 m entrance porch**, and **sweeping gable timberwork**. Overhang aprons use native 26° roof pieces and bracket beams, fitting around neighboring wings and the porch. Use 3 m or taller walls for 2 m of headroom under the side apron. The porch adds 8 m² of deck, needs a 4 m entrance edge, includes terrain-reaching foundations, and moves the steps to its outer edge. Swept trim uses native 1 m beams along curves on exposed gables at least 4 m wide. Choose Auto/None/Dragon/Raven ridge ends; Auto adds unlocked native carvings at Ornate/Grand. Costs, structural support, planning and undo cover all extra pieces.

**Saved drafts:** Hallwright auto-saves corner positions, entrance hints and every design setting per character/world. Use **F4 → Resume saved Hallwright draft** after F6/restarting or leaving the tool. It rechecks the local preview against current ground, unlocks and support; reopening does not change terrain or submit ghosts. **Delete** clears the outline and its saved copy, and successful submission removes it. Planner-only reloads and temporarily busy planner menus pause shape input without discarding markers. Large-shell API availability is checked before any excavation.

**Layout guides:** corner markers follow the actual terrain and have amber posts at least **4 m tall**, extending to the planned wall height for taller halls. Posts stay visible through occlusion; footprint/storey outlines and roof ridge/gable lines show the intended building volume. Select **Layout** in the preview controls for a clean outline without piece meshes, or switch back to **Materials/Ghosts**. Roof visibility also controls the roof guide. Door markers get tall posts too. These guides have no colliders, can be shared live and clear with the draft.

**Storeys:** choose one, two or three. Wall height applies to each storey. Floors follow the same concave footprint, with native interior stairs, a landing at either end, guarded floor openings and structural joists. The solver keeps columns out of the stair route. Straight flights require a 2 m-wide run of **8 / 10 / 12 m** for 2 / 3 / 4 m storeys including landings, with space beside it. If no flight fits, the preview explains how to enlarge a wing. Auto uses unlocked reinforced timber for taller load-bearing columns. The 2,048-piece limit includes every storey and ornament; large royal designs may need lower intricacy or a smaller footprint.

**Basement:** check the box for a **3 m stone cellar entered from the main floor**. Stone floor/wall recipes and wooden stairs must be unlocked. The ground floor sits just above the outside entrance terrain; a small deck and level apron meet it. The pit follows the floor-cell union with retaining-wall clearance, keeping concave courtyards outside the excavation. Cellar slabs, retaining walls, stairs and the complete upper shell share the same planner group. Previewing changes no ground; **Plan shell** excavates and grades the entrance, then submits the ghosts. Failed submission restores the ground. The site must be clear, above the water table, within native 8 m terrain limits, loaded and accessible. Confirmation acquires loaded terrain through the native ownership API, refreshes synchronized data and rechecks the entire site before editing, including when joining another player. Restoration validates access and saved heights before acquisition, then rechecks for conflicts. Ownership changes mid-operation stop the edit; recovery remains available.

**Ground recovery:** Hallwright keeps one exact recovery record per world in `BepInEx/config/hallwright-ground-<world-id>.json`, surviving F6 and restarts. **U removes only unbuilt ghosts.** Reopen Hallwright for **Restore ground** after removing any built cellar (the planner’s Takedown can remove a recorded add-on shell). Recovery refuses existing buildings and later conflicting terrain edits, preserves paint and untouched vertices, and verifies restored height data. **Keep excavation** discards the recovery record so another cellar can be planned. Draw a new outline after restoring or keeping a site.

**Royal detail:** Ornate/Grand add alternating daylight openings with solid sills and framed sides. Grand adds horizontal carved bands. King’s hall adds repeated knotwork and unlocked darkwood carved panels, alongside the existing swept gables, roof tiers, overhangs, porch and dragon/raven crest choices. Normal recipes, costs and support checks cover all pieces.

**Marked entrances:** after the outline, **Ctrl+click** approximately where doors belong, near exterior walls. The solver fits up to **eight** separate 2 m openings to metre positions within 4 m of each hint, jointly preserving a viable stair route and external decks. Fitted doorway outlines show the result. **Ctrl+click** a marker again, **Ctrl+Backspace**, or its **Remove** button deletes it. The **first marker** receives the covered porch and sets the basement floor height from its exterior landing; additional doors get their own stairs or cellar decks/aprons. Door/Gate selection applies to all entrances. Without entrance markers, choose a single entrance with the edge selector. **Edit outline** closes settings so you can mark doors; **L** reopens it.

**Auto materials** follows the character's actual unlocked hammer recipes. It uses available core-wood framing, stone plinths and darkwood details; reinforced ridge columns are added only when needed and unlocked. You can choose a material style instead. The preview shows normal material costs and required stations. Support is estimated from native collider contacts, actual material losses, terrain and nearby built pieces; unresolved collapses block acceptance. **Plan shell** rechecks the site and turns the result into shared BuildOrders ghosts, then exits shape mode so you can build normally. Ground stays intact unless Basement is selected; existing buildings are never removed. The game's normal placement, materials, station and stability rules still apply when building.

Footprints are bounded to **24 corners / 256 tiles (1,024 m²)** and shells to **2,048 pieces**. Corners can extend **64 m along either grid axis from the first corner**, and roof ridges can span the complete outline without artificial 24 m subdivisions. The piece budget is separate: storeys, basements, foundations and ornament may require lower intricacy before the area limit is reached. All output must remain within 70 m of you; move toward the centre of a large outline before confirming. Backspace edits, Delete clears, and Escape closes settings or exits. **U** removes the last plan's unbuilt ghosts (re-enter Hallwright after planning). Large shells require the whole-building BuildOrders API supplied in [upstream PR #5](https://github.com/HardHeadHackerHead/valheim-mods/pull/5); older API v1 accepts up to 256 pieces. Local previews clear on F6, character/world changes and switching modes; unsubmitted Hallwright drafts remain recoverable from F4. ClaudeTools adds `hall catalog`, `hall rectangle`, `hall outline`, `hall options`, `hall status`, `hall resume`, `hall doors <x,z>...`, `hall doors clear`, `hall stairs`, `hall entrances`, `hall pieces`, `hall ui`, `hall clear`, `hall undo`, `hall restoreground`, `hall keepground` and explicit `hall confirm`; cameras can use `hallpreview`.


Curve, Arch, Mirror, and Repeat tools for **BuildOrders**, using normal pieces, materials, and support rules. The original Curve API has been
merged upstream. Mirror/Repeat also need the new ghost-selection/input API; use the patched planner from [upstream PR #3](https://github.com/HardHeadHackerHead/valheim-mods/pull/3) until it merges. Curve still works with the original API release (BuildOrders 1.9.3 or newer).
The add-on reconnects to the live planner after F6 or individual reloads, without referencing its assembly.

Equip a **hammer**, select a building piece, and close the piece menu:

Press **F4** to choose **Curve**, **Arch**, **Mirror**, or **Repeat** from one window. Press F4/Escape or × to close it.
Choosing the current mode resumes your preview; choosing another clears only that local preview. Submitted ghosts and your last undo
remain. Curve/Arch need a beam or pole selected; Mirror/Repeat need the extended planner API. Unavailable choices explain what is needed.
The picker has a free cursor and an **Exit shape mode** button to return to normal building.

| Controls | Action |
| --- | --- |
| **F4** | Open/close the mode picker: Curve, Arch, Mirror, or Repeat |
| **Left Shift + left click** | Mark curve points, arch endpoints, or the mirror line |
| **Left Ctrl + left click** | Mirror: toggle a source piece/ghost; Repeat: copy a source piece/ghost and its orientation |
| **L** | Curve/Mirror: submit ghosts; Arch/Repeat: open/confirm options |
| **U** in any shape mode | Remove the last submitted shape's unbuilt ghosts |
| **Backspace** | Remove the last marker |
| **Left Ctrl + Backspace** | Mirror: remove the last source selection |
| **Exit shape mode** in the picker | Return to normal hammer building |
| **Escape** | Close options first, or exit the shape mode |

**Curve:** select a beam/pole, press F4, and choose **Curve**. Mark **start, bend, end**; the curve passes through the bend marker and works in 3D,
including vertical arches. The turquoise strokes show piece center lines. Pieces keep their native lengths with overlapping joints;
the first/last endpoints meet your markers. Shorter beams follow tighter bends. Beams/poles need exactly two endpoint snap points,
original prefab scale, and a span of 0.25–8 metres. Tight or degenerate bends are rejected before planning.

**Arch:** select a beam/pole, press **F4**, and choose **Arch**. Shift+click **start and end**; the center-height panel opens
beside the preview. Set the **center rise** with the slider, or type an exact height and press **Enter/Apply**. The ± buttons
step by **0.05 m**, or **0.01 m** while holding Shift. Rise is measured above the line between your endpoints, so 0 m gives a
straight span and unequal endpoint heights work too. The gold guide marks the center; the preview updates as you adjust it.
**Confirm** applies pending edits and submits normal ghosts. **Edit end** replaces the second endpoint while keeping the rise;
**Escape** closes the panel and **L** reopens it. **Cancel** exits; **F4** opens the mode picker. The arch is a vertical parabolic curve with native-length
overlapping beams, whose center lines approximate the curve. Both outer endpoints stay fixed. Ends must be horizontally separated;
shorter beams allow tighter arches. Center rise is limited to **0–32 m**, within the usual curve-length and piece-count limits.
Arch uses the same original planning API as Curve.

**Mirror:** press F4 and choose **Mirror**. Mark **two points** to define a vertical mirror plane; only their horizontal direction matters. Ctrl+click
built pieces or visible planner ghosts to select a group; click again to deselect. Thin wire boxes show the source selection, and heavier
boxes preview the copies. L submits the mirrored group; original pieces/ghosts stay intact. You can mark the line and select pieces in
either order. The reflection axis and pivot compensation are chosen from each piece's native snap layout, with mesh bounds as a fallback.
Triangular under-roof walls and sloped beams use their thickness axis; ordinary roofs keep their width axis, so wedge slopes and roof
pitches mirror correctly. Asymmetric carvings, lettering, or handed decorations
remain their original meshes: inspect the resulting ghosts before building those.

**Repeat:** select a post, decoration, or other building piece, press F4, and choose **Repeat**. Mark **start, bend, end** for its path. After the third
point, an options panel opens beside the wire preview with a free mouse cursor. Choose a **Path anchor** (piece origin, visual center, or
a named native snap point such as Bottom/Top), then adjust **spacing**, **yaw**, **pitch**, **roll**, and whether
pieces **turn with the curve**; the preview updates immediately. Use sliders for broad changes, or type an exact value and press **Enter**
or **Apply**. The ± buttons adjust spacing by **0.05 m** and rotation by **1°**; hold **Shift** for **0.01 m / 0.1°** steps.
**Confirm** also applies any unfinished numeric entries before submitting shared ghosts. **Edit path** lets you replace the end
point, and **Cancel** exits. Escape closes the options while keeping the path; L reopens them. F4 opens the mode picker.

Spacing is a maximum, adjusted evenly along the curve to include both endpoints. Curve-following applies the change in horizontal
heading relative to the first point; seed tilt and initial heading are preserved, then the chosen yaw/pitch/roll offsets are applied.
An upright post stays upright with zero tilt offsets. A vertical tangent keeps the previous heading. The chosen anchor sits on the path
after the final rotation, so a Bottom snap follows the path at the base of a post, or an end snap at the end of a beam. Gold crosses mark
the anchor positions. Piece origin preserves the previous behavior; Visual center uses the mesh bounds. Sampling the same piece type
keeps your anchor choice; choosing a different type resets it to Piece origin. This does not align pieces to terrain automatically. The keyboard shortcuts **[ / ]**, **Page Up / Down**, and **Home** still work outside the panel.
Ctrl+click a built piece/visible ghost outside the panel to copy its prefab and orientation; completed paths reopen the options afterward.
Mark a horizontal curve for a palisade, or an arch to repeat ribs/decorations in three dimensions.

Curves are bounded to 128 metres and all shapes to 256 output pieces. Markers and selected source origins must be within 40 metres;
BuildOrders checks every output pose against its 80-metre reach, unlocks, wards, and no-build rules before accepting anything. Ghost
selection uses visible ghost bounding boxes and respects nearer physical hits; it is approximate rather than precise mesh picking.
Selections are pose snapshots: moving/removing a source afterward does not alter your preview. Scaled and terrain-operation pieces
are excluded. Curve, Arch, Mirror and Repeat never alter terrain. Hallwright alters ground only when an optional basement is confirmed. Pieces build through the normal planner rules.

Use **Exit shape mode** in the F4 picker, or press **Escape** outside the options, before selecting another hammer piece or building normally with **E**. Modes reserve normal hammer placement,
and yield to planner blueprint/bridge placement and menus. The Plans window lists generated groups with Move/Level disabled.
Only the designer needs BuildShapes for submitted plans; live draft sharing requires BuildShapes 0.5.8+ on designer and viewer. Participating builders should use the updated BuildOrders and chosen piece prefabs. Older planners
can receive/build ghosts but still expose terrain actions on add-on groups.

U removes only remaining ghosts; built pieces and terrain stay intact. Undo is one step across all shape tools in the current session,
and clears on death/respawn, F6, or world changes. Saved/shared ghosts remain after the add-on unloads. Settings are in
`BepInEx/config/com.bobisme.buildshapes.cfg`. Radial repeat and ornament presets are future additions.

Automated checks cover two-endpoint arch height/symmetry, curve/station geometry, independently integrated arc spacing, reflected tilted frames, offset pivots, 33 captured native snap layouts (including triangular gables), repeat
bounds and vertical heading, plus reflection dispatch across original/extended/missing/reloaded planners. Shared-preview checks cover full-size packets, malformed data, revision ordering, clear/expiry/rejoin behavior, peer bounds and update cadence. The upstream API has separate
batch, ghost-ray, input-conflict, undo, and world tests. Rendering, multiplayer delivery, and fresh-launch behavior still need playtests:

Open the F4 picker with no mode active and during each tool. Try resuming, switching, Escape/F4/×, Exit shape mode, changing hammer piece, opening another menu, and F6. Picker clicks must never place/attack/mark; switching clears the local preview while submitted ghosts and undo remain.

1. Curve a 1m/2m wood beam horizontally and vertically; compare center lines to submitted ghosts and normal support/material costs.
2. Arch two level and two unequal-height endpoints. Adjust the slider, fractional height, and Shift ± buttons; check endpoints stay fixed. Try Edit end, invalid heights, Enter/Apply, Confirm with a pending edit, Escape/L, and F6 with the panel open. Menu clicks must not move/look/attack/place.
3. Mirror a roof wing, triangular under-roof wall (including inverted variants), sloped beam, and offset-pivot beam across an oblique
   line. Check native snaps, wedge slopes, roof pitch, and copied versus original pieces.
4. Repeat upright posts along a curve. Confirm the options open automatically, cursor is free, spacing/yaw/pitch/roll/follow change the
   preview, and slider clicks never move/look/attack/place. Enter fractional spacing/angles, use ± with/without Shift, and check that
   sliders keep the precise value until dragged. Try invalid/out-of-range entries, Enter/Apply, and Confirm with a pending edit.
   Choose a Bottom/Top/end snap and check that it stays on the path while
   changing pitch/roll/yaw. Try Edit path, Escape/L, Cancel, the F4 picker, then F6 with the panel open.
5. Build one output piece, then U: only its remaining ghosts disappear. Try protected, distant, scaled, and terrain-operation pieces.
6. F6, reload the planner independently, switch worlds, open inventory/F11, or begin a blueprint/bridge. Check input/preview cleanup.
7. Have another player view/build shapes with the updated planner alone. Save/restart and confirm ghosts persist.

For developers, `mise run verify -- --planner-assembly /path/to/BuildOrders.dll` checks the compiled public planning/query interface,
game targets, dependency declaration, catalog metadata, and loader-readable symbols.

## PolygonLeveler

Mark out a building pad and level it, or flatten uneven ground while keeping its average slope. Equip a **hoe**:

| Controls | Action |
| --- | --- |
| **Left Shift + left click** | Put a marker on the ground you aim at |
| **L** | Level the convex polygon horizontally at the **first marker's height** |
| **Shift + L** | Flatten the current ground to its closest fitted plane, keeping its average slope |
| **U** | Undo the last polygon, restoring the previous terrain |
| **Backspace** | Remove the last marker |
| **Delete** | Clear the markers |
| **Escape** or put away the hoe | Stop sending edits to further terrain tiles |

Markers can be placed in any order; their convex hull forms the boundary, so interior markers do not create dents. The turquoise outline initially shows the first marker's horizontal plane. Put the first marker at the height you want, then add at least two more corners. The HUD shows the area and first-marker height before you press L. While Left Shift is held, the normal hoe action is suppressed so placing a marker does not alter the ground. L is only handled during normal play with a hoe; it does not replace QualityOfLife's inventory item-lock key.

**Shift + L** fits a plane to the current ground heights at every selected grid vertex, rather than using the marker heights. It minimizes the sum of squared vertical height changes, removing bumps while retaining the average slope. Shared tile-edge vertices count once in the fit. The outline updates to the fitted plane when you start the action. A polygon whose selected grid vertices are nearly collinear cannot define a stable plane: mark a wider area. The fitted plane must satisfy the same terrain-height, protection, reach, and size limits as horizontal leveling; it is rejected rather than clipped into an uneven surface. U also undoes fitted-plane flattening.

The default limit is 400 m², with all selected vertices within 30 metres of you, at most 16 markers, and 2,048 grid vertices across terrain tiles. Leveling costs one normal hoe use in stamina and durability, with no stone cost. It preserves ground paint and cultivation and honors wards, no-build areas, and the game's normal ±8-metre terrain limits. A tile that fails to rebuild at the requested height is restored before saving.

The mod edits grid vertices inside the boundary, including the matching vertices on both sides of tile seams. Ground triangles blend to unchanged vertices outside the polygon, so the visible edge follows the terrain grid. Local marker previews disappear on reload; the actual terrain changes persist in the ordinary world save. Canceling or losing a multiplayer connection stops further tiles and leaves any completed tile edits in place.

**U** restores the heights, smoothing, and height-modification flags from before your last polygon, including a partially completed polygon. It preserves paint/cultivation and edits outside the selected vertices, and refuses undo if any selected vertex's height data has changed since leveling. You must still be within reach and have ward permission. Undo costs no additional stamina or durability and does not refund the original hoe use. It provides one step, with no redo; a new polygon replaces the previous undo. History is kept in memory for ten minutes after each tile is leveled, with at most 64 retained tile requests per owner. F6/reload, unloaded terrain, disconnecting, or a terrain ownership change can make undo unavailable. Ground leveled with an older version has no undo snapshot.

Undo checks all tiles first, then rechecks and restores each tile before saving. Ownership, permissions, or terrain can change between replies, so a multi-tile undo can stop partway through. Press U again to retry; completed undo tiles are acknowledged without overwriting subsequent edits.

For multiplayer, install PolygonLeveler **0.1.3 or newer** on the host and participating players: a terrain tile's network owner performs and acknowledges its edit. Every owner must confirm preparation before edits begin; an owner without the matching protocol times out without being sent a commit. Owners on 0.1.2 still support L/U but reject Shift + L without edits. Terrain ownership is not forcibly taken. Plain game saves contain only normal terrain data, so the flattened ground remains after removing the mod.

Settings and controls are in F7 → Mod settings, or `BepInEx/config/com.bobisme.polygonleveler.cfg`. Farmhand and PolygonLeveler can be installed independently through the manager.

PolygonLeveler playtest checks:

1. Mark a triangle and rectangle on sloping ground. Press L and verify the interior becomes a plane at the first marker's height, while outside vertices and cultivated/painted ground are preserved.
2. Level across a terrain-tile seam. Confirm both copies of the seam vertices match and the result survives save/reload.
3. Try collinear markers, an oversized area, protected ground, excessive height changes, and an unloaded region. Each should be rejected before edits begin.
4. Have another player own a terrain tile. With the mod installed on both sides, confirm owner replies and persistent edits; without it on the owner, confirm preparation times out without applying the polygon.
5. Press F6, change tools, or disconnect during preparation/application. Confirm local markers and RPC callbacks clean up and normal hoe controls work afterward. Already completed edits remain.
6. Level cultivated ground, then press U: verify original heights/smoothing return and cultivation stays. Change an inside vertex with a normal hoe before U and verify undo refuses; change an outside vertex and verify it remains after undo. Test interrupted undo, repeated U after a lost reply, and undo across a tile seam with another player owning a tile.
7. Mark bumpy sloping ground and press Shift + L. Confirm it forms a sloped plane rather than horizontal ground, its outline follows that plane, and U restores the bumps. Repeat across a tile seam and with an older multiplayer owner, which must reject the action without committing any tile.

## Farmhand

Farmhand lays out a crop field from the boundary you draw, keeping normal crop growth and resource costs.
Equip a **cultivator**, select a crop in its build menu, then **Shift+click** each corner **in boundary order**.
The outline closes automatically after three corners, and you can keep adding corners. Concave **L/U-shaped outlines** retain their
notches; corners are never rearranged into a convex hull. Self-crossings, overlapping edges and degenerate plots show an error.

Rows follow the **first edge**. Minimum spacing defaults to **1.8 metres** and increases for the crop's growth radius. Crop centers stay
half a spacing inside the boundary, including its concave edges. Changing the selected crop or spacing updates the layout. Green dots
are within ordinary placement reach; amber dots need you to walk closer. The actual planting additionally checks cultivation, biome,
sunlight, obstacles, wards and growth space; rejected spots turn red.

| Controls | Action |
| --- | --- |
| **Left Shift + left-click** | Add a plot corner on the aimed ground |
| **Left Shift + J** with a marked plot | Start/resume planting the selected crop throughout the plot |
| **Backspace** | Remove the last corner |
| **Delete** | Clear the marked plot |
| **Escape**, open a menu, or put away the cultivator | Pause the batch; the marked plot and completed spots stay |
| **Left Shift + J** without a marked plot | Plant the original short row around the placement ghost |
| **Left Shift + U** | Harvest nearby mature crops, then replant the same crop types |
| **Left Shift + Left Ctrl + U** | Harvest nearby crops without replanting |

For a larger plot, **walk through the field while it plants nearby spots**. It waits for stamina to recover. Running out of seeds or
breaking the cultivator pauses the job; bring supplies/repair it, then press Shift+J again. Successful spots are remembered, and a
manual restart also retries blocked spots. Editing the boundary, changing crops/spacing, changing characters/worlds or reloading
clears the local layout progress; crops already planted remain normal saved plants. Plots support at most **32 corners**, **512 crop
spots**, and **400 m²** by default (area and marker reach are configurable). The outline may be concave but cannot contain crossing edges
or separate holes; trace an indentation to leave space around an obstacle.

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

Builds and publishing leave your game untouched by default. To deploy all mods' DLLs and PDBs to your own installed ScriptEngine:

```bash
mise run install
```

Press F6 in game afterward, or restart Valheim. BepInEx and ScriptEngine must already be installed. For a published GitHub repository, add its `owner/repo` under F7 → Mod sources; the manager reads `dist/manifest.json` and installs both files.

## Validation

`mise run test` exercises centered crop-row geometry, ordered concave plot containment, border clearance, rotated grid layout, crossing rejection and layout work limits, minimum growth spacing, harvest acknowledgement/timeouts, convex hulls, polygon grid containment, degenerate markers, least-squares plane fitting (slopes, noisy ground, tile-seam weighting, world coordinates, and degenerate samples), native terrain-height limits including hidden saturation and legacy-modifier offsets, and selected-vertex undo snapshots/conflict detection. Builds verify public API usage against the installed game. `mise run verify` checks the private fields/methods and Harmony targets against the installed assembly, and checks all published symbols with the installed ScriptEngine's Cecil. Live multiplayer RPCs and terrain persistence still require the playtests above.

Manual checks for a first playtest:

1. Mark rectangles, L/U-shaped fields and a plot around a path. Check rows follow the first edge, leave borders and keep concave notches empty. Try a crossed outline and remove its bad corner with Backspace.
2. Press Shift+J and walk through a larger plot. Check it plants within normal reach, waits for stamina, and leaves completed dots hidden. Escape/menu pauses; resuming must not duplicate successful spots. Run out of seeds, repair a broken tool, and retry blocked spots.
3. Plant a row on cultivated soil with no marked plot. Check each crop consumes the usual seeds, stamina, and durability; aim near the edge of reach and confirm distant spots are skipped.
4. Try uncultivated soil, a wrong biome, a roof, an occupied spot, and a protected ward. No invalid crop should be planted or paid for.
5. Run out of seeds or stamina midway through a row. The batch should stop without creating free crops.
6. Harvest and replant mature crops. With no seeds, harvesting should still work and leave empty spots. Left Ctrl should harvest only.
7. With a friend hosting, harvest crops whose network owner is the friend. Confirm no duplicate drops and no replant before the harvest response.
8. Change tool, open a menu, die, move away, or press F6 during a batch. Check it stops and restores normal manual planting. Repeated F6 reloads should not duplicate actions.
9. Check seed use from nearby chests with BuildFromChests, and rejection of BuildOrders plan mode or redirected ghosts.

The first version is built and checked against local assemblies; gameplay, multiplayer latency, and F6 reload behavior still need a playtest. No game assemblies are redistributed in this repository.

## Development

Mods live in `mods/<Name>/`. `scripts/publish.py` builds the release DLL/PDB and generates `dist/manifest.json`. Commit generated `dist/` artifacts for the mod manager and bump the version in `Plugin.cs` before publishing updates. Changes to this repository go directly to main; changes to the upstream friend repositories go through pull requests.

Add `cover.png`, `cover.jpg`, or `cover.jpeg` to a mod folder to publish its thumbnail alongside the manifest; PNG takes precedence if more than one exists. Covers are separate from the install files. The current [cover artwork and generation prompts](assets/covers/PROMPTS.md) are saved in this repository.

The build layout follows the MIT-licensed [mod manager template](https://github.com/HardHeadHackerHead/valheim-mod-manager/tree/main/template). Farmhand uses [Harmony patches](https://harmony.pardeike.net/v2/articles/patching) around normal game methods and removes them on unload.
