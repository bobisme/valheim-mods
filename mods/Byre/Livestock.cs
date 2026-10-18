using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Byre
{
    // On every sheep, goat, lamb and kid: when it was last shorn or milked (one long in its ZDO, "byre_last": nothing else is kept), the
    // fleece shown while the wool is grown, the harvest (E with the shears or the pail in the bag, done on the animal's owner, the wool
    // or milk dropped at its feet), and a wild one running from players near.
    internal sealed class Livestock : MonoBehaviour
    {
        internal const string LastKey = "byre_last", TestKey = "byre_test";
        private static readonly int Last = LastKey.GetStableHashCode();
        internal static readonly Dictionary<MonsterAI, Livestock> ByAI = new Dictionary<MonsterAI, Livestock>();

        internal Species Kind; internal bool Young;
        internal ZNetView View; internal Tameable Tame; internal Character Body; internal MonsterAI AI;
        private Renderer _fleece; private float _next;

        private void Awake()
        {
            View = GetComponent<ZNetView>(); Tame = GetComponent<Tameable>(); Body = GetComponent<Character>(); AI = GetComponent<MonsterAI>();
            string prefab = Utils.GetPrefabName(gameObject);
            Kind = Species.Of(prefab); Young = Kind != null && Kind.IsYoung(prefab);
            foreach (SkinnedMeshRenderer r in GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (r.name == Animals.FleeceName) _fleece = r;
            if (View == null || View.GetZDO() == null) return; // (a copy that isn't networked: an icon's, the prefab's)
            View.Register("Byre_Harvest", RPC_Harvest);
            if (AI != null) ByAI[AI] = this;
            Live<Livestock>.Add(this);
            _next = UnityEngine.Random.Range(0f, 2f);
            if (Body != null) Body.m_onDeath += Died;
        }
        private void OnDestroy()
        {
            if (AI != null && ByAI.TryGetValue(AI, out Livestock l) && l == this) ByAI.Remove(AI);
            Live<Livestock>.Remove(this);
        }
        // Twice a second's worth of work spread out: the fleece only changes when the wool grows back or is shorn.
        private void Update()
        {
            if (_fleece == null || Time.time < _next) return;
            _next = Time.time + 2f;
            _fleece.enabled = Young || Ready();
        }

        internal static double Now => ZNet.instance != null ? Husbandry.Seconds(ZNet.instance.GetTime().Ticks) : 0;
        internal static float DayLength => EnvMan.instance != null ? EnvMan.instance.m_dayLengthSec : 1800f;
        // (the period's day: the game's, stretched by the server's RegrowFactor)
        private static float PeriodDay => DayLength * (Plugin.Instance != null ? Plugin.Instance.RegrowFactor.Value : 1f);
        internal double LastAt => View != null && View.IsValid() ? Husbandry.Seconds(View.GetZDO().GetLong(Last, 0L)) : 0;
        internal bool Fed => Tame == null || !Tame.IsHungry();
        internal bool Tamed => Body != null && Body.IsTamed();
        internal bool Ready() => Kind != null && Husbandry.Ready(Kind, LastAt, Now, PeriodDay, Fed);
        internal double Remaining() => Kind == null ? 0 : Husbandry.Remaining(Kind, LastAt, Now, PeriodDay, Fed);
        internal bool Test => View != null && View.IsValid() && View.GetZDO().GetBool(TestKey, false);
        internal void SetLast(double seconds) { if (View != null && View.IsValid()) { View.ClaimOwnership(); View.GetZDO().Set(Last, Husbandry.Ticks(seconds)); } _next = 0; }

        internal static bool HasTool(Humanoid user, Species s) => user != null && user.GetInventory().GetAllItems().Exists(i => i.m_dropPrefab != null && i.m_dropPrefab.name == s.Tool);

        // E on it with the tool in the bag. True: handled (no petting).
        internal bool Use(Humanoid user)
        {
            if (Kind == null || !Husbandry.CanHarvest(Tamed, Young) || !HasTool(user, Kind)) return false;
            if (!Ready()) { user.Message(MessageHud.MessageType.Center, $"{Kind.NotReady}: {Husbandry.Hours(Remaining(), DayLength)}"); return true; }
            View.InvokeRPC("Byre_Harvest");
            user.Message(MessageHud.MessageType.Center, Kind == Species.Sheep ? $"You shear the {Body.m_name.ToLowerInvariant()}." : $"You milk the {Body.m_name.ToLowerInvariant()}.");
            return true;
        }
        // On the owner: once only per regrowth (two players at once get one harvest), the timer set and the yield dropped at its feet.
        private void RPC_Harvest(long sender)
        {
            if (!View.IsOwner() || Kind == null || !Husbandry.CanHarvest(Tamed, Young) || !Ready()) return;
            int n = Husbandry.Yield(Kind, Fed, UnityEngine.Random.value);
            View.GetZDO().Set(Last, ZNet.instance.GetTime().Ticks);
            _next = 0;
            Drop(Kind.Product, n);
            Tame?.m_petEffect.Create(transform.position, transform.rotation);
        }
        private void Drop(string item, int n)
        {
            GameObject prefab = ZNetScene.instance?.GetPrefab(item);
            if (prefab == null || n <= 0) return;
            Vector3 at = transform.position + Vector3.up * 0.6f + UnityEngine.Random.insideUnitSphere * 0.3f;
            GameObject go = Instantiate(prefab, at, Quaternion.Euler(0, UnityEngine.Random.Range(0, 360), 0));
            ItemDrop d = go.GetComponent<ItemDrop>(); ItemDrop.OnCreateNew(d); d.SetStack(n);
        }
        // A sheep killed in full fleece leaves its wool too (CharacterDrop can't know whether it's grown).
        private void Died()
        {
            if (View != null && View.IsOwner() && Kind == Species.Sheep && !Young && Ready()) Drop(Kind.Product, 1);
        }

        internal string Hover()
        {
            if (Kind == null || Young || !Tamed) return "";
            if (Ready()) return HasTool(Player.m_localPlayer, Kind) ? $"\n[<color=yellow><b>$KEY_Use</b></color>] {Kind.Verb} ({Kind.Ready.ToLowerInvariant()})" : $"\n{Kind.Ready} (needs {(Kind == Species.Sheep ? "shears" : "a milk pail")})";
            return $"\n{Kind.NotReady}: {Husbandry.Hours(Remaining(), DayLength)}{(Fed ? "" : " (hungry: slower)")}";
        }

        // ---- the game's hooks ----
        [HarmonyPatch(typeof(Tameable), nameof(Tameable.Interact))]
        private static class Harvest
        {
            private static bool Prefix(Tameable __instance, Humanoid user, bool hold, bool alt, ref bool __result)
            {
                if (hold || alt || !(__instance.GetComponent<Livestock>() is Livestock l)) return true;
                bool used = false;
                Plugin.Guard(() => used = l.Use(user));
                if (used) __result = true;
                return !used;
            }
        }
        // (the shears or pail used from the hotbar on it: the same)
        [HarmonyPatch(typeof(Tameable), nameof(Tameable.UseItem))]
        private static class HarvestWith
        {
            private static bool Prefix(Tameable __instance, Humanoid user, ItemDrop.ItemData item, ref bool __result)
            {
                if (!(__instance.GetComponent<Livestock>() is Livestock l) || l.Kind == null || item?.m_dropPrefab == null || item.m_dropPrefab.name != l.Kind.Tool) return true;
                bool used = false;
                Plugin.Guard(() => used = l.Use(user));
                __result = used;
                return !used;
            }
        }
        [HarmonyPatch(typeof(Tameable), nameof(Tameable.GetHoverText))]
        private static class Readiness
        {
            private static void Postfix(Tameable __instance, ref string __result)
            {
                if (__instance.GetComponent<Livestock>() is Livestock l && l.View != null && l.View.IsValid())
                {
                    string more = "";
                    Plugin.Guard(() => more = l.Hover());
                    if (more.Length > 0) __result += Localization.instance.Localize(more);
                }
            }
        }
        // A wild one runs from a player near (half as near for one crouching), after the game's own AI has had its say: the movement set
        // last is the one the body follows. The game's own flee covers being hurt.
        // Seeing someone far off (a villager, a player, a tame wolf) doesn't keep a wild one alerted, as it would a boar: alerted, it never
        // tames (Tameable.TamingUpdate), so with a colony about it never would. Wary only of a player near or of being hurt.
        private static readonly Func<BaseAI, float, Vector3, bool> Flee = AccessTools.MethodDelegate<Func<BaseAI, float, Vector3, bool>>(AccessTools.Method(typeof(BaseAI), "Flee"));
        private static readonly Action<BaseAI, bool> SetAlerted = AccessTools.MethodDelegate<Action<BaseAI, bool>>(AccessTools.Method(typeof(BaseAI), "SetAlerted"));
        private static readonly AccessTools.FieldRef<MonsterAI, Character> Target = AccessTools.FieldRefAccess<MonsterAI, Character>("m_targetCreature");
        private static readonly AccessTools.FieldRef<BaseAI, float> SinceHurt = AccessTools.FieldRefAccess<BaseAI, float>("m_timeSinceHurt");
        [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateAI))]
        private static class Shy
        {
            private static void Postfix(MonsterAI __instance, float dt, bool __result)
            {
                if (!__result || !ByAI.TryGetValue(__instance, out Livestock l) || l.Kind == null || l.Tamed) return;
                Player p = Player.GetClosestPlayer(l.transform.position, l.Kind.FleeRange * 2);
                bool flees = p != null && Husbandry.Flees(l.Kind, false, Vector3.Distance(p.transform.position, l.transform.position), p.IsCrouching());
                if (flees) { Flee(__instance, dt, p.transform.position); return; }
                if (p == null && __instance.IsAlerted() && SinceHurt(__instance) > __instance.m_fleeTimeSinceHurt) { Target(__instance) = null; SetAlerted(__instance, false); }
            }
        }
        // (and isn't alerted by them in the first place: calmed each frame and alerted again, its alert sound would play over and over)
        [HarmonyPatch(typeof(MonsterAI), "SetAlerted")]
        private static class Calm
        {
            private static bool Prefix(MonsterAI __instance, bool alert)
            {
                if (!alert || !ByAI.TryGetValue(__instance, out Livestock l) || l.Kind == null || l.Tamed) return true;
                if (SinceHurt(__instance) <= __instance.m_fleeTimeSinceHurt || Player.GetClosestPlayer(l.transform.position, l.Kind.FleeRange * 2) != null) return true;
                Target(__instance) = null;
                return false;
            }
        }
    }
}
