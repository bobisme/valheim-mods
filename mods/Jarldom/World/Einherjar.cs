using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Jarldom
{
    // They answer back: in a raid the colony is losing (a raider at the hall or the pillars, or half its fighters down), a Valkyrie
    // brings one of those gone to Valhalla back to fight: glowing, in the gear they went up in, named over their head, for 90 seconds.
    // The worthiest not yet come back; once a soul, one a raid (the rules in Returning). The body is the villager's with the game's
    // own monster mind in place of a villager's (it finds foes and fights them by itself), not saved with the world.
    internal static class Einherjar
    {
        public const string Name = "JarldomEinherjar";
        public static readonly int Hash = Name.GetStableHashCode();
        public const string NameKey = "jarldom_ein_name", UntilKey = "jarldom_ein_until", ColonyKey = "jarldom_ein_colony", SayKey = "jarldom_ein_say", SayTimeKey = "jarldom_ein_sayt";
        public const string GearKey = "jarldom_soulgear:"; // (+ vid, on the pillars: what each soul went up in)
        private const float Drop = 15.5f; // (the Valkyrie's flight in, to where she lets them down)
        private static GameObject _prefab;
        public static GameObject Prefab => _prefab;
        private static readonly Dictionary<string, (string vid, Vector3 at, float due)> Coming = new Dictionary<string, (string, Vector3, float)>(); // (by colony)
        private static readonly Dictionary<ZDOID, int> Kills = new Dictionary<ZDOID, int>();
        private static readonly Dictionary<ZDOID, long> Shown = new Dictionary<ZDOID, long>();
        private static float _next, _nextWatch;

        public static bool Is(Component c) => c != null && c.GetComponent<ZNetView>() is ZNetView v && v.IsValid() && v.GetZDO().GetPrefab() == Hash;

        // A copy of the villager's body: no bag, a glow about it, tougher, and the game's monster mind left to run it.
        public static void Ensure()
        {
            if (_prefab != null || VillagerPrefab.Prefab == null) return;
            GameObject go = Object.Instantiate(VillagerPrefab.Prefab, Assets.Holder, false);
            go.name = Name;
            Container bag = go.GetComponent<Container>(); if (bag != null) Object.DestroyImmediate(bag);
            Humanoid h = go.GetComponent<Humanoid>();
            h.m_name = "Einherjar";
            h.m_health = 400f;
            h.m_damageModifiers = new HitData.DamageModifiers { m_blunt = HitData.DamageModifier.Resistant, m_slash = HitData.DamageModifier.Resistant, m_pierce = HitData.DamageModifier.Resistant,
                m_poison = HitData.DamageModifier.Immune, m_spirit = HitData.DamageModifier.Immune, m_chop = HitData.DamageModifier.Immune, m_pickaxe = HitData.DamageModifier.Immune };
            h.m_deathEffects = Effects("vfx_odin_despawn", "fx_Fader_Roar");
            MonsterAI ai = go.GetComponent<MonsterAI>();
            ai.m_viewRange = 45f; ai.m_hearRange = 45f; ai.m_alertRange = 30f;
            ai.m_fleeIfLowHealth = 0f; ai.m_fleeIfHurtWhenTargetCantBeReached = false; ai.m_fleeIfNotAlerted = false;
            ai.m_enableHuntPlayer = false; ai.m_attackPlayerObjects = false; ai.m_circulateWhileCharging = false;
            ai.m_randomMoveRange = 6f; ai.m_randomMoveInterval = 6f;
            ai.m_passiveAggresive = true; // (a world of passive monsters: they still fight what comes)
            ZNetView view = go.GetComponent<ZNetView>(); view.m_persistent = false; // (gone with the session: never left behind)
            var glow = new GameObject("einherjar_glow");
            glow.transform.SetParent(go.transform, false); glow.transform.localPosition = new Vector3(0, 1.2f, 0);
            Light l = glow.AddComponent<Light>();
            l.type = LightType.Point; l.color = new Color(0.62f, 0.8f, 1f); l.range = 7f; l.intensity = 2.2f; l.shadows = LightShadows.None;
            _prefab = go;
        }
        public static void Forget() { _prefab = null; Coming.Clear(); Kills.Clear(); Shown.Clear(); }
        public static void ForgetWorld() { Coming.Clear(); Kills.Clear(); Shown.Clear(); }
        private static EffectList Effects(params string[] names)
        {
            var list = new List<EffectList.EffectData>();
            foreach (string n in names) { GameObject p = Assets.Find(n); if (p != null) list.Add(new EffectList.EffectData { m_prefab = p, m_enabled = true }); else Plugin.Trace("Jarldom: no effect " + n); }
            return new EffectList { m_effectPrefabs = list.ToArray() };
        }

        // ---- the call, on the colony owner's game, each raid tick ----
        public static void Watch(Colony c)
        {
            if (!c.IsOwner || !c.RaidOn || Coming.ContainsKey(c.Id) || Time.time < _nextWatch) return;
            _nextWatch = Time.time + 2f;
            var (losing, _) = Losing(c);
            string vid = Returning.Pick(c.R.Flags, losing, c.R.Roster.Where(r => r.State == SoulState.Ascended).Select(r => (r.Vid, r.Worth)));
            if (vid != null) Call(c, vid, true);
        }
        // Is it going badly? (and why, for the test tool)
        public static (bool losing, string why) Losing(Colony c)
        {
            Character at = Character.GetAllCharacters().FirstOrDefault(ch => ch != null && !ch.IsDead() && !Combat.Friendly(ch) && Combat.Raider(ch, c)
                && (Utils.DistanceXZ(ch.transform.position, c.Position) < 12f || c.RoomAt(ch.transform.position, RoomType.GreatHall) != null));
            var fighters = c.R.Roster.Where(r => r.State == SoulState.Active && (r.Prof == Prof.Guard || r.Prof == Prof.Hunter)).ToList();
            if (fighters.Count == 0) fighters = c.R.Roster.Where(r => r.State == SoulState.Active).ToList(); // (no fighters: everyone stands for them)
            int down = fighters.Count(r => c.R.Fallen.Any(f => f.Vid == r.Vid));
            bool losing = Returning.Losing(at != null, fighters.Count, down);
            return (losing, (at != null ? Localization.instance.Localize(at.m_name) + " at the hall; " : "") + $"{down} of {fighters.Count} fighters down");
        }
        // A Valkyrie flies in with them (every game near sees her and hears the name); they're let down near the fight.
        public static string Call(Colony c, string vid, bool real)
        {
            RosterRow row = c.R.Row(vid);
            if (row == null) return "no such soul";
            Vector3 at = Where(c);
            string name = FullName(c, row);
            if (real) { Returning.Mark(c.R.Flags, vid, c.Day); c.MarkDirty(); c.Flush(); }
            Coming[c.Id] = (vid, at, Time.time + Drop);
            Net.Einherjar_(at, name);
            return "";
        }
        private static string FullName(Colony c, RosterRow row) => c.R.Log.LastOrDefault(e => e.Kind == "ascend" && e.A.StartsWith(row.Name))?.A ?? row.Name;
        // Between the pillars and the nearest raider, a few steps out (or by the pillars).
        private static Vector3 Where(Colony c)
        {
            Character foe = Character.GetAllCharacters().Where(ch => ch != null && !ch.IsDead() && !Combat.Friendly(ch) && Combat.Raider(ch, c) && Utils.DistanceXZ(ch.transform.position, c.Position) < c.Radius + 30)
                .OrderBy(ch => Utils.DistanceXZ(ch.transform.position, c.Position)).FirstOrDefault();
            Vector3 dir = foe != null ? foe.transform.position - c.Position : Vector3.forward; dir.y = 0;
            float d = foe != null ? Mathf.Min(8f, dir.magnitude * 0.5f) : 4f;
            Vector3 want = c.Position + dir.normalized * Mathf.Max(3f, d);
            return Colony.Spot(want, 3f) ?? Colony.Spot(c.Position, 4f) ?? want;
        }
        // On every game, from the call: the Valkyrie and the name.
        public static void Arriving(Vector3 at, string name)
        {
            Valkyrie.Fly(at);
            Player me = Player.m_localPlayer;
            if (me != null && Vector3.Distance(me.transform.position, at) < 150f)
                me.Message(MessageHud.MessageType.Center, $"A Valkyrie rides from Valhalla, bearing {name} back to fight!");
        }

        // ---- the body ----
        private static void Spawn(Colony c, string vid, Vector3 at)
        {
            RosterRow row = c.R.Row(vid);
            if (_prefab == null || row == null) return;
            GameObject go = Object.Instantiate(_prefab, at, Quaternion.LookRotation(Vector3.ProjectOnPlane(at - c.Position, Vector3.up).normalized + Vector3.forward * 0.001f));
            ZNetView view = go.GetComponent<ZNetView>();
            ZDO z = view.GetZDO();
            string name = FullName(c, row);
            z.Set(NameKey, name); z.Set(ColonyKey, c.Id);
            z.Set(UntilKey, ZNet.instance.GetTime().Ticks + (long)(Returning.Seconds * System.TimeSpan.TicksPerSecond));
            Villagers.Looks(go, new VillagerState { Vid = row.Vid, Female = row.Female });
            // (pale as a ghost, hair gone to silver: still themselves)
            Vector3 skin = new Vector3(0.72f, 0.88f, 1.15f), hair = new Vector3(0.82f, 0.86f, 0.95f);
            VisEquipment vis = go.GetComponent<VisEquipment>();
            if (vis != null) { vis.SetSkinColor(skin); vis.SetHairColor(hair); }
            z.Set(ZDOVars.s_skinColor, skin); z.Set(ZDOVars.s_hairColor, hair);
            Humanoid h = go.GetComponent<Humanoid>();
            Dress(h, c.Z.GetString(GearKey + vid, ""));
            h.SetLevel(1);
            go.GetComponent<MonsterAI>()?.SetPatrolPoint();
            Assets.Effect("vfx_odin_despawn", at); Assets.Effect("fx_Fader_Roar", at);
            Say(z, new[] { "Valhöll sendir mik! / Valhalla sends me!", "Enn em ek hér! / I'm here still!", "Fyrir Óðin ok fyrir yðr! / For Odin and for you!", "Upp, bræðr! / Up, brothers!" }[Random.Range(0, 4)]);
            c.Log("text", "", "", $"In the colony's need, {name} came back from Valhalla to fight.");
            foreach (Brain b in Villagers.All.Where(b => b.S.Colony == c.Id && Vector3.Distance(b.Pos, at) < 40f).OrderBy(b => Vector3.Distance(b.Pos, at)).Take(2))
                Greet.Say(b, Greeting.Return, null, urgent: true, about: name.Split(' ')[0]);
            Plugin.Log($"Jarldom: {name} came back from Valhalla at {at}");
        }
        // In what they went up in (a bow left out: no arrows in Valhalla); a plain bronze kit for one gone before it was kept.
        private static void Dress(Humanoid h, string kept)
        {
            var worn = Returning.Worn(kept).Where(w => Assets.Find(w.prefab)?.GetComponent<ItemDrop>() is ItemDrop d && d.m_itemData.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Bow
                && d.m_itemData.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Ammo && d.m_itemData.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Tool).ToList();
            bool weapon = worn.Any(w => Assets.Find(w.prefab).GetComponent<ItemDrop>().m_itemData.IsWeapon());
            if (!weapon) worn = Returning.Worn("SwordBronze:1,ShieldBronzeBuckler:1,HelmetBronze:1,ArmorBronzeChest:1,ArmorBronzeLegs:1").Concat(worn.Where(w => !Returning.Worn("SwordBronze,ShieldBronzeBuckler,HelmetBronze,ArmorBronzeChest,ArmorBronzeLegs").Any(d => SameSlot(d.prefab, w.prefab)))).ToList();
            Inventory inv = h.GetInventory();
            foreach (var (prefab, quality) in worn)
            {
                GameObject p = Assets.Find(prefab);
                ItemDrop.ItemData item = p != null ? inv.AddItem(p.name, 1, quality, 0, 0L, "", new Vector2i(-1, -1), false, false, false) : null;
                if (item != null) h.EquipItem(item, false);
            }
        }
        private static bool SameSlot(string a, string b)
        {
            var ia = Assets.Find(a)?.GetComponent<ItemDrop>()?.m_itemData.m_shared; var ib = Assets.Find(b)?.GetComponent<ItemDrop>()?.m_itemData.m_shared;
            return ia != null && ib != null && ia.m_itemType == ib.m_itemType;
        }
        // Words over their head, seen on every game near (like a villager's).
        private static void Say(ZDO z, string text) { z.Set(SayKey, text); z.Set(SayTimeKey, ZNet.instance.GetTime().Ticks); }

        // Every quarter second on each game: the Valkyrie's drop (on the colony owner), their words, and their going when the time's up
        // or the raid is over (on the game that runs them).
        public static void Tick()
        {
            if (Time.time < _next || ZNet.instance == null) return;
            _next = Time.time + 0.25f;
            foreach (var kv in Coming.ToList())
            {
                if (Time.time < kv.Value.due) continue;
                Coming.Remove(kv.Key);
                Colony c = Colony.Of(kv.Key);
                if (c != null && c.Valid && c.IsOwner) Plugin.Guard(() => Spawn(c, kv.Value.vid, kv.Value.at));
            }
            Player me = Player.m_localPlayer;
            long now = ZNet.instance.GetTime().Ticks;
            foreach (Character ch in Character.GetAllCharacters().ToList())
            {
                if (ch == null || !Is(ch)) continue;
                ZNetView view = ch.GetComponent<ZNetView>(); ZDO z = view.GetZDO();
                long said = z.GetLong(SayTimeKey, 0L);
                if (me != null && Chat.instance != null && said != 0 && !(Shown.TryGetValue(z.m_uid, out long seen) && seen == said) && Vector3.Distance(me.transform.position, ch.transform.position) < 40f)
                {
                    Shown[z.m_uid] = said;
                    string[] two = z.GetString(SayKey, "").Split(new[] { " / " }, System.StringSplitOptions.None);
                    Chat.instance.SetNpcText(ch.gameObject, Vector3.up * 2.2f, 30f, 5f, "", two.Length > 1 ? Greetings.Show((two[0], two[1])) : two[0], false);
                }
                if (!view.IsOwner() || ch.IsDead()) continue;
                long until = z.GetLong(UntilKey, 0L);
                Colony c = Colony.Of(z.GetString(ColonyKey, ""));
                bool over = c != null && c.Valid && !c.RaidOn && !Losing(c).losing;
                if (over && until - now > 10 * System.TimeSpan.TicksPerSecond) { z.Set(UntilKey, until = now + 10 * System.TimeSpan.TicksPerSecond); Say(z, "Sigr er unninn. / The fight is won."); }
                else if (until - now < 4 * System.TimeSpan.TicksPerSecond && z.GetString(SayKey, "") != "Valhöll kallar. / Valhalla calls me back." && until - now > 0) Say(z, "Valhöll kallar. / Valhalla calls me back.");
                if (now >= until) Plugin.Guard(() => Leave(ch));
            }
            if (Shown.Count > 200) Shown.Clear();
        }
        // Back to Valhalla: in smoke, the saga told how many they slew.
        private static void Leave(Character ch)
        {
            ZDO z = ch.GetComponent<ZNetView>().GetZDO();
            string name = z.GetString(NameKey, "One of the Einherjar");
            Kills.TryGetValue(z.m_uid, out int n); Kills.Remove(z.m_uid);
            Colony c = Colony.Of(z.GetString(ColonyKey, ""));
            if (c != null && c.Valid && c.IsOwner && n > 0) c.Log("text", "", "", $"{name} slew {n} {(n == 1 ? "foe" : "foes")} and went back to Valhalla.");
            Assets.Effect("vfx_odin_despawn", ch.transform.position);
            Valkyrie.Fly(ch.transform.position, true);
            ZNetScene.instance.Destroy(ch.gameObject);
        }

        // Their kills, for the saga.
        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        private static class Slain
        {
            private static void Prefix(Character __instance)
            {
                Character killer = Combat.LastHit(__instance)?.GetAttacker();
                if (killer == null || !Is(killer)) return;
                ZDOID id = killer.GetComponent<ZNetView>().GetZDO().m_uid;
                Kills[id] = (Kills.TryGetValue(id, out int n) ? n : 0) + 1;
            }
        }
        // Their name over them (and in the game's own enemy bar): "Thorstein the Bold, Einherjar".
        [HarmonyPatch(typeof(Character), nameof(Character.GetHoverName))]
        private static class Named
        {
            private static void Postfix(Character __instance, ref string __result)
            {
                if (!Is(__instance)) return;
                string n = __instance.GetComponent<ZNetView>().GetZDO().GetString(NameKey, "");
                if (n != "") __result = n + ", Einherjar";
            }
        }
    }
}
