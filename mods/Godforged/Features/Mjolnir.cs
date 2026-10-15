using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace Godforged
{
    // Mjölnir, Thor's hammer: a one-handed mace (a copy of Frostner, so the game's mace swings and skill), with a short handle (Loki, as a
    // fly, bit Brokkr as he forged it: a little less reach). The secondary attack throws it; where it lands lightning strikes, and it comes
    // back to the hand (Throws). Without Járngreipr, the iron gauntlets, it can't be thrown (bare hands could never catch it) and it burns
    // the hand that holds it (down to 1 health, never to death).
    // Thor's kit (Mjölnir thrown, Járngreipr worn, and the game's own Megingjord belt): the lightning leaps on from foe to foe.
    internal sealed class Mjolnir:Feature
    {
        internal override string Name=>"Mjölnir";
        internal static readonly ItemDef Hammer=new ItemDef{Prefab="GF_Mjolnir",Name="Mjölnir",Base="MaceSilver",HandEuler=new Vector3(-90,0,0),
            Description="Thor's hammer, forged by Brokkr and Eitri. Its handle is short: a fly bit the smith as he worked. Thrown, it calls down the lightning where it lands and comes back to the hand that threw it. Only a hand in Járngreipr can throw it or hold it for long: bare, it burns.",
            Cost="Iron:30, BlackMetal:20, Thunderstone:4, TrophyEikthyr:1",Station="forge",Level=3,Weight=8f,
            Model=t=>Meshes.AddAll(t,"mjolnir"),Stats=HammerStats,Returns=true,MaySecondary=MayThrow,Impact=Strike,ReturnHit=OnTheWayHome};
        internal override IEnumerable<ItemDef> Items=>new[]{Hammer};

        private static ConfigEntry<float> _strike,_radius,_chainShare,_chainRange,_burn,_split,_returnShare;
        private static ConfigEntry<int> _chain,_splitTier;
        private static ConfigEntry<bool> _buildings,_returnBuildings;

        internal override void Bind(ConfigFile c)
        {
            const string s="1 - Mjölnir";
            _strike=c.Bind(s,"StrikeDamage",60f,"Lightning damage where a thrown Mjölnir lands, at the centre.");
            _radius=c.Bind(s,"StrikeRadius",4f,"How far the strike reaches, in metres.");
            _chain=c.Bind(s,"ChainTargets",4,"With Thor's kit (Mjölnir, Járngreipr and Megingjord), how many more foes the lightning leaps to.");
            _chainRange=c.Bind(s,"ChainRange",12f,"How far each leap reaches, in metres.");
            _chainShare=c.Bind(s,"ChainDamage",0.6f,"Each leap's damage, as a share of the strike's.");
            _buildings=c.Bind(s,"StrikeHurtsBuildings",false,"Whether the lightning damages buildings.");
            _split=c.Bind(s,"StrikeSplitsTrees",80f,"Chop damage the strike does to trees, logs and stumps (only to them). 0: none.");
            _splitTier=c.Bind(s,"StrikeSplitTier",4,"The strike's tool tier against trees (the game's: 2 iron, 3 black metal, 4 and up anything).");
            _returnShare=c.Bind(s,"ReturnDamage",0.6f,"On its way home, Mjölnir hits what it passes through for this share of its own damage. 0: none.");
            _returnBuildings=c.Bind(s,"ReturnHurtsBuildings",false,"Whether Mjölnir on its way home damages buildings.");
            _burn=c.Bind(s,"BareHandBurn",3f,"Health lost each second holding Mjölnir without Járngreipr (never below 1). 0: none.");
        }

        // Held without Járngreipr, it burns: a little health each second, with a crackle, never below 1.
        private static float _nextBurn;private static bool _warned;
        internal override void Tick()
        {
            Player p=Player.m_localPlayer;
            if(p==null||p.IsDead()||!Hammer.Is(p.GetCurrentWeapon())||Jarngreipr.Worn(p)||_burn.Value<=0f){_warned=false;return;}
            if(!_warned){p.Message(MessageHud.MessageType.Center,"Mjölnir burns your bare hand: wear Járngreipr");_warned=true;_nextBurn=Time.time+1f;}
            if(Time.time<_nextBurn)return;
            _nextBurn=Time.time+1f;
            float burn=Mathf.Min(_burn.Value,p.GetHealth()-1f);
            if(burn<=0f)return;
            p.UseHealth(burn);
            Sound.Play(Bolt.Crackle(),p.transform.position+Vector3.up,0.35f,Random.Range(1.2f,1.5f),15f,p.transform);
        }
        internal override void Start()=>Bolt.Start();
        internal override void Register(ZNetScene scene)=>Throws.Projectile(scene,Hammer,"SpearBronze",t=>Meshes.AddAll(t,"mjolnir"),spin:true,sound:"hum");

        // In flight: a deep throbbing hum, the hammer's charge, with a crackle now and then. Two seconds that loop clean (55 Hz and a 6 Hz
        // throb both fit it whole).
        internal static AudioClip Hum()
        {
            float lp=0f;
            return Sound.Make("mjolnir hum",2f,t=>
            {
                float tone=Sound.Sine(55f*t)*0.55f+Sound.Sine(110f*t)*0.3f+Sound.Sine(165f*t)*0.15f+Sound.Sine(220f*t)*0.08f;
                float throb=0.55f+0.45f*Mathf.Sin(2f*Mathf.PI*6f*t);
                float n=Sound.Noise();lp+=(n-lp)*0.35f;
                float crackle=Random.value<0.004f?n*0.9f:lp*0.06f;
                return tone*throb*0.75f+crackle;
            });
        }

        private static void HammerStats(ItemDrop.ItemData.SharedData s)
        {
            s.m_damages=new HitData.DamageTypes{m_blunt=110f,m_lightning=40f};
            s.m_maxDurability=500f;s.m_durabilityPerLevel=0f;
            if(s.m_attack!=null)s.m_attack.m_attackRange*=0.85f;   // (the short handle)
            Attack spearThrow=Things.Game("SpearBronze")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_secondaryAttack;
            if(spearThrow==null){Plugin.Warn("Mjölnir: no spear throw to copy; it can't be thrown");return;}
            Attack a=spearThrow.Clone();
            a.m_attackStamina=25f;a.m_damageMultiplier=1.6f;
            a.m_projectileVel*=1.75f;a.m_projectileVelMin*=1.75f;a.m_forceMultiplier=Mathf.Max(a.m_forceMultiplier,1.5f);   // (thrown hard: fast and flat)
            s.m_attackForce=Mathf.Max(s.m_attackForce*1.5f,90f);   // (and it knocks foes back)
            s.m_secondaryAttack=a;
        }

        private static bool MayThrow(Player p,ItemDrop.ItemData item)
        {
            if(Jarngreipr.Worn(p))return true;
            p.Message(MessageHud.MessageType.Center,"Without Járngreipr you could never catch Mjölnir again");
            return false;
        }

        // Thor's kit: Járngreipr and the game's Megingjord worn (Mjölnir is in flight).
        internal static bool Kit(Player p)=>Jarngreipr.Worn(p)&&p.GetInventory().GetEquippedItems().Any(i=>
            i.m_dropPrefab!=null&&i.m_dropPrefab.name=="BeltStrength"||i.m_shared.m_name=="$item_beltstrength");

        // Lightning splits trees: chop damage to the trees, logs and stumps the strike reaches (only lightning to everything else, which
        // trees shrug off).
        private static void Split(Player p,Vector3 at)
        {
            if(_split.Value<=0f)return;
            var done=new HashSet<IDestructible>();
            foreach(Collider col in Physics.OverlapSphere(at,_radius.Value,Hits.Mask,QueryTriggerInteraction.Ignore))
            {
                GameObject go=Projectile.FindHitObject(col);
                IDestructible target=go!=null?go.GetComponent<IDestructible>():null;
                bool tree=target is TreeBase||target is TreeLog||target is Destructible d&&d.GetDestructibleType()==DestructibleType.Tree;
                if(!tree||!done.Add(target))continue;
                Vector3 point=col.ClosestPoint(at);
                var hit=new HitData{m_damage=new HitData.DamageTypes{m_chop=_split.Value},m_point=point,m_dir=(go.transform.position-at).normalized,
                    m_toolTier=(short)_splitTier.Value,m_skill=Skills.SkillType.Clubs,m_hitCollider=col,m_ranged=true,m_blockable=false,m_dodgeable=false};
                hit.SetAttacker(p);
                target.Damage(hit);
            }
        }

        // On its way home it hits what's in the way: part of its own damage, and a shove the way it's flying.
        private static void OnTheWayHome(Player p,ItemDrop.ItemData item,Collider col,Vector3 at,Vector3 dir)
        {
            if(_returnShare.Value<=0f||item==null)return;
            HitData.DamageTypes dmg=item.GetDamage();dmg.Modify(_returnShare.Value);
            if(Hits.Deal(p,col,at,dir,dmg,80f,Skills.SkillType.Clubs,_returnBuildings.Value,4,true))
                Sound.Play(Bolt.Crackle(),at,0.5f,Random.Range(0.8f,1f),30f);
        }

        private static void Strike(Player p,ItemDrop.ItemData item,Vector3 at,Collider hit)
        {
            Bolt.Sky(at);
            Hits.Blast(p,at,_radius.Value,new HitData.DamageTypes{m_lightning=_strike.Value},70f,_buildings.Value,Skills.SkillType.Clubs,0.5f);
            Split(p,at);
            if(!Kit(p))return;
            // The lightning leaps on, each time to the nearest foe the strike didn't reach.
            var struck=new HashSet<Character>(Character.GetAllCharacters().Where(c=>c!=null&&Vector3.Distance(c.GetCenterPoint(),at)<=_radius.Value));
            Vector3 from=at+Vector3.up*0.5f;
            for(int i=0;i<_chain.Value;i++)
            {
                Character next=null;float best=_chainRange.Value;
                foreach(Character c in Character.GetAllCharacters())
                {
                    if(c==null||c==p||c.IsDead()||struck.Contains(c)||Hits.Friendly(p,c))continue;
                    float d=Vector3.Distance(from,c.GetCenterPoint());
                    if(d<best){best=d;next=c;}
                }
                if(next==null)break;
                struck.Add(next);
                Vector3 to=next.GetCenterPoint();
                Bolt.Send(from,to,false);
                var h=new HitData{m_damage=new HitData.DamageTypes{m_lightning=_strike.Value*_chainShare.Value},m_point=to,m_dir=(to-from).normalized,
                    m_pushForce=15f,m_skill=Skills.SkillType.Clubs,m_ranged=true,m_blockable=false,m_dodgeable=false};
                h.SetAttacker(p);
                next.Damage(h);
                from=to;
            }
        }
    }

    // A lightning bolt between two points, seen and heard on every game (GF_Bolt): a strike from the sky with a thunderclap, or a leap
    // from foe to foe with a crackle. Two jagged lines (a blue glow round a white core) that flicker and are gone in a third of a second.
    internal static class Bolt
    {
        private const string Rpc="GF_Bolt";
        internal static void Start()=>Net.Register(Rpc,(sender,pkg)=>Draw(pkg.ReadVector3(),pkg.ReadVector3(),pkg.ReadBool()));
        internal static void Send(Vector3 from,Vector3 to,bool sky)
        {
            Draw(from,to,sky);
            var pkg=new ZPackage();pkg.Write(from);pkg.Write(to);pkg.Write(sky);
            Net.Others(Rpc,pkg);
        }
        internal static void Sky(Vector3 at)=>Send(at+new Vector3(Random.Range(-6f,6f),40f,Random.Range(-6f,6f)),at,true);
        private static void Draw(Vector3 from,Vector3 to,bool sky)=>new GameObject("GF_Bolt").AddComponent<BoltFx>().Init(from,to,sky);

        internal static AudioClip Thunder()
        {
            float lp=0f,lp2=0f;
            return Sound.Make("thunder",2.6f,t=>
            {
                float n=Sound.Noise();
                lp+=(n-lp)*0.08f;lp2+=(lp-lp2)*0.04f;
                float crack=n*Mathf.Exp(-t*16f);
                float rumble=lp2*7f*Sound.Env(t,0.04f,2.6f)*(0.75f+0.25f*Mathf.Sin(t*9f));
                return crack*0.9f+rumble;
            });
        }
        internal static AudioClip Crackle()
        {
            float lp=0f;
            return Sound.Make("crackle",0.4f,t=>
            {
                float n=Sound.Noise();lp+=(n-lp)*0.5f;
                return (Random.value<0.15f?n:lp*0.6f)*Sound.Env(t,0.005f,0.4f);
            });
        }
    }

    internal sealed class BoltFx:MonoBehaviour
    {
        private const float Life=0.35f;
        private LineRenderer _core,_glow;private Light _light;
        private float _t,_coreWidth,_glowWidth,_lightPower;private Vector3 _from,_to;

        internal void Init(Vector3 from,Vector3 to,bool sky)
        {
            _from=from;_to=to;
            _glowWidth=sky?0.9f:0.45f;_coreWidth=sky?0.22f:0.1f;_lightPower=sky?8f:4f;
            _glow=Line("glow",Look.Glow(new Color(0.45f,0.7f,1f),0.8f));
            _core=Line("core",Look.Glow(Color.white,1f));
            Shape();
            var l=new GameObject("light");l.transform.SetParent(transform,false);l.transform.position=to+Vector3.up;
            _light=l.AddComponent<Light>();
            _light.type=LightType.Point;_light.color=new Color(0.6f,0.8f,1f);_light.range=sky?30f:15f;_light.intensity=_lightPower;_light.shadows=LightShadows.None;
            Sound.Play(sky?Bolt.Thunder():Bolt.Crackle(),to,sky?1f:0.6f,Random.Range(0.9f,1.1f),sky?150f:40f);
        }
        private LineRenderer Line(string name,Material m)
        {
            var go=new GameObject(name);go.transform.SetParent(transform,false);
            LineRenderer lr=go.AddComponent<LineRenderer>();
            lr.sharedMaterial=m;lr.useWorldSpace=true;lr.numCapVertices=2;
            lr.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;lr.receiveShadows=false;
            return lr;
        }
        // A new jagged path every few frames: lightning flickers.
        private void Shape()
        {
            float len=Vector3.Distance(_from,_to);
            int n=Mathf.Clamp(Mathf.CeilToInt(len/2.5f),4,24);
            Vector3 dir=(_to-_from).normalized;
            Vector3 side=Vector3.Cross(dir,Mathf.Abs(dir.y)>0.9f?Vector3.right:Vector3.up).normalized,up=Vector3.Cross(side,dir);
            var pts=new Vector3[n+1];
            for(int i=0;i<=n;i++)
            {
                float f=i/(float)n,j=i==0||i==n?0f:len*0.05f;
                pts[i]=Vector3.Lerp(_from,_to,f)+side*Random.Range(-j,j)+up*Random.Range(-j,j);
            }
            foreach(LineRenderer lr in new[]{_glow,_core}){lr.positionCount=pts.Length;lr.SetPositions(pts);}
        }
        private void Update()
        {
            _t+=Time.deltaTime;
            if(_t>=Life){Destroy(gameObject);return;}
            if(Time.frameCount%3==0)Shape();
            float k=(1f-_t/Life)*(Random.value<0.3f?0.35f:1f);
            _glow.widthMultiplier=_glowWidth*k;_core.widthMultiplier=_coreWidth*k;_light.intensity=_lightPower*k;
        }
    }
}
