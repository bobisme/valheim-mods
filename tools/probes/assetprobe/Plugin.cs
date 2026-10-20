using System;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using UnityEngine;

// Throwaway: on BepInEx/claude/assetprobe.req, writes every loaded asset's count by type and name to assetprobe-<tag>.txt
[BepInPlugin("cx.bob.assetprobe", "AssetProbe", "1.0.0")]
public class AssetProbe : BaseUnityPlugin
{
    float _next;
    void Update()
    {
        if (Time.unscaledTime < _next) return; _next = Time.unscaledTime + 1;
        string dir = Path.Combine(Paths.BepInExRootPath, "claude"), req = Path.Combine(dir, "assetprobe.req");
        if (!File.Exists(req)) return;
        string[] args = File.ReadAllText(req).Trim().Split(' '); File.Delete(req);
        string tag = args.Length > 0 && args[0] != "" ? args[0] : "x";
        if (args.Length > 1 && args[1] == "recipes")
        {
            var rb = new StringBuilder();
            foreach (Recipe r in ObjectDB.instance.m_recipes.Where(r => r != null && r.m_item != null && args.Skip(2).Any(a => r.m_item.name.Contains(a) || r.m_resources.Any(q => q.m_resItem != null && q.m_resItem.name.Contains(a)))))
                rb.AppendLine($"{r.name}: {r.m_amount} {r.m_item.name} at {r.m_craftingStation?.name} lvl{r.m_minStationLevel} from {string.Join(", ", r.m_resources.Select(q => q.m_amount + " " + q.m_resItem?.name))}");
            foreach (var cs in Resources.FindObjectsOfTypeAll<CookingStation>().GroupBy(c => c.name)) rb.AppendLine($"cook {cs.Key}: {string.Join(", ", cs.First().m_conversion.Select(c => c.m_from?.name + "->" + c.m_to?.name + " " + c.m_cookTime))}");
            foreach (var sm in Resources.FindObjectsOfTypeAll<Smelter>().GroupBy(c => c.name)) rb.AppendLine($"smelt {sm.Key}: fuel {sm.First().m_fuelItem?.name} {string.Join(", ", sm.First().m_conversion.Select(c => c.m_from?.name + "->" + c.m_to?.name))} sec {sm.First().m_secPerProduct}");
            foreach (var f in Resources.FindObjectsOfTypeAll<Fermenter>().GroupBy(c => c.name)) rb.AppendLine($"ferment {f.Key}: {f.First().m_fermentationDuration}s {string.Join(", ", f.First().m_conversion.Select(c => c.m_from?.name + "->" + c.m_to?.name + " x" + c.m_producedItems))}");
            File.WriteAllText(Path.Combine(dir, "assetprobe-" + tag + ".txt"), rb.ToString()); return;
        }
        if (args.Contains("gc")) { GC.Collect(); Resources.UnloadUnusedAssets(); }
        var sb = new StringBuilder();
        sb.AppendLine($"unityMB {UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576} monoMB {UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / 1048576}");
        foreach (var g in AppDomain.CurrentDomain.GetAssemblies().GroupBy(a => a.GetName().Name.Split('-')[0]).Where(g => g.Count() > 1)) sb.AppendLine($"asm {g.Key} {g.Count()}");
        foreach (Type t in new[] { typeof(Material), typeof(Mesh), typeof(Texture2D), typeof(Sprite), typeof(GameObject), typeof(MonoBehaviour), typeof(ScriptableObject) })
        {
            var all = Resources.FindObjectsOfTypeAll(t);
            sb.AppendLine($"total {t.Name} {all.Length}");
            Func<UnityEngine.Object, string> key = o => t == typeof(MonoBehaviour) ? o.GetType().Assembly.GetName().Name.Split('-')[0] + ":" + o.GetType().Name : o.name;
            foreach (var g in all.GroupBy(key)) sb.AppendLine($"{t.Name}\t{g.Count()}\t{g.Key}");
        }
        foreach (var g in Resources.FindObjectsOfTypeAll<Texture2D>().Where(x => x.name == "").GroupBy(x => x.width + "x" + x.height + " " + x.hideFlags)) sb.AppendLine($"NoNameTex\t{g.Count()}\t{g.Key}");
        foreach (var g in Resources.FindObjectsOfTypeAll<GameObject>().Where(x => x.transform.parent == null).GroupBy(x => x.name + (x.scene.IsValid() ? " @" + x.scene.name : " @none"))) sb.AppendLine($"Root\t{g.Count()}\t{g.Key}");
        foreach (var g in Resources.FindObjectsOfTypeAll<GameObject>().GroupBy(x => { var t = x.transform; while (t.parent != null) t = t.parent; return t.name + (x.scene.IsValid() ? " @" + x.scene.name : " @none"); })) sb.AppendLine($"UnderRoot\t{g.Count()}\t{g.Key}");
        foreach (var g in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.GetType().Name != "ParticleSystemRenderer" && (r.sharedMaterials.Any(m => m == null) || r.GetComponent<MeshFilter>() is MeshFilter f && f.sharedMesh == null)).GroupBy(r => r.transform.root.name)) sb.AppendLine($"Broken\t{g.Count()}\t{g.Key}");
        sb.AppendLine($"heapMB {GC.GetTotalMemory(false) / 1048576}");
        File.WriteAllText(Path.Combine(dir, "assetprobe-" + tag + ".txt"), sb.ToString());
        Logger.LogInfo("AssetProbe wrote " + tag);
    }
}
