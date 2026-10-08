using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace Farmhand
{
    public sealed partial class Plugin
    {
        private ConfigEntry<KeyCode> _markerKey,_plotKey,_removeMarker,_clearPlot;
        private ConfigEntry<float> _plotAreaLimit,_plotDistance;
        private readonly List<Vector3> _corners=new List<Vector3>();
        private readonly List<GameObject> _cornerLooks=new List<GameObject>();
        private readonly List<PlotSite> _sites=new List<PlotSite>();
        private LineRenderer _plotLine;
        private Material _outlineMaterial,_cornerMaterial,_readyMaterial,_farMaterial,_blockedMaterial;
        private Player _plotOwner;
        private Piece _plotCrop;
        private float _plotSpacing,_nextPlotRefresh;
        private int _refreshSite;
        private bool _plotDirty=true,_plantingPlot;
        private string _plotError;
        private double _plotArea;
        private sealed class PlotSite { internal Vector3 Position; internal bool Grounded; internal int State; internal GameObject Look; internal Renderer Renderer; }
        internal bool Marking => Ready(Player.m_localPlayer)&&Input.GetKey(_modifier.Value);

        private void SetupPlot()
        {
            _markerKey=Config.Bind("Controls","PlacePlotCorner",KeyCode.Mouse0,"With modifier held, place plot corners in boundary order.");
            _plotKey=Config.Bind("Controls","PlantPlot",KeyCode.J,"With modifier held, plant the marked plot. Walk through it to bring pending spots within normal reach.");
            _removeMarker=Config.Bind("Controls","RemovePlotCorner",KeyCode.Backspace,"Remove the last plot corner.");
            _clearPlot=Config.Bind("Controls","ClearPlot",KeyCode.Delete,"Clear the marked plot and its preview.");
            _plotAreaLimit=Config.Bind("Planting","MaximumPlotArea",400f,new ConfigDescription("Maximum marked plot area in square metres.",new AcceptableValueRange<float>(1,1000)));
            _plotAreaLimit.SettingChanged+=(sender,args)=>_plotDirty=true;
            _plotDistance=Config.Bind("Planting","MarkerDistance",30f,new ConfigDescription("Maximum distance to the aimed corner in metres.",new AcceptableValueRange<float>(5,40)));
        }
        private void UpdatePlot(Player p)
        {
            if(_corners.Count>0&&_plotOwner!=p)ClearPlot();
            bool visible=Ready(p);
            foreach(GameObject go in _cornerLooks)if(go!=null)go.SetActive(visible);
            if(_plotLine!=null)_plotLine.gameObject.SetActive(visible);
            foreach(PlotSite site in _sites)if(site.Look!=null)site.Look.SetActive(visible&&site.State!=1);
            if(!visible)return;
            Piece selected=p.GetSelectedPiece();
            float spacing=IsCrop(selected)?Layout.Spacing(_spacing.Value,selected.GetComponent<Plant>().m_growRadius):0;
            if(!Busy&&(_plotDirty||selected!=_plotCrop||spacing!=_plotSpacing))Replan(selected,spacing);
            if(Time.unscaledTime<_nextPlotRefresh)return;
            _nextPlotRefresh=Time.unscaledTime+0.1f;
            // Bounded terrain refresh; distant and planted sites need no per-frame ray casts.
            for(int n=0;n<32&&n<_sites.Count;n++)
            { _refreshSite%= _sites.Count;PlotSite site=_sites[_refreshSite++];if(site.State!=1)Ground(site); }
            foreach(PlotSite site in _sites)
            {
                if(site.Look==null||site.State==1)continue;
                bool near=site.Grounded&&Vector3.Distance(p.GetEyePoint(),site.Position)<p.m_maxPlaceDistance+(_plotCrop?.m_extraPlacementDistance??0);
                site.Look.transform.position=site.Position+Vector3.up*0.08f;
                site.Renderer.sharedMaterial=site.State==2||!site.Grounded?_blockedMaterial:near?_readyMaterial:_farMaterial;
            }
        }
        private void MarkPlot(Player p)
        {
            if(!IsCrop(p.GetSelectedPiece())){Say(p,"Select a crop in the cultivator menu first.");return;}
            if(GameAccess.PlanningActive()){Say(p,"Turn off BuildOrders plan mode first.");return;}
            if(_corners.Count>=PlotLayout.MaxMarkers){Say(p,"At most 32 corners; remove one first.");return;}
            Transform aim=GameCamera.instance!=null?GameCamera.instance.transform:null;
            if(aim==null||!Physics.Raycast(aim.position,aim.forward,out RaycastHit hit,80f,LayerMask.GetMask("terrain"),QueryTriggerInteraction.Ignore))return;
            Vector3 delta=hit.point-p.transform.position;delta.y=0;
            if(delta.magnitude>_plotDistance.Value){Say(p,"That corner is too far away.");return;}
            if(_corners.Any(c=>new Vector2(c.x-hit.point.x,c.z-hit.point.z).sqrMagnitude<0.04f))return;
            if(_corners.Count>0&&Vector2.Distance(new Vector2(_corners[0].x,_corners[0].z),new Vector2(hit.point.x,hit.point.z))>_plotDistance.Value*2)
            {Say(p,"Keep the plot closer to its first corner.");return;}
            _plotOwner=p;_corners.Add(hit.point);_plotDirty=true;
        }
        private void Replan(Piece crop,float spacing)
        {
            foreach(PlotSite site in _sites)if(site.Look!=null)Destroy(site.Look);
            _sites.Clear();_refreshSite=0;_plotCrop=crop;_plotSpacing=spacing;_plotDirty=false;_plotError=null;
            var boundary=_corners.Select(v=>new PlotPoint(v.x,v.z)).ToArray();_plotArea=PlotLayout.Area(boundary);
            if(_corners.Count>=3)
            {
                if(!IsCrop(crop))_plotError="Select a crop in the cultivator menu.";
                else try
                {
                    foreach(PlotPoint point in PlotLayout.Plan(boundary,spacing,_plotAreaLimit.Value))
                    {
                        var site=new PlotSite{Position=new Vector3((float)point.X,_corners[0].y,(float)point.Z)};
                        Ground(site);_sites.Add(site);
                    }
                    if(_sites.Count==0)_plotError="No crops fit with this spacing and border. Widen the plot.";
                }
                catch(ArgumentException ex){_plotError=ex.Message;}
            }
            if(_corners.Count>0)DrawPlot();
        }
        private bool Ground(PlotSite site)
        {
            float top=_corners.Max(v=>v.y)+8,bottom=_corners.Min(v=>v.y)-8;
            site.Grounded=Physics.Raycast(new Vector3(site.Position.x,top,site.Position.z),Vector3.down,out RaycastHit hit,top-bottom,LayerMask.GetMask("terrain"),QueryTriggerInteraction.Ignore);
            if(site.Grounded)site.Position=hit.point;
            return site.Grounded;
        }
        private void StartPlot(Player p)
        {
            if(_corners.Count<3){Say(p,"Mark at least three plot corners with your modifier+marker key first.");return;}
            if(_plotDirty)Replan(p.GetSelectedPiece(),IsCrop(p.GetSelectedPiece())?Layout.Spacing(_spacing.Value,p.GetSelectedPiece().GetComponent<Plant>().m_growRadius):0);
            if(_plotError!=null){Say(p,_plotError);return;}
            if(GameAccess.PlanningActive()){Say(p,"Turn off BuildOrders plan mode first.");return;}
            // A deliberate retry revisits blocked spots, while successful spots stay complete.
            foreach(PlotSite site in _sites)if(site.State==2)site.State=0;
            if(_sites.All(s=>s.State==1)){Say(p,"This plot is planted. Use your clear-plot key for a new plot.");return;}
            _plantingPlot=true;StartWork(p,PlantPlot(p,_plotCrop));
        }
        private IEnumerator PlantPlot(Player p,Piece crop)
        {
            int planted=0,blocked=0;float nextWaitMessage=0;
            while(StillWorking(p))
            {
                PlotSite nearest=null;float distance=float.MaxValue;
                int pending=0;
                foreach(PlotSite site in _sites)
                {
                    if(site.State!=0)continue;pending++;
                    float d=(site.Position-p.GetEyePoint()).sqrMagnitude;
                    if(d<distance&&site.Grounded&&d<Mathf.Pow(p.m_maxPlaceDistance+crop.m_extraPlacementDistance,2))
                    {nearest=site;distance=d;}
                }
                if(pending==0)break;
                if(nearest==null)
                {
                    if(Time.unscaledTime>=nextWaitMessage){Say(p,$"{pending} spots waiting. Walk through your plot; Escape pauses.");nextWaitMessage=Time.unscaledTime+8;}
                    yield return new WaitForSeconds(0.15f);continue;
                }
                if(GameAccess.TryPlant(p,crop,nearest.Position,out string reason))
                {nearest.State=1;planted++;if(nearest.Look!=null)nearest.Look.SetActive(false);}
                else if(reason=="Not enough stamina")
                {
                    if(Time.unscaledTime>=nextWaitMessage){Say(p,"Recovering stamina; Escape pauses planting.");nextWaitMessage=Time.unscaledTime+8;}
                }
                else if(reason=="Missing seeds/materials"||reason=="Cultivator is broken")
                {Say(p,$"Planted {planted}; paused: {reason}. Press {_modifier.Value}+{_plotKey.Value} to continue.");yield break;}
                else if(reason!="Out of reach"){nearest.State=2;blocked++;}
                yield return new WaitForSeconds(0.15f);
            }
            Say(p,$"Planted {planted}; {blocked} blocked spots. {_modifier.Value}+{_plotKey.Value} retries blocked spots; {_clearPlot.Value} clears the plot.");
        }
        private Material PlotMaterial(Color color)
        {
            Shader shader=Shader.Find("Sprites/Default")??Shader.Find("Unlit/Color");
            return shader!=null?new Material(shader){color=color}:null;
        }
        private void DrawPlot()
        {
            if(_outlineMaterial==null)
            {
                _outlineMaterial=PlotMaterial(new Color(0.3f,0.8f,0.55f));_cornerMaterial=PlotMaterial(new Color(0.65f,0.43f,0.23f));
                _readyMaterial=PlotMaterial(new Color(0.3f,0.9f,0.45f));_farMaterial=PlotMaterial(new Color(0.9f,0.67f,0.22f));
                _blockedMaterial=PlotMaterial(new Color(0.9f,0.3f,0.2f));
            }
            GameObject Dot(string name,Vector3 position,float size,Material material)
            {
                GameObject go=GameObject.CreatePrimitive(PrimitiveType.Sphere);go.name=name;go.layer=LayerMask.NameToLayer("Ignore Raycast");
                DestroyImmediate(go.GetComponent<Collider>());go.transform.position=position;go.transform.localScale=Vector3.one*size;
                if(material!=null)go.GetComponent<Renderer>().sharedMaterial=material;return go;
            }
            foreach(GameObject go in _cornerLooks)if(go!=null)Destroy(go);_cornerLooks.Clear();
            foreach(Vector3 corner in _corners)_cornerLooks.Add(Dot("Farmhand corner",corner+Vector3.up*0.12f,0.18f,_cornerMaterial));
            foreach(PlotSite site in _sites)
            {site.Look=Dot("Farmhand crop spot",site.Position+Vector3.up*0.08f,0.10f,_farMaterial);site.Renderer=site.Look.GetComponent<Renderer>();}
            if(_plotLine==null){_plotLine=new GameObject("Farmhand plot").AddComponent<LineRenderer>();_plotLine.useWorldSpace=true;_plotLine.startWidth=_plotLine.endWidth=0.035f;}
            _plotLine.sharedMaterial=_outlineMaterial;
            if(_outlineMaterial!=null)_outlineMaterial.color=_plotError==null?new Color(0.3f,0.8f,0.55f):new Color(0.9f,0.3f,0.2f);
            var points=new List<Vector3>();int edges=_corners.Count>=3?_corners.Count:Math.Max(0,_corners.Count-1);
            for(int edge=0;edge<edges;edge++)
            {
                Vector3 a=_corners[edge],b=_corners[(edge+1)%_corners.Count];int steps=Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(a,b)/0.75f),1,128);
                for(int step=0;step<steps;step++)
                {
                    Vector3 at=Vector3.Lerp(a,b,(float)step/steps);
                    if(Physics.Raycast(at+Vector3.up*8,Vector3.down,out RaycastHit hit,16,LayerMask.GetMask("terrain"),QueryTriggerInteraction.Ignore))at=hit.point;
                    points.Add(at+Vector3.up*0.08f);
                }
            }
            if(edges>0)points.Add(_corners[edges==_corners.Count?0:_corners.Count-1]+Vector3.up*0.08f);
            _plotLine.positionCount=points.Count;_plotLine.SetPositions(points.ToArray());
        }
        private string PlotSummary()
        {
            if(_corners.Count<3)return $"{_corners.Count} corners. Place them around the boundary in order; concave outlines work.";
            if(_plotError!=null)return _plotError;
            return $"{_plotArea:0.#} m² | {_sites.Count} crop spots | {_sites.Count(s=>s.State==1)} planted | {_sites.Count(s=>s.State==0)} waiting | green: in reach, amber: walk closer";
        }
        private void ClearPlot()
        {
            foreach(GameObject go in _cornerLooks)if(go!=null)Destroy(go);_cornerLooks.Clear();
            foreach(PlotSite site in _sites)if(site.Look!=null)Destroy(site.Look);_sites.Clear();_corners.Clear();
            if(_plotLine!=null)Destroy(_plotLine.gameObject);_plotLine=null;
            _plotOwner=null;_plotCrop=null;_plotDirty=true;_plotError=null;_plotArea=0;
        }
        private void DestroyPlot()
        {
            ClearPlot();foreach(Material material in new[]{_outlineMaterial,_cornerMaterial,_readyMaterial,_farMaterial,_blockedMaterial})if(material!=null)Destroy(material);
        }
    }
}
