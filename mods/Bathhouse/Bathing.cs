using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Bathhouse
{
    // This game's own bather: sitting in a working sauna's heat (on a bench, in the hot tub, or sat on the floor) adds up to Bathed; out
    // and straight into cold water (the sea, a lake, a plunge tub) soon after: Invigorated. Others in the same sauna (players, and any
    // mod's bathers through the Api) make both last longer.
    internal static class Bathing
    {
        internal static float Sat; internal static double LeftHeatAt; internal static int Company;
        internal static Heat Here; internal static string Why = "";
        internal static bool Plunged; private static bool _told, _bathedThisSitting; private static float _next, _outSince = -1;
        private static readonly System.Reflection.FieldInfo Emote = AccessTools.Field(typeof(Player), "m_emoteState");
        private static readonly System.Reflection.FieldInfo AttachPoint = AccessTools.Field(typeof(Player), "m_attachPoint");
        // (what's worn: a sauna's sat in, and the plunge taken, stripped down; weapons and the belt may stay by)
        private static readonly System.Reflection.FieldInfo[] Worn = new[] { "m_helmetItem", "m_chestItem", "m_legItem", "m_shoulderItem" }.Select(n => AccessTools.Field(typeof(Humanoid), n)).ToArray();
        private static bool _toldDressed;
        internal static readonly List<Func<Vector3, int>> CompanyFrom = new List<Func<Vector3, int>>();

        internal static void Tick()
        {
            if (Time.time < _next) return;
            const float step = 0.25f; _next = Time.time + step;
            Player p = Player.m_localPlayer;
            if (p == null || p.IsDead() || ZNet.instance == null) { Sat = 0; return; }
            double now = StoveSteam.Now;
            // (sat counts as in, as a high bench isn't "in shelter" to the game; but not sat in a plunge tub just outside the door)
            bool plungeSeat = SeatOf(p) is Chair seat && IsPlunge(seat);
            Heat h = p.InShelter() || p.IsAttached() && !plungeSeat ? Room.Around(p.transform.position) : null;
            Here = h; Why = h == null ? "" : Room.WhyNot(h);
            bool inHeat = h != null && Why == "";
            if (inHeat)
            {
                _outSince = -1; LeftHeatAt = now;
                Company = CompanyAt(h, p);
                string dressed = Dressed(p);
                if (Sitting(p) && dressed != "")
                {
                    if (!_toldDressed) { _toldDressed = true; p.Message(MessageHud.MessageType.Center, $"Too hot for {dressed}: strip down to bathe."); }
                }
                else if (Sitting(p))
                {
                    if (!_told) { _told = true; p.Message(MessageHud.MessageType.TopLeft, "The heat soaks into you. Sit a while."); }
                    Sat = Sauna.Sit(Sat, step, h.Steam?.Steaming == true);
                    if (Sauna.Done(Sat) && !_bathedThisSitting)
                    {
                        Effects.Bathe(p, Sauna.Lengthen(Company));
                        _bathedThisSitting = true; Plunged = false;
                        Plugin.Log($"Bathed ({Company} in company)");
                    }
                }
            }
            else
            {
                // (out of the heat a while: the next time in starts from the start)
                if (_outSince < 0) _outSince = Time.time;
                if (Time.time - _outSince > 20f) { Sat = 0; _told = false; _toldDressed = false; _bathedThisSitting = false; }
            }
            // The cold plunge: straight from the heat, Bathed, into cold water (not the hot tub's).
            if (!Plunged && Effects.Has(p, Effects.Bathed) && Dressed(p) == "" && Sauna.Plunge(now, LeftHeatAt) && ColdWater(p))
            {
                Plunged = true;
                Effects.Invigorate(p, Sauna.Lengthen(Company));
                p.Message(MessageHud.MessageType.Center, "The cold takes your breath away!");
            }
        }

        // What the player still has on that the sauna wants off ("" when stripped down): "your helmet and cape".
        internal static string Dressed(Player p)
        {
            var on = Worn.Select(f => f?.GetValue(p) as ItemDrop.ItemData).Where(i => i != null).Select(i => Localization.instance.Localize(i.m_shared.m_name)).ToList();
            if (on.Count == 0) return "";
            return "your " + (on.Count == 1 ? on[0] : string.Join(", ", on.Take(on.Count - 1)) + " and " + on[on.Count - 1]);
        }

        internal static bool Sitting(Player p)
        {
            if (p.IsAttached() && !p.InBed() && !p.IsAttachedToShip()) return true;
            return (Emote?.GetValue(p) as string) == "sit";
        }
        internal static Chair SeatOf(Player p) => p.IsAttached() && AttachPoint?.GetValue(p) is Transform t && t != null ? t.GetComponentInParent<Chair>() : null;

        // In cold water: swimming or wading in the world's water (away from any hot tub), or sat in a plunge tub.
        internal static bool ColdWater(Player p)
        {
            Chair seat = SeatOf(p);
            if (seat != null) return IsPlunge(seat);
            // (or stood down in one: inside its staves, below its water)
            if (Room.Plunges(p.transform.position, 1.5f).Any(t => Utils.DistanceXZ(t.transform.position, p.transform.position) < 0.65f && p.transform.position.y < t.transform.position.y + 0.7f)) return true;
            if (!p.InWater()) return false;
            return !Room.Heats(p.transform.position, 2.5f).Any(x => !x.Stove);
        }

        private static bool IsPlunge(Chair seat) => seat.GetComponentInParent<Piece>() is Piece pc && Utils.GetPrefabName(pc.gameObject) == Kit.Plunge;

        // Others bathing in the same heat: players in the room, and what other mods count (villagers).
        internal static int CompanyAt(Heat h, Player me)
        {
            int n = Player.GetAllPlayers().Count(o => o != null && o != me && !o.IsDead() && Utils.DistanceXZ(o.transform.position, h.At) <= Sauna.Reach && Mathf.Abs(o.transform.position.y - h.At.y) < 3f);
            foreach (Func<Vector3, int> f in CompanyFrom.ToList()) { try { n += Math.Max(0, f(h.At)); } catch (Exception e) { Plugin.Warn("A bathers count: " + e.Message); } }
            return n;
        }

        // What a stove says when looked at: whether its room is a sauna, and if not, why not.
        internal static string Verdict(Heat h)
        {
            string why = Room.WhyNot(h);
            if (why != "") return $"\n<color=#ffb070>{why}</color>";
            string steam = h.Steam?.Steaming == true ? " The air is thick with steam." : "";
            return $"\n<color=#a0e0a0>A sauna: strip down and sit in the heat a minute to be Bathed.{steam}</color>";
        }

        [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.GetHoverText))]
        private static class StoveHover
        {
            private static void Postfix(Fireplace __instance, ref string __result)
            {
                if (Utils.GetPrefabName(__instance.gameObject) != Kit.Stove || !(__instance.GetComponent<Piece>() is Piece p)) return;
                __result += Verdict(new Heat { Piece = p, Stove = true, At = p.transform.position });
            }
        }
        [HarmonyPatch(typeof(Chair), nameof(Chair.GetHoverText))]
        private static class SeatHover
        {
            private static void Postfix(Chair __instance, ref string __result)
            {
                Piece p = __instance.GetComponentInParent<Piece>();
                if (p == null || Utils.GetPrefabName(p.gameObject) != Kit.Bench) return;
                Heat h = Room.Around(p.transform.position);
                __result += h == null ? "\n<color=#c0c0c0>Put it in a small room with a sauna stove.</color>" : Verdict(h);
                Player me = Player.m_localPlayer;
                if (me != null && Room.Around(me.transform.position) is Heat mine && mine.Piece == h.Piece && Dressed(me) is string d && d != "") __result += $"\n<color=#ffb070>Strip down first: off with {d}.</color>";
                else if (me != null && Here != null && h.Piece == Here.Piece && Sat > 0 && !_bathedThisSitting) __result += $"\n<color=#c0c0c0>In the heat {Sauna.Span(Sat)} of {Sauna.Span(Sauna.SitSeconds)}</color>";
            }
        }
    }
}
