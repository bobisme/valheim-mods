using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Jarldom
{
    // The game's hooks that make a Player-shaped Humanoid live like a player: one bag shared with its Container, a brain in place of the
    // monster AI, skills without a Skills component, armour, no friendly fire, and its own hover text and E.
    internal static class Body
    {
        internal static readonly AccessTools.FieldRef<Humanoid, Inventory> InventoryOf = AccessTools.FieldRefAccess<Humanoid, Inventory>("m_inventory");
        internal static readonly AccessTools.FieldRef<Container, uint> LastRevision = AccessTools.FieldRefAccess<Container, uint>("m_lastRevision");
        internal static readonly AccessTools.FieldRef<Character, ZSyncAnimation> Anim = AccessTools.FieldRefAccess<Character, ZSyncAnimation>("m_zanim");
        internal static readonly AccessTools.FieldRef<Character, bool> Blocking = AccessTools.FieldRefAccess<Character, bool>("m_blocking");
        internal static readonly AccessTools.FieldRef<Humanoid, float> DrawTime = AccessTools.FieldRefAccess<Humanoid, float>("m_attackDrawTime");
        private static readonly HashSet<int> Loaded = new HashSet<int>();
        internal static bool OpeningBag;

        internal static bool IsBag(Container c) => c != null && c.m_name == VillagerPrefab.BagName && c.GetComponent<Humanoid>() != null;
        internal static void Forget() { Loaded.Clear(); SoulSeen.Clear(); }

        [HarmonyPatch(typeof(Container), "Awake")]
        private static class ShareBag
        {
            private static void Postfix(Container __instance)
            {
                if (!IsBag(__instance) || __instance.GetInventory() == null) return;
                InventoryOf(__instance.GetComponent<Humanoid>()) = __instance.GetInventory();
            }
        }

        // The bag is kept on the villager's still soul, not the walking body (the game sends a moving body whole up to twenty times a
        // second; a bag's list of items is a quarter of it). A copy on the body is the newer one (written while there was no soul here)
        // and is taken over by the next save.
        public const string BagKey = "jarldom_bag";
        private static readonly AccessTools.FieldRef<Container, bool> Loading = AccessTools.FieldRefAccess<Container, bool>("m_loading");
        private static readonly Dictionary<int, uint> SoulSeen = new Dictionary<int, uint>();
        // What's left on the body: a real, empty bag (the game's own loader reads it when Jarldom isn't there to: in a reload, or
        // after an uninstall), not no bytes at all, which it can't read.
        private static byte[] _empty;
        private static byte[] Empty { get { if (_empty == null) { var pkg = new ZPackage(); new Inventory("", null, VillagerPrefab.BagWidth, VillagerPrefab.BagRows).Save(pkg); _empty = pkg.GetArray(); } return _empty; } }
        private static bool Blank(byte[] b) => b == null || b.Length == 0 || b.Length == Empty.Length && b.SequenceEqual(Empty);
        private static byte[] Saved(ZDO body, ZDO soul, out bool onBody)
        {
            byte[] b = body.GetByteArray(ZDOVars.s_items);
            onBody = !Blank(b);
            return onBody ? b : soul?.GetByteArray(BagKey);
        }
        [HarmonyPatch(typeof(Container), "Save")]
        private static class SaveOnSoul
        {
            private static bool Prefix(Container __instance) => !IsBag(__instance) || !ToSoul(__instance);
        }
        // Written to the soul (false: no soul here yet, so on the body, as the game would).
        private static bool ToSoul(Container __instance)
        {
            {
                ZNetView v = __instance.GetComponent<ZNetView>();
                if (v == null || !v.IsValid()) return false;
                ZDO body = v.GetZDO(), soul = SoulStore.Of(body);
                if (soul == null) return false;
                var pkg = new ZPackage(); __instance.GetInventory().Save(pkg);
                if (!soul.IsOwner()) soul.SetOwner(ZDOMan.GetSessionID());
                soul.Set(BagKey, pkg.GetArray());
                if (!(body.GetByteArray(ZDOVars.s_items) is byte[] had && had.Length == Empty.Length && had.SequenceEqual(Empty))) body.Set(ZDOVars.s_items, Empty);
                LastRevision(__instance) = body.DataRevision; SoulSeen[__instance.GetInstanceID()] = soul.DataRevision;
                return true;
            }
        }
        // The owner loads once when it takes over and is the truth after that (or worn gear and wear would be replaced by old copies over
        // and over); another game loads again when the soul's copy changes.
        [HarmonyPatch(typeof(Container), "Load")]
        private static class LoadOnce
        {
            private static bool Prefix(Container __instance, ref bool __result)
            {
                if (!IsBag(__instance)) return true;
                ZNetView v = __instance.GetComponent<ZNetView>();
                if (v == null || !v.IsValid()) return true;
                int id = __instance.GetInstanceID();
                ZDO body = v.GetZDO(), soul = SoulStore.Of(body);
                __result = false;
                if (v.IsOwner()) { if (!Loaded.Add(id)) { LastRevision(__instance) = body.DataRevision; return false; } }
                else
                {
                    Loaded.Remove(id);
                    // (the body's revision moves with every step: only a change to the bag itself, on the soul or the body, is a reason)
                    uint rev = (soul?.DataRevision ?? 0) * 31u + (uint)(Blank(body.GetByteArray(ZDOVars.s_items)) ? 0 : body.GetByteArray(ZDOVars.s_items).Length);
                    if (__instance.IsInUse() || SoulSeen.TryGetValue(id, out uint seen) && seen == rev) return false;
                    SoulSeen[id] = rev;
                }
                LastRevision(__instance) = body.DataRevision;
                byte[] data = Saved(body, soul, out bool onBody);
                if (data == null || data.Length == 0) return false;
                Loading(__instance) = true;
                try { __instance.GetInventory().Load(new ZPackage(data)); } finally { Loading(__instance) = false; }
                if (onBody && v.IsOwner() && soul != null) ToSoul(__instance); // (an old copy on the body: over to the soul at once)
                else if (v.IsOwner() && body.GetByteArray(ZDOVars.s_items) is byte[] none && none.Length == 0) body.Set(ZDOVars.s_items, Empty); // (a blank from an earlier build)
                __result = true;
                return false;
            }
        }

        // The brain instead of the monster's: no wandering, no aggro of its own, no free regeneration.
        [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateAI))]
        private static class Think
        {
            private static bool Prefix(MonsterAI __instance, float dt, ref bool __result)
            {
                if (!VillagerPrefab.Is(__instance)) return true;
                __result = Villagers.Think(__instance, dt);
                return false;
            }
        }

        // Players can't hurt villagers (or each other's colony), and worn armour counts as it does for a player.
        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        private static class Damage
        {
            private static bool Prefix(Character __instance, HitData hit)
            {
                if (!VillagerPrefab.Is(__instance)) return true;
                Character attacker = hit.GetAttacker();
                if (attacker != null && (attacker.IsPlayer() || VillagerPrefab.Is(attacker))) return false;
                // (singed by a hearth they came too near, not a foe's fire: the flames beaten out and a step back; five burned to death
                // at the colony's fire pits feeding them, by them on the benches, the skald at the hearth)
                if (attacker == null && (hit.m_hitType == HitData.HitType.Burning || hit.m_damage.m_fire > 0) && Classes.NearestFire(__instance.transform.position, 5f) is Fireplace hearth)
                {
                    __instance.GetSEMan()?.RemoveStatusEffect(SEMan.s_statusEffectBurning, true);
                    Brain b = Villagers.Brain(__instance);
                    if (b != null && b.Held == null)
                    {
                        Vector3 away = __instance.transform.position - hearth.transform.position; away.y = 0;
                        if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
                        Vector3 to = hearth.transform.position + away.normalized * 3f;
                        b.Move.To(Colony.Spot(to, 1f) ?? to, 0.6f);
                        if (UnityEngine.Random.value < 0.3f) b.Say("Hot! Too close to the fire.", 3);
                    }
                    return false;
                }
                if (__instance is Humanoid h)
                {
                    float armor = 0;
                    foreach (ItemDrop.ItemData item in h.GetInventory()?.GetEquippedItems() ?? new List<ItemDrop.ItemData>()) armor += item.GetArmor();
                    if (armor > 0) hit.ApplyArmor(armor);
                }
                Villagers.Hurt(__instance, hit);
                return true;
            }
        }

        // Skills, kept in the villager's state with the player's formulas.
        [HarmonyPatch(typeof(Character), nameof(Character.RaiseSkill))]
        private static class Raise
        {
            private static bool Prefix(Character __instance, Skills.SkillType skill, float value)
            {
                if (!VillagerPrefab.Is(__instance)) return true;
                Villagers.Brain(__instance)?.RaiseSkill(skill, value);
                return false;
            }
        }
        [HarmonyPatch(typeof(Character), nameof(Character.GetSkillFactor))]
        private static class Factor
        {
            private static void Postfix(Character __instance, Skills.SkillType skill, ref float __result)
            {
                Brain b = Villagers.Brain(__instance);
                if (b != null) __result = b.SkillLevel(skill) / 100f;
            }
        }
        [HarmonyPatch(typeof(Character), nameof(Character.GetSkillLevel))]
        private static class Level
        {
            private static void Postfix(Character __instance, Skills.SkillType skillType, ref float __result)
            {
                Brain b = Villagers.Brain(__instance);
                if (b != null) __result = b.SkillLevel(skillType);
            }
        }
        [HarmonyPatch(typeof(Character), nameof(Character.GetRandomSkillFactor))]
        private static class RandomFactor
        {
            private static void Postfix(Character __instance, Skills.SkillType skill, ref float __result)
            {
                Brain b = Villagers.Brain(__instance);
                if (b == null) return;
                float mid = Mathf.Lerp(0.4f, 1f, b.SkillLevel(skill) / 100f);
                __result = Mathf.Clamp01(UnityEngine.Random.Range(mid - 0.15f, mid + 0.15f));
            }
        }

        // A missed pickaxe swing must not dig holes in the colony.
        [HarmonyPatch(typeof(Attack), nameof(Attack.SpawnOnHitTerrain))]
        private static class NoDigging
        {
            private static bool Prefix(Character character, ref GameObject __result)
            {
                if (character == null || !VillagerPrefab.Is(character)) return true;
                __result = null;
                return false;
            }
        }

        // Death: the colony keeps them (and their worn gear) to rise again; the bag goes to a tombstone.
        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        private static class Death
        {
            private static void Prefix(Character __instance)
            {
                if (!VillagerPrefab.Is(__instance)) return;
                ZNetView v = __instance.GetComponent<ZNetView>();
                if (v == null || !v.IsOwner()) return;
                Plugin.Guard(() => Villagers.Died(__instance as Humanoid));
            }
        }

        // Hover and E: the villager's name and what they're doing; E opens their panel, Shift+E their bag.
        [HarmonyPatch(typeof(Character), nameof(Character.GetHoverText))]
        private static class HoverChar { private static void Postfix(Character __instance, ref string __result) { if (VillagerPrefab.Is(__instance)) __result = Villagers.Hover(__instance); } }
        [HarmonyPatch(typeof(Character), nameof(Character.GetHoverName))]
        private static class HoverName { private static void Postfix(Character __instance, ref string __result) { if (VillagerPrefab.Is(__instance)) __result = Villagers.DisplayName(__instance); } }
        [HarmonyPatch(typeof(Container), nameof(Container.GetHoverText))]
        private static class HoverBag { private static void Postfix(Container __instance, ref string __result) { if (IsBag(__instance)) __result = Villagers.Hover(__instance.GetComponent<Character>()); } }
        [HarmonyPatch(typeof(Container), nameof(Container.GetHoverName))]
        private static class HoverBagName { private static void Postfix(Container __instance, ref string __result) { if (IsBag(__instance)) __result = Villagers.DisplayName(__instance.GetComponent<Character>()); } }

        [HarmonyPatch(typeof(Container), nameof(Container.Interact))]
        private static class Use
        {
            private static bool Prefix(Container __instance, Humanoid character, bool hold, bool alt, ref bool __result)
            {
                if (!IsBag(__instance) || OpeningBag) return true;
                if (hold) { __result = false; return false; }
                __result = true;
                if (character != Player.m_localPlayer) return false;
                if (ZInput.GetButton("AltPlace") || ZInput.GetButton("JoyAltPlace") || alt) return true; // Shift+E: the bag itself
                string vid = __instance.GetComponent<ZNetView>()?.GetZDO()?.GetString(Villagers.VidKey, "") ?? "";
                if (Player.m_localPlayer is Player me && Colony.All.Any(c => c.Valid && Thing.Ready(c) && Thing.Current(c).By == vid && c.Allows(me) && Thing.There(c, me.transform.position))) { Ui.ThingPanel.Recall(); return false; } // (the one speaking at the Thing: the jarl's answer)
                Ui.VillagerPanel.Open(__instance.GetComponent<Humanoid>());
                return false;
            }
        }
        // A hotbar key on a villager hands over one meal or potion, never gear.
        [HarmonyPatch(typeof(Container), nameof(Container.UseItem))]
        private static class Give
        {
            private static bool Prefix(Container __instance, Humanoid user, ItemDrop.ItemData item, ref bool __result)
            {
                if (!IsBag(__instance)) return true;
                __result = Villagers.Feed(__instance.GetComponent<Humanoid>(), user, item);
                return false;
            }
        }
        internal static void OpenBag(Humanoid h)
        {
            Container bag = h != null ? h.GetComponent<Container>() : null;
            if (bag == null || Player.m_localPlayer == null) return;
            OpeningBag = true;
            try { bag.Interact(Player.m_localPlayer, false, false); }
            finally { OpeningBag = false; }
        }

        // Held poses (sleeping, sitting) make the body kinematic: skip the game's motion then, or Unity complains every frame.
        [HarmonyPatch(typeof(Character), "UpdateMotion")]
        private static class Held
        {
            private static bool Prefix(Character __instance)
            {
                if (!VillagerPrefab.Is(__instance)) return true;
                Rigidbody body = __instance.GetComponent<Rigidbody>();
                return body == null || !body.isKinematic;
            }
        }
    }
}
