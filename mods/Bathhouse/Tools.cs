using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Bathhouse
{
    // A "bath" command for Claude Tools, found while the game runs (no reference), to check saunas and the buffs in a live game.
    internal static class Tools
    {
        private const string ClaudeToolsGuid = "com.quad.claudetools";
        private static BaseUnityPlugin _tools;
        private static float _nextCheck;
        private static System.Collections.Generic.List<ItemDrop.ItemData> _stripped = new System.Collections.Generic.List<ItemDrop.ItemData>();

        internal static void Tick()
        {
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 5;
            BaseUnityPlugin found = BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(ClaudeToolsGuid, out PluginInfo info) ? info.Instance : null;
            if (found == _tools) return;
            _tools = found;
            if (found == null) return;
            try
            {
                found.GetType().GetMethod("RegisterCommand", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object[]{Plugin.Name, "bath",
                    "bath status | room [x z] | grant bathed|invigorated [company] | steam | sit [n] | plunge | stand | throw | hover | locker | strip | dress | clear: check Bathhouse. status shows the sauna the player is in (or the nearest heat and why its room isn't one), the time sat in the heat, the company and the buffs; room judges the room round the heat nearest a point (the player by default: walls, roof, size, benches, lit); grant gives a buff as the sauna or the plunge would (company lengthens it); steam throws water on the nearest stove; sit puts the player on a seat of the nearest sauna (plunge: in the nearest plunge tub; stand: up), as E does; throw is E on the nearest bucket; hover shows what the nearest stove, its bench and bucket say; locker is E on the nearest bath locker; strip takes off the armour the sauna wants off (dress puts it back); clear takes the buffs away.",
                    (Func<string[], Action<JObject>, Action<string>, IEnumerator>)Run});
                Plugin.Log("Claude Tools found: bath command added");
            }
            catch (Exception e) { Plugin.Log("Could not add the bath command: " + e.Message); }
        }
        internal static void Stop()
        {
            try { _tools?.GetType().GetMethod("UnregisterAll", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object[] { Plugin.Name }); } catch (Exception) { }
            _tools = null;
        }
        private static float F(string[] a, int i) => a.Length > i && float.TryParse(a[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : 0;
        private static JArray V(Vector3 v) => new JArray(Math.Round(v.x, 1), Math.Round(v.y, 1), Math.Round(v.z, 1));
        private static JObject Facts(Heat h)
        {
            RoomFacts f = Room.Facts(h, fresh: true);
            return new JObject { ["heat"] = h.Stove ? "sauna stove" : "hot tub", ["at"] = V(h.At), ["lit"] = f.Lit, ["cover"] = Math.Round(f.Cover, 2), ["underRoof"] = f.UnderRoof,
                ["walls"] = $"{f.Walls}/{f.Rays}", ["across"] = Math.Round(f.Across, 1), ["benches"] = f.Benches, ["why"] = Sauna.WhyNot(f), ["seats"] = Room.Seats(h).Count };
        }

        private static string Spread(ParticleSystem x)
        {
            var ps = new ParticleSystem.Particle[x.particleCount]; int n = x.GetParticles(ps);
            if (n == 0) return "none";
            var p = ps.Take(n).ToArray();
            return $"y {p.Min(q => q.position.y):F2}..{p.Max(q => q.position.y):F2} x {p.Min(q => q.position.x):F2}..{p.Max(q => q.position.x):F2} size {p.Min(q => q.GetCurrentSize(x)):F2}..{p.Max(q => q.GetCurrentSize(x)):F2} alpha {p.Max(q => q.GetCurrentColor(x).a)} life {p.Min(q => q.remainingLifetime):F1}..{p.Max(q => q.remainingLifetime):F1} space {x.main.simulationSpace} scale {x.transform.lossyScale}";
        }
        private static IEnumerator Run(string[] a, Action<JObject> output, Action<string> error)
        {
            Player me = Player.m_localPlayer;
            if (me == null || ZNet.instance == null) { error("needs the player in a world"); yield break; }
            string verb = a.Length > 1 ? a[1] : "status";
            switch (verb)
            {
                case "status":
                {
                    Heat h = Room.Around(me.transform.position) ?? Room.Heats(me.transform.position, 30).FirstOrDefault();
                    var o = new JObject { ["here"] = V(me.transform.position), ["inShelter"] = me.InShelter(), ["sitting"] = Bathing.Sitting(me), ["dressed"] = Bathing.Dressed(me), ["sat"] = Math.Round(Bathing.Sat, 1),
                        ["inHeat"] = Bathing.Here != null && Bathing.Why == "", ["company"] = Bathing.Company, ["leftHeat"] = Bathing.LeftHeatAt > 0 ? Math.Round(StoveSteam.Now - Bathing.LeftHeatAt) : -1,
                        ["bathed"] = Math.Round(Effects.Left(me, Effects.Bathed)), ["invigorated"] = Math.Round(Effects.Left(me, Effects.Invigorated)),
                        ["rested"] = Math.Round(Effects.Left(me, SEMan.s_statusEffectRested)), ["comfort"] = me.GetComfortLevel(), ["maxStamina"] = Math.Round(me.GetMaxStamina(), 1),
                        ["inWater"] = me.InWater(), ["coldWater"] = Bathing.ColdWater(me), ["stoves"] = StoveSteam.Loaded.Count };
                    if (h != null) o["sauna"] = Facts(h);
                    output(o); break;
                }
                case "room":
                {
                    Vector3 at = a.Length > 3 ? new Vector3(F(a, 2), me.transform.position.y, F(a, 3)) : me.transform.position;
                    if (a.Length > 3 && ZoneSystem.instance != null) at.y = ZoneSystem.instance.GetSolidHeight(at);
                    Heat h = Room.Heats(at, 30).FirstOrDefault();
                    if (h == null) { error("no sauna stove or hot tub within 30 m"); yield break; }
                    output(Facts(h)); break;
                }
                case "grant":
                {
                    int company = a.Length > 3 ? (int)F(a, 3) : 0;
                    string what = a.Length > 2 ? a[2] : "bathed";
                    if (what.StartsWith("inv")) Effects.Invigorate(me, Sauna.Lengthen(company)); else { Effects.Bathe(me, Sauna.Lengthen(company)); Bathing.LeftHeatAt = StoveSteam.Now; } // (as if just out of the heat: a plunge now counts)
                    yield return null;
                    output(new JObject { ["bathed"] = Math.Round(Effects.Left(me, Effects.Bathed)), ["invigorated"] = Math.Round(Effects.Left(me, Effects.Invigorated)), ["rested"] = Math.Round(Effects.Left(me, SEMan.s_statusEffectRested)) });
                    break;
                }
                case "steam":
                {
                    StoveSteam s = StoveSteam.Nearest(me.transform.position, 30, false);
                    if (s == null) { error("no sauna stove within 30 m"); yield break; }
                    if (!s.Lit) { error("the nearest stove is cold"); yield break; }
                    s.Throw();
                    yield return new WaitForSeconds(0.5f);
                    output(new JObject { ["stove"] = V(s.transform.position), ["steaming"] = s.Steaming, ["puffs"] = new JArray(s.GetComponentsInChildren<ParticleSystem>(true).Where(x => x.transform.parent?.name == "Steam" || x.name == "Steam").Select(x => $"{x.name} {x.particleCount} alive={x.IsAlive()} active={x.gameObject.activeInHierarchy} layer={LayerMask.LayerToName(x.gameObject.layer)} r={x.GetComponent<ParticleSystemRenderer>()?.enabled} mat={x.GetComponent<ParticleSystemRenderer>()?.sharedMaterial?.name}/{x.GetComponent<ParticleSystemRenderer>()?.sharedMaterial?.shader?.name} bounds={x.GetComponent<ParticleSystemRenderer>()?.bounds} {Spread(x)}")) }); break;
                }
                case "sit": // (sit n: on a seat of the sauna nearest, as E on it does; plunge: in the nearest plunge tub; stand: up again)
                case "plunge":
                {
                    Chair seat = null;
                    if (verb == "plunge") seat = Room.Plunges(me.transform.position, 30).FirstOrDefault()?.GetComponentInChildren<Chair>();
                    else if (Room.Heats(me.transform.position, 30).FirstOrDefault() is Heat h) seat = Room.Seats(h).ElementAtOrDefault(a.Length > 2 ? (int)F(a, 2) : 0);
                    if (seat == null) { error("no seat there"); yield break; }
                    if (me.IsAttached()) me.AttachStop();
                    Vector3 to = seat.transform.position + seat.transform.forward * 0.8f; // (within reach of it, as one walks up)
                    me.transform.position = to; if (me.GetComponent<Rigidbody>() is Rigidbody body) body.position = to;
                    yield return new WaitForSeconds(0.3f);
                    seat.Interact(me, false, false);
                    yield return new WaitForSeconds(0.5f);
                    output(new JObject { ["seat"] = V(seat.transform.position), ["attached"] = me.IsAttached(), ["sitting"] = Bathing.Sitting(me) }); break;
                }
                case "stand": me.AttachStop(); yield return new WaitForSeconds(0.3f); output(new JObject { ["at"] = V(me.transform.position) }); break;
                case "throw": // (as E on the nearest bucket)
                {
                    BucketUse b = UnityEngine.Object.FindObjectsByType<BucketUse>(FindObjectsSortMode.None).OrderBy(x => Vector3.Distance(x.transform.position, me.transform.position)).FirstOrDefault();
                    if (b == null) { error("no bucket"); yield break; }
                    output(new JObject { ["thrown"] = b.Interact(me, false, false), ["hover"] = b.GetHoverText() }); break;
                }
                case "hover": // (what the nearest stove, bench seat and bucket say when looked at)
                {
                    Heat h = Room.Heats(me.transform.position, 30).FirstOrDefault(x => x.Stove);
                    if (h == null) { error("no sauna stove within 30 m"); yield break; }
                    output(new JObject { ["stove"] = h.Piece.GetComponent<Fireplace>()?.GetHoverText(), ["seat"] = Room.Seats(h).FirstOrDefault()?.GetHoverText(),
                        ["bucket"] = UnityEngine.Object.FindObjectsByType<BucketUse>(FindObjectsSortMode.None).OrderBy(x => Vector3.Distance(x.transform.position, h.At)).FirstOrDefault()?.GetHoverText() });
                    break;
                }
                case "locker": // (E on the nearest bath locker: strip down into it, or dress from it)
                {
                    Container box = UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None).Where(Locker.Is).OrderBy(x => Vector3.Distance(x.transform.position, me.transform.position)).FirstOrDefault();
                    if (box == null) { error("no bath locker"); yield break; }
                    bool used = box.Interact(me, false, false);
                    yield return new WaitForSeconds(0.5f);
                    output(new JObject { ["used"] = used, ["inLocker"] = new JArray(box.GetInventory().GetAllItems().Select(i => i.m_shared.m_name)), ["dressed"] = Bathing.Dressed(me),
                        ["equipped"] = new JArray(me.GetInventory().GetEquippedItems().Select(i => i.m_shared.m_name)), ["bagFree"] = me.GetInventory().GetEmptySlots(), ["bag"] = $"{me.GetInventory().GetWidth()}x{me.GetInventory().GetHeight()} {me.GetInventory().NrOfItems()}", ["hover"] = box.GetHoverText() });
                    break;
                }
                case "aim": // (aim: from eye height round the nearest plunge tub, at it, as the game finds what's looked at: what E would use)
                {
                    Piece tub = Room.Plunges(me.transform.position, 30).FirstOrDefault();
                    if (tub == null) { error("no plunge tub"); yield break; }
                    int mask = (int)AccessTools.Field(typeof(Player), "m_interactMask").GetValue(me);
                    var o = new JObject();
                    foreach (var (side, eye) in new[] { ("front", new Vector3(0, 1.6f, 1.9f)), ("back", new Vector3(0, 1.6f, -1.9f)), ("left", new Vector3(-1.9f, 1.6f, 0)), ("low", new Vector3(1.6f, 0.9f, 0.3f)), ("above", new Vector3(0.6f, 1.8f, 0.6f)) })
                    {
                        Vector3 from = tub.transform.TransformPoint(eye), to = tub.transform.TransformPoint(new Vector3(0, 0.6f, 0));
                        var hits = Physics.RaycastAll(from, (to - from).normalized, 50f, mask).OrderBy(h => h.distance).ToArray();
                        RaycastHit first = hits.FirstOrDefault(h => !(h.collider.attachedRigidbody && h.collider.attachedRigidbody.gameObject == me.gameObject));
                        Interactable use = first.collider != null ? first.collider.GetComponentInParent<Interactable>() : null;
                        o[side] = first.collider == null ? "nothing" : $"{first.collider.name} {(use as Component)?.GetType().Name ?? "none"} at {first.distance:F1} m";
                    }
                    output(o); break;
                }
                case "cells": // (cells <name|quality|x|y;...>: sets where the nearest locker's things go back to, as if put away from there)
                {
                    Container box = UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None).Where(Locker.Is).OrderBy(x => Vector3.Distance(x.transform.position, me.transform.position)).FirstOrDefault();
                    if (box == null || a.Length < 3) { error("cells <record>, by a bath locker"); yield break; }
                    box.GetComponent<ZNetView>().ClaimOwnership(); box.GetComponent<ZNetView>().GetZDO().Set(Locker.CellsKey, a[2]);
                    output(new JObject { ["cells"] = a[2] }); break;
                }
                case "strip": // (takes off the helmet, chest, legs and cape into the bag; dress puts the same back on)
                {
                    _stripped = me.GetInventory().GetEquippedItems().Where(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Helmet || i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Chest
                        || i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Legs || i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shoulder).ToList();
                    foreach (ItemDrop.ItemData i in _stripped) me.UnequipItem(i, false);
                    yield return null;
                    output(new JObject { ["off"] = new JArray(_stripped.Select(i => i.m_shared.m_name)), ["dressed"] = Bathing.Dressed(me) }); break;
                }
                case "dress":
                {
                    foreach (ItemDrop.ItemData i in _stripped) if (me.GetInventory().ContainsItem(i)) me.EquipItem(i, false);
                    _stripped.Clear();
                    yield return null;
                    output(new JObject { ["dressed"] = Bathing.Dressed(me) }); break;
                }
                case "clear":
                    me.GetSEMan().RemoveStatusEffect(Effects.Bathed, true); me.GetSEMan().RemoveStatusEffect(Effects.Invigorated, true);
                    Bathing.Sat = 0; Bathing.Plunged = false;
                    output(new JObject { ["cleared"] = true }); break;
                default: error("bath status | room [x z] | grant bathed|invigorated [company] | steam | clear"); break;
            }
        }
    }
}
