using System;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace BobsPipes
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(global::BobsPipes.Tobacco.Guid)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.bobisme.bobspipes";
        public const string Name = "Bob's Pipes";
        public const string Version = "0.1.2";
        internal static Plugin Instance;
        internal ConfigEntry<float> Minutes, Strength;
        internal ConfigEntry<bool> ShowSmoke, Rain;
        internal readonly Tobacco Tobacco = new Tobacco();
        private Harmony _harmony;
        private ItemDrop.ItemData _pipe;
        private Player _owner;
        private Bowl _bowl;
        private float _nextRegister, _nextSave, _nextPuff;
        private bool _stopping;
        private static readonly System.Reflection.FieldInfo RightHand = AccessTools.Field(typeof(Humanoid), "m_rightItem");
        internal static ItemDrop.ItemData RightItem(Player player) => player!=null?(ItemDrop.ItemData)RightHand.GetValue(player):null;
        internal const string BowlKey = "bob_pipes_bowl";
        internal const string EndKey = "bob_pipe_end", BlendKey = "bob_pipe_blend", PuffKey = "bob_pipe_puff", PatinaKey = "bob_pipe_patina";
        internal static Bowl Read(ItemDrop.ItemData item) => item?.m_customData != null && item.m_customData.TryGetValue(BowlKey, out string data) ? Bowl.Read(data) : default;

        private void Awake()
        {
            Instance = this;
            Minutes = Config.Bind("Smoking", "BowlMinutes", 5f, new ConfigDescription("How long a freshly packed bowl lasts. Existing bowls keep their saved contents.", new AcceptableValueRange<float>(0.5f,60)));
            Strength = Config.Bind("Smoking", "EffectStrength", 100f, new ConfigDescription("Percent of the small pipe bonuses. 0 makes smoking cosmetic.", new AcceptableValueRange<float>(0,100)));
            ShowSmoke = Config.Bind("Look", "Smoke", true, "Subtle smoke from the bowl and occasional exhalations.");
            Rain = Config.Bind("Smoking", "RainExtinguishes", true, "An exposed bowl goes out in rain; its remaining tobacco is kept.");
            _harmony = new Harmony(Guid); _harmony.PatchAll(typeof(Plugin).Assembly);
            Items.Register();
            foreach (Player player in Player.GetAllPlayers()) PipeLook.Attach(player);
            Logger.LogInfo(Name + " " + Version + " loaded; use the pipe or a blend from inventory/hotbar.");
        }
        private void Update()
        {
            if (Time.unscaledTime >= _nextRegister)
            {
                _nextRegister = Time.unscaledTime+1;
                Items.Register(); Items.Relink(Player.m_localPlayer?.GetInventory());
                Tobacco.Ready();
            }
            if (_pipe == null) return;
            bool owned = OwnsPipe();
            if (!owned || _owner != Player.m_localPlayer || _owner.IsDead() || ZNet.instance == null || !Tobacco.Ready())
            { Snuff(null, owned); return; }
            if (_owner.InAttack() || _owner.IsBlocking() || _owner.IsSwimming() || _owner.InBed())
            { Snuff("You put your pipe away."); return; }
            if (Rain.Value && EnvMan.IsWet() && !Cover.IsUnderRoof(_owner.GetCenterPoint()))
            { Snuff("The rain puts out your pipe. The bowl is saved."); return; }
            _bowl = _bowl.Burn(Math.Max(0, Time.deltaTime));
            Save(false);
            if (_bowl.Empty) { Snuff("Your bowl has burned down."); return; }
            if (Time.time >= _nextPuff)
            {
                if (_owner.GetVelocity().sqrMagnitude < 2.25f && !InventoryGui.IsVisible() && !Menu.IsVisible())
                {
                    ZDO zdo = Data(_owner);
                    zdo?.Set(PuffKey, (long)(ZNet.instance.GetTimeSeconds()*1000));
                    _nextPuff = Time.time+UnityEngine.Random.Range(16f,26f);
                }
                else _nextPuff = Time.time+2;
            }
        }
        private bool OwnsPipe() => _owner != null && _owner.GetInventory().ContainsItem(_pipe);
        private void Save(bool notify)
        {
            if (!OwnsPipe()) return;
            _pipe.m_customData[BowlKey] = _bowl.Save();
            if (notify || Time.unscaledTime >= _nextSave)
            { _nextSave = Time.unscaledTime+1; Data(_owner)?.Set(PatinaKey, Math.Min(5,(int)(_bowl.Smoked/300))); _owner.GetInventory().m_onChanged?.Invoke(); }
        }
        internal void Snuff(string message = null, bool save = true, bool removeEffect = true)
        {
            if (_pipe == null || _stopping) return;
            _stopping = true;
            try
            {
                if (save) Save(true);
                Player owner = _owner;
                string effect = Blend.All[_bowl.Blend].Effect;
                Data(owner)?.Set(EndKey, 0L);
                _pipe = null; _owner = null;
                if(removeEffect) owner?.GetSEMan().RemoveStatusEffect(effect.GetStableHashCode(), true);
                if (message != null) Say(owner, message);
            }
            finally { _stopping = false; }
        }
        internal void EffectStopped(Character character, int hash, bool exhausted)
        {
            if (character == _owner && _pipe != null && Blend.All[_bowl.Blend].Effect.GetStableHashCode() == hash)
            { if(exhausted)_bowl=_bowl.Burn(_bowl.Remaining); Snuff(null,true,false); }
        }
        internal void InventoryChanged(Inventory inventory)
        { if (_pipe != null && _owner != null && inventory == _owner.GetInventory() && !inventory.ContainsItem(_pipe)) Snuff(null, false); }
        internal bool Use(Player player, Inventory inventory, ItemDrop.ItemData item, bool fromGui)
        {
            if (!Items.IsPipe(item) && Items.BlendIndex(item) < 0) return false;
            if (player != Player.m_localPlayer || inventory != player.GetInventory() || !inventory.ContainsItem(item)) return true;
            if (player.IsDead() || player.InCutscene() || player.InBed()) return true;
            if (!Tobacco.Ready()) { Say(player, "Bob's Pipes needs Quad's Cigars with smoking API v1 (0.2.1). Update it, then restart."); return true; }
            if (!Items.IsPipe(item))
            {
                ItemDrop.ItemData pipe = inventory.GetAllItems().FirstOrDefault(i => Items.IsPipe(i) && Read(i).Empty);
                if (pipe == null) { Say(player, "You need an empty Carved Pipe. Put out and finish its bowl before changing tobacco."); return true; }
                Pack(player, pipe, item); return true;
            }
            if (_pipe == item) { Snuff("You tamp out the ember and save the bowl."); return true; }
            Bowl bowl = Read(item);
            if (bowl.Empty)
            {
                ItemDrop.ItemData tin = inventory.GetAllItems().FirstOrDefault(i => Items.BlendIndex(i) >= 0);
                if (tin == null) { Say(player, "Prepare pipe tobacco at Quad's Cigar Rolling Table, then use a tin to choose a blend."); return true; }
                Pack(player, item, tin); return true;
            }
            if (fromGui || !CanLight(player)) { Say(player, "Close the inventory and use your hotbar pipe with free hands, near a fire or holding a torch."); return true; }
            if (!NearFire(player)) { Say(player, "Bring your packed pipe to a burning campfire, hearth, or torch."); return true; }
            if (Rain.Value && EnvMan.IsWet() && !Cover.IsUnderRoof(player.GetCenterPoint()))
            { Say(player, "Find shelter before lighting your pipe in the rain."); return true; }
            if (!Tobacco.Exclusive(player, Blend.All[bowl.Blend].Effect)) { Say(player, "The tobacco mod is reloading; try again in a moment."); return true; }
            Snuff();
            _owner=player; _pipe=item; _bowl=bowl; _nextPuff=Time.time+UnityEngine.Random.Range(16f,24f);
            StatusEffect applied=player.GetSEMan().AddStatusEffect(Items.Effects[bowl.Blend],true);
            if (applied == null) { Snuff(); Say(player,"Your pipe could not be lit; the bowl is kept."); return true; }
            ZDO data=Data(player);
            data?.Set(PatinaKey,Math.Min(5,(int)(bowl.Smoked/300))); data?.Set(BlendKey,bowl.Blend); data?.Set(EndKey,(long)((ZNet.instance.GetTimeSeconds()+bowl.Remaining)*1000));
            data?.Set(PuffKey,(long)(ZNet.instance.GetTimeSeconds()*1000));
            Say(player,"You tamp and light the "+Blend.All[bowl.Blend].Name.ToLower()+". Use the pipe again to put it out.");
            return true;
        }
        private void Pack(Player player, ItemDrop.ItemData pipe, ItemDrop.ItemData tin)
        {
            Inventory inv=player.GetInventory(); int blend=Items.BlendIndex(tin);
            if (blend<0 || !inv.ContainsItem(pipe) || !inv.ContainsItem(tin) || !Read(pipe).Empty) return;
            Bowl bowl=Bowl.Pack(blend,Minutes.Value*60,Read(pipe).Smoked);
            if (!inv.RemoveItem(tin,1)) return;
            pipe.m_customData[BowlKey]=bowl.Save(); inv.m_onChanged?.Invoke();
            Say(player,"Packed "+Blend.All[blend].Name+". Use your pipe again near a fire to light it.");
        }
        private static bool CanLight(Player player) => ZNet.instance!=null && Data(player)!=null &&
            !player.InAttack()&&!player.IsBlocking()&&!player.IsSwimming()&&player.IsOnGround()&&
            (RightItem(player)==null || RightItem(player).m_shared.m_itemType==ItemDrop.ItemData.ItemType.Torch)&&
            !Menu.IsVisible()&&!Console.IsVisible()&&!TextInput.IsVisible()&&!Minimap.IsOpen()&&
            !(Chat.instance!=null&&Chat.instance.HasFocus())&&!Hud.IsPieceSelectionVisible();
        private static bool NearFire(Player player)
        {
            ItemDrop.ItemData held=RightItem(player);
            if (held!=null && held.m_shared.m_itemType==ItemDrop.ItemData.ItemType.Torch) return true;
            foreach (Collider collider in Physics.OverlapSphere(player.transform.position,2.5f))
            { Fireplace fire=collider.GetComponentInParent<Fireplace>(); if (fire!=null && fire.isActiveAndEnabled && fire.IsBurning()) return true; }
            return false;
        }
        internal static ZDO Data(Player player)
        { ZNetView view=player?.GetComponent<ZNetView>(); return view!=null&&view.IsValid()&&view.IsOwner()?view.GetZDO():null; }
        internal float RemainingFor(int blend) => _pipe!=null&&_bowl.Blend==blend?(float)_bowl.Remaining:0;
        private static void Say(Player player,string text)=>player?.Message(MessageHud.MessageType.TopLeft,text);
        private void OnDestroy()
        {
            Snuff(); Tobacco.Release(); _harmony?.UnpatchSelf();
            foreach(Player player in Player.GetAllPlayers()) PipeLook.Clear(player);
            Items.Unregister(); if(Instance==this)Instance=null;
        }
    }
    internal sealed class PipeEffect : SE_Stats
    {
        internal int BlendIndex;
        public override void Setup(Character character)
        {
            float strength=Plugin.Instance?.Strength.Value/100f??0;
            m_ttl=Plugin.Instance?.RemainingFor(BlendIndex)??0;
            m_staminaRegenMultiplier=BlendIndex==0?1+0.05f*strength:1;
            m_healthRegenMultiplier=BlendIndex==1?1+0.1f*strength:1;
            m_runStaminaDrainModifier=BlendIndex==2?-0.05f*strength:0;
            base.Setup(character);
        }
        public override void Stop() { base.Stop(); Plugin.Instance?.EffectStopped(m_character,NameHash(),m_ttl>0&&m_time>=m_ttl); }
    }
    [HarmonyPatch(typeof(Humanoid),"UseItem")]
    internal static class PipeUse
    {
        private static bool Prefix(Humanoid __instance,Inventory __0,ItemDrop.ItemData __1,bool __2)
        { return !(__instance is Player player && Plugin.Instance!=null && Plugin.Instance.Use(player,__0??player.GetInventory(),__1,__2)); }
    }
    [HarmonyPatch(typeof(Player),"OnDeath")]
    internal static class PipeDeath { private static void Prefix(Player __instance){if(__instance==Player.m_localPlayer)Plugin.Instance?.Snuff();} }
    [HarmonyPatch(typeof(Inventory),"Changed")]
    internal static class PipeMoved { private static void Postfix(Inventory __instance)=>Plugin.Instance?.InventoryChanged(__instance); }
    [HarmonyPatch(typeof(ItemDrop.ItemData),"GetTooltip",new[]{typeof(ItemDrop.ItemData),typeof(int),typeof(bool),typeof(float),typeof(int),typeof(bool)})]
    internal static class PipeTooltip
    {
        private static void Postfix(ItemDrop.ItemData __0,ref string __result)
        {
            if(!Items.IsPipe(__0))return;
            Bowl bowl=Plugin.Read(__0);
            __result+="\n\n"+(bowl.Empty?"Bowl: empty. Use a tobacco tin to pack it.":"Bowl: "+Blend.All[bowl.Blend].Name+" · "+Math.Ceiling(bowl.Remaining/60)+" min remaining.");
            __result+="\nUse: pack / light near fire / put out. The pipe is kept.";
            if(bowl.Smoked>=300)__result+="\nThe bowl has begun to take on a warm, seasoned patina.";
        }
    }
}
