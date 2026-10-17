using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Bathhouse
{
    // The bathhouse's pieces, made from the game's own: the fire pit for the stove (its fire, fuel and warmth, the flames moved into the
    // stove's firebox and no smoke: the stones keep the heat), a plank wall for the bench, the bucket and the plunge tub (something that
    // stands and wears), each with its own look made in Blender (Models/bath.py) and colliders of its own shape. The hut round them is
    // the player's own building.
    internal static class Kit
    {
        public const string Stove = "Bathhouse_Stove", Bench = "Bathhouse_Bench", Bucket = "Bathhouse_Bucket", Plunge = "Bathhouse_PlungeTub", Locker = "Bathhouse_Locker";
        public const string HotTub = "piece_bathtub"; // (the game's own: it counts as a sauna's heat and seats)
        internal sealed class Def
        {
            public string Name, From, Title, Model, Description; public (string item, int n)[] Cost;
        }
        internal static readonly Def[] All =
        {
            new Def { Name = Stove, From = "fire_pit", Title = "Sauna stove", Model = "stove", Cost = new[] { ("Stone", 20), ("Wood", 4) },
                Description = "A kiuas: a firebox of mortared field stones under a heap of rounded rocks to hold the heat. Light it in a small room of your own, roofed and walled, with a sauna bench, and it's a sauna: sit in the heat a minute and you're Bathed. It makes no smoke indoors." },
            new Def { Name = Bench, From = "woodwall", Title = "Sauna bench", Model = "bench", Cost = new[] { ("Wood", 12) },
                Description = "Two tiers of planks: sit on the high one, feet on the low one, where the heat gathers. Seats three. Put it in a sauna with its back to a wall." },
            new Def { Name = Bucket, From = "woodwall", Title = "Water bucket", Model = "bucket", Cost = new[] { ("Wood", 3), ("Resin", 2) },
                Description = "A bucket and a long ladle. Set it by the sauna stove and throw water on the hot stones (E): a cloud of steam, and the heat works twice as fast for a while." },
            new Def { Name = Plunge, From = "woodwall", Title = "Plunge tub", Model = "plunge", Cost = new[] { ("Wood", 20), ("Resin", 4) },
                Description = "A staved tub of cold water for a bathhouse far from the sea or a lake. Straight from the sauna's heat, sit in it (E): Invigorated." },
            new Def { Name = Locker, From = "piece_chest_wood", Title = "Bath locker", Model = "locker", Cost = new[] { ("Wood", 10), ("LeatherScraps", 2) },
                Description = "A clothes chest with pegs over it, for the changing room: a sauna's sat in, and the plunge taken, stripped down. E puts away all you wear; E again and you're dressed. Shift+E opens it." },
        };
        internal static bool Ours(string name) => All.Any(d => d.Name == name);

        // Where a bather sits on each, in the piece's own frame (the seat's top, and where the body's root goes: a seat's height under it).
        internal static readonly Vector3[] BenchSeats = { new Vector3(-0.6f, 0.85f, -0.3f), new Vector3(0, 0.85f, -0.3f), new Vector3(0.6f, 0.85f, -0.3f) };
        internal const float SeatDrop = 0.45f;
        internal static readonly Vector3 TubSeat = new Vector3(0, 0.30f, 0);
        internal static readonly Vector3 Firebox = new Vector3(0, 0.15f, 0.12f), StonesTop = new Vector3(0, 1.05f, 0);

        private static readonly MethodInfo UpdateKnownRecipes = AccessTools.Method(typeof(Player), "UpdateKnownRecipesList"), UpdateAvailablePieces = AccessTools.Method(typeof(Player), "UpdateAvailablePiecesList");
        internal static readonly Dictionary<string, GameObject> Made = new Dictionary<string, GameObject>();
        private static readonly List<Object> Owned = new List<Object>();
        private static GameObject _holder; private static ZNetScene _scene; private static float _next, _settleAt = -1; private static bool _broken;
        internal static PieceTable Hammer() => ObjectDB.instance?.GetItemPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;

        // Once a second: made for this world, and in the hammer's menu (through world loads and reloads of this mod).
        internal static void Ensure()
        {
            if (_broken || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1;
            ZNetScene scene = ZNetScene.instance; PieceTable table = Hammer();
            if (scene == null || table == null || Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
            if (scene != _scene) { Forget(); _scene = scene; }
            table.m_pieces.RemoveAll(p => p == null);
            bool changed = false, every = true;
            foreach (Def d in All)
            {
                if (!Made.TryGetValue(d.Name, out GameObject prefab) || prefab == null)
                {
                    prefab = Make(scene, d);
                    if (prefab == null) { every = false; continue; }
                    Made[d.Name] = prefab;
                }
                if (table.m_pieces.Contains(prefab) && scene.GetPrefab(d.Name) == prefab) continue;
                PieceReload.ReplacePrefab(scene, prefab, table);
                PieceReload.Reattach(scene, prefab);
                changed = true;
            }
            if (changed && Player.m_localPlayer is Player p) { UpdateKnownRecipes?.Invoke(p, null); UpdateAvailablePieces?.Invoke(p, null); }
            if (every) { if (_settleAt < 0) _settleAt = Time.unscaledTime + 3; else if (Time.unscaledTime >= _settleAt) Leftovers.TakeOver(Owned); }
        }
        internal static void Broken() => _broken = true;
        internal static void Unload()
        {
            PieceTable table = Hammer();
            foreach (GameObject prefab in Made.Values) PieceReload.UnloadPiece(prefab, table);
            // (the meshes and materials stay: what's standing still draws with them till the next copy puts its own in their place)
            Leftovers.HandOver(Owned.Concat(new Object[] { _holder }));
        }
        private static void Forget() { Made.Clear(); foreach (Object o in Owned) if (o != null) Object.Destroy(o); Owned.Clear(); _mats = null; }

        private static GameObject Make(ZNetScene scene, Def d)
        {
            GameObject from = scene.GetPrefab(d.From);
            if (from == null) { Plugin.Warn($"No {d.From} to make the {d.Title} from."); return null; }
            Material[] mats = Materials(scene);
            if (mats == null) return null;
            Mesh mesh = Model(d.Model);
            if (mesh == null) return null;
            if (_holder == null) { _holder = new GameObject("Bathhouse prefabs"); _holder.SetActive(false); Object.DontDestroyOnLoad(_holder); }
            GameObject go = Object.Instantiate(from, _holder.transform, false); go.name = d.Name;
            Owned.Add(go);
            Fireplace fire = go.GetComponent<Fireplace>();
            // The fire's own parts (its flames, light, sound, warmth) are kept for the stove; everything else of the game's look goes (its
            // LODs, its snow, its broken bits), and its colliders and snap points; the kit's in their place.
            var keep = new HashSet<Transform>();
            if (fire != null) foreach (GameObject o in new[] { fire.m_enabledObject, fire.m_enabledObjectLow, fire.m_enabledObjectHigh })
                    if (o != null) foreach (Transform t in o.GetComponentsInChildren<Transform>(true)) keep.Add(t);
            WearNTear wear = go.GetComponent<WearNTear>();
            foreach (LODGroup lod in go.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(lod);
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true).Where(r => !keep.Contains(r.transform) && !(r is ParticleSystemRenderer)).ToList()) { GameObject o = r.gameObject; Object.DestroyImmediate(r); if (o.GetComponent<MeshFilter>() is MeshFilter f) Object.DestroyImmediate(f); }
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true).Where(c => !c.isTrigger && !keep.Contains(c.transform)).ToList()) Object.DestroyImmediate(c);
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true).Where(t => t != go.transform && (t.name.StartsWith("_snappoint") || t.name.StartsWith("$hud_snappoint") || t.CompareTag("snappoint"))).ToList()) Object.DestroyImmediate(t.gameObject);
            var look = new GameObject("Model"); look.transform.SetParent(go.transform, false);
            look.AddComponent<MeshFilter>().sharedMesh = mesh;
            look.AddComponent<MeshRenderer>().sharedMaterials = mats;
            if (wear != null) { wear.m_new = look; wear.m_worn = look; wear.m_broken = look; wear.m_fragmentRoots = new GameObject[0]; wear.m_noRoofWear = true; wear.m_noSupportWear = true; wear.m_materialType = d.Name == Stove ? WearNTear.MaterialType.Stone : WearNTear.MaterialType.Wood; }
            int pieceLayer = LayerMask.NameToLayer("piece");
            switch (d.Name)
            {
                case Stove:
                    // (its body and the heap of stones; the hearth stone in front)
                    Box(go, pieceLayer, new Vector3(0, 0.52f, 0), new Vector3(0.9f, 1.04f, 0.8f), Quaternion.identity);
                    Box(go, pieceLayer, new Vector3(0, 0.02f, 0.5f), new Vector3(0.9f, 0.04f, 0.2f), Quaternion.identity);
                    MakeStove(go, fire);
                    // (stone stands on stone or the ground in the game, never on planks; a stove stands on a sauna's plank floor)
                    if (wear != null) wear.m_noSupportWear = false;
                    break;
                case Bench:
                    // (the high tier, the low tier, and a seat on the high one for each of three)
                    Box(go, pieceLayer, new Vector3(0, 0.43f, -0.3f), new Vector3(2.0f, 0.86f, 0.5f), Quaternion.identity);
                    Box(go, pieceLayer, new Vector3(0, 0.21f, 0.2f), new Vector3(2.0f, 0.42f, 0.5f), Quaternion.identity);
                    foreach (Vector3 s in BenchSeats) Seat(go, pieceLayer, s, s.y - SeatDrop, "Sauna bench");
                    break;
                case Bucket:
                    Post(go, pieceLayer, new Vector3(0, 0.14f, 0), 0.3f, 0.15f); // (a centimetre into what it stands on: a bare touch is no support)
                    go.AddComponent<BucketUse>();
                    break;
                case Plunge:
                    // (its staves in a ring of boxes, its floor, the step; a seat inside, low in the water)
                    for (int i = 0; i < 12; i++)
                    {
                        float a = i * 30f;
                        Vector3 at = Quaternion.Euler(0, a, 0) * new Vector3(0, 0.475f, 0.725f);
                        Box(go, pieceLayer, at, new Vector3(0.4f, 0.95f, 0.06f), Quaternion.Euler(0, a, 0));
                    }
                    Box(go, pieceLayer, new Vector3(0, 0.03f, 0), new Vector3(1.4f, 0.06f, 1.4f), Quaternion.identity);
                    Box(go, pieceLayer, new Vector3(0, 0.2f, 0.92f), new Vector3(0.6f, 0.4f, 0.3f), Quaternion.identity);
                    Seat(go, pieceLayer, TubSeat + Vector3.up * 0.5f, TubSeat.y - 0.25f, "Plunge tub", solid: false);
                    Box(go, pieceLayer, new Vector3(0, 0.22f, 0.5f), new Vector3(0.5f, 0.44f, 0.24f), Quaternion.identity); // (a step inside, under the water, to climb out by)
                    Water(go, scene);
                    if (wear != null) wear.m_noRoofWear = false; // (it stands outside, full of water: the rain doesn't rot it)
                    break;
                case Locker:
                    // (the chest, the back board with its pegs)
                    Box(go, pieceLayer, new Vector3(0, 0.25f, 0), new Vector3(1.1f, 0.5f, 0.55f), Quaternion.identity);
                    Box(go, pieceLayer, new Vector3(0, 0.8f, -0.25f), new Vector3(1.1f, 1.6f, 0.06f), Quaternion.identity);
                    if (go.GetComponent<Container>() is Container box) { box.m_name = "Bath locker"; box.m_width = 4; box.m_height = 3; box.m_privacy = Container.PrivacySetting.Public; }
                    break;
            }
            var piece = go.GetComponent<Piece>();
            piece.m_name = d.Title; piece.m_description = d.Description;
            piece.m_category = Piece.PieceCategory.Furniture;
            piece.m_craftingStation = scene.GetPrefab("piece_workbench")?.GetComponent<CraftingStation>();
            piece.m_groundPiece = false; piece.m_groundOnly = false; piece.m_clipEverything = false; piece.m_comfort = 0;
            piece.m_resources = d.Cost.Select(c => Req(c.item, c.n)).Where(r => r != null).ToArray();
            piece.m_enabled = true;
            try { if (Icons.Make(go, d.Name) is Sprite icon) { piece.m_icon = icon; Owned.Add(icon); Owned.Add(icon.texture); } }
            catch (Exception e) { Plugin.Warn($"No icon for the {d.Title}: {e.Message}"); }
            return go;
        }

        // The stove: the fire pit's flames, light and sound moved into the firebox and made small, its warmth filling a small room, no
        // smoke and no burning; steam over the stones, and the hiss, for the bucket.
        private static void MakeStove(GameObject go, Fireplace fire)
        {
            if (fire == null) return;
            fire.m_name = "Sauna stove";
            var box = new GameObject("Firebox"); box.transform.SetParent(go.transform, false);
            box.transform.localPosition = Firebox; box.transform.localScale = Vector3.one * 0.38f;
            foreach (GameObject o in new[] { fire.m_enabledObject, fire.m_enabledObjectLow, fire.m_enabledObjectHigh }.Where(o => o != null).Distinct())
                o.transform.SetParent(box.transform, false);
            foreach (ParticleSystem flame in box.GetComponentsInChildren<ParticleSystem>(true)) { var fm = flame.main; fm.scalingMode = ParticleSystemScalingMode.Hierarchy; }
            foreach (SmokeSpawner s in go.GetComponentsInChildren<SmokeSpawner>(true)) Object.DestroyImmediate(s.gameObject);
            fire.m_smokeSpawner = null;
            foreach (Aoe a in go.GetComponentsInChildren<Aoe>(true)) Object.DestroyImmediate(a.gameObject); // (no burning: the fire's shut in)
            foreach (Transform t in box.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Pathfinding_blocker")).ToList()) Object.DestroyImmediate(t.gameObject);
            foreach (EffectArea area in box.GetComponentsInChildren<EffectArea>(true).Where(a => a.m_type.HasFlag(EffectArea.Type.Heat)).ToList())
            {
                // (the warmth, still the lit fire's own: back to full size, at the stones, as far as a small room reaches)
                area.transform.localScale = Vector3.one / box.transform.localScale.x;
                area.transform.position = go.transform.TransformPoint(StonesTop);
                if (area.GetComponent<SphereCollider>() is SphereCollider sc) sc.radius = Sauna.Reach;
            }
            fire.m_checkTerrainOffset = 0.2f;
            fire.m_coverCheckOffset = 0.5f;
            // (the light through the firebox's mouth: dimmer than an open fire's, a glow on the floor)
            foreach (Light l in box.GetComponentsInChildren<Light>(true)) { l.intensity *= 0.4f; l.range *= 0.7f; l.shadows = LightShadows.None; }
            // Steam: a burst of soft white puffs off the stones when water is thrown, rising to the roof and spreading (the fire's own
            // smoke puff for its look, made white).
            var steam = new GameObject("Steam"); steam.transform.SetParent(go.transform, false); steam.transform.localPosition = StonesTop;
            // (the fire glow's material, laid over what's behind it, with a soft round puff for its picture)
            Material glow = ZNetScene.instance.GetPrefab(HotTub)?.transform.Find("_Enabled/fire/Particles/flare")?.GetComponent<ParticleSystemRenderer>()?.sharedMaterial;
            var puff = new GameObject("Puffs"); puff.transform.SetParent(steam.transform, false);
            // (on the game's effects layer, as its own smoke is: the cameras leave some layers out, and pieces' get shadows and the like)
            int fx = LayerMask.NameToLayer("effect"); if (fx >= 0) { puff.layer = fx; steam.layer = fx; }
            var ps = puff.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.playOnAwake = false; main.loop = false; main.duration = 1f; main.maxParticles = 150;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f); main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.6f); main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.startColor = new Color(0.62f, 0.62f, 0.6f, 0.3f); main.gravityModifier = -0.015f; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission; emission.rateOverTime = 0;
            var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 55; shape.radius = 0.4f; shape.rotation = new Vector3(-90, 0, 0);
            var grow = ps.sizeOverLifetime; grow.enabled = true; grow.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.7f), new Keyframe(1, 3.2f)));
            var fade = ps.colorOverLifetime; fade.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.08f), new GradientAlphaKey(0.7f, 0.45f), new GradientAlphaKey(0, 1) });
            fade.color = g;
            var slow = ps.limitVelocityOverLifetime; slow.enabled = true; slow.limit = 0.35f; slow.dampen = 0.12f;
            // (wisps: the cloud curls and drifts apart as it spreads under the roof)
            var curl = ps.noise; curl.enabled = true; curl.strength = 0.35f; curl.frequency = 0.7f; curl.scrollSpeed = 0.3f; curl.damping = true;
            // (stopped by the roof and walls, so it gathers and spreads under the ceiling instead of rising through it)
            var hit = ps.collision; hit.enabled = true; hit.type = ParticleSystemCollisionType.World; hit.mode = ParticleSystemCollisionMode.Collision3D;
            hit.quality = ParticleSystemCollisionQuality.Medium; hit.collidesWith = LayerMask.GetMask("piece", "Default", "static_solid", "terrain");
            hit.bounce = 0; hit.dampen = 0.6f; hit.lifetimeLoss = 0; hit.radiusScale = 0.35f; hit.enableDynamicColliders = false;
            var r = puff.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            if (glow != null)
            {
                var m = new Material(glow) { name = "Bathhouse steam", mainTexture = Puff() };
                if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
                r.sharedMaterial = m; Owned.Add(m);
            }
            go.AddComponent<StoveSteam>();
        }
        // A soft round puff: white, clear at its edge.
        private static Texture2D _puff;
        private static Texture2D Puff()
        {
            if (_puff != null) return _puff;
            const int n = 64; _puff = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "Bathhouse puff", wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2 - 1, dy = (y + 0.5f) / n * 2 - 1, d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1 - d); a = a * a * (3 - 2 * a);
                    px[y * n + x] = new Color(1, 1, 1, a);
                }
            _puff.SetPixels(px); _puff.Apply(); Owned.Add(_puff);
            return _puff;
        }
        // The game's hot-tub water for the plunge tub's surface (its look only: a tub isn't swum in).
        private static void Water(GameObject go, ZNetScene scene)
        {
            GameObject surface = scene.GetPrefab(HotTub)?.transform.Find("water/WaterSurface")?.gameObject;
            if (surface == null) return;
            GameObject w = Object.Instantiate(surface, go.transform, false); w.name = "Water";
            foreach (Collider c in w.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            w.transform.localPosition = new Vector3(0, 0.80f, 0); w.transform.localScale = new Vector3(0.68f, 1f, 0.68f); // (its plane is 2 m across)
        }

        private static Piece.Requirement Req(string item, int n)
        {
            ItemDrop drop = ObjectDB.instance?.GetItemPrefab(item)?.GetComponent<ItemDrop>();
            return drop == null ? null : new Piece.Requirement { m_resItem = drop, m_amount = n, m_recover = true };
        }
        private static void Box(GameObject go, int layer, Vector3 at, Vector3 size, Quaternion turn)
        {
            var o = new GameObject("Collider"); o.layer = layer; o.transform.SetParent(go.transform, false);
            o.transform.localPosition = at; o.transform.localRotation = turn;
            o.AddComponent<BoxCollider>().size = size;
        }
        private static void Post(GameObject go, int layer, Vector3 at, float height, float radius)
        {
            var o = new GameObject("Post"); o.layer = layer; o.transform.SetParent(go.transform, false); o.transform.localPosition = at;
            var c = o.AddComponent<CapsuleCollider>(); c.direction = 1; c.height = height; c.radius = radius;
        }
        // A seat (the game's own chair: E to sit, as on a bench): a thin box just over the seat, so the look finds it before the plank,
        // and where the body sits.
        private static void Seat(GameObject go, int layer, Vector3 top, float root, string name, bool solid = true)
        {
            var o = new GameObject("SitPoint"); o.layer = layer; o.transform.SetParent(go.transform, false);
            o.transform.localPosition = top + Vector3.up * 0.03f;
            var box = o.AddComponent<BoxCollider>(); box.size = new Vector3(0.5f, 0.08f, 0.42f);
            box.isTrigger = !solid; // (in the water of a tub: looked at for E, never stood on)
            var attach = new GameObject("attachpoint"); attach.transform.SetParent(o.transform, false);
            attach.transform.localPosition = new Vector3(0, root - (top.y + 0.03f), 0);
            Chair chair = o.AddComponent<Chair>();
            chair.m_name = name; chair.m_attachPoint = attach.transform; chair.m_useDistance = 2f; chair.m_attachAnimation = "attach_chair";
            chair.m_detachOffset = new Vector3(0, 0.5f, 0.6f);
        }

        // ---- the looks: the models, and the game's materials they're drawn in ----
        private static readonly Dictionary<string, Mesh> Meshes = new Dictionary<string, Mesh>();
        private static Mesh Model(string name)
        {
            if (Meshes.TryGetValue(name, out Mesh had) && had != null) return had;
            try
            {
                using (var raw = typeof(Plugin).Assembly.GetManifestResourceStream("Bathhouse." + name + ".json.gz"))
                {
                    if (raw == null) { Plugin.Warn("No model " + name); return null; }
                    using (var gz = new System.IO.Compression.GZipStream(raw, System.IO.Compression.CompressionMode.Decompress))
                    using (var text = new System.IO.StreamReader(gz))
                    {
                        var j = Newtonsoft.Json.Linq.JObject.Parse(text.ReadToEnd());
                        float[] v = j["v"].ToObject<float[]>(), n = j["n"].ToObject<float[]>(), uv = j["uv"].ToObject<float[]>();
                        int[][] t = j["t"].ToObject<int[][]>();
                        var verts = new Vector3[v.Length / 3]; var norms = new Vector3[verts.Length]; var uvs = new Vector2[verts.Length];
                        for (int i = 0; i < verts.Length; i++) { verts[i] = new Vector3(v[3 * i], v[3 * i + 1], v[3 * i + 2]); norms[i] = new Vector3(n[3 * i], n[3 * i + 1], n[3 * i + 2]); uvs[i] = new Vector2(uv[2 * i], uv[2 * i + 1]); }
                        var mesh = new Mesh { name = "Bathhouse " + name };
                        if (verts.Length > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                        mesh.vertices = verts; mesh.normals = norms; mesh.uv = uvs; mesh.colors = Enumerable.Repeat(Color.white, verts.Length).ToArray(); // (the piece shader reads the vertex colour)
                        mesh.subMeshCount = t.Length;
                        for (int k = 0; k < t.Length; k++) mesh.SetTriangles(t[k], k);
                        mesh.RecalculateBounds(); mesh.RecalculateTangents();
                        Owned.Add(mesh); Meshes[name] = mesh;
                        return mesh;
                    }
                }
            }
            catch (Exception e) { Plugin.Warn($"The {name} model: {e.Message}"); return null; }
        }
        // (as the models number them: wood, stone, soot, iron, water, ember, rope)
        private static Material[] _mats;
        private static Material[] Materials(ZNetScene scene)
        {
            if (_mats != null && _mats.All(m => m != null)) return _mats;
            Material wood = PieceMat(scene, "wood_pole");
            if (wood == null) { Plugin.Warn("No wood_pole material to draw the kit in."); return null; }
            Material stone = PieceMat(scene, "stone_wall_1x1");
            _mats = new[]
            {
                Grain(wood, new Color(0.78f, 0.7f, 0.6f), 0.6f, "wood"),
                Stone(wood, stone, new Color(0.82f, 0.8f, 0.77f), "stone"),
                Stone(wood, stone, new Color(0.28f, 0.26f, 0.25f), "soot"),
                Plain(wood, new Color(0.17f, 0.17f, 0.18f), "iron", gloss: 0.35f),
                Plain(wood, new Color(0.13f, 0.2f, 0.22f), "water", gloss: 0.85f),
                Glow(wood, new Color(1f, 0.38f, 0.08f), "ember"),
                Plain(wood, new Color(0.55f, 0.48f, 0.35f), "rope"),
            };
            return _mats;
        }
        // (a piece's own material: the one in the game's piece shader, not its snow cap)
        private static Material PieceMat(ZNetScene scene, string prefab) => scene.GetPrefab(prefab)?.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).FirstOrDefault(m => m != null && m.shader != null && m.shader.name == "Custom/Piece");
        private static Material Grain(Material basis, Color tint, float scale, string name)
        {
            var m = new Material(basis) { name = "Bathhouse " + name, color = tint }; m.mainTextureScale = Vector2.one * scale;
            Owned.Add(m); return m;
        }
        // Stone: the plank material (no shine of its own) with a rock's picture on it.
        private static Material Stone(Material wood, Material stone, Color tint, string name)
        {
            Material m = Plain(wood, tint, name);
            // (a rock's own picture and bumps, in the pieces' shader: the rock shader lays moss on every upward face)
            Material rock = ZNetScene.instance.GetPrefab("rock4_forest")?.GetComponentsInChildren<MeshRenderer>(true).Select(x => x.sharedMaterial).FirstOrDefault(x => x != null);
            if (rock?.mainTexture != null)
            {
                m.mainTexture = rock.mainTexture; m.mainTextureScale = Vector2.one * 0.6f;
                if (m.HasProperty("_BumpMap") && rock.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", rock.GetTexture("_BumpMap")); m.SetTextureScale("_BumpMap", Vector2.one * 0.6f); }
            }
            return m;
        }
        // The game's material in a plain colour (stone, iron, water, rope), matte unless given a gloss.
        private static Material Plain(Material basis, Color color, string name, float gloss = 0)
        {
            var m = new Material(basis) { name = "Bathhouse " + name, color = color };
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", null);
            if (m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", null);
            foreach (string k in new[] { "_Glossiness", "_Smoothness" }) if (m.HasProperty(k)) m.SetFloat(k, gloss);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0);
            Owned.Add(m); return m;
        }
        // Coals: lit from within.
        private static Material Glow(Material basis, Color color, string name)
        {
            Material m = Plain(basis, color, name);
            if (m.HasProperty("_EmissionColor")) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", color * 1.6f); }
            return m;
        }
    }
}
