using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hallwright
{
    // While drawing a Hallwright outline: dotted lines laid on the ground between corners, a live line from the last corner
    // to where the next click would land (and on back to the first), and each line's length floating above it.
    public sealed partial class Plugin
    {
        private Material _hallDotMaterial;
        private Texture2D _hallDotTexture;
        private GameObject _hallLiveNext, _hallLiveBack;
        private string _hallLiveKey;
        private readonly List<(Vector3 at,string text,bool live)> _hallLabels=new List<(Vector3,string,bool)>();
        private GUIStyle _hallLabelStyle, _hallLiveLabelStyle, _hallLabelShadow;
        private static readonly Color HallEdgeColor=new Color(1f,0.78f,0.25f,0.95f), HallLiveColor=new Color(1f,1f,1f,1f), HallBackColor=new Color(1f,1f,1f,0.55f), HallBadColor=new Color(1f,0.35f,0.3f,0.9f);

        // The snapped corner a click at this spot would add (the same rules as MarkHall); closes is true on the first corner.
        private bool HallSnap(Vector3 hit,out Vector3 point,out Quaternion frame,out bool closes,out string problem)
        {
            point=default;frame=_hallFrame;closes=false;problem=null;
            Vector3 ground=HallMarkerGround(hit);
            if(_markers.Count==1)
            {
                Vector3 right=ground-_hallOrigin;right.y=0;
                if(right.magnitude<1){problem="Make the first edge at least 2 metres long.";return false;}
                right.Normalize();frame=HallYaw(Quaternion.LookRotation(Vector3.Cross(right,Vector3.up),Vector3.up).eulerAngles.y);
            }
            Vector3 local=Quaternion.Inverse(frame)*(ground-_hallOrigin),last=Quaternion.Inverse(frame)*(_markers[_markers.Count-1]-_hallOrigin);
            last.y=0;
            local=new Vector3(Mathf.Round(local.x/2)*2,0,Mathf.Round(local.z/2)*2);
            if(_markers.Count>=3 && local.sqrMagnitude<0.1f){point=_markers[0];closes=true;return true;}
            if(Mathf.Abs(local.x-last.x)>0.01 && Mathf.Abs(local.z-last.z)>0.01)
            {if(Mathf.Abs(local.x-last.x)>=Mathf.Abs(local.z-last.z))local.z=last.z;else local.x=last.x;}
            // Squaring the corner can land it on the first corner: that closes the outline too (never a duplicate corner).
            if(_markers.Count>=3 && local.sqrMagnitude<0.1f){point=_markers[0];closes=true;return true;}
            if((local-last).sqrMagnitude<1){problem="Place a distinct corner on the grid.";return false;}
            point=HallMarkerGround(_hallOrigin+frame*local);return true;
        }

        private Material HallDots()
        {
            if(_hallDotMaterial!=null)return _hallDotMaterial;
            Shader shader=Shader.Find("Sprites/Default");
            if(shader==null)return null;
            _hallDotTexture=new Texture2D(2,1,TextureFormat.RGBA32,false){wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Point};
            _hallDotTexture.SetPixels(new[]{Color.white,new Color(1,1,1,0)});_hallDotTexture.Apply();
            // Tiled along the line in world units: two and a half dashes per metre.
            _hallDotMaterial=new Material(shader){mainTexture=_hallDotTexture,mainTextureScale=new Vector2(2.5f,1),renderQueue=3050};
            return _hallDotMaterial;
        }
        // Points on the terrain every half metre from a to b, just above it so the dots don't sink into slopes.
        private static Vector3[] HallGroundPath(Vector3 a,Vector3 b)
        {
            Vector3 flat=b-a;flat.y=0;
            int n=Mathf.Clamp(Mathf.CeilToInt(flat.magnitude/0.5f),1,256);
            var points=new Vector3[n+1];
            for(int i=0;i<=n;i++)points[i]=HallMarkerGround(Vector3.Lerp(a,b,i/(float)n))+Vector3.up*0.15f;
            return points;
        }
        private GameObject HallDottedLine(Vector3[] points,Color color,GameObject reuse=null)
        {
            Material dots=HallDots();
            if(dots==null)return null;
            GameObject go=reuse;
            if(go==null)
            {
                go=new GameObject("Hallwright dotted line"){layer=LayerMask.NameToLayer("Ignore Raycast")};
                var made=go.AddComponent<LineRenderer>();made.useWorldSpace=true;made.sharedMaterial=dots;made.textureMode=LineTextureMode.Tile;
                made.startWidth=made.endWidth=0.16f;made.shadowCastingMode=ShadowCastingMode.Off;made.receiveShadows=false;made.alignment=LineAlignment.View;
                _visuals.Add(go);
            }
            var line=go.GetComponent<LineRenderer>();
            line.startColor=line.endColor=color;line.positionCount=points.Length;line.SetPositions(points);go.SetActive(true);
            return go;
        }
        private static string HallLength(Vector3 a,Vector3 b){Vector3 d=b-a;d.y=0;return $"{d.magnitude:0.#} m";}
        // Live labels float higher than the placed edges' so the two never sit on top of each other.
        private static Vector3 HallLabelAt(Vector3[] path,bool live=false)=>path[path.Length/2]+Vector3.up*(live?1.6f:0.7f);

        // Called from DrawHallGuides after every preview rebuild: the placed edges.
        private void DrawHallEdges()
        {
            _hallLabels.RemoveAll(l=>!l.live);
            int count=_markers.Count;bool closed=count>=3 && _hallDesign!=null;
            for(int i=0;i<count-1 || closed && i<count;i++)
            {
                Vector3 a=_markers[i],b=_markers[(i+1)%count];
                Vector3[] path=HallGroundPath(a,b);
                HallDottedLine(path,HallEdgeColor);
                _hallLabels.Add((HallLabelAt(path),HallLength(a,b),false));
            }
            _hallLiveKey=null; // ClearVisuals took the live lines with it.
        }

        // Every frame while drawing: where the next corner would go.
        private void UpdateHallLive(Player player)
        {
            RaycastHit hit=default;
            bool show=!_hallMenu && _markers.Count>=1 && _markers.Count<HallLayout.MaximumCorners && player!=null && CameraRay(out Ray ray)
                && Physics.Raycast(ray,out hit,80,BuildLayers,QueryTriggerInteraction.Ignore);
            Vector3 point=default;bool closes=false;string problem=null;
            if(show)
            {
                show=HallSnap(hit.point,out point,out _,out closes,out problem);
                if(show && Vector3.Distance(hit.point,player.transform.position)>40)problem="move closer";
            }
            string key=show?$"{_markers.Count}:{point.x:0.00},{point.z:0.00}:{problem}":null;
            if(key==_hallLiveKey && (key==null || _hallLiveNext!=null))return;
            _hallLiveKey=key;_hallLabels.RemoveAll(l=>l.live);
            if(!show)
            {
                if(_hallLiveNext!=null)_hallLiveNext.SetActive(false);
                if(_hallLiveBack!=null)_hallLiveBack.SetActive(false);
                return;
            }
            Vector3 last=_markers[_markers.Count-1];
            Vector3[] next=HallGroundPath(last,point);
            _hallLiveNext=HallDottedLine(next,problem!=null?HallBadColor:HallLiveColor,_hallLiveNext);
            _hallLabels.Add((HallLabelAt(next,true),HallLength(last,point)+(problem!=null?" · "+problem:closes?" · closes the outline":""),true));
            if(_markers.Count>=2 && !closes)
            {
                Vector3[] back=HallGroundPath(point,_markers[0]);
                _hallLiveBack=HallDottedLine(back,HallBackColor,_hallLiveBack);
                _hallLabels.Add((HallLabelAt(back,true),HallLength(point,_markers[0]),true));
            }
            else if(_hallLiveBack!=null)_hallLiveBack.SetActive(false);
        }

        private void DrawHallLabels()
        {
            Camera camera=Camera.main;
            if(camera==null || _hallLabels.Count==0)return;
            if(_hallLabelStyle==null)
            {
                _hallLabelStyle=new GUIStyle(GUI.skin.label){alignment=TextAnchor.MiddleCenter,fontSize=Mathf.RoundToInt(Mathf.Clamp(Screen.height/50f,16,30)),fontStyle=FontStyle.Bold,normal={textColor=new Color(1f,0.82f,0.4f)}};
                _hallLiveLabelStyle=new GUIStyle(_hallLabelStyle){normal={textColor=Color.white}};
                _hallLabelShadow=new GUIStyle(_hallLabelStyle){normal={textColor=new Color(0,0,0,0.75f)}};
            }
            foreach(var label in _hallLabels)
            {
                Vector3 screen=camera.WorldToScreenPoint(label.at);
                if(screen.z<=0.5f || screen.z>90)continue;
                var rect=new Rect(screen.x-150,Screen.height-screen.y-14,300,28);
                GUI.Label(new Rect(rect.x+1.5f,rect.y+1.5f,rect.width,rect.height),label.text,_hallLabelShadow);
                GUI.Label(rect,label.text,label.live?_hallLiveLabelStyle:_hallLabelStyle);
            }
        }
        private void DestroyHallDrawing()
        {
            if(_hallDotMaterial!=null)Destroy(_hallDotMaterial);
            if(_hallDotTexture!=null)Destroy(_hallDotTexture);
            _hallDotMaterial=null;_hallDotTexture=null;_hallLabels.Clear();_hallLiveKey=null;
        }
    }
}
