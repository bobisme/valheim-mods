using System.Linq;
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
        public static void Show(Vector3 at, Quaternion rot, Vector3 dir, Vector3 archer)
        {
            Funeral.LaunchedAt = Time.time; // (the farewells on the shore go by this, on every game)
            if (Player.m_localPlayer == null || Vector3.Distance(Player.m_localPlayer.transform.position, at) > 400) return;
            var go = new GameObject("JarldomShipBurial");
            go.transform.SetPositionAndRotation(at, rot);
            Transform look = new GameObject("look").transform; look.SetParent(go.transform, false);
            if (Pieces.KarveLook(look) == null) { Object.Destroy(go); return; }
            // The dead laid on the deck in a linen shroud.
            Material linen = Assets.Find("wood_pole")?.GetComponentsInChildren<MeshRenderer>(true).Select(r => r.sharedMaterial).FirstOrDefault(m => m != null && m.shader != null && m.shader.name == "Custom/Piece");
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule); Object.Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(go.transform, false); body.transform.localPosition = new Vector3(0, 0.75f, -0.4f); body.transform.localRotation = Quaternion.Euler(90, 0, 0); body.transform.localScale = new Vector3(0.42f, 0.85f, 0.32f);
            if (linen != null) body.GetComponent<MeshRenderer>().sharedMaterial = Pieces.Plain(linen, new Color(0.86f, 0.82f, 0.72f));
            // Ablaze (once the arrow strikes): the bonfire's own flames and smoke along its length, and their glow on the water.
            // (only its particles and its crackle: not its log heap, its burning ground or its colliders)
            var fire = new GameObject("fire"); fire.transform.SetParent(go.transform, false); fire.SetActive(false);
            Transform bonfire = Assets.Find("bonfire")?.transform;
            GameObject flames = bonfire?.Find("_enabled_high/fx_BonfireFlames")?.gameObject, crackle = bonfire?.Find("_enabled_high/SFX")?.gameObject;
            if (flames != null)
                for (int i = 0; i < 3; i++) { GameObject f = Object.Instantiate(flames, fire.transform, false); f.SetActive(true); f.transform.localPosition = new Vector3(0, 0.6f, -2.4f + i * 2.4f); f.transform.localScale = Vector3.one * 1.2f; }
            if (crackle != null) { GameObject a = Object.Instantiate(crackle, fire.transform, false); a.SetActive(true); a.transform.localPosition = Vector3.up; }
            Light glow = new GameObject("glow").AddComponent<Light>(); glow.transform.SetParent(fire.transform, false); glow.transform.localPosition = Vector3.up * 3f;
            glow.type = LightType.Point; glow.color = new Color(1f, 0.55f, 0.2f); glow.range = 18f; glow.intensity = 2.5f;
            if (Vector3.Distance(Player.m_localPlayer.transform.position, at) < 150) MusicMan.instance?.TriggerMusic(Music); // (the Valkyrie's own theme, the game's opening)
            go.AddComponent<Voyage>().Begin(at, rot, dir, archer, fire);
        }
        public const string Music = "intro";
        private sealed class Voyage : MonoBehaviour
        {
            private const float Slide = 8f, Loose = 10f, Flight = 1.8f, Drift = 52f, Sink = 14f, Out = 120f;
            private Vector3 _from, _sea, _dir, _archer; private Quaternion _rot; private float _t, _water; private GameObject _fire, _arrow;
            public void Begin(Vector3 at, Quaternion rot, Vector3 dir, Vector3 archer, GameObject fire)
            {
                _from = at; _rot = rot; _dir = dir; _archer = archer; _fire = fire; _water = ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f;
                _sea = new Vector3(at.x, _water - 0.15f, at.z) + dir * 8f; // (afloat, off the beach)
                if (archer == Vector3.zero) _fire.SetActive(true); // (nobody to loose an arrow: lit as it goes)
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
                    transform.position = _sea + _dir * (Out * s * s * 0.4f + Out * s * 0.6f) + Vector3.up * Mathf.Sin(_t * 0.9f) * 0.12f; // (slowly at first, the tide taking it)
                    transform.rotation = heading * Quaternion.Euler(Mathf.Sin(_t * 0.7f) * 2.5f, 0, Mathf.Sin(_t * 0.5f) * 3f);
                }
                else if (_t < Slide + Drift + Sink)
                {
                    float s = (_t - Slide - Drift) / Sink;
                    transform.position = _sea + _dir * Out + Vector3.down * (s * s * 7f);
                    transform.rotation = heading * Quaternion.Euler(-s * 18f, 0, s * 9f); // (by the stern, listing)
                }
                else { if (_arrow != null) Destroy(_arrow); Destroy(gameObject); return; }
                Arrow();
            }
            // The burning arrow: loosed from the shore in an arc, into the hull; the ship catches.
            private void Arrow()
            {
                if (_archer == Vector3.zero || _fire.activeSelf) return;
                if (_t < Loose) return;
                if (_arrow == null)
                {
                    _arrow = new GameObject("JarldomBurningArrow");
                    GameObject shaft = Assets.Find("ArrowFire") ?? Assets.Find("ArrowWood");
                    if (shaft != null) Assets.Model(shaft, _arrow.transform, Vector3.zero, Quaternion.identity, 1.4f);
                    GameObject flames = Assets.Find("bonfire")?.transform.Find("_enabled_high/fx_BonfireFlames")?.gameObject;
                    if (flames != null) { GameObject f = Instantiate(flames, _arrow.transform, false); f.SetActive(true); f.transform.localScale = Vector3.one * 0.12f; }
                    Light l = _arrow.AddComponent<Light>(); l.color = new Color(1f, 0.6f, 0.25f); l.range = 6f; l.intensity = 2f;
                    Assets.Effect("sfx_bow_fire", _archer);
                }
                float k = Mathf.Clamp01((_t - Loose) / Flight);
                Vector3 target = transform.position + Vector3.up * 1.2f, p = Vector3.Lerp(_archer, target, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * (Vector3.Distance(_archer, target) * 0.25f);
                Vector3 ahead = Vector3.Lerp(_archer, target, Mathf.Min(1f, k + 0.02f)) + Vector3.up * Mathf.Sin(Mathf.Min(1f, k + 0.02f) * Mathf.PI) * (Vector3.Distance(_archer, target) * 0.25f);
                _arrow.transform.position = p; if ((ahead - p).sqrMagnitude > 0.0001f) _arrow.transform.rotation = Quaternion.LookRotation(ahead - p);
                if (k >= 1f) { Destroy(_arrow); _arrow = null; _fire.SetActive(true); Assets.Effect("sfx_FireAddFuel", target); Assets.Effect("vfx_bonfire_AddFuel", target); }
            }
            private static float Smooth(float x) => x * x * (3 - 2 * x);
        }
    }
}
