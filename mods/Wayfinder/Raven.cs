using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Wayfinder
{
    // Flóki's ravens: let one out of its cage and it climbs, circles, looks about, and flies for the nearest land it can see
    // (land not yet on your map first). Your map keeps a line the way it flew, and the land where it was going is drawn in.
    // If it sees no land it comes back to you. (Only you see the bird: it is your raven.)
    internal static class Raven
    {
        internal static readonly List<(Vector3 a,Vector3 b)> Lines=new List<(Vector3,Vector3)>();
        internal static Vector3? Heading=>_heading.HasValue&&Time.time<_headingUntil?_heading:null;
        private static Vector3? _heading;
        private static float _headingUntil;
        private enum Stage{Climb,Circle,Fly,Return}
        private static GameObject _bird;
        private static Animator _anim;
        private static Stage _stage;
        private static float _stageAt,_angle,_flown;
        private static Vector3 _from,_target,_centre;
        private static bool _found;
        private static float _nextCaw;
        private static readonly System.Reflection.MethodInfo Explored=AccessTools.Method(typeof(Minimap),"IsExplored");
        private static readonly System.Reflection.MethodInfo Explore=AccessTools.Method(typeof(Minimap),"Explore",new[]{typeof(Vector3),typeof(float)});
        internal static string Last="";

        internal static bool Release(Player me)
        {
            if(_bird!=null){me.Message(MessageHud.MessageType.Center,"Your raven is still aloft.");return false;}
            GameObject crow=ZNetScene.instance!=null?ZNetScene.instance.GetPrefab("Crow"):null;
            Transform model=crow!=null?crow.transform.Find("crow_anim"):null;
            if(model==null){me.Message(MessageHud.MessageType.Center,"The raven will not leave its cage.");return false;}
            _bird=new GameObject("WayfinderRaven");
            GameObject body=Object.Instantiate(model.gameObject,_bird.transform,false);
            body.transform.localPosition=Vector3.zero;body.transform.localScale=Vector3.one*1.1f; // (a raven, bigger than a crow)
            foreach(Renderer r in body.GetComponentsInChildren<Renderer>())r.material.color=new Color(0.25f,0.25f,0.3f);
            _anim=body.GetComponent<Animator>();
            Flap(true);
            _from=me.transform.position;
            _bird.transform.position=me.transform.position+Vector3.up*1.8f+me.transform.forward*0.6f;
            _bird.transform.rotation=Quaternion.LookRotation(me.transform.forward);
            _centre=_from;_stage=Stage.Climb;_stageAt=Time.time;_angle=Random.value*360;_flown=0;
            Caw();
            me.Message(MessageHud.MessageType.Center,"You let the raven go.");
            return true;
        }
        private static void Flap(bool on)
        {
            if(_anim==null)return;
            _anim.SetBool("flying",true);_anim.SetBool("flapping",on);
        }
        private static void Caw()
        {
            RandomFlyingBird crow=ZNetScene.instance?.GetPrefab("Crow")?.GetComponent<RandomFlyingBird>();
            if(crow!=null&&_bird!=null)crow.m_randomNoise.Create(_bird.transform.position,Quaternion.identity);
            _nextCaw=Time.time+Random.Range(3f,7f);
        }

        internal static void Tick()
        {
            if(_bird==null)return;
            Player me=Player.m_localPlayer;
            if(me==null){Stop();return;}
            float water=ZoneSystem.instance!=null?ZoneSystem.instance.m_waterLevel:30;
            float t=Time.time-_stageAt,dt=Time.deltaTime;
            Vector3 p=_bird.transform.position,goal;
            float speed;
            if(Time.time>_nextCaw)Caw();
            switch(_stage)
            {
                case Stage.Climb: // a rising spiral over you
                    _angle+=dt*70;
                    goal=_centre+Quaternion.Euler(0,_angle,0)*Vector3.forward*Mathf.Lerp(2,14,t/4)+Vector3.up*Mathf.Lerp(p.y-_centre.y,Mathf.Max(water+45-_centre.y,30),t/4);
                    speed=12;
                    if(t>4){_stage=Stage.Circle;_stageAt=Time.time;Flap(false);}
                    break;
                case Stage.Circle: // looking about
                    _angle+=dt*50;
                    goal=new Vector3(_centre.x,Mathf.Max(water+45,_centre.y+30),_centre.z)+Quaternion.Euler(0,_angle,0)*Vector3.forward*16;
                    speed=10;
                    if(t>3)
                    {
                        _found=Look(me.transform.position,water,out _target);
                        _stage=_found?Stage.Fly:Stage.Return;_stageAt=Time.time;Flap(true);
                        Vector3 d=_target-_from;
                        if(_found)
                        {
                            Last=$"flew {Policy.Point(Policy.Bearing(d.x,d.z))} toward land {Policy.Distance(d.magnitude)} off";
                            me.Message(MessageHud.MessageType.Center,$"The raven flies {Policy.Point(Policy.Bearing(d.x,d.z))}: land!");
                            Lines.Add((_from,_target));while(Lines.Count>6)Lines.RemoveAt(0);
                            _heading=_target;_headingUntil=Time.time+600;
                            if(Minimap.instance!=null&&Explore!=null)Explore.Invoke(Minimap.instance,new object[]{_target,70f});
                        }
                        else{Last="came back: no land in sight";me.Message(MessageHud.MessageType.Center,"The raven circles, and comes back to you. No land in sight.");}
                    }
                    break;
                case Stage.Fly:
                    goal=new Vector3(_target.x,Mathf.Max(water+40,Sounding.Ground(_target)+30),_target.z);
                    speed=22;
                    _flown+=speed*dt;
                    if(_flown>380||t>20){Stop();return;}
                    break;
                default: // back to the cage
                    goal=me.transform.position+Vector3.up*1.6f;
                    speed=Mathf.Lerp(8,16,Mathf.Clamp01((p-goal).magnitude/30));
                    if((p-goal).magnitude<1.2f||t>40){Stop();return;}
                    break;
            }
            Vector3 dir=goal-p;
            if(dir.sqrMagnitude>0.01f)
            {
                Quaternion want=Quaternion.LookRotation(dir.normalized);
                _bird.transform.rotation=Quaternion.RotateTowards(_bird.transform.rotation,want,dt*140);
                _bird.transform.position+=_bird.transform.forward*speed*dt+(dir.normalized-_bird.transform.forward)*speed*dt*0.3f;
            }
        }

        // The nearest land in sight from up high: rings outward from where you are; among the land in the first ring that has any
        // (and the next few hundred metres), land you have not seen yet comes first.
        private static bool Look(Vector3 from,float water,out Vector3 land)
        {
            land=from;
            if(WorldGenerator.instance==null)return false;
            bool atSea=WorldGenerator.instance.GetHeight(from.x,from.z)<water-0.5f;
            float start=atSea?40:150,range=Plugin.RavenRange.Value;
            float firstRing=-1;Vector3 any=Vector3.zero;bool haveAny=false;
            for(float r=start;r<=range;r+=40)
            {
                if(firstRing>0&&r>firstRing+400)break;
                int n=Mathf.Clamp((int)(2*Mathf.PI*r/40),24,360);
                for(int i=0;i<n;i++)
                {
                    float a=i*Mathf.PI*2/n;
                    Vector3 p=from+new Vector3(Mathf.Sin(a)*r,0,Mathf.Cos(a)*r);
                    if(WorldGenerator.instance.GetHeight(p.x,p.z)<water+1.5f)continue;
                    if(!atSea&&IsExplored(p))continue; // (from land: only what is new)
                    if(firstRing<0)firstRing=r;
                    if(!haveAny){any=p;haveAny=true;}
                    if(!IsExplored(p)){land=p;return true;}
                }
            }
            if(haveAny){land=any;return true;}
            return false;
        }
        private static bool IsExplored(Vector3 p)
        {
            if(Minimap.instance==null||Explored==null)return false;
            try{return (bool)Explored.Invoke(Minimap.instance,new object[]{p});}catch{return false;}
        }

        internal static void Stop(){if(_bird!=null)Object.Destroy(_bird);_bird=null;_anim=null;}
    }

    // Using a caged raven (from the hotbar or the inventory) lets it go.
    [HarmonyPatch(typeof(Humanoid),nameof(Humanoid.UseItem))]
    internal static class LetRavenGo
    {
        private static bool Prefix(Humanoid __instance,Inventory inventory,ItemDrop.ItemData item)
        {
            if(!Items.Is(item,Items.Raven)||!(__instance is Player me)||me!=Player.m_localPlayer)return true;
            if(Raven.Release(me))(inventory??me.GetInventory()).RemoveItem(item,1);
            return false;
        }
    }
}
