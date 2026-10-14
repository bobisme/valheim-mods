using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WrongUniverse
{
    // A "wu" command for Claude Tools (found while the game runs, no reference) to check the gadgets in a live game:
    // wu list | give <prefab|all> | held, and `wu <feature command> ...` for each feature's own tests.
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
                string help="wu list | give <prefab|all> | held | icons"+string.Concat(Features.All.Where(f=>f.Command!=null).Select(f=>" | "+f.Command+" "+(f.Help??"")))+
                    ": check Wrong Universe. list shows the items, give puts one (or one of each) in the inventory, held says what's in hand.";
                found.GetType().GetMethod("RegisterCommand",BindingFlags.Public|BindingFlags.Static)?.Invoke(null,new object[]{Plugin.Name,"wu",help,
                    (Func<string[],Action<JObject>,Action<string>,IEnumerator>)Run});
                Plugin.Log("Claude Tools found: wu command added");
            }
            catch(Exception e){Plugin.Log("Could not add the wu command: "+e.Message);}
        }
        internal static void Stop()
        {
            try{_tools?.GetType().GetMethod("UnregisterAll",BindingFlags.Public|BindingFlags.Static)?.Invoke(null,new object[]{Plugin.Name});}catch(Exception){}
            _tools=null;
        }
        internal static JArray V(Vector3 v)=>new JArray(Math.Round(v.x,2),Math.Round(v.y,2),Math.Round(v.z,2));

        private static IEnumerator Run(string[] args,Action<JObject> output,Action<string> error)
        {
            Player me=Player.m_localPlayer;
            if(me==null){error("wu: no player");return null;}
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
                    string what=args.Length>2?args[2]:"all";
                    var defs=what=="all"?Things.All.ToList():Things.All.Where(d=>string.Equals(d.Prefab,what,StringComparison.OrdinalIgnoreCase)).ToList();
                    if(defs.Count==0){error("wu give: no such item (wu list)");return null;}
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
                case "icons":
                {
                    // Draws every item's icon again (now, in the world) and saves each to claude/shots/wu-icon-<prefab>.png.
                    string dir=System.IO.Path.Combine(Paths.BepInExRootPath,"claude","shots");
                    var res=new JObject();
                    foreach(ItemDef d in Things.All)
                    {
                        if(d.Built==null){res[d.Prefab]="not built";continue;}
                        Sprite s=Icon.MakeItem(d.Built,d.Attach,d.IconLook);
                        if(s==null)
                        {
                            string raw=System.IO.Path.Combine(dir,"wu-icon-"+d.Prefab+"-refused.png");
                            if(Icon.LastTexture!=null)System.IO.File.WriteAllBytes(raw,ImageConversion.EncodeToPNG(Icon.LastTexture));
                            res[d.Prefab]="refused: "+Icon.LastWhy;Icon.LastTexture=null;continue;
                        }
                        d.Shared.m_icons=new[]{s};
                        string file=System.IO.Path.Combine(dir,"wu-icon-"+d.Prefab+".png");
                        System.IO.File.WriteAllBytes(file,ImageConversion.EncodeToPNG(s.texture));
                        res[d.Prefab]=file;
                    }
                    output(res);
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
            if(f==null){error("wu: unknown command "+sub);return null;}
            return f.Test(args,output,error);
        }
    }
}
