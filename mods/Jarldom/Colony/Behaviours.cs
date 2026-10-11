using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // The scripts on the mod's pieces. A reload swaps the old copy's scripts on standing pieces for the new copy's (Reattach), so E on them
    // runs the new code at once; they keep nothing of their own that isn't in the ZDO.
    internal static class PieceScripts
    {
        private static readonly (string prefix, Type type)[] Kinds =
        {
            (Pieces.PillarsName, typeof(HighSeat)), (Pieces.AltarPrefix, typeof(Altar)), (Pieces.HornName, typeof(WarHorn)), (Pieces.PyreName, typeof(Pyre)),
        };
        public static void AttachAll()
        {
            if (ZNetScene.instance == null) return;
            foreach (ZNetView v in AllViews())
            {
                string name = Utils.GetPrefabName(v.gameObject);
                foreach (var (prefix, type) in Kinds)
                {
                    if (!name.StartsWith(prefix)) continue;
                    foreach (MonoBehaviour old in v.GetComponents<MonoBehaviour>().Where(m => m != null && m.GetType().Name == type.Name && m.GetType() != type).ToList()) UnityEngine.Object.DestroyImmediate(old);
                    if (v.GetComponent(type) == null)
                    {
                        Component c = v.gameObject.AddComponent(type);
                        if (c is Altar a) a.God = name.Substring(Pieces.AltarPrefix.Length);
                    }
                }
            }
        }
        public static void DetachAll()
        {
            foreach (ZNetView v in AllViews())
                foreach (var (_, type) in Kinds)
                {
                    Component c = v.GetComponent(type);
                    if (c != null) UnityEngine.Object.DestroyImmediate(c);
                }
        }
        private static IEnumerable<ZNetView> AllViews()
        {
            foreach (int hash in Pieces.All.Select(p => p.name.GetStableHashCode()).ToList())
                foreach (ZNetView v in Assets.Instances(hash)) yield return v;
        }
    }

    // The High-Seat Pillars: the colony lives on their ZDO. E opens the colony window.
    internal sealed class HighSeat : MonoBehaviour, Hoverable, Interactable
    {
        public Colony Colony;
        private float _retry;
        private void Awake() { if (ZNetScene.instance != null) TryAttach(); }
        private void TryAttach()
        {
            ZNetView v = GetComponent<ZNetView>();
            if (v == null || !v.IsValid()) return;
            Colony = Colony.Attach(this);
        }
        private void Update()
        {
            if (Colony != null && Colony.Valid) return;
            if (Time.time < _retry) return;
            _retry = Time.time + 2;
            TryAttach();
        }
        private void OnDestroy() { Colony?.Detach(); Colony = null; }
        public static void AttachAll() => PieceScripts.AttachAll();

        public string GetHoverName() => Colony?.R?.Name ?? "High-seat pillars";
        public string GetHoverText()
        {
            if (Colony == null || !Colony.Valid) return Localization.instance.Localize("High-seat pillars\n<size=80%>(founding...)</size>");
            ColonyRecord r = Colony.R;
            string coming = r.ComingLanded && r.Coming.Count > 0 ? $"\n<color=#ffd27a>{r.Coming.Count} newcomer{(r.Coming.Count == 1 ? "" : "s")} wait{(r.Coming.Count == 1 ? "s" : "")} for your word</color>" : "";
            return Localization.instance.Localize($"{r.Name}\n<size=80%>{Tables.Of(r.Rank).Name} of {Colony.Population}, renown {Colony.Renown}</size>{coming}\n[<color=yellow><b>$KEY_Use</b></color>] Hold council");
        }
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || user != Player.m_localPlayer) return false;
            if (Colony == null || !Colony.Valid) return false;
            Ui.ColonyWindow.Open(Colony);
            return true;
        }
        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
        public float GetHoverOffset() => 1.5f;
    }

    // A god's altar: offerings from the player's bag, and the god a temple is for.
    internal sealed class Altar : MonoBehaviour, Hoverable, Interactable
    {
        public string God = "odin";
        public string GetHoverName() => "Altar of " + (Tables.God(God)?.Name ?? God);
        public string GetHoverText()
        {
            GodInfo g = Tables.God(God);
            Colony c = Colony.At(transform.position);
            string favour = c != null ? $"\n<size=80%>{g?.Name}'s favour: {Faith.FavourWord(c.FavourOf(God))} ({c.FavourOf(God)})</size>" : "";
            return Localization.instance.Localize($"Altar of {g?.Name}{favour}\n<size=80%>wants {g?.Offerings}</size>\n[<color=yellow><b>$KEY_Use</b></color>] Make an offering   <size=80%>(or use an item on it)</size>");
        }
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !(user is Player p)) return false;
            Faith.OfferFromBag(this, p);
            return true;
        }
        public bool UseItem(Humanoid user, ItemDrop.ItemData item) { if (user is Player p) Faith.Offer(this, p, item, item.m_stack); return true; }
        public float GetHoverOffset() => 1.2f;
    }

    // The war horn: blow it to call everyone in, again for the all-clear.
    internal sealed class WarHorn : MonoBehaviour, Hoverable, Interactable
    {
        public string GetHoverName() => "War horn";
        public string GetHoverText()
        {
            Colony c = Colony.At(transform.position);
            bool on = c != null && c.HornUntil > Time.time;
            return Localization.instance.Localize($"War horn{(on ? " <color=#ff8c59>(sounding)</color>" : "")}\n[<color=yellow><b>$KEY_Use</b></color>] {(on ? "Sound the all-clear" : "Blow the horn")}");
        }
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !(user is Player)) return false;
            Colony c = Colony.At(transform.position);
            if (c == null) { user.Message(MessageHud.MessageType.Center, "The horn must stand in a colony."); return true; }
            Net.Act(c, "horn", c.HornUntil > Time.time ? "off" : "on");
            return true;
        }
        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
        public float GetHoverOffset() => 1.5f;
    }

    // The funeral pyre: grave goods in its hold; lit from the colony window for a worthy soul.
    internal sealed class Pyre : MonoBehaviour, Hoverable, Interactable
    {
        public string GetHoverName() => "Funeral pyre";
        public string GetHoverText()
        {
            Container goods = GetComponent<Container>();
            int n = goods != null ? goods.GetInventory().NrOfItems() : 0;
            bool burning = GetComponent<ZNetView>()?.GetZDO()?.GetLong(Ascension.BurnKey, 0L) > 0;
            return Localization.instance.Localize($"Funeral pyre{(burning ? " <color=#ff8c59>(burning)</color>" : "")}\n<size=80%>{n} grave goods</size>\n[<color=yellow><b>$KEY_Use</b></color>] Grave goods");
        }
        public bool Interact(Humanoid user, bool hold, bool alt) => GetComponent<Container>()?.Interact(user, hold, alt) ?? false;
        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
        public float GetHoverOffset() => 1.5f;
        private void Update() => Ascension.PyreTick(this);
        // Grave goods aren't storage: chest mods (and the colony) leave them be.
        private void Start() { ZNetView v = GetComponent<ZNetView>(); if (v != null && v.IsValid() && v.IsOwner() && !v.GetZDO().GetBool("dh_notstorage")) v.GetZDO().Set("dh_notstorage", true); }
    }
}
