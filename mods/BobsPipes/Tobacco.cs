using System;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;

namespace BobsPipes
{
    // Always reconnect to the live Cigars instance; no reference to its reloadable assembly identity.
    internal sealed class Tobacco
    {
        internal const string Guid = "com.dhack.cigarsmoking";
        private BaseUnityPlugin _instance;
        private MethodInfo _register, _unregister, _stop;
        private bool _checked;
        // Shared: Cigars offers smoking API v1 or newer, so a pipe and a cigar share the one-active-smoke rule.
        // Without it (Quad's released Cigars), pipes still work on their own; lighting one just cannot put out a cigar.
        internal bool Shared => _stop != null;
        internal bool Ready()
        {
            BaseUnityPlugin current = Chainloader.PluginInfos.TryGetValue(Guid, out var info) ? info.Instance : null;
            if (current == null) { _instance = null; _register = _unregister = _stop = null; _checked = false; return false; }
            if (current == _instance && _checked) return true;
            _instance = current; _register = _unregister = _stop = null; _checked = true;
            if (!Bind(current)) _register = _unregister = _stop = null;
            return true;
        }
        private bool Bind(BaseUnityPlugin current)
        {
            Type type = current.GetType();
            FieldInfo version = type.GetField("SmokingApiVersion", BindingFlags.Public|BindingFlags.Static);
            if (version == null || !version.IsLiteral || version.FieldType != typeof(int) || !(version.GetRawConstantValue() is int v) || v < 1) return false;
            MethodInfo register = type.GetMethod("RegisterSmokingEffect", new[] { typeof(string) });
            MethodInfo unregister = type.GetMethod("UnregisterSmokingEffect", new[] { typeof(string) });
            MethodInfo stop = type.GetMethod("StopOtherSmoking", new[] { typeof(Character), typeof(string) });
            if (register?.ReturnType != typeof(bool) || unregister?.ReturnType != typeof(void) || stop?.ReturnType != typeof(bool) || register.IsStatic || unregister.IsStatic || stop.IsStatic)
                return false;
            _register = register; _unregister = unregister;
            try
            {
                foreach (Blend blend in Blend.All)
                    if (!(bool)_register.Invoke(current, new object[] { blend.Effect })) { Unregister(current); return false; }
            }
            catch { Unregister(current); return false; }
            _stop = stop;
            return true;
        }
        internal bool Exclusive(Character character, string effect)
        {
            if (!Ready()) return false;
            if (!Shared) return true; // nothing to coordinate with
            try { return (bool)_stop.Invoke(_instance, new object[] { character, effect }); }
            catch { return false; }
        }
        private void Unregister(BaseUnityPlugin current)
        {
            if (_unregister != null)
                foreach (Blend blend in Blend.All)
                    try { _unregister.Invoke(current, new object[] { blend.Effect }); } catch { }
        }
        internal void Release()
        {
            if (_instance != null) Unregister(_instance);
            _instance = null; _register = _unregister = _stop = null; _checked = false;
        }
    }
    internal sealed class Blend
    {
        internal readonly string Id, Name, Leaf, Extra, Flavour;
        internal readonly int ExtraCount;
        internal string Prefab => "BobPipeBlend_" + Id;
        internal string Effect => "SE_bob_pipe_" + Id;
        private Blend(string id, string name, string leaf, string extra, int count, string flavour)
        { Id=id; Name=name; Leaf=leaf; Extra=extra; ExtraCount=count; Flavour=flavour; }
        internal static readonly Blend[] All =
        {
            new Blend("meadow", "Meadow Pipe Tobacco", "dh_dried_meadow", "", 0, "Light, grassy and mellow. A bowl for watching the fire. Stamina regeneration +5% while lit."),
            new Blend("forest", "Honeywood Pipe Tobacco", "dh_aged_forest", "Honey", 1, "Dark forest leaf mellowed with honey. Sweet woodsmoke for a quiet evening. Health regeneration +10% while lit."),
            new Blend("plains", "Cloudberry Pipe Tobacco", "dh_aged_plains", "Cloudberry", 2, "Spicy plains leaf with a bright cloudberry finish. Running costs 5% less stamina while lit."),
        };
    }
}
