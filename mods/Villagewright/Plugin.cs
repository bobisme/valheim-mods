using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using Village;

[assembly: System.Reflection.AssemblyMetadata("ClaudeTools.Reload", "world")]

namespace Villagewright
{
    // Lays out a whole village on the land (the layout engine, Village.Planner, is Fimbulwinter's) and builds it: the ground levelled
    // and painted, every house, hall and temple designed by Hallwright's geometry, and the palisade, fields, torches and dock put down.
    // Pick the Villagewright stake: click the ground for the village's centre, click again for its edge (a circle), or Shift+click
    // round a plot; press E for the settings.
    [BepInPlugin(Guid, Name, Version)]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        public const string Guid = "cx.bob.villagewright";
        public const string Name = "Villagewright";
        public const string Version = "0.1.3";
        internal static Plugin Instance;
        private Harmony _harmony;
        internal ConfigEntry<bool> Enabled;
        internal ConfigEntry<KeyCode> SettingsKey, PlotModifier, RoadModifier, BackKey;

        // The site and brief being designed, and its plan.
        internal Site Site;
        internal Kind Kind = Kind.Hamlet;
        internal Brief Brief;
        internal Plan Plan;
        internal string Problem;
        internal readonly List<Vector3> PlotCorners = new List<Vector3>();
        // Where the road meets the village: one or two egress points, or one and an extension point on the road (Ctrl+click).
        internal readonly List<Vector3> Egress = new List<Vector3>();
        internal readonly List<string> EgressRoad = new List<string>();
        internal Vector3? Extension;
        internal Vector3? CentreMark;

        private void Awake()
        {
            Instance = this;
            Enabled = Config.Bind("General", "Enabled", true, "The Villagewright stake in the hammer's build menu (Misc).");
            SettingsKey = Config.Bind("Controls", "Settings", KeyCode.L, "Open the village settings while the stake is picked.");
            PlotModifier = Config.Bind("Controls", "PlotModifier", KeyCode.LeftShift, "Hold while clicking with the stake to mark a plot's corners instead of a circle.");
            RoadModifier = Config.Bind("Controls", "RoadModifier", KeyCode.LeftControl, "Hold while clicking with the stake to mark where the road meets the village: on the site's edge an egress point (one or two), well outside it with one egress point the extension point on the road that a lane goes on to. With the backspace key, removes the last.");
            BackKey = Config.Bind("Controls", "RemoveMarker", KeyCode.Backspace, "Remove the last plot corner (or the circle).");
            _harmony = new Harmony(Guid); _harmony.PatchAll(typeof(Plugin).Assembly);
            Logger.LogInfo($"{Name} {Version} loaded; pick the Villagewright stake in the hammer's build menu (Misc).");
        }

        private void Update()
        {
            UpdateCommands(); EnsureStake(); TickBuild(); TickStake(); VillageWindow.Tick();
        }

        private void OnDestroy()
        {
            StopBuild(); ClearPreview(); UnregisterCommands(); DestroyStake(); VillageWindow.Close(); GameUi.Destroy();
            _harmony?.UnpatchSelf();
            if (Instance == this) Instance = null;
        }

        internal static void Say(string text) => Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "Villagewright: " + text);
        internal void Log(string text) => Logger.LogInfo(text);
        internal void Warn(string text) => Logger.LogWarning(text);

        internal Brief CurrentBrief()
        {
            if (Brief == null || Brief.Kind != Kind) Brief = Brief.For(Kind, Site?.Radius ?? 60, Brief?.Seed ?? NewSeed());
            Brief.Egress = Egress.Select(e => new P2(e.x, e.z)).ToList();
            Brief.Extension = Extension.HasValue ? new P2(Extension.Value.x, Extension.Value.z) : (P2?)null;
            // Two points on one of Fimbulwinter's roads: the village follows the road itself.
            Brief.Road = Egress.Count == 2 && EgressRoad[0] != null && EgressRoad[0] == EgressRoad[1] ? WorldRoads.Points(EgressRoad[0]) : null;
            return Brief;
        }
        internal static int NewSeed() => UnityEngine.Random.Range(1, 999999);

        // Lay the village out again from the site and brief.
        internal void Replan()
        {
            Plan = null; Problem = null;
            if (Site == null) { Problem = "Mark the village's site first."; ClearPreview(); return; }
            try { Plan = Planner.Make(Site, CurrentBrief(), new LiveGround()); }
            catch (ArgumentException ex) { Problem = ex.Message; }
            catch (Exception ex) { Problem = ex.GetBaseException().Message; Warn("Village layout: " + ex); }
            DrawPreview();
        }

        internal void ClearRoad() { Egress.Clear(); EgressRoad.Clear(); Extension = null; }
        // A road point: on the site's edge an egress point, well outside it (with one egress point) the extension point.
        internal string AddRoadPoint(Vector3 at)
        {
            if (Site == null) throw new ArgumentException("Mark the site first.");
            bool snapped = WorldRoads.Nearest(at, 20, out Vector3 onRoad, out string road);
            if (snapped) at = onRoad;
            double inset = Site.Inset(new P2(at.x, at.z));
            if (inset < -12)
            {
                if (Egress.Count != 1) throw new ArgumentException("An extension point (well outside the site) goes with exactly one egress point on its edge.");
                Extension = at; Replan();
                return "the lane goes on to the road" + (snapped ? " (on " + road + ")" : "") + ".";
            }
            if (Math.Abs(inset) > 12) throw new ArgumentException("Put an egress point on the site's edge (within 12 m of it), or the extension point well outside it.");
            if (Egress.Count == 2) throw new ArgumentException("Two egress points already: Ctrl+Backspace removes the last.");
            if (Extension.HasValue) throw new ArgumentException("With an extension point there's one egress point: Ctrl+Backspace removes the extension first.");
            Egress.Add(at); EgressRoad.Add(snapped ? road : null); Replan();
            return (Egress.Count == 2 ? "the road runs through the village" : "the village's way out to the road") + (snapped ? " (on " + road + ")" : "") + ".";
        }
        internal void RemoveRoadPoint()
        {
            if (Extension.HasValue) Extension = null;
            else if (Egress.Count > 0) { Egress.RemoveAt(Egress.Count - 1); EgressRoad.RemoveAt(EgressRoad.Count - 1); }
            Replan();
        }

        internal void SetCircle(Vector3 centre, float radius)
        {
            PlotCorners.Clear(); CentreMark = centre;
            Site = Village.Site.Circle(centre.x, centre.z, Mathf.Clamp(radius, 24, 160));
            if (Brief != null) { int seed = Brief.Seed; Brief = Brief.For(Kind, Site.Radius, seed); }
            Replan();
        }
        internal void SetPlot(IList<Vector3> corners)
        {
            CentreMark = null;
            Site = corners.Count >= 3 ? Village.Site.Polygon(corners.Select(c => new P2(c.x, c.z)).ToList()) : null;
            if (Site != null && Brief != null) { int seed = Brief.Seed; Brief = Brief.For(Kind, Site.Radius, seed); }
            Replan();
        }
    }

    // The land as the game has it now (loaded terrain), or as the world made it where it isn't loaded.
    internal sealed class LiveGround : IGround
    {
        public double WaterLevel => ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30;
        public double Height(double x, double z)
        {
            var at = new Vector3((float)x, 0, (float)z);
            if (Heightmap.GetHeight(at, out float h)) return h;
            return WorldGenerator.instance != null ? WorldGenerator.instance.GetHeight((float)x, (float)z) : 0;
        }
    }
}
