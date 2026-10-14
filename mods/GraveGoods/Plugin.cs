using System.Globalization;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

// Claude Tools may reload it in a world: it adds no prefabs; grave goods are ordinary items with a record on them.
[assembly: System.Reflection.AssemblyMetadata("ClaudeTools.Reload","world")]

namespace GraveGoods
{
    [BepInPlugin(Guid,Name,Version)]
    public sealed class Plugin:BaseUnityPlugin
    {
        public const string Guid="com.bobisme.gravegoods";
        public const string Name="Grave Goods";
        public const string Version="0.1.1";
        internal static Plugin Instance;
        internal static ConfigEntry<float> ChestChance;
        internal static ConfigEntry<string> RarityWeights;
        internal static ConfigEntry<bool> GroundGlow,SlotTint;
        private Harmony _harmony;

        private void Awake()
        {
            Instance=this;
            ChestChance=Config.Bind("Loot","ChestChance",0.35f,new ConfigDescription("How often a treasure chest holds grave goods (0 to 1). Each chest is rolled once, when someone first comes near it.",new AcceptableValueRange<float>(0,1)));
            RarityWeights=Config.Bind("Loot","RarityWeights","40,33,18,7,2","How likely each rarity is, as weights: Plain, Fine, Runed, Heirloom, Saga.");
            GroundGlow=Config.Bind("Display","GroundGlow",true,"Runed and rarer grave goods glow in their colour where they lie.");
            SlotTint=Config.Bind("Display","SlotTint",true,"Grave goods' inventory slots are tinted in their rarity's colour.");
            _harmony=new Harmony(Guid);_harmony.PatchAll(typeof(Plugin).Assembly);
            Logger.LogInfo($"{Name} {Version} loaded.");
        }
        internal static void Log(string text)=>Instance?.Logger.LogInfo(text);
        internal static double[] Weights()
        {
            double[] w=RarityWeights.Value.Split(',').Select(s=>double.TryParse(s.Trim(),NumberStyles.Float,CultureInfo.InvariantCulture,out double v)?System.Math.Max(0,v):0).ToArray();
            if(w.Length!=5||w.Sum()<=0)w=new double[]{40,33,18,7,2};
            return w;
        }
        private void Update(){Tools.Tick();if(!InventoryGui.IsVisible())Pinned.Off();}
        private void OnDestroy()
        {
            Tools.Stop();
            Pinned.Off();
            Looks.Stop();
            _harmony?.UnpatchSelf();
            if(Instance==this)Instance=null;
        }
    }
}
