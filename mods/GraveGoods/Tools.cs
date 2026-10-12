using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Random=System.Random;

namespace GraveGoods
{
    // A "gravegoods" command for Claude Tools, found while the game runs (no reference), to check grave goods in a live game.
    internal static class Tools
    {
        private const string ClaudeToolsGuid="com.quad.claudetools";
        private const string TestKey="bob_gravegoods_test",BlessKey="bob_gravegoods_blessed";
        private static BaseUnityPlugin _tools;
        private static float _nextCheck;

        internal static void Tick()
        {
            if(Time.unscaledTime<_nextCheck)return;
            _nextCheck=Time.unscaledTime+5;
            BaseUnityPlugin found=BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(ClaudeToolsGuid,out PluginInfo info)?info.Instance:null;
            if(found==_tools)return;
            _tools=found;
            if(found==null)return;
            try
            {
                found.GetType().GetMethod("RegisterCommand",BindingFlags.Public|BindingFlags.Static)?.Invoke(null,new object[]{Plugin.Name,"gravegoods",
                    "gravegoods info | pool [tier] | give <plain|fine|runed|heirloom|saga> [tier] [prefab] [count] | bless <rarity> [tier] | take | sim [chests] [biome tier] | chests [radius] | chest [count] [tier] [rarity|-] | chest natural | drop <rarity> [tier] | remove | hover <container|player> <x> <y> | hover off: check Grave Goods. give puts test grave goods in the player's inventory, bless puts test powers on what they wear, and take takes both back; sim rolls chests without placing anything; chests lists treasure chests near the player and whether they were rolled; chest places a test treasure chest ahead of the player full of finds (natural: left to be rolled as a found chest is), drop puts a test find on the ground ahead, and remove takes test chests and dropped test finds away.",
                    (Func<string[],Action<JObject>,Action<string>,IEnumerator>)Run});
                Plugin.Log("Claude Tools found: gravegoods command added");
            }
            catch(Exception e){Plugin.Log("Could not add the gravegoods command: "+e.Message);}
        }
        internal static void Stop()
        {
            try{_tools?.GetType().GetMethod("UnregisterAll",BindingFlags.Public|BindingFlags.Static)?.Invoke(null,new object[]{Plugin.Name});}catch(Exception){}
            _tools=null;
        }
        private static int Int(string[] a,int i,int d)=>a.Length>i&&int.TryParse(a[i],out int v)?v:d;
        private static JObject Describe(ItemDrop.ItemData item)
        {
            Goods g=Effects.Of(item);
            var o=new JObject{["prefab"]=item.m_dropPrefab!=null?item.m_dropPrefab.name:"",["quality"]=item.m_quality,["equipped"]=item.m_equipped};
            if(g==null)return o;
            o["title"]=Policy.Title(g,Looks.BaseName(item));o["rarity"]=Policy.RarityNames[(int)g.Rarity];o["tier"]=g.Tier;
            o["powers"]=new JArray(g.Affixes.Select(Policy.Line));
            if(g.Saga!="")o["lore"]=g.Saga;
            HitData.DamageTypes d=item.GetDamage();
            if(d.GetTotalDamage()>0)o["damage"]=Math.Round(d.GetTotalDamage(),1);
            if(item.GetArmor()>0)o["armor"]=Math.Round(item.GetArmor(),1);
            o["durability"]=$"{Math.Round(item.m_durability)}/{Math.Round(item.GetMaxDurability())}";
            return o;
        }

        private static IEnumerator Later(Action<JObject> output)
        {
            yield return new WaitForSecondsRealtime(0.7f);
            output(Pinned.State());
        }
        private static IEnumerator Run(string[] args,Action<JObject> output,Action<string> error)
        {
            Player me=Player.m_localPlayer;
            if(me==null){error("gravegoods: no player");return null;}
            string sub=args.Length>1?args[1]:"info";
            switch(sub)
            {
                case "info":
                {
                    Effects.Totals t=Effects.Worn();
                    output(new JObject{
                        ["equipped"]=new JArray(me.GetInventory().GetEquippedItems().Select(i=>i.m_dropPrefab!=null?i.m_dropPrefab.name:i.m_shared.m_name)),
                        ["carried"]=new JArray(me.GetInventory().GetAllItems().Where(i=>Effects.Of(i)!=null).Select(Describe)),
                        ["worn"]=new JObject(t.Stat.Select(kv=>new JProperty(kv.Key,Math.Round(kv.Value,3)))),
                        ["skills"]=new JObject(t.Skill.Select(kv=>new JProperty(kv.Key.ToString(),kv.Value))),
                        ["maxHealth"]=Math.Round(me.GetMaxHealth(),1),["maxStamina"]=Math.Round(me.GetMaxStamina(),1),["carryWeight"]=Math.Round(me.GetMaxCarryWeight(),1),
                    });
                    break;
                }
                case "pool":
                {
                    int tier=Int(args,2,0);
                    var all=Pool.All.Where(e=>tier==0||e.Tier==tier);
                    output(new JObject{["count"]=all.Count(),["byTier"]=new JObject(Enumerable.Range(1,Policy.MaxTier).Select(t=>new JProperty(t.ToString(),Pool.All.Count(e=>e.Tier==t)))),
                        ["items"]=tier==0?null:new JArray(all.Select(e=>e.Prefab.name+" ("+e.Slot+")"))});
                    break;
                }
                case "give":
                {
                    int r=Array.FindIndex(Policy.RarityNames,n=>n.Equals(args.Length>2?args[2]:"",StringComparison.OrdinalIgnoreCase));
                    if(r<0){error("gravegoods give: plain, fine, runed, heirloom or saga");break;}
                    int tier=Int(args,3,Loot.TierAt(me.transform.position));
                    string prefab=args.Length>4&&args[4]!="-"?args[4]:null;
                    int count=Math.Max(1,Math.Min(20,Int(args,5,1)));
                    var rng=new Random();
                    var made=new JArray();
                    for(int n=0;n<count;n++)
                    {
                        Pool.Entry e=prefab!=null?Pool.All.FirstOrDefault(x=>x.Prefab.name.Equals(prefab,StringComparison.OrdinalIgnoreCase)):Pool.Pick(tier,rng);
                        if(e==null){error("gravegoods give: no such weapon or gear "+prefab);break;}
                        ItemDrop.ItemData item=Loot.Make(e,(Rarity)r,tier,rng);
                        item.m_customData[TestKey]="1";
                        if(!me.GetInventory().AddItem(item)){error("gravegoods give: no room");break;}
                        made.Add(Describe(item));
                    }
                    output(new JObject{["given"]=made});
                    break;
                }
                case "bless":
                {
                    // gravegoods bless <rarity> [tier]: put test powers on what the player wears and wields now (take strips them)
                    int r=Array.FindIndex(Policy.RarityNames,n=>n.Equals(args.Length>2?args[2]:"",StringComparison.OrdinalIgnoreCase));
                    if(r<0){error("gravegoods bless: plain, fine, runed, heirloom or saga");break;}
                    int tier=Int(args,3,Loot.TierAt(me.transform.position));
                    var rng=new Random();var done=new JArray();
                    foreach(ItemDrop.ItemData item in me.GetInventory().GetEquippedItems())
                    {
                        Slot? slot=Pool.SlotOf(item.m_shared);
                        if(slot==null||Effects.Of(item)!=null)continue;
                        Goods g=Policy.Roll((Rarity)r,tier,slot.Value,slot==Slot.Weapon?item.m_shared.m_skillType.ToString():"",Pool.ArmorClass(item.m_shared),rng);
                        item.m_customData[Policy.Key]=Policy.Save(g);item.m_customData[BlessKey]="1";
                        done.Add(Describe(item));
                    }
                    output(new JObject{["blessed"]=done,["maxHealth"]=Math.Round(me.GetMaxHealth(),1)});
                    break;
                }
                case "take":
                {
                    int stripped=0;
                    foreach(ItemDrop.ItemData i in me.GetInventory().GetAllItems().Where(i=>i.m_customData!=null&&i.m_customData.ContainsKey(BlessKey)).ToList())
                    {
                        i.m_customData.Remove(BlessKey);i.m_customData.Remove(Policy.Key);
                        i.m_durability=Math.Min(i.m_durability,i.GetMaxDurability());
                        stripped++;
                    }
                    var test=me.GetInventory().GetAllItems().Where(i=>i.m_customData!=null&&i.m_customData.ContainsKey(TestKey)).ToList();
                    foreach(ItemDrop.ItemData i in test){if(i.m_equipped)me.UnequipItem(i,false);me.GetInventory().RemoveItem(i);}
                    output(new JObject{["taken"]=test.Count,["stripped"]=stripped});
                    break;
                }
                case "sim":
                {
                    // gravegoods sim [chests] [tier]: roll that many chests (nothing placed) and count what comes out
                    int n=Math.Max(1,Math.Min(100000,Int(args,2,1000))),tier=Int(args,3,Loot.TierAt(me.transform.position));
                    var rng=new Random();
                    var counts=new int[5];int finds=0;var sagas=new JArray();var tiers=new int[Policy.MaxTier+1];
                    for(int i=0;i<n;i++)
                    {
                        if(rng.NextDouble()>=Plugin.ChestChance.Value)continue;
                        ItemDrop.ItemData item=Loot.Roll(tier,rng);
                        Goods g=Effects.Of(item);
                        if(g==null)continue;
                        finds++;counts[(int)g.Rarity]++;tiers[g.Tier]++;
                        if(g.Rarity==Rarity.Saga&&sagas.Count<8)sagas.Add(g.Name+" ("+item.m_dropPrefab.name+")");
                    }
                    output(new JObject{["chests"]=n,["finds"]=finds,["byRarity"]=new JObject(Policy.RarityNames.Select((s,i)=>new JProperty(s,counts[i]))),
                        ["byTier"]=new JObject(Enumerable.Range(1,Policy.MaxTier).Where(t=>tiers[t]>0).Select(t=>new JProperty(t.ToString(),tiers[t]))),["sagas"]=sagas});
                    break;
                }
                case "chests":
                {
                    float radius=Int(args,2,60);
                    var list=new JArray();
                    foreach(Container c in UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None))
                    {
                        ZNetView nv=c.GetComponentInParent<ZNetView>();
                        if(nv==null||!nv.IsValid()||!Loot.IsTreasure(Utils.GetPrefabName(nv.gameObject))||Vector3.Distance(c.transform.position,me.transform.position)>radius)continue;
                        list.Add(new JObject{["chest"]=Utils.GetPrefabName(nv.gameObject),["at"]=new JArray(Math.Round(c.transform.position.x),Math.Round(c.transform.position.y),Math.Round(c.transform.position.z)),
                            ["rolled"]=nv.GetZDO().GetBool(Loot.RolledKey),["owner"]=nv.IsOwner(),["goods"]=new JArray(c.GetInventory().GetAllItems().Where(i=>Effects.Of(i)!=null).Select(Describe))});
                    }
                    output(new JObject{["chests"]=list});
                    break;
                }
                case "chest":
                {
                    // gravegoods chest [count] [tier] [rarity|-]: a test treasure chest ahead of the player, holding that many finds
                    int n=Math.Max(0,Math.Min(30,Int(args,2,6))),tier=Int(args,3,Loot.TierAt(me.transform.position));
                    int r=args.Length>4?Array.FindIndex(Policy.RarityNames,x=>x.Equals(args[4],StringComparison.OrdinalIgnoreCase)):-1;
                    GameObject prefab=ZNetScene.instance.GetPrefab("TreasureChest_forestcrypt");
                    if(prefab==null){error("gravegoods chest: no TreasureChest_forestcrypt");break;}
                    Vector3 at=me.transform.position+me.transform.forward*2.2f;
                    if(ZoneSystem.instance.GetSolidHeight(at,out float h))at.y=h;
                    GameObject go=UnityEngine.Object.Instantiate(prefab,at,Quaternion.LookRotation(-me.transform.forward));
                    ZNetView nv=go.GetComponent<ZNetView>();
                    nv.GetZDO().Set(TestKey,true);
                    if(args.Length>2&&args[2]=="natural"){output(new JObject{["at"]=new JArray(Math.Round(at.x,1),Math.Round(at.y,1),Math.Round(at.z,1)),["natural"]=true});break;} // (left for the chest roll)
                    nv.GetZDO().Set(Loot.RolledKey,true);
                    Container c=go.GetComponentInChildren<Container>();
                    var rng=new Random();
                    for(int i=0;i<n;i++)
                    {
                        ItemDrop.ItemData item=r<0?Loot.Roll(tier,rng):Loot.Make(Pool.Pick(Policy.ChestTier(tier,rng),rng),(Rarity)r,tier,rng);
                        if(item!=null)c.GetInventory().AddItem(item);
                    }
                    output(new JObject{["at"]=new JArray(Math.Round(at.x,1),Math.Round(at.y,1),Math.Round(at.z,1)),["goods"]=new JArray(c.GetInventory().GetAllItems().Where(i=>Effects.Of(i)!=null).Select(Describe))});
                    break;
                }
                case "drop":
                {
                    // gravegoods drop <rarity> [tier]: a test find on the ground ahead of the player (remove takes it away)
                    int r=Array.FindIndex(Policy.RarityNames,n=>n.Equals(args.Length>2?args[2]:"",StringComparison.OrdinalIgnoreCase));
                    if(r<0){error("gravegoods drop: plain, fine, runed, heirloom or saga");break;}
                    var rng=new Random();int tier=Int(args,3,Loot.TierAt(me.transform.position));
                    ItemDrop.ItemData item=Loot.Make(Pool.Pick(tier,rng),(Rarity)r,tier,rng);
                    item.m_customData[TestKey]="1";
                    ItemDrop d=ItemDrop.DropItem(item,1,me.transform.position+me.transform.forward*1.8f+Vector3.up*0.5f,Quaternion.identity);
                    output(new JObject{["dropped"]=Describe(d.m_itemData)});
                    break;
                }
                case "remove":
                {
                    int count=0;
                    foreach(ItemDrop d in UnityEngine.Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None))
                        if(d!=null&&d.m_itemData?.m_customData!=null&&d.m_itemData.m_customData.ContainsKey(TestKey)){ZNetView v=d.GetComponent<ZNetView>();if(v!=null&&v.IsValid()){v.ClaimOwnership();ZNetScene.instance.Destroy(d.gameObject);count++;}}
                    foreach(ZNetView v in UnityEngine.Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None))
                        if(v!=null&&v.IsValid()&&v.GetZDO().GetBool(TestKey)){v.ClaimOwnership();ZNetScene.instance.Destroy(v.gameObject);count++;}
                    output(new JObject{["removed"]=count});
                    break;
                }
                case "hover":
                {
                    // gravegoods hover <container|player> <x> <y>: show that slot's tooltip and hold it there for a screenshot
                    // (until "gravegoods hover off" or the inventory closes)
                    if(args.Length>2&&args[2]=="off"){Pinned.Off();output(new JObject{["hover"]="off"});break;}
                    InventoryGui gui=InventoryGui.instance;
                    if(gui==null||!InventoryGui.IsVisible()){error("gravegoods hover: the inventory isn't open");break;}
                    InventoryGrid grid=args.Length>2&&args[2]=="player"?gui.m_playerGrid:HarmonyLib.AccessTools.FieldRefAccess<InventoryGui,InventoryGrid>("m_containerGrid")(gui);
                    int x=Int(args,3,0),y=Int(args,4,0);
                    InventoryElement el=grid.GetComponentsInChildren<InventoryElement>().FirstOrDefault(e=>e.Position.x==x&&e.Position.y==y);
                    if(el==null||el.m_tooltip==null){error("gravegoods hover: no slot there");break;}
                    ItemDrop.ItemData item=HarmonyLib.AccessTools.FieldRefAccess<InventoryGrid,Inventory>("m_inventory")(grid)?.GetItemAt(x,y);
                    if(item==null){error("gravegoods hover: that slot is empty");break;}
                    HarmonyLib.AccessTools.Method(typeof(InventoryGrid),"CreateItemTooltip").Invoke(grid,new object[]{item,el.m_tooltip});
                    Pinned.On(el);
                    output(new JObject{["topic"]=el.m_tooltip.m_topic});
                    return Later(output);
                }
                default:error("gravegoods: unknown "+sub);break;
            }
            return null;
        }
    
    }

    // A copy of a slot's tooltip, held up for a screenshot (the game's own goes away when the mouse isn't over its slot).
    internal static class Pinned
    {
        private static GameObject _shown;
        internal static void On(InventoryElement e)
        {
            Off();
            UITooltip tip=e.m_tooltip;
            Canvas canvas=e.GetComponentInParent<Canvas>();
            if(tip.m_tooltipPrefab==null||canvas==null)return;
            _shown=UnityEngine.Object.Instantiate(tip.m_tooltipPrefab,canvas.rootCanvas.transform);
            _shown.name="GraveGoodsPreview";
            Transform text=Utils.FindChild(_shown.transform,"Text"),topic=Utils.FindChild(_shown.transform,"Topic");
            if(text!=null)text.GetComponent<TMPro.TMP_Text>().text=Localization.instance.Localize(tip.m_text);
            if(topic!=null)topic.GetComponent<TMPro.TMP_Text>().text=Localization.instance.Localize(tip.m_topic);
            var corners=new Vector3[4];((RectTransform)e.transform).GetWorldCorners(corners);
            _shown.transform.position=corners[2];
            _shown.SetActive(true);
            Utils.ClampUIToScreen(_shown.transform.GetChild(0).transform as RectTransform);
        }
        internal static void Off(){if(_shown!=null)UnityEngine.Object.Destroy(_shown);_shown=null;}
        internal static JObject State()=>new JObject{["shown"]=_shown!=null&&_shown.activeInHierarchy,["at"]=_shown!=null?_shown.transform.position.ToString():""};
    }
}
