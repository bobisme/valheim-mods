using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jarldom
{
    // A villager's state (a few KB: needs, moodlets, skills, bonds, counts) kept on a still object of its own, a soul at the pillars, not
    // on the walking body: the game sends a body's whole record again every time it moves, and a soul's only when it's written (at
    // most twice a minute, or at a death or a command). The body keeps only small things (its id, name, what it's doing, its mood) and
    // the soul's id. Bodies from before the soul are moved over the first time a brain loads them.
    internal static class SoulStore
    {
        public const string PrefabName = "JarldomSoul", SoulKey = "jarldom_soul";
        private static GameObject _prefab;
        public static GameObject Prefab
        {
            get
            {
                if (_prefab != null) return _prefab;
                _prefab = new GameObject(PrefabName);
                _prefab.transform.SetParent(Assets.Holder, false);
                ZNetView v = _prefab.AddComponent<ZNetView>();
                v.m_persistent = true; v.m_distant = false; v.m_type = ZDO.ObjectType.Default;
                return _prefab;
            }
        }
        public static void ForgetPrefab() { if (_prefab != null) Object.Destroy(_prefab); _prefab = null; }

        // A body's soul. The body keeps the soul's id, but the game gives every object a new id when it loads a world (and mends only
        // its own links), so the id is a fast path for this session and the soul is otherwise found by what it says it is: the
        // villager's id (or, for a board, its colony's), from an index of the still objects made from this prefab.
        public static ZDO Of(ZDO body)
        {
            if (body == null || ZDOMan.instance == null) return null;
            ZDOID id = body.GetZDOID(SoulKey);
            if (id.IsNone()) return null;
            ZDO soul = ZDOMan.instance.GetZDO(id);
            string vid = body.GetString(Villagers.VidKey, "");
            if (soul != null && soul.GetPrefab() == Hash && soul.GetString(Villagers.VidKey, "") == vid) return soul;
            soul = Find(Villagers.VidKey, vid);
            if (soul != null && body.IsOwner()) body.Set(SoulKey, soul.m_uid); // (mended for next time)
            return soul;
        }
        private static readonly AccessTools.FieldRef<ZDOMan, Dictionary<ZDOID, ZDO>> ById = AccessTools.FieldRefAccess<ZDOMan, Dictionary<ZDOID, ZDO>>("m_objectsByID");
        private static readonly Dictionary<string, ZDO> Index = new Dictionary<string, ZDO>();
        private static float _indexedAt = -100f;
        private static int Hash => PrefabName.GetStableHashCode();
        // The still object whose key says value (a soul by its villager's id, a board by its colony's): from the index, rebuilt from
        // every object this game has (at most every few seconds) when it isn't there.
        public static ZDO Find(string key, string value)
        {
            if (string.IsNullOrEmpty(value) || ZDOMan.instance == null) return null;
            string k = key + "=" + value;
            if (Index.TryGetValue(k, out ZDO z) && z.IsValid() && z.GetPrefab() == Hash && z.GetString(key, "") == value) return z;
            if (Time.time - _indexedAt < 5f) return null;
            _indexedAt = Time.time; Index.Clear();
            int hash = Hash;
            foreach (ZDO o in ById(ZDOMan.instance).Values)
            {
                if (o.GetPrefab() != hash) continue;
                string vid = o.GetString(Villagers.VidKey, ""), id = o.GetString(Colony.IdKey, "");
                if (vid != "") Index[Villagers.VidKey + "=" + vid] = o;
                else if (id != "") Index[Colony.IdKey + "=" + id] = o;
            }
            return Index.TryGetValue(k, out z) ? z : null;
        }
        public static void Forget() { Index.Clear(); _indexedAt = -100f; }
        // What a villager is: from the body when it has a copy (from before souls, or kept there while its soul couldn't be found:
        // either way the newer), else from its soul.
        public static byte[] StateOf(ZDO body)
        {
            byte[] b = body?.GetByteArray(Villagers.StateKey);
            if (b != null && b.Length > 0) return b;
            return Of(body)?.GetByteArray(Villagers.StateKey);
        }
        // On the body's owner: the state written to the soul (made if there isn't one yet, at the colony's pillars).
        public static void Write(ZDO body, VillagerState s, Colony colony)
        {
            byte[] bytes = s.Save();
            ZDO soul = Of(body);
            if (soul == null && !body.GetZDOID(SoulKey).IsNone()) { body.Set(Villagers.StateKey, bytes); return; } // (its soul isn't loaded on this game: keep it on the body until it is)
            if (soul == null) soul = Make(body, s, colony);
            if (soul == null) { body.Set(Villagers.StateKey, bytes); return; }
            if (!soul.IsOwner()) soul.SetOwner(ZDOMan.GetSessionID());
            soul.Set(Villagers.StateKey, bytes);
            if (body.GetByteArray(Villagers.StateKey)?.Length > 0) body.Set(Villagers.StateKey, new byte[0]); // (the old copy off the body)
        }
        private static ZDO Make(ZDO body, VillagerState s, Colony colony)
        {
            if (ZDOMan.instance == null || ZNetScene.instance?.GetPrefab(PrefabName) == null) return null;
            Vector3 at = colony != null ? colony.Position + Vector3.down * 2f : body.GetPosition(); // (under the pillars, out of the way)
            int hash = PrefabName.GetStableHashCode();
            ZDO soul = ZDOMan.instance.CreateNewZDO(at, hash);
            soul.Persistent = true; soul.Type = ZDO.ObjectType.Default; soul.Distant = false;
            soul.SetPrefab(hash);
            soul.Set(Villagers.VidKey, s.Vid); soul.Set(Villagers.ColonyKey, s.Colony);
            body.Set(SoulKey, soul.m_uid);
            Index[Villagers.VidKey + "=" + s.Vid] = soul;
            return soul;
        }
        // The body gone for good or for now (a death, a trip away, a dismissal): its soul goes with it (the state lives on elsewhere:
        // in the colony's record while away or fallen).
        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Destroy))]
        private static class GoesWithBody
        {
            private static void Prefix(GameObject go)
            {
                if (go != null && go.GetComponent<HighSeat>() is HighSeat seat && seat.Colony?.BoardZ is ZDO board && go.GetComponent<ZNetView>()?.IsOwner() == true)
                { if (!board.IsOwner()) board.SetOwner(ZDOMan.GetSessionID()); ZDOMan.instance.DestroyZDO(board); return; } // (the pillars' board goes with them)
                if (go == null || !VillagerPrefab.Is(go.GetComponent<Character>())) return;
                ZNetView v = go.GetComponent<ZNetView>();
                if (v == null || !v.IsValid() || !v.IsOwner()) return;
                ZDO soul = Of(v.GetZDO());
                if (soul == null) return;
                if (!soul.IsOwner()) soul.SetOwner(ZDOMan.GetSessionID());
                ZDOMan.instance.DestroyZDO(soul);
            }
        }
    }
}
