using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Byre
{
    // On every animal of ours, grown or young: when it was last shorn, milked or plucked (one long in its ZDO, "byre_last"), the fleece
    // shown while the wool is grown, the harvest (E with the shears or the pail in the bag, a goose plucked by hand; done on the animal's
    // owner, the wool, milk or feathers dropped at its feet), and a wild one running from players near. A tame goose chases strangers
    // (the players who have petted it are kept in "byre_known"); a tame pig led through the Black Forest roots up truffles ("byre_rooted":
    // when it last found one).
    internal sealed class Livestock : MonoBehaviour
    {
        internal const string LastKey = "byre_last", TestKey = "byre_test", KnownKey = "byre_known", RootedKey = "byre_rooted";
        private static readonly int Last = LastKey.GetStableHashCode(), Known = KnownKey.GetStableHashCode(), Rooted = RootedKey.GetStableHashCode();
        internal static readonly Dictionary<MonsterAI, Livestock> ByAI = new Dictionary<MonsterAI, Livestock>();

        internal Species Kind; internal bool Young;
        internal ZNetView View; internal Tameable Tame; internal Character Body; internal MonsterAI AI;
        private Renderer _fleece; private float _next, _rootAt;

        private void Awake()
        {
            View = GetComponent<ZNetView>(); Tame = GetComponent<Tameable>(); Body = GetComponent<Character>(); AI = GetComponent<MonsterAI>();
            string prefab = Utils.GetPrefabName(gameObject);
            Kind = Species.Of(prefab); Young = Kind != null && Kind.IsYoung(prefab);
            foreach (SkinnedMeshRenderer r in GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (r.name == Animals.FleeceName) _fleece = r;
            if (View == null || View.GetZDO() == null) return; // (a copy that isn't networked: an icon's, the prefab's)
            View.Register("Byre_Harvest", RPC_Harvest);
            View.Register<long>("Byre_Know", RPC_Know);
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
        // Every 2 s: the fleece (it only changes when the wool grows back or is shorn); a pig's nose to the ground.
        private void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + 2f;
            if (_fleece != null) _fleece.enabled = Young || Ready();
            if (Kind != null && Kind.Roots && Time.time >= _rootAt) { _rootAt = Time.time + Husbandry.RootCheckSeconds; Root(false); }
        }

        internal static double Now => ZNet.instance != null ? Husbandry.Seconds(ZNet.instance.GetTime().Ticks) : 0;
        internal static float DayLength => EnvMan.instance != null ? EnvMan.instance.m_dayLengthSec : 1800f;
        // (the period's day: the game's, stretched by the server's RegrowFactor)
        private static float PeriodDay => DayLength * (Plugin.Instance != null ? Plugin.Instance.RegrowFactor.Value : 1f);
        internal double LastAt => View != null && View.IsValid() ? Husbandry.Seconds(View.GetZDO().GetLong(Last, 0L)) : 0;
        internal bool Fed => Tame == null || !Tame.IsHungry();
        internal bool Tamed => Body != null && Body.IsTamed();
        internal bool Ready() => Kind != null && Kind.HasProduct && Husbandry.Ready(Kind, LastAt, Now, PeriodDay, Fed);
        internal double Remaining() => Kind == null || !Kind.HasProduct ? 0 : Husbandry.Remaining(Kind, LastAt, Now, PeriodDay, Fed);
        internal bool Test => View != null && View.IsValid() && View.GetZDO().GetBool(TestKey, false);
        internal void SetLast(double seconds) { if (View != null && View.IsValid()) { View.ClaimOwnership(); View.GetZDO().Set(Last, Husbandry.Ticks(seconds)); } _next = 0; }

        // (no tool asked for, a goose's feathers: by hand)
        internal static bool HasTool(Humanoid user, Species s) => user != null && (s.Tool == null || user.GetInventory().GetAllItems().Exists(i => i.m_dropPrefab != null && i.m_dropPrefab.name == s.Tool));
        private static string ToolText(Species s) => s.Tool == "ByreShears" ? "shears" : "a milk pail";

        // E on it with the tool in the bag. True: handled (no petting). By hand (a goose), only when its feathers are in: else it's petted.
        internal bool Use(Humanoid user)
        {
            if (Kind == null || !Kind.HasProduct || !Husbandry.CanHarvest(Tamed, Young) || !HasTool(user, Kind)) return false;
            if (!Ready())
            {
                if (Kind.Tool == null) return false;
                user.Message(MessageHud.MessageType.Center, $"{Kind.NotReady}: {Husbandry.Hours(Remaining(), DayLength)}"); return true;
            }
            View.InvokeRPC("Byre_Harvest");
            user.Message(MessageHud.MessageType.Center, $"You {Kind.Verb.ToLowerInvariant()} the {Body.m_name.ToLowerInvariant()}.");
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
            if (!Kind.HasProduct) return Kind.Roots ? "\nLead it through the Black Forest: it roots for truffles" : "";
            if (Ready()) return HasTool(Player.m_localPlayer, Kind) ? $"\n[<color=yellow><b>$KEY_Use</b></color>] {Kind.Verb} ({Kind.Ready.ToLowerInvariant()})" : $"\n{Kind.Ready} (needs {ToolText(Kind)})";
            return $"\n{Kind.NotReady}: {Husbandry.Hours(Remaining(), DayLength)}{(Fed ? "" : " (hungry: slower)")}";
        }

        // ---- the goose's guard ----
        // The players it knows: those who have petted it (or plucked it). Kept on its ZDO, written by its owner.
        internal bool Knows(long player) => View != null && View.IsValid() && ("," + View.GetZDO().GetString(Known, "") + ",").Contains("," + player + ",");
        internal void Know(Humanoid user)
        {
            if (!(user is Player p) || Kind == null || !Kind.Guards || !Tamed || View == null || !View.IsValid() || Knows(p.GetPlayerID())) return;
            View.InvokeRPC("Byre_Know", p.GetPlayerID());
        }
        private void RPC_Know(long sender, long player)
        {
            if (!View.IsOwner() || Knows(player)) return;
            string had = View.GetZDO().GetString(Known, "");
            View.GetZDO().Set(Known, had.Length == 0 ? player.ToString() : had + "," + player);
        }
        private Character _chasing; private float _chaseStart, _restUntil, _lookAt;
        internal Character Chasing => _chasing;
        // On its owner, after the game's AI (the movement set last is the one the body follows): after a stranger near with a honk, up to
        // ChaseStop short of it, then a rest. A creature that's dead, gone or got away, or a chase gone on too long: let be.
        internal void Guard(MonsterAI ai, float dt)
        {
            if (_chasing != null)
            {
                float d = Vector3.Distance(_chasing.transform.position, transform.position);
                if (_chasing.IsDead() || !Husbandry.KeepsChasing(d, Time.time - _chaseStart))
                {
                    if (d <= Husbandry.ChaseStop) Honk(ai);
                    _chasing = null; _restUntil = Time.time + Husbandry.ChaseRest;
                    return;
                }
                MoveTo(ai, dt, _chasing.transform.position, Husbandry.ChaseStop, true);
                return;
            }
            if (Time.time < _lookAt) return;
            _lookAt = Time.time + 1f;
            Character found = null; float best = float.MaxValue;
            foreach (Character c in Character.GetAllCharacters())
            {
                if (c == null || c == Body || c.IsDead() || c.IsBoss()) continue;
                float d = Vector3.Distance(c.transform.position, transform.position);
                if (d >= best) continue;
                bool player = c is Player;
                bool stranger = Husbandry.Stranger(player, player && Knows(((Player)c).GetPlayerID()), c.IsTamed(), c.GetFaction() == Character.Faction.Players);
                if (Husbandry.Chases(Kind, Tamed, Young, stranger, d, Time.time < _restUntil)) { found = c; best = d; }
            }
            if (found == null) return;
            _chasing = found; _chaseStart = Time.time;
            Honk(ai);
        }
        private void Honk(MonsterAI ai)
        {
            EffectList e = ai.m_alertedEffects?.m_effectPrefabs?.Length > 0 ? ai.m_alertedEffects : ai.m_idleSound;
            e?.Create(transform.position, transform.rotation);
        }

        // ---- the pig's truffles ----
        // On its owner, once a minute: led through the Black Forest (following someone), now and then a truffle at its feet. Forced (the
        // test tool): whatever the roll and the gap. What happened, for the tool.
        internal string Root(bool force)
        {
            if (Kind == null || !Kind.Roots || View == null || !View.IsValid()) return "not a pig";
            if (!View.IsOwner()) return "not its owner";
            GameObject follow = AI != null ? AI.GetFollowTarget() : null;
            bool forest = Heightmap.FindBiome(transform.position) == Heightmap.Biome.BlackForest;
            double since = Now - Husbandry.Seconds(View.GetZDO().GetLong(Rooted, 0L));
            if (since < 0) since = double.MaxValue; // (a clock set back)
            float minutes = Plugin.Instance != null ? Plugin.Instance.TruffleMinutes.Value : 10f;
            if (!Husbandry.Roots(Kind, Tamed, Young, follow != null, forest, force ? double.MaxValue : since, force ? 0 : UnityEngine.Random.value, minutes))
                return !Tamed || Young ? "not a tame grown pig" : follow == null ? "not following anyone" : !forest ? "not in the Black Forest" : "nothing this time";
            View.GetZDO().Set(Rooted, ZNet.instance.GetTime().Ticks);
            Drop("ByreTruffle", 1);
            Tame?.m_petEffect.Create(transform.position, transform.rotation);
            if (follow != null && follow == Player.m_localPlayer?.gameObject) Player.m_localPlayer.Message(MessageHud.MessageType.Center, $"The {Body.m_name.ToLowerInvariant()} roots up a truffle!");
            return "truffle";
        }

        // ---- the game's hooks ----
        [HarmonyPatch(typeof(Tameable), nameof(Tameable.Interact))]
        private static class Harvest
        {
            private static bool Prefix(Tameable __instance, Humanoid user, bool hold, bool alt, ref bool __result)
            {
                if (hold || alt || !(__instance.GetComponent<Livestock>() is Livestock l)) return true;
                bool used = false;
                Plugin.Guard(() => { used = l.Use(user); l.Know(user); }); // (petted or plucked: a goose knows you after)
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
        private static readonly Func<BaseAI, float, Vector3, float, bool, bool> MoveTo = AccessTools.MethodDelegate<Func<BaseAI, float, Vector3, float, bool, bool>>(AccessTools.Method(typeof(BaseAI), "MoveTo"));
        [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateAI))]
        private static class Shy
        {
            private static void Postfix(MonsterAI __instance, float dt, bool __result)
            {
                if (!__result || !ByAI.TryGetValue(__instance, out Livestock l) || l.Kind == null) return;
                if (l.Tamed) { if (l.Kind.Guards && !l.Young) Plugin.Guard(() => l.Guard(__instance, dt)); return; }
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
