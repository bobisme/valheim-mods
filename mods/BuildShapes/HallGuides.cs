using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace BuildShapes
{
    public sealed partial class Plugin
    {
        private bool _hallGuideOnly,_hallGuideHasFloor;
        private Material _hallMarkerMaterial;
        private static Vector3 HallMarkerGround(Vector3 point)
        {
            if(Physics.Raycast(point+Vector3.up*30,Vector3.down,out RaycastHit hit,70,LayerMask.GetMask("terrain"),QueryTriggerInteraction.Ignore))point.y=hit.point.y;
            return point;
        }
        private void HallMarkerLine(Vector3[] points,float width)
        {
            if(_hallMarkerMaterial==null)
            {
                Shader shader=new[]{"Hidden/Internal-Colored","Sprites/Default"}.Select(Shader.Find).FirstOrDefault(s=>s!=null && s.isSupported);
                if(shader==null)return;
                _hallMarkerMaterial=new Material(shader){color=Color.white,renderQueue=3100};
                if(_hallMarkerMaterial.HasProperty("_ZTest"))_hallMarkerMaterial.SetInt("_ZTest",(int)CompareFunction.Always);
                if(_hallMarkerMaterial.HasProperty("_ZWrite"))_hallMarkerMaterial.SetInt("_ZWrite",0);
                if(_hallMarkerMaterial.HasProperty("_SrcBlend"))_hallMarkerMaterial.SetInt("_SrcBlend",(int)BlendMode.SrcAlpha);
                if(_hallMarkerMaterial.HasProperty("_DstBlend"))_hallMarkerMaterial.SetInt("_DstBlend",(int)BlendMode.OneMinusSrcAlpha);
                if(_hallMarkerMaterial.HasProperty("_Cull"))_hallMarkerMaterial.SetInt("_Cull",(int)CullMode.Off);
            }
            int before=_visuals.Count;Line(points,width,true);
            if(_visuals.Count>before)
            {
                var line=_visuals[_visuals.Count-1].GetComponent<LineRenderer>();line.sharedMaterial=_hallMarkerMaterial;
                line.startColor=line.endColor=new Color(1f,0.75f,0.2f,0.8f);
                line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;
            }
        }
        private void DrawHallGuides()
        {
            if(_markers.Count==0)return;
            float floor=_hallGuideHasFloor?_hallFloorY:_markers.Max(p=>p.y)+_hallRaise;
            float wallTop=floor+_hallHeight*_hallStoreys;
            Vector3 At(Vector3 point,float y)=>new Vector3(point.x,y,point.z);
            foreach(Vector3 marker in _markers)
            {
                Vector3 ground=HallMarkerGround(marker)+Vector3.up*0.06f;
                Vector3 tip=At(ground,Mathf.Max(ground.y+4,wallTop));
                // Only narrow marker posts show through occlusion; the building outline uses normal depth.
                HallMarkerLine(new[]{ground,tip},0.065f);
                foreach(Vector3 point in new[]{ground,tip})
                {
                    HallMarkerLine(new[]{point-Vector3.right*0.25f,point+Vector3.right*0.25f},0.05f);
                    HallMarkerLine(new[]{point-Vector3.forward*0.25f,point+Vector3.forward*0.25f},0.05f);
                }
                if(_hallGuideHasFloor)Line(new[]{At(marker,floor),At(marker,wallTop)},0.04f);
            }
            // Until the outline solves, these are provisional open edges at a common height.
            bool closed=_hallGuideHasFloor && _hallDesign!=null;
            for(int level=0;level<=_hallStoreys;level++)
            {
                var ring=_markers.Select(p=>At(p,floor+level*_hallHeight+0.06f)).ToList();
                if(closed)ring.Add(ring[0]);
                if(ring.Count>1)Line(ring.ToArray(),level==0?0.07f:0.04f,level==0);
            }
            if(closed && _hallBasement)Line(_markers.Concat(new[]{_markers[0]}).Select(p=>At(p,floor-3+0.06f)).ToArray(),0.045f);
            if(closed && _hallShowRoof)foreach(var wing in _hallDesign.Wings)
            {
                double width=wing.Width*2,length=wing.Length*2,peak=_hallHeight*_hallStoreys+width*0.5*_hallKit.Slope+wing.TierLift;
                Vector3 W(double u,double y,double v)=>HallWorld(wing.At(u,y,v));
                foreach(double end in new[]{0.0,length})
                {
                    if(wing.TierLift==0)Line(new[]{W(0,_hallHeight*_hallStoreys,end),W(width/2,peak,end),W(width,_hallHeight*_hallStoreys,end)},0.04f);
                    else
                    {
                        double shoulder=_hallHeight*_hallStoreys+2*_hallKit.Slope;
                        Line(new[]{W(0,_hallHeight*_hallStoreys,end),W(2,shoulder,end),W(2,shoulder+wing.TierLift,end),W(width/2,peak,end),W(width-2,shoulder+wing.TierLift,end),W(width-2,shoulder,end),W(width,_hallHeight*_hallStoreys,end)},0.04f);
                    }
                }
                Line(new[]{W(width/2,peak,0),W(width/2,peak,length)},0.045f);
            }
            foreach(var door in _hallDoorPoints)
            {
                Vector3 ground=HallMarkerGround(HallWorld(door))+Vector3.up*0.06f;
                Vector3 top=At(ground,Mathf.Max(ground.y+3,floor+(float)(_hallKit?.DoorHeight??2)));
                HallMarkerLine(new[]{ground,top},0.05f);
                HallMarkerLine(new[]{top-Vector3.right*0.25f,top+Vector3.right*0.25f},0.05f);
            }
            if(closed)foreach(var door in _hallDesign.Entrances)
            {
                Vector3 a=HallWorld(door.At+HallLayout.Turn(new V3(-1,0,0),door.Yaw)),b=HallWorld(door.At+HallLayout.Turn(new V3(1,0,0),door.Yaw));
                HallMarkerLine(new[]{a,a+Vector3.up*(float)_hallKit.DoorHeight,b+Vector3.up*(float)_hallKit.DoorHeight,b},0.045f);
            }
        }
    }
}
