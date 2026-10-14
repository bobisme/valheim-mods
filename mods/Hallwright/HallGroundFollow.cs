using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace Hallwright
{
    // When the plan a Hallwright hall dug its ground for is removed (BuildOrders' Plans window, its blueprint commands, or U here), the
    // ground goes back by itself, as Restore ground does: once the ground is loaded near you, and only if nothing of the hall is built on
    // it (then it stays dug, and Restore ground is there for after it's taken down). BuildOrders has no removal event, so its RemovePlan
    // is patched where the running copy is (found again after BuildOrders reloads).
    public sealed partial class Plugin
    {
        private Type _removalHooked;
        private float _removalCheck,_groundNextTry;
        private long _groundWorld;
        private bool _groundPending;
        private int _groundTries;

        private void HookPlanRemoval()
        {
            if(Time.unscaledTime<_removalCheck)return;
            _removalCheck=Time.unscaledTime+1;
            Type type=_planner.PluginType;
            if(type==null || type==_removalHooked)return;
            _removalHooked=type;
            MethodInfo remove=AccessTools.Method(type,"RemovePlan",new[]{typeof(string),typeof(bool)});
            if(remove==null || remove.ReturnType!=typeof(int)){Logger.LogWarning("This BuildOrders has no RemovePlan: removing a Hallwright plan in its Plans window won't put the ground back; use Restore ground.");return;}
            try{_harmony.Patch(remove,postfix:new HarmonyMethod(typeof(Plugin),nameof(AfterPlanRemoved)));}
            catch(Exception ex){Logger.LogWarning("Could not follow BuildOrders plan removal: "+ex.GetBaseException().Message);}
        }
        private static void AfterPlanRemoved(object[] __args){if(__args!=null && __args.Length>0 && __args[0] is string key)Instance?.HallPlanRemoved(key);}

        private GroundRecord ReadHallGroundRecord()
        {
            string path=HallGroundFile;
            if(!File.Exists(path) || new FileInfo(path).Length>2000000)return null;
            return JsonConvert.DeserializeObject<GroundRecord>(File.ReadAllText(path));
        }
        // The plan is gone: if it's the one the ground record was dug for, put the ground back soon (removed pieces take a moment to go).
        private void HallPlanRemoved(string key)
        {
            try
            {
                if(ZNet.instance==null || string.IsNullOrEmpty(key))return;
                GroundRecord record=ReadHallGroundRecord();
                if(record==null || record.Plan!=key || record.Removed)return;
                record.Removed=true;WriteHallGround(record);
                _groundWorld=ZNet.instance.GetWorldUID();_groundPending=true;_groundTries=0;_groundNextTry=Time.unscaledTime+1.5f;
            }
            catch(Exception ex){Logger.LogWarning("Hallwright ground record: "+ex.GetBaseException().Message);}
        }

        // Every frame: a record whose plan was removed is put back once it can be.
        private void FollowHallGround(Player player)
        {
            if(ZNet.instance==null || player==null)return;
            long world=ZNet.instance.GetWorldUID();
            if(world!=_groundWorld)
            {
                _groundWorld=world;_groundTries=0;_groundNextTry=Time.unscaledTime+5;
                try{_groundPending=ReadHallGroundRecord()?.Removed==true;}catch(Exception){_groundPending=false;}
            }
            if(!_groundPending || Time.unscaledTime<_groundNextTry)return;
            _groundNextTry=Time.unscaledTime+3;
            try
            {
                int skipped=RestoreHallGround();_groundPending=false;
                if(_active)QueueHallPreview();
                Say(skipped==0?"The hall's plan was removed: its ground is put back.":$"The hall's plan was removed: its ground is put back, except {skipped} points changed since.");
            }
            catch(Exception ex)
            {
                string why=ex.GetBaseException().Message;
                if(!File.Exists(HallGroundFile)){_groundPending=false;return;}
                if(why.StartsWith("Move closer",StringComparison.Ordinal))return;   // not loaded or out of reach: again when you're near
                if(++_groundTries<5)return;   // pieces being taken down, or terrain changing hands
                _groundPending=false;
                try{GroundRecord record=ReadHallGroundRecord();if(record!=null){record.Removed=false;WriteHallGround(record);}}catch(Exception){}
                Say(why.StartsWith("Clear existing buildings",StringComparison.Ordinal)
                    ?"The hall's plan was removed, but parts of it are built on the dug ground, so it stays dug. Take them down, then Restore ground in the Hallwright options."
                    :"The hall's plan was removed, but its ground couldn't be put back: "+why+" Restore ground is in the Hallwright options.");
            }
        }
    }
}
