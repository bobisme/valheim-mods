using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BuildShapes
{
    public sealed partial class Plugin
    {
        // Explicit developer command for the disposable Creative world, never an ordinary design action.
        private static void TestSiteWorld(long world)
        {
            if(ZNet.instance==null || !ZNet.instance.IsServer() || ZNet.instance.GetWorldUID()!=world ||
                !string.Equals(ZNet.instance.GetWorldName(),"Creative",StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Test sites require the local Creative world and its exact world ID.");
        }
        private JObject FindTestSite(float size)
        {
            if(size<32 || size>64)throw new ArgumentException("Test sites are 32–64 m wide.");
            Vector3 origin=Player.m_localPlayer.transform.position,best=Vector3.zero;float score=float.MaxValue,range=0;
            for(int ring=192;ring<=768;ring+=64)for(int angle=0;angle<16;angle++)
            {
                Vector3 center=origin+new Vector3(Mathf.Cos(angle*Mathf.PI/8)*ring,0,Mathf.Sin(angle*Mathf.PI/8)*ring);
                center.x=Mathf.Round(center.x/16)*16;center.z=Mathf.Round(center.z/16)*16;
                var biome=WorldGenerator.instance.GetBiome(center);
                if(biome!=Heightmap.Biome.Meadows && biome!=Heightmap.Biome.BlackForest)continue;
                float low=float.MaxValue,high=float.MinValue;bool valid=true;
                for(float z=-size/2-4;z<=size/2+4;z+=4)for(float x=-size/2-4;x<=size/2+4;x+=4)
                {Vector3 p=center+new Vector3(x,0,z);float h=WorldGenerator.instance.GetHeight(p);low=Mathf.Min(low,h);high=Mathf.Max(high,h);if(h<ZoneSystem.instance.m_waterLevel+3 || Location.IsInsideNoBuildLocation(new Vector3(p.x,h,p.z)))valid=false;}
                if(!valid || high-low>10)continue;
                float cost=(high-low)*100+(biome==Heightmap.Biome.Meadows?0:20)+ring/100f;
                if(cost>=score)continue;score=cost;range=high-low;best=new Vector3(center.x,(low+high)/2,center.z);
            }
            if(score==float.MaxValue)throw new ArgumentException("No dry, gently sloped test site found nearby.");
            return new JObject{["world"]=ZNet.instance.GetWorldUID(),["center"]=new JArray(best.x,best.y,best.z),["size"]=size,["naturalRelief"]=range,["biome"]=WorldGenerator.instance.GetBiome(best).ToString()};
        }
        private int ClearTestSite(Vector3 center,float size,long world)
        {
            TestSiteWorld(world);
            Vector3 distance=center-Player.m_localPlayer.transform.position;distance.y=0;
            if(!V(center).Finite||size<32||size>64||distance.magnitude>70)throw new ArgumentException("Clear a loaded 32–64 m test site within 70 m.");
            int removed=0;
            foreach(var view in Resources.FindObjectsOfTypeAll<ZNetView>())
            {
                if(view==null||!view.gameObject.scene.IsValid()||!view.IsValid())continue;
                Vector3 p=view.transform.position;if(Mathf.Abs(p.x-center.x)>size/2+8||Mathf.Abs(p.z-center.z)>size/2+8)continue;
                if(view.GetComponent<Piece>()!=null||view.GetComponent<Character>()!=null)continue;
                string name=Utils.GetPrefabName(view.gameObject);
                if(view.GetComponent<TreeBase>()==null&&view.GetComponent<TreeLog>()==null&&view.GetComponent<MineRock>()==null&&view.GetComponent<MineRock5>()==null&&view.GetComponent<Pickable>()==null&&
                    !(view.GetComponent<Destructible>()!=null&&new[]{"Rock","Bush","Beech","Birch","FirTree","PineTree","Oak"}.Any(prefix=>name.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))) && !(view.GetComponent<ItemDrop>()!=null && new[]{"Wood","RoundLog","Stone","Resin","BeechSeeds","BirchSeeds","FirCone","PineCone"}.Contains(name)))continue;
                TestSiteWorld(world);if(!view.IsOwner())view.ClaimOwnership();if(!view.IsOwner())continue;
                var drops=view.GetComponent<DropOnDestroyed>();if(drops!=null)drops.m_dropWhenDestroyed=new DropTable();
                ZNetScene.instance.Destroy(view.gameObject);removed++;
            }
            return removed;
        }
        private IEnumerator MakeTestSite(Vector3 center,float size,long world,Action<JObject> output,Action<string> error)
        {
            TestSiteWorld(world);
            if(!V(center).Finite || Mathf.Abs(center.x)>10000 || Mathf.Abs(center.z)>10000 || size<32 || size>64 || center.y<ZoneSystem.instance.m_waterLevel+2 || center.y>100)
                throw new ArgumentException("Choose a dry 32–64 m test site with finite world coordinates.");
            Stop();
            if(!Player.m_localPlayer.TeleportTo(center+Vector3.up*2,Quaternion.identity,true)){error("The player cannot teleport right now.");yield break;}
            bool loaded=false;
            for(int i=0;i<100;i++)
            {
                yield return new WaitForSeconds(0.2f);TestSiteWorld(world);
                loaded=!Player.m_localPlayer.IsTeleporting();
                for(float z=-size/2-4;z<=size/2+4;z+=8)for(float x=-size/2-4;x<=size/2+4;x+=8)
                    if(Heightmap.FindHeightmap(center+new Vector3(x,0,z))==null)loaded=false;
                if(loaded)break;
            }
            if(!loaded){error("The destination terrain is still loading. No terrain was changed.");yield break;}
            var pieces=new List<Piece>();Piece.GetAllPiecesInRadius(center,(size/2+6)*1.42f,pieces);
            if(pieces.Any(p=>p!=null && p.GetComponent<ZNetView>()?.IsValid()==true && !Player.IsPlacementGhost(p.gameObject)))
            {error("This destination has existing buildings. No clearing or terrain edits were made.");yield break;}
            var jobs=new List<GroundCell>();var tiles=new Dictionary<Heightmap,TerrainComp>();var snapshots=new Dictionary<TerrainComp,GroundTile>();
            foreach(var map in Heightmap.GetAllHeightmaps())
            {
                if(map==null || map.IsDistantLod)continue;
                var cells=new List<GroundCell>();
                for(int z=0;z<=map.m_width;z++)for(int x=0;x<=map.m_width;x++)
                {
                    Vector3 p=map.transform.position+new Vector3((x-map.m_width/2)*map.m_scale,0,(z-map.m_width/2)*map.m_scale);
                    float dx=Mathf.Max(Mathf.Abs(p.x-center.x)-size/2,0),dz=Mathf.Max(Mathf.Abs(p.z-center.z)-size/2,0),outside=Mathf.Sqrt(dx*dx+dz*dz);
                    if(outside>=4)continue;
                    float before=map.GetHeight(x,z)+map.transform.position.y,target=Mathf.Lerp(before,center.y,outside==0?1:Mathf.SmoothStep(1,0,outside/4));
                    p.y=target;if(!PrivateArea.CheckAccess(p,0,false,false)||Location.IsInsideNoBuildLocation(p)){error("The test site reaches protected ground.");yield break;}
                    cells.Add(new GroundCell{Map=map,X=x,Z=z,Index=z*(map.m_width+1)+x,Target=target,Displayed=before});
                }
                if(cells.Count==0)continue;
                var comp=map.GetAndCreateTerrainCompiler();HallOwner(comp,true);GroundArrays(comp,out var levels,out var smooth,out var modified);
                _hallCapture=comp;_hallUnderlying=_hallBaseline=null;try{map.Poke();}finally{_hallCapture=null;}
                if(_hallUnderlying==null||_hallBaseline==null)throw new ArgumentException("Cannot capture native terrain limits.");
                var tile=new GroundTile{X=map.transform.position.x,Y=map.transform.position.y,Z=map.transform.position.z,Width=map.m_width,Scale=map.m_scale};
                foreach(var c in cells)
                {
                    if(!HallExcavation.Delta(_hallUnderlying[c.Index]+tile.Y,_hallBaseline[c.Index]+tile.Y,c.Target,out float delta))
                    {error("This site exceeds the native terrain limits. No terrain or objects were changed.");yield break;}
                    tile.Vertices.Add(new GroundVertex{Index=c.Index,BeforeLevel=levels[c.Index],BeforeSmooth=smooth[c.Index],BeforeModified=modified[c.Index],AfterLevel=delta,AfterSmooth=0,AfterModified=true});
                }
                jobs.AddRange(cells);tiles[map]=comp;snapshots[comp]=tile;
            }
            if(jobs.Count==0||jobs.Count>8192||tiles.Count>9)throw new ArgumentException("Test-site terrain coverage is outside the supported bounds.");
            // Capture terrain before changing anything. The site tool never removes player pieces or characters.
            string backup=Path.Combine(Paths.ConfigPath,$"testsite-{world}-{DateTime.UtcNow:yyyyMMddHHmmssfff}.json");
            File.WriteAllText(backup,JsonConvert.SerializeObject(new GroundRecord{World=world,Tiles=snapshots.Values.ToList()}));
            foreach(var pair in snapshots)
            {
                TestSiteWorld(world);HallOwner(pair.Key);GroundArrays(pair.Key,out var levels,out var smooth,out var modified);
                foreach(var v in pair.Value.Vertices){levels[v.Index]=v.AfterLevel;smooth[v.Index]=0;modified[v.Index]=true;}
                var paints=(Color[])AccessTools.Field(typeof(TerrainComp),"m_paintMask").GetValue(pair.Key);
                var painted=(bool[])AccessTools.Field(typeof(TerrainComp),"m_modifiedPaint").GetValue(pair.Key);
                var tile=pair.Value;
                for(int z=0;z<tile.Width;z++)for(int x=0;x<tile.Width;x++)
                {float wx=tile.X+(x-tile.Width/2+0.5f)*tile.Scale,wz=tile.Z+(z-tile.Width/2+0.5f)*tile.Scale;if(Mathf.Abs(wx-center.x)<=size/2&&Mathf.Abs(wz-center.z)<=size/2){paints[z*tile.Width+x]=Color.red;painted[z*tile.Width+x]=true;}}
                SaveHallTile(pair.Key,pair.Value);
            }
            int removed=ClearTestSite(center,size,world);
            yield return new WaitForSeconds(1);
            removed+=ClearTestSite(center,size,world);
            yield return null;
            float worst=0;
            for(float z=-size/2+1;z<=size/2-1;z+=2)for(float x=-size/2+1;x<=size/2-1;x+=2)
                worst=Mathf.Max(worst,Mathf.Abs(ZoneSystem.instance.GetGroundHeight(center+new Vector3(x,0,z))-center.y));
            Player.m_localPlayer.TeleportTo(center+Vector3.up*1.5f,Quaternion.identity,true);
            output(new JObject{["center"]=new JArray(center.x,center.y,center.z),["size"]=size,["terrainVertices"]=jobs.Count,["clearedObjects"]=removed,["flatnessError"]=worst,["terrainBackup"]=backup});
        }
    }
}
