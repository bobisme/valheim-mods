using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Camping
{
    // A piece's icon for the build menu: a picture of it, made once, far below the world (no network object, nothing left behind).
    internal static class Icons
    {
        internal static Sprite Make(GameObject prefab, string name)
        {
            var stage = new GameObject("Camping icon stage"); stage.transform.position = new Vector3(0, -3000, 0);
            stage.SetActive(false); // (nothing of the copy wakes: no wear and tear registering itself, no network object)
            try
            {
                GameObject copy; ZNetView.m_forceDisableInit = true;
                try { copy = Object.Instantiate(prefab, stage.transform); } finally { ZNetView.m_forceDisableInit = false; }
                copy.transform.localPosition = Vector3.zero; copy.transform.localRotation = Quaternion.Euler(0, 150, 0);
                foreach (var b in copy.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(b);
                copy.SetActive(true); stage.SetActive(true);
                int layer = 31; for (int l = 31; l > 8; l--) if (string.IsNullOrEmpty(LayerMask.LayerToName(l))) { layer = l; break; }
                foreach (Transform t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
                Renderer[] rs = copy.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
                if (rs.Length == 0) return null;
                Bounds bounds = rs[0].bounds; foreach (Renderer r in rs) bounds.Encapsulate(r.bounds);
                var camera = new GameObject("Camping icon camera").AddComponent<Camera>(); camera.transform.SetParent(stage.transform, false);
                camera.enabled = false; camera.cullingMask = 1 << layer; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0, 0, 0, 0);
                camera.fieldOfView = 20; camera.nearClipPlane = 0.05f; camera.farClipPlane = 80; camera.allowHDR = false; camera.allowMSAA = false;
                camera.transform.position = bounds.center + new Vector3(0.3f, 0.45f, -1f).normalized * (bounds.extents.magnitude / Mathf.Sin(10 * Mathf.Deg2Rad)) * 1.02f;
                camera.transform.LookAt(bounds.center);
                var light = new GameObject("Camping icon light").AddComponent<Light>(); light.transform.SetParent(stage.transform, false);
                light.transform.rotation = Quaternion.Euler(45, 30, 0); light.type = LightType.Directional; light.intensity = 1.3f; light.shadows = LightShadows.None; light.cullingMask = 1 << layer;
                RenderTexture target = RenderTexture.GetTemporary(128, 128, 24, RenderTextureFormat.ARGB32), previous = RenderTexture.active;
                try
                {
                    camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                    var tex = new Texture2D(128, 128, TextureFormat.RGBA32, false) { name = "Camping icon " + name }; tex.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); tex.Apply();
                    var sprite = Sprite.Create(tex, new Rect(0, 0, 128, 128), new Vector2(0.5f, 0.5f), 100); sprite.name = name;
                    return sprite;
                }
                finally { RenderTexture.active = previous; camera.targetTexture = null; RenderTexture.ReleaseTemporary(target); }
            }
            finally { Object.DestroyImmediate(stage); }
        }
    }
}
