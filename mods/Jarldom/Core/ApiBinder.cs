using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Jarldom
{
    // Another mod's published API (a "public static class Api" with "public const int Version"), bound to typed delegates once.
    // Never throws: a mod that's missing, too old, or lacks a method gives null, and the feature that wanted it stays off.
    // The publishing side's rules: bump Version when adding a method; never change a method's signature (add a new one instead);
    // say in a comment when a method only answers on the host. An Api with no Version counts as version 0.
    public sealed class ApiBinder
    {
        public Type Type { get; }
        public int Version { get; }
        public bool Present => Type != null;
        private readonly Dictionary<string, Delegate> _bound = new Dictionary<string, Delegate>();
        private readonly Action<string> _warn;

        public ApiBinder(Type type, Action<string> warn = null) { Type = type; Version = ReadVersion(type); _warn = warn; }

        public static int ReadVersion(Type t)
        {
            if (t == null) return 0;
            try
            {
                FieldInfo f = t.GetField("Version", BindingFlags.Public | BindingFlags.Static);
                if (f != null && f.FieldType == typeof(int)) return (int)f.GetValue(null);
                PropertyInfo p = t.GetProperty("Version", BindingFlags.Public | BindingFlags.Static);
                if (p != null && p.PropertyType == typeof(int)) return (int)p.GetValue(null, null);
            }
            catch { }
            return 0;
        }
        public static bool Usable(int have, int need) => have >= need;

        // The static method of that name whose parameters and return fit T (a Func or Action), from version `since` on; else null.
        // A method that should be there and isn't is told once (to the log), not on every call.
        public T Bind<T>(string name, int since = 0) where T : class
        {
            if (Type == null || !Usable(Version, since)) return null;
            string key = name + "|" + typeof(T).FullName;
            if (_bound.TryGetValue(key, out Delegate had)) return had as T;
            Delegate made = null;
            try
            {
                MethodInfo invoke = typeof(T).GetMethod("Invoke");
                Type[] args = invoke?.GetParameters().Select(p => p.ParameterType).ToArray();
                MethodInfo m = args == null ? null : Type.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, args, null);
                if (m != null && invoke.ReturnType.IsAssignableFrom(m.ReturnType)) made = Delegate.CreateDelegate(typeof(T), m, false);
            }
            catch { made = null; }
            if (made == null) _warn?.Invoke($"{Type.FullName}.{name} (version {Version}) isn't there in the shape expected: that part is left off.");
            _bound[key] = made;
            return made as T;
        }
    }
}
