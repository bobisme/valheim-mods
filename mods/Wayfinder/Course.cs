using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Wayfinder
{
    // The plotted course: waypoints laid on the big map (Ctrl+click), followed leg by leg. Each character keeps one course per
    // world; a cartography table holds a course to share, and the helmsman's course is shown to everyone aboard.
    internal static class Course
    {
        internal static readonly List<(double x,double z,string name)> Points=new List<(double,double,string)>();
        internal static int Next;
        internal static int Changes;                       // bumped on every change: the map and the ship's copy follow it
        private static readonly Stack<string> Undo=new Stack<string>();
        private const string KeyPrefix="bob_wayfinder_course:";
        internal const string ZdoKey="bob_wayfinder_course";
        private static string _loadedFor;
        private static float _nextFollow;

        internal static bool Active=>Points.Count>0&&Next<Points.Count;
        internal static IList<(double x,double z)> Flat=>Points.Select(p=>(p.x,p.z)).ToList();
        internal static Vector3 At(int i)=>new Vector3((float)Points[i].x,0,(float)Points[i].z);
        internal static string Label(int i)=>string.IsNullOrEmpty(Points[i].name)?"waypoint "+(i+1):Points[i].name;

        private static string Key()=>Plugin.World() is string w?KeyPrefix+w:null;
        // The character's saved course for this world, once they are in it.
        internal static void Tick()
        {
            Player me=Player.m_localPlayer;
            string key=Key();
            if(me==null||key==null){if(_loadedFor!=null){_loadedFor=null;Points.Clear();Next=0;Undo.Clear();Changes++;}return;}
            if(_loadedFor!=key+me.GetPlayerID())
            {
                _loadedFor=key+me.GetPlayerID();
                me.m_customData.TryGetValue(key,out string saved);
                Policy.Load(saved,Points,out Next);
                Undo.Clear();Changes++;
            }
            if(Time.time>=_nextFollow){_nextFollow=Time.time+0.25f;Follow(me);}
        }
        private static void Store()
        {
            Changes++;
            Player me=Player.m_localPlayer;
            string key=Key();
            if(me==null||key==null)return;
            if(Points.Count==0)me.m_customData.Remove(key);
            else me.m_customData[key]=Policy.Save(Points,Next);
        }
        private static void Remember(){Undo.Push(Policy.Save(Points,Next));while(Undo.Count>40){var keep=Undo.ToArray().Take(30).Reverse().ToArray();Undo.Clear();foreach(var k in keep)Undo.Push(k);}}

        internal static void Add(Vector3 at,string name=null)
        {
            if(Points.Count>=Policy.MaxWaypoints){Message("A course holds at most "+Policy.MaxWaypoints+" waypoints.");return;}
            Remember();
            double near=Minimap.instance!=null?NearRadius():60;
            int i=Policy.InsertAt(Flat,at.x,at.z,near);
            bool finished=Points.Count>0&&Next>=Points.Count;
            Points.Insert(i,(at.x,at.z,Policy.Clean(name??"")));
            if(finished)Next=i==Points.Count-1?i:Points.Count; // (a finished course takes a new end as its goal)
            else if(i<Next)Next++;
            Store();
            Sounds.Tick();
        }
        internal static bool RemoveNear(Vector3 at)
        {
            int i=Policy.Nearest(Flat,at.x,at.z,NearRadius());
            if(i<0)return false;
            Remember();
            Points.RemoveAt(i);
            if(i<Next)Next--;
            Next=Mathf.Clamp(Next,0,Points.Count);
            Store();
            Sounds.Tick();
            return true;
        }
        internal static void Clear()
        {
            if(Points.Count==0)return;
            Remember();Points.Clear();Next=0;Store();
            Message("Course cleared. (Ctrl+Z brings it back.)");
        }
        internal static void Reverse()
        {
            if(Points.Count<2)return;
            Remember();
            Points.Reverse();
            Next=0;
            Player me=Player.m_localPlayer;
            if(me!=null)Next=Policy.Advance(Flat,0,me.transform.position.x,me.transform.position.z,Plugin.ArriveRadius.Value);
            Store();
            Message("Course reversed: homeward.");
        }
        internal static void UndoLast()
        {
            if(Undo.Count==0)return;
            Policy.Load(Undo.Pop(),Points,out Next);
            Store();
        }
        internal static void Restart()
        {
            if(Points.Count==0)return;
            Remember();Next=0;Store();
        }
        internal static void Skip()
        {
            if(!Active)return;
            Remember();Next++;Store();
        }
        internal static void Replace(string saved,string from)
        {
            var copy=new List<(double,double,string)>();
            if(!Policy.Load(saved,copy,out _)||copy.Count==0)return;
            Remember();
            Points.Clear();Points.AddRange(copy);Next=0;
            Player me=Player.m_localPlayer;
            if(me!=null)Next=Policy.Advance(Flat,0,me.transform.position.x,me.transform.position.z,Plugin.ArriveRadius.Value);
            Store();
            Message($"Course taken from {from}: {Points.Count} waypoints, {Policy.Distance(Policy.Length(Flat,0))}.");
        }

        // A waypoint reached: on to the next.
        private static void Follow(Player me)
        {
            if(!Active)return;
            Vector3 p=me.transform.position;
            int next=Policy.Advance(Flat,Next,p.x,p.z,Plugin.ArriveRadius.Value);
            if(next==Next)return;
            Next=next;
            Store();
            if(Next>=Points.Count)
            {
                Message("Journey's end: "+Label(Points.Count-1)+".",MessageHud.MessageType.Center);
                Sounds.Arrive(true);
            }
            else
            {
                Vector3 to=At(Next)-p;
                Message($"{Label(Next-1)} reached. Next: {Label(Next)}, {Policy.Distance(to.magnitude)} {Policy.Point(Policy.Bearing(to.x,to.z))}.",MessageHud.MessageType.Center);
                Sounds.Arrive(false);
            }
        }

        internal static float NearRadius()
        {
            Minimap map=Minimap.instance;
            if(map==null)return 60;
            float zoom=map.m_mode==Minimap.MapMode.Large?map.LargeZoom:map.SmallZoom;
            return Mathf.Max(20,map.m_removeRadius*zoom*2f*1.4f);
        }
        internal static void Message(string text,MessageHud.MessageType type=MessageHud.MessageType.TopLeft)=>Player.m_localPlayer?.Message(type,text);
    }

    // Map clicks: Ctrl+click lays a waypoint (or one on a pin, named for it), Ctrl+right-click lifts the nearest.
    [HarmonyPatch(typeof(Minimap),"OnMapLeftClick")]
    internal static class PlotOnClick
    {
        private static bool Prefix(Minimap __instance)
        {
            if(!Plugin.Plotting())return true;
            Vector3 at=MapInput.Pointer(__instance);
            Minimap.PinData pin=MapInput.PinNear(__instance,at);
            if(pin!=null)Course.Add(pin.m_pos,pin.m_name);else Course.Add(at);
            return false;
        }
    }
    [HarmonyPatch(typeof(Minimap),"OnMapDblClick")]
    internal static class NoPinDialogWhilePlotting
    {
        private static bool Prefix()=>!Plugin.Plotting();
    }
    [HarmonyPatch(typeof(Minimap),"RemovePinUnderPointer")]
    internal static class LiftOnRightClick
    {
        private static bool Prefix(Minimap __instance)
        {
            if(!Plugin.Plotting())return true;
            Course.RemoveNear(MapInput.Pointer(__instance));
            return false;
        }
    }
    internal static class MapInput
    {
        private static readonly System.Reflection.MethodInfo ScreenToWorld=AccessTools.Method(typeof(Minimap),"ScreenToWorldPoint");
        private static readonly System.Reflection.MethodInfo Closest=AccessTools.Method(typeof(Minimap),"GetClosestPin",new[]{typeof(Vector3),typeof(float),typeof(bool)});
        internal static Vector3 Pointer(Minimap map)=>(Vector3)ScreenToWorld.Invoke(map,new object[]{(Vector3)ZInput.pointerPosition});
        internal static Minimap.PinData PinNear(Minimap map,Vector3 at)
        {
            if(Closest==null)return null;
            try{return (Minimap.PinData)Closest.Invoke(map,new object[]{at,Course.NearRadius()*0.6f,true});}catch{return null;}
        }
    }

    // A cartography table keeps a course beside its map: writing the map leaves your course there, reading takes it.
    [HarmonyPatch(typeof(MapTable),"Start")]
    internal static class TableCourseRpc
    {
        private static void Postfix(MapTable __instance)
        {
            ZNetView view=__instance.GetComponent<ZNetView>();
            if(view==null||!view.IsValid())return;
            view.Unregister(TableCourse.Rpc);
            view.Register<string>(TableCourse.Rpc,(sender,text)=>{if(view.IsOwner())view.GetZDO().Set(Course.ZdoKey,text??"");});
        }
    }
    [HarmonyPatch(typeof(MapTable),"OnWrite")]
    internal static class TableCourse
    {
        internal const string Rpc="bob_wayfinder_table_course";
        private static void Postfix(MapTable __instance,bool __result,ItemDrop.ItemData item)
        {
            if(!__result||item!=null||Course.Points.Count==0||!PrivateArea.CheckAccess(__instance.transform.position,0,false))return;
            ZNetView view=__instance.GetComponent<ZNetView>();
            if(view==null||!view.IsValid())return;
            view.InvokeRPC(Rpc,Policy.Save(Course.Points,0));
            Course.Message($"Your course is marked on the table too ({Course.Points.Count} waypoints).");
        }
    }
    [HarmonyPatch(typeof(MapTable),"OnRead",new[]{typeof(Switch),typeof(Humanoid),typeof(ItemDrop.ItemData),typeof(bool)})]
    internal static class TableCourseRead
    {
        private static void Postfix(MapTable __instance,ItemDrop.ItemData item,bool showMessage)
        {
            if(item!=null||!showMessage)return; // (writing reads first, quietly)
            ZNetView view=__instance.GetComponent<ZNetView>();
            string text=view!=null&&view.IsValid()?view.GetZDO().GetString(Course.ZdoKey):"";
            if(string.IsNullOrEmpty(text)||text==Policy.Save(Course.Points,0))return;
            Course.Replace(text,"the table");
        }
    }
}
