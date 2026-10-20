using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using UnityEngine;

// Throwaway (bn-3uwk): on BepInEx/claude/staticprobe.req "<tag>", after a full GC writes, for every loaded non-system assembly, each
// static field's size (collections: Count; arrays: Length; delegates: handlers; strings: length), with dead Unity objects counted,
// and the same for instance fields of objects that live across world loads (DontDestroyOnLoad), to staticprobe-<tag>.txt.
[BepInPlugin("cx.bob.staticprobe", "StaticProbe", "1.0.0")]
public class StaticProbe : BaseUnityPlugin
{
    float _next;
    static readonly string[] Skip = { "System", "mscorlib", "Mono.", "Unity.", "UnityEngine", "netstandard", "Microsoft.", "BepInEx", "0Harmony", "HarmonyX", "MonoMod", "Newtonsoft", "Assembly-CSharp-firstpass", "StaticProbe", "AssetProbe", "Steamworks", "Splatform", "PlayFab", "SoftReferenceableAssets", "Sirenix", "ConnectedStorage" };
    void Update()
    {
        if (Time.unscaledTime < _next) return; _next = Time.unscaledTime + 1;
        string dir = Path.Combine(Paths.BepInExRootPath, "claude"), req = Path.Combine(dir, "staticprobe.req");
        if (!File.Exists(req)) return;
        string tag = File.ReadAllText(req).Trim(); File.Delete(req); if (tag == "") tag = "x";
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var sb = new StringBuilder();
        sb.AppendLine($"heapMB\t{GC.GetTotalMemory(true) / 1048576}");
        foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
        {
            string an = a.GetName().Name;
            if (Skip.Any(s => an.StartsWith(s))) continue;
            Type[] types; try { types = a.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); } catch { continue; }
            foreach (Type t in types)
            {
                if (t.ContainsGenericParameters) continue;
                FieldInfo[] fs; try { fs = t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly); } catch { continue; }
                foreach (FieldInfo f in fs)
                {
                    if (f.IsLiteral) continue;
                    object v; try { v = f.GetValue(null); } catch { continue; }
                    Line(sb, $"{an.Split('-')[0]}|{t.FullName}.{f.Name}", v);
                }
            }
        }
        // objects that outlive a world: their own collections and events
        foreach (MonoBehaviour mb in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
        {
            if (mb == null || !mb.gameObject.scene.IsValid() || mb.gameObject.scene.name != "DontDestroyOnLoad") continue;
            Type t = mb.GetType();
            for (Type x = t; x != null && x != typeof(MonoBehaviour); x = x.BaseType)
                foreach (FieldInfo f in x.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    object v; try { v = f.GetValue(mb); } catch { continue; }
                    Line(sb, $"DDOL:{mb.gameObject.name}|{x.FullName}.{f.Name}", v);
                }
        }
        File.WriteAllText(Path.Combine(dir, "staticprobe-" + tag + ".txt"), sb.ToString());
        Logger.LogInfo("StaticProbe wrote " + tag);
    }
    static void Line(StringBuilder sb, string key, object v)
    {
        if (v == null) return;
        int n = -1, dead = 0;
        switch (v)
        {
            case string s: if (s.Length > 1000) n = s.Length; break;
            case Delegate d: n = d.GetInvocationList().Length; dead = d.GetInvocationList().Count(h => h.Target is UnityEngine.Object o && o == null); break;
            case Array arr: n = arr.Length; break;
            case ICollection c: n = c.Count; break;
            case UnityEngine.Object o: if (o == null) { n = 0; dead = 1; } break;
            default:
                // HashSet<T>, Queue<T> etc. that aren't ICollection: their Count property
                var p = v.GetType().GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
                if (p != null && p.PropertyType == typeof(int) && p.GetIndexParameters().Length == 0) try { n = (int)p.GetValue(v); } catch { }
                break;
        }
        if (n < 0) return;
        if (v is IEnumerable e && !(v is string) && n > 0 && n < 200000)
            try { foreach (object item in e) { object it = item is DictionaryEntry de ? de.Value : item; if (it is UnityEngine.Object uo && uo == null) dead++; else if (it != null && it.GetType().IsGenericType && it.GetType().GetGenericTypeDefinition() == typeof(KeyValuePair<,>)) { var val = it.GetType().GetProperty("Value").GetValue(it); var kk = it.GetType().GetProperty("Key").GetValue(it); if (val is UnityEngine.Object vo && vo == null || kk is UnityEngine.Object ko && ko == null) dead++; } } } catch { }
        if (n >= 20 || dead > 0) sb.AppendLine($"{key}\t{n}\t{dead}");
    }
}
