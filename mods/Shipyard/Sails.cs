using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Shipyard
{
    // Homespun's sails (Core/Hulls.cs Sailcloth): found by name through its Api (version 3: no hard reference), looked for again now and
    // then (it may load after us, or be reloaded); and a sail's colour painted on the knarr: her stripes, set and furled.
    internal static class Sails
    {
        internal const string Key = "shipyard_sail"; // (on a launched boat's ZDO: her sail's colour, "" undyed; none: as baked)
        private static float _looked = -100;
        private static Func<string, (float r, float g, float b)?> _rgb;

        internal static void Bind()
        {
            if (Time.realtimeSinceStartup - _looked < 10) return;
            _looked = Time.realtimeSinceStartup;
            Type api = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Homespun.Api", false)).LastOrDefault(t => t != null);
            int version = api?.GetField("Version", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is int v ? v : 0;
            if (api == null || version < 3) { Sailcloth.Sail = null; Sailcloth.ColourOf = null; Sailcloth.Variants = new string[0]; _rgb = null; return; }
            try
            {
                var isSail = (Func<string, bool>)Delegate.CreateDelegate(typeof(Func<string, bool>), api.GetMethod("IsSail", new[] { typeof(string) }));
                var colourOf = (Func<string, string>)Delegate.CreateDelegate(typeof(Func<string, string>), api.GetMethod("ColourOf", new[] { typeof(string) }));
                _rgb = (Func<string, (float r, float g, float b)?>)Delegate.CreateDelegate(typeof(Func<string, (float r, float g, float b)?>), api.GetMethod("Rgb", new[] { typeof(string) }));
                Sailcloth.Variants = (string[])api.GetProperty("Sails").GetValue(null);
                Sailcloth.ColourOf = p => isSail(p) ? colourOf(p) ?? "" : null;
                Sailcloth.Sail = (string)api.GetProperty("Sail").GetValue(null);
            }
            catch (Exception e) { Plugin.Warn("Homespun's sails: " + e.Message); Sailcloth.Sail = null; Sailcloth.ColourOf = null; Sailcloth.Variants = new string[0]; }
        }

        // A model in a sail's colour: its stripes repainted (the white stays white, wool's own). The material shared by all in that colour.
        private static readonly Dictionary<string, Material> Painted = new Dictionary<string, Material>();
        internal static void Paint(GameObject look, string model, string colour)
        {
            if (look == null || !(look.GetComponent<MeshRenderer>() is MeshRenderer r)) return;
            Material[] baked = Looks.For(ZNetScene.instance, model);
            var stripe = Sailcloth.Stripe(colour, _rgb);
            if (stripe == null || baked == null) { if (baked != null) r.sharedMaterials = baked; return; }
            string key = model + "|" + colour;
            if (!Painted.TryGetValue(key, out Material m) || m == null)
            {
                Texture2D tex = Repaint(model, stripe.Value);
                if (tex == null) { r.sharedMaterials = baked; return; }
                m = new Material(baked[0]) { name = "Shipyard " + model + " " + (colour == "" ? "undyed" : colour) };
                m.SetTexture("_MainTex", tex);
                Looks.Owned.Add(m); Looks.Owned.Add(tex); Painted[key] = m;
            }
            r.sharedMaterial = m;
        }
        internal static void Forget() => Painted.Clear(); // (Looks.Forget destroys them)

        // The baked albedo read again (the material's own is no longer readable), its madder red turned to the stripe's colour, the
        // shading kept: a pixel as red as the baked stripe takes the new colour as bright as it was; wood and white are left.
        private static readonly MethodInfo LoadImage = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule")?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });
        private static Texture2D Repaint(string model, (float r, float g, float b) to)
        {
            using (var raw = typeof(Plugin).Assembly.GetManifestResourceStream("Shipyard." + model + "_albedo.png"))
            {
                if (raw == null || LoadImage == null) return null;
                var bytes = new byte[raw.Length]; raw.Read(bytes, 0, bytes.Length);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                if (!(bool)LoadImage.Invoke(null, new object[] { tex, bytes, false })) { Object.DestroyImmediate(tex); return null; }
                Color[] px = tex.GetPixels();
                const float redLum = 0.3f * 0.58f + 0.59f * 0.12f + 0.11f * 0.09f; // (the baked madder red's brightness)
                for (int i = 0; i < px.Length; i++)
                {
                    Color c = px[i];
                    float t = Mathf.Clamp01((c.r - c.g - 0.15f) / 0.3f);
                    if (t <= 0) continue;
                    float k = (0.3f * c.r + 0.59f * c.g + 0.11f * c.b) / redLum;
                    px[i] = Color.Lerp(c, new Color(Mathf.Clamp01(to.r * k), Mathf.Clamp01(to.g * k), Mathf.Clamp01(to.b * k), c.a), t);
                }
                tex.SetPixels(px); tex.Apply(true, true);
                tex.name = "Shipyard " + model + " albedo repainted"; tex.wrapMode = TextureWrapMode.Repeat;
                return tex;
            }
        }
    }
}
