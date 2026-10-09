using System.Collections.Generic;
using UnityEngine;

namespace Omens
{
    // Thor's storm as every game sees it: the host announces it (and repeats it for late arrivals); each game turns its own weather to a
    // thunderstorm and, unless an offering stayed Thor's hammer, strikes the ground near its own player now and then. A crackle of light on
    // the ground warns of each strike; only a player under the open sky can be hit.
    internal static class Storm
    {
        internal static bool Active,Softened;
        private static float _heardAt,_nextStrike;
        private static bool _forced;
        private static readonly List<GameObject> Live=new List<GameObject>();

        internal static void Heard(bool active,bool softened){Active=active;Softened=softened;_heardAt=Time.time;}

        internal static void Tick()
        {
            if(Active&&Time.time-_heardAt>75)Active=false; // the host went quiet
            Player me=Player.m_localPlayer;
            bool outside=me!=null&&me.transform.position.y<3000; // not in a dungeon
            if(Active&&outside&&EnvMan.instance!=null){EnvMan.instance.SetForceEnvironment("ThunderStorm");_forced=true;}
            else Release();
            if(!Active||Softened||!outside||me.IsDead())return;
            if(_nextStrike<=0){_nextStrike=Time.time+Policy.NextStrike(Random.value);return;}
            if(Time.time<_nextStrike)return;
            _nextStrike=Time.time+Policy.NextStrike(Random.value);
            // Most strikes land a way off; one in four comes close, to whoever stands in the open.
            bool close=Policy.CloseStrike(Random.value)&&!Cover.IsUnderRoof(me.GetCenterPoint());
            Vector2 offset=Random.insideUnitCircle.normalized*(close?Random.Range(0.5f,3.5f):Random.Range(8f,30f));
            Vector3 spot=me.transform.position+new Vector3(offset.x,0,offset.y);
            if(ZoneSystem.instance!=null&&ZoneSystem.instance.GetSolidHeight(spot,out float h))spot.y=h;
            var go=new GameObject("BobOmenStrike");
            go.transform.position=spot;
            go.AddComponent<Strike>();
            Live.Add(go);
            Live.RemoveAll(x=>x==null);
        }
        private static void Release()
        {
            if(!_forced)return;
            _forced=false;
            if(EnvMan.instance!=null)EnvMan.instance.SetForceEnvironment("");
        }
        internal static void Clear()
        {
            Release();Active=false;
            foreach(GameObject go in Live)if(go!=null)Object.Destroy(go);
            Live.Clear();
        }
    }

    // One strike: a warning crackle on the ground, then the bolt, a flash and thunder. It hurts the local player if they stand too close.
    internal sealed class Strike:MonoBehaviour
    {
        private float _born;private bool _struck;
        private Light _warn;
        private static Material _boltMaterial;

        private void Start()
        {
            _born=Time.time;
            var warn=new GameObject("Warning");warn.transform.SetParent(transform,false);warn.transform.localPosition=Vector3.up*0.3f;
            _warn=warn.AddComponent<Light>();
            _warn.type=LightType.Point;_warn.color=new Color(0.6f,0.8f,1f);_warn.range=4;_warn.intensity=0;_warn.shadows=LightShadows.None;
        }
        private void Update()
        {
            float age=Time.time-_born;
            if(!_struck)
            {
                // A flickering glow that builds on the spot.
                _warn.intensity=Mathf.Lerp(0.5f,3f,age/Policy.StrikeWarning)*(0.6f+0.4f*Mathf.PerlinNoise(Time.time*25f,0));
                if(age>=Policy.StrikeWarning)Hit();
                return;
            }
            if(age>Policy.StrikeWarning+2.5f)Destroy(gameObject);
        }
        private void Hit()
        {
            _struck=true;
            Destroy(_warn.gameObject);
            Vector3 ground=transform.position;
            // The bolt: a jagged line from the clouds, gone in a fifth of a second.
            var bolt=new GameObject("Bolt");bolt.transform.SetParent(transform,false);
            LineRenderer line=bolt.AddComponent<LineRenderer>();
            if(_boltMaterial==null)
            {
                Shader shader=Shader.Find("Sprites/Default")??Shader.Find("Legacy Shaders/Particles/Alpha Blended");
                if(shader!=null)_boltMaterial=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
            }
            if(_boltMaterial!=null)line.sharedMaterial=_boltMaterial;
            line.startColor=new Color(0.85f,0.92f,1f);line.endColor=Color.white;
            line.startWidth=0.5f;line.endWidth=0.15f;line.useWorldSpace=true;
            const int points=14;line.positionCount=points;
            for(int i=0;i<points;i++)
            {
                float t=i/(float)(points-1);
                Vector2 jag=i==0||i==points-1?Vector2.zero:Random.insideUnitCircle*2.2f*(1-t*0.6f);
                line.SetPosition(i,ground+new Vector3(jag.x,(1-t)*70f,jag.y));
            }
            Destroy(bolt,0.2f);
            var flash=new GameObject("Flash");flash.transform.SetParent(transform,false);flash.transform.localPosition=Vector3.up*3;
            Light light=flash.AddComponent<Light>();
            light.type=LightType.Point;light.color=new Color(0.8f,0.88f,1f);light.range=45;light.intensity=7;light.shadows=LightShadows.None;
            Destroy(flash,0.18f);
            // Thunder, from the game's own sound if it has one loose.
            GameObject thunder=ZNetScene.instance!=null?ZNetScene.instance.GetPrefab("sfx_thunder"):null;
            if(thunder!=null)Instantiate(thunder,ground,Quaternion.identity);
            // Scorched: a few seconds of the game's own flames where it hit.
            GameObject fire=Looks.Fire(null,ground,0.5f);
            if(fire!=null)Destroy(fire,4f);

            Player me=Player.m_localPlayer;
            if(me!=null&&!me.IsDead()&&Vector3.Distance(me.transform.position,ground)<=Policy.StrikeRadius&&!Cover.IsUnderRoof(me.GetCenterPoint()))
            {
                var hit=new HitData{m_point=me.GetCenterPoint(),m_dir=Vector3.down,m_hitType=HitData.HitType.Undefined};
                hit.m_damage.m_lightning=Policy.StrikeDamage;
                me.Damage(hit);
                me.Message(MessageHud.MessageType.TopLeft,"Thor's lightning strikes you!");
            }
        }
    }
}
