using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Byre
{
    // The animals, made from the game's boar (its skeleton, its animations, its Humanoid + MonsterAI + Tameable + Procreation, which is what
    // taming by food needs: the deer's AnimalAI can't be tamed). The young from the boar's piglet (the same skeleton at half size, and the
    // game's Growup), the bodies on death from the boar's ragdoll. Each wears its own Blender model; the sheep its fleece as a second
    // mesh on the same bones (Livestock shows it while the wool is grown). The boar's tusks and grunts go: the deer's voice in their place.
    internal static class Animals
    {
        internal const string FleeceName = "Fleece";

        internal static GameObject Make(ZNetScene scene, Transform holder, Species s, bool young, GameObject adult, GameObject ragdoll)
        {
            GameObject from = scene.GetPrefab(young ? s.YoungFrom : s.From);
            if (from == null) { Plugin.Warn($"No {(young ? s.YoungFrom : s.From)} to make the {s.Title} from."); return null; }
            GameObject go = Object.Instantiate(from, holder, false); go.name = young ? s.Young : s.Prefab;
            if (!Dress(go, s)) { Object.DestroyImmediate(go); return null; }
            GameObject deer = scene.GetPrefab("Deer");

            Character ch = go.GetComponent<Character>();
            ch.m_name = young ? s.YoungTitle : s.Title;
            ch.m_faction = Character.Faction.AnimalsVeg; // (everyone's enemy: it runs, it never hunts)
            ch.m_health = young ? s.Health / 2 : s.Health;
            ch.m_boss = false;
            if (go.GetComponent<Humanoid>() is Humanoid h)
            {
                // (no tusks: nothing to attack with, so an alerted one keeps its distance instead of charging)
                h.m_defaultItems = new GameObject[0]; h.m_randomWeapon = new GameObject[0]; h.m_randomArmor = new GameObject[0]; h.m_randomShield = new GameObject[0];
                h.m_randomSets = new Humanoid.ItemSet[0]; h.m_randomItems = new Humanoid.RandomItem[0];
            }
            Character deerCh = deer != null ? deer.GetComponent<Character>() : null;
            if (deerCh != null) ch.m_hitEffects = Copy(deerCh.m_hitEffects, null);
            // (no body for the young: a grown one's would lie there; without one, what it drops falls where it died)
            ch.m_deathEffects = Copy(deerCh != null ? deerCh.m_deathEffects : ch.m_deathEffects, young ? null : ragdoll);

            MonsterAI ai = go.GetComponent<MonsterAI>();
            if (ai != null)
            {
                ai.m_consumeItems = s.Foods.Select(f => Costs.FindItem(f)).Where(f => f != null).ToList();
                ai.m_fleeIfLowHealth = 1f; ai.m_fleeTimeSinceHurt = 20f; // (hurt at all: it runs)
                ai.m_enableHuntPlayer = false; ai.m_attackPlayerObjects = false;
                ai.m_jumpInterval = s == Species.Goat ? 9f : 0f; // (a goat hops up the rocks now and then)
                if (deer != null && deer.GetComponent<BaseAI>() is BaseAI deerAi) { ai.m_idleSound = Copy(deerAi.m_idleSound, null); ai.m_idleSoundInterval = deerAi.m_idleSoundInterval; ai.m_idleSoundChance = deerAi.m_idleSoundChance; }
            }
            if (go.GetComponent<Tameable>() is Tameable tame)
            {
                tame.m_tamingTime = s.TamingSeconds; tame.m_fedDuration = s.FedSeconds; tame.m_commandable = false;
                tame.m_saddleItem = null; tame.m_saddle = null; tame.m_randomStartingName = new List<string>();
            }
            Procreation breed = go.GetComponent<Procreation>();
            if (young) { if (breed != null) Object.DestroyImmediate(breed); }
            else if (breed != null)
            {
                breed.m_offspring = null; breed.m_seperatePartner = null; breed.m_noPartnerOffspring = null; // (set to the young below, once it's made)
                breed.m_maxCreatures = s.MaxNear;
            }
            if (go.GetComponent<Growup>() is Growup grow)
            {
                if (young) { grow.m_grownPrefab = adult; grow.m_growTime = s.GrowSeconds; grow.m_altGrownPrefabs = new List<Growup.GrownEntry>(); grow.m_inheritTame = true; }
                else Object.DestroyImmediate(grow);
            }
            Drops(go, s, young);
            go.AddComponent<Livestock>();
            return go;
        }
        internal static void Breed(GameObject adult, GameObject young)
        {
            if (adult != null && adult.GetComponent<Procreation>() is Procreation p) p.m_offspring = young;
        }

        // What's left when one dies: meat, and its hide (a sheep's fleece with it; leather scraps for a goat).
        private static void Drops(GameObject go, Species s, bool young)
        {
            CharacterDrop drops = go.GetComponent<CharacterDrop>() ?? go.AddComponent<CharacterDrop>();
            drops.m_drops = new List<CharacterDrop.Drop>();
            void Add(string item, int min, int max, float chance)
            {
                GameObject prefab = Costs.FindItem(item)?.gameObject;
                if (prefab != null) drops.m_drops.Add(new CharacterDrop.Drop { m_prefab = prefab, m_amountMin = min, m_amountMax = max + 1, m_chance = chance, m_levelMultiplier = true });
            }
            if (young) { Add("RawMeat", 1, 1, 0.5f); return; }
            Add("RawMeat", 1, 2, 1f);
            if (s == Species.Sheep) Add("LeatherScraps", 1, 1, 0.5f);
            else Add("LeatherScraps", 1, 2, 1f);
            // (the fleece itself drops from Livestock on death while it's grown: CharacterDrop can't know that)
        }

        // An effect list copied, the game's ragdoll in it swapped for ours (null: left out).
        private static EffectList Copy(EffectList from, GameObject ragdoll)
        {
            var list = new EffectList { m_effectPrefabs = new EffectList.EffectData[0] };
            if (from?.m_effectPrefabs == null) return list;
            var data = new List<EffectList.EffectData>();
            foreach (EffectList.EffectData e in from.m_effectPrefabs)
            {
                if (e == null || e.m_prefab == null) continue;
                bool body = e.m_prefab.GetComponent<Ragdoll>() != null;
                if (body && ragdoll == null) continue;
                data.Add(new EffectList.EffectData { m_prefab = body ? ragdoll : e.m_prefab, m_enabled = e.m_enabled, m_variant = e.m_variant, m_attach = e.m_attach, m_follow = e.m_follow,
                    m_inheritParentRotation = e.m_inheritParentRotation, m_inheritParentScale = e.m_inheritParentScale, m_multiplyParentVisualScale = e.m_multiplyParentVisualScale, m_randomRotation = e.m_randomRotation, m_scale = e.m_scale, m_childTransform = e.m_childTransform });
            }
            if (ragdoll != null && !data.Any(e => e.m_prefab == ragdoll)) data.Add(new EffectList.EffectData { m_prefab = ragdoll, m_enabled = true });
            list.m_effectPrefabs = data.ToArray();
            return list;
        }

        // The body that falls: the boar's ragdoll in our model.
        internal static GameObject Ragdoll(ZNetScene scene, Transform holder, Species s)
        {
            GameObject from = scene.GetPrefab(s.RagdollFrom);
            if (from == null) { Plugin.Warn($"No {s.RagdollFrom} for the {s.Title}'s body."); return null; }
            GameObject go = Object.Instantiate(from, holder, false); go.name = s.Ragdoll;
            if (!Dress(go, s)) { Object.DestroyImmediate(go); return null; }
            if (go.GetComponent<Ragdoll>() is Ragdoll r) r.m_mainModel = Main(go);
            return go;
        }

        // The boar's look off, ours on: its main skinned mesh takes our body (same bones, same bind poses), the tusks and LODs go, the
        // fleece goes on as a copy of the body's renderer wearing the fleece mesh.
        private static bool Dress(GameObject go, Species s)
        {
            SkinnedMeshRenderer main = Main(go);
            if (main == null || main.sharedMesh == null) { Plugin.Warn($"{go.name}: no skinned mesh to dress"); return false; }
            Mesh body = Models.Skinned(s.Key, main.sharedMesh);
            Material mat = Models.Material(s.Key, main.sharedMaterial);
            if (body == null || mat == null) return false;
            foreach (LODGroup lod in go.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(lod);
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true).Where(r => r != main && !(r is ParticleSystemRenderer)).ToList())
                if (r != null && r.transform.childCount == 0) Object.DestroyImmediate(r.gameObject); else if (r != null) Object.DestroyImmediate(r);
            main.sharedMesh = body; main.sharedMaterials = new[] { mat };
            main.enabled = true;
            if (s == Species.Sheep && Models.Skinned(s.Key + "_fleece", main.sharedMesh) is Mesh wool)
            {
                GameObject f = Object.Instantiate(main.gameObject, main.transform.parent, false); f.name = FleeceName;
                var fr = f.GetComponent<SkinnedMeshRenderer>(); fr.sharedMesh = wool; fr.sharedMaterials = new[] { mat }; fr.bones = main.bones; fr.rootBone = main.rootBone;
                f.transform.localPosition = main.transform.localPosition; f.transform.localRotation = main.transform.localRotation; f.transform.localScale = main.transform.localScale;
            }
            if (go.GetComponentInChildren<LevelEffects>(true) is LevelEffects lv)
            {
                // (stars: a little bigger, never the boar's colour shifts; nothing of the boar's own switched on)
                lv.m_mainRender = main; lv.m_baseEnableObject = null;
                foreach (LevelEffects.LevelSetup l in lv.m_levelSetups) { l.m_hue = 0; l.m_saturation = 0; l.m_value = 0; l.m_enableObject = null; l.m_setEmissiveColor = false; }
            }
            return true;
        }
        internal static SkinnedMeshRenderer Main(GameObject go)
        {
            var all = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            return all.FirstOrDefault(r => r.name == "Poly Art Boar") ?? all.Where(r => r.name != FleeceName && r.sharedMesh != null).OrderByDescending(r => r.sharedMesh.vertexCount).FirstOrDefault();
        }

        // ---- wild herds ----
        // One spawn list of our own, put on every zone's SpawnSystem as it wakes (the game's lists are prefabs shared by all; ours is beside them).
        private static SpawnSystemList _list;
        internal static SpawnSystemList Spawns(Transform holder, IDictionary<string, GameObject> made)
        {
            if (_list != null) return _list;
            _list = holder.gameObject.AddComponent<SpawnSystemList>();
            foreach (Species s in Species.All)
            {
                if (!made.TryGetValue(s.Prefab, out GameObject prefab) || prefab == null) continue;
                _list.m_spawners.Add(new SpawnSystem.SpawnData
                {
                    m_name = "Byre " + s.Key, m_prefab = prefab, m_enabled = false, // (Kit.Tick switches it on: the setting, and a server that has the mod)
                    m_biome = (Heightmap.Biome)System.Enum.Parse(typeof(Heightmap.Biome), s.Biome), m_biomeArea = Heightmap.BiomeArea.Median,
                    m_maxSpawned = 3, m_spawnInterval = 600, m_spawnChance = 25, m_spawnDistance = 40,
                    m_groupSizeMin = s.HerdMin, m_groupSizeMax = s.HerdMax, m_groupRadius = 5,
                    m_minTilt = s.MinTilt, m_maxTilt = s.MaxTilt, m_minAltitude = 1, m_inForest = s == Species.Goat, m_outsideForest = true,
                    m_spawnAtDay = true, m_spawnAtNight = false, m_huntPlayer = false, m_groundOffset = 0.5f, m_minLevel = 1, m_maxLevel = 2,
                });
            }
            return _list;
        }
        internal static void Settle(bool on)
        {
            if (_list == null || Plugin.Instance == null) return;
            foreach (SpawnSystem.SpawnData d in _list.m_spawners)
            {
                Species s = Species.Of(d.m_prefab != null ? d.m_prefab.name : null);
                d.m_enabled = on;
                d.m_spawnChance = s == Species.Goat ? Plugin.Instance.GoatChance.Value : Plugin.Instance.SheepChance.Value;
                d.m_spawnInterval = Plugin.Instance.SpawnInterval.Value;
            }
        }
        internal static void Forget() => _list = null;

        [HarmonyLib.HarmonyPatch(typeof(SpawnSystem), "Awake")]
        private static class AddSpawns
        {
            private static void Postfix(SpawnSystem __instance) => Plugin.Guard(() =>
            {
                if (_list != null && !__instance.m_spawnLists.Contains(_list)) { __instance.m_spawnLists.RemoveAll(l => l == null); __instance.m_spawnLists.Add(_list); }
            });
        }
    }
}
