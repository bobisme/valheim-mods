using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Demolisher
{
    // The outline on the ground: placed edges in red (a loop once closed), a white line from the last corner to where the next
    // click would land and a faint one on back to the first, and a post at each corner.
    public sealed partial class Plugin
    {
        private GameObject _outlineRoot;
        private Material _lineMaterial;
        private LineRenderer _edges,_next,_back;
        private readonly List<LineRenderer> _posts=new List<LineRenderer>();
        private static readonly Color EdgeColor=new Color(1f,0.2f,0.12f,0.95f),NextColor=Color.white,BackColor=new Color(1f,1f,1f,0.45f),BadColor=new Color(1f,0.55f,0.1f,0.95f);

        private LineRenderer Line(string name,float width)
        {
            if(_lineMaterial==null)_lineMaterial=new Material(Shader.Find("Sprites/Default")){renderQueue=3050};
            if(_outlineRoot==null)_outlineRoot=new GameObject("Demolisher outline");
            var go=new GameObject(name){layer=LayerMask.NameToLayer("Ignore Raycast")};go.transform.SetParent(_outlineRoot.transform,false);
            var line=go.AddComponent<LineRenderer>();
            line.useWorldSpace=true;line.sharedMaterial=_lineMaterial;line.startWidth=line.endWidth=width;
            line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;line.alignment=LineAlignment.View;line.positionCount=0;
            return line;
        }
        // Points on the ground every metre from a to b, a little above it so the line doesn't sink into slopes.
        private static void GroundPath(List<Vector3> into,Vector3 a,Vector3 b)
        {
            Vector3 flat=b-a;flat.y=0;
            int n=Mathf.Clamp(Mathf.CeilToInt(flat.magnitude),1,256);
            for(int i=into.Count==0?0:1;i<=n;i++)into.Add(Ground(Vector3.Lerp(a,b,i/(float)n))+Vector3.up*0.15f);
        }
        private static void SetPath(LineRenderer line,List<Vector3> points,Color color)
        {
            line.startColor=line.endColor=color;line.positionCount=points.Count;line.SetPositions(points.ToArray());
        }

        private void RedrawOutline()
        {
            if(_edges==null){_edges=Line("Edges",0.14f);_next=Line("Next",0.1f);_back=Line("Back",0.08f);}
            var points=new List<Vector3>();
            for(int i=0;i+1<_corners.Count;i++)GroundPath(points,_corners[i],_corners[i+1]);
            if(_closed && _corners.Count>=3)GroundPath(points,_corners[_corners.Count-1],_corners[0]);
            SetPath(_edges,points,EdgeColor);
            while(_posts.Count<_corners.Count)_posts.Add(Line("Post",0.08f));
            for(int i=0;i<_posts.Count;i++)
            {
                bool shown=i<_corners.Count;_posts[i].gameObject.SetActive(shown);
                if(shown)SetPath(_posts[i],new List<Vector3>{_corners[i],_corners[i]+Vector3.up*(i==0?2.5f:1.8f)},EdgeColor);
            }
            _liveKey=null;
        }
        private string _liveKey;
        // Every frame with the stake in hand: where the next corner would go.
        private void UpdateLive(Player player)
        {
            if(_next==null)RedrawOutline();
            Vector3 point=default;bool closes=false;string problem=null;
            bool show=!_closed && _corners.Count>=1 && _queue.Count==0 && NextCorner(out point,out closes,out problem);
            if(!show){_next.positionCount=0;_back.positionCount=0;_liveKey=null;return;}
            string key=$"{_corners.Count}:{point.x:0.0},{point.z:0.0}:{problem}";
            if(key==_liveKey)return;
            _liveKey=key;
            var next=new List<Vector3>();GroundPath(next,_corners[_corners.Count-1],point);
            SetPath(_next,next,problem!=null?BadColor:NextColor);
            if(_corners.Count>=2 && !closes){var back=new List<Vector3>();GroundPath(back,point,_corners[0]);SetPath(_back,back,BackColor);}
            else _back.positionCount=0;
        }
        private void ShowOutline(bool shown){if(_outlineRoot!=null && _outlineRoot.activeSelf!=shown)_outlineRoot.SetActive(shown);}
        private void DestroyOutline()
        {
            if(_outlineRoot!=null)Destroy(_outlineRoot);
            if(_lineMaterial!=null)Destroy(_lineMaterial);
            _outlineRoot=null;_lineMaterial=null;_edges=_next=_back=null;_posts.Clear();
        }
    }
}
