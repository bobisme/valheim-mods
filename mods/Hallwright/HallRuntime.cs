using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hallwright
{
    public sealed partial class Plugin
    {
        private bool _staveTemple,_staveGallery=true;
        private bool _hallNorse,_hallBenches=true,_hallHearth=true,_hallFooting=true;
        private float _hallSink=0.5f;
        private int HallStyle=>_staveTemple?2:_hallNorse?1:0;
        // Ground changes when the shell is planned: a basement's cellar pit, or a dug-in longhouse.
        private bool HallDigs=>!_staveTemple && (_hallBasement || _hallNorse && _hallSink>0);
        // 0 timber hall, 1 Norse longhouse, 2 stave temple; switching sets each style's defaults.
        private void SetHallStyle(int style)
        {
            if(style==HallStyle)return;
            _staveTemple=style==2;_hallNorse=style==1;
            if(style==2){_hallStoreys=1;_hallBasement=false;_hallDetail=2;_hallOverhang=true;_hallSweep=true;_hallPorch=true;_hallRoof45=true;_hallHeight=3;}
            if(style==1)
            {
                _hallStoreys=1;_hallBasement=false;_hallOverhang=false;_hallPorch=false;_hallRoof45=true;_hallHeight=2;_hallShutters=false;_hallFooting=true;
                // The door goes near one end of the longest wall.
                if(_hallDoorPoints.Count==0 && _markers.Count>=3)
                    _hallEntrance=Enumerable.Range(0,_markers.Count).OrderByDescending(i=>Vector3.Distance(_markers[i],_markers[(i+1)%_markers.Count])).First();
            }
        }
        private int _staveHeight=6,_staveCrowns=1;
        private bool _hallMenu, _hallRoof45=true, _hallSolid=true, _hallShowRoof=true, _hallTiered, _hallOverhang, _hallPorch, _hallSweep, _hallBasement, _hallShutters=true;
        private int _hallHeight=3, _hallDetail=1, _hallEntrance, _hallMaterialMode, _hallEntranceMode, _hallCrestMode, _hallStoreys=1;
        private float _hallRaise=0.15f, _hallDue, _hallNextCatalog;
        private Vector3 _hallOrigin;
        private Quaternion _hallFrame=Quaternion.identity;
        private readonly List<V3> _hallDoorPoints=new List<V3>(), _hallChimneyPoints=new List<V3>();
        private HallLayout.Design _hallDesign;
        private HallLayout.Kit _hallKit;
        private float _hallFloorY;
        private string _hallProblem, _hallNote, _hallFingerprint;
        private string _hallAnnouncedProblem;
        private int _hallAnnouncedCorners=-1;
        private float _hallProblemUntil;
        private int _hallFalls, _hallAddedPosts;
        private long _hallSolveMs;
        private Rect _hallRect;
        private bool _hallRectPlaced;
        private readonly Dictionary<string,GameObject> _hallCatalog=new Dictionary<string,GameObject>();
        private readonly Dictionary<string,int> _hallBill=new Dictionary<string,int>();
        private readonly HashSet<string> _hallStations=new HashSet<string>();
        private readonly List<string> _hallRoles=new List<string>();
        private readonly Dictionary<string,List<HallMesh>> _hallMeshes=new Dictionary<string,List<HallMesh>>();
        private readonly Dictionary<Material,Material> _hallGhostMaterials=new Dictionary<Material,Material>();
        private readonly MaterialPropertyBlock _hallTint=new MaterialPropertyBlock();
        private sealed class HallMesh { internal Mesh Mesh; internal Matrix4x4 Local; internal Material[] Materials; internal string Path; }

        private bool HallKnown(string name) => name!=null && _hallCatalog.ContainsKey(name);
        private void RefreshHallCatalog(Player player)
        {
            _hallCatalog.Clear();
            PieceTable table=ObjectDB.instance?.GetItemPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;
            if(table==null)return;
            foreach(GameObject prefab in table.m_pieces)
            {
                Piece piece=prefab!=null?prefab.GetComponent<Piece>():null;
                if(piece==null || !player.IsRecipeKnown(piece.m_name) ||
                    prefab.GetComponentInChildren<TerrainOp>(true)!=null || prefab.GetComponentInChildren<TerrainModifier>(true)!=null)continue;
                _hallCatalog[Utils.GetPrefabName(prefab)]=prefab;
            }
        }
        private HallLayout.Kit HallKit(Player player)
        {
            RefreshHallCatalog(player);
            var kit=new HallLayout.Kit();
            bool gate=_hallEntranceMode==2 || _hallEntranceMode==0 && _hallHeight>=3 && HallKnown("wood_gate");
            if(gate && _hallHeight<3)throw new ArgumentException("A gate needs walls at least 3 m high. Choose Door or raise the walls.");
            if(gate){kit.Door="wood_gate";kit.DoorHeight=3;}
            else if(_hallEntranceMode==0 && _hallHeight>=3 && !HallKnown("wood_gate"))_hallNote="Gate not unlocked; Auto uses a door.";
            foreach(string name in new[]{kit.Floor,kit.Wall,kit.Half,kit.Quarter,kit.Door,kit.Post,kit.Beam,kit.ShortBeam})
                if(!HallKnown(name))throw new ArgumentException("Unlock "+name+" before planning a timber hall.");
            if(_hallMaterialMode==2 && !HallKnown("wood_pole_log"))throw new ArgumentException("Core-wood posts are not unlocked yet.");
            if(_hallMaterialMode==3 && !HallKnown("stone_floor_2x2"))throw new ArgumentException("Stone foundations are not unlocked yet.");
            if(_hallMaterialMode==4 && !HallKnown("darkwood_beam"))throw new ArgumentException("Darkwood beams are not unlocked yet.");
            if(_hallMaterialMode!=1 && HallKnown("wood_pole_log") && HallKnown("wood_wall_log"))
            {kit.Post="wood_pole_log";kit.Beam="wood_wall_log";kit.SpanCells=5;}
            if(_hallMaterialMode==4 || _hallMaterialMode==0 && _hallDetail>=2)
            {
                if(HallKnown("darkwood_beam"))kit.Beam="darkwood_beam";
                if(HallKnown("darkwood_raven"))kit.Raven="darkwood_raven";
                if(HallKnown("darkwood_arch"))kit.Arch="darkwood_arch";
                if(HallKnown("darkwood_decowall"))kit.Lattice="darkwood_decowall";
            }
            if((_hallStoreys>1 || _staveTemple) && _hallMaterialMode==0 && HallKnown("woodiron_pole"))kit.LoadPost="woodiron_pole";
            if(HallKnown("wood_window"))kit.Window="wood_window";
            if(HallKnown("wood_floor_1x1"))kit.SmallFloor="wood_floor_1x1";
            if(HallKnown("stone_fence"))kit.StoneFence="stone_fence";
            else if(_hallNorse && _hallFooting)_hallNote=(_hallNote==null?"":_hallNote+" ")+"The stone fence isn't unlocked yet (carry some stone); the walls are timber to the ground.";
            else if(_hallShutters)_hallNote=(_hallNote==null?"":_hallNote+" ")+"Window shutters aren't unlocked yet (they need bronze nails); walls stay closed, with open daylight windows from Ornate.";
            bool steep=_hallRoof45;
            string roof=steep?"wood_roof_45":"wood_roof",ridge=steep?"wood_roof_top_45":"wood_roof_top",wedge=steep?"wood_wall_roof_45":"wood_wall_roof_a";
            foreach(string name in new[]{roof,ridge,wedge})if(!HallKnown(name))throw new ArgumentException("The selected roof pitch needs "+name+" unlocked.");
            kit.Roof=roof;kit.Ridge=ridge;kit.Wedge=wedge;kit.Slope=steep?1:0.5;
            // Joists meet the underside of the floor without protruding into the stair landing.
            kit.JoistDrop=ShapesOf(kit.Beam).Max(s=>s.Key.Aabb().max.y)+0.1;
            // Derive lengths from native snap axes instead of visual mesh bounds.
            Vector3[] ends=HallEnds(_hallCatalog[kit.Beam]);kit.BeamLength=(ends[1]-ends[0]).magnitude;
            ends=HallEnds(_hallCatalog[kit.ShortBeam]);kit.ShortLength=(ends[1]-ends[0]).magnitude;
            return kit;
        }
        private HallLayout.Details HallDetailsKit()
        {
            if(_hallOverhang && !HallKnown("wood_roof"))throw new ArgumentException("Unlock 26° thatch roofs for the 2 m overhang apron.");
            string carving=null;
            if(_hallCrestMode==2)carving="wood_dragon1";
            else if(_hallCrestMode==3)carving="darkwood_raven";
            else if(_hallCrestMode==0 && _hallDetail>=2)
                carving=HallKnown("wood_dragon1")?"wood_dragon1":HallKnown("darkwood_raven")?"darkwood_raven":null;
            if(carving!=null && !HallKnown(carving))throw new ArgumentException("Unlock the "+(_hallCrestMode==2?"dragon":"raven")+" carving before planning.");
            if(_hallBasement)foreach(string name in new[]{"stone_floor_2x2","stone_wall_2x1"})if(!HallKnown(name))throw new ArgumentException("Unlock stone floors and walls for a retaining-wall basement.");
            if((_hallBasement || _hallStoreys>1) && !HallKnown("wood_stair"))throw new ArgumentException("Unlock wooden stairs for interior storeys.");
            string hearth=null;
            if(_hallNorse && _hallHearth)
            {
                if(HallKnown("hearth"))hearth="hearth";
                else _hallNote=(_hallNote==null?"":_hallNote+" ")+"The hearth isn't unlocked yet (it needs stone); the longhouse is planned without its long-fire.";
            }
            if(_hallNorse && (_hallStoreys>1 || _hallBasement || _hallPorch || _hallOverhang))throw new ArgumentException("A longhouse is one sunken storey: turn off storeys, basement, porch and overhangs, or choose Timber hall.");
            return new HallLayout.Details{Norse=_hallNorse && !_staveTemple,Benches=_hallBenches,StoneFooting=_hallNorse && !_staveTemple && _hallFooting,Hearth=hearth,Storeys=_hallStoreys,Basement=_hallBasement,Overhang=_hallOverhang,Porch=_hallPorch,Sweep=_hallSweep,Shutters=_hallShutters,Finial=carving,DoorPoints=_hallDoorPoints,ChimneyPoints=_staveTemple?null:_hallChimneyPoints};
        }
        private static Vector3[] HallSnaps(GameObject prefab)
        {
            var snaps=new List<Transform>();prefab.GetComponent<Piece>().GetSnapPoints(snaps);
            return snaps.Where(s=>s!=null).Select(s=>prefab.transform.InverseTransformPoint(s.position)).ToArray();
        }
        private static Vector3[] HallEnds(GameObject prefab)
        {
            Vector3[] s=HallSnaps(prefab);
            if(s.Length<2)throw new ArgumentException("No usable beam snap points on "+prefab.name+".");
            float[] ranges={s.Max(v=>v.x)-s.Min(v=>v.x),s.Max(v=>v.y)-s.Min(v=>v.y),s.Max(v=>v.z)-s.Min(v=>v.z)};
            int axis=ranges[1]>ranges[0]?1:0;if(ranges[2]>ranges[axis])axis=2;
            float lo=s.Min(v=>v[axis]),hi=s.Max(v=>v[axis]);
            Vector3 Mean(IEnumerable<Vector3> values){Vector3 p=Vector3.zero;int n=0;foreach(var v in values){p+=v;n++;}return p/n;}
            return new[]{Mean(s.Where(v=>Mathf.Abs(v[axis]-lo)<0.001f)),Mean(s.Where(v=>Mathf.Abs(v[axis]-hi)<0.001f))};
        }
        private static Vector3 HallAnchor(GameObject prefab,HallLayout.Anchor kind)
        {
            Vector3[] snaps=HallSnaps(prefab);
            if(snaps.Length==0)throw new ArgumentException("Missing snap anchor on "+prefab.name+".");
            if(kind==HallLayout.Anchor.Point)return snaps[0];
            float y=snaps.Min(v=>v.y);
            var low=snaps.Where(v=>Mathf.Abs(v.y-y)<0.001f).ToArray();
            Vector3 anchor=Vector3.zero;foreach(var v in low)anchor+=v;return anchor/low.Length;
        }
        private Vector3 HallWorld(V3 point) => new Vector3(_hallOrigin.x,_hallFloorY,_hallOrigin.z)+_hallFrame*V(point);
        private float HallGround(Vector3 at)
        {
            Vector3 from=new Vector3(at.x,_hallOrigin.y+30,at.z);
            if(!Physics.Raycast(from,Vector3.down,out RaycastHit hit,70,LayerMask.GetMask("terrain"),QueryTriggerInteraction.Ignore))
                throw new ArgumentException("Move closer: ground is not loaded under the footprint.");
            return hit.point.y;
        }
        private void AddHallPart(HallLayout.Part part)
        {
            if(_output.Count>=HallLayout.MaximumParts)throw new ArgumentException("The shell and its foundations exceed 2,048 pieces. Reduce area or intricacy.");
            if(!_hallCatalog.TryGetValue(part.Prefab,out var prefab))throw new ArgumentException("A planned piece is no longer unlocked: "+part.Prefab);
            if((prefab.transform.localScale-Vector3.one).sqrMagnitude>1e-6)throw new ArgumentException("Scaled pieces cannot be used in a hall.");
            Vector3 target=HallWorld(part.At),position;Quaternion rotation;
            if(part.Kind==HallLayout.Anchor.Segment)
            {
                Vector3[] ends=HallEnds(prefab);Vector3 end=HallWorld(part.End);
                if(Mathf.Abs((end-target).magnitude-(ends[1]-ends[0]).magnitude)>0.01f)throw new ArgumentException("A beam would need stretching.");
                rotation=Quaternion.FromToRotation(ends[1]-ends[0],end-target);
                position=(target+end)*0.5f-rotation*((ends[0]+ends[1])*0.5f);
            }
            else
            {rotation=_hallFrame*Quaternion.Euler(0,(float)part.Yaw,0);position=target-rotation*HallAnchor(prefab,part.Kind);}
            if(!V(position).Finite || Vector3.Distance(position,Player.m_localPlayer.transform.position)>70)
                throw new ArgumentException("Keep the complete hall within 70 metres of you.");
            if(!PrivateArea.CheckAccess(position,0,false,false) || Location.IsInsideNoBuildLocation(position))throw new ArgumentException("The hall reaches protected ground.");
            // Use the same pose tolerance as the planner, so support never relies on duplicate ghosts it will omit.
            if(_output.Any(p=>p.Prefab==part.Prefab && (p.Position-position).sqrMagnitude<0.01f && Quaternion.Angle(p.Rotation,rotation)<5))return;
            _output.Add(new PiecePose(null,part.Prefab,position,rotation));_hallRoles.Add(part.Role);
        }
        private void HallColumn(V3 at,double top,string post,string role)
        {
            float ground=HallGroundAfter(at);double depth=_hallFloorY+top-ground;
            if(depth>(role=="support"?24:12))throw new ArgumentException("The foundation or structural column is too tall here. Reduce the footprint or use a flatter site.");
            int count=Math.Max(1,(int)Math.Ceiling((depth+0.15)/2));
            for(int n=0;n<count;n++)AddHallPart(new HallLayout.Part{Prefab=post,At=new V3(at.X,top-2*(n+1),at.Z),Kind=HallLayout.Anchor.Bottom,Role=role});
        }
        // Where a planned ground change leaves the terrain at this footprint point (the current ground elsewhere).
        private (double y,int mode)? HallGroundTarget(double x,double z)
        {
            if(_hallDesign==null || _staveTemple)return null;
            if(_hallBasement){double? pit=HallExcavation.Target(_hallDesign,x,z);return pit.HasValue?(pit.Value,0):((double,int)?)null;}
            return _hallNorse?HallExcavation.Norse(_hallDesign,x,z,_hallSink):null;
        }
        private float HallGroundAfter(V3 at)
        {
            var target=HallGroundTarget(at.X,at.Z);
            if(target.HasValue && target.Value.mode==0)return _hallFloorY+(float)target.Value.y;
            float now=HallGround(HallWorld(at));
            return target.HasValue?(float)HallExcavation.Settle(now,_hallFloorY+target.Value.y,target.Value.mode):now;
        }
        private void BuildHallPreview(bool announceProblem=true)
        {
            SaveHallDraft();
            var solveWatch=System.Diagnostics.Stopwatch.StartNew();
            _hallDue=0;_hallGuideHasFloor=false;_hallPreviewVersion++;ClearVisuals();_output.Clear();_hallRoles.Clear();_hallBill.Clear();_hallStations.Clear();_hallSupport.Clear();
            _hallProblem=null;_hallNote=null;_hallFalls=0;_hallAddedPosts=0;_hallDesign=null;_hallGroundJob.Clear();_hallTargetMaps.Clear();
            try
            {
                if(_markers.Count<3)throw new ArgumentException("Click the ground for each corner, then press E inside the outline for the hall settings.");
                _hallKit=HallKit(Player.m_localPlayer);
                var corners=_markers.Select(p=>{Vector3 v=Quaternion.Inverse(_hallFrame)*(p-_hallOrigin);return new V3(Math.Round(v.x/2)*2,0,Math.Round(v.z/2)*2);}).ToArray();
                var details=HallDetailsKit();
                _hallDesign=_staveTemple?StaveLayout.Plan(corners,_hallKit,_hallHeight,_hallDetail,_hallEntrance,details,new StaveLayout.Options{Height=_staveHeight,Crowns=_staveCrowns,Gallery=_staveGallery}):HallLayout.Plan(corners,_hallKit,_hallHeight,_hallDetail,_hallEntrance,_hallTiered,details);
                _hallFloorY=_hallOrigin.y;
                float highest=float.MinValue,lowest=float.MaxValue;
                foreach(V3 v in _hallDesign.Vertices){float y=HallGround(HallWorld(v));highest=Mathf.Max(highest,y);lowest=Mathf.Min(lowest,y);}
                foreach(var c in _hallDesign.Cells){float y=HallGround(HallWorld(new V3(c.X*2+1,0,c.Z*2+1)));highest=Mathf.Max(highest,y);lowest=Mathf.Min(lowest,y);}
                foreach(V3 at in _hallDesign.PorchFloors)highest=Mathf.Max(highest,HallGround(HallWorld(at)));
                // A longhouse floor is dug in below the average ground under it: deeper uphill, on posts where a slope falls away.
                bool sunken=_hallNorse && !_staveTemple && _hallSink>0;
                float average=_hallDesign.Vertices.Select(v=>HallGround(HallWorld(v))).Concat(_hallDesign.Cells.Select(c=>HallGround(HallWorld(new V3(c.X*2+1,0,c.Z*2+1))))).Average();
                _hallFloorY=_hallBasement?HallGround(HallWorld(_hallDesign.EntryLanding))+_hallRaise:sunken?average-_hallSink:highest+_hallRaise;
                _hallGuideHasFloor=true;
                PrepareHallGround();
                bool stone=(_hallMaterialMode==0 || _hallMaterialMode==3) && HallKnown("stone_floor_2x2");
                if(stone && !_hallBasement && !_hallNorse)
                {
                    foreach(V3 at in _hallDesign.Cells.Select(c=>new V3(c.X*2+1,0,c.Z*2+1)).Concat(_hallDesign.PorchFloors))
                    {
                        float ground=HallGround(HallWorld(at));
                        double depth=_hallFloorY-ground;
                        if(depth>6)throw new ArgumentException("Stone foundations would exceed 6 metres. Use a flatter site.");
                        // Native stone top snaps are Y +0.5; floor skins sit 0.06 m above the plinth.
                        int count=Math.Max(1,(int)Math.Ceiling(depth+0.1));
                        for(int n=0;n<count;n++)AddHallPart(new HallLayout.Part{Prefab="stone_floor_2x2",At=new V3(at.X,-n-1.06,at.Z),Kind=HallLayout.Anchor.Bottom,Role="foundation"});
                    }
                }
                else foreach(V3 v in _hallDesign.Vertices)if(!HallStairs.Blocks(_hallDesign,v))HallColumn(v,_hallBasement && HallExcavation.Pit(_hallDesign,v.X,v.Z)?-3:0,_hallKit.Post,"foundation");
                foreach(var part in _hallDesign.Parts)AddHallPart(part);
                // The stone footing reaches down to the ground (after digging) wherever it falls away below the floor.
                foreach(var footing in _hallDesign.Parts.Where(p=>p.Role=="stone footing" && Math.Abs(p.At.Y+1)<0.001).ToList())
                {
                    float ground=HallGroundAfter(new V3(footing.At.X,0,footing.At.Z));
                    for(int n=1;_hallFloorY-n>ground+0.05f;n++)
                    {
                        if(n>4)throw new ArgumentException("The stone footing would stand more than 5 m tall here. Use a flatter site or turn the footing off.");
                        AddHallPart(new HallLayout.Part{Prefab=footing.Prefab,At=new V3(footing.At.X,-1-n,footing.At.Z),Kind=footing.Kind,Yaw=footing.Yaw,Role="stone footing"});
                    }
                }
                // Each entrance receives its own route to the unedited terrain.
                if(!_hallBasement)foreach(var entry in _hallDesign.Entrances)
                {
                    V3 outward=V(Quaternion.Euler(0,(float)entry.Yaw,0)*Vector3.back);
                    for(int stair=0;stair<6;stair++)
                    {
                        var step=HallStairs.Exterior(entry,stair);V3 low=step.At;
                        if(_hallFloorY-stair<=HallGround(HallWorld(entry.Landing+outward*(2*stair)))+0.25)break;
                        AddHallPart(step);
                        HallColumn(low,low.Y,_hallKit.Post,"foundation");
                        if(stair==5)throw new ArgumentException("An entrance needs more than six stair sections. Move its marker or use flatter ground.");
                    }
                }
                ComputeHallSupport(Player.m_localPlayer);
                _hallFalls=_hallSupport.Values.Count(v=>v.Collapses);
                if(_hallFalls>0)
                {
                    // Solve extra ridge columns against the actual terrain, then re-evaluate the whole shell.
                    string post=_hallMaterialMode==0 && HallKnown("woodiron_pole")?"woodiron_pole":_hallKit.Post;
                    if(_staveTemple)
                    {
                        foreach(var at in _hallDesign.StaveSupports.GroupBy(p=>new {p.X,p.Z}).Select(g=>g.OrderByDescending(p=>p.Y).First()))
                        {HallColumn(new V3(at.X,0,at.Z),at.Y,post,"support");_hallAddedPosts++;}
                    }
                    else foreach(var wing in _hallDesign.Wings)
                    {
                        double length=wing.Length*2,peak=wing.RoofBase+wing.Width*_hallKit.Slope+wing.TierLift;
                        for(double v=length<=2?1:2;v<length;v+=4)
                        {
                            V3 at=wing.At(wing.Width,0,v);
                            if(HallStairs.Blocks(_hallDesign,at) || HallDoors.Blocks(_hallDesign,at) || HallLayout.ChimneyBlocks(_hallDesign,at))
                            {
                                var alternatives=_hallDesign.Vertices.Where(p=>!HallStairs.Blocks(_hallDesign,p) && !HallDoors.Blocks(_hallDesign,p) && !HallLayout.ChimneyBlocks(_hallDesign,p)).OrderBy(p=>(p-at).Length).ToArray();
                                if(alternatives.Length==0)continue;at=alternatives[0];
                            }
                            HallColumn(at,peak,post,"support");_hallAddedPosts++;
                        }
                    }
                    ComputeHallSupport(Player.m_localPlayer);_hallFalls=_hallSupport.Values.Count(v=>v.Collapses);
                    _hallNote=(_hallNote==null?"":_hallNote+" ")+$"Added {_hallAddedPosts} structural supports"+(post=="woodiron_pole"?" using unlocked reinforced timber.":".");
                }
                if(_hallSupport.Count!=_output.Count)throw new ArgumentException("Some pieces could not be checked for support.");
                if(_hallFalls>0)_hallProblem=$"{_hallFalls} pieces would fall in the support estimate. Lower the chamber or walls, choose 26° roofs, or simplify the outline.";
                foreach(var pose in _output)
                {
                    Piece piece=_hallCatalog[pose.Prefab].GetComponent<Piece>();
                    foreach(var req in piece.m_resources)
                    {if(req.m_resItem==null)continue;string key=req.m_resItem.name;int amount=req.GetAmount(1);_hallBill[key]=(_hallBill.TryGetValue(key,out int current)?current:0)+amount;}
                    if(piece.m_craftingStation!=null)_hallStations.Add(Localization.instance.Localize(piece.m_craftingStation.m_name));
                }
            }
            catch(Exception ex)
            {
                _hallProblem=ex.GetBaseException().Message;
                _output.Clear();_hallRoles.Clear();_hallBill.Clear();_hallStations.Clear();_hallSupport.Clear();
            }
            DrawHallGuides();
            _previewError=_hallProblem;_hallSolveMs=solveWatch.ElapsedMilliseconds;
            if(announceProblem)AnnounceHallDrawingProblem();
        }
        private void AnnounceHallDrawingProblem()
        {
            // Incomplete outlines stay quiet; successful edits re-arm the same reason.
            if(_markers.Count<4 || _hallProblem==null)
            {_hallAnnouncedProblem=null;_hallAnnouncedCorners=-1;_hallProblemUntil=0;return;}
            if(_hallMenu)return;
            // Catalog refreshes and repeated solves must not queue duplicate HUD messages.
            if(_hallProblem==_hallAnnouncedProblem && _markers.Count==_hallAnnouncedCorners)return;
            _hallAnnouncedProblem=_hallProblem;_hallAnnouncedCorners=_markers.Count;
            _hallProblemUntil=Time.unscaledTime+7;
        }
        private void DrawHallDrawingProblem(float scale)
        {
            if(_hallProblem==null || Time.unscaledTime>=_hallProblemUntil)return;
            float width=Mathf.Min(800,Screen.width/scale-40);
            string text=$"{(_staveTemple?"Temple":"Hall")} outline needs a change\n{_hallProblem}";
            float height=_hallNotice.CalcHeight(new GUIContent(text),width);
            GUI.Box(new Rect((Screen.width/scale-width)/2,Screen.height/scale*0.22f,width,height),text,_hallNotice);
        }
        private void QueueHallPreview(){SaveHallDraft();_hallDue=Time.unscaledTime+0.18f;}
        private void UpdateHall()
        {
            if(_hallDue>0 && Time.unscaledTime>=_hallDue)BuildHallPreview();
            if(Time.unscaledTime>=_hallNextCatalog)
            {
                _hallNextCatalog=Time.unscaledTime+2;
                RefreshHallCatalog(Player.m_localPlayer);
                string fingerprint=string.Join("|",_hallCatalog.Keys.OrderBy(n=>n));
                if(_hallFingerprint!=null && fingerprint!=_hallFingerprint)QueueHallPreview();
                _hallFingerprint=fingerprint;
            }
            DrawHallMeshes();
            UpdateHallLive(Player.m_localPlayer);
        }
        private void MarkHallDoor(Player player)
        {
            if(_markers.Count<3){Say("Draw the floor plan first, then Shift+click near an exterior wall to mark entrances.");return;}
            if(!CameraRay(out Ray ray) || !Physics.Raycast(ray,out RaycastHit hit,80,BuildLayers,QueryTriggerInteraction.Ignore))return;
            if(Vector3.Distance(hit.point,player.transform.position)>40){Say("Move within 40 metres of the entrance.");return;}
            Vector3 local=Quaternion.Inverse(_hallFrame)*(hit.point-_hallOrigin);V3 point=new V3(local.x,0,local.z);
            int remove=_hallDoorPoints.FindIndex(p=>(p-point).Length<0.8);
            if(remove<0 && _hallDesign!=null)remove=_hallDesign.Entrances.FindIndex(d=>(d.At-point).Length<0.8);
            if(remove>=0 && remove<_hallDoorPoints.Count)_hallDoorPoints.RemoveAt(remove);
            else if(_hallDoorPoints.Count<HallDoors.Maximum)_hallDoorPoints.Add(point);
            else{Say("Mark at most eight entrances. Shift+click a marker to remove it.");return;}
            BuildHallPreview(false);Say(_hallProblem??$"{_hallDoorPoints.Count} entrance markers; Shift+click again to remove, Shift+Backspace removes the last.");
        }
        private void MarkHallChimney(Player player)
        {
            if(_staveTemple){Say("Chimneys are for longhouses. Switch the style to Longhouse to mark one.");return;}
            if(_markers.Count<3){Say("Draw the floor plan first, then Ctrl+click a floor tile to put a chimney above it.");return;}
            if(!CameraRay(out Ray ray) || !Physics.Raycast(ray,out RaycastHit hit,80,BuildLayers,QueryTriggerInteraction.Ignore))return;
            if(Vector3.Distance(hit.point,player.transform.position)>40){Say("Move within 40 metres of the chimney spot.");return;}
            Vector3 local=Quaternion.Inverse(_hallFrame)*(hit.point-_hallOrigin);
            V3 tile=new V3(Math.Floor(local.x/2)*2+1,0,Math.Floor(local.z/2)*2+1);
            int remove=_hallChimneyPoints.FindIndex(p=>(p-tile).Length<0.5);
            if(remove>=0)_hallChimneyPoints.RemoveAt(remove);
            else if(_hallChimneyPoints.Count<HallLayout.MaximumChimneys)_hallChimneyPoints.Add(tile);
            else{Say("Mark at most four chimneys. Ctrl+click one to remove it.");return;}
            BuildHallPreview(false);Say(_hallProblem??$"{_hallChimneyPoints.Count} chimney"+(_hallChimneyPoints.Count==1?"":"s")+"; Ctrl+click the tile again to remove, Ctrl+Backspace removes the last.");
        }
        private void MarkHall(Player player)
        {
            if(_markers.Count>=HallLayout.MaximumCorners){Say("Use at most 24 corners.");return;}
            if(!CameraRay(out Ray ray) || !Physics.Raycast(ray,out RaycastHit hit,80,BuildLayers,QueryTriggerInteraction.Ignore))return;
            if(Vector3.Distance(hit.point,player.transform.position)>40){Say("Move within 40 metres of the corner.");return;}
            if(_markers.Count==0){Vector3 point=HallMarkerGround(hit.point);_hallOrigin=point;_hallFrame=Quaternion.identity;_markers.Add(point);}
            else
            {
                if(!HallSnap(hit.point,out Vector3 point,out Quaternion frame,out bool closes,out string problem)){Say(problem);return;}
                if(closes){BuildHallPreview();OpenHallMenu();return;}
                _hallFrame=frame;_markers.Add(point);
            }
            BuildHallPreview();
        }
        // The outline turns in the steps Valheim turns placed pieces by (Player.m_placeRotationDegrees, 22.5°) unless FreeAngle is on.
        private static readonly System.Reflection.FieldInfo PlaceRotationStep=HarmonyLib.AccessTools.Field(typeof(Player),"m_placeRotationDegrees");
        private Quaternion HallYaw(float yaw)
        {
            float step=Player.m_localPlayer!=null && PlaceRotationStep?.GetValue(Player.m_localPlayer) is float f && f>0?f:22.5f;
            return Quaternion.Euler(0,_hallFreeAngle.Value?yaw:Mathf.Round(yaw/step)*step,0);
        }
        private void OpenHallMenu(){_hallMenu=true;_menuOpenedFrame=Time.frameCount;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
        private void CloseHallMenu(){_hallMenu=false;CloseHallWindow();_editingNumber=false;_numberEdits.Clear();_numberError=null;}
        private void ConfirmHall(Player player)
        {
            float raise=_hallRaise;if(!ReadNumber("Floor raise",0,2,ref raise))return;
            _hallRaise=raise;_numberEdits.Clear();BuildHallPreview();
            if(_hallProblem!=null || _output.Count==0){Say(_hallProblem??"No hall preview.");return;}
            try
            {
                string key=SubmitHall(player);int count=_output.Count;_lastPlan=key;Stop();Say($"{(_staveTemple?"Temple":"Hall")} planned: {count} shared ghosts. E builds; U removes unbuilt pieces."+(HallDigs?" Ground restoration is in Hallwright options.":""));
            }
            catch(Exception ex){Say(ex.GetBaseException().Message);}
        }
        private string SubmitHall(Player player)
        {
            if(!_planner.CanCreateShell(_output.Count,out string capabilityError))throw new ArgumentException(capabilityError);
            GroundRecord ground=ApplyHallGround();
            try
            {
                if(!_planner.CreateShell(player,_staveTemple?"Stave Temple":"Hallwright",_output.Select(p=>p.Prefab).ToArray(),_output.Select(p=>p.Position).ToArray(),_output.Select(p=>p.Rotation).ToArray(),out string key,out string error))throw new ArgumentException(error);
                if(ground!=null){ground.Plan=key;WriteHallGround(ground);}
                _hallDraftSubmitted=true;DiscardHallDraft();return key;
            }
            catch
            {if(ground!=null)RestoreHallGround();throw;}
        }
        private void ClearHall()
        {
            CloseHallMenu();ClearHallPieces();_sketchShown.Clear();_sketchPending.Clear();_hallProblemUntil=0;_hallAnnouncedProblem=null;_hallAnnouncedCorners=-1;_hallGuideHasFloor=false;_hallDoorPoints.Clear();_hallChimneyPoints.Clear();_hallGroundJob.Clear();_hallTargetMaps.Clear();_hallDesign=null;_hallDue=0;_hallProblem=_hallNote=_hallFingerprint=null;
            _hallRoles.Clear();_hallBill.Clear();_hallStations.Clear();_hallSupport.Clear();
        }
        private void DestroyHall()
        {ClearHallPieces();DestroySketch();if(_hallTemplateHolder!=null)Destroy(_hallTemplateHolder);_hallTemplateHolder=null;_hallTemplates.Clear();if(_hallMarkerMaterial!=null)Destroy(_hallMarkerMaterial);_hallMarkerMaterial=null;_hallCapture=null;_hallUnderlying=_hallBaseline=null;foreach(var material in _hallGhostMaterials.Values)if(material!=null)Destroy(material);_hallGhostMaterials.Clear();_hallMeshes.Clear();_hallMaterials.Clear();_hallColliderShapes.Clear();}

        private List<HallMesh> HallMeshes(string name)
        {
            if(_hallMeshes.TryGetValue(name,out var cached))return cached;
            var list=ReadHallMeshes(_hallCatalog[name]);_hallMeshes[name]=list;return list;
        }
        private static List<HallMesh> ReadHallMeshes(GameObject prefab)
        {
            var list=new List<HallMesh>();
            // A piece keeps its distant, simpler models (flat planes) beside the close one in a level-of-detail group;
            // drawing them all at once made overlapping planes flicker. Under a group, draw only its closest level.
            var nearest=new Dictionary<LODGroup,HashSet<Renderer>>();
            foreach(LODGroup group in prefab.GetComponentsInChildren<LODGroup>(true))
            {LOD[] levels=group.GetLODs();nearest[group]=new HashSet<Renderer>(levels.Length>0?levels[0].renderers.Where(r=>r!=null):Enumerable.Empty<Renderer>());}
            foreach(MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                MeshRenderer renderer=filter.GetComponent<MeshRenderer>();
                if(filter.sharedMesh==null || renderer==null || !renderer.enabled)continue;
                bool hidden=false;for(Transform t=filter.transform;t!=prefab.transform;t=t.parent)if(!t.gameObject.activeSelf || t.name=="_GhostOnly")hidden=true;
                if(hidden)continue;
                LODGroup owner=filter.GetComponentInParent<LODGroup>(true);
                if(owner!=null && nearest.TryGetValue(owner,out var close) && close.Count>0 && !close.Contains(renderer))continue;
                string path=filter.name;for(Transform t=filter.transform.parent;t!=null && t!=prefab.transform;t=t.parent)path=t.name+"/"+path;
                list.Add(new HallMesh{Path=path,Mesh=filter.sharedMesh,Local=prefab.transform.worldToLocalMatrix*filter.transform.localToWorldMatrix,Materials=renderer.sharedMaterials});
            }
            return list;
        }
        private Material HallGhostMaterial(Material source)
        {
            if(_hallGhostMaterials.TryGetValue(source,out var cached))return cached;
            Shader shader=new[]{"Legacy Shaders/Transparent/Diffuse","Sprites/Default"}.Select(Shader.Find).FirstOrDefault(s=>s!=null && s.isSupported);
            if(shader==null)throw new InvalidOperationException("No supported preview shader.");
            var material=new Material(shader){color=new Color(0.65f,0.9f,0.82f,0.65f),renderQueue=3000};
            if(source.HasProperty("_MainTex"))material.mainTexture=source.mainTexture;
            _hallGhostMaterials[source]=material;return material;
        }
        // The preview is made of real renderers, one stripped copy per piece: drawn straight with Graphics.DrawMesh, the game's piece
        // shader smeared and speckled them. Rebuilt only when the design or the preview settings change.
        private GameObject _hallPieceRoot,_hallTemplateHolder;
        private string _hallPieceKey;
        private int _hallPreviewVersion;
        private readonly Dictionary<string,GameObject> _hallTemplates=new Dictionary<string,GameObject>();
        private GameObject HallTemplate(string name)
        {
            if(_hallTemplates.TryGetValue(name,out var cached) && cached!=null)return cached;
            if(_hallTemplateHolder==null){_hallTemplateHolder=new GameObject("Hallwright templates");_hallTemplateHolder.SetActive(false);}
            var template=new GameObject(name);template.transform.SetParent(_hallTemplateHolder.transform,false);
            foreach(HallMesh mesh in HallMeshes(name))
            {
                var part=new GameObject(mesh.Path);part.transform.SetParent(template.transform,false);
                part.transform.localPosition=mesh.Local.GetPosition();part.transform.localRotation=mesh.Local.rotation;part.transform.localScale=mesh.Local.lossyScale;
                part.AddComponent<MeshFilter>().sharedMesh=mesh.Mesh;
                var renderer=part.AddComponent<MeshRenderer>();renderer.sharedMaterials=mesh.Materials;renderer.shadowCastingMode=ShadowCastingMode.Off;
            }
            return _hallTemplates[name]=template;
        }
        private void ClearHallPieces(){if(_hallPieceRoot!=null)Destroy(_hallPieceRoot);_hallPieceRoot=null;_hallPieceKey=null;_sketchArrivals.Clear();}
        private void DrawHallMeshes()
        {
            AnimateSketch();
            string key=$"{_hallPreviewVersion}:{HallPreviewMode}:{_hallShowRoof}";
            if(key==_hallPieceKey)return;
            ClearHallPieces();_hallPieceKey=key;
            // Out of Sketch, or no hall to draw (an outline gone invalid, Layout): when it comes back, it's drawn in again.
            if(!_hallSketch || _hallGuideOnly || _output.Count==0){_sketchShown.Clear();_sketchPending.Clear();}
            if(_hallGuideOnly || _output.Count==0)return;
            _hallPieceRoot=new GameObject("Hallwright preview");
            var drawn=new List<(GameObject,PiecePose,bool)>();
            if(_hallSketch)DrawSketchConstruction(_hallPieceRoot.transform);
            for(int i=0;i<_output.Count;i++)
            {
                var pose=_output[i];if(!_hallShowRoof && _hallRoles[i].EndsWith("roof",StringComparison.Ordinal))continue;
                _hallSupport.TryGetValue(i.ToString(),out var support);
                bool falls=support?.Collapses==true;
                if(_hallSketch)
                {
                    var drawing=Instantiate(HallSketchTemplate(pose.Prefab),pose.Position,pose.Rotation,_hallPieceRoot.transform);
                    SketchMix(drawing,pose.Position);
                    if(falls)SketchFalls(drawing);
                    drawn.Add((drawing,pose,falls));
                    continue;
                }
                var copy=Instantiate(HallTemplate(pose.Prefab),pose.Position,pose.Rotation,_hallPieceRoot.transform);
                if(!_hallSolid || falls)foreach(var renderer in copy.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if(!_hallSolid)renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>m==null?null:HallGhostMaterial(m)).ToArray();
                    if(falls){_hallTint.Clear();_hallTint.SetColor("_Color",new Color(1,0.3f,0.2f,0.75f));renderer.SetPropertyBlock(_hallTint);}
                }
            }
            if(_hallSketch)SketchArrive(drawn);
        }
    }
}
