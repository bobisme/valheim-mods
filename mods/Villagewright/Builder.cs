using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using Village;

namespace Villagewright
{
    // Building a planned village for real: trees and rocks cleared off it, the ground levelled and painted, then every piece put down a
    // building at a time (spread over frames). Everything it places and every ground point it changes is written down per world
    // (BepInEx/config/villagewright-<world>.json), so Undo takes the last village away and puts the ground back.
    public sealed partial class Plugin
    {
        private static readonly FieldInfo Levels = AccessTools.Field(typeof(TerrainComp), "m_levelDelta"), Smooths = AccessTools.Field(typeof(TerrainComp), "m_smoothDelta"),
            Modified = AccessTools.Field(typeof(TerrainComp), "m_modifiedHeight"), Ops = AccessTools.Field(typeof(TerrainComp), "m_operations"),
            OpPoint = AccessTools.Field(typeof(TerrainComp), "m_lastOpPoint"), OpRadius = AccessTools.Field(typeof(TerrainComp), "m_lastOpRadius"),
            PaintMask = AccessTools.Field(typeof(TerrainComp), "m_paintMask"), Painted = AccessTools.Field(typeof(TerrainComp), "m_modifiedPaint"),
            CompView = AccessTools.Field(typeof(TerrainComp), "m_nview");
        private static readonly MethodInfo SaveComp = AccessTools.Method(typeof(TerrainComp), "Save");

        public sealed class Record { public long World; public List<VillageRecord> Villages = new List<VillageRecord>(); }
        public sealed class VillageRecord
        {
            public string Name; public float X, Z; public int Seed; public string Kind; public string Built;
            public List<long> Users = new List<long>(); public List<uint> Ids = new List<uint>();
            public List<TileRecord> Tiles = new List<TileRecord>();
            public List<RemovedPiece> Removed = new List<RemovedPiece>();
        }
        // A construction that stood in the village's way, to put back on undo.
        public sealed class RemovedPiece { public string Prefab; public float X, Y, Z, RX, RY, RZ, RW; public long Creator; }
        public sealed class TileRecord
        {
            public float X, Y, Z; public int Width;
            public List<int> Index = new List<int>(); public List<float> Level = new List<float>(), Smooth = new List<float>();
            public List<bool> WasModified = new List<bool>(); public List<float> Paint = new List<float>(); public List<bool> WasPainted = new List<bool>();
            public List<float> AfterLevel = new List<float>();
        }

        private string RecordPath => Path.Combine(Paths.ConfigPath, "villagewright-" + ZNet.instance.GetWorldUID() + ".json");
        internal Record ReadRecord()
        {
            try { if (File.Exists(RecordPath)) return JsonConvert.DeserializeObject<Record>(File.ReadAllText(RecordPath)) ?? new Record(); }
            catch (Exception ex) { Warn("Village record: " + ex.Message); }
            return new Record { World = ZNet.instance.GetWorldUID() };
        }
        private void WriteRecord(Record r) { string tmp = RecordPath + ".tmp"; File.WriteAllText(tmp, JsonConvert.SerializeObject(r)); if (File.Exists(RecordPath)) File.Delete(RecordPath); File.Move(tmp, RecordPath); }

        private Coroutine _building;
        internal string BuildState = "idle";
        internal int BuildPlaced, BuildTotal, BuildCleared, BuildRemoved, BuildKept, BuildGround, BuildClamped;
        internal bool ClearBuilt = true;   // take down other constructions in the way (else only the world's own ruins)
        internal readonly List<string> BuildProblems = new List<string>();
        internal List<Building> Designs;
        private void TickBuild() { }
        private void StopBuild() { if (_building != null) StopCoroutine(_building); _building = null; }
        internal bool Building => _building != null;

        // Design every building of the plan (no world changes): problems show before anything is built.
        internal List<Building> DesignAll()
        {
            Designs = Plan == null ? null : Plan.Lots.Select(Buildings.Design).ToList();
            return Designs;
        }

        internal void Build()
        {
            if (Plan == null) throw new ArgumentException(Problem ?? "Lay out a village first.");
            if (Building) throw new ArgumentException("A village is already going up.");
            var designs = DesignAll();
            var bad = designs.Where(d => d.Problem != null).ToList();
            foreach (var d in bad) Warn($"{d.Lot.Name}: {d.Problem}");
            _building = StartCoroutine(BuildRoutine(Plan, designs));
        }

        private IEnumerator BuildRoutine(Plan plan, List<Building> designs)
        {
            BuildProblems.Clear(); BuildPlaced = BuildCleared = BuildRemoved = BuildKept = BuildGround = BuildClamped = 0;
            BuildTotal = designs.Sum(d => d.Pieces.Count + (SignText(d.Lot.Use) != null ? 2 : 0)) + plan.Props.Count;
            var record = new VillageRecord { Name = plan.Brief.Kind + " village", X = (float)plan.Square.X, Z = (float)plan.Square.Z, Seed = plan.Brief.Seed, Kind = plan.Brief.Kind.ToString(), Built = DateTime.Now.ToString("s") };
            var all = ReadRecord(); all.World = ZNet.instance.GetWorldUID(); all.Villages.Add(record);
            void Save() { try { WriteRecord(all); } catch (Exception ex) { Warn("Village record: " + ex.Message); } }
            try
            {
                BuildState = "clearing";
                BuildCleared = Clear(plan, designs, record);
                Save();
                yield return null;
                BuildState = "ground";
                Ground(plan, designs, record);
                Save();
                // Let the heightmaps and their colliders catch up before anything stands on them.
                for (int i = 0; i < 3; i++) yield return null;
                BuildState = "building";
                long me = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerID() : 0;
                foreach (var b in designs.Where(d => d.Problem == null))
                {
                    foreach (var p in b.Pieces) Place(p.Prefab, p.Position, p.Rotation, me, record);
                    Save();
                    yield return null;
                }
                int n = 0;
                foreach (var prop in plan.Props)
                {
                    Place(prop.Prefab, new Vector3((float)prop.X, (float)prop.Y, (float)prop.Z), Quaternion.Euler(0, (float)prop.Yaw, 0), me, record);
                    if (++n % 150 == 0) { Save(); yield return null; }
                }
                // Signs by the doors of the working buildings, on a post at the street side of the wall.
                foreach (var b in designs.Where(d => d.Problem == null)) Signpost(b.Lot, me, record);
                Save();
                BuildState = "done"; ShowPreview = false; ClearPreview();
                Say($"the village is built: {BuildPlaced} pieces" + (BuildProblems.Count > 0 ? $", {BuildProblems.Count} problems (see the log)" : "") + ".");
            }
            finally { _building = null; if (BuildState != "done") BuildState = "stopped"; Save(); }
        }

        internal static string SignText(Use use) => use switch { Use.Smithy => "Smithy", Use.Storehouse => "Storehouse", Use.Boathouse => "Boathouse", _ => null };
        private void Signpost(Lot lot, long me, VillageRecord record)
        {
            string text = SignText(lot.Use);
            if (text == null) return;
            double along = lot.DoorAlong + 2.6 <= lot.Width - 0.6 ? lot.DoorAlong + 2.6 : lot.DoorAlong - 2.6;
            P2 post = lot.World(along, -0.7), face = lot.World(along, -0.95);
            float ground = ZoneSystem.instance.GetGroundHeight(new Vector3((float)post.X, 0, (float)post.Z));
            Place("wood_pole2", new Vector3((float)post.X, ground + 1f, (float)post.Z), Quaternion.Euler(0, (float)lot.Yaw, 0), me, record);
            GameObject sign = Place("sign", new Vector3((float)face.X, ground + 1.55f, (float)face.Z), Quaternion.Euler(0, (float)lot.Yaw + SignTurn, 0), me, record);
            sign?.GetComponent<Sign>()?.SetText(text);
        }
        internal static float SignTurn = 180f;

        private GameObject Place(string prefabName, Vector3 at, Quaternion rot, long creator, VillageRecord record)
        {
            GameObject prefab = Buildings.Prefab(prefabName);
            if (prefab == null) { if (!BuildProblems.Contains("missing " + prefabName)) { BuildProblems.Add("missing " + prefabName); Warn("This game has no " + prefabName + "; left out."); } return null; }
            GameObject go = Instantiate(prefab, at, rot);
            var piece = go.GetComponent<Piece>();
            if (piece != null && creator != 0)
                try { piece.SetCreator(creator, Splatform.PlatformManager.DistributionPlatform.LocalUser.PlatformUserID); } catch (Exception) { }
            var view = go.GetComponent<ZNetView>();
            if (view != null && view.GetZDO() != null)
            {
                view.GetZDO().Set("villagewright", record.Seed);
                // Fires and torches start full.
                var fire = go.GetComponent<Fireplace>();
                if (fire != null && !fire.m_infiniteFuel) view.GetZDO().Set(ZDOVars.s_fuel, fire.m_maxFuel);
                record.Users.Add(view.GetZDO().m_uid.UserID); record.Ids.Add(view.GetZDO().m_uid.ID);
            }
            BuildPlaced++;
            return go;
        }

        // Things in the way: trees, logs, stumps, rocks, bushes and wild plants, and constructions (never creatures or items). Constructions
        // are written down to put back on undo; a chest with something in it stays, and so do player-built pieces unless ClearBuilt.
        private int Clear(Plan plan, List<Building> designs, VillageRecord record)
        {
            int cleared = 0;
            // Everything natural inside the village itself goes (the houses, the square and the ground between them), and along its
            // streets, fields and palisade outside that.
            var corePoints = new List<P2>();
            foreach (var l in plan.Lots) corePoints.AddRange(l.Corners(6));
            for (int a = 0; a < 16; a++) corePoints.Add(plan.Square + P2.Forward(a * 22.5) * (plan.SquareRadius + 8));
            var core = corePoints.Count >= 3 ? Geometry.ConvexHull(corePoints) : null;
            var palisade = plan.Palisade.Count > 2 ? plan.Palisade.Concat(new[] { plan.Palisade[0] }).ToList() : null;
            bool InTheWay(Vector3 p)
            {
                var q = new P2(p.x, p.z);
                if (core != null && Inside(core, q)) return true;
                if (palisade != null && (Inside(plan.Palisade, q) || Geometry.PolylineDistance(q, palisade, out _, out _) < 3)) return true;
                foreach (var l in plan.Lots) if (l.Covers(q, 5)) return true;
                foreach (var f in plan.Fields) if (f.Covers(q, 3)) return true;
                foreach (var s in plan.Streets) if (Geometry.PolylineDistance(q, s.Points, out _, out _) < s.Width / 2 + 3) return true;
                foreach (var p2 in plan.Props) if (Math.Abs(p2.X - p.x) < 1.5 && Math.Abs(p2.Z - p.z) < 1.5) return true;
                return false;
            }
            foreach (var view in FindObjectsOfType<ZNetView>())
            {
                if (view == null || !view.IsValid()) continue;
                GameObject go = view.gameObject;
                if (go.GetComponent<Character>() != null || go.GetComponent<ItemDrop>() != null) continue;
                var piece = go.GetComponent<Piece>();
                if (piece != null)
                {
                    if (view.GetZDO().GetInt("villagewright") != 0 || !InTheWay(go.transform.position)) continue;
                    var box = go.GetComponent<Container>();
                    if ((piece.GetCreator() != 0 && !ClearBuilt) || (box != null && box.GetInventory() != null && box.GetInventory().NrOfItems() > 0)) { BuildKept++; continue; }
                    GameObject prefab = ZNetScene.instance.GetPrefab(view.GetZDO().GetPrefab());
                    if (prefab == null) continue;
                    Quaternion r = go.transform.rotation; Vector3 at = go.transform.position;
                    record.Removed.Add(new RemovedPiece { Prefab = prefab.name, X = at.x, Y = at.y, Z = at.z, RX = r.x, RY = r.y, RZ = r.z, RW = r.w, Creator = piece.GetCreator() });
                    view.ClaimOwnership(); ZNetScene.instance.Destroy(go); BuildRemoved++;
                    continue;
                }
                bool natural = go.GetComponent<TreeBase>() != null || go.GetComponent<TreeLog>() != null || go.GetComponent<MineRock>() != null || go.GetComponent<MineRock5>() != null
                    || go.GetComponent<Destructible>() != null || go.GetComponent<Pickable>() != null;
                if (!natural || view.GetZDO().GetInt("villagewright") != 0) continue;
                if (!InTheWay(go.transform.position)) continue;
                view.ClaimOwnership(); ZNetScene.instance.Destroy(go); cleared++;
            }
            return cleared;
        }

        private static bool Inside(List<P2> poly, P2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                if ((poly[i].Z > p.Z) != (poly[j].Z > p.Z) && p.X < (poly[j].X - poly[i].X) * (p.Z - poly[i].Z) / (poly[j].Z - poly[i].Z) + poly[i].X) inside = !inside;
            return inside;
        }
        // The ground: the plan's grading, longhouses dug in, painted; every changed point written down first.
        private void Ground(Plan plan, List<Building> designs, VillageRecord record)
        {
            plan.Ground.Bounds(out double x0, out double z0, out double x1, out double z1);
            var norse = designs.Where(d => d.Problem == null && d.Lot.Style == Style.Longhouse).ToList();
            foreach (Heightmap map in Heightmap.GetAllHeightmaps().ToList())
            {
                if (map == null || map.IsDistantLod) continue;
                float scale = map.m_scale; int w = map.m_width, half = w / 2; Vector3 o = map.transform.position;
                if (o.x + half * scale < x0 || o.x - half * scale > x1 || o.z + half * scale < z0 || o.z - half * scale > z1) continue;
                TerrainComp comp = map.GetAndCreateTerrainCompiler();
                var nview = CompView.GetValue(comp) as ZNetView;
                if (nview == null || !nview.IsValid()) continue;
                nview.ClaimOwnership();
                var levels = (float[])Levels.GetValue(comp); var smooth = (float[])Smooths.GetValue(comp); var modified = (bool[])Modified.GetValue(comp);
                var paint = (Color[])PaintMask.GetValue(comp); var painted = (bool[])Painted.GetValue(comp);
                var tile = new TileRecord { X = o.x, Y = o.y, Z = o.z, Width = w };
                for (int z = 0; z <= w; z++) for (int x = 0; x <= w; x++)
                {
                    double wx = o.x + (x - half) * scale, wz = o.z + (z - half) * scale;
                    if (wx < x0 || wx > x1 || wz < z0 || wz > z1) continue;
                    int index = z * (w + 1) + x;
                    double now = map.GetHeight(x, z) + o.y;
                    var g = plan.Ground.At(wx, wz, now);
                    double target = g?.y ?? now; Paint paintAs = g?.paint ?? Paint.None; bool touched = g.HasValue;
                    foreach (var b in norse)
                    {
                        double? dug = Buildings.Dig(b, wx, wz, target);
                        if (dug.HasValue) { if (dug.Value < target - 0.01) paintAs = Paint.Dirt; target = dug.Value; touched = true; }
                    }
                    if (!touched) continue;
                    float delta = (float)(target - now);
                    float level = levels[index] + delta;
                    // The game keeps ground within 8 m of how the world made it.
                    if (Math.Abs(level + smooth[index]) > 7.9f) { level = Mathf.Clamp(level + smooth[index], -7.9f, 7.9f) - smooth[index]; BuildClamped++; }
                    tile.Index.Add(index); tile.Level.Add(levels[index]); tile.Smooth.Add(smooth[index]); tile.WasModified.Add(modified[index]);
                    tile.AfterLevel.Add(level);
                    Color was = index < paint.Length ? paint[index] : Color.clear;
                    tile.Paint.AddRange(new[] { was.r, was.g, was.b, was.a }); tile.WasPainted.Add(index < painted.Length && painted[index]);
                    levels[index] = level; modified[index] = true;
                    if (paintAs != Paint.None && index < paint.Length)
                    {
                        paint[index] = paintAs == Paint.Paved ? Heightmap.m_paintMaskPaved : paintAs == Paint.Cultivated ? Heightmap.m_paintMaskCultivated : Heightmap.m_paintMaskDirt;
                        painted[index] = true;
                    }
                    BuildGround++;
                }
                if (tile.Index.Count == 0) continue;
                record.Tiles.Add(tile);
                SaveTile(comp, map, o, w * scale / 2);
            }
        }
        private static void SaveTile(TerrainComp comp, Heightmap map, Vector3 o, float radius)
        {
            Ops.SetValue(comp, (int)Ops.GetValue(comp) + 1); OpPoint.SetValue(comp, o); OpRadius.SetValue(comp, radius);
            SaveComp.Invoke(comp, new object[] { false });
            map.Poke();
            try { ClutterSystem.instance?.ResetGrass(o, radius); } catch (Exception) { }
        }

        // Take the last village away: its pieces (built or not) and its ground (where nothing else has changed it since).
        internal string Undo()
        {
            var all = ReadRecord();
            if (all.Villages.Count == 0) throw new ArgumentException("No village to take away in this world.");
            var v = all.Villages[all.Villages.Count - 1];
            int removed = 0;
            for (int i = 0; i < v.Ids.Count; i++)
            {
                ZDO zdo = ZDOMan.instance.GetZDO(new ZDOID(v.Users[i], v.Ids[i]));
                if (zdo == null) continue;
                ZNetView view = ZNetScene.instance.FindInstance(zdo);
                if (view != null) { view.ClaimOwnership(); ZNetScene.instance.Destroy(view.gameObject); }
                else { zdo.SetOwner(ZDOMan.GetSessionID()); ZDOMan.instance.DestroyZDO(zdo); }
                removed++;
            }
            // The constructions it took down go back where they stood (once: the record forgets them as they go back).
            int returned = 0;
            foreach (var r in v.Removed)
            {
                GameObject prefab = Buildings.Prefab(r.Prefab);
                if (prefab == null) continue;
                GameObject go = Instantiate(prefab, new Vector3(r.X, r.Y, r.Z), new Quaternion(r.RX, r.RY, r.RZ, r.RW));
                var zdo = go.GetComponent<ZNetView>()?.GetZDO();
                if (zdo != null && r.Creator != 0) zdo.Set(ZDOVars.s_creator, r.Creator);
                returned++;
            }
            if (v.Removed.Count > 0) { v.Removed.Clear(); WriteRecord(all); }
            int restored = 0, skipped = 0, missing = 0;
            foreach (var t in v.Tiles)
            {
                Heightmap map = Heightmap.FindHeightmap(new Vector3(t.X, t.Y, t.Z));
                if (map == null || map.transform.position != new Vector3(t.X, t.Y, t.Z)) { missing++; continue; }
                TerrainComp comp = map.GetAndCreateTerrainCompiler();
                (CompView.GetValue(comp) as ZNetView)?.ClaimOwnership();
                var levels = (float[])Levels.GetValue(comp); var smooth = (float[])Smooths.GetValue(comp); var modified = (bool[])Modified.GetValue(comp);
                var paint = (Color[])PaintMask.GetValue(comp); var painted = (bool[])Painted.GetValue(comp);
                for (int i = 0; i < t.Index.Count; i++)
                {
                    int k = t.Index[i];
                    if (Math.Abs(levels[k] - t.AfterLevel[i]) > 0.001f) { skipped++; continue; }
                    levels[k] = t.Level[i]; smooth[k] = t.Smooth[i]; modified[k] = t.WasModified[i];
                    if (k < paint.Length) { paint[k] = new Color(t.Paint[i * 4], t.Paint[i * 4 + 1], t.Paint[i * 4 + 2], t.Paint[i * 4 + 3]); painted[k] = t.WasPainted[i]; }
                    restored++;
                }
                SaveTile(comp, map, map.transform.position, t.Width * map.m_scale / 2);
            }
            if (missing > 0) throw new ArgumentException($"Removed {removed} pieces, but {missing} of the village's ground tiles aren't loaded: stand in the village and undo again to put its ground back.");
            all.Villages.RemoveAt(all.Villages.Count - 1); WriteRecord(all);
            return $"Took the village away: {removed} pieces, {restored} ground points put back" + (returned > 0 ? $", {returned} old constructions put back" : "") + (skipped > 0 ? $" ({skipped} changed since, left alone)" : "") + ".";
        }
    }
}
