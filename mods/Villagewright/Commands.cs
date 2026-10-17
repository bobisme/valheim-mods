using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Village;

namespace Villagewright
{
    // Claude Tools commands: `village ...` lays out, previews, builds and takes away villages without the stake.
    public sealed partial class Plugin
    {
        private BaseUnityPlugin _claude;
        private float _nextClaude;
        private void UpdateCommands()
        {
            if (Time.unscaledTime < _nextClaude) return; _nextClaude = Time.unscaledTime + 2;
            BaseUnityPlugin found = BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("com.quad.claudetools", out var info) ? info.Instance : null;
            if (found == _claude) return; _claude = found; if (found == null) return;
            try
            {
                found.GetType().GetMethod("RegisterCommand", BindingFlags.Static | BindingFlags.Public)?.Invoke(null, new object[]{Name, "village",
                    "village scout [range] [radius] [coast] | site <radius> [x z] | plot <x,z> <x,z> <x,z>... | options kind=hamlet|farming|fishing|market|hillfort|temple seed=N|new houses=N hall=on|off temple=on|off smithy=on|off market=on|off palisade=on|off dock=on|off torches=on|off clearbuilt=on|off fields=N storehouses=N gates=N detail=0..4 street=3..6 | road <x,z> [<x,z>] (egress points) or road <x,z> to <x,z> (egress, extension) or road clear | window [close] | plan | design | status | build | undo | preview on|off | list: lay out a village on the land and build it",
                    new Func<string[], Action<JObject>, Action<string>, IEnumerator>(Command)});
                found.GetType().GetMethod("RegisterFrame", BindingFlags.Static | BindingFlags.Public)?.Invoke(null, new object[] { Name, new Func<string, float[]>(Frame) });
            }
            catch (Exception ex) { Warn("Claude Tools link: " + ex.GetBaseException().Message); }
        }
        private void UnregisterCommands()
        { if (_claude != null) try { _claude.GetType().GetMethod("UnregisterAll", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object[] { Name }); } catch (Exception) { } _claude = null; }
        private float[] Frame(string name)
        {
            if (name != "village" || Plan == null) return null;
            return new[] { (float)Plan.Square.X, (float)Plan.SquareHeight, (float)Plan.Square.Z, 0f };
        }
        private static float F(string s) { if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) throw new ArgumentException("Not a number: " + s); return v; }
        private static bool On(string s) { if (s != "on" && s != "off") throw new ArgumentException("Use on or off."); return s == "on"; }

        private IEnumerator Command(string[] args, Action<JObject> output, Action<string> error)
        {
            try
            {
                string action = args.Length > 1 ? args[1].ToLowerInvariant() : "status";
                Player player = Player.m_localPlayer;
                switch (action)
                {
                    case "site":
                        {
                            if (args.Length < 3) throw new ArgumentException("village scout [range] [radius] [coast] | site <radius> [x z]");
                            Vector3 at = args.Length >= 5 ? new Vector3(F(args[3]), 0, F(args[4])) : player.transform.position;
                            at.y = ZoneSystem.instance.GetGroundHeight(at);
                            ClearRoad(); SetCircle(at, F(args[2])); output(Status()); return null;
                        }
                    case "plot":
                        {
                            var corners = args.Skip(2).Select(a => { var p = a.Split(','); var v = new Vector3(F(p[0]), 0, F(p[1])); v.y = ZoneSystem.instance.GetGroundHeight(v); return v; }).ToList();
                            if (corners.Count < 3) throw new ArgumentException("village plot <x,z> <x,z> <x,z>...");
                            ClearRoad(); PlotCorners.Clear(); PlotCorners.AddRange(corners); SetPlot(corners); output(Status()); return null;
                        }
                    case "options":
                        {
                            var b = CurrentBrief();
                            foreach (var a in args.Skip(2))
                            {
                                var kv = a.Split('='); if (kv.Length != 2) throw new ArgumentException("Options are key=value.");
                                string k = kv[0].ToLowerInvariant(), v = kv[1].ToLowerInvariant();
                                switch (k)
                                {
                                    case "kind":
                                        Kind = (Kind)Enum.Parse(typeof(Kind), v, true);
                                        b = Brief = Brief.For(Kind, Site?.Radius ?? 60, b.Seed); break;
                                    case "seed": b.Seed = v == "new" ? NewSeed() : int.Parse(v); break;
                                    case "houses": b.Houses = Mathf.Clamp(int.Parse(v), 0, 40); break;
                                    case "hall": b.Hall = On(v); break;
                                    case "keep": b.Keep = On(v); break;
                                    case "land": LandChoice = v == "auto" ? (Land?)null : (Land)Enum.Parse(typeof(Land), v, true); break;
                                    case "temple": b.Temple = On(v); break;
                                    case "smithy": b.Smithy = On(v); break;
                                    case "market": b.Market = On(v); break;
                                    case "palisade": b.Palisade = On(v); break;
                                    case "dock": b.Dock = On(v); break;
                                    case "torches": b.Torches = On(v); break;
                                    case "clearbuilt": ClearBuilt = On(v); break;
                                    case "fields": b.Fields = Mathf.Clamp(int.Parse(v), 0, 16); break;
                                    case "storehouses": b.Storehouses = Mathf.Clamp(int.Parse(v), 0, 6); break;
                                    case "gates": b.Gates = Mathf.Clamp(int.Parse(v), 1, 6); break;
                                    case "detail": b.Detail = Mathf.Clamp(int.Parse(v), 0, 4); break;
                                    case "street": b.StreetWidth = Mathf.Clamp(F(v), 3, 6); break;
                                    default: throw new ArgumentException("Unknown option " + k);
                                }
                            }
                            Replan(); output(Status()); return null;
                        }
                    case "road":
                        {
                            // village road <x,z> [<x,z>] | road <x,z> to <x,z> | road clear
                            ClearRoad();
                            if (args.Length > 2 && args[2] != "clear")
                                foreach (var a in args.Skip(2).Where(a => a != "to"))
                                {
                                    var p = a.Split(','); var v = new Vector3(F(p[0]), 0, F(p[1])); v.y = ZoneSystem.instance.GetGroundHeight(v);
                                    AddRoadPoint(v);
                                }
                            Replan(); output(Status()); return null;
                        }
                    case "scout":
                        {
                            float range = args.Length > 2 ? F(args[2]) : 600, radius = args.Length > 3 ? F(args[3]) : 70;
                            bool coast = args.Length > 4 && args[4] == "coast";
                            output(Scout(player.transform.position, range, radius, coast)); return null;
                        }
                    case "window": if (args.Length > 2 && args[2] == "close") VillageWindow.Close(); else VillageWindow.Open(); output(new JObject { ["open"] = VillageWindow.IsOpen }); return null;
                    case "plan": Replan(); output(Status()); return null;
                    case "status": output(Status()); return null;
                    case "design":
                        {
                            var designs = DesignAll() ?? throw new ArgumentException(Problem ?? "Lay out a village first.");
                            output(new JObject
                            {
                                ["pieces"] = designs.Sum(d => d.Pieces.Count),
                                ["buildings"] = new JArray(designs.Select(d => new JObject { ["name"] = d.Lot.Name, ["style"] = d.Lot.Style.ToString(), ["size"] = $"{d.Lot.Width}x{d.Lot.Depth}", ["pieces"] = d.Pieces.Count, ["problem"] = d.Problem, ["furniture"] = string.Join(" ", d.Pieces.Where(x => x.Role == "bed" || x.Role == "chest" || x.Role == "table" || x.Role == "rug" || x.Role == "barrel").Select(x => x.Prefab)), ["cells"] = d.Design?.Cells.Count })),
                            });
                            return null;
                        }
                    case "build": Build(); output(new JObject { ["building"] = true, ["pieces"] = BuildTotal }); return null;
                    case "undo": output(new JObject { ["result"] = Undo() }); return null;
                    case "preview": ShowPreview = args.Length < 3 || On(args[2]); DrawPreview(); output(new JObject { ["preview"] = ShowPreview }); return null;
                    case "list":
                        output(new JObject { ["villages"] = new JArray(ReadRecord().Villages.Select(v => new JObject { ["name"] = v.Name, ["at"] = new JArray(v.X, v.Z), ["seed"] = v.Seed, ["pieces"] = v.Ids.Count, ["groundTiles"] = v.Tiles.Count, ["built"] = v.Built })) });
                        return null;
                    default: throw new ArgumentException("Unknown village command: " + action);
                }
            }
            catch (Exception ex) { error(ex is ArgumentException ? ex.Message : ex.ToString()); return null; }
        }

        internal JObject Status()
        {
            var o = new JObject { ["problem"] = Problem, ["build"] = new JObject { ["state"] = BuildState, ["placed"] = BuildPlaced, ["total"] = BuildTotal, ["cleared"] = BuildCleared, ["removed"] = BuildRemoved, ["kept"] = BuildKept, ["groundPoints"] = BuildGround, ["clamped"] = BuildClamped, ["problems"] = new JArray(BuildProblems) } };
            if (Egress.Count > 0) o["road"] = new JObject { ["egress"] = new JArray(Egress.Select(e => new JArray(Math.Round(e.x, 1), Math.Round(e.z, 1)))), ["extension"] = Extension.HasValue ? new JArray(Math.Round(Extension.Value.x, 1), Math.Round(Extension.Value.z, 1)) : null, ["worldRoad"] = Plan?.Streets.Any(s => s.World) ?? false };
            if (Site != null) o["site"] = new JObject { ["centre"] = new JArray(Math.Round(Site.Centre.X, 1), Math.Round(Site.Centre.Z, 1)), ["radius"] = Math.Round(Site.Radius, 1), ["plot"] = Site.Plot?.Count ?? 0 };
            if (Brief != null) o["brief"] = JObject.FromObject(Brief);
            if (Plan == null) return o;
            o["plan"] = new JObject
            {
                ["square"] = new JArray(Math.Round(Plan.Square.X, 1), Math.Round(Plan.SquareHeight, 2), Math.Round(Plan.Square.Z, 1)), ["squareRadius"] = Plan.SquareRadius,
                ["streets"] = Plan.Streets.Count, ["streetMetres"] = Math.Round(Plan.Streets.Sum(s => Geometry.Length(s.Points))),
                ["lots"] = new JArray(Plan.Lots.Select(l => new JObject { ["name"] = l.Name, ["use"] = l.Use.ToString(), ["style"] = l.Style.ToString(), ["size"] = $"{l.Width}x{l.Depth}", ["centre"] = new JArray(Math.Round(l.Centre.X, 1), Math.Round(l.Centre.Z, 1)), ["yaw"] = Math.Round(l.Yaw, 1), ["pad"] = l.Pad, ["door"] = new JArray(Math.Round(l.Door.X, 1), Math.Round(l.Door.Z, 1)) })),
                ["fields"] = Plan.Fields.Count, ["props"] = Plan.Props.Count, ["palisade"] = Plan.Palisade.Count, ["gates"] = Plan.Gates.Count,
                ["shore"] = Plan.Shore.HasValue ? new JArray(Math.Round(Plan.Shore.Value.X, 1), Math.Round(Plan.Shore.Value.Z, 1)) : null,
                ["notes"] = new JArray(Plan.Notes), ["ms"] = Plan.Millis,
            };
            return o;
        }
    }
}
