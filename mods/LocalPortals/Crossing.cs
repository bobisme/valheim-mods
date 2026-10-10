using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace LocalPortals
{
    // Stepping through: when the local player's body passes into a portal's surface, they come out of the linked portal's
    // surface at the same spot, moving and looking the same way relative to it, with the camera carried along.
    internal static class Crossing
    {
        private static readonly Dictionary<LocalPortal,Vector3> Last=new Dictionary<LocalPortal,Vector3>();
        private static readonly AccessTools.FieldRef<GameCamera,Vector3> CameraPlayerPos=AccessTools.FieldRefAccess<GameCamera,Vector3>("m_playerPos");
        private static readonly AccessTools.FieldRef<GameCamera,Vector3> CameraPlayerVel=AccessTools.FieldRefAccess<GameCamera,Vector3>("m_playerVel");
        private static readonly AccessTools.FieldRef<Character,float> MaxAirAltitude=AccessTools.FieldRefAccess<Character,float>("m_maxAirAltitude");
        private static readonly System.Reflection.FieldInfo CartList=AccessTools.Field(typeof(Vagon),"m_instances");
        private static IEnumerable<Vagon> Carts()=>CartList?.GetValue(null) as List<Vagon>??new List<Vagon>();
        private static float _blockedMessage;
        internal static int Count;      // trips through, this session

        internal static void Tick()
        {
            Player me=Player.m_localPlayer;
            if(me==null||me.IsDead()||me.IsAttached()||me.IsTeleporting()||me.InIntro())
            {
                Last.Clear();
                foreach(LocalPortal p in LocalPortal.Live)if(p!=null)Open(p,false);
                return;
            }
            Vector3 body=me.transform.position+Vector3.up*(float)Policy.BodyHeight;
            LocalPortal through=null;
            foreach(LocalPortal p in LocalPortal.Live)
            {
                if(p==null)continue;
                if((p.transform.position-body).sqrMagnitude>36){Last.Remove(p);Open(p,false);continue;}
                Vector3 now=p.transform.InverseTransformPoint(body);
                // The board behind the glass gives way to a player in front, so they can step in; from behind it is solid.
                Open(p,p.Partner!=null&&now.z>0&&Mathf.Abs(now.x)<Policy.HalfWidth+0.4);
                if(through==null&&p.Partner!=null&&Last.TryGetValue(p,out Vector3 before)&&Policy.Crossed((before.x,before.y,before.z),(now.x,now.y,now.z)))through=p;
                Last[p]=now;
            }
            if(through==null)return;
            if(PullingCart(me))
            {
                if(Time.time>_blockedMessage){_blockedMessage=Time.time+3;me.Message(MessageHud.MessageType.Center,"The cart will not go through");}
                return;
            }
            Go(me,through,through.Partner);
        }

        private static void Open(LocalPortal p,bool open)
        {
            if(p.Back!=null&&p.Back.enabled==open)p.Back.enabled=!open;
        }

        // The game camera stays behind the player, so it reaches a portal after them. Having stepped through, the player is in
        // the exit portal's world while the camera, following, is still in front of the entry portal: it goes on looking at the
        // player through the entry portal's glass until it reaches that glass itself. The game works out the camera from the
        // player, which puts it behind the exit portal: while the camera is carried, that place is moved through to the matching
        // place in front of the entry portal. Once the camera comes out in front of the exit portal it is simply where the game
        // put it. Its jump across the world is hidden from the motion blur.
        private static LocalPortal _exit;       // while carried: the portal the player came out of
        private static int _carryFrame=-1;
        internal static string CameraBlock;     // for the lportal command: what last stopped the carried camera
        internal static float CameraClear,CameraWanted;
        internal static bool Carrying=>_exit!=null;

        private static void StartCarry(LocalPortal entry,LocalPortal exit)
        {
            GameCamera cam=GameCamera.instance;
            if(cam==null)return;
            // straight back in before the camera came out: the camera never left the world the player returns to
            if(_exit==entry){_exit=null;return;}
            // only a camera that is in front of the entry portal follows the player through it
            _exit=entry.transform.InverseTransformPoint(cam.transform.position).z>0?exit:null;
        }
        private static void StopCarry()
        {
            if(_exit!=null)Unblur();
            _exit=null;
        }

        internal static void CarryCamera(GameCamera cam)
        {
            Reblur();
            ShowCut();
            if(cam==null||_carryFrame!=Time.frameCount||_exit==null||_exit.Partner==null)return;
            Transform t=cam.transform,a=_exit.transform,b=_exit.Partner.transform;
            t.SetPositionAndRotation(Point(a,b,t.position),Turn(a,b)*t.rotation);
        }

        // The game keeps its camera out of walls by casting from the player's head back to the camera. While the camera is
        // carried, that line runs back through the exit portal's plane and on, in the entry portal's world, from the entry
        // portal: the cast is made there instead (nothing stops it up to the glass). Returns false to leave the game's own test.
        internal static bool CollideCamera(GameCamera cam,Vector3 from,ref Vector3 end)
        {
            if(_exit==null)return CutAway(cam,from,end);
            Player me=Player.m_localPlayer;
            if(me==null||me.IsAttached()||me.IsDead()||GameCamera.InFreeFly()||_exit.Partner==null){StopCarry();return false;}
            Transform a=_exit.transform,b=_exit.Partner.transform;
            Vector3 e=a.InverseTransformPoint(from),c=a.InverseTransformPoint(end);
            if(c.z>=0||e.z<=0||e.sqrMagnitude>100){StopCarry();return CutAway(cam,from,end);} // the camera came out (or the player went back)
            float k=e.z/(e.z-c.z);
            // seen through the glass no longer (a high camera passes over the top): the camera comes to this side now
            if(!Policy.InOpening(e.x+(c.x-e.x)*k,e.y+(c.y-e.y)*k)){StopCarry();return CutAway(cam,from,end);}
            Vector3 dir=(end-from).normalized;
            Vector3 glass=Vector3.Lerp(from,end,k);
            float beyond=Vector3.Distance(glass,end);
            Vector3 start=Point(a,b,glass),onward=Turn(a,b)*dir;
            const float width=0.2f;
            float clear=beyond;
            int hits=Physics.SphereCastNonAlloc(start-onward*width,width,onward,Hits,beyond+width,cam.m_blockCameraMask,QueryTriggerInteraction.Ignore);
            CameraBlock=null;
            for(int i=0;i<hits;i++)
            {
                Collider hit=Hits[i].collider;
                if(hit==null||hit.transform.IsChildOf(b)||hit.transform.IsChildOf(a))continue; // the portals' own frames
                float d=Hits[i].distance-width-0.1f;
                if(Hits[i].distance>0&&d<clear){clear=Mathf.Max(0.02f,d);CameraBlock=hit.name+" ("+hit.transform.root.name+")";}
            }
            end=glass+dir*clear; // still on this side's line: CarryCamera then puts it through
            _carryFrame=Time.frameCount;CameraClear=clear;CameraWanted=beyond;
            return true;
        }
        private static readonly RaycastHit[] Hits=new RaycastHit[16];

        // A mirror between the camera and the player, seen from behind (just stepped out, the camera still behind it), is cut
        // away: drawn as its shadow only, and not in the way of the camera. Always returns false: the game's own test then runs.
        private static readonly List<Collider> Unblocked=new List<Collider>();
        private static readonly List<LocalPortal> Cut=new List<LocalPortal>(),WasCut=new List<LocalPortal>();
        private static int _cutFrame=-1;
        private static bool CutAway(GameCamera cam,Vector3 from,Vector3 end)
        {
            if(_cutFrame!=Time.frameCount){_cutFrame=Time.frameCount;Cut.Clear();}
            foreach(LocalPortal p in LocalPortal.Live)
            {
                if(p==null||(p.transform.position-from).sqrMagnitude>100)continue;
                // in the way of the player's eyes or feet (a high camera sees the head over the top while the body is behind)
                Vector3 c=p.transform.InverseTransformPoint(end);
                if(!(Blocks(p.transform.InverseTransformPoint(from),c)||(Player.m_localPlayer!=null&&Blocks(p.transform.InverseTransformPoint(Player.m_localPlayer.transform.position+Vector3.up*0.2f),c))))continue;
                if(!Cut.Contains(p))Cut.Add(p);
                foreach(Collider col in p.Solid)if(col!=null&&col.enabled){col.enabled=false;Unblocked.Add(col);}
            }
            return false;
        }
        private static bool Blocks(Vector3 e,Vector3 c)
        {
            if(!(e.z>0&&c.z<0))return false;
            float k=e.z/(e.z-c.z);
            return Policy.InOutline(e.x+(c.x-e.x)*k,e.y+(c.y-e.y)*k);
        }
        internal static void Reblock()
        {
            foreach(Collider c in Unblocked)if(c!=null)c.enabled=true;
            Unblocked.Clear();
        }
        // After the camera is placed: show the cut-away mirrors as shadows only, and the others whole again.
        private static void ShowCut()
        {
            if(_cutFrame!=Time.frameCount)Cut.Clear();
            foreach(LocalPortal p in WasCut)if(p!=null&&!Cut.Contains(p))p.Hide(false);
            foreach(LocalPortal p in Cut)if(p!=null)p.Hide(true);
            WasCut.Clear();WasCut.AddRange(Cut);
        }

        // The motion blur sees a camera that jumped across the world as a very fast one: it is off for the next few frames.
        private static UnityEngine.PostProcessing.PostProcessingProfile _blurProfile;
        private static bool _blurWas;
        private static int _blurUntil=-1;
        private static void Unblur()
        {
            var pp=GameCamera.instance!=null?GameCamera.instance.GetComponent<UnityEngine.PostProcessing.PostProcessingBehaviour>():null;
            if(pp==null||pp.profile==null)return;
            if(_blurUntil<0){_blurProfile=pp.profile;_blurWas=_blurProfile.motionBlur.enabled;_blurProfile.motionBlur.enabled=false;}
            _blurUntil=Time.frameCount+3;
        }
        private static void Reblur()
        {
            if(_blurUntil<0||Time.frameCount<=_blurUntil)return;
            if(_blurProfile!=null)_blurProfile.motionBlur.enabled=_blurWas;
            _blurUntil=-1;_blurProfile=null;
        }
        internal static void Stop()
        {
            if(_blurUntil>=0&&_blurProfile!=null)_blurProfile.motionBlur.enabled=_blurWas;
            _blurUntil=-1;_blurProfile=null;_exit=null;
            foreach(LocalPortal p in WasCut)if(p!=null)p.Hide(false);
            WasCut.Clear();Cut.Clear();Reblock();
        }

        private static bool PullingCart(Player me)
        {
            foreach(Vagon cart in Carts())if(cart!=null&&cart.IsAttached(me))return true;
            return false;
        }

        // A point in front of a's surface comes out in front of b's; turned half round so walking in means walking out.
        internal static Vector3 Point(Transform a,Transform b,Vector3 world)
        {
            Vector3 l=a.InverseTransformPoint(world);
            return b.TransformPoint(new Vector3(-l.x,l.y,-l.z));
        }
        internal static Quaternion Turn(Transform a,Transform b)=>b.rotation*Quaternion.Euler(0,180,0)*Quaternion.Inverse(a.rotation);

        internal static void Go(Player me,LocalPortal from,LocalPortal to)
        {
            Transform a=from.transform,b=to.transform;
            StartCarry(from,to);
            Quaternion turn=Turn(a,b);
            Vector3 was=me.transform.position;
            Vector3 position=Point(a,b,was);
            Quaternion rotation=turn*me.transform.rotation;
            Vector3 look=turn*me.GetLookDir();
            var body=me.GetComponent<Rigidbody>();
            Vector3 velocity=body!=null?turn*body.linearVelocity:Vector3.zero;

            float fallen=MaxAirAltitude(me)-was.y; // how high above the ground they were, so falling damage stays the same
            me.transform.SetPositionAndRotation(position,rotation);
            if(body!=null){body.position=position;body.rotation=rotation;body.linearVelocity=velocity;}
            MaxAirAltitude(me)=position.y+fallen;
            me.SetLookDir(look);
            Physics.SyncTransforms();
            me.ResetCloth();

            GameCamera cam=GameCamera.instance;
            if(cam!=null)
            {
                CameraPlayerPos(cam)=Point(a,b,CameraPlayerPos(cam)); // the camera's smoothed follow point, so it does not swing across
                CameraPlayerVel(cam)=turn*CameraPlayerVel(cam);
            }
            Last.Clear();
            Count++;
        }
    }
}
