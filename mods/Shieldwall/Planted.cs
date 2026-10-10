using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Shieldwall
{
    // A stave set in a stave socket. The socket is a building piece (on the ground, a wall or a tower); the stave in it looses its magic at
    // foes in reach (a Hearth stave mends friends instead) and never hurts players, companions, tames or buildings. A Warstone feeds only
    // so many staves, nearest first, and only so far, both growing with its level. Use takes a stave out or sets one in; Shift+Use spends
    // warshards to strengthen it. If the horde brings the socket down, its stave falls where it stood.
    internal sealed class Planted:MonoBehaviour
    {
        internal const string LegacyName="BobPlantedStave",ItemKey="bob_sw_item",KillsKey="bob_sw_kills";
        internal static readonly List<Planted> Loaded=new List<Planted>();
        // Our shots carry the socket's own item; that is how the friendly-fire guard (and the kill count) knows them.
        internal static readonly Dictionary<ItemDrop.ItemData,Planted> Shots=new Dictionary<ItemDrop.ItemData,Planted>();
        private static int _sight=-1;

        internal ZNetView View;internal StaticTarget Target;private WearNTear _wear;internal Container Box;
        private ItemDrop.ItemData _item,_shot;private Stave _stave; // the stave in the socket's one slot, and the copy its shots carry
        private GameObject _look;private Light _light;
        private float _length=3,_next;
        internal bool Holding=>_item!=null;
        internal Vector3 Tip=>transform.position+Vector3.up*(Pieces.SocketTop+_length-0.5f);

        private void Awake()
        {
            View=GetComponent<ZNetView>();Target=GetComponent<StaticTarget>();_wear=GetComponent<WearNTear>();Box=GetComponent<Container>();
            if(View==null||!View.IsValid())return;
            Loaded.Add(this);
            if(_wear!=null)_wear.m_onDestroyed+=Fallen;
            Load();
            _next=Time.time+Random.Range(0.5f,1.5f);
        }
        private void OnDestroy()
        {
            Loaded.Remove(this);
            if(_shot!=null)Shots.Remove(_shot);
            if(_wear!=null)_wear.m_onDestroyed-=Fallen;
        }
        // The stave in the socket: whatever stands in its one slot (the game's own container keeps it, saves it and drops it if the socket
        // falls). Anything that is not a stave is handed back.
        private string _loaded="";
        internal void Load()
        {
            Inventory slot=Box!=null?Box.GetInventory():null;
            if(slot==null)return;
            Migrate(slot);
            ItemDrop.ItemData item=slot.GetAllItems().FirstOrDefault();
            if(item!=null&&!Items.Plantable(item)){Refuse(slot,item);item=null;}
            string prefab=PrefabOf(item);
            string signature=item==null||prefab==null?"":prefab+"|"+item.m_quality;
            if(signature==_loaded)return;
            _loaded=signature;
            if(_shot!=null)Shots.Remove(_shot);
            _item=null;_shot=null;_stave=null;
            if(_look!=null)Object.Destroy(_look);
            if(_light!=null)Object.Destroy(_light.gameObject);
            _shown=-1;
            GameObject source=prefab==null?null:Items.Get(prefab)??Assets.Find(prefab);
            if(source==null)return;
            _item=item;
            _shot=item.Clone();_shot.m_dropPrefab=source;
            _stave=Policy.Staves.FirstOrDefault(st=>st.Name==item.m_shared.m_name);
            Shots[_shot]=this;
            Build(source);
        }
        // A socket from before the slot (0.2.0) kept its stave in its own data: move it into the slot.
        private void Migrate(Inventory slot)
        {
            ZDO z=View.GetZDO();
            string prefab=z.GetString(ItemKey,"");
            if(prefab==""||!View.IsOwner())return;
            GameObject source=Items.Get(prefab)??Assets.Find(prefab);
            if(source!=null&&source.GetComponent<ItemDrop>() is ItemDrop drop)
            {
                ItemDrop.ItemData item=drop.m_itemData.Clone();item.m_dropPrefab=source;ItemDrop.LoadFromZDO(item,z);item.m_quality=z.GetInt(ZDOVars.s_quality,item.m_quality);
                if(!slot.AddItem(item))ItemDrop.DropItem(item,1,transform.position+Vector3.up*1.2f,Quaternion.identity);
            }
            z.Set(ItemKey,"");
        }
        // Something else put in the slot goes back to whoever put it there (they have the socket open, so their game owns it).
        private void Refuse(Inventory slot,ItemDrop.ItemData item)
        {
            if(!View.IsOwner())return;
            slot.RemoveItem(item);
            Player me=Player.m_localPlayer;
            if(me!=null&&Box.IsInUse()&&me.GetInventory().AddItem(item))me.Message(MessageHud.MessageType.Center,"Only a staff can stand in a stave socket.");
            else ItemDrop.DropItem(item,item.m_stack,transform.position+Vector3.up*1.2f,Quaternion.identity);
        }
        internal static void DetachAll(){foreach(Planted p in Loaded.ToList())if(p!=null){if(p._shot!=null)Shots.Remove(p._shot);if(p._look!=null)Object.Destroy(p._look);if(p._light!=null)Object.Destroy(p._light.gameObject);Object.Destroy(p);}Loaded.Clear();Shots.Clear();}
        internal static void AttachAll()
        {
            foreach(ZNetView view in Assets.Instances(Pieces.SocketName.GetStableHashCode()))
            {
                foreach(MonoBehaviour stale in view.GetComponents<MonoBehaviour>())if(stale!=null&&stale.GetType().Name==nameof(Planted)&&stale.GetType()!=typeof(Planted))Object.DestroyImmediate(stale);
                foreach(Transform t in view.transform.Cast<Transform>().Where(t=>t.name=="StaveLook"||t.name=="StaveGlow").ToList())Object.Destroy(t.gameObject);
                if(view.GetComponent<Planted>()==null)view.gameObject.AddComponent<Planted>();
            }
        }

        // The stave stands in the pedestal head up (the head is the end farthest from where a hand holds it): big, thick and heavy.
        private void Build(GameObject source)
        {
            Transform attach=source.transform.Find("attach");
            _look=new GameObject("StaveLook");
            _look.transform.SetParent(transform,false);
            GameObject model=Assets.Model(attach!=null?attach.gameObject:source,_look.transform,Vector3.zero,Quaternion.identity,1,r=>r.GetComponent<MeshFilter>()!=null);
            if(model==null)return;
            Vector3 shaft=Shaft(model,_look.transform,out Vector3 butt);
            Quaternion upright=Quaternion.FromToRotation(shaft,Vector3.up);
            model.transform.localRotation=upright;
            // The butt of the shaft stands at the socket's centre, sunk 0.35 m into it.
            Vector3 foot=upright*butt;
            model.transform.localPosition=new Vector3(-foot.x,-foot.y-0.35f,-foot.z);
            Bounds standing=Local(model,_look.transform);
            // Centre on where the shaft really is: look up the staff from below at its bottom slice only (once per kind of staff).
            string kind=source.name;
            // The shaft's own axis already stands it upright; only its butt needs centring (measured once per kind of staff).
            if(!Trued.TryGetValue(kind,out var fix))fix=(Quaternion.identity,null);
            // Turn about the butt, then put the butt's middle at the socket's centre.
            Vector3 pivot=new Vector3(0,standing.min.y,0);
            model.transform.localPosition=fix.lean*(model.transform.localPosition-pivot)+pivot;
            model.transform.localRotation=fix.lean*model.transform.localRotation;
            standing=Local(model,_look.transform);
            if(fix.bottom==null){fix.bottom=Centre(model,_look.transform,standing,0.16f,0.3f)??Vector2.zero;Trued[kind]=fix;Plugin.Log($"Stave {kind}: shaft centred by {fix.bottom.Value.x:0.00},{fix.bottom.Value.y:0.00}");}
            model.transform.localPosition-=new Vector3(fix.bottom.Value.x,0,fix.bottom.Value.y);
            model.transform.localPosition+=Vector3.up*(-0.35f-Local(model,_look.transform).min.y);
            standing=Local(model,_look.transform);
            // Every stave stands about four metres over its socket, and as thick for its height as a tree limb: no hand could swing it.
            float k=Mathf.Clamp(4f/Mathf.Max(0.3f,standing.max.y),0.8f,4f);
            _look.transform.localScale=new Vector3(Mathf.Min(k*2.1f,4.5f),k,Mathf.Min(k*2.1f,4.5f)); // a short staff grows tall, not wide
            _look.transform.localPosition=new Vector3(0,Pieces.SocketTop,0);
            _length=Mathf.Clamp(standing.max.y*k,1.5f,5.5f);
            var lit=new GameObject("StaveGlow");lit.transform.SetParent(transform,false);lit.transform.localPosition=new Vector3(0,Pieces.SocketTop+_length-0.4f,0);
            _light=lit.AddComponent<Light>();_light.type=LightType.Point;_light.range=6;_light.intensity=1.8f;_light.shadows=LightShadows.None;
            _light.color=_stave!=null&&Items.Colours.TryGetValue(_stave.Kind,out var c)?c.glow*1.6f:new Color(0.6f,0.6f,1f);
        }
        // The staff's shaft: the long axis of its longest mesh, in the frame given, pointing at its head (the end farther from where a hand
        // holds it, the item's origin). A staff's mesh is modelled along its length, whatever angle it is carried at.
        private static Vector3 Shaft(GameObject model,Transform frame,out Vector3 butt)
        {
            butt=Vector3.zero;
            MeshFilter longest=null;float length=0;int axis=1;
            foreach(MeshFilter f in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if(f.sharedMesh==null)continue;
                Vector3 size=Vector3.Scale(f.sharedMesh.bounds.size,f.transform.lossyScale);
                int a=size.x>=size.y&&size.x>=size.z?0:size.y>=size.z?1:2;
                if(size[a]>length){length=size[a];longest=f;axis=a;}
            }
            if(longest==null)return Vector3.up;
            Bounds m=longest.sharedMesh.bounds;
            Vector3 along=Vector3.zero;along[axis]=m.extents[axis];
            Matrix4x4 to=frame.worldToLocalMatrix*longest.transform.localToWorldMatrix;
            Vector3 a1=to.MultiplyPoint3x4(m.center+along),a2=to.MultiplyPoint3x4(m.center-along);
            // The grip is the frame's origin (where the hand holds the item); the head is the far end.
            butt=a1.sqrMagnitude>=a2.sqrMagnitude?a2:a1;
            Vector3 dir=(a1.sqrMagnitude>=a2.sqrMagnitude?a1-a2:a2-a1).normalized;
            // The butt's true centre: the middle of the mesh's own points in its bottom tenth (a lopsided head pulls the box, not the shaft).
            // The butt's true centre. A staff's head often pulls its bounding box off the shaft, so centre on the shaft itself: the mesh's
            // longest, thinnest part (each material is its own part, and the wood of the shaft is usually one).
            Mesh mesh=longest.sharedMesh;
            float best=0;Vector3 line=Vector3.zero;bool found=false;
            for(int i=0;i<mesh.subMeshCount;i++)
            {
                Bounds part=mesh.GetSubMesh(i).bounds;
                float length2=part.size[axis],width=0;
                for(int k=0;k<3;k++)if(k!=axis)width=Mathf.Max(width,part.size[k]);
                float slender=length2/Mathf.Max(0.01f,width);
                if(length2>m.size[axis]*0.4f&&slender>best){best=slender;line=to.MultiplyPoint3x4(part.center);found=true;}
            }
            if(found)butt=line+dir*(Vector3.Dot(butt,dir)-Vector3.Dot(line,dir));
            return dir;
        }
        private static readonly Dictionary<string,(Quaternion lean,Vector2? bottom)> Trued=new Dictionary<string,(Quaternion,Vector2?)>();
        // The middle of the shaft (frame x/z) in a thin slice at some fraction of its height: two orthographic side views of just that slice.
        private static Vector2? Centre(GameObject model,Transform frame,Bounds standing,float at,float band=0.08f)
        {
            float slice=Mathf.Max(0.04f,standing.size.y*band),half=Mathf.Max(standing.extents.x,standing.extents.z)+0.05f;
            float height=standing.min.y+standing.size.y*at;
            float? x=Side(model,frame,standing,height,frame.forward,frame.right,slice,half),z=Side(model,frame,standing,height,frame.right,frame.forward,slice,half);
            return x==null||z==null?(Vector2?)null:new Vector2(x.Value,z.Value);
        }
        // Looking along "look", how far along "across" (in metres from the bounds' centre line) the middle of the bottom slice lies.
        private static float? Side(GameObject model,Transform frame,Bounds standing,float height2,Vector3 look,Vector3 across,float slice,float half)
        {
            var renderers=model.GetComponentsInChildren<Renderer>(true);
            var layers=renderers.Select(r=>r.gameObject.layer).ToArray();
            int layer=31;for(int l=31;l>8;l--)if(string.IsNullOrEmpty(LayerMask.LayerToName(l))){layer=l;break;}
            int width=128,height=Mathf.Clamp(Mathf.RoundToInt(width*slice/(2*half)),4,128);
            var holder=new GameObject("ShieldwallButtCamera");
            RenderTexture target=RenderTexture.GetTemporary(width,height,16,RenderTextureFormat.ARGB32);
            RenderTexture previous=RenderTexture.active;
            try
            {
                foreach(Renderer r in renderers)r.gameObject.layer=layer;
                Vector3 middle=frame.TransformPoint(new Vector3(standing.center.x,height2,standing.center.z));
                float distance=half+2;
                holder.transform.position=middle-look*distance;
                holder.transform.rotation=Quaternion.LookRotation(look,frame.up);
                Camera camera=holder.AddComponent<Camera>();
                camera.enabled=false;camera.orthographic=true;camera.orthographicSize=slice/2;camera.aspect=(float)width/height;camera.cullingMask=1<<layer;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(1,0,1,1); // magenta: some staff shaders write no alpha
                camera.nearClipPlane=0.1f;camera.farClipPlane=distance*2;camera.allowHDR=false;camera.allowMSAA=false;
                var light=new GameObject("Light");light.transform.SetParent(holder.transform,false);
                Light sun=light.AddComponent<Light>();sun.type=LightType.Directional;sun.cullingMask=1<<layer;sun.intensity=1;
                camera.targetTexture=target;camera.Render();
                RenderTexture.active=target;
                var texture=new Texture2D(width,height,TextureFormat.RGBA32,false);
                texture.ReadPixels(new Rect(0,0,width,height),0,0);texture.Apply();
                Color32[] pixels=texture.GetPixels32();Object.Destroy(texture);
                // The median column of what it sees (a feather or a hook to one side barely moves it).
                var columns=new List<int>();
                for(int y=0;y<height;y++)for(int xx=0;xx<width;xx++){Color32 c=pixels[y*width+xx];if(c.g>30||c.r<225||c.b<225)columns.Add(xx);}
                if(columns.Count==0)return null;
                columns.Sort();
                float px=(columns[columns.Count/2]+0.5f)/width*2-1; // -1..1 across the view, along the camera's right
                Vector3 world=middle+holder.transform.right*px*(slice/2*camera.aspect);
                return Vector3.Dot(frame.InverseTransformPoint(world)-new Vector3(0,0,0),frame.InverseTransformDirection(across).normalized);
            }
            catch(System.Exception e){Debug.LogWarning("[Shieldwall] could not find a staff's butt: "+e.Message);return null;}
            finally
            {
                for(int i=0;i<renderers.Length;i++)if(renderers[i]!=null)renderers[i].gameObject.layer=layers[i];
                RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);Object.DestroyImmediate(holder);
            }
        }
        private static Bounds Local(GameObject model,Transform frame)
        {
            bool any=false;Bounds b=new Bounds();
            foreach(MeshFilter f in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if(f.sharedMesh==null)continue;
                Bounds m=f.sharedMesh.bounds;Matrix4x4 to=frame.worldToLocalMatrix*f.transform.localToWorldMatrix;
                for(int i=0;i<8;i++)
                {
                    Vector3 p=to.MultiplyPoint3x4(m.center+Vector3.Scale(m.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)));
                    if(!any){b=new Bounds(p,Vector3.zero);any=true;}else b.Encapsulate(p);
                }
            }
            return b;
        }

        // ---- power: the nearest stone in reach feeds it, if the stone has room (nearest staves first) ----
        private Warstone _stone;private bool _powered;private float _nextPower;private string _why="";
        internal Warstone Stone(){Power();return _powered?_stone:null;}
        private void Power()
        {
            if(Time.time<_nextPower)return;
            _nextPower=Time.time+1;
            _stone=null;_powered=false;
            if(_item==null){_why="Empty";return;}
            Warstone near=Warstone.Loaded.Where(w=>w!=null&&w.Z!=null).OrderBy(w=>Vector3.Distance(w.transform.position,transform.position)).FirstOrDefault();
            if(near==null){_why="Asleep: no Warstone near";return;}
            int level=near.Level;float reach=Policy.PowerRadius(level),d=Vector3.Distance(near.transform.position,transform.position);
            if(d>reach){_why=$"Asleep: {d:0} m from the Warstone, which reaches {reach:0} m (raise its level)";return;}
            int capacity=Policy.Capacity(level);
            int rank=Loaded.Count(p=>p!=null&&p!=this&&p._item!=null&&Vector3.Distance(p.transform.position,near.transform.position)<d);
            _stone=near;
            if(rank>=capacity){_why=$"Asleep: the Warstone feeds only {capacity} staves (raise its level)";return;}
            _powered=true;_why="";
        }
        internal static Planted Nearest(Vector3 at,float range)=>Loaded.Where(p=>p!=null&&p._item!=null&&p.Stone()!=null&&Vector3.Distance(p.transform.position,at)<=range)
            .OrderBy(p=>Vector3.Distance(p.transform.position,at)).FirstOrDefault();
        private int Quality=>_item?.m_quality??1;
        private float Range=>_stave!=null?Policy.RangeAt(_stave,Quality):Policy.BorrowedRange;
        private float Cooldown=>_stave!=null?_stave.Cooldown:Policy.BorrowedCooldown;

        // ---- watching and shooting (on the game that owns the socket) ----
        private float _nextLoad;
        private void Update()
        {
            if(View==null||!View.IsValid())return;
            if(Time.time>=_nextLoad){_nextLoad=Time.time+0.5f;Load();}
            if(_item==null)return;
            Warstone stone=Stone();
            ShowPower(stone!=null);
            if(!View.IsOwner()||stone==null||Time.time<_next)return;
            _next=Time.time+Cooldown;
            if(_stave!=null&&_stave.Kind==StaveKind.Hearth){Mend(stone);return;}
            Character foe=Choose(stone);
            if(foe==null){_next=Time.time+0.5f;return;}
            Shoot(foe);
        }
        // A fed stave glows in its colour; a sleeping one stands dark as cold iron, with no light.
        private int _shown=-1;
        private static MaterialPropertyBlock _dark;
        private void ShowPower(bool on)
        {
            if(_light!=null)
            {
                _light.enabled=on;
                if(on)_light.intensity=1.8f+0.5f*Mathf.Sin(Time.time*2.2f+transform.position.x);
            }
            if(_shown==(on?1:0)||_look==null)return;
            _shown=on?1:0;
            if(_dark==null){_dark=new MaterialPropertyBlock();_dark.SetColor("_Color",new Color(0.22f,0.22f,0.25f,1));_dark.SetColor("_EmissionColor",Color.black);}
            foreach(Renderer r in _look.GetComponentsInChildren<Renderer>(true))
            {
                if(on)r.SetPropertyBlock(null);else r.SetPropertyBlock(_dark);
            }
        }
        internal static bool IsFoe(Character c)
        {
            if(c==null||c.IsDead()||c.IsPlayer()||c.IsTamed()||c.GetFaction()==Character.Faction.Players)return false;
            if(c.GetComponent<Raider>()!=null)return true;
            Character.Faction f=c.GetFaction();
            if(f==Character.Faction.AnimalsVeg||f==Character.Faction.Dverger||f==Character.Faction.PlayerSpawned||f==Character.Faction.TrainingDummy)return false;
            BaseAI ai=c.GetBaseAI();
            return ai==null||!ai.IsAggravatable()||ai.IsAggravated(); // a calm passive thing (a Dverger, a hare) is left alone
        }
        // Who to shoot: Thunder seeks the strongest; the rest whoever is closest to the stone (the one about to strike it).
        private Character Choose(Warstone stone)
        {
            if(_sight<0)_sight=LayerMask.GetMask("Default","static_solid","terrain","piece");
            Vector3 tip=Tip;float range=Range;
            Character best=null;float bestScore=float.MaxValue;
            foreach(Character c in Character.GetAllCharacters())
            {
                if(!IsFoe(c))continue;
                Vector3 at=c.GetCenterPoint();
                float d=Vector3.Distance(at,tip);
                if(d>range)continue;
                float score=_stave!=null&&_stave.Kind==StaveKind.Thunder?-c.GetMaxHealth()+d*0.1f:Vector3.Distance(c.transform.position,stone.transform.position);
                if(score>=bestScore)continue;
                if(Physics.Linecast(tip,at,out RaycastHit hit,_sight,QueryTriggerInteraction.Ignore)&&hit.collider.GetComponentInParent<Character>()!=c&&hit.collider.GetComponentInParent<Planted>()!=this)continue;
                best=c;bestScore=score;
            }
            return best;
        }
        private void Shoot(Character foe)
        {
            Attack attack=_item.m_shared.m_attack;
            GameObject prefab=_stave!=null?Assets.Find(_stave.Projectile):attack?.m_attackProjectile;
            if(prefab==null)return;
            Projectile template=prefab.GetComponent<Projectile>();
            float speed=Mathf.Max(10,attack!=null&&attack.m_projectileVel>0?attack.m_projectileVel:30);
            float gravity=template!=null?template.m_gravity:0;
            Vector3 tip=Tip,at=foe.GetCenterPoint(),v=foe.GetVelocity();
            var lead=Policy.Lead((tip.x,tip.y,tip.z),(at.x,at.y,at.z),(v.x,v.y,v.z),speed);
            Vector3 aim=new Vector3((float)lead.x,(float)lead.y,(float)lead.z);
            Vector3 flat=aim-tip;flat.y=0;
            Vector3 dir=(aim-tip).normalized;
            if(Policy.Arc(flat.magnitude,aim.y-tip.y,speed,gravity,out double angle))
                dir=Quaternion.AngleAxis(-(float)angle*Mathf.Rad2Deg,Vector3.Cross(flat.normalized,Vector3.up)*-1)*flat.normalized;
            GameObject shot=Object.Instantiate(prefab,tip+dir*0.6f,Quaternion.LookRotation(dir));
            CinderSpawner cinders=shot.GetComponent<CinderSpawner>();
            if(cinders!=null)
            {
                Object.DestroyImmediate(cinders); // fire that cannot catch on your roof
                if(shot.GetComponent<Projectile>() is Projectile p)p.m_onHit=null; // the spawner's own hook on the shot
            }
            var hit=new HitData{m_pushForce=10,m_staggerMultiplier=1,m_hitType=HitData.HitType.Turret,m_itemWorldLevel=(byte)Game.m_worldLevel,m_blockable=true,m_dodgeable=true};
            if(_stave!=null)
            {
                float power=Policy.Power(Quality);
                switch(_stave.Type)
                {
                    case "fire":hit.m_damage.m_fire=_stave.Damage*power;break;
                    case "frost":hit.m_damage.m_frost=_stave.Damage*power;break;
                    case "lightning":hit.m_damage.m_lightning=_stave.Damage*power;break;
                    default:hit.m_damage.m_blunt=_stave.Damage*power;break;
                }
                hit.m_damage.m_blunt+=_stave.Splash*power;
            }
            else{hit.m_damage=_item.GetDamage();hit.m_damage.Modify(Policy.BorrowedPower);}
            StatusEffect status=_item.m_shared.m_attackStatusEffect;
            if(status!=null)hit.m_statusEffectHash=status.NameHash();
            shot.GetComponent<IProjectile>()?.Setup(null,dir*speed,-1,hit,_shot,null);
            if(attack!=null)attack.m_startEffect.Create(tip,Quaternion.LookRotation(dir));
        }
        internal void Killed(){if(View!=null&&View.IsValid())View.GetZDO().Set(KillsKey,View.GetZDO().GetInt(KillsKey,0)+1);}
        // The Hearth stave: friends near it heal; during a siege the stone itself is mended too.
        private void Mend(Warstone stone)
        {
            float amount=_stave.Damage*Policy.Power(Quality);
            bool any=false;
            foreach(Character c in Character.GetAllCharacters())
            {
                if(c==null||c.IsDead()||!(c.IsPlayer()||c.IsTamed()||c.GetFaction()==Character.Faction.Players))continue;
                if(Vector3.Distance(c.transform.position,transform.position)>Range||c.GetHealth()>=c.GetMaxHealth()-0.5f)continue;
                c.Heal(amount,true);any=true;
            }
            // The stone: half a percent of its strength each pulse (more with upgrades), during a siege.
            ZDO z=stone.Z;
            if(stone.Phase==Phase.Battle&&Vector3.Distance(stone.transform.position,transform.position)<=Range+5&&z.GetFloat(Shieldwall.Stone.HealthKey,0)<z.GetFloat(Shieldwall.Stone.MaxHealthKey,0))
            {Net.Damage(stone,-z.GetFloat(Shieldwall.Stone.MaxHealthKey,0)*0.005f*Policy.Power(Quality),stone.transform.position);any=true;}
            if(any)Assets.Effect("vfx_HealthUpgrade",transform.position+Vector3.up*Pieces.SocketTop);
        }

        // ---- the socket brought down (by the horde, or taken down with the hammer): the stave falls where it stood ----
        private void Fallen()
        {
            if(_item==null||View==null||!View.IsValid()||!View.IsOwner())return;
            Net.Say($"A tower has fallen! Its {_item.m_shared.m_name.ToLowerInvariant()} lies on the ground.",transform.position,60); // the slot drops it
        }

        // ---- hover and strengthening (the socket's own container opens on Use: pick a stave from your bag into its slot) ----
        internal string Hover()
        {
            if(View==null||!View.IsValid())return "";
            Power();
            if(_item==null)return Localization.instance.Localize("Stave socket ( empty )\n[<color=yellow><b>$KEY_Use</b></color>] Choose a stave");
            string stars=_item.m_quality>1?" "+new string('★',_item.m_quality-1):"";
            string name=_item.m_shared.m_name+stars+(_stave==null?" (borrowed: weak)":"");
            string state=!_powered?$"<color=#A0A0A0>{_why}</color>":_stave!=null&&_stave.Kind==StaveKind.Hearth?$"Mending friends within {Range:0} m":
                $"Watching {Range:0} m{(_stave!=null&&_stave.Kind==StaveKind.Thunder?", the strongest first":", the nearest to the stone first")}";
            int kills=View.GetZDO().GetInt(KillsKey,0);
            if(kills>0)state+=$" · {kills} slain";
            if(_wear!=null&&_wear.GetHealthPercentage()<0.99f)state+=$" · socket {Mathf.RoundToInt(_wear.GetHealthPercentage()*100)}%";
            string line=$"{name}\n{state}\n[<color=yellow><b>$KEY_Use</b></color>] Change the stave";
            if(_stave!=null&&_item.m_quality<Policy.MaxQuality)
                line+=$"\n[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] Strengthen: {Policy.UpgradeShards(_item.m_quality+1)} warshards, Warstone level {Policy.UpgradeLevel(_item.m_quality+1)}";
            return Localization.instance.Localize(line);
        }
        // Which prefab an item is: its own link if alive, else by its name (a stave picked up across a reload can carry a dead link).
        internal static string PrefabOf(ItemDrop.ItemData item)
        {
            if(item==null)return null;
            if(item.m_dropPrefab!=null)return item.m_dropPrefab.name;
            Stave stave=Policy.Staves.FirstOrDefault(st=>st.Name==item.m_shared?.m_name);
            if(stave!=null)return stave.Prefab;
            return ObjectDB.instance!=null&&ObjectDB.instance.TryGetItemPrefab(item.m_shared,out GameObject prefab)&&prefab!=null?prefab.name:null;
        }
        internal string Name=>_item!=null?_item.m_shared.m_name:"Stave socket";
        private static readonly System.Reflection.MethodInfo Changed=AccessTools.Method(typeof(Inventory),"Changed");
        // A stave dragged onto the socket from the hotbar goes straight into an empty slot.
        internal bool Drop(Player player,ItemDrop.ItemData item)
        {
            if(!Items.Plantable(item)||Box==null)return false;
            if(_item!=null){player.Message(MessageHud.MessageType.Center,"A stave already stands here.");return true;}
            if(PrefabOf(item)==null){player.Message(MessageHud.MessageType.Center,"That stave will not take to the socket.");return true;}
            View.ClaimOwnership();
            player.UnequipItem(item,false);
            if(!Box.GetInventory().AddItem(item.Clone()))return true;
            player.GetInventory().RemoveItem(item);
            _nextLoad=0;
            Assets.Effect("vfx_HealthUpgrade",transform.position+Vector3.up*Pieces.SocketTop);
            return true;
        }
        internal bool Strengthen(Player player)
        {
            if(_item==null||_stave==null){player.Message(MessageHud.MessageType.Center,"Only a war stave can be strengthened here.");return true;}
            int to=_item.m_quality+1;
            if(to>Policy.MaxQuality){player.Message(MessageHud.MessageType.Center,"It is as strong as a stave can be.");return true;}
            _nextPower=0;Power();
            int level=_stone!=null?_stone.Level:0,need=Policy.UpgradeLevel(to),shards=Policy.UpgradeShards(to);
            if(level<need){player.Message(MessageHud.MessageType.Center,$"The Warstone must be level {need} to strengthen it further. Build its upgrades.");return true;}
            Inventory bag=player.GetInventory();
            if(bag.CountItems("Warshard")<shards){player.Message(MessageHud.MessageType.Center,$"It takes {shards} warshards.");return true;}
            bag.RemoveItem("Warshard",shards);
            View.ClaimOwnership();
            ItemDrop.ItemData item=_item;item.m_quality=to;
            Changed.Invoke(Box.GetInventory(),Changed.GetParameters().Length==0?null:Changed.GetParameters().Select(pp=>(object)false).ToArray()); // the container saves the stronger stave
            _loaded="";Load();
            Assets.Effect("fx_DvergerMage_Support_start",transform.position+Vector3.up*Pieces.SocketTop);
            player.Message(MessageHud.MessageType.Center,$"The {item.m_shared.m_name.ToLowerInvariant()} grows stronger: {new string('★',to-1)}");
            return true;
        }

        // ---- for tests ----
        internal static GameObject Make(ItemDrop.ItemData item,Vector3 at,Quaternion rotation)
        {
            GameObject go=Object.Instantiate(Pieces.Socket,at,rotation);
            Piece piece=go.GetComponent<Piece>();
            if(Player.m_localPlayer!=null)piece?.SetCreator(Player.m_localPlayer.GetPlayerID(),Splatform.PlatformManager.DistributionPlatform.LocalUser.PlatformUserID);
            if(item==null)return go;
            go.GetComponent<Container>()?.GetInventory().AddItem(item);
            go.GetComponent<Planted>()?.Load();
            return go;
        }
    }

    // Staves planted in the ground by version 0.1: they fall over as items (to be set in a socket), and the old marker goes.
    internal sealed class LegacyPlanted:MonoBehaviour
    {
        private static GameObject _prefab;
        internal static GameObject Prefab
        {
            get
            {
                if(_prefab!=null)return _prefab;
                var go=new GameObject(Planted.LegacyName);go.transform.SetParent(Assets.Holder,false);
                ZNetView view=go.AddComponent<ZNetView>();view.m_persistent=true;view.m_type=ZDO.ObjectType.Default;
                go.AddComponent<LegacyPlanted>();
                return _prefab=go;
            }
        }
        internal static void Forget(){_prefab=null;}
        private void Start()
        {
            ZNetView view=GetComponent<ZNetView>();
            if(view==null||!view.IsValid()||!view.IsOwner())return;
            string prefab=view.GetZDO().GetString(Planted.ItemKey,"");
            GameObject source=string.IsNullOrEmpty(prefab)?null:Items.Get(prefab)??Assets.Find(prefab);
            if(source!=null&&source.GetComponent<ItemDrop>() is ItemDrop drop)
            {
                ItemDrop.ItemData item=drop.m_itemData.Clone();item.m_dropPrefab=source;ItemDrop.LoadFromZDO(item,view.GetZDO());
                ItemDrop.DropItem(item,1,transform.position+Vector3.up,Quaternion.identity);
            }
            ZNetScene.instance.Destroy(gameObject);
        }
    }

    // A socket placed too close to another: refused, with the reason.
    [HarmonyPatch(typeof(Player),nameof(Player.TryPlacePiece))]
    internal static class SocketSpacing
    {
        private static readonly AccessTools.FieldRef<Player,GameObject> Ghost=AccessTools.FieldRefAccess<Player,GameObject>("m_placementGhost");
        private static bool Prefix(Player __instance,Piece piece,ref bool __result)
        {
            if(piece==null||Utils.GetPrefabName(piece.gameObject)!=Pieces.SocketName)return true;
            GameObject ghost=Ghost(__instance);
            Vector3 at=ghost!=null?ghost.transform.position:__instance.transform.position;
            if(Planted.Loaded.Any(p=>p!=null&&Vector3.Distance(p.transform.position,at)<Policy.SocketSpacing))
            {
                __instance.Message(MessageHud.MessageType.Center,$"Too close to another stave socket (keep {Policy.SocketSpacing:0} m apart).");
                __result=false;return false;
            }
            // Only as many sockets as the stone can feed, and only within its reach.
            Warstone near=Warstone.Loaded.Where(w=>w!=null).OrderBy(w=>Vector3.Distance(w.transform.position,at)).FirstOrDefault();
            if(near!=null)
            {
                int level=near.Level;float reach=Policy.PowerRadius(level);
                string refused=null;
                if(Vector3.Distance(near.transform.position,at)>reach)refused=$"Beyond the Warstone's reach ({reach:0} m at level {level}). Raise its level to build farther.";
                else
                {
                    int built=Planted.Loaded.Count(p=>p!=null&&Vector3.Distance(p.transform.position,near.transform.position)<=reach);
                    if(built>=Policy.Capacity(level))refused=$"The Warstone feeds only {Policy.Capacity(level)} staves at level {level}. Raise its level to build more sockets.";
                }
                if(refused!=null){__instance.Message(MessageHud.MessageType.Center,refused);__result=false;return false;}
            }
            return true;
        }
    }

    // Shots from staves never hurt players, companions, tames or buildings: only foes. The raider struck remembers who shot it.
    [HarmonyPatch(typeof(Projectile),"IsValidTarget")]
    internal static class StaveShotTargets
    {
        private static readonly AccessTools.FieldRef<Projectile,Character> Owner=AccessTools.FieldRefAccess<Projectile,Character>("m_owner");
        private static readonly AccessTools.FieldRef<Projectile,ItemDrop.ItemData> Weapon=AccessTools.FieldRefAccess<Projectile,ItemDrop.ItemData>("m_weapon");
        private static bool Prefix(Projectile __instance,IDestructible destr,ref bool __result)
        {
            ItemDrop.ItemData weapon=Weapon(__instance);
            if(Owner(__instance)!=null||weapon==null||!Planted.Shots.TryGetValue(weapon,out Planted socket))return true;
            __result=destr is Character c&&Planted.IsFoe(c);
            if(__result&&destr is Character foe&&socket!=null)Raider.ShotBy(foe,socket);
            return false;
        }
    }
    [HarmonyPatch(typeof(Aoe),"ShouldHit")]
    internal static class StaveBlastTargets
    {
        private static readonly AccessTools.FieldRef<Aoe,Character> Owner=AccessTools.FieldRefAccess<Aoe,Character>("m_owner");
        private static readonly AccessTools.FieldRef<Aoe,ItemDrop.ItemData> Weapon=AccessTools.FieldRefAccess<Aoe,ItemDrop.ItemData>("m_itemData");
        private static bool Prefix(Aoe __instance,Collider collider,ref bool __result)
        {
            ItemDrop.ItemData weapon=Weapon(__instance);
            if(Owner(__instance)!=null||weapon==null||!Planted.Shots.TryGetValue(weapon,out Planted socket))return true;
            __result=collider!=null&&collider.GetComponentInParent<Character>() is Character c&&Planted.IsFoe(c);
            if(__result&&collider.GetComponentInParent<Character>() is Character foe&&socket!=null)Raider.ShotBy(foe,socket);
            return __result; // a foe: let the game's own checks run; anything else: no hit
        }
    }
    // The killing blow from a stave counts on its socket.
    [HarmonyPatch(typeof(Character),"OnDeath")]
    internal static class StaveKills
    {
        private static void Prefix(Character __instance)
        {
            if(Raider.LastShot.TryGetValue(__instance,out var shot)&&shot.socket!=null&&Time.time-shot.at<4)shot.socket.Killed();
            Raider.LastShot.Remove(__instance);
        }
    }
    // The socket is the game's own one-slot container; it speaks for itself on hover, strengthens on Shift+Use, takes a stave from the hotbar.
    [HarmonyPatch(typeof(Container),nameof(Container.GetHoverText))]
    internal static class SocketHover
    {
        private static void Postfix(Container __instance,ref string __result)
        {
            if(__instance.GetComponent<Planted>() is Planted socket&&PrivateArea.CheckAccess(__instance.transform.position,0,false))__result=socket.Hover();
        }
    }
    [HarmonyPatch(typeof(Container),nameof(Container.Interact))]
    internal static class SocketStrengthen
    {
        private static bool Prefix(Container __instance,Humanoid character,bool hold,bool alt,ref bool __result)
        {
            if(!alt||hold||!(__instance.GetComponent<Planted>() is Planted socket)||!(character is Player player)||player!=Player.m_localPlayer)return true;
            if(!PrivateArea.CheckAccess(__instance.transform.position)){__result=true;return false;}
            __result=socket.Strengthen(player);
            return false;
        }
    }
    [HarmonyPatch(typeof(Container),nameof(Container.UseItem))]
    internal static class SocketFromHotbar
    {
        private static bool Prefix(Container __instance,Humanoid user,ItemDrop.ItemData item,ref bool __result)
        {
            if(!(__instance.GetComponent<Planted>() is Planted socket)||!(user is Player player)||player!=Player.m_localPlayer)return true;
            __result=socket.Drop(player,item);
            return false;
        }
    }
}
