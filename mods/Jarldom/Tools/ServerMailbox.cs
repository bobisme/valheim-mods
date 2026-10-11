using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Jarldom
{
    // Test only, on a dedicated server (no player, so Claude Tools turns its commands away): the jarldom commands from a folder of its
    // own, BepInEx/claude/jarldom/<name>.txt (a command a line), answered in <name>.done.json. "focus <x> <z>" loads the world round
    // a point as a player standing there would, so a colony there wakes and runs with no one in it.
    internal static class ServerMailbox
    {
        private static float _next; private static string _dir;
        public static void Tick()
        {
            if (Time.unscaledTime < _next || ZNet.instance == null || !ZNet.instance.IsDedicated()) return;
            _next = Time.unscaledTime + 0.5f;
            if (_dir == null) { _dir = Path.Combine(BepInEx.Paths.BepInExRootPath, "claude", "jarldom"); Directory.CreateDirectory(_dir); }
            foreach (string file in Directory.GetFiles(_dir, "*.txt"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                string[] lines;
                try { lines = File.ReadAllLines(file); File.Delete(file); } catch { continue; }
                var outputs = new JArray(); var errors = new JArray();
                foreach (string raw in lines.Select(l => l.Trim()).Where(l => l != ""))
                {
                    string[] a = raw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    try
                    {
                        if (a[0] == "focus" && a.Length >= 3)
                        {
                            Vector3 at = new Vector3(float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture), 30f, float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture));
                            ZNet.instance.SetReferencePosition(at);
                            outputs.Add(new JObject { ["focus"] = $"{at.x:0},{at.z:0}" });
                        }
                        else if (a[0] == "probe")
                        {
                            Vector3 r = ZNet.instance.GetReferencePosition();
                            int pillars = Assets.Instances(Pieces.PillarsName.GetStableHashCode()).Count();
                            outputs.Add(new JObject { ["reference"] = $"{r.x:0},{r.y:0},{r.z:0}", ["objects"] = ZNetScene.instance?.NrOfInstances() ?? -1, ["colonies"] = Colony.All.Count(), ["pillars"] = pillars,
                                ["villagers"] = Villagers.Bodies().Count(), ["brains"] = Villagers.All.Count(), ["zoneLoaded"] = ZoneSystem.instance.IsZoneLoaded(r), ["time"] = ZNet.instance.GetTimeSeconds() });
                        }
                        else if (a[0] == "jarldom") Tools.RunNow(a, o => outputs.Add(o), e => errors.Add(e));
                        else errors.Add("unknown: " + raw + " (focus <x> <z> | jarldom ...)");
                    }
                    catch (Exception e) { errors.Add(raw + ": " + e.Message); }
                }
                var done = new JObject { ["request"] = name, ["time"] = DateTime.Now.ToString("s"), ["outputs"] = outputs, ["errors"] = errors, ["ok"] = errors.Count == 0 };
                try { File.WriteAllText(Path.Combine(_dir, name + ".done.json"), done.ToString()); } catch { }
            }
        }
    }
}
