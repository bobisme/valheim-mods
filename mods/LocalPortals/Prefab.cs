using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object=UnityEngine.Object;

namespace LocalPortals
{
    // The local portal piece: the game's wood portal for its building rules, with its frame, effects and teleporting replaced
    // by a wooden full-length mirror whose glass shows the view from the linked portal.
    internal static class PortalPrefab
    {
        internal const string Name="BobLocalPortal",DisplayName="Looking-Glass Portal"; // (the name it is saved under stays: built ones keep loading)
        private const string ModelVersion="Model7";
        // The wild looking-glass: not built but found (a location or dungeon puts it there), rough stone and roots and crystal.
        // It cannot be damaged or taken down, and links only by name (its placer names it: the "bob_lportal_name" string).
        internal const string WildName="BobWildLookingGlass",WildDisplayName="Wild Looking-Glass";
        private const string WildModelVersion="Wild3";
        internal static int WildHash=>WildName.GetStableHashCode();
        internal static GameObject Wild=>_wild;
        internal static bool IsWild(ZDO zdo)=>zdo!=null&&zdo.GetPrefab()==WildHash;
        internal static int Hash=>Name.GetStableHashCode();
        internal static GameObject Prefab=>_prefab;
        private static GameObject _holder,_prefab,_wild;
        private static Mesh _surface;
        private static readonly Dictionary<Vector3,Mesh> Boxes=new Dictionary<Vector3,Mesh>();
        private static Material _glowMaterial;

        private static GameObject Build(ZNetScene scene)
        {
            if(_prefab!=null)return _prefab;
            GameObject vanilla=scene.GetPrefab("portal_wood");
            if(vanilla==null){Debug.LogWarning("[LocalPortals] The game's portal_wood was not found; the local portal is not added.");return null;}
            _holder=new GameObject(Name+"Prefabs");
            _holder.SetActive(false); // keeps the template from waking up as a real object
            Object.DontDestroyOnLoad(_holder);
            GameObject go=Bare(vanilla,Name);

            var piece=go.GetComponent<Piece>();
            piece.m_name=DisplayName;
            piece.m_description="A tall looking-glass in a root-bound frame that looks out of another one: see the other side in its glass, walk into it and step out of the other. Build two within "+Policy.LinkRange+" m and the second links to the first; or name two the same (E), like the game's portals.";
            piece.m_resources=new[]{Requirement(scene,"FineWood",10),Requirement(scene,"GreydwarfEye",6),Requirement(scene,"SurtlingCore",1)}.Where(r=>r!=null).ToArray();

            Model(go.transform,scene,vanilla);
            try{Sprite icon=Icon.Draw(go);if(icon!=null)piece.m_icon=icon;} // (or the game's portal's picture stays)
            catch(System.Exception e){Debug.LogWarning("[LocalPortals] Could not draw the build menu picture: "+e.Message);}
            var wear=go.GetComponent<WearNTear>();
            if(wear!=null)wear.m_materialType=WearNTear.MaterialType.Wood;
            go.AddComponent<LocalPortal>();
            _prefab=go;
            try{BuildWild(scene,vanilla);}
            catch(System.Exception e){Debug.LogError("[LocalPortals] Could not make the wild looking-glass; it is left out: "+e);_wild=null;}
            return go;
        }
        // The game's wood portal with its teleporting, effects and colliders taken off: the rest (saving, placing) is kept.
        private static GameObject Bare(GameObject vanilla,string name)
        {
            GameObject go=Object.Instantiate(vanilla,_holder.transform,false);
            go.name=name;
            go.transform.localPosition=Vector3.zero;
            Object.DestroyImmediate(go.GetComponent<TeleportWorld>());
            foreach(string child in new[]{"TELEPORT","_target_found_red","New","Mesh collider","Proximity","GuidePoint","portal_destruction"})
            {
                Transform t=go.transform.Find(child);
                if(t!=null)Object.DestroyImmediate(t.gameObject);
            }
            foreach(Collider c in go.GetComponents<Collider>())Object.DestroyImmediate(c);
            return go;
        }
        private static void BuildWild(ZNetScene scene,GameObject vanilla)
        {
            GameObject go=Bare(vanilla,WildName);
            var piece=go.GetComponent<Piece>();
            piece.m_name=WildDisplayName;
            piece.m_description="An ancient looking-glass grown out of the rock, found, not made.";
            piece.m_resources=new Piece.Requirement[0];
            piece.m_canBeRemoved=false;
            Object.DestroyImmediate(go.GetComponent<WearNTear>()); // (nothing to strike: it cannot be broken)
            WildModel(go.transform,scene,vanilla);
            go.AddComponent<LocalPortal>();
            _wild=go;
        }

        // The looking-glass: an arched frame of dark wood in three mouldings (a wide band, a pale bead round the glass and a
        // raised outer lip), carved capitals where the arch springs, stepped feet and a solid board behind the glass; not the
        // same on both sides: an old root climbs the left and over the arch, an inlay glowing in the portal's colour runs with
        // it, runes glow up the right, crystals grow by the right foot and one floats above the arch; a soft light, and motes
        // of light drifting up the glass. Frame, feet and back are solid; the back lets its own player through from the front
        // (Crossing), so they can step into the glass.
        private static void Model(Transform root,ZNetScene scene,GameObject vanilla)
        {
            Material dark=MaterialOf(scene,vanilla,"darkwood_beam","darkwood_pole","wood_beam");
            Material pale=MaterialOf(scene,vanilla,"wood_beam","wood_pole","woodwall");
            Material planks=MaterialOf(scene,vanilla,"wood_wall_half","woodwall","wood_floor");
            Material glow=GlowMaterial(dark);
            var visual=new GameObject("Visual").transform;
            visual.SetParent(root,false);
            new GameObject(ModelVersion).transform.SetParent(visual,false); // portals built with an older model are remade on a reload
            var solid=new GameObject("Frame").transform;
            solid.SetParent(root,false);
            solid.gameObject.layer=root.gameObject.layer;

            float hw=(float)Policy.HalfWidth,spring=(float)Policy.Spring,top=(float)Policy.Top,y0=(float)Policy.Bottom;
            Mesh band=Shapes.Moulding(-0.01f,0.17f,-0.12f,0.05f,y0);
            Part(visual,"Band",band,dark);
            Part(visual,"Bead",Shapes.Moulding(-0.015f,0.035f,-0.02f,0.085f,y0),pale);
            Part(visual,"Lip",Shapes.Moulding(0.15f,0.21f,-0.14f,0.075f,y0),dark);
            Part(visual,"Trim",Shapes.Moulding(0.075f,0.095f,0.04f,0.062f,y0+0.12f,0,0.68f),glow,false); // up the left and over the arch
            var bandCollider=new GameObject("Band");
            bandCollider.layer=solid.gameObject.layer;
            bandCollider.transform.SetParent(solid,false);
            bandCollider.AddComponent<MeshCollider>().sharedMesh=band;

            Mesh back=Shapes.Board(0.04f,(float)(Policy.BackZ-Policy.BackThickness/2),(float)(Policy.BackZ+Policy.BackThickness/2),y0);
            Part(visual,"BackBoard",back,planks);
            var backCollider=new GameObject("Back");
            backCollider.layer=solid.gameObject.layer;
            backCollider.transform.SetParent(solid,false);
            var mc=backCollider.AddComponent<MeshCollider>();mc.sharedMesh=back;mc.convex=true;

            float side=hw+0.09f,z=-0.035f;
            foreach(float s in new[]{-1f,1f})
            {
                string n=s<0?"L":"R";
                Block(visual,solid,"Foot"+n,dark,new Vector3(s*side,0.05f,z),new Vector3(0.34f,0.1f,0.78f));
                Block(visual,null,"Step"+n,dark,new Vector3(s*side,0.14f,z),new Vector3(0.26f,0.08f,0.56f));
                Block(visual,null,"Capital"+n,pale,new Vector3(s*side,spring,z+0.01f),new Vector3(0.3f,0.07f,0.27f));
                Block(visual,null,"Corbel"+n,dark,new Vector3(s*side,spring-0.08f,z),new Vector3(0.22f,0.09f,0.22f));
            }
            Runes(visual,glow,hw);
            Root(visual,Bark(MaterialOf(scene,vanilla,"Oak_log","beech_log","Beech_log","Pinetree_log","FirTree_log","darkwood_pole")));
            Crystals(visual,glow,hw,top);

            Glass(visual,vanilla);

            var wear=root.GetComponent<WearNTear>();
            if(wear!=null){wear.m_new=wear.m_worn=wear.m_broken=visual.gameObject;wear.m_fragmentRoots=null;}
        }

        // A column of glowing runes carved up the right side of the frame, where the inlay is not.
        private static readonly (float x0,float y0,float x1,float y1)[][] Glyphs=
        {
            new[]{(0f,0f,0f,1f),(0f,0.75f,0.55f,1f),(0f,0.45f,0.55f,0.7f)},                                 // fehu
            new[]{(0.28f,0f,0.28f,1f),(0.28f,0.62f,0f,1f),(0.28f,0.62f,0.56f,1f)},                          // algiz
            new[]{(0f,0f,0f,1f),(0f,1f,0.45f,0.78f),(0.45f,0.78f,0f,0.55f),(0f,0.55f,0.5f,0f)},             // raidho
            new[]{(0.5f,1f,0f,0.5f),(0f,0.5f,0.5f,0f)},                                                      // kenaz
            new[]{(0f,0f,0f,1f),(0.6f,0f,0.6f,1f),(0f,1f,0.6f,0f),(0f,0f,0.6f,1f)},                          // dagaz
            new[]{(0f,0f,0f,1f),(0f,0.78f,0.5f,0.58f),(0f,0.52f,0.5f,0.32f)},                               // ansuz
            new[]{(0.3f,1f,0f,0.65f),(0.3f,1f,0.6f,0.65f),(0f,0.65f,0.6f,0.18f),(0.6f,0.65f,0f,0.18f)},     // othala
            new[]{(0f,0f,0f,1f),(0f,0.72f,0.42f,0.5f),(0.42f,0.5f,0f,0.28f)},                               // thurisaz
            new[]{(0.3f,0f,0.3f,1f),(0f,0.7f,0.6f,0.3f)},                                                    // nauthiz
        };
        private static void Runes(Transform visual,Material glow,float hw)
        {
            var strokes=new List<(Vector2,Vector2)>();
            const float tall=0.12f,wide=0.065f;
            for(int i=0;i<Glyphs.Length;i++)
            {
                Vector2 at=new Vector2(hw+0.085f-wide/2,0.32f+i*0.158f);
                foreach(var (x0,y0,x1,y1) in Glyphs[i])
                    strokes.Add((at+new Vector2(x0/0.6f*wide,y0*tall),at+new Vector2(x1/0.6f*wide,y1*tall)));
            }
            Part(visual,"TrimRunes",Shapes.Strokes(strokes,0.011f,0.05f,0.009f),glow,false);
        }

        // An old root grown up out of the ground by the left foot: it winds in and out round the frame, climbs over the arch
        // and curls up past the top towards the floating crystal, with a few twigs and roots spread on the ground.
        private static void Root(Transform visual,Material bark)
        {
            var edge=Policy.Edge(0.25,48,-0.12);
            var line=new List<Vector3>();
            foreach(var (x,y) in edge)line.Add(new Vector3((float)x,(float)y,0));
            // along the edge to a little past the top, a point every 6 cm
            var path=new List<Vector3>();
            float total=0;for(int i=1;i<line.Count;i++)total+=Vector3.Distance(line[i-1],line[i]);
            float stop=total*0.58f,step=0.06f;
            for(float d=0;d<=stop;d+=step)path.Add(Along(line,d));
            // then a curl, turning up and back on itself
            Vector3 dir=(path[path.Count-1]-path[path.Count-2]).normalized,at=path[path.Count-1];
            for(int k=0;k<16;k++)
            {
                dir=Quaternion.Euler(0,0,24)*dir;
                at+=dir*(0.075f*(1-k/19f));
                path.Add(at);
            }
            var radius=new List<float>();
            float length=0;
            for(int i=0;i<path.Count;i++)
            {
                if(i>0)length+=Vector3.Distance(path[i-1],path[i]);
                float t=i/(float)(path.Count-1);
                Vector3 p=path[i];
                float weave=Mathf.Clamp01((stop-length)/0.4f+0.5f); // it stops winding round the frame as it curls
                p.z=-0.035f+0.12f*Mathf.Sin(length*2.4f)*weave;
                Vector3 outward=new Vector3(p.x,p.y-(float)Policy.Spring,0).normalized;if(p.y<Policy.Spring)outward=new Vector3(Mathf.Sign(p.x),0,0);
                p+=outward*(0.025f*Mathf.Sin(length*7.3f)*weave); // knotted
                path[i]=p;
                radius.Add(Mathf.Lerp(0.075f,0.012f,Mathf.Pow(t,0.8f))*(1+0.16f*Mathf.Sin(length*9.1f))*(i<3?1.35f:1));
            }
            Part(visual,"Root",Shapes.Tube(path,radius),bark);
            // short knotted twigs off the root, curling up and back
            int twig=0;
            foreach(var (f,lean) in new[]{(0.24f,0.5f),(0.43f,-0.45f),(0.86f,0.35f)})
            {
                int i=Mathf.RoundToInt(f*(path.Count-1));
                Vector3 from=path[i];
                Vector3 outward=new Vector3(from.x,from.y-(float)Policy.Spring*0.8f,0).normalized;
                Vector3 d=(outward+Vector3.forward*lean).normalized;
                var twigPath=new List<Vector3>();var twigRadius=new List<float>();
                Vector3 at2=from;
                for(int k=0;k<8;k++)
                {
                    twigPath.Add(at2);
                    twigRadius.Add(Mathf.Lerp(radius[i]*0.6f,0.006f,k/7f));
                    d=(Quaternion.AngleAxis(16,Vector3.Cross(d,Vector3.up).normalized)*d).normalized; // turning up, then over
                    at2+=d*0.028f;
                }
                Part(visual,"Twig"+twig++,Shapes.Tube(twigPath,twigRadius,6),bark);
            }
            // thick roots spread on the ground round its foot, sinking in
            Vector3 foot=path[1];foot.y=0.03f;
            int n=0;
            foreach(Vector3 d in new[]{new Vector3(-1,0,0.35f),new Vector3(-0.45f,0,-0.9f),new Vector3(-0.25f,0,1f),new Vector3(-0.9f,0,-0.3f)})
            {
                var p=new List<Vector3>();var r=new List<float>();
                float reach=n==3?0.18f:0.28f;
                for(int k=0;k<6;k++){float t=k/5f;p.Add(foot+d.normalized*(t*reach)+Vector3.up*(0.03f-t*t*0.09f));r.Add(Mathf.Lerp(0.065f,0.02f,t));}
                Part(visual,"GroundRoot"+n++,Shapes.Tube(p,r,6),bark);
            }
        }
        private static Vector3 Along(List<Vector3> line,float d)
        {
            for(int i=1;i<line.Count;i++)
            {
                float l=Vector3.Distance(line[i-1],line[i]);
                if(d<=l)return Vector3.Lerp(line[i-1],line[i],l>0?d/l:0);
                d-=l;
            }
            return line[line.Count-1];
        }

        // A long crystal floating above the arch, off to the right where the root curls up to it (it turns and bobs: LocalPortal),
        // and a little cluster of crystals grown out of the ground by the right foot. All glow in the portal's colour.
        private static void Crystals(Transform visual,Material glow,float hw,float top)
        {
            Mesh tall=Shapes.Crystal(0.06f,0.38f),small=Shapes.Crystal(0.035f,0.2f),tiny=Shapes.Crystal(0.022f,0.12f);
            var floating=new GameObject("TrimCrystal");
            floating.transform.SetParent(visual,false);
            floating.transform.localPosition=new Vector3(0.2f,top+0.66f,-0.035f);
            floating.transform.localRotation=Quaternion.Euler(0,0,-8);
            floating.AddComponent<MeshFilter>().sharedMesh=tall;
            var r=floating.AddComponent<MeshRenderer>();r.sharedMaterial=glow;r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            for(int k=0;k<3;k++) // shards circling it (LocalPortal turns them)
            {
                var shard=new GameObject("TrimShard"+k);
                shard.transform.SetParent(floating.transform.parent,false);
                shard.transform.localPosition=floating.transform.localPosition;
                shard.AddComponent<MeshFilter>().sharedMesh=k==0?small:tiny;
                var sr=shard.AddComponent<MeshRenderer>();sr.sharedMaterial=glow;sr.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var cluster=new (Vector3 at,Vector3 tilt,Mesh mesh)[]
            {
                (new Vector3(hw+0.36f,0.06f,0.24f),new Vector3(8,0,-22),small),
                (new Vector3(hw+0.45f,0.03f,0.1f),new Vector3(-12,0,-48),tiny),
                (new Vector3(hw+0.29f,0.03f,0.36f),new Vector3(28,0,-8),tiny),
                (new Vector3(hw+0.42f,0.02f,0.33f),new Vector3(20,40,-35),tiny),
            };
            int i=0;
            foreach(var (at,tilt,mesh) in cluster)Part(visual,"TrimCluster"+i++,mesh,glow,false).transform.SetPositionAndRotation(visual.TransformPoint(at),visual.rotation*Quaternion.Euler(tilt));
        }

        // The wild looking-glass: the same glass in a ring of broken, lumpy rock that runs into the ground, a slab of rock behind,
        // boulders round its feet and one lodged on top, a split shard leaning on the right, roots hanging over the stone and
        // creeping off over the ground, and crystals glowing in the portal's colour grown out of the rock here and there.
        private static void WildModel(Transform root,ZNetScene scene,GameObject vanilla)
        {
            Material rock=Stone(MaterialOf(scene,vanilla,"Rock_3","rock1","rock4_forest","rock3_silver","stone_wall_1x1"));
            Material bark=Bark(MaterialOf(scene,vanilla,"Oak_log","beech_log","Beech_log","Pinetree_log","FirTree_log","darkwood_pole"));
            Material glow=GlowMaterial(MaterialOf(scene,vanilla,"darkwood_beam","darkwood_pole","wood_beam"));
            var visual=new GameObject("Visual").transform;
            visual.SetParent(root,false);
            new GameObject(WildModelVersion).transform.SetParent(visual,false);
            var solid=new GameObject("Frame").transform;
            solid.SetParent(root,false);
            solid.gameObject.layer=root.gameObject.layer;
            float top=(float)Policy.Top,y0=(float)Policy.Bottom;

            Mesh ring=Shapes.RockRing(7);
            Part(visual,"Rock",ring,rock);
            Collide(solid,"Rock",ring,false);
            float b0=(float)(Policy.BackZ-Policy.BackThickness/2),b1=(float)(Policy.BackZ+Policy.BackThickness/2);
            Part(visual,"RockBack",Shapes.RockBack(3,b0,b1,y0),rock);
            Collide(solid,"Back",Shapes.Board(0.04f,b0,b1,y0),true);

            var stones=new (Vector3 at,float r,float squash,Vector3 turn,bool solid)[]
            {
                (new Vector3(-0.98f,0.06f,0.12f),0.28f,0.75f,new Vector3(0,20,8),true),
                (new Vector3(-1.08f,0.0f,-0.28f),0.21f,0.7f,new Vector3(0,70,-6),true),
                (new Vector3(-0.8f,0.0f,0.42f),0.13f,0.8f,new Vector3(0,10,0),false),
                (new Vector3(0.95f,0.08f,-0.06f),0.33f,0.7f,new Vector3(0,-35,5),true),
                (new Vector3(1.12f,0.0f,0.32f),0.16f,0.75f,new Vector3(0,50,0),false),
                (new Vector3(0.2f,top+0.16f,-0.06f),0.32f,0.58f,new Vector3(4,30,12),false),   // lodged on top, off centre
                (new Vector3(0.9f,1.95f,-0.06f),0.19f,1.7f,new Vector3(0,15,-18),false),       // a split shard leaning on the right
            };
            int i=0;
            foreach(var (at,r,squash,turn,isSolid) in stones)
            {
                Mesh boulder=Shapes.Boulder(r,11+i,squash);
                GameObject go=Part(visual,"Stone"+i,boulder,rock);
                go.transform.localPosition=at;go.transform.localRotation=Quaternion.Euler(turn);
                if(isSolid){var c=Collide(solid,"Stone"+i,boulder,true);c.transform.localPosition=at;c.transform.localRotation=Quaternion.Euler(turn);}
                i++;
            }

            Vine(visual,"Root0",bark,0.075f,0.014f,new Vector3(0.32f,top+0.36f,0.12f),new Vector3(0.66f,top+0.02f,0.24f),new Vector3(0.95f,2.05f,0.26f),
                new Vector3(1.0f,1.5f,0.2f),new Vector3(0.95f,1.05f,0.28f),new Vector3(0.86f,0.78f,0.33f));
            Vine(visual,"Root1",bark,0.09f,0.018f,new Vector3(-0.05f,top+0.42f,-0.02f),new Vector3(-0.5f,top+0.2f,-0.2f),new Vector3(-0.92f,2.0f,-0.26f),
                new Vector3(-1.0f,1.2f,-0.16f),new Vector3(-1.0f,0.45f,0.05f),new Vector3(-1.15f,0.12f,0.32f),new Vector3(-1.5f,-0.02f,0.52f));
            Vine(visual,"Root2",bark,0.045f,0.008f,new Vector3(-0.62f,top+0.02f,0.27f),new Vector3(-0.74f,top-0.3f,0.3f),new Vector3(-0.8f,top-0.62f,0.28f));

            Cluster(visual,glow,"TrimLeft",new Vector3(-1.0f,0.3f,0.3f),new Vector3(-0.7f,1,0.6f),6,0.32f,21);
            Cluster(visual,glow,"TrimRight",new Vector3(1.0f,1.35f,0.18f),new Vector3(1,0.5f,0.35f),5,0.26f,22);
            Cluster(visual,glow,"TrimTop",new Vector3(-0.15f,top+0.34f,0.1f),new Vector3(-0.35f,1,0.25f),4,0.36f,23);
            Cluster(visual,glow,"TrimFoot",new Vector3(0.78f,0.04f,0.42f),new Vector3(0.3f,1,0.7f),4,0.17f,24);
            Cluster(visual,glow,"TrimInner",new Vector3(-0.72f,1.75f,0.2f),new Vector3(0.3f,0.6f,1),2,0.12f,25);

            Glass(visual,vanilla);
        }
        // The glass, a soft light and motes of light drifting up in front of it: the same in every model.
        private static void Glass(Transform visual,GameObject vanilla)
        {
            var glass=new GameObject("Surface");
            glass.transform.SetParent(visual,false);
            glass.AddComponent<MeshFilter>().sharedMesh=_surface??=Shapes.Surface();
            var r=glass.AddComponent<MeshRenderer>();
            r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;r.receiveShadows=false;
            var light=new GameObject("Light");
            light.transform.SetParent(visual,false);
            light.transform.localPosition=new Vector3(0,1.5f,0.5f);
            var l=light.AddComponent<Light>();
            l.type=LightType.Point;l.range=3.5f;l.intensity=0.45f;l.shadows=LightShadows.None;
            Motes(visual,vanilla);
        }
        private static Collider Collide(Transform solid,string name,Mesh mesh,bool convex)
        {
            var go=new GameObject(name){layer=solid.gameObject.layer};
            go.transform.SetParent(solid,false);
            var mc=go.AddComponent<MeshCollider>();mc.sharedMesh=mesh;mc.convex=convex;
            return mc;
        }
        // A root through these points (a smooth curve), tapering from r0 to r1, a little knotted.
        private static void Vine(Transform visual,string name,Material bark,float r0,float r1,params Vector3[] points)
        {
            var path=new List<Vector3>();var radius=new List<float>();
            for(int i=0;i<points.Length-1;i++)
                for(int k=0;k<6;k++)
                {
                    float t=k/6f;
                    Vector3 p0=points[Mathf.Max(i-1,0)],p1=points[i],p2=points[i+1],p3=points[Mathf.Min(i+2,points.Length-1)];
                    path.Add(0.5f*(2*p1+(-p0+p2)*t+(2*p0-5*p1+4*p2-p3)*t*t+(-p0+3*p1-3*p2+p3)*t*t*t));
                }
            path.Add(points[points.Length-1]);
            for(int i=0;i<path.Count;i++)
            {
                float t=i/(float)(path.Count-1);
                radius.Add(Mathf.Lerp(r0,r1,t)*(1+0.18f*Mathf.Sin(i*1.9f)));
            }
            Part(visual,name,Shapes.Tube(path,radius,6),bark);
        }
        // Crystals grown out of the rock in a clump, leaning every way round `toward`.
        private static void Cluster(Transform visual,Material glow,string name,Vector3 at,Vector3 toward,int count,float size,int seed)
        {
            for(int i=0;i<count;i++)
            {
                float k=i==0?1:0.45f+0.4f*(Shapes.Scatter(i,1,seed)*0.5f+0.5f);
                Mesh mesh=Shapes.Crystal(size*0.16f*k,size*k);
                Vector3 lean=(toward.normalized+new Vector3(Shapes.Scatter(i,2,seed),Shapes.Scatter(i,3,seed)*0.4f,Shapes.Scatter(i,4,seed))*0.55f).normalized;
                GameObject go=Part(visual,name+i,mesh,glow,false);
                go.transform.localPosition=at+new Vector3(Shapes.Scatter(i,5,seed),0,Shapes.Scatter(i,6,seed))*size*0.25f+lean*size*k*0.35f;
                go.transform.localRotation=Quaternion.FromToRotation(Vector3.up,lean);
            }
        }

        private static GameObject Part(Transform parent,string name,Mesh mesh,Material material,bool shadows=true)
        {
            var go=new GameObject(name);
            go.transform.SetParent(parent,false);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var r=go.AddComponent<MeshRenderer>();
            if(material!=null)r.sharedMaterial=material;
            r.shadowCastingMode=shadows?UnityEngine.Rendering.ShadowCastingMode.On:UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        // Motes of light drifting slowly up in front of the glass; each portal tints them.
        private static void Motes(Transform parent,GameObject vanilla)
        {
            Material spark=vanilla.GetComponentsInChildren<ParticleSystemRenderer>(true).Select(x=>x.sharedMaterial).FirstOrDefault(m=>m!=null&&m.name.Contains("sparc"))
                ??vanilla.GetComponentsInChildren<ParticleSystemRenderer>(true).Select(x=>x.sharedMaterial).FirstOrDefault(m=>m!=null);
            if(spark==null)return;
            var go=new GameObject("Motes");
            go.transform.SetParent(parent,false);
            go.transform.localPosition=new Vector3(0,(float)Policy.CentreY,0.12f);
            var ps=go.AddComponent<ParticleSystem>();
            ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;
            main.duration=5;main.loop=true;main.playOnAwake=true;
            main.startLifetime=new ParticleSystem.MinMaxCurve(3f,5f);
            main.startSpeed=0;
            main.startSize=new ParticleSystem.MinMaxCurve(0.012f,0.03f);
            main.simulationSpace=ParticleSystemSimulationSpace.World;
            main.maxParticles=40;
            main.startColor=Color.white;
            var emission=ps.emission;emission.rateOverTime=5;
            var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(2*(float)Policy.HalfWidth,(float)(Policy.Top-Policy.Bottom)*0.9f,0.2f);
            var velocity=ps.velocityOverLifetime;velocity.enabled=true;velocity.space=ParticleSystemSimulationSpace.Local;
            velocity.x=new ParticleSystem.MinMaxCurve(-0.03f,0.03f);velocity.y=new ParticleSystem.MinMaxCurve(0.04f,0.12f);velocity.z=new ParticleSystem.MinMaxCurve(-0.01f,0.03f);
            var noise=ps.noise;noise.enabled=true;noise.strength=0.05f;noise.frequency=0.4f;
            var fade=ps.colorOverLifetime;fade.enabled=true;
            var gradient=new Gradient();
            gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(0.9f,0.25f),new GradientAlphaKey(0.6f,0.7f),new GradientAlphaKey(0,1)});
            fade.color=gradient;
            var pr=go.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial=spark;pr.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
        }

        private static void Block(Transform visual,Transform solid,string name,Material material,Vector3 centre,Vector3 size,bool shadows=true)
        {
            if(!Boxes.TryGetValue(size,out Mesh mesh))Boxes[size]=mesh=Shapes.Box(size,1.2f);
            var go=new GameObject(name);
            go.transform.SetParent(visual,false);
            go.transform.localPosition=centre;
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var r=go.AddComponent<MeshRenderer>();
            if(material!=null)r.sharedMaterial=material;
            r.shadowCastingMode=shadows?UnityEngine.Rendering.ShadowCastingMode.On:UnityEngine.Rendering.ShadowCastingMode.Off;
            if(solid==null)return;
            var c=new GameObject(name);
            c.layer=solid.gameObject.layer;
            c.transform.SetParent(solid,false);
            c.transform.localPosition=centre;
            c.AddComponent<BoxCollider>().size=size;
        }

        private static Piece.Requirement Requirement(ZNetScene scene,string item,int amount)
        {
            ItemDrop drop=scene.GetPrefab(item)?.GetComponent<ItemDrop>();
            if(drop==null){Debug.LogWarning("[LocalPortals] Missing ingredient "+item);return null;}
            return new Piece.Requirement{m_resItem=drop,m_amount=amount,m_recover=true};
        }

        // The prefab's own building material: pieces also carry a snow overlay mesh, which is skipped.
        private static Material MaterialOf(ZNetScene scene,GameObject fallback,params string[] prefabs)
        {
            foreach(string name in prefabs)
            {
                Material m=Wood(scene.GetPrefab(name));
                if(m!=null)return m;
            }
            return Wood(fallback);
        }
        private static Material Wood(GameObject prefab)
        {
            if(prefab==null)return null;
            var all=prefab.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r=>r.sharedMaterials)
                .Where(m=>m!=null&&m.shader!=null&&!m.name.ToLower().Contains("snow")&&!m.shader.name.ToLower().Contains("snow")).ToList();
            return all.FirstOrDefault(m=>m.shader.name.Contains("Piece"))??all.FirstOrDefault();
        }
        // The wild one's rock: the game's boulder stone, greyer and darker (its moss less yellow).
        private static Material _stone;
        private static Material Stone(Material rock)
        {
            if(_stone!=null||rock==null)return _stone??rock;
            _stone=new Material(rock){name="LocalPortalStone"};
            if(_stone.HasProperty("_Color"))_stone.color=new Color(0.86f,0.86f,0.83f,1);
            if(_stone.HasProperty("_MossColor"))_stone.SetColor("_MossColor",_stone.GetColor("_MossColor")*0.6f);
            return _stone;
        }

        // The root's bark: an oak log's, darker and older.
        private static Material _bark;
        private static Material Bark(Material log)
        {
            if(_bark!=null||log==null)return _bark??log;
            _bark=new Material(log){name="LocalPortalBark"};
            if(_bark.HasProperty("_Color"))_bark.color=new Color(0.42f,0.36f,0.3f,1);
            if(_bark.HasProperty("_Glossiness"))_bark.SetFloat("_Glossiness",0.05f);
            return _bark;
        }

        // The inlay: a copy of the wood's material (a shader the game surely has), untextured and glowing; each portal tints it.
        private static Material GlowMaterial(Material wood)
        {
            if(_glowMaterial!=null)return _glowMaterial;
            if(wood==null)return null;
            _glowMaterial=new Material(wood){name="LocalPortalGlow"};
            _glowMaterial.mainTexture=Texture2D.whiteTexture; // (none at all and the piece shader cuts it away as see-through)
            if(_glowMaterial.HasProperty("_Color"))_glowMaterial.color=Color.white;
            _glowMaterial.EnableKeyword("_EMISSION");
            if(_glowMaterial.HasProperty("_EmissionMap"))_glowMaterial.SetTexture("_EmissionMap",Texture2D.whiteTexture);
            return _glowMaterial;
        }

        // A hot reload leaves portals already in the world running the old copy's code: give them this copy's, and this
        // copy's model if they were built with an older one.
        internal static void Refresh()
        {
            if(ZNetScene.instance==null||_prefab==null)return;
            foreach(ZNetView view in Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None))
            {
                if(view==null||!view.IsValid()||view.GetComponent<LocalPortal>()!=null)continue;
                int hash=view.GetZDO().GetPrefab();
                GameObject template=hash==Hash?_prefab:hash==WildHash?_wild:null;
                if(template==null)continue;
                foreach(MonoBehaviour stale in view.GetComponents<MonoBehaviour>())
                    if(stale!=null&&stale.GetType().FullName==typeof(LocalPortal).FullName)Object.DestroyImmediate(stale);
                if(view.transform.Find("Visual/"+(template==_wild?WildModelVersion:ModelVersion))==null)Remodel(view.transform,template);
                view.gameObject.AddComponent<LocalPortal>();
            }
        }
        private static void Remodel(Transform portal,GameObject template)
        {
            foreach(string child in new[]{"Visual","Frame","RingColliders"})
            {
                Transform old=portal.Find(child);
                if(old!=null)Object.DestroyImmediate(old.gameObject);
            }
            foreach(Collider c in portal.GetComponents<Collider>())Object.DestroyImmediate(c);
            foreach(string child in new[]{"Visual","Frame"})
            {
                GameObject copy=Object.Instantiate(template.transform.Find(child).gameObject,portal,false);
                copy.name=child;
            }
            var wear=portal.GetComponent<WearNTear>();
            if(wear==null)return;
            GameObject visual=portal.Find("Visual").gameObject;
            wear.m_new=wear.m_worn=wear.m_broken=visual;
            wear.m_materialType=WearNTear.MaterialType.Wood;
            AccessTools.Method(typeof(WearNTear),"SetupColliders")?.Invoke(wear,null); // its support check keeps a list of its colliders
            // and its wear and highlighting keep lists of its renderers
            var renderers=AccessTools.Method(typeof(WearNTear),"GetHighlightRenderers")?.Invoke(wear,null);
            if(renderers!=null)AccessTools.Field(typeof(WearNTear),"m_renderers")?.SetValue(wear,renderers);
            AccessTools.Field(typeof(WearNTear),"m_oldMaterials")?.SetValue(wear,null);
        }

        internal static void Register(ZNetScene scene)
        {
            GameObject prefab;
            try{prefab=Build(scene);}
            catch(System.Exception e) // never stop a world from loading
            {
                Debug.LogError("[LocalPortals] Could not make the local portal; it is left out: "+e);
                if(_holder!=null)Object.Destroy(_holder);
                _holder=null;_prefab=null;_wild=null;
                return;
            }
            if(prefab==null)return;
            var named=(Dictionary<int,GameObject>)AccessTools.Field(typeof(ZNetScene),"m_namedPrefabs").GetValue(scene);
            foreach(GameObject p in new[]{prefab,_wild})
            {
                if(p==null)continue;
                scene.m_prefabs.RemoveAll(x=>x!=p&&x!=null&&x.name==p.name); // an earlier copy's, left registered by its unload
                if(!scene.m_prefabs.Contains(p))scene.m_prefabs.Add(p);
                named[p.name.GetStableHashCode()]=p; // a host without it deletes saved portals as invalid
            }
            AddToHammer();
        }
        internal static void AddToHammer()
        {
            if(_prefab==null||ObjectDB.instance==null)return;
            PieceTable table=ObjectDB.instance.GetItemPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;
            if(table==null)return;
            table.m_pieces.RemoveAll(p=>p==null||(p!=_prefab&&p.name==Name)); // an earlier copy's destroyed or stale entry
            if(table.m_pieces.Contains(_prefab))return;
            int vanilla=table.m_pieces.FindIndex(p=>p!=null&&p.name=="portal_wood");
            if(vanilla>=0)table.m_pieces.Insert(vanilla+1,_prefab);else table.m_pieces.Add(_prefab);
        }
        // Unloading: out of the hammer's menu. In a world the piece stays known to the game until the next copy's Register replaces
        // it, so no area loading in between loses its portals (the host deletes what it cannot build).
        internal static void Unregister()
        {
            if(_prefab==null)return;
            PieceTable table=ObjectDB.instance?.GetItemPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;
            table?.m_pieces.Remove(_prefab);
            if(ZNetScene.instance==null)Object.Destroy(_holder);
            _holder=null;_prefab=null;_wild=null;
        }
    }
}
