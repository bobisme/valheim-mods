using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Object=UnityEngine.Object;

namespace WrongUniverse
{
    // The Gravity Gun. Hold the attack button on something loose (an item, a log, a cart, a creature) to lift it and carry it in front of you;
    // let go to drop it; block or the secondary attack flings it. With nothing held, the secondary attack punts what's in front away.
    // To move a networked thing this game has to own it, so it claims it; a held or flying creature's own walking and AI are paused (the
    // patches below) and its body is steered here. A flung thing hurts what it slams into, and a flung creature hurts itself too.
    internal static class Gravity
    {
        private static readonly AccessTools.FieldRef<Character,Rigidbody> BodyOf=AccessTools.FieldRefAccess<Character,Rigidbody>("m_body");
        private static readonly AccessTools.FieldRef<Character,float> MaxAir=AccessTools.FieldRefAccess<Character,float>("m_maxAirAltitude");

        private static Rigidbody _body;          // what's held
        private static Character _creature;
        private static ZNetView _view;
        private static float _hold,_primaryAt,_nextGrab;
        private static LineRenderer _tether;
        private static AudioSource _hum;
        // Creatures in the air after a fling: their walking stays paused until they land.
        private static readonly Dictionary<Character,float> Flying=new Dictionary<Character,float>();
        private static int _mask;
        private static int Mask=>_mask!=0?_mask:_mask=Hits.Mask|LayerMask.GetMask("item","Default_small","piece");

        internal static bool Holding=>_body!=null;
        internal static string HeldName=>_creature!=null?_creature.m_name:_body!=null?_body.name:null;
        // Whether this creature's movement is ours right now (held, or flying from a fling).
        internal static bool Controls(Character c)=>c!=null&&(c==_creature&&_creature!=null||Flying.ContainsKey(c));

        // Primary: called every physics step while the attack button is held.
        internal static bool Primary(Player p,ItemDrop.ItemData item)
        {
            _primaryAt=Time.time;
            if(_body==null&&Time.time>=_nextGrab)
            {
                _nextGrab=Time.time+0.25f;   // (one look every quarter second while the button is held on nothing)
                Grab(p);
            }
            return true;
        }
        internal static bool Secondary(Player p,ItemDrop.ItemData item)
        {
            if(Time.time<_nextGrab)return true;
            _nextGrab=Time.time+0.4f;
            if(_body!=null){Fling(p,Gadgets.FlingSpeed.Value);return true;}
            if(Pick(p,Gadgets.PuntRange.Value,out Rigidbody b,out Character c,out ZNetView v)&&Take(b,c,v))
            {
                _body=b;_creature=c;_view=v;
                Fling(p,Gadgets.FlingSpeed.Value*0.7f);
            }
            return true;
        }

        // What's under the crosshair: a loose body (or a creature) we may take, or nothing (a wall, the ground, a friend, a boss, a player).
        private static bool Pick(Player p,float range,out Rigidbody body,out Character creature,out ZNetView view)
        {
            body=null;creature=null;view=null;
            Transform cam=GameCamera.instance!=null?GameCamera.instance.transform:null;
            if(cam==null)return false;
            float behind=Vector3.Distance(cam.position,p.GetEyePoint());   // (the third-person camera sits behind the player)
            RaycastHit[] hits=Physics.SphereCastAll(cam.position,0.3f,cam.forward,range+behind,Mask,QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits,(a,b)=>a.distance.CompareTo(b.distance));
            foreach(RaycastHit h in hits)
            {
                if(h.collider==null)continue;
                Character c=h.collider.GetComponentInParent<Character>();
                if(c==p)continue;
                if(c!=null)
                {
                    if(c.IsPlayer()||c.IsBoss()||c.IsDead()||Hits.Friendly(p,c))return false;
                    creature=c;body=BodyOf(c);view=c.GetComponent<ZNetView>();
                    return body!=null;
                }
                Rigidbody rb=h.collider.attachedRigidbody;
                if(rb==null||rb.isKinematic||rb.mass>Gadgets.MaxMass.Value)return false;
                Vagon cart=rb.GetComponentInParent<Vagon>();
                if(cart!=null&&cart.IsAttached())return false;   // (someone is pulling it)
                Ship ship=rb.GetComponentInParent<Ship>();
                if(ship!=null&&ship.HasPlayerOnboard())return false;   // (nobody gets flung out of the sea in their boat)
                body=rb;view=rb.GetComponentInParent<ZNetView>();
                return true;
            }
            return false;
        }
        private static bool Take(Rigidbody body,Character creature,ZNetView view)
        {
            if(view!=null)
            {
                if(!view.IsValid())return false;
                if(!view.IsOwner())view.ClaimOwnership();
            }
            if(creature!=null)Flying.Remove(creature);
            return true;
        }
        private static void Grab(Player p)
        {
            if(!Pick(p,Gadgets.GrabRange.Value,out Rigidbody b,out Character c,out ZNetView v)||!Take(b,c,v))return;
            _body=b;_creature=c;_view=v;
            _hold=Mathf.Clamp(Size(b)+1.6f,2f,6f);
            if(c!=null){b.useGravity=false;MaxAir(c)=c.transform.position.y;}
            Gadgets.Sfx(Gadgets.SfxGrab,b.position);
        }
        private static float Size(Rigidbody b)
        {
            float r=0.5f;
            foreach(Collider col in b.GetComponentsInChildren<Collider>())if(!col.isTrigger)r=Mathf.Max(r,col.bounds.extents.magnitude);
            return Mathf.Min(r,4f);
        }

        internal static void Drop()
        {
            if(_body!=null&&_creature!=null){_body.useGravity=true;Flying[_creature]=Time.time+4f;}   // (falls to the ground as a thrown one does)
            _body=null;_creature=null;_view=null;
        }
        private static void Fling(Player p,float speed)
        {
            Transform cam=GameCamera.instance!=null?GameCamera.instance.transform:p.transform;
            Rigidbody b=_body;Character c=_creature;
            Vector3 v=cam.forward*speed+Vector3.up*speed*0.08f;
            b.linearVelocity=v;
            b.angularVelocity=Random.insideUnitSphere*4f;
            b.WakeUp();
            if(c!=null){b.useGravity=true;Flying[c]=Time.time+5f;MaxAir(c)=c.transform.position.y;}
            Impact.On(b.gameObject,p,c,speed);
            _nextGrab=Time.time+0.6f;   // (the attack button is likely still down: don't catch it again at once)
            Gadgets.Sfx(Gadgets.SfxFling,b.position);
            _body=null;_creature=null;_view=null;
        }

        internal static void FixedTick()
        {
            // Flying creatures: their fall is counted from their highest point (so a high fling hurts on landing), and they walk again on landing.
            if(Flying.Count>0)
            {
                var done=new List<Character>();
                foreach(var kv in Flying)
                {
                    Character c=kv.Key;
                    if(c==null||c.IsDead()){done.Add(c);continue;}
                    MaxAir(c)=Mathf.Max(MaxAir(c),c.transform.position.y);
                    if(Time.time>kv.Value||c.IsOnGround()&&Time.time>kv.Value-4.6f)done.Add(c);
                }
                foreach(Character c in done)Flying.Remove(c);
            }

            Player p=Player.m_localPlayer;
            if(_body==null)return;
            ItemDef held=p!=null?Things.Held(p,out _):null;
            bool still=p!=null&&!p.IsDead()&&held==Gadgets.GravityGun&&Time.time-_primaryAt<0.15f;
            if(!still||_view!=null&&(!_view.IsValid()||!_view.IsOwner())||_creature!=null&&_creature.IsDead()){Drop();return;}
            Transform cam=GameCamera.instance!=null?GameCamera.instance.transform:p.transform;
            Vector3 aim=cam.forward;
            Vector3 want=p.GetEyePoint()+aim*_hold+Vector3.up*0.2f;
            Vector3 v=(want-_body.worldCenterOfMass)*12f;
            _body.linearVelocity=Vector3.ClampMagnitude(v,30f);
            _body.angularVelocity*=0.85f;
            if(_creature!=null)MaxAir(_creature)=_creature.transform.position.y;   // (no fall damage for being carried)
        }

        // The tether and the hum, every frame.
        internal static void Tick()
        {
            Player p=Player.m_localPlayer;
            if(_body!=null&&p!=null&&(ZInput.GetButtonDown("Block")||ZInput.GetButtonDown("JoyBlock")))Fling(p,Gadgets.FlingSpeed.Value);
            bool on=_body!=null&&p!=null;
            if(on)
            {
                if(_tether==null)
                {
                    var go=new GameObject("WU_GravityTether");Object.DontDestroyOnLoad(go);
                    _tether=go.AddComponent<LineRenderer>();
                    _tether.positionCount=12;_tether.useWorldSpace=true;_tether.numCapVertices=3;
                    _tether.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                    _tether.sharedMaterial=Look.Glow(new Color(1f,0.55f,0.1f));
                }
                Vector3 a=p.GetEyePoint()+p.transform.right*0.25f-Vector3.up*0.35f+p.transform.forward*0.6f,b=_body.worldCenterOfMass;
                for(int i=0;i<12;i++)
                {
                    float t=i/11f;
                    Vector3 wobble=Mathf.Sin(t*Mathf.PI)*0.12f*new Vector3(Mathf.Sin(Time.time*23f+t*9f),Mathf.Cos(Time.time*19f+t*7f),0f);
                    _tether.SetPosition(i,Vector3.Lerp(a,b,t)+wobble);
                }
                _tether.widthMultiplier=0.05f+0.03f*Mathf.Sin(Time.time*30f);
                _tether.startColor=new Color(1f,0.6f,0.15f,0.9f);_tether.endColor=new Color(0.3f,0.7f,1f,0.7f);
                if(_hum==null)_hum=Sound.Loop(Gadgets.Hum(),p.transform,25f);
                if(_hum!=null)_hum.volume=0.35f*Plugin.Volume.Value;
            }
            if(_tether!=null)_tether.gameObject.SetActive(on);
            if(!on&&_hum!=null){Object.Destroy(_hum.gameObject);_hum=null;}
        }
        internal static void Stop()
        {
            Drop();
            foreach(Character c in Flying.Keys)if(c!=null){Rigidbody b=BodyOf(c);if(b!=null)b.useGravity=true;}
            Flying.Clear();
            if(_tether!=null)Object.Destroy(_tether.gameObject);
            if(_hum!=null)Object.Destroy(_hum.gameObject);
            _tether=null;_hum=null;
            Impact.ClearAll();
        }
    }

    // Put on a flung thing for a few seconds: the first hard knock hurts what it hit (and a creature itself), then it goes.
    internal sealed class Impact:MonoBehaviour
    {
        private static readonly List<Impact> Live=new List<Impact>();
        private Player _thrower;private Character _self;private float _speed,_until;

        internal static void On(GameObject go,Player thrower,Character self,float speed)
        {
            Impact i=go.GetComponent<Impact>()??go.AddComponent<Impact>();
            i._thrower=thrower;i._self=self;i._speed=speed;i._until=Time.time+4f;
            if(!Live.Contains(i))Live.Add(i);
        }
        internal static void ClearAll(){foreach(Impact i in Live)if(i!=null)Destroy(i);Live.Clear();}
        private void Update(){if(Time.time>_until)Destroy(this);}
        private void OnDestroy(){Live.Remove(this);}
        private void OnCollisionEnter(Collision hit)
        {
            float v=hit.relativeVelocity.magnitude;
            if(v<6f||_thrower==null)return;
            float k=Mathf.Clamp01(v/Mathf.Max(1f,_speed));
            var dmg=new HitData.DamageTypes{m_blunt=Gadgets.ImpactDamage.Value*k};
            Vector3 at=hit.contactCount>0?hit.GetContact(0).point:transform.position;
            Vector3 dir=-hit.relativeVelocity;
            Hits.Deal(_thrower,hit.collider,at,dir,dmg,20f*k,Skills.SkillType.None,true,2);
            if(_self!=null)
            {
                Collider own=_self.GetComponent<Collider>();
                if(own!=null)Hits.Deal(_thrower,own,at,-dir,dmg,0f,Skills.SkillType.None,false,2);
            }
            Gadgets.Sfx(Gadgets.SfxThud,at);
            Destroy(this);
        }
    }

    // A held or flying creature doesn't walk or think: the gravity gun moves its body.
    [HarmonyPatch(typeof(Character),"UpdateMotion")]
    internal static class GravityMotion{private static bool Prefix(Character __instance)=>!Gravity.Controls(__instance);}
    [HarmonyPatch(typeof(BaseAI),nameof(BaseAI.UpdateAI))]
    internal static class GravityMind
    {
        private static readonly AccessTools.FieldRef<BaseAI,Character> Of=AccessTools.FieldRefAccess<BaseAI,Character>("m_character");
        private static bool Prefix(BaseAI __instance,ref bool __result)
        {
            if(!Gravity.Controls(Of(__instance)))return true;
            __result=false;return false;
        }
    }
}
