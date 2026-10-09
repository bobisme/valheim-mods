using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;
using Random=UnityEngine.Random;
using Object=UnityEngine.Object;

namespace Gary
{
    // Gary's caretaking: small jobs with real things, one at a time, between following you around. Quick reactions (a falling tree,
    // an omen, the sky) come first; errands (seeds, deadfall, the harvest, crops to replant, stray fire, a tree to talk to, a spot to
    // show you, a trophy to look at) after his other activities.
    internal static class Chores
    {
        private const int Pickup=1,Deadfall=2,Replant=3,StompFire=4,TreeTalk=5,ShowSpot=6,Trophy=7,Deliver=8;
        internal const string BasketKey="bob_gary_basket",PileKey="bob_gary_pile",CarriedSince="bob_gary_carried_since";

        // ---- quick reactions ----
        internal static bool Urgent(Companion.State st,MonsterAI ai,Player master,float dt)
        {
            if(Time.time<st.TimberUntil)
            {
                // Out of the way of a falling tree.
                Vector3 away=st.Body.transform.position-st.TimberFrom;away.y=0;
                if(away.sqrMagnitude<0.01f)away=-st.Body.transform.forward;
                Brain.Move(ai,dt,st.Body.transform.position+away.normalized*6,0.5f,true);
                Brain.Status(st,"getting clear of the falling tree");
                return true;
            }
            if(Plugin.Instance.OmenSense.Value)Sense(st,master);
            return false;
        }

        // ---- errands ----
        internal static bool Tick(Companion.State st,MonsterAI ai,Player master,float dt)
        {
            if(master!=Player.m_localPlayer||Game.instance==null){st.Errand=0;return false;}
            if(st.Errand==0&&!Start(st,ai,master))return false;
            if(Time.time>st.ErrandUntil||Vector3.Distance(master.transform.position,st.Body.transform.position)>50){End(st,30);return false;}
            switch(st.Errand)
            {
                case Pickup:return DoPickup(st,ai,master,dt);
                case Deadfall:return DoDeadfall(st,ai,master,dt);
                case Replant:return DoReplant(st,ai,master,dt);
                case StompFire:return DoFire(st,ai,master,dt);
                case TreeTalk:return DoTree(st,ai,master,dt);
                case ShowSpot:return DoShow(st,ai,master,dt);
                case Trophy:return DoTrophy(st,ai,master,dt);
                case Deliver:return DoDeliver(st,ai,master,dt);
            }
            End(st,5);return false;
        }
        private static void Begin(Companion.State st,int kind,Component target,Vector3 spot,float seconds)
        {
            st.Errand=kind;st.ErrandTarget=target;st.ErrandSpot=spot;st.ErrandUntil=Time.time+seconds;st.ErrandStarted=0;
            st.StuckTime=0;st.LastPosition=st.Body.transform.position;
        }
        private static void End(Companion.State st,float rest){st.Errand=0;st.ErrandTarget=null;st.NextErrand=Time.time+rest;}
        // Walk to within reach; false while still walking (or given up when stuck).
        private static bool Reach(Companion.State st,MonsterAI ai,Vector3 target,float reach,float dt,bool run=false)
        {
            if(Utils.DistanceXZ(st.Body.transform.position,target)<=reach){ai.StopMoving();return true;}
            Brain.Move(ai,dt,target,Mathf.Max(0.5f,reach-0.3f),run);
            if(Vector3.Distance(st.Body.transform.position,st.LastPosition)<0.1f)st.StuckTime+=dt;
            else{st.StuckTime=0;st.LastPosition=st.Body.transform.position;}
            if(st.StuckTime>4)End(st,30);
            return false;
        }
        private static bool Work(Companion.State st,MonsterAI ai,Vector3 at,float seconds,int pose=Activities.Work)
        {
            Activities.Look(ai,at);
            if(st.ErrandStarted==0){st.ErrandStarted=Time.time;Personality.CancelVibe(st);Activities.Pose(st,pose,seconds+0.3f);return false;}
            return Time.time-st.ErrandStarted>=seconds;
        }
        private static Vector3? Bed()
        {
            PlayerProfile profile=Game.instance.GetPlayerProfile();
            return profile!=null&&profile.HaveCustomSpawnPoint()?profile.GetCustomSpawnPoint():(Vector3?)null;
        }
        private static bool Home(Vector3 at)
        {
            Vector3? bed=Bed();
            return bed!=null&&Utils.DistanceXZ(at,bed.Value)<=Plugin.Instance.ReforestRadius.Value;
        }

        // What to do next, most useful first.
        private static bool Start(Companion.State st,MonsterAI ai,Player master)
        {
            if(Time.time<st.NextErrand)return false;
            st.NextErrand=Time.time+3;
            Plugin p=Plugin.Instance;ZDO z=Companion.Data(st.Body);
            bool home=Home(master.transform.position);
            // Carrying enough: bring it back.
            if(Basket(z,BasketKey).Count>0||Basket(z,PileKey).Count>0)
            {
                double carried=(Companion.Now-z.GetLong(CarriedSince,Companion.Now))/(double)TimeSpan.TicksPerSecond;
                int count=CarePolicy.Count(Basket(z,BasketKey))+CarePolicy.Count(Basket(z,PileKey));
                if(CarePolicy.Deliver(count,carried)){Begin(st,Deliver,null,Vector3.zero,25);return true;}
            }
            if(p.StompFires.Value&&home&&FindFire(master) is Fire fire){Begin(st,StompFire,fire,fire.transform.position,20);Plugin.Tell("Fire! I'll stamp it out!");return true;}
            if(p.ReplantCrops.Value&&Replanting.Next(master) is Replanting.Spot spot){Begin(st,Replant,null,spot.Position,20);st.ErrandCrop=spot;return true;}
            if((p.SeedPouch.Value||p.HarvestHelp.Value)&&FindItem(st,master,home) is ItemDrop item){Begin(st,Pickup,item,item.transform.position,15);return true;}
            if(p.Deadfall.Value&&home&&Time.time>=st.NextDeadfall&&FindDeadfall(st,master) is Pickable fall){st.NextDeadfall=Time.time+Random.Range(60f,120f);Begin(st,Deadfall,fall,fall.transform.position,15);return true;}
            if(p.TrophyHall.Value&&Time.time>=st.NextTrophy&&FindTrophy(master) is ItemStand stand){st.NextTrophy=Time.time+Random.Range(300f,480f);Begin(st,Trophy,stand,stand.transform.position,20);return true;}
            if(p.TreeTalk.Value&&Time.time>=st.NextTreeTalk&&FindTree(st) is TreeBase tree){st.NextTreeTalk=Time.time+Random.Range(360f,600f);Begin(st,TreeTalk,tree,tree.transform.position,20);return true;}
            if(p.ShowSpots.Value&&Time.time>=st.NextShowSpot&&FindSpot(ai,master) is Pickable bush)
            {
                st.NextShowSpot=Time.time+Random.Range(600f,900f);Begin(st,ShowSpot,bush,bush.transform.position,60);st.ShownTarget=bush;
                Personality.Mood(st,Personality.Greeting);Plugin.Tell("Ooh! Come see what I found!");return true;
            }
            return false;
        }

        // ---- the basket: count-only, so only plain items go in (never custom data, quality or world-level differences) ----
        private static Dictionary<string,int> Basket(ZDO z,string key)=>CarePolicy.Basket(z.GetString(key,""));
        private static bool Plain(ItemDrop drop)
        {
            ItemDrop.ItemData item=drop.m_itemData;
            return item!=null&&item.m_dropPrefab!=null&&item.m_stack>0&&item.m_quality==1&&item.m_variant==0&&item.m_worldLevel==Game.m_worldLevel&&
                !item.m_cheated&&(item.m_customData==null||item.m_customData.Count==0);
        }
        private static ItemDrop FindItem(Companion.State st,Player master,bool home)
        {
            Plugin p=Plugin.Instance;ZDO z=Companion.Data(st.Body);
            bool pouchFull=Reforest.Pouch(z).Sum()>=CarePolicy.SeedCapacity,basketFull=CarePolicy.Count(Basket(z,BasketKey))>=CarePolicy.BasketCapacity;
            long now=ZNet.instance.GetTime().Ticks;
            ItemDrop best=null;float bestD=20;
            foreach(ItemDrop drop in Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None))
            {
                if(drop==null||!drop.isActiveAndEnabled||!drop.m_autoPickup||drop.IsPiece()||drop.InTar())continue;
                float d=Vector3.Distance(drop.transform.position,st.Body.transform.position);
                if(d>=bestD||Vector3.Distance(drop.transform.position,master.transform.position)>20)continue;
                ZNetView view=drop.GetComponent<ZNetView>();
                if(view==null||!view.IsValid()||!view.IsOwner()||view.GetZDO().GetBool(Nature.GiftDrop,false))continue;
                string name=Utils.GetPrefabName(drop.gameObject);
                bool seed=CarePolicy.SeedKind(name)>=0;
                if(seed?(!p.SeedPouch.Value||pouchFull):(!p.HarvestHelp.Value||basketFull||!home||!CarePolicy.Harvest.Contains(name)))continue;
                // The harvest: only what fell in the last few minutes, never things laid down long ago; and not at a player's feet.
                long spawned=view.GetZDO().GetLong(ZDOVars.s_spawnTime,0L);
                if(!seed&&(spawned==0||(now-spawned)/(double)TimeSpan.TicksPerSecond>300))continue;
                if(Player.GetAllPlayers().Any(pl=>pl!=null&&!pl.IsDead()&&Vector3.Distance(pl.transform.position,drop.transform.position)<3))continue;
                if(!Nature.Allowed(drop.transform.position,master)||!drop.CanPickup()||!Plain(drop))continue;
                best=drop;bestD=d;
            }
            return best;
        }
        private static bool DoPickup(Companion.State st,MonsterAI ai,Player master,float dt)
        {
            ItemDrop drop=st.ErrandTarget as ItemDrop;
            if(drop==null||!drop.isActiveAndEnabled){End(st,2);return false;}
            Brain.Status(st,"picking something up");
            if(!Reach(st,ai,drop.transform.position,1.3f,dt))return true;
            ZNetView view=drop.GetComponent<ZNetView>();ZDO z=Companion.Data(st.Body);
            if(view==null||!view.IsValid()||!view.IsOwner()||!Plain(drop)){End(st,2);return true;}
            string name=Utils.GetPrefabName(drop.gameObject);int seed=CarePolicy.SeedKind(name);
            int stack=drop.m_itemData.m_stack,taken=0;
            if(seed>=0)
            {
                // Seeds into his pouch, one at a time, while there is room.
                while(taken<stack&&Reforest.Pouch(z).Sum()<CarePolicy.SeedCapacity&&drop.RemoveOne()){Reforest.AddSeed(z,seed);taken++;}
                if(taken>0)Plugin.Tell(taken==1?"A seed! I'll plant it near home.":$"{taken} seeds! I'll plant them near home.");
            }
            else
            {
                var basket=Basket(z,BasketKey);
                int room=CarePolicy.BasketCapacity-CarePolicy.Count(basket);
                while(taken<stack&&taken<room&&drop.RemoveOne())taken++; // deplete the real stack before crediting the basket
                if(taken>0){Carry(z,BasketKey,basket,name,taken);}
            }
            End(st,1);ai.StopMoving();return true;
        }
        private static void Carry(ZDO z,string key,Dictionary<string,int> basket,string name,int count)
        {
            if(CarePolicy.Count(Basket(z,BasketKey))+CarePolicy.Count(Basket(z,PileKey))==0)z.Set(CarriedSince,Companion.Now);
            basket[name]=(basket.TryGetValue(name,out int had)?had:0)+count;
            z.Set(key,CarePolicy.Save(basket));
        }
        private static bool DoDeliver(Companion.State st,MonsterAI ai,Player master,float dt)
        {
            ZDO z=Companion.Data(st.Body);
            // Deadfall goes on his nest's wood pile, if he has one close by; everything else (and the deadfall without a nest) to you.
            Vector3 target=master.transform.position;string key=BasketKey;
            var pile=Basket(z,PileKey);
            if(pile.Count>0)
            {
                key=PileKey;
                ZDOID nest=z.GetZDOID(Activities.Home);
                GameObject woodpile=nest.IsNone()?null:ZNetScene.instance.FindInstance(nest);
                if(woodpile!=null&&Vector3.Distance(woodpile.transform.position,master.transform.position)<40)target=woodpile.transform.position;
            }
            else if(Basket(z,BasketKey).Count==0){End(st,2);return false;}
            Brain.Status(st,key==PileKey&&target!=master.transform.position?"stacking deadfall by my nest":"bringing you what I gathered");
            if(!Reach(st,ai,target,key==PileKey&&target!=master.transform.position?2.2f:2.5f,dt,true))return true;
            var items=Basket(z,key);
            z.Set(key,""); // spend before spawning: a reload cannot make a second copy
            foreach(var kv in items)
            {
                GameObject prefab=ZNetScene.instance.GetPrefab(kv.Key);
                if(prefab==null||prefab.GetComponent<ItemDrop>()==null)continue;
                int left=kv.Value;
                while(left>0)
                {
                    GameObject go=Object.Instantiate(prefab,target+Vector3.up*0.6f+Random.insideUnitSphere*0.4f,Quaternion.identity);
                    ItemDrop item=go.GetComponent<ItemDrop>();
                    int amount=Mathf.Min(left,item.m_itemData.m_shared.m_maxStackSize);
                    item.SetStack(amount);ItemDrop.OnCreateNew(item);
                    Companion.Data(item)?.Set(Nature.GiftDrop,true); // delivered: he never picks it back up
                    left-=amount;
                }
            }
            if(Basket(z,BasketKey).Count==0&&Basket(z,PileKey).Count==0)z.Set(CarriedSince,0L);
            Personality.Mood(st,Personality.Greeting);
            Plugin.Tell(key==PileKey&&target!=master.transform.position?"Stacked some deadfall by my nest.":"Here's what I gathered!");
            End(st,4);return true;
        }

        // ---- deadfall: branches and loose stones lying around home ----
        private static Pickable FindDeadfall(Companion.State st,Player master)
        {
            Pickable best=null;float bestD=25;
            foreach(Pickable p in Object.FindObjectsByType<Pickable>(FindObjectsSortMode.None))
            {
                if(p==null||!p.isActiveAndEnabled||p.GetPicked()||!p.CanBePicked())continue;
                float d=Vector3.Distance(p.transform.position,st.Body.transform.position);
                if(d>=bestD||Vector3.Distance(p.transform.position,master.transform.position)>25)continue;
                ZNetView view=p.GetComponent<ZNetView>();
                if(view==null||!view.IsValid()||!view.IsOwner()||!CarePolicy.Deadfall.Contains(Utils.GetPrefabName(p.gameObject))||!Home(p.transform.position)||!Nature.Allowed(p.transform.position,master))continue;
                best=p;bestD=d;
            }
            return best;
        }
        private static bool DoDeadfall(Companion.State st,MonsterAI ai,Player master,float dt)
        {
            Pickable p=st.ErrandTarget as Pickable;
            if(p==null||p.GetPicked()){End(st,2);return false;}
            Brain.Status(st,"tidying up deadfall");
            if(!Reach(st,ai,p.transform.position,1.6f,dt))return true;
            if(!Work(st,ai,p.transform.position,1.2f))return true;
            ZNetView view=p.GetComponent<ZNetView>();ZDO z=Companion.Data(st.Body);
            if(view==null||!view.IsValid()||!view.IsOwner()||p.m_itemPrefab==null){End(st,2);return true;}
            int amount=p.m_dontScale?p.m_amount:Mathf.Max(p.m_minAmountScaled,Game.instance.ScaleDrops(p.m_itemPrefab,p.m_amount));
            p.SetPicked(true);view.InvokeRPC(ZNetView.Everybody,"RPC_SetPicked",true); // deplete before crediting
            Carry(z,PileKey,Basket(z,PileKey),p.m_itemPrefab.name,amount);
            End(st,2);return true;
        }

        // ---- crops: replant what you harvested by hand, with seeds from your bag ----
        private static bool DoReplant(Companion.State st,MonsterAI ai,Player master,float dt)
        {
            Replanting.Spot spot=st.ErrandCrop;
            if(spot==null){End(st,2);return false;}
            Brain.Status(st,"replanting your crop");
            if(!Reach(st,ai,spot.Position,1.3f,dt))return true;
            if(!Work(st,ai,spot.Position,1.5f))return true;
            Replanting.Plant(master,spot);st.ErrandCrop=null;
            End(st,2);return true;
        }

        // ---- stray fire near your home ----
        private static Fire FindFire(Player master)
        {
            foreach(Fire fire in Fire.s_fires)
            {
                if(fire==null||Vector3.Distance(fire.transform.position,master.transform.position)>25)continue;
                // Only fire that threatens what you built.
                bool nearBuilding=Physics.OverlapSphere(fire.transform.position,12,LayerMask.GetMask("piece"),QueryTriggerInteraction.Ignore)
                    .Any(c=>c.GetComponentInParent<Piece>() is Piece piece&&piece.GetCreator()!=0);
                if(nearBuilding)return fire;
            }
            return null;
        }
        private static bool DoFire(Companion.State st,MonsterAI ai,Player master,float dt)
        {
            Fire fire=st.ErrandTarget as Fire;
            if(fire==null){End(st,2);return false;}
            Brain.Status(st,"stamping out a fire");
            if(!Reach(st,ai,fire.transform.position,1.8f,dt,true))return true;
            if(!Work(st,ai,fire.transform.position,1f,Activities.Dance))return true; // a stomping dance
            ZNetView view=fire.GetComponent<ZNetView>();
            if(view!=null&&view.IsValid()){view.ClaimOwnership();view.Destroy();}else Object.Destroy(fire.gameObject);
            Personality.Mood(st,Personality.Victory);Plugin.Tell("Fire's out!");
            End(st,3);return true;
        }

        // ---- talking to trees ----
        private static TreeBase FindTree(Companion.State st)
        {
            TreeBase best=null;float bestD=15;
            foreach(TreeBase tree in Object.FindObjectsByType<TreeBase>(FindObjectsSortMode.None))
            {
                if(tree==null||CarePolicy.TreeKind(tree.name)<0)continue;
                float d=Vector3.Distance(tree.transform.position,st.Body.transform.position);
                if(d<bestD){best=tree;bestD=d;}
            }
            return best;
        }
        private static bool DoTree(Companion.State st,MonsterAI ai,Player master,float dt)
        {
            TreeBase tree=st.ErrandTarget as TreeBase;
            if(tree==null){End(st,2);return false;}
            Vector3 trunk=tree.transform.position;
            Vector3 side=st.Body.transform.position-trunk;side.y=0;
            Vector3 stand=trunk+(side.sqrMagnitude>0.01f?side.normalized:Vector3.forward)*1.4f;
            Brain.Status(st,"listening to an old tree");
            if(!Reach(st,ai,stand,1f,dt))return true;
            if(!Work(st,ai,trunk+Vector3.up,3f))return true;
            int kind=CarePolicy.TreeKind(tree.name);string name=ReforestPolicy.Names[Math.Max(0,kind)];
            ZDO z=Companion.Data(st.Body);
            Vector3 foot=trunk+(stand-trunk).normalized*0.9f+Vector3.up*0.4f;
            switch(CarePolicy.Listen(Random.value))
            {
                case CarePolicy.TreeGift.Resin:Drop("Resin",Random.Range(1,3),foot);Plugin.Tell($"The old {name} gave us some resin.");break;
                case CarePolicy.TreeGift.Seed:
                    if(kind>=0&&Reforest.AddSeed(z,kind))Plugin.Tell($"The {name} gave me a seed to plant!");
                    else{Drop(CarePolicy.Seeds[Math.Max(0,kind)],1,foot);Plugin.Tell($"The {name} dropped a seed for you.");}
                    break;
                case CarePolicy.TreeGift.Feather:Drop("Feathers",1,foot);Plugin.Tell("A bird left a feather in the branches.");break;
            }
            Personality.Mood(st,Personality.Cozy);
            End(st,5);return true;
        }
        private static void Drop(string prefab,int count,Vector3 at)
        {
            GameObject source=ZNetScene.instance.GetPrefab(prefab);
            if(source==null||source.GetComponent<ItemDrop>()==null)return;
            ItemDrop item=Object.Instantiate(source,at,Quaternion.identity).GetComponent<ItemDrop>();
            item.SetStack(count);ItemDrop.OnCreateNew(item);
            Companion.Data(item)?.Set(Nature.GiftDrop,true); // his own gift: he will not pick it back up
        }

        // ---- showing you a spot ----
        private static readonly HashSet<string> Patches=new HashSet<string>{"RaspberryBush","BlueberryBush","CloudberryBush","Pickable_Mushroom","Pickable_Mushroom_yellow","Pickable_Mushroom_blue"};
        private static Pickable FindSpot(MonsterAI ai,Player master)
        {
            if(master.InInterior()||EffectArea.IsPointInsideArea(master.transform.position,EffectArea.Type.PlayerBase)!=null)return null; // out exploring, not at home
            Pickable best=null;float bestD=float.MaxValue;
            foreach(Pickable p in Object.FindObjectsByType<Pickable>(FindObjectsSortMode.None))
            {
                if(p==null||!p.isActiveAndEnabled||p.GetPicked()||!p.CanBePicked()||!Patches.Contains(Utils.GetPrefabName(p.gameObject)))continue;
                float d=Vector3.Distance(p.transform.position,master.transform.position);
                if(d<18||d>45||d>=bestD||!Nature.HavePath(ai,p.transform.position))continue;
                best=p;bestD=d;
            }
            return best;
        }
        private static bool DoShow(Companion.State st,MonsterAI ai,Player master,float dt)
        {
            Pickable p=st.ErrandTarget as Pickable;
            if(p==null||p.GetPicked()){st.ShownTarget=null;End(st,5);return false;}
            Brain.Status(st,"showing you something I found");
            if(!Reach(st,ai,p.transform.position,2f,dt,true))return true;
            Activities.Look(ai,master.GetCenterPoint());
            if(Vector3.Distance(master.transform.position,p.transform.position)<=5)
            {
                Activities.Pose(st,Activities.Cheer,2);Personality.Mood(st,Personality.Greeting);
                Plugin.Tell("Here! Isn't it nice?");st.ShownTarget=null;End(st,10);return true;
            }
            if(Time.time>=st.NextWave){st.NextWave=Time.time+5;Activities.Pose(st,Activities.Wave,1.5f);}
            return true;
        }

        // ---- trophies in your hall ----
        private static ItemStand FindTrophy(Player master)
        {
            if(EffectArea.IsPointInsideArea(master.transform.position,EffectArea.Type.PlayerBase)==null)return null;
            var stands=Object.FindObjectsByType<ItemStand>(FindObjectsSortMode.None)
                .Where(s=>s!=null&&s.m_guardianPower==null&&s.HaveAttachment()&&Vector3.Distance(s.transform.position,master.transform.position)<14).ToList();
            return stands.Count==0?null:stands[Random.Range(0,stands.Count)];
        }
        private static bool DoTrophy(Companion.State st,MonsterAI ai,Player master,float dt)
        {
            ItemStand stand=st.ErrandTarget as ItemStand;
            if(stand==null||!stand.HaveAttachment()){End(st,5);return false;}
            Vector3 at=stand.transform.position;
            Vector3 below=new Vector3(at.x,st.Body.transform.position.y,at.z)+(st.Body.transform.position-at).normalized*1.6f;
            Brain.Status(st,"looking at your trophies");
            if(!Reach(st,ai,below,1.2f,dt))return true;
            CarePolicy.Feeling feeling=CarePolicy.Trophy(ObjectDB.instance?.GetItemPrefab(stand.GetAttachedItem())?.name);
            int pose=feeling==CarePolicy.Feeling.Growl?Activities.Crouch:feeling==CarePolicy.Feeling.Sad?Activities.Curl:Activities.Cheer;
            if(!Work(st,ai,at,2.5f,pose))return true;
            if(feeling==CarePolicy.Feeling.Growl){Personality.Mood(st,Personality.Warning,"*growls at the troll*");Plugin.Tell("Grrr. I never liked trolls.");}
            else if(feeling==CarePolicy.Feeling.Sad)Plugin.Tell("...that one looks like my cousin.");
            else{Personality.Mood(st,Personality.Greeting);Plugin.Tell("We did that! Well, you did.");}
            End(st,5);return true;
        }

        // ---- omens and the sky (the Omens mod, found while the game runs; no reference) ----
        private static readonly Dictionary<long,float> Felt=new Dictionary<long,float>();
        private static Type _sign,_moon,_aurora,_storm;private static FieldInfo _loaded,_kind,_id;
        private static BepInEx.BaseUnityPlugin _omens;
        private static void Bind()
        {
            BepInEx.BaseUnityPlugin found=Chainloader.PluginInfos.TryGetValue("com.bobisme.omens",out var info)?info.Instance:null;
            if(found==_omens)return;
            _omens=found;_sign=_moon=_aurora=_storm=null;_loaded=_kind=_id=null;
            if(found==null)return;
            Assembly asm=found.GetType().Assembly;
            _sign=asm.GetType("Omens.OmenSign");_moon=asm.GetType("Omens.BloodMoon");_aurora=asm.GetType("Omens.Aurora");_storm=asm.GetType("Omens.Storm");
            const BindingFlags any=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
            _loaded=_sign?.GetField("Loaded",any);_kind=_sign?.GetField("Kind",any);_id=_sign?.GetField("Id",any);
        }
        private static bool Flag(Type t)=>t?.GetField("Active",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)?.GetValue(null) is bool b&&b;
        private static void Sense(Companion.State st,Player master)
        {
            if(Time.time<st.NextSense)return;
            st.NextSense=Time.time+2;
            try{Bind();}catch{_omens=null;}
            if(_omens==null)return;
            // A sign nearby: he bristles at a bad one and perks up at a good one.
            if(_loaded?.GetValue(null) is IList signs)
                foreach(object sign in signs)
                {
                    if(!(sign is Component c)||c==null||Vector3.Distance(c.transform.position,st.Body.transform.position)>15)continue;
                    long id=_id?.GetValue(sign) is long l?l:0;
                    if(Felt.TryGetValue(id,out float at)&&Time.time-at<600)continue;
                    Felt[id]=Time.time;
                    int feeling=CarePolicy.OmenFeeling(_kind?.GetValue(sign) is Enum e?Convert.ToInt32(e):-1);
                    if(feeling<0){Activities.Pose(st,Activities.Crouch,3);Personality.Mood(st,Personality.Warning,"*uneasy rustling*");Plugin.Tell("I don't like this place...");}
                    else if(feeling>0){Activities.Pose(st,Activities.Cheer,2);Personality.Mood(st,Personality.Greeting);Plugin.Tell("Something good is here! I can feel it!");}
                    return;
                }
            if(master.InInterior())return;
            bool night=EnvMan.IsNight();
            if(night&&Flag(_moon)&&Time.time>=st.NextSky)
            {st.NextSky=Time.time+180;Activities.Pose(st,Activities.Crouch,2.5f);Personality.Mood(st,Personality.Warning);Plugin.Tell("The moon is wrong. I'm staying close to you.");}
            else if(night&&Flag(_aurora)&&Time.time>=st.NextSky)
            {st.NextSky=Time.time+240;Activities.Pose(st,Activities.Dance,5);Personality.Mood(st,Personality.Greeting);Plugin.Tell("The sky is dancing! I'm dancing too!");}
            else if(Flag(_storm)&&Time.time>=st.NextSky)
            {st.NextSky=Time.time+60;Activities.Pose(st,Activities.Crouch,1.5f);Personality.Mood(st,Personality.Warning,"*flinches at the thunder*");}
        }

        // ---- a tree falling near you ----
        internal static void Timber(Vector3 at)
        {
            Player me=Player.m_localPlayer;
            if(me==null||Plugin.Instance==null||!Plugin.Instance.TimberWarnings.Value||Vector3.Distance(me.transform.position,at)>25)return;
            foreach(Companion.State st in Companion.States.Values)
            {
                if(st.Body==null||Companion.Owner(st.Body)!=me||!st.Body.GetComponent<ZNetView>().IsOwner()||Vector3.Distance(st.Body.transform.position,at)>25)continue;
                st.TimberFrom=at;st.TimberUntil=Time.time+3;
                if(Time.time>=st.NextTimberShout){st.NextTimberShout=Time.time+8;Personality.Mood(st,Personality.Warning);Plugin.Tell("Timber!");}
            }
        }
    }

    // Crops you harvested by hand, remembered for a few minutes so Gary can replant them with the same seeds from your bag.
    internal static class Replanting
    {
        internal sealed class Spot{public Vector3 Position;public string Sapling,Seed;public int Amount;public float At;}
        private static readonly List<Spot> Spots=new List<Spot>();
        private static Dictionary<string,(string sapling,ItemDrop seed,int amount)> _crops;

        // Which sapling grows into each harvestable crop, and the seed it costs, read from the game's own prefabs once.
        private static Dictionary<string,(string sapling,ItemDrop seed,int amount)> Crops()
        {
            if(_crops!=null||ZNetScene.instance==null)return _crops;
            _crops=new Dictionary<string,(string,ItemDrop,int)>();
            foreach(GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                Plant plant=prefab!=null?prefab.GetComponent<Plant>():null;Piece piece=prefab!=null?prefab.GetComponent<Piece>():null;
                if(plant==null||piece==null||!plant.m_needCultivatedGround||piece.m_resources==null||piece.m_resources.Length!=1||piece.m_resources[0].m_resItem==null)continue;
                foreach(GameObject grown in plant.m_grownPrefabs)
                    if(grown!=null&&!_crops.ContainsKey(grown.name))_crops[grown.name]=(prefab.name,piece.m_resources[0].m_resItem,piece.m_resources[0].m_amount);
            }
            return _crops;
        }
        internal static void Picked(Pickable p,Humanoid by)
        {
            if(by!=Player.m_localPlayer||Plugin.Instance==null||!Plugin.Instance.ReplantCrops.Value)return;
            var crops=Crops();string name=Utils.GetPrefabName(p.gameObject);
            if(crops==null||!crops.TryGetValue(name,out var crop))return;
            Heightmap ground=Heightmap.FindHeightmap(p.transform.position);
            if(ground==null||!ground.IsCultivated(p.transform.position))return;
            Spots.RemoveAll(s=>Time.time-s.At>300||Vector3.Distance(s.Position,p.transform.position)<0.3f);
            if(Spots.Count<40)Spots.Add(new Spot{Position=p.transform.position,Sapling=crop.sapling,Seed=crop.seed.name,Amount=crop.amount,At=Time.time});
        }
        // The next spot he can replant: harvested a few seconds ago (so Farmhand's own replanting goes first), near you, still empty,
        // and you carry the seeds.
        internal static Spot Next(Player master)
        {
            Spots.RemoveAll(s=>Time.time-s.At>300);
            foreach(Spot s in Spots.OrderBy(s=>Vector3.Distance(s.Position,master.transform.position)))
            {
                if(Time.time-s.At<5||Vector3.Distance(s.Position,master.transform.position)>20)continue;
                if(!Empty(s)){Spots.Remove(s);return Next(master);}
                if(SeedCount(master,s)<s.Amount)continue;
                Spots.Remove(s);return s;
            }
            return null;
        }
        private static bool Empty(Spot s)
        {
            foreach(Collider c in Physics.OverlapSphere(s.Position+Vector3.up*0.2f,0.45f,LayerMask.GetMask("Default","static_solid","Default_small","piece","piece_nonsolid","item"),QueryTriggerInteraction.Collide))
                if(c.GetComponentInParent<Plant>()!=null||c.GetComponentInParent<Pickable>()!=null)return false;
            return true;
        }
        private static int SeedCount(Player master,Spot s)
        {
            GameObject seed=ObjectDB.instance?.GetItemPrefab(s.Seed);
            string name=seed?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name;
            return name==null?0:master.GetInventory().CountItems(name);
        }
        internal static void Plant(Player master,Spot s)
        {
            GameObject prefab=ZNetScene.instance.GetPrefab(s.Sapling);
            Heightmap ground=Heightmap.FindHeightmap(s.Position);
            if(prefab==null||ground==null||!ground.IsCultivated(s.Position)||!Empty(s)||SeedCount(master,s)<s.Amount)return;
            string seedName=ObjectDB.instance.GetItemPrefab(s.Seed).GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
            master.GetInventory().RemoveItem(seedName,s.Amount); // the seed is spent before the crop appears
            GameObject crop=Object.Instantiate(prefab,s.Position,Quaternion.Euler(0,Random.Range(0,360f),0));
            Piece piece=crop.GetComponent<Piece>();
            if(piece!=null){piece.SetCreator(master.GetPlayerID(),Splatform.PlatformManager.DistributionPlatform.LocalUser.PlatformUserID);piece.m_placeEffect.Create(s.Position,crop.transform.rotation,crop.transform);}
            Plugin.Tell($"Replanted your {Localization.instance.Localize(prefab.GetComponent<Piece>()?.m_name??"crop").ToLowerInvariant()}.");
        }
    }

    [HarmonyPatch(typeof(Pickable),nameof(Pickable.Interact))]
    internal static class RememberHarvest
    {
        private static void Prefix(Pickable __instance,Humanoid character,bool repeat,out bool __state)=>__state=!repeat&&__instance!=null&&!__instance.GetPicked();
        private static void Postfix(Pickable __instance,Humanoid character,bool __result,bool __state){if(__state&&__result&&__instance!=null)Replanting.Picked(__instance,character);}
    }
    [HarmonyPatch(typeof(TreeLog),"Awake")]
    internal static class TimberWarning
    {
        private static void Postfix(TreeLog __instance){if(__instance!=null)Chores.Timber(__instance.transform.position);}
    }
}
