using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jarldom
{
    // The mod's own prefabs: an inactive holder for templates, registration with the scene (from ZNetScene.Awake, ObjectDB.Awake and a hot
    // reload, so a saved piece or villager always finds its prefab), the hammer's pieces, and drawing helpers.
    internal static class Assets
    {
        private static GameObject _holder;
        internal static readonly List<Object> Owned = new List<Object>();
        private static readonly List<GameObject> Registered = new List<GameObject>();
        private static readonly AccessTools.FieldRef<ZNetScene, Dictionary<int, GameObject>> Named = AccessTools.FieldRefAccess<ZNetScene, Dictionary<int, GameObject>>("m_namedPrefabs");
        private static readonly AccessTools.FieldRef<ZNetScene, Dictionary<ZDO, ZNetView>> Instances_ = AccessTools.FieldRefAccess<ZNetScene, Dictionary<ZDO, ZNetView>>("m_instances");

        internal static Transform Holder
        {
            get
            {
                if (_holder == null) { _holder = new GameObject("JarldomPrefabs"); _holder.SetActive(false); Object.DontDestroyOnLoad(_holder); }
                return _holder.transform;
            }
        }
        internal static GameObject Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            GameObject found = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(name) : null;
            if (found == null && ObjectDB.instance != null) found = ObjectDB.instance.GetItemPrefab(name);
            return found;
        }

        // ---- registration ----
        internal static void RegisterScene(ZNetScene scene)
        {
            if (scene == null) return;
            try
            {
                VillagerPrefab.Ensure(scene);
                Einherjar.Ensure();
                Pieces.Ensure(scene);
                foreach (GameObject go in VillagerPrefab.All.Concat(Pieces.All).Concat(new[] { SoulStore.Prefab, Einherjar.Prefab }).Where(g => g != null)) Register(scene, go);
                Items.RegisterScene(scene);
            }
            catch (Exception e) { Plugin.Warn("Jarldom: registering prefabs failed: " + e); }
            if (ObjectDB.instance != null) RegisterDB(ObjectDB.instance);
        }
        internal static void RegisterDB(ObjectDB db)
        {
            if (db == null) return;
            try
            {
                if (ZNetScene.instance != null) { Pieces.Ensure(ZNetScene.instance); Pieces.AddToHammer(db); }
            }
            catch (Exception e) { Plugin.Warn("Jarldom: adding to the hammer failed: " + e); }
        }
        // A hot reload while in a world: the scene and ObjectDB already exist.
        internal static void RegisterLoaded() { if (ZNetScene.instance != null) RegisterScene(ZNetScene.instance); Items.RegisterLoaded(); }

        internal static void Register(ZNetScene scene, GameObject prefab)
        {
            if (scene == null || prefab == null) return;
            if (!scene.m_prefabs.Contains(prefab)) scene.m_prefabs.Add(prefab);
            Named(scene)[prefab.name.GetStableHashCode()] = prefab;
            if (!Registered.Contains(prefab)) Registered.Add(prefab);
        }
        internal static void UnregisterAll()
        {
            if (ObjectDB.instance != null) Pieces.RemoveFromHammer(ObjectDB.instance);
            Items.Unregister();
            if (ZNetScene.instance != null)
                foreach (GameObject prefab in Registered)
                {
                    if (prefab == null) continue;
                    ZNetScene.instance.m_prefabs.Remove(prefab);
                    var named = Named(ZNetScene.instance); int hash = prefab.name.GetStableHashCode();
                    if (named.TryGetValue(hash, out GameObject current) && current == prefab) named.Remove(hash); // only our own entry
                }
            Registered.Clear();
            Owned.Clear();
            Pieces.Forget(); VillagerPrefab.Forget(); Einherjar.Forget();
            if (_holder != null) Object.Destroy(_holder);
            _holder = null;
        }

        // Every loaded networked object of one prefab (for attaching after a reload: once, not on a timer).
        internal static IEnumerable<ZNetView> Instances(int prefabHash)
        {
            if (ZNetScene.instance == null) yield break;
            foreach (ZNetView v in Instances_(ZNetScene.instance).Values.ToList())
                if (v != null && v.IsValid() && v.GetZDO().GetPrefab() == prefabHash) yield return v;
        }

        // ---- looks ----
        // A prefab's meshes (most detailed LOD only) as children of a parent, without colliders or scripts.
        internal static GameObject Model(GameObject source, Transform parent, Vector3 position, Quaternion rotation, float scale = 1, Func<Renderer, bool> keep = null)
        {
            if (source == null) return null;
            var model = new GameObject(source.name + "_look");
            model.transform.SetParent(parent, false); model.transform.localPosition = position; model.transform.localRotation = rotation; model.transform.localScale = Vector3.one * scale;
            var lod0 = new HashSet<Renderer>(source.GetComponentsInChildren<LODGroup>(true).SelectMany(g => g.GetLODs().Take(1)).SelectMany(l => l.renderers).Where(r => r != null));
            var lodded = new HashSet<Renderer>(source.GetComponentsInChildren<LODGroup>(true).SelectMany(g => g.GetLODs()).SelectMany(l => l.renderers).Where(r => r != null));
            foreach (MeshRenderer r in source.GetComponentsInChildren<MeshRenderer>(true))
            {
                MeshFilter f = r.GetComponent<MeshFilter>();
                if (f == null || f.sharedMesh == null || lodded.Contains(r) && !lod0.Contains(r) || !Shown(r.transform, source.transform) || keep != null && !keep(r)) continue;
                var part = new GameObject(r.name);
                part.transform.SetParent(model.transform, false);
                part.transform.localPosition = source.transform.InverseTransformPoint(r.transform.position);
                part.transform.localRotation = Quaternion.Inverse(source.transform.rotation) * r.transform.rotation;
                part.transform.localScale = Divide(r.transform.lossyScale, source.transform.lossyScale);
                part.AddComponent<MeshFilter>().sharedMesh = f.sharedMesh;
                part.AddComponent<MeshRenderer>().sharedMaterials = r.sharedMaterials;
            }
            return model;
        }
        private static bool Shown(Transform part, Transform root)
        {
            for (Transform t = part; t != null && t != root; t = t.parent) if (!t.gameObject.activeSelf) return false;
            return true;
        }
        private static Vector3 Divide(Vector3 a, Vector3 b) => new Vector3(b.x != 0 ? a.x / b.x : a.x, b.y != 0 ? a.y / b.y : a.y, b.z != 0 ? a.z / b.z : a.z);

        internal static Bounds MeshBounds(Transform root, GameObject model)
        {
            bool any = false; Bounds b = new Bounds();
            foreach (MeshFilter f in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh == null) continue;
                Bounds m = f.sharedMesh.bounds;
                Matrix4x4 toRoot = root.worldToLocalMatrix * f.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = m.center + Vector3.Scale(m.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = toRoot.MultiplyPoint3x4(corner);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return any ? b : new Bounds(Vector3.up * 0.5f, Vector3.one);
        }

        internal static void Tint(GameObject root, Color tint, Color glow)
        {
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r.GetType().Name == "ParticleSystemRenderer") continue;
                r.sharedMaterials = r.sharedMaterials.Select(m =>
                {
                    if (m == null) return null;
                    var copy = new Material(m) { hideFlags = HideFlags.HideAndDontSave };
                    if (copy.HasProperty("_Color")) copy.color = m.color * tint;
                    if (glow.maxColorComponent > 0 && copy.HasProperty("_EmissionColor")) { copy.EnableKeyword("_EMISSION"); copy.SetColor("_EmissionColor", glow); }
                    Owned.Add(copy); return copy;
                }).ToArray();
            }
        }

        // An icon: the model drawn by a temporary camera far below the world on an unused layer.
        // An icon now, and again once the player stands in the world: drawn while a world loads, the game's shaders aren't set up yet
        // (their moss, wet and light values) and icons come out lurid green and yellow, or blank white.
        internal static void Icon(GameObject item, string name, Quaternion turn, Action<Sprite> set)
        {
            Sprite now = Icon(item, name, turn); if (now != null) set(now);
            Redraws.Add(() => { Sprite later = Icon(item, name, turn); if (later != null) set(later); });
        }
        private static readonly List<Action> Redraws = new List<Action>(); private static float _redrawAt = -1;
        internal static void Tick()
        {
            if (Redraws.Count == 0) return;
            if (Player.m_localPlayer == null || EnvMan.instance == null) { _redrawAt = -1; return; }
            if (_redrawAt < 0) { _redrawAt = Time.time + 3f; return; }
            if (Time.time < _redrawAt) return;
            Action[] all = Redraws.ToArray(); Redraws.Clear(); _redrawAt = -1;
            foreach (Action a in all) { try { a(); } catch (Exception e) { Plugin.Warn("Jarldom: an icon: " + e.Message); } }
            Plugin.Log($"Jarldom: {all.Length} icons drawn again in the world's light");
        }
        internal static bool IconLightKept = true; // (test: jarldom icon compares)
        internal static Sprite Icon(GameObject item, string name, Quaternion turn)
        {
            try
            {
                var stage = new GameObject("JarldomIconStage");
                stage.transform.position = new Vector3(0, -3000, 0);
                try
                {
                    int layer = 31; for (int l = 31; l > 8; l--) if (string.IsNullOrEmpty(LayerMask.LayerToName(l))) { layer = l; break; }
                    GameObject model = Model(item, stage.transform, Vector3.zero, turn);
                    if (model == null) return null;
                    foreach (Transform t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
                    Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
                    if (renderers.Length == 0) return null;
                    Bounds bounds = renderers[0].bounds; foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
                    var cameraObject = new GameObject("Camera"); cameraObject.transform.SetParent(stage.transform, false);
                    Camera camera = cameraObject.AddComponent<Camera>();
                    camera.enabled = false; camera.cullingMask = 1 << layer; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0, 0, 0, 0);
                    camera.fieldOfView = 20; camera.nearClipPlane = 0.05f; camera.farClipPlane = 200; camera.allowHDR = false; camera.allowMSAA = false;
                    float radius = Mathf.Max(0.05f, bounds.extents.magnitude);
                    camera.transform.position = bounds.center + new Vector3(0.1f, 0.4f, -1f).normalized * (radius / Mathf.Sin(10 * Mathf.Deg2Rad)) * 1.05f;
                    camera.transform.LookAt(bounds.center);
                    var lightObject = new GameObject("Light"); lightObject.transform.SetParent(stage.transform, false); lightObject.transform.rotation = Quaternion.Euler(40, 20, 0);
                    Light light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.5f; light.shadows = LightShadows.None; light.cullingMask = 1 << layer;
                    const int size = 256;
                    RenderTexture target = RenderTexture.GetTemporary(size, size, 24, RenderTextureFormat.ARGB32);
                    RenderTexture previous = RenderTexture.active;
                    // The scene's light kept out: fog and the world's ambient (whatever the loading screen or the hour left them at) tint an
                    // icon wildly otherwise; a flat grey ambient, as Claude Tools' renders use.
                    bool fog = RenderSettings.fog; Color ambient = RenderSettings.ambientLight; var ambientMode = RenderSettings.ambientMode; float reflect = RenderSettings.reflectionIntensity;
                    try
                    {
                        if (IconLightKept) { RenderSettings.fog = false; RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.6f, 0.61f, 0.64f); RenderSettings.reflectionIntensity = 0.3f; }
                        camera.targetTexture = target; camera.Render();
                        RenderTexture.active = target;
                        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                        texture.ReadPixels(new Rect(0, 0, size, size), 0, 0); texture.Apply();
                        texture.hideFlags = HideFlags.HideAndDontSave; Owned.Add(texture);
                        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
                        sprite.name = name; sprite.hideFlags = HideFlags.HideAndDontSave; Owned.Add(sprite);
                        return sprite;
                    }
                    finally
                    {
                        RenderSettings.fog = fog; RenderSettings.ambientMode = ambientMode; RenderSettings.ambientLight = ambient; RenderSettings.reflectionIntensity = reflect;
                        RenderTexture.active = previous; camera.targetTexture = null; RenderTexture.ReleaseTemporary(target);
                    }
                }
                finally { Object.DestroyImmediate(stage); }
            }
            catch (Exception e) { Plugin.Warn("Jarldom: could not draw an icon for " + name + ": " + e.Message); return null; }
        }

        internal static Piece.Requirement[] Needs(params (string item, int amount)[] needs) => needs.Select(n => new Piece.Requirement
        {
            m_resItem = Find(n.item)?.GetComponent<ItemDrop>(), m_amount = n.amount, m_recover = true,
        }).Where(r => r.m_resItem != null).ToArray();

        internal static void Effect(string prefab, Vector3 at, Quaternion? rotation = null)
        {
            GameObject found = Find(prefab);
            if (found != null) Object.Instantiate(found, at, rotation ?? Quaternion.identity);
        }
    }
}
