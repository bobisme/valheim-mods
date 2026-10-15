using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Godforged
{
    // A "gf" command for Claude Tools (found while the game runs, no reference) to check the relics in a live game:
    // gf list | give <prefab|all> | held, and `gf <feature command> ...` for each feature's own tests.
    internal static class Tools
    {
        private const string ClaudeToolsGuid="com.quad.claudetools";
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
                string help="gf list | give <prefab|all> | repair | held | icons | look <prefab> [yaw pitch child] | bones"+string.Concat(Features.All.Where(f=>f.Command!=null).Select(f=>" | "+f.Command+" "+(f.Help??"")))+
                    ": check Godforged. list shows the items, give puts one (or one of each) in the inventory, held says what's in hand.";
                found.GetType().GetMethod("RegisterCommand",BindingFlags.Public|BindingFlags.Static)?.Invoke(null,new object[]{Plugin.Name,"gf",help,
                    (Func<string[],Action<JObject>,Action<string>,IEnumerator>)Run});
                Plugin.Log("Claude Tools found: gf command added");
            }
            catch(Exception e){Plugin.Log("Could not add the gf command: "+e.Message);}
        }
        internal static void Stop()
        {
            try{_tools?.GetType().GetMethod("UnregisterAll",BindingFlags.Public|BindingFlags.Static)?.Invoke(null,new object[]{Plugin.Name});}catch(Exception){}
            _tools=null;
        }
        // The game's own test: single player or the host, or devcommands on and a server admin (as Jarldom's tools).
        private static bool Cheats()=>ZNet.instance!=null&&(ZNet.instance.IsServer()||global::Console.instance!=null&&global::Console.instance.IsCheatsEnabled());
        internal static JArray V(Vector3 v)=>new JArray(Math.Round(v.x,2),Math.Round(v.y,2),Math.Round(v.z,2));

        private static IEnumerator Run(string[] args,Action<JObject> output,Action<string> error)
        {
            Player me=Player.m_localPlayer;
            if(me==null){error("gf: no player");return null;}
            string sub=args.Length>1?args[1]:"list";
            switch(sub)
            {
                case "list":
                    output(new JObject{["items"]=new JArray(Things.All.Select(d=>new JObject{["prefab"]=d.Prefab,["name"]=d.Name,["built"]=d.Built!=null,
                        ["type"]=d.Shared?.m_itemType.ToString(),["recipe"]=d.Recipe!=null&&ObjectDB.instance!=null&&ObjectDB.instance.m_recipes.Contains(d.Recipe),
                        ["icon"]=d.Shared?.m_icons!=null&&d.Shared.m_icons.Length>0&&d.Shared.m_icons[0]!=null}))});
                    return null;
                case "give":
                {
                    if(!Cheats()){error("gf give makes items: only in single player, for the host, or for a server admin with devcommands on");return null;}
                    string what=args.Length>2?args[2]:"all";
                    var defs=what=="all"?Things.All.ToList():Things.All.Where(d=>string.Equals(d.Prefab,what,StringComparison.OrdinalIgnoreCase)).ToList();
                    if(defs.Count==0){error("gf give: no such item (gf list)");return null;}
                    var given=new JArray();
                    foreach(ItemDef d in defs)
                    {
                        if(d.Built==null)continue;
                        ItemDrop.ItemData item=me.GetInventory().AddItem(d.Built,1)==true?me.GetInventory().GetAllItems().LastOrDefault(d.Is):null;
                        given.Add(d.Prefab+(item!=null?"":" (no room)"));
                    }
                    output(new JObject{["given"]=given});
                    return null;
                }
                case "repair":
                {
                    // Every relic in the bag as good as new (those made before 0.2.13 started at the copied item's durability).
                    if(!Cheats()){error("gf repair mends items: only in single player, for the host, or for a server admin with devcommands on");return null;}
                    var mended=new JArray();
                    foreach(ItemDrop.ItemData i in me.GetInventory().GetAllItems())
                        if(Things.Of(i)!=null&&i.m_durability<i.GetMaxDurability()){i.m_durability=i.GetMaxDurability();mended.Add(i.m_shared.m_name);}
                    output(new JObject{["mended"]=mended});
                    return null;
                }
                case "icons":
                {
                    // Draws every item's icon again (now, in the world) and saves each to claude/shots/gf-icon-<prefab>.png.
                    string dir=System.IO.Path.Combine(Paths.BepInExRootPath,"claude","shots");
                    var res=new JObject();
                    foreach(ItemDef d in Things.All)
                    {
                        if(d.Built==null){res[d.Prefab]="not built";continue;}
                        Sprite s=Icon.MakeItem(d.Built,d.Attach,d.IconLook);
                        if(s==null)
                        {
                            string raw=System.IO.Path.Combine(dir,"gf-icon-"+d.Prefab+"-refused.png");
                            if(Icon.LastTexture!=null)System.IO.File.WriteAllBytes(raw,ImageConversion.EncodeToPNG(Icon.LastTexture));
                            res[d.Prefab]="refused: "+Icon.LastWhy;Icon.LastTexture=null;continue;
                        }
                        d.Shared.m_icons=new[]{s};
                        string file=System.IO.Path.Combine(dir,"gf-icon-"+d.Prefab+".png");
                        System.IO.File.WriteAllBytes(file,ImageConversion.EncodeToPNG(s.texture));
                        res[d.Prefab]=file;
                    }
                    output(res);
                    return null;
                }
                case "look":
                {
                    // Studio pictures of a model: gf look <item prefab|scene prefab> [yaw] [pitch] [child]
                    string what=args.Length>2?args[2]:"";
                    float yaw=args.Length>3&&float.TryParse(args[3],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float y)?y:35f;
                    float pitch=args.Length>4&&float.TryParse(args[4],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float pt)?pt:20f;
                    ItemDef d=Things.Find(what);
                    GameObject prefab=d!=null?d.Built:ZNetScene.instance?.GetPrefab(what);
                    if(prefab==null){error("gf look: no such prefab");return null;}
                    string child=args.Length>5?args[5]:d?.Attach;
                    string file=System.IO.Path.Combine(Paths.BepInExRootPath,"claude","shots","gf-look-"+what+".png");
                    string done=Stage.Shoot(prefab,child,file,768,yaw,pitch);
                    if(done==null)error("gf look: nothing to draw");else output(new JObject{["file"]=done});
                    return null;
                }
                case "mat":
                {
                    // What a palette slot's material really is: shader, keywords, colours (gf mat glow_red emit_red white)
                    var o=new JObject();
                    for(int i=2;i<args.Length;i++)
                    {
                        Material m=Look.Slot(args[i]);
                        var props=new JObject();
                        foreach(string c in new[]{"_Color","_TintColor","_EmissionColor"})if(m.HasProperty(c))props[c]=m.GetColor(c).ToString();
                        foreach(string fl in new[]{"_Metallic","_Glossiness","_SrcBlend","_DstBlend","_ZWrite","_Cull"})if(m.HasProperty(fl))props[fl]=m.GetFloat(fl);
                        o[args[i]]=new JObject{["shader"]=m.shader.name,["queue"]=m.renderQueue,["keywords"]=string.Join(" ",m.shaderKeywords),["props"]=props,
                            ["textures"]=string.Join(" ",m.GetTexturePropertyNames())};
                    }
                    output(o);
                    return null;
                }
                case "grip":
                {
                    // Tune how an item sits in the hand, live: gf grip <prefab> ex ey ez [px py pz]. Sets its equipoffset (for the next
                    // equip) and turns the copy in the player's hand now. Put the numbers found into the item's HandEuler/HandPosition.
                    ItemDef d=args.Length>2?Things.Find(args[2]):null;
                    if(d==null||d.Built==null){error("gf grip <prefab> ex ey ez [px py pz]");return null;}
                    var inv=System.Globalization.CultureInfo.InvariantCulture;
                    float A(int i,float dflt)=>args.Length>i?float.Parse(args[i],inv):dflt;
                    var e=new Vector3(A(3,0),A(4,0),A(5,0));var pos=new Vector3(A(6,0),A(7,0),A(8,0));
                    Transform grip=d.Built.transform.Find("equipoffset");
                    if(grip==null){grip=new GameObject("equipoffset").transform;grip.SetParent(d.Built.transform,false);}
                    grip.localPosition=pos;grip.localRotation=Quaternion.Euler(e);
                    d.HandEuler=e;d.HandPosition=pos;
                    var vis=me.GetComponent<VisEquipment>();
                    int turned=0;
                    foreach(string field in new[]{"m_rightItemInstance","m_leftItemInstance"})
                    {
                        var inst=HarmonyLib.Traverse.Create(vis).Field(field).GetValue<GameObject>();
                        if(inst==null||!inst.name.StartsWith("attach"))continue;
                        if(Things.Of(me.GetCurrentWeapon())!=d)continue;
                        inst.transform.localPosition=pos;inst.transform.localRotation=Quaternion.Euler(e);turned++;
                    }
                    output(new JObject{["euler"]=V(e),["position"]=V(pos),["turnedInHand"]=turned});
                    return null;
                }
                case "bones":
                {
                    // Where the hands, head and back are (for aiming `view world` at what the player holds and wears), and which way the player faces.
                    var o=new JObject{["yaw"]=Math.Round(me.transform.eulerAngles.y,1),["position"]=V(me.transform.position)};
                    foreach(string bone in new[]{"RightHand","LeftHand","Head","Spine2","Hips"})
                    {
                        Transform t=Utils.FindChild(me.transform,bone);
                        if(t!=null)o[bone]=V(t.position);
                    }
                    output(o);
                    return null;
                }
                case "held":
                {
                    ItemDef d=Things.Held(me,out ItemDrop.ItemData item);
                    output(new JObject{["held"]=item?.m_shared.m_name,["ours"]=d?.Prefab,["equipped"]=new JArray(me.GetInventory().GetEquippedItems().Select(i=>i.m_shared.m_name))});
                    return null;
                }
            }
            Feature f=Features.All.FirstOrDefault(x=>x.Command==sub);
            if(f==null){error("gf: unknown command "+sub);return null;}
            return f.Test(args,output,error);
        }
    }
}
