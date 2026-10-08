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
        internal bool Ready()
        {
            BaseUnityPlugin current = Chainloader.PluginInfos.TryGetValue(Guid, out var info) ? info.Instance : null;
            if (current == null) { _instance = null; _register = _unregister = _stop = null; return false; }
            if (current == _instance && _stop != null) return true;
            _instance = current; _register = _unregister = _stop = null;
            Type type = current.GetType();
            FieldInfo version = type.GetField("SmokingApiVersion", BindingFlags.Public|BindingFlags.Static);
            if (version == null || !version.IsLiteral || version.FieldType != typeof(int) || !Equals(version.GetRawConstantValue(), 1)) return false;
            _register = type.GetMethod("RegisterSmokingEffect", new[] { typeof(string) });
            _unregister = type.GetMethod("UnregisterSmokingEffect", new[] { typeof(string) });
            _stop = type.GetMethod("StopOtherSmoking", new[] { typeof(Character), typeof(string) });
            if (_register?.ReturnType != typeof(bool) || _unregister?.ReturnType != typeof(void) || _stop?.ReturnType != typeof(bool) || _register.IsStatic || _unregister.IsStatic || _stop.IsStatic)
            { _stop = null; return false; }
            try
            {
                foreach (Blend blend in Blend.All)
                    if (!(bool)_register.Invoke(current, new object[] { blend.Effect })) { Release(); return false; }
                return true;
            }
            catch { Release(); return false; }
        }
        internal bool Exclusive(Character character, string effect)
        {
            if (!Ready()) return false;
            try { return (bool)_stop.Invoke(_instance, new object[] { character, effect }); }
            catch { return false; }
        }
        internal void Release()
        {
            if (_instance != null && _unregister != null)
                foreach (Blend blend in Blend.All)
                    try { _unregister.Invoke(_instance, new object[] { blend.Effect }); } catch { }
            _instance = null; _register = _unregister = _stop = null;
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
