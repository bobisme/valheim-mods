using System;
using System.Linq;
using UnityEngine;

namespace Gary
{
    // Gary replants the forest around your home: now and then, while you are near your bed, he finds open wild ground nearby (beside
    // a stump you left, if there is one), walks over, works the soil for a moment and plants a real sapling of a tree that grows there.
    internal static class Reforest
    {
        internal const string PlantedAt="bob_gary_planted_at",Sapling="bob_gary_sapling",SeedKey="bob_gary_seed_";
        // His seed pouch: tree seeds and cones he picked up, one count per kind (ReforestPolicy.Saplings order).
        internal static int[] Pouch(ZDO z)=>Enumerable.Range(0,CarePolicy.Seeds.Length).Select(i=>z.GetInt(SeedKey+i,0)).ToArray();
        internal static bool AddSeed(ZDO z,int kind)
        {
            if(kind<0||kind>=CarePolicy.Seeds.Length||Pouch(z).Sum()>=CarePolicy.SeedCapacity)return false;
            z.Set(SeedKey+kind,z.GetInt(SeedKey+kind,0)+1);return true;
        }
        private static readonly int Space=LayerMask.GetMask("Default","static_solid","Default_small","piece","piece_nonsolid");
        private static readonly int Roof=LayerMask.GetMask("Default","static_solid","piece");
        private static readonly int Pieces=LayerMask.GetMask("piece","piece_nonsolid");

        internal static bool Tick(Companion.State st,MonsterAI ai,Player master,float dt)
        {
            Plugin plugin=Plugin.Instance;
            // Only the game that knows where its player sleeps: the player's own.
            if(!plugin.Reforestation.Value||master!=Player.m_localPlayer||Game.instance==null){st.PlantSpot=null;return false;}
            ZDO z=Companion.Data(st.Body);
            if(st.PlantSpot==null)
            {
                if(Time.time<st.NextPlantLook)return false;
                st.NextPlantLook=Time.time+30;
                PlayerProfile profile=Game.instance.GetPlayerProfile();
                if(profile==null||!profile.HaveCustomSpawnPoint())return false;
                Vector3 bed=profile.GetCustomSpawnPoint();
                float radius=plugin.ReforestRadius.Value;
                double since=(Companion.Now-z.GetLong(PlantedAt,0))/(double)TimeSpan.TicksPerSecond,interval=plugin.ReforestMinutes.Value*60;
                int young=Young(bed,radius);int[] seeds=Pouch(z);
                if(!ReforestPolicy.Ready(true,Utils.DistanceXZ(master.transform.position,bed),radius,since,interval,young,plugin.ReforestMax.Value,seeds.Sum()>0))return false;
                bool needSeed=ReforestPolicy.NeedsSeed(since,interval,young,plugin.ReforestMax.Value);
                if(!Find(st,ai,master,bed,radius,needSeed?seeds:null,out Vector3 spot,out int kind))return false;
                st.PlantSpot=spot;st.PlantKind=kind;st.PlantSeed=needSeed;st.PlantUntil=Time.time+(float)ReforestPolicy.GiveUpSeconds;st.PlantStarted=0;
                st.StuckTime=0;st.LastPosition=st.Body.transform.position;
            }
            Vector3 target=st.PlantSpot.Value;
            if(Time.time>st.PlantUntil||Vector3.Distance(master.transform.position,target)>30){Stop(st,60);return false;}
            Brain.Status(st,"planting a tree");
            if(Utils.DistanceXZ(st.Body.transform.position,target)>1.4f)
            {
                Brain.Move(ai,dt,target,1.0f,false);
                if(Vector3.Distance(st.Body.transform.position,st.LastPosition)<0.1f)st.StuckTime+=dt;
                else{st.StuckTime=0;st.LastPosition=st.Body.transform.position;}
                if(st.StuckTime>4)Stop(st,60);
                return true;
            }
            ai.StopMoving();Activities.Look(ai,target);
            if(st.PlantStarted==0){st.PlantStarted=Time.time;Personality.CancelVibe(st);Activities.Pose(st,Activities.Work,(float)ReforestPolicy.PlantSeconds+0.5f);return true;}
            if(Time.time-st.PlantStarted<ReforestPolicy.PlantSeconds)return true;
            Plant(st,master,target,st.PlantKind);
            return true;
        }
        private static void Stop(Companion.State st,float wait){st.PlantSpot=null;st.NextPlantLook=Time.time+wait;}

        // His saplings still growing around home (only those loaded: home is where you are).
        private static int Young(Vector3 bed,float radius)=>
            UnityEngine.Object.FindObjectsByType<global::Plant>(FindObjectsSortMode.None).Count(p=>p!=null&&Utils.DistanceXZ(p.transform.position,bed)<=radius&&
                Companion.Data(p)?.GetBool(Sapling,false)==true);

        // seeds: only trees he has a seed for (a bonus planting); null: any tree that grows there (his free one).
        private static bool Find(Companion.State st,MonsterAI ai,Player master,Vector3 bed,float radius,int[] seeds,out Vector3 spot,out int kind)
        {
            spot=Vector3.zero;kind=-1;
            Vector3 from=st.Body.transform.position;
            // First choice: beside a stump you left, within a short walk.
            var stumps=UnityEngine.Object.FindObjectsByType<Destructible>(FindObjectsSortMode.None)
                .Where(d=>d!=null&&ReforestPolicy.StumpKind(d.name)>=0&&Vector3.Distance(d.transform.position,from)<=25&&Utils.DistanceXZ(d.transform.position,bed)<=radius)
                .OrderBy(d=>Vector3.Distance(d.transform.position,from)).Take(6).ToList();
            foreach(Destructible stump in stumps)
                for(int i=0;i<6;i++)
                {
                    Vector3 candidate=stump.transform.position+Quaternion.Euler(0,UnityEngine.Random.Range(0,360f),0)*Vector3.forward*UnityEngine.Random.Range(2.2f,3.5f);
                    if(Valid(ai,master,bed,radius,candidate,ReforestPolicy.StumpKind(stump.name),seeds,out spot,out kind))return true;
                }
            // Otherwise any fair open ground near him.
            for(int i=0;i<14;i++)
            {
                Vector3 candidate=from+Quaternion.Euler(0,UnityEngine.Random.Range(0,360f),0)*Vector3.forward*UnityEngine.Random.Range(6f,20f);
                if(Valid(ai,master,bed,radius,candidate,-1,seeds,out spot,out kind))return true;
            }
            return false;
        }
        private static bool Valid(MonsterAI ai,Player master,Vector3 bed,float radius,Vector3 candidate,int stumpKind,int[] seeds,out Vector3 spot,out int kind)
        {
            kind=-1;
            if(!Nature.Ground(ai,candidate,out spot)||!Nature.Allowed(spot,master))return false;
            spot.y-=0.1f; // Nature.Ground lifts the spot for walking
            Heightmap ground=Heightmap.FindHeightmap(spot);
            if(ground==null)return false;
            if(!Physics.Raycast(spot+Vector3.up*2,Vector3.down,out RaycastHit hit,4,LayerMask.GetMask("terrain"),QueryTriggerInteraction.Ignore)||hit.normal.y<0.8f)return false; // only gentle slopes
            float nearest=float.MaxValue;
            foreach(Collider c in Physics.OverlapSphere(spot,(float)ReforestPolicy.MinFromBuilding,Pieces,QueryTriggerInteraction.Ignore))
                if(c.GetComponentInParent<Piece>() is Piece piece&&piece.GetCreator()!=0)nearest=Mathf.Min(nearest,Vector3.Distance(c.ClosestPoint(spot),spot));
            int biome=(int)WorldGenerator.instance.GetBiome(spot);
            var grows=ReforestPolicy.Saplings.Select(name=>ZNetScene.instance.GetPrefab(name)?.GetComponent<global::Plant>() is global::Plant p&&((int)p.m_biome&biome)!=0).ToArray();
            kind=seeds!=null?ReforestPolicy.ChooseSeeded(stumpKind,grows,seeds,UnityEngine.Random.value):ReforestPolicy.Choose(stumpKind,grows,UnityEngine.Random.value);
            if(kind<0)return false;
            global::Plant plant=ZNetScene.instance.GetPrefab(ReforestPolicy.Saplings[kind]).GetComponent<global::Plant>();
            bool crowded=Physics.OverlapSphere(spot+Vector3.up*0.5f,Mathf.Max(1.5f,plant.m_growRadius+0.5f),Space,QueryTriggerInteraction.Ignore).Length>0;
            bool roofed=Physics.Raycast(spot+Vector3.up*0.5f,Vector3.up,100,Roof,QueryTriggerInteraction.Ignore);
            return ReforestPolicy.Spot(Utils.DistanceXZ(spot,bed),radius,nearest,EffectArea.IsPointInsideArea(spot,EffectArea.Type.PlayerBase,2)!=null,
                ground.IsCultivated(spot),ground.IsCleared(spot),roofed,crowded,spot.y<ZoneSystem.instance.m_waterLevel+1);
        }
        private static void Plant(Companion.State st,Player master,Vector3 spot,int kind)
        {
            ZDO z=Companion.Data(st.Body);
            st.PlantSpot=null;st.NextPlantLook=Time.time+30;
            GameObject prefab=kind>=0?ZNetScene.instance.GetPrefab(ReforestPolicy.Saplings[kind]):null;
            if(prefab==null)return;
            if(st.PlantSeed)
            {
                if(z.GetInt(SeedKey+kind,0)<=0)return; // the seed is gone (another game's copy of him used it)
                z.Set(SeedKey+kind,z.GetInt(SeedKey+kind,0)-1);
            }
            z.Set(PlantedAt,Companion.Now); // before planting: a reload cannot plant twice
            GameObject sapling=UnityEngine.Object.Instantiate(prefab,spot,Quaternion.Euler(0,UnityEngine.Random.Range(0,360f),0));
            Companion.Data(sapling.transform)?.Set(Sapling,true);
            Piece piece=sapling.GetComponent<Piece>();
            piece?.m_placeEffect.Create(spot,sapling.transform.rotation,sapling.transform);
            Personality.Mood(st,Personality.Cozy);
            if(master==Player.m_localPlayer)Plugin.Tell(st.PlantSeed?$"Planted a {ReforestPolicy.Names[kind]} from a seed I found!":$"Planted a little {ReforestPolicy.Names[kind]} tree!");
        }
    }
}
