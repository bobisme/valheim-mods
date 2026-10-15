using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BuildKit
{
    // A piece's build-menu icon, drawn from its own model out of sight (as Demolisher draws its stake's).
    internal static class Icons
    {
        public static Sprite Render(GameObject prefab)
        {
            var stage = new GameObject("BuildKit icon stage"); stage.transform.position = new Vector3(0, -3000, 0);
            try
            {
                // As the game's own placement ghost: no network object, nothing in the world.
                GameObject copy; ZNetView.m_forceDisableInit = true;
                try { copy = Object.Instantiate(prefab, stage.transform); } finally { ZNetView.m_forceDisableInit = false; }
                copy.transform.localPosition = Vector3.zero; copy.transform.localRotation = Quaternion.Euler(0, 200, 0);
                foreach (var behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
                copy.SetActive(true);
                int layer = 31; for (int l = 31; l > 8; l--) if (string.IsNullOrEmpty(LayerMask.LayerToName(l))) { layer = l; break; }
                foreach (Transform t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
                Renderer[] renderers = copy.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
                if (renderers.Length == 0) return null;
                Bounds bounds = renderers[0].bounds; foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
                var camera = new GameObject("BuildKit icon camera").AddComponent<Camera>(); camera.transform.SetParent(stage.transform, false);
                camera.enabled = false; camera.cullingMask = 1 << layer; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0, 0, 0, 0);
                camera.fieldOfView = 20; camera.nearClipPlane = 0.05f; camera.farClipPlane = 60; camera.allowHDR = false; camera.allowMSAA = false;
                camera.transform.position = bounds.center + new Vector3(0.35f, 0.45f, -1f).normalized * (bounds.extents.magnitude / Mathf.Sin(10 * Mathf.Deg2Rad)) * 0.72f; // (closer than the bounding sphere: a flat piece fills the frame)
                camera.transform.LookAt(bounds.center);
                var light = new GameObject("BuildKit icon light").AddComponent<Light>(); light.transform.SetParent(stage.transform, false);
                light.transform.rotation = Quaternion.Euler(50, 30, 0); light.type = LightType.Directional; light.intensity = 1.4f; light.shadows = LightShadows.None; light.cullingMask = 1 << layer;
                RenderTexture target = RenderTexture.GetTemporary(128, 128, 24, RenderTextureFormat.ARGB32), previous = RenderTexture.active;
                try
                {
                    camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                    var texture = new Texture2D(128, 128, TextureFormat.RGBA32, false); texture.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); texture.Apply();
                    texture.hideFlags = HideFlags.HideAndDontSave;
                    var sprite = Sprite.Create(texture, new Rect(0, 0, 128, 128), new Vector2(0.5f, 0.5f), 100); sprite.name = prefab.name; sprite.hideFlags = HideFlags.HideAndDontSave;
                    return sprite;
                }
                finally { RenderTexture.active = previous; camera.targetTexture = null; RenderTexture.ReleaseTemporary(target); }
            }
            finally { Object.DestroyImmediate(stage); }
        }

        // Pieces set out together far below the world, as ghosts (nothing saved, nothing in anyone's world), drawn from a yaw and pitch
        // round their middle, to a PNG: a roof's corners judged as they'd be built.
        public static string Stage(System.Collections.Generic.List<(GameObject prefab, Vector3 at, float yaw)> parts, float yaw, float pitch, float dist, string path, int w = 1200, int h = 800)
        {
            var stage = new GameObject("BuildKit stage"); stage.transform.position = new Vector3(0, -3000, 0);
            try
            {
                int layer = 31; for (int l = 31; l > 8; l--) if (string.IsNullOrEmpty(LayerMask.LayerToName(l))) { layer = l; break; }
                foreach (var (prefab, at, turn) in parts)
                {
                    GameObject copy; ZNetView.m_forceDisableInit = true;
                    try { copy = Object.Instantiate(prefab, stage.transform); } finally { ZNetView.m_forceDisableInit = false; }
                    copy.transform.localPosition = at; copy.transform.localRotation = Quaternion.Euler(0, turn, 0);
                    foreach (var behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
                    copy.SetActive(true);
                    foreach (Transform t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
                }
                Renderer[] renderers = stage.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
                if (renderers.Length == 0) return null;
                Bounds bounds = renderers[0].bounds; foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
                var camera = new GameObject("BuildKit stage camera").AddComponent<Camera>(); camera.transform.SetParent(stage.transform, false);
                camera.enabled = false; camera.cullingMask = 1 << layer; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.55f, 0.66f, 0.78f, 1);
                camera.fieldOfView = 40; camera.nearClipPlane = 0.05f; camera.farClipPlane = 200;
                camera.transform.position = bounds.center + Quaternion.Euler(pitch, yaw, 0) * Vector3.back * dist; camera.transform.LookAt(bounds.center);
                var light = new GameObject("BuildKit stage light").AddComponent<Light>(); light.transform.SetParent(stage.transform, false);
                light.transform.rotation = Quaternion.Euler(45, yaw + 40, 0); light.type = LightType.Directional; light.intensity = 1.3f; light.shadows = LightShadows.Soft; light.cullingMask = 1 << layer;
                RenderTexture target = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32), previous = RenderTexture.active;
                try
                {
                    camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                    var texture = new Texture2D(w, h, TextureFormat.RGBA32, false); texture.ReadPixels(new Rect(0, 0, w, h), 0, 0); texture.Apply();
                    System.IO.File.WriteAllBytes(path, texture.EncodeToPNG()); Object.Destroy(texture);
                    return path;
                }
                finally { RenderTexture.active = previous; camera.targetTexture = null; RenderTexture.ReleaseTemporary(target); }
            }
            finally { Object.DestroyImmediate(stage); }
        }
    }
}
