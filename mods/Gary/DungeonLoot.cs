using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace Gary
{
    internal static class DungeonLoot
    {
        internal const string Cleared="bob_gary_cleared_v1";
        private static readonly HashSet<string> Materials=new HashSet<string>(StringComparer.Ordinal)
        {"SurtlingCore","IronScrap","CopperOre","TinOre","SilverOre","Crystal","WolfHairBundle","WolfClaw","JuteRed","JuteBlue","SoftTissue","BlackCore","WitheredBone","FrostCore","Obsidian"};
        private static Player _player;
        private static string _world,_activeId,_emptyId;
        private static Vector3 _interior;
        private static float _next,_emptySince,_hintUntil,_lastError;
        private static readonly Dictionary<string,float> EmptyScans=new Dictionary<string,float>();
        private static List<string> _cleared=new List<string>();
        private static string World => "bob_gary_loot_v1_"+ZNet.instance.GetWorldUID().ToString(CultureInfo.InvariantCulture);
        internal static void Enter(Player p,Teleport door)
        {
            if(p!=Player.m_localPlayer||door.m_targetPoint==null||door.m_targetPoint.transform.position.y-door.transform.position.y<1000)return;
            Load(p);_activeId=Guide.Id(door.transform.position);_interior=door.m_targetPoint.transform.position;_hintUntil=Time.time+15;
            p.m_customData[_world+"_inside"]=_activeId+"|"+_interior.x.ToString("R",CultureInfo.InvariantCulture)+"|"+
                _interior.y.ToString("R",CultureInfo.InvariantCulture)+"|"+_interior.z.ToString("R",CultureInfo.InvariantCulture);
        }
        private static void Load(Player p)
        {
            string world=World;if(_player==p&&_world==world)return;
            Clear();_player=p;_world=world;
            _cleared=LootPolicy.Ledger(p.m_customData.TryGetValue(world,out string saved)?saved:"");
            if(p.m_customData.TryGetValue(world+"_inside",out string inside)&&inside.Length<150)
            {
                string[] parts=inside.Split('|');
                if(parts.Length==4&&LootPolicy.ValidId(parts[0])&&float.TryParse(parts[1],NumberStyles.Float,CultureInfo.InvariantCulture,out float x)&&
                    float.TryParse(parts[2],NumberStyles.Float,CultureInfo.InvariantCulture,out float y)&&float.TryParse(parts[3],NumberStyles.Float,CultureInfo.InvariantCulture,out float z)&&
                    Finite(x)&&Finite(y)&&Finite(z))
                {_activeId=parts[0];_interior=new Vector3(x,y,z);}
            }
        }
        internal static void Tick()
        {
            if(Time.time<_next)return;_next=Time.time+2;
            Player p=Player.m_localPlayer;
            if(p==null||ZNet.instance==null||ZNetScene.instance==null||ZDOMan.instance==null||ZoneSystem.instance==null)return;
            ZDO player=Companion.Data(p);if(player==null||!p.GetComponent<ZNetView>().IsOwner())return;
            Load(p);_next=Time.time+2;
            // Mirror only the owner's saved knowledge into native player state for Gary's simulator.
            string saved=string.Join(";",_cleared);
            if(player.GetString(Cleared,"")!=saved)player.Set(Cleared,saved);
            if(p.IsTeleporting()||p.IsDead()){_emptyId=null;return;}
            if(!p.InInterior())
            {
                if(Time.time>=_hintUntil){_activeId=null;p.m_customData.Remove(_world+"_inside");}
                _emptyId=null;return;
            }
            if(_activeId==null||Vector3.Distance(p.transform.position,_interior)>350){_emptyId=null;return;}
            LootState state=Inspect(_interior);
            if(state!=LootState.Empty)_emptyId=null;
            else if(_emptyId!=_activeId){_emptyId=_activeId;_emptySince=Time.time;return;}
            else if(Time.time-_emptySince<6)return;
            if(LootPolicy.Update(_cleared,_activeId,state))
            {
                saved=string.Join(";",_cleared);p.m_customData[_world]=saved;player.Set(Cleared,saved);
            }
        }
        internal static bool IsCleared(Player p,string id) => LootPolicy.Ledger(Companion.Data(p)?.GetString(Cleared,"")).Contains(id);
        internal static bool Eligible(Player player,string id,Vector3 interior)
        {
            LootState state=Inspect(interior);
            string key=ZNet.instance.GetWorldUID().ToString(CultureInfo.InvariantCulture)+"|"+id;
            if(state==LootState.Remaining){EmptyScans.Remove(key);return true;}
            if(state==LootState.Unknown){EmptyScans.Remove(key);return !IsCleared(player,id);}
            // Wait for repeated ready scans before filtering a previously unseen empty interior.
            if(!EmptyScans.TryGetValue(key,out float since))
            {if(EmptyScans.Count>=128)EmptyScans.Clear();EmptyScans[key]=Time.time;return !IsCleared(player,id);}
            return Time.time-since<6&&!IsCleared(player,id);
        }
        internal static LootState Inspect(Vector3 interior)
        {
            try{return Scan(interior);}
            catch(Exception e)
            {if(Time.time-_lastError>10){_lastError=Time.time;Plugin.Instance?.Error(e);}return LootState.Unknown;}
        }
        private static LootState Scan(Vector3 interior)
        {
            if(ZNetScene.instance==null||ZDOMan.instance==null||ZoneSystem.instance==null)return LootState.Unknown;
            // Read loaded geometry only. Never generate a dungeon or load room assets for inspection.
            DungeonGenerator selected=null;Bounds bounds=default;
            foreach(DungeonGenerator generator in UnityEngine.Object.FindObjectsByType<DungeonGenerator>(FindObjectsSortMode.None))
            {
                if(!generator.isActiveAndEnabled||generator.m_algorithm!=DungeonGenerator.Algorithm.Dungeon)continue;
                Room[] rooms=generator.GetComponentsInChildren<Room>();if(rooms.Length==0||rooms.Length>LootPolicy.MaxRooms)continue;
                Bounds candidate=RoomBounds(rooms);
                if(!candidate.Contains(interior))continue;
                if(selected!=null)return LootState.Unknown;
                selected=generator;bounds=candidate;
            }
            if(selected!=null)
            {
                Room[] loaded=selected.GetComponentsInChildren<Room>();ZDO data=Companion.Data(selected);
                if(data==null||!Complete(selected,data,loaded))return LootState.Unknown;
            }
            else if(!FixedCave(interior,out bounds))return LootState.Unknown;
            if(bounds.size.x>256||bounds.size.z>256||bounds.size.y>350)return LootState.Unknown;
            Vector2s min=ZoneSystem.GetZone(bounds.min),max=ZoneSystem.GetZone(bounds.max);
            if((max.x-min.x+1)*(max.y-min.y+1)>25)return LootState.Unknown;
            LootState result=LootState.Empty;
            var objects=new List<ZDO>();int inspected=0;
            for(int x=min.x;x<=max.x;x++)for(int z=min.y;z<=max.y;z++)
            {
                var sector=new Vector2s((short)x,(short)z);
                Vector3 point=ZoneSystem.GetZonePos(sector);point.y=interior.y;
                if(!ZNetScene.instance.IsAreaReady(point))return LootState.Unknown;
                objects.Clear();ZDOMan.instance.FindSectorObjects(sector,new SimulationDistance(0,0),objects);
                if(objects.Count>8192)return LootState.Unknown;
                foreach(ZDO obj in objects)
                {
                    if(!bounds.Contains(obj.GetPosition()))continue;
                    if(++inspected>8192)return LootState.Unknown;
                    GameObject go=ZNetScene.instance.FindInstance(obj.m_uid);
                    if(go==null){result=LootPolicy.Merge(result,LootState.Unknown);continue;}
                    result=LootPolicy.Merge(result,ObjectLoot(go));
                    if(result==LootState.Remaining)return result;
                }
            }
            return result;
        }
        private static bool FixedCave(Vector3 interior,out Bounds bounds)
        {
            // Troll caves use authored geometry rather than a DungeonGenerator. Their EnvZone is not the cave.
            bounds=default;bool found=false;
            foreach(Location location in UnityEngine.Object.FindObjectsByType<Location>(FindObjectsSortMode.None))
            {
                if(!location.isActiveAndEnabled||!location.m_hasInterior||location.m_generator!=null||
                    !location.gameObject.name.StartsWith("TrollCave",StringComparison.Ordinal))continue;
                Transform root=location.transform.Find("Interior");if(root==null||!root.gameObject.activeInHierarchy)continue;
                bool geometry=false;Bounds candidate=new Bounds(root.position,Vector3.zero);
                foreach(Collider collider in root.GetComponentsInChildren<Collider>())
                {
                    if(!collider.enabled||collider.isTrigger)continue;
                    candidate.Encapsulate(collider.bounds);geometry=true;
                }
                if(!geometry)continue;
                // Include the real exit and all potential separate network object origins.
                foreach(Teleport door in root.GetComponentsInChildren<Teleport>())candidate.Encapsulate(door.transform.position);
                foreach(ZNetView view in root.GetComponentsInChildren<ZNetView>(true))
                {
                    // Ignore authored alternate rooms that the native location did not activate.
                    Transform branch=view.transform;
                    while(branch.parent!=null&&branch.parent!=root)branch=branch.parent;
                    if(branch.gameObject.activeSelf)candidate.Encapsulate(view.transform.position);
                }
                candidate.Expand(8);if(!candidate.Contains(interior))continue;
                if(found)return false;found=true;bounds=candidate;
            }
            return found;
        }
        private static bool Complete(DungeonGenerator generator,ZDO data,Room[] rooms)
        {
            if((int)AccessTools.Field(typeof(DungeonGenerator),"m_roomsToLoad").GetValue(generator)>0)return false;
            Array pending=AccessTools.Field(typeof(DungeonGenerator),"m_loadedRooms").GetValue(generator) as Array;
            if(pending!=null&&pending.Length>0)return false;
            byte[] saved=data.GetByteArray(ZDOVars.s_roomData);int count=LootPolicy.RoomCount(saved);
            // Legacy or missing layouts are unknown, rather than assumed complete.
            if(count==0||count!=rooms.Length)return false;
            var used=new HashSet<Room>();
            using(var reader=new BinaryReader(new MemoryStream(saved,false)))
            {
                reader.ReadInt32();
                for(int i=0;i<count;i++)
                {
                    int hash=reader.ReadInt32();var pos=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
                    var rot=new Quaternion(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
                    if(!Finite(pos.x)||!Finite(pos.y)||!Finite(pos.z)||!Finite(rot.x)||!Finite(rot.y)||!Finite(rot.z)||!Finite(rot.w))return false;
                    Room match=null;
                    foreach(Room room in rooms)if(!used.Contains(room)&&room.GetHash()==hash&&Vector3.Distance(room.transform.position,pos)<0.05f&&Quaternion.Angle(room.transform.rotation,rot)<0.1f){match=room;break;}
                    if(match==null)return false;used.Add(match);
                }
            }
            return true;
        }
        private static Bounds RoomBounds(Room[] rooms)
        {
            Bounds bounds=new Bounds(rooms[0].transform.position,Vector3.zero);
            foreach(Room room in rooms)
            {
                Vector3 half=(Vector3)room.m_size*0.5f+Vector3.one*2;
                for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
                    bounds.Encapsulate(room.transform.TransformPoint(new Vector3(half.x*x,half.y*y,half.z*z)));
                foreach(ZNetView view in room.GetComponentsInChildren<ZNetView>(true))bounds.Encapsulate(view.transform.position);
            }
            bounds.Expand(4);return bounds;
        }
        private static LootState ObjectLoot(GameObject go)
        {
            if(go.GetComponent<Character>()!=null)return LootState.Empty; // enemies and their future drops are not treasure
            LootState state=LootState.Empty;
            foreach(Container chest in go.GetComponentsInChildren<Container>())
            {
                ZNetView view=chest.m_rootObjectOverride!=null?chest.m_rootObjectOverride:chest.GetComponent<ZNetView>();
                ZDO z=view!=null&&view.IsValid()?view.GetZDO():null;
                state=LootPolicy.Merge(state,LootPolicy.Chest(z?.GetByteArray(ZDOVars.s_items),z?.GetBool(ZDOVars.s_addedDefaultItems,false)??false));
            }
            foreach(PickableItem pile in go.GetComponentsInChildren<PickableItem>())
                state=LootPolicy.Merge(state,pile.m_itemPrefab!=null?LootState.Remaining:LootState.Unknown);
            foreach(Pickable pick in go.GetComponentsInChildren<Pickable>())
            {
                if(pick.m_respawnTimeMinutes>0)continue; // regrowing mushrooms/food don't keep a dungeon unfinished
                ZDO z=Companion.Data(pick);
                if(z==null){state=LootPolicy.Merge(state,LootState.Unknown);continue;}
                if(!z.GetBool(ZDOVars.s_enabled,true)||z.GetBool(ZDOVars.s_picked,pick.m_defaultPicked))continue;
                if(pick.m_itemPrefab==null){state=LootPolicy.Merge(state,LootState.Unknown);continue;}
                if(Valuable(pick.m_itemPrefab)||Drops(pick.m_extraDrops))state=LootState.Remaining;
            }
            foreach(ItemStand stand in go.GetComponentsInChildren<ItemStand>())
            {
                if(!stand.m_canBeRemoved)continue;
                ZNetView view=stand.m_netViewOverride!=null?stand.m_netViewOverride:stand.GetComponent<ZNetView>();
                ZDO z=view!=null&&view.IsValid()?view.GetZDO():null;if(z==null){state=LootPolicy.Merge(state,LootState.Unknown);continue;}
                int item=z.GetInt(ZDOVars.s_item,0);if(item==0)continue;
                GameObject prefab=ZNetScene.instance.GetPrefab(item);
                state=LootPolicy.Merge(state,prefab==null?LootState.Unknown:Valuable(prefab)?LootState.Remaining:LootState.Empty);
            }
            foreach(ItemDrop drop in go.GetComponentsInChildren<ItemDrop>())
                if(drop.m_itemData.m_stack>0&&Valuable(drop.gameObject))state=LootState.Remaining;
            if(Resource(go,0,new HashSet<GameObject>()))state=LootState.Remaining;
            return state;
        }
        private static bool Valuable(GameObject prefab)
        {
            ItemDrop item=prefab!=null?prefab.GetComponent<ItemDrop>():null;
            return item!=null&&(item.m_itemData.m_shared.m_value>0||Materials.Contains(Utils.GetPrefabName(prefab)));
        }
        private static bool Drops(DropTable table)
        {
            if(table==null||table.m_dropChance<=0||table.m_dropMax<=0)return false;
            foreach(DropTable.DropData drop in table.m_drops)if(drop.m_stackMax>0&&Valuable(drop.m_item))return true;
            return false;
        }
        private static bool Resource(GameObject go,int depth,HashSet<GameObject> seen)
        {
            // Read drop definitions only; never roll random drops or instantiate a destruction stage.
            if(depth>4||!seen.Add(go))return true; // unresolved stages conservatively remain eligible
            MineRock rock=go.GetComponent<MineRock>();
            if(rock!=null&&Drops(rock.m_dropItems))
            {
                ZDO z=Companion.Data(rock);
                Collider[] areas=AccessTools.Field(typeof(MineRock),"m_hitAreas").GetValue(rock) as Collider[];
                if(z==null||areas==null||areas.Length==0||areas.Length>1024)return true;
                for(int i=0;i<areas.Length;i++)if(z.GetFloat("Health"+i,rock.GetHealth())>0)return true;
            }
            MineRock5 rock5=go.GetComponent<MineRock5>();if(rock5!=null&&Drops(rock5.m_dropItems))return true;
            DropOnDestroyed drops=go.GetComponent<DropOnDestroyed>();
            // Scrap is covered by mining stages, not ordinary iron gates; building salvage isn't dungeon loot.
            if(drops!=null&&Drops(drops.m_dropWhenDestroyed)&&Utils.GetPrefabName(go)!="dungeon_sunkencrypt_irongate_rusty")return true;
            Destructible destructible=go.GetComponent<Destructible>();
            return destructible!=null&&destructible.m_spawnWhenDestroyed!=null&&Resource(destructible.m_spawnWhenDestroyed,depth+1,seen);
        }
        private static bool Finite(float f) => !float.IsNaN(f)&&!float.IsInfinity(f);
        internal static void Clear()
        {_player=null;_world=_activeId=_emptyId=null;_next=_emptySince=_hintUntil=0;EmptyScans.Clear();_cleared=new List<string>();}
    }
}
