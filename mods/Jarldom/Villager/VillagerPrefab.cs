using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Jarldom
{
    // The villager's body: the game's Player prefab without what makes it *the* player (input, profile, skills, talking), with the game's own
    // Humanoid and MonsterAI in their place and a chest-like Container for its bag. Only the game's components, so a reload or an uninstall
    // never leaves our code on a body; the brain lives in Villagers' dictionaries, keyed by the body.
    internal static class VillagerPrefab
    {
        public const string Name = "JarldomVillager";
        public static readonly int Hash = Name.GetStableHashCode();
        public const string BagName = "JarldomBag"; // the Container's name tells our bags from every other
        public const int BagWidth = 8, BagRows = 4;
        private static GameObject _prefab;
        public static GameObject Prefab => _prefab;
        public static IEnumerable<GameObject> All { get { if (_prefab != null) yield return _prefab; } }

        public static void Ensure(ZNetScene scene)
        {
            if (_prefab != null) return;
            GameObject player = scene.GetPrefab("Player");
            if (player == null) { Plugin.Warn("Jarldom: the Player prefab is missing, so no villager can be made"); return; }
            _prefab = Make(player);
        }
        public static void Forget() => _prefab = null;

        // Asked in hot paths (every character's motion, every skill check): the answer is kept per object.
        private static readonly Dictionary<int, bool> Known = new Dictionary<int, bool>();
        public static bool Is(Component c)
        {
            if (c == null) return false;
            int id = c.gameObject.GetInstanceID();
            if (Known.TryGetValue(id, out bool known)) return known;
            ZNetView v = c.GetComponent<ZNetView>();
            if (v == null || !v.IsValid()) return false;
            bool yes = v.GetZDO().GetPrefab() == Hash;
            if (Known.Count > 20000) Known.Clear();
            Known[id] = yes;
            return yes;
        }
        public static void ForgetKnown() => Known.Clear();

        private static GameObject Make(GameObject source)
        {
            GameObject go = Object.Instantiate(source, Assets.Holder, false);
            go.name = Name;
            Player original = source.GetComponent<Player>();
            foreach (System.Type t in new[] { typeof(PlayerController), typeof(Player), typeof(Talker), typeof(Skills) })
            {
                Component c = go.GetComponent(t);
                if (c != null) Object.DestroyImmediate(c);
            }
            Humanoid h = go.AddComponent<Humanoid>();
            if (original != null)
            {
                h.m_unarmedWeapon = original.m_unarmedWeapon;
                h.m_consumeItemEffects = original.m_consumeItemEffects;
                h.m_equipEffects = original.m_equipEffects;
                h.m_hitEffects = original.m_hitEffects;
                h.m_critHitEffects = original.m_critHitEffects;
                h.m_backstabHitEffects = original.m_backstabHitEffects;
                h.m_deathEffects = original.m_deathEffects;
                h.m_waterEffects = original.m_waterEffects;
                h.m_tarEffects = original.m_tarEffects;
                h.m_slideEffects = original.m_slideEffects;
                h.m_jumpEffects = original.m_jumpEffects;
                h.m_flyingContinuousEffect = original.m_flyingContinuousEffect;
            }
            h.m_name = "Villager";
            h.m_group = "jarldom";
            h.m_faction = Character.Faction.Players;
            h.m_health = 25f;
            h.m_regenAllHPTime = 300f;
            h.m_walkSpeed = 1.6f; h.m_runSpeed = 6.5f; h.m_speed = 4f; h.m_crouchSpeed = 2f;
            h.m_turnSpeed = 300f; h.m_runTurnSpeed = 300f; h.m_acceleration = 1f;
            h.m_jumpForce = 8f; h.m_jumpForceForward = 2f;
            h.m_canSwim = true; h.m_swimDepth = 1.6f; h.m_swimSpeed = 2f; h.m_swimTurnSpeed = 100f; h.m_swimAcceleration = 0.05f;
            h.m_groundTilt = Character.GroundTiltType.None;
            h.m_eye = Utils.FindChild(go.transform, "EyePos");
            h.m_tolerateWater = true;
            h.m_staggerWhenBlocked = true;
            h.m_damageModifiers = new HitData.DamageModifiers { m_chop = HitData.DamageModifier.Immune, m_pickaxe = HitData.DamageModifier.Immune, m_spirit = HitData.DamageModifier.Immune };

            // Footsteps and the moment a swing lands were wired to the Player that is gone.
            CharacterAnimEvent anim = go.GetComponentInChildren<CharacterAnimEvent>(true);
            if (anim != null) AccessTools.Field(typeof(CharacterAnimEvent), "m_character")?.SetValue(anim, h);

            MonsterAI ai = go.AddComponent<MonsterAI>();
            ai.m_viewRange = 30f; ai.m_viewAngle = 150f; ai.m_hearRange = 30f; ai.m_mistVision = true;
            ai.m_alertedEffects = new EffectList(); ai.m_idleSound = new EffectList(); ai.m_idleSoundChance = 0f;
            ai.m_pathAgentType = Pathfinding.AgentType.Humanoid;
            ai.m_moveMinAngle = 90f; ai.m_smoothMovement = true; ai.m_jumpInterval = 0f;
            ai.m_randomMoveInterval = 10f; ai.m_randomMoveRange = 4f;
            ai.m_avoidFire = false; ai.m_afraidOfFire = false; ai.m_avoidWater = true; ai.m_aggravatable = false;
            ai.m_attackPlayerObjects = false;
            ai.m_consumeItems = new List<ItemDrop>();

            ZNetView view = go.GetComponent<ZNetView>();
            view.m_persistent = true; view.m_distant = false; view.m_type = ZDO.ObjectType.Default;
            ZSyncTransform sync = go.GetComponent<ZSyncTransform>();
            if (sync != null) { sync.m_syncPosition = true; sync.m_syncRotation = true; sync.m_syncBodyVelocity = false; sync.m_characterParentSync = false; }
            Rigidbody body = go.GetComponent<Rigidbody>();
            if (body != null) body.mass = 50f;

            // The bag: a chest-like inventory saved with the villager (opened with the game's own chest screen); the Humanoid uses the
            // same inventory (Body.ShareBag), so what you put in is what they can wear, wield and eat.
            Container bag = go.AddComponent<Container>();
            bag.m_name = BagName;
            bag.m_width = BagWidth; bag.m_height = BagRows;
            bag.m_privacy = Container.PrivacySetting.Public;
            bag.m_checkGuardStone = false;
            bag.m_autoDestroyEmpty = false;
            bag.m_openEffects = new EffectList(); bag.m_closeEffects = new EffectList();
            Container chest = source.GetComponent<Container>();
            GameObject wood = ZNetScene.instance?.GetPrefab("piece_chest_wood");
            Container like = wood != null ? wood.GetComponent<Container>() : null;
            if (like != null) bag.m_bkg = like.m_bkg;
            return go;
        }
    }
}
