using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace BuildShapes
{
    public sealed partial class Plugin
    {
        private sealed class GroundCell { internal Heightmap Map; internal int Index,X,Z; internal float Target,Displayed; }
        private readonly List<GroundCell> _hallGroundJob=new List<GroundCell>();
        private readonly Dictionary<Heightmap,Dictionary<int,float>> _hallTargetMaps=new Dictionary<Heightmap,Dictionary<int,float>>();
        private static readonly FieldInfo HallLevels=AccessTools.Field(typeof(TerrainComp),"m_levelDelta"),HallSmooth=AccessTools.Field(typeof(TerrainComp),"m_smoothDelta"),HallModified=AccessTools.Field(typeof(TerrainComp),"m_modifiedHeight"),HallOps=AccessTools.Field(typeof(TerrainComp),"m_operations"),HallOpPoint=AccessTools.Field(typeof(TerrainComp),"m_lastOpPoint"),HallOpRadius=AccessTools.Field(typeof(TerrainComp),"m_lastOpRadius");
        private static readonly MethodInfo HallSave=AccessTools.Method(typeof(TerrainComp),"Save"),HallLoad=AccessTools.Method(typeof(TerrainComp),"CheckLoad");
        private static TerrainComp _hallCapture;
        private static float[] _hallUnderlying,_hallBaseline;
        private sealed class GroundRecord
        {
            public long World;public List<GroundTile> Tiles=new List<GroundTile>();
        }
        private sealed class GroundTile
        {
            public float X,Y,Z;public int Width;public float Scale;public List<GroundVertex> Vertices=new List<GroundVertex>();
        }
        private sealed class GroundVertex
        {
            public int Index;public float BeforeLevel,BeforeSmooth,AfterLevel,AfterSmooth;public bool BeforeModified,AfterModified;
        }
        private string HallGroundFile=>Path.Combine(Paths.ConfigPath,"hallwright-ground-"+ZNet.instance.GetWorldUID()+".json");
        private void PrepareHallGround()
        {
            _hallGroundJob.Clear();_hallTargetMaps.Clear();if(!_hallBasement || _hallDesign==null)return;
            if(_hallFloorY-3<ZoneSystem.instance.m_waterLevel+0.75f)throw new ArgumentException("The cellar would flood. Choose higher ground (basement floor must be above the water table).");
            var cells=_hallDesign.Cells;
            Vector3[] corners={HallWorld(new V3(cells.Min(c=>c.X)*2-5,0,cells.Min(c=>c.Z)*2-5)),HallWorld(new V3((cells.Max(c=>c.X)+1)*2+5,0,cells.Min(c=>c.Z)*2-5)),HallWorld(new V3(cells.Min(c=>c.X)*2-5,0,(cells.Max(c=>c.Z)+1)*2+5)),HallWorld(new V3((cells.Max(c=>c.X)+1)*2+5,0,(cells.Max(c=>c.Z)+1)*2+5))};
            float x0=corners.Min(v=>v.x),x1=corners.Max(v=>v.x),z0=corners.Min(v=>v.z),z1=corners.Max(v=>v.z);
            foreach(Heightmap map in Heightmap.GetAllHeightmaps())
            {
                if(map==null || map.IsDistantLod)continue;
                float scale=map.m_scale;if(scale<0.5f || scale>4 || map.m_width<1 || map.m_width>128)throw new ArgumentException("Unsupported terrain grid.");
                int half=map.m_width/2;
                int a=Math.Max(0,Mathf.CeilToInt((x0-map.transform.position.x)/scale+half)),b=Math.Min(map.m_width,Mathf.FloorToInt((x1-map.transform.position.x)/scale+half));
                int c=Math.Max(0,Mathf.CeilToInt((z0-map.transform.position.z)/scale+half)),d=Math.Min(map.m_width,Mathf.FloorToInt((z1-map.transform.position.z)/scale+half));
                if((long)Math.Max(0,b-a+1)*Math.Max(0,d-c+1)>8192)throw new ArgumentException("Too many terrain samples.");
                for(int z=c;z<=d;z++)for(int x=a;x<=b;x++)
                {
                    Vector3 point=map.transform.position+new Vector3((x-half)*scale,0,(z-half)*scale);
                    Vector3 local=Quaternion.Inverse(_hallFrame)*(point-new Vector3(_hallOrigin.x,point.y,_hallOrigin.z));
                    double? target=HallExcavation.Target(_hallDesign,local.x,local.z);if(!target.HasValue)continue;
                    float y=_hallFloorY+(float)target.Value,old=map.GetHeight(x,z)+map.transform.position.y;
                    point.y=y;
                    if(Vector3.Distance(point,Player.m_localPlayer.transform.position)>70 || !PrivateArea.CheckAccess(point,0,false,false) || Location.IsInsideNoBuildLocation(point))throw new ArgumentException("Excavation reaches inaccessible or protected ground.");
                    if(Math.Abs(y-old)>8)throw new ArgumentException("The cellar needs more than 8 m of terrain change. Choose a flatter site.");
                    if(_hallGroundJob.Count>=2048)throw new ArgumentException("Excavation exceeds 2,048 terrain vertices.");
                    _hallGroundJob.Add(new GroundCell{Map=map,X=x,Z=z,Index=z*(map.m_width+1)+x,Target=y,Displayed=old});
                }
            }
            if(_hallGroundJob.Count==0 || _hallGroundJob.Select(c=>c.Map).Distinct().Count()>4)throw new ArgumentException("Basement terrain must be loaded in at most four tiles.");
            // Verify continuous loaded coverage, not merely whichever heightmaps happened to be present.
            foreach(var c in _hallDesign.Cells)for(int z=0;z<=2;z++)for(int x=0;x<=2;x++)
                if(Heightmap.FindHeightmap(HallWorld(new V3(c.X*2+x,0,c.Z*2+z)))==null)throw new ArgumentException("Move closer: basement terrain is not loaded.");
            for(int z=cells.Min(c=>c.Z)*2-5;z<=(cells.Max(c=>c.Z)+1)*2+5;z++)
            for(int x=cells.Min(c=>c.X)*2-5;x<=(cells.Max(c=>c.X)+1)*2+5;x++)
                if(HallExcavation.Target(_hallDesign,x,z).HasValue && Heightmap.FindHeightmap(HallWorld(new V3(x,0,z)))==null)throw new ArgumentException("Move closer: excavation or entrance apron is not fully loaded.");
            foreach(var group in _hallGroundJob.GroupBy(c=>c.Map))_hallTargetMaps[group.Key]=group.ToDictionary(c=>c.Index,c=>c.Target);
            CheckHallGroundClear(_hallGroundJob);
        }
        private static readonly Collider[] HallGroundHits=new Collider[256];
        private static void CheckHallGroundClear(IEnumerable<GroundCell> cells)
        {
            foreach(var c in cells)
            {
                Vector3 pos=c.Map.transform.position+new Vector3((c.X-c.Map.m_width/2)*c.Map.m_scale,0,(c.Z-c.Map.m_width/2)*c.Map.m_scale);pos.y=(c.Target+c.Displayed)*0.5f;
                // Refuse excavation/restoration near real buildings; ghosts have no valid native network view.
                int count=Physics.OverlapBoxNonAlloc(pos,new Vector3(c.Map.m_scale*0.55f,Math.Abs(c.Target-c.Displayed)*0.5f+1,c.Map.m_scale*0.55f),HallGroundHits,Quaternion.identity,LayerMask.GetMask("piece","piece_nonsolid"),QueryTriggerInteraction.Ignore);
                if(count==HallGroundHits.Length)throw new ArgumentException("Too many nearby pieces to check excavation safely.");
                for(int i=0;i<count;i++)
                {
                    Collider hit=HallGroundHits[i];
                    Piece piece=hit.GetComponentInParent<Piece>();var view=piece?.GetComponent<ZNetView>();
                    if(view!=null && view.IsValid() && !Player.IsPlacementGhost(piece.gameObject))throw new ArgumentException("Clear existing buildings from the excavation before planning or restoring ground.");
                }
            }
        }
        private static void GroundArrays(TerrainComp comp,out float[] level,out float[] smooth,out bool[] modified)
        {level=(float[])HallLevels.GetValue(comp);smooth=(float[])HallSmooth.GetValue(comp);modified=(bool[])HallModified.GetValue(comp);if(level==null || smooth==null || modified==null || level.Length!=smooth.Length || level.Length!=modified.Length)throw new ArgumentException("Terrain compiler is not ready.");}
        private void WriteHallGround(GroundRecord record)
        {
            string path=HallGroundFile,temp=path+".tmp";File.WriteAllText(temp,JsonConvert.SerializeObject(record));
            if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
        }
        private GroundRecord ApplyHallGround()
        {
            if(!_hallBasement)return null;
            if(File.Exists(HallGroundFile))throw new ArgumentException("A previous basement ground record is still available. Restore it or keep it before excavating another cellar.");
            PrepareHallGround();
            var terrain=new Dictionary<Heightmap,TerrainComp>();
            foreach(var map in _hallGroundJob.Select(c=>c.Map).Distinct())
            {var comp=map.GetAndCreateTerrainCompiler();HallOwner(comp,true);terrain.Add(map,comp);}
            // Ownership acquisition may load newer terrain. Recheck the entire site before capture/edit.
            PrepareHallGround();var record=new GroundRecord{World=ZNet.instance.GetWorldUID()};
            var comps=new Dictionary<GroundTile,TerrainComp>();
            // Prepare and validate every tile and recovery data before editing any vertex.
            foreach(var group in _hallGroundJob.GroupBy(c=>c.Map))
            {
                Heightmap map=group.Key;if(!terrain.TryGetValue(map,out var comp))throw new ArgumentException("Loaded terrain changed. Refresh the preview and retry.");HallOwner(comp);GroundArrays(comp,out var levels,out var smooth,out var modified);
                _hallCapture=comp;_hallUnderlying=_hallBaseline=null;
                try{map.Poke();}finally{_hallCapture=null;}
                if(_hallUnderlying==null || _hallBaseline==null)throw new ArgumentException("Unable to capture terrain's native height limits.");
                var tile=new GroundTile{X=map.transform.position.x,Y=map.transform.position.y,Z=map.transform.position.z,Width=map.m_width,Scale=map.m_scale};
                foreach(var c in group)
                {
                    if(!HallExcavation.Delta(_hallUnderlying[c.Index]+map.transform.position.y,_hallBaseline[c.Index]+map.transform.position.y,c.Target,out float delta))throw new ArgumentException("The basement exceeds the game's native 8 m digging limit on this terrain.");
                    tile.Vertices.Add(new GroundVertex{Index=c.Index,BeforeLevel=levels[c.Index],BeforeSmooth=smooth[c.Index],BeforeModified=modified[c.Index],AfterLevel=delta,AfterSmooth=0,AfterModified=true});
                }
                record.Tiles.Add(tile);comps[tile]=comp;
            }
            _hallUnderlying=_hallBaseline=null;WriteHallGround(record);var applied=new List<GroundTile>();
            try
            {
                foreach(var tile in record.Tiles)
                {
                    TerrainComp comp=comps[tile];HallOwner(comp);GroundArrays(comp,out var levels,out var smooth,out var modified);
                    foreach(var v in tile.Vertices){levels[v.Index]=v.AfterLevel;smooth[v.Index]=0;modified[v.Index]=true;}
                    applied.Add(tile);SaveHallTile(comp,tile);
                    Heightmap map=Heightmap.FindHeightmap(new Vector3(tile.X,tile.Y,tile.Z));
                    foreach(var c in _hallGroundJob.Where(c=>c.Map==map))if(Math.Abs(map.GetHeight(c.X,c.Z)+map.transform.position.y-c.Target)>0.02f)throw new ArgumentException("Excavation did not reach its requested plane.");
                }
                return record;
            }
            catch
            {
                foreach(var tile in applied){TerrainComp comp=comps[tile];HallOwner(comp);GroundArrays(comp,out var levels,out var smooth,out var modified);foreach(var v in tile.Vertices){levels[v.Index]=v.BeforeLevel;smooth[v.Index]=v.BeforeSmooth;modified[v.Index]=v.BeforeModified;}SaveHallTile(comp,tile);}
                File.Delete(HallGroundFile);throw;
            }
        }
        private static void SaveHallTile(TerrainComp comp,GroundTile tile)
        {
            HallOwner(comp);HallOps.SetValue(comp,(int)HallOps.GetValue(comp)+1);HallOpPoint.SetValue(comp,new Vector3(tile.X,tile.Y,tile.Z));HallOpRadius.SetValue(comp,tile.Width*tile.Scale/2);
            var map=Heightmap.FindHeightmap(new Vector3(tile.X,tile.Y,tile.Z));map.Poke();HallOwner(comp);
            var zdo=comp.GetComponent<ZNetView>().GetZDO();uint revision=zdo.DataRevision;
            HallSave.Invoke(comp,new object[]{false});
            if(zdo.DataRevision==revision)throw new ArgumentException("Terrain save was not acknowledged by its owner.");
            try{ClutterSystem.instance?.ResetGrass(map.transform.position,tile.Width*tile.Scale/2);}catch(Exception){}
        }
        private static bool HallGroundMatches(GroundVertex v,float level,float smooth,bool modified)=>
            level==v.AfterLevel && smooth==v.AfterSmooth && modified==v.AfterModified || level==v.BeforeLevel && smooth==v.BeforeSmooth && modified==v.BeforeModified;
        private void RestoreHallGround(bool keep=false)
        {
            string path=HallGroundFile;if(!File.Exists(path))throw new ArgumentException("No basement ground record in this world.");
            if(keep){File.Delete(path);return;}
            if(new FileInfo(path).Length>1000000)throw new ArgumentException("Ground record is too large.");
            var record=JsonConvert.DeserializeObject<GroundRecord>(File.ReadAllText(path));
            if(record==null || record.World!=ZNet.instance.GetWorldUID() || record.Tiles==null || record.Tiles.Any(t=>t==null || t.Vertices==null || t.Vertices.Count<1 || t.Vertices.Any(v=>v==null) || t.Vertices.Select(v=>v.Index).Distinct().Count()!=t.Vertices.Count || t.Vertices.Any(v=>float.IsNaN(v.BeforeLevel) || float.IsInfinity(v.BeforeLevel) || float.IsNaN(v.BeforeSmooth) || float.IsInfinity(v.BeforeSmooth))) || record.Tiles.Count<1 || record.Tiles.Count>4 || record.Tiles.Sum(t=>t.Vertices.Count)>2048)throw new ArgumentException("Invalid basement ground record.");
            var ready=new List<(GroundTile tile,TerrainComp comp)>();var occupied=new List<GroundCell>();
            foreach(var tile in record.Tiles)
            {
                var map=Heightmap.FindHeightmap(new Vector3(tile.X,tile.Y,tile.Z));
                if(map==null || map.transform.position!=new Vector3(tile.X,tile.Y,tile.Z) || map.m_width!=tile.Width || map.m_scale!=tile.Scale)throw new ArgumentException("Move closer: saved terrain tile is not loaded.");
                TerrainComp comp=map.GetAndCreateTerrainCompiler();LoadHallGround(comp);GroundArrays(comp,out var levels,out var smooth,out var modified);
                foreach(var v in tile.Vertices)
                {
                    if(v.Index<0 || v.Index>=levels.Length || !HallGroundMatches(v,levels[v.Index],smooth[v.Index],modified[v.Index]))throw new ArgumentException("Ground has changed since excavation. Restoration would overwrite another edit; keep the record or accept the current ground.");
                    int x=v.Index%(tile.Width+1),z=v.Index/(tile.Width+1);float now=map.GetHeight(x,z)+tile.Y;
                    var c=new GroundCell{Map=map,Index=v.Index,X=x,Z=z,Displayed=now,Target=now+v.BeforeLevel-v.AfterLevel+v.BeforeSmooth-v.AfterSmooth};occupied.Add(c);
                    Vector3 pos=map.transform.position+new Vector3((x-tile.Width/2)*tile.Scale,0,(z-tile.Width/2)*tile.Scale);
                    if(Vector3.Distance(pos,Player.m_localPlayer.transform.position)>70 || !PrivateArea.CheckAccess(pos,0,false,false) || Location.IsInsideNoBuildLocation(pos))throw new ArgumentException("Ground restoration is out of reach or protected.");
                }
                ready.Add((tile,comp));
            }
            CheckHallGroundClear(occupied);
            // Validate access, conflicts and buildings across every tile before claiming ownership.
            foreach(var entry in ready)
            {
                HallOwner(entry.comp,true);GroundArrays(entry.comp,out var levels,out var smooth,out var modified);
                if(entry.tile.Vertices.Any(v=>!HallGroundMatches(v,levels[v.Index],smooth[v.Index],modified[v.Index])))throw new ArgumentException("Ground changed during ownership acquisition. Recovery retained; retry after the area settles.");
            }
            foreach(var entry in ready){HallOwner(entry.comp);GroundArrays(entry.comp,out var levels,out var smooth,out var modified);foreach(var v in entry.tile.Vertices){levels[v.Index]=v.BeforeLevel;smooth[v.Index]=v.BeforeSmooth;modified[v.Index]=v.BeforeModified;}SaveHallTile(entry.comp,entry.tile);
                GroundArrays(entry.comp,out levels,out smooth,out modified);
                if(entry.tile.Vertices.Any(v=>levels[v.Index]!=v.BeforeLevel || smooth[v.Index]!=v.BeforeSmooth || modified[v.Index]!=v.BeforeModified))throw new ArgumentException("Terrain restoration could not be verified; recovery record retained.");
            }
            File.Delete(path);
        }
        private bool HallPredictedGround(Obb reach)
        {
            Bounds bounds=reach.Aabb();
            foreach(var group in _hallTargetMaps)
            {
                Heightmap map=group.Key;int half=map.m_width/2;float scale=map.m_scale;
                int x0=Math.Max(0,Mathf.FloorToInt((bounds.min.x-map.transform.position.x)/scale+half)),x1=Math.Min(map.m_width-1,Mathf.FloorToInt((bounds.max.x-map.transform.position.x)/scale+half));
                int z0=Math.Max(0,Mathf.FloorToInt((bounds.min.z-map.transform.position.z)/scale+half)),z1=Math.Min(map.m_width-1,Mathf.FloorToInt((bounds.max.z-map.transform.position.z)/scale+half));
                if(x0>x1 || z0>z1)continue;
                var targets=group.Value;
                Vector3 At(int x,int z)=>new Vector3(map.transform.position.x+(x-half)*scale,
                    targets.TryGetValue(z*(map.m_width+1)+x,out float y)?y:map.GetHeight(x,z)+map.transform.position.y,map.transform.position.z+(z-half)*scale);
                for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)
                {
                    Vector3 a=At(x,z),b=At(x+1,z),c=At(x,z+1),d=At(x+1,z+1);
                    if(HallTriangleContact(reach,a,c,b) || HallTriangleContact(reach,b,c,d))return true;
                }
            }
            return false;
        }
        private static bool HallTriangleContact(Obb box,Vector3 a,Vector3 b,Vector3 c)
        {
            // Triangle/OBB separating-axis test: the same terrain mesh, including sloped pit edges.
            Quaternion inverse=Quaternion.Inverse(box.R);a=inverse*(a-box.C);b=inverse*(b-box.C);c=inverse*(c-box.C);
            return HallTerrainGeometry.Contact(V(a),V(b),V(c),V(box.H));
        }
        [HarmonyPatch(typeof(TerrainComp),nameof(TerrainComp.ApplyToHeightmap))]
        private static class HallCaptureBaseline
        {
            [HarmonyPrefix,HarmonyPriority(Priority.First)]
            private static void Prefix(TerrainComp __instance,List<float> heights,float[] baseHeights)
            {if(__instance==_hallCapture){_hallUnderlying=heights.ToArray();_hallBaseline=(float[])baseHeights.Clone();}}
        }
    }
}
