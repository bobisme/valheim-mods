using System.Collections.Generic;
using UnityEngine;

namespace BuildShapes
{
    public sealed partial class Plugin
    {
        private readonly List<KeyValuePair<string,Vector3>> _repeatAnchors=new List<KeyValuePair<string,Vector3>>();
        private string _anchorPrefab;
        private int _repeatAnchor;
        private Vector2 _anchorScroll;
        private Vector3 RepeatAnchor => _repeatAnchors.Count==0?Vector3.zero:_repeatAnchors[Mathf.Clamp(_repeatAnchor,0,_repeatAnchors.Count-1)].Value;

        private void ResetRepeatAnchors()
        {_repeatAnchors.Clear();_anchorPrefab=null;_repeatAnchor=0;_anchorScroll=Vector2.zero;}
        private void SetRepeatAnchors(string name)
        {
            // Sampling another copy of the same prefab changes its orientation, not the user's anchor.
            if(_anchorPrefab==name && _repeatAnchors.Count>0)return;
            ResetRepeatAnchors();_anchorPrefab=name;
            _repeatAnchors.Add(new KeyValuePair<string,Vector3>("Piece origin",Vector3.zero));
            _repeatAnchors.Add(new KeyValuePair<string,Vector3>("Visual center",LocalBounds(name).center));
            Piece piece=ZNetScene.instance?.GetPrefab(name)?.GetComponent<Piece>();
            if(piece==null)return;
            var snaps=new List<Transform>();piece.GetSnapPoints(snaps);
            var names=new Dictionary<string,int>();
            foreach(Transform snap in snaps)
            {
                if(snap==null)continue;
                Vector3 local=piece.transform.InverseTransformPoint(snap.position);
                if(!V(local).Finite)continue;
                string label=Localization.instance?.Localize(snap.name)??snap.name;
                if(label.StartsWith("$hud_snappoint_"))label=label.Substring("$hud_snappoint_".Length).Replace('_',' ');
                if(string.IsNullOrWhiteSpace(label))label="Snap point";
                int count=names.TryGetValue(label,out int prior)?prior+1:1;names[label]=count;
                if(count>1)label+=$" ({count})";
                _repeatAnchors.Add(new KeyValuePair<string,Vector3>(label,local));
            }
        }
        private bool DrawRepeatAnchors()
        {
            if(_repeatAnchors.Count==0)return false;
            _repeatAnchor=Mathf.Clamp(_repeatAnchor,0,_repeatAnchors.Count-1);
            GUILayout.Label("Path anchor: "+_repeatAnchors[_repeatAnchor].Key,_menuText);
            bool changed=false;
            _anchorScroll=GUILayout.BeginScrollView(_anchorScroll,GUILayout.Height(105));
            for(int i=0;i<_repeatAnchors.Count;i++)
                if(GUILayout.Button((i==_repeatAnchor?"● ":"")+_repeatAnchors[i].Key,i==_repeatAnchor?_menuSelected:_menuButton))
                {changed=i!=_repeatAnchor;_repeatAnchor=i;}
            GUILayout.EndScrollView();
            GUILayout.Label("The chosen point sits on the path. Gold crosses mark it.",_menuHint);
            return changed;
        }
    }
}
