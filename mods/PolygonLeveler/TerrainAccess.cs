using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PolygonLeveler
{
    internal static class TerrainAccess
    {
        private const string RequestRpc = "Bobisme_PolygonLeveler_Request_v2";
        private const string ReplyRpc = "Bobisme_PolygonLeveler_Reply_v2";
        private static readonly FieldInfo Right = AccessTools.Field(typeof(Humanoid), "m_rightItem");
        private static readonly MethodInfo InputMethod = AccessTools.Method(typeof(Player), "TakeInput");
        private static readonly MethodInfo Wear = AccessTools.Method(typeof(Player), "GetPlaceDurability");
        private static readonly MethodInfo InternalOperation = AccessTools.Method(typeof(TerrainComp), "InternalDoOperation");
        private static readonly MethodInfo Save = AccessTools.Method(typeof(TerrainComp), "Save");
        private static readonly MethodInfo Load = AccessTools.Method(typeof(TerrainComp), "CheckLoad");
        private static readonly FieldInfo HMap = AccessTools.Field(typeof(TerrainComp), "m_hmap");
        private static readonly FieldInfo Levels = AccessTools.Field(typeof(TerrainComp), "m_levelDelta");
        private static readonly FieldInfo Smooth = AccessTools.Field(typeof(TerrainComp), "m_smoothDelta");
        private static readonly FieldInfo Modified = AccessTools.Field(typeof(TerrainComp), "m_modifiedHeight");
        private static readonly FieldInfo Operations = AccessTools.Field(typeof(TerrainComp), "m_operations");
        private static readonly FieldInfo LastPoint = AccessTools.Field(typeof(TerrainComp), "m_lastOpPoint");
        private static readonly FieldInfo LastRadius = AccessTools.Field(typeof(TerrainComp), "m_lastOpRadius");
        private static readonly FieldInfo Areas = AccessTools.Field(typeof(PrivateArea), "m_allAreas");
        private static readonly MethodInfo AreaEnabled = AccessTools.Method(typeof(PrivateArea), "IsEnabled");
        private static readonly MethodInfo AreaInside = AccessTools.Method(typeof(PrivateArea), "IsInside");
        private static readonly MethodInfo AreaPermitted = AccessTools.Method(typeof(PrivateArea), "IsPermitted");
        private static readonly HashSet<ZNetView> Views = new HashSet<ZNetView>();
        private static readonly Dictionary<string, Action<bool, string>> Callbacks = new Dictionary<string, Action<bool, string>>();
        private static readonly Dictionary<string, Prepared> Requests = new Dictionary<string, Prepared>();
        private static TerrainComp _captureCompiler;
        private static float[] _underlying, _limitBase;

        internal sealed class Batch
        {
            internal TerrainComp Compiler;
            internal Heightmap Map;
            internal float[] Targets;
            internal float[] Deltas;
            internal readonly List<Vector2Int> Cells = new List<Vector2Int>();
        }
        private sealed class Prepared
        {
            internal Batch Batch;
            internal ZDOID Player;
            internal long Sender;
            internal float Expires;
            internal bool Applied;
            internal bool UndoRequested, Undone;
            internal TerrainSnapshot Before, After;
        }
        internal static ItemDrop.ItemData RightItem(Player p) => Right.GetValue(p) as ItemDrop.ItemData;
        internal static bool TakeInput(Player p) => (bool)InputMethod.Invoke(p, null);
        internal static float Durability(Player p, ItemDrop.ItemData hoe) => (float)Wear.Invoke(p, new object[] { hoe });

        internal static List<Batch> Plan(Player player, IReadOnlyList<Point> hull, float height, float distance, bool fitPlane, out GroundPlane plane)
        {
            var batches = new List<Batch>();
            int count = 0;
            double minX = hull.Min(p => p.X), maxX = hull.Max(p => p.X), minZ = hull.Min(p => p.Z), maxZ = hull.Max(p => p.Z);
            if (maxX - minX > 60 || maxZ - minZ > 60) throw new Exception("Polygon is too wide.");
            foreach (Heightmap map in Heightmap.GetAllHeightmaps())
            {
                if (map == null || map.IsDistantLod || !map.IsPointInside(new Vector3((float)((minX + maxX) / 2), height, (float)((minZ + maxZ) / 2)), 45f)) continue;
                float scale = map.m_scale;
                if (!(scale > 0) || !Geometry.Finite(scale)) throw new Exception("Invalid terrain grid.");
                Vector3 origin = map.transform.position;
                int half = map.m_width / 2;
                int x0 = Mathf.Max(0, Mathf.CeilToInt((float)((minX - origin.x) / scale) + half));
                int x1 = Mathf.Min(map.m_width, Mathf.FloorToInt((float)((maxX - origin.x) / scale) + half));
                int z0 = Mathf.Max(0, Mathf.CeilToInt((float)((minZ - origin.z) / scale) + half));
                int z1 = Mathf.Min(map.m_width, Mathf.FloorToInt((float)((maxZ - origin.z) / scale) + half));
                if ((long)Math.Max(0, x1 - x0 + 1) * Math.Max(0, z1 - z0 + 1) > 8192) throw new Exception("Terrain grid is too dense for this polygon.");
                var batch = new Batch { Map = map };
                for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    Vector3 pos = Vertex(map, x, z, height);
                    if (!Geometry.Contains(hull, new Point(pos.x, pos.z))) continue;
                    Vector3 delta = pos - player.transform.position; delta.y = 0;
                    if (delta.magnitude > distance) throw new Exception("Move closer: part of the polygon is out of reach.");
                    if (!PrivateArea.CheckAccess(pos, 0f, false) || Location.IsInsideNoBuildLocation(pos)) throw new Exception("Polygon includes protected ground.");
                    if (++count > Geometry.MaxVertices) throw new Exception("Polygon covers too many terrain vertices.");
                    batch.Cells.Add(new Vector2Int(x, z));
                }
                if (batch.Cells.Count > 0) batches.Add(batch);
            }
            if (count == 0) throw new Exception("No loaded terrain vertices inside the polygon.");
            // Reject uncovered/unloaded boundary before creating any terrain compilers.
            foreach (Point p in hull)
                if (Heightmap.FindHeightmap(new Vector3((float)p.X, height, (float)p.Z)) == null) throw new Exception("Some polygon terrain is not loaded.");
            for (double z = Math.Ceiling(minZ); z <= maxZ; z++)
            for (double x = Math.Ceiling(minX); x <= maxX; x++)
                if (Geometry.Contains(hull, new Point(x, z)) && Heightmap.FindHeightmap(new Vector3((float)x, height, (float)z)) == null)
                    throw new Exception("Some polygon terrain is not loaded.");
            plane = fitPlane ? GroundPlane.Fit(batches.SelectMany(b => b.Cells.Select(c =>
            {
                Vector3 pos = Vertex(b.Map, c.x, c.y, 0);
                return new GroundSample(pos.x, pos.z, b.Map.GetHeight(c.x, c.y) + b.Map.transform.position.y);
            }))) : new GroundPlane(0, 0, height, 0, 0);
            GroundPlane targetPlane = plane;
            foreach (Batch batch in batches)
            {
                batch.Targets = batch.Cells.Select(c =>
                {
                    Vector3 pos = Vertex(batch.Map, c.x, c.y, 0);
                    float target = (float)targetPlane.At(pos.x, pos.z);
                    if (!Geometry.Finite(target)) throw new Exception("Fitted plane has an invalid height.");
                    return target;
                }).ToArray();
                batch.Compiler = batch.Map.GetAndCreateTerrainCompiler(); Register(batch.Compiler);
            }
            return batches;
        }

        private static Vector3 Vertex(Heightmap map, int x, int z, float height) =>
            new Vector3(map.transform.position.x + (x - map.m_width / 2) * map.m_scale, height,
                map.transform.position.z + (z - map.m_width / 2) * map.m_scale);

        internal static void Register(TerrainComp compiler)
        {
            if (compiler == null || Plugin.Instance == null) return;
            ZNetView view = compiler.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || !Views.Add(view)) return;
            view.Register<ZPackage>(RequestRpc, (sender, pkg) => Handle(compiler, view, sender, pkg));
            view.Register<ZPackage>(ReplyRpc, (sender, pkg) => Reply(view, sender, pkg));
        }

        internal static void Send(Batch batch, Player player, long id, int phase, Action<bool, string> callback)
        {
            if (batch.Compiler == null || player == null) { callback(false, "Terrain or player unloaded."); return; }
            ZNetView view = batch.Compiler.GetComponent<ZNetView>();
            if (view == null || !view.IsValid()) { callback(false, "Terrain owner is unavailable."); return; }
            string key = view.GetZDO().m_uid + ":" + id + ":" + phase;
            Callbacks[key] = callback;
            var pkg = new ZPackage();
            pkg.Write(id); pkg.Write(phase);
            if (phase == 1 || phase == 5)
            {
                pkg.Write(player.GetZDOID()); pkg.Write(batch.Targets[0]); pkg.Write(batch.Cells.Count);
                for (int i = 0; i < batch.Cells.Count; i++)
                {
                    pkg.Write(batch.Cells[i].x); pkg.Write(batch.Cells[i].y);
                    if (phase == 5) pkg.Write(batch.Targets[i]);
                }
            }
            try { view.InvokeRPC(RequestRpc, pkg); }
            catch (Exception ex) { Callbacks.Remove(key); callback(false, ex.GetBaseException().Message); }
        }

        private static void Handle(TerrainComp compiler, ZNetView view, long sender, ZPackage pkg)
        {
            if (!view.IsOwner() || Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
            long id = 0; int phase = 0;
            try
            {
                if (pkg.Size() > 30000 || pkg.Size() < 12) return;
                id = pkg.ReadLong(); phase = pkg.ReadInt();
                if (phase != 1 && phase != 5 && pkg.GetPos() != pkg.Size()) throw new Exception("Invalid terrain request.");
                string key = view.GetZDO().m_uid + ":" + sender + ":" + id;
                PruneRequests();
                if (phase == 1 || phase == 5)
                {
                    ZDOID player = pkg.ReadZDOID(); float height = pkg.ReadSingle(); int count = pkg.ReadInt();
                    if (!Geometry.Finite(height) || count < 1 || count > Geometry.MaxVertices || pkg.Size() - pkg.GetPos() != count * (phase == 5 ? 12 : 8))
                        throw new Exception("Invalid terrain request.");
                    if (Requests.Count >= 64 && !Requests.ContainsKey(key)) throw new Exception("Too many pending terrain requests.");
                    if (Requests.TryGetValue(key, out Prepared prior))
                    { Respond(view, sender, id, phase, true, "Ready"); return; }
                    var batch = new Batch { Compiler = compiler, Map = HMap.GetValue(compiler) as Heightmap, Targets = new float[count] };
                    var seen = new HashSet<Vector2Int>();
                    for (int i = 0; i < count; i++)
                    {
                        var cell = new Vector2Int(pkg.ReadInt(), pkg.ReadInt());
                        if (!seen.Add(cell)) throw new Exception("Duplicate terrain vertex.");
                        batch.Targets[i] = phase == 5 ? pkg.ReadSingle() : height;
                        if (!Geometry.Finite(batch.Targets[i])) throw new Exception("Invalid terrain height.");
                        batch.Cells.Add(cell);
                    }
                    var prepared = new Prepared { Batch = batch, Player = player, Sender = sender, Expires = Time.unscaledTime + 60f };
                    Validate(prepared, view);
                    Requests[key] = prepared;
                    Respond(view, sender, id, phase, true, "Ready");
                }
                else if (phase == 2)
                {
                    if (!Requests.TryGetValue(key, out Prepared prepared)) throw new Exception("Terrain request expired or owner changed.");
                    if (prepared.UndoRequested) throw new Exception("This operation has been canceled for undo.");
                    if (!prepared.Applied)
                    {
                        Validate(prepared, view);
                        Apply(prepared);
                        prepared.Applied = true;
                        prepared.Expires = Time.unscaledTime + 600f;
                    }
                    Respond(view, sender, id, phase, true, "Leveled");
                }
                else if (phase == 3 || phase == 4)
                {
                    if (!Requests.TryGetValue(key, out Prepared prepared)) throw new Exception("Undo history expired, terrain unloaded, or owner changed.");
                    Validate(prepared, view, true);
                    if (!prepared.Undone)
                    {
                        if (prepared.Applied && !Matches(prepared.After, prepared.Batch))
                            throw new Exception("Ground has changed since leveling; undo would overwrite another edit.");
                        if (phase == 3) prepared.UndoRequested = true;
                        else
                        {
                            if (!prepared.UndoRequested) throw new Exception("Undo was not prepared.");
                            if (prepared.Applied) Restore(prepared);
                            prepared.Undone = true;
                        }
                        prepared.Expires = Time.unscaledTime + 600f;
                    }
                    Respond(view, sender, id, phase, true, phase == 3 ? "Undo ready" : "Restored");
                }
                else throw new Exception("Unknown terrain request.");
            }
            catch (Exception ex) { Respond(view, sender, id, phase, false, ex.GetBaseException().Message); }
        }

        private static void Validate(Prepared request, ZNetView view, bool undo = false)
        {
            if (!view.IsOwner()) throw new Exception("Terrain owner changed.");
            ZDO actor = ZDOMan.instance.GetZDO(request.Player);
            Player player = Player.GetAllPlayers().FirstOrDefault(p => p.GetZDOID() == request.Player);
            if (actor == null || actor.GetOwner() != request.Sender || player == null) throw new Exception("Player is not available on the terrain owner.");
            Batch batch = request.Batch;
            Heightmap map = batch.Map;
            if (map == null || map.IsDistantLod) throw new Exception("Terrain is not loaded.");
            Load.Invoke(batch.Compiler, null);
            for (int i = 0; i < batch.Cells.Count; i++)
            {
                Vector2Int cell = batch.Cells[i];
                if (cell.x < 0 || cell.y < 0 || cell.x > map.m_width || cell.y > map.m_width) throw new Exception("Invalid terrain vertex.");
                Vector3 pos = Vertex(map, cell.x, cell.y, batch.Targets[i]);
                Vector3 delta = pos - actor.GetPosition(); delta.y = 0;
                if (delta.magnitude > Plugin.Instance.Distance || !AccessFor(player.GetPlayerID(), pos) || Location.IsInsideNoBuildLocation(pos))
                    throw new Exception("Terrain is out of reach or protected.");
            }
            if (undo) return;
            // Capture actual native heights BEFORE compiler deltas/clamping. This also handles legacy modifiers.
            _captureCompiler = batch.Compiler;
            _underlying = _limitBase = null;
            try { map.Poke(); }
            finally { _captureCompiler = null; }
            if (_underlying == null || _limitBase == null) throw new Exception("Terrain compiler changed during preparation.");
            batch.Deltas = new float[batch.Cells.Count];
            for (int i = 0; i < batch.Cells.Count; i++)
            {
                Vector2Int cell = batch.Cells[i];
                int index = cell.y * (map.m_width + 1) + cell.x;
                if (!Geometry.LevelDelta(_underlying[index] + map.transform.position.y, _limitBase[index] + map.transform.position.y,
                    batch.Targets[i], out double levelDelta)) throw new Exception("Target exceeds the game's terrain height limits. Choose a closer height or smaller polygon.");
                batch.Deltas[i] = (float)levelDelta;
            }
            _underlying = _limitBase = null;
        }

        internal static void Capture(TerrainComp compiler, List<float> heights, float[] baseHeights)
        {
            if (compiler != _captureCompiler) return;
            _underlying = heights.ToArray();
            _limitBase = (float[])baseHeights.Clone();
        }

        private static bool AccessFor(long playerId, Vector3 point)
        {
            bool blocked = false, allowed = false;
            foreach (PrivateArea area in (List<PrivateArea>)Areas.GetValue(null))
            {
                if (area == null || !(bool)AreaEnabled.Invoke(area, null) || !(bool)AreaInside.Invoke(area, new object[] { point, 0f })) continue;
                blocked = true;
                Piece piece = area.GetComponent<Piece>();
                if ((piece != null && piece.GetCreator() == playerId) || (bool)AreaPermitted.Invoke(area, new object[] { playerId })) allowed = true;
            }
            return !blocked || allowed;
        }

        private static void Apply(Prepared request)
        {
            Batch batch = request.Batch;
            var settings = new TerrainOp.Settings { m_level = true, m_levelRadius = 0f, m_square = false,
                m_levelOffset = 0f, m_raise = false, m_smooth = false, m_paintCleared = false };
            float[] levels = (float[])Levels.GetValue(batch.Compiler), smooth = (float[])Smooth.GetValue(batch.Compiler);
            bool[] modified = (bool[])Modified.GetValue(batch.Compiler);
            int[] indices = batch.Cells.Select(c => c.y * (batch.Map.m_width + 1) + c.x).ToArray();
            var before = new TerrainSnapshot(indices, levels, smooth, modified);
            object operations = Operations.GetValue(batch.Compiler), point = LastPoint.GetValue(batch.Compiler), radius = LastRadius.GetValue(batch.Compiler);
            try
            {
                for (int i = 0; i < batch.Cells.Count; i++)
                {
                    Vector2Int cell = batch.Cells[i];
                    InternalOperation.Invoke(batch.Compiler, new object[] { Vertex(batch.Map, cell.x, cell.y, batch.Targets[i]), Vector3.zero, settings });
                    // Correct hidden clamping offsets using the captured underlying terrain rather than displayed height.
                    levels[indices[i]] = batch.Deltas[i];
                    smooth[indices[i]] = 0f;
                }
                batch.Map.Poke();
                for (int i = 0; i < batch.Cells.Count; i++)
                    if (Mathf.Abs(batch.Map.GetHeight(batch.Cells[i].x, batch.Cells[i].y) + batch.Map.transform.position.y - batch.Targets[i]) > 0.02f)
                        throw new Exception("Terrain did not reach the requested plane; tile was restored.");
                var after = new TerrainSnapshot(indices, levels, smooth, modified);
                Save.Invoke(batch.Compiler, new object[] { false });
                request.Before = before;
                request.After = after;
            }
            catch
            {
                before.Restore(levels, smooth, modified);
                Operations.SetValue(batch.Compiler, operations); LastPoint.SetValue(batch.Compiler, point); LastRadius.SetValue(batch.Compiler, radius);
                batch.Map.Poke();
                throw;
            }
            RefreshGrass(batch);
        }

        private static bool Matches(TerrainSnapshot snapshot, Batch batch) => snapshot != null &&
            snapshot.Matches((float[])Levels.GetValue(batch.Compiler), (float[])Smooth.GetValue(batch.Compiler), (bool[])Modified.GetValue(batch.Compiler));

        private static void Restore(Prepared request)
        {
            Batch batch = request.Batch;
            float[] levels = (float[])Levels.GetValue(batch.Compiler), smooth = (float[])Smooth.GetValue(batch.Compiler);
            bool[] modified = (bool[])Modified.GetValue(batch.Compiler);
            object operations = Operations.GetValue(batch.Compiler), point = LastPoint.GetValue(batch.Compiler), radius = LastRadius.GetValue(batch.Compiler);
            try
            {
                request.Before.Restore(levels, smooth, modified);
                // A new operation retains the accounting for unrelated edits made in this tile.
                Operations.SetValue(batch.Compiler, (int)operations + 1);
                LastPoint.SetValue(batch.Compiler, batch.Map.transform.position);
                LastRadius.SetValue(batch.Compiler, batch.Map.m_width * batch.Map.m_scale / 2f);
                batch.Map.Poke();
                Save.Invoke(batch.Compiler, new object[] { false });
            }
            catch
            {
                request.After.Restore(levels, smooth, modified);
                Operations.SetValue(batch.Compiler, operations); LastPoint.SetValue(batch.Compiler, point); LastRadius.SetValue(batch.Compiler, radius);
                batch.Map.Poke();
                throw;
            }
            RefreshGrass(batch);
        }

        private static void RefreshGrass(Batch batch)
        {
            // Cosmetic refresh cannot turn an already saved operation into a failed acknowledgement.
            try { if (ClutterSystem.instance != null) ClutterSystem.instance.ResetGrass(batch.Map.transform.position, batch.Map.m_width * batch.Map.m_scale / 2f); }
            catch (Exception ex) { Debug.LogWarning("PolygonLeveler grass refresh: " + ex.Message); }
        }

        private static void Respond(ZNetView view, long sender, long id, int phase, bool ok, string text)
        {
            var pkg = new ZPackage(); pkg.Write(id); pkg.Write(phase); pkg.Write(ok); pkg.Write(text);
            view.InvokeRPC(sender, ReplyRpc, pkg);
        }
        private static void Reply(ZNetView view, long sender, ZPackage pkg)
        {
            if (pkg.Size() > 2048 || pkg.Size() < 13 || !view.IsValid() || sender != view.GetZDO().GetOwner()) return;
            long id = pkg.ReadLong(); int phase = pkg.ReadInt(); bool ok = pkg.ReadBool(); string text = pkg.ReadString();
            string key = view.GetZDO().m_uid + ":" + id + ":" + phase;
            if (Callbacks.TryGetValue(key, out Action<bool, string> callback)) { Callbacks.Remove(key); callback(ok, text); }
        }
        internal static void PruneRequests()
        {
            foreach (string key in Requests.Where(p => p.Value.Expires < Time.unscaledTime || p.Value.Batch.Compiler == null).Select(p => p.Key).ToArray()) Requests.Remove(key);
            Views.RemoveWhere(v => v == null);
        }
        internal static void Forget(long id)
        {
            foreach (string key in Callbacks.Keys.Where(k => k.Contains(":" + id + ":")).ToArray()) Callbacks.Remove(key);
        }
        internal static void ClearCallbacks() => Callbacks.Clear();
        internal static void UnregisterAll()
        {
            foreach (ZNetView view in Views) if (view != null) { view.Unregister(RequestRpc); view.Unregister(ReplyRpc); }
            Views.Clear(); Callbacks.Clear(); Requests.Clear();
            _captureCompiler = null; _underlying = _limitBase = null;
        }
    }
}
