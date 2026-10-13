using UnityEngine;

namespace Jarldom
{
    // A soul's last voyage, as every game near sees it: the funeral ship slides off the beach, goes out burning on the tide, and sinks.
    // (The real piece is gone already, consumed with its grave goods on the colony owner's game: this is a copy with no network parts.)
    internal static class ShipBurial
    {
        // On the colony's owner: the way out to open water from where it stands (the deepest way within 60 m), as a direction.
        public static Vector3 Out(Vector3 at, Vector3 colony)
        {
            float water = ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f;
            Vector3 best = at - colony; best.y = 0; float bestScore = float.MinValue;
            for (int k = 0; k < 24; k++)
            {
                Vector3 d = Quaternion.Euler(0, k * 15f, 0) * Vector3.forward; float score = 0;
                for (float r = 10f; r <= 60f; r += 10f) score += Mathf.Min(6f, water - ZoneSystem.instance.GetGroundHeight(at + d * r));
                if (score > bestScore) { bestScore = score; best = d; }
            }
            return best.sqrMagnitude > 0.01f ? best.normalized : Vector3.forward;
        }
        public static void Show(Vector3 at, Quaternion rot, Vector3 dir)
        {
            if (Player.m_localPlayer == null || Vector3.Distance(Player.m_localPlayer.transform.position, at) > 400) return;
            var go = new GameObject("JarldomShipBurial");
            go.transform.SetPositionAndRotation(at, rot);
            Transform look = new GameObject("look").transform; look.SetParent(go.transform, false);
            if (Pieces.KarveLook(look) == null) { Object.Destroy(go); return; }
            // Ablaze: the bonfire's own flames and smoke along its length (the fire pit's if there's no bonfire), and their glow on the water.
            // (only its particles and its crackle: not its log heap, its burning ground or its colliders)
            Transform bonfire = Assets.Find("bonfire")?.transform;
            GameObject flames = bonfire?.Find("_enabled_high/fx_BonfireFlames")?.gameObject, crackle = bonfire?.Find("_enabled_high/SFX")?.gameObject;
            if (flames != null)
                for (int i = 0; i < 3; i++) { GameObject f = Object.Instantiate(flames, go.transform, false); f.SetActive(true); f.transform.localPosition = new Vector3(0, 0.6f, -2.4f + i * 2.4f); f.transform.localScale = Vector3.one * 1.2f; }
            if (crackle != null) { GameObject a = Object.Instantiate(crackle, go.transform, false); a.SetActive(true); a.transform.localPosition = Vector3.up; }
            Light glow = new GameObject("glow").AddComponent<Light>(); glow.transform.SetParent(go.transform, false); glow.transform.localPosition = Vector3.up * 3f;
            glow.type = LightType.Point; glow.color = new Color(1f, 0.55f, 0.2f); glow.range = 18f; glow.intensity = 2.5f;
            go.AddComponent<Voyage>().Begin(at, rot, dir);
        }
        private sealed class Voyage : MonoBehaviour
        {
            private const float Slide = 8f, Drift = 52f, Sink = 14f, Out = 120f;
            private Vector3 _from, _sea, _dir; private Quaternion _rot; private float _t, _water;
            public void Begin(Vector3 at, Quaternion rot, Vector3 dir)
            {
                _from = at; _rot = rot; _dir = dir; _water = ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f;
                _sea = new Vector3(at.x, _water - 0.15f, at.z) + dir * 8f; // (afloat, off the beach)
            }
            private void Update()
            {
                _t += Time.deltaTime;
                Quaternion heading = Quaternion.LookRotation(_dir);
                if (_t < Slide)
                {
                    float s = Smooth(_t / Slide);
                    transform.position = Vector3.Lerp(_from, _sea, s);
                    transform.rotation = Quaternion.Slerp(_rot, heading, s);
                }
                else if (_t < Slide + Drift)
                {
                    float s = (_t - Slide) / Drift;
                    transform.position = _sea + _dir * (Out * s) + Vector3.up * Mathf.Sin(_t * 0.9f) * 0.12f;
                    transform.rotation = heading * Quaternion.Euler(Mathf.Sin(_t * 0.7f) * 2.5f, 0, Mathf.Sin(_t * 0.5f) * 3f);
                }
                else if (_t < Slide + Drift + Sink)
                {
                    float s = (_t - Slide - Drift) / Sink;
                    transform.position = _sea + _dir * Out + Vector3.down * (s * s * 7f);
                    transform.rotation = heading * Quaternion.Euler(-s * 18f, 0, s * 9f); // (by the stern, listing)
                }
                else Destroy(gameObject);
            }
            private static float Smooth(float x) => x * x * (3 - 2 * x);
        }
    }
}
