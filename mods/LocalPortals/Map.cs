using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LocalPortals
{
    // The mirrors on the map (a setting, off at first): a pin with the mirror's own picture on each one in explored land,
    // named if it has a name, and on the big map a faint line between the two of a linked pair. The pins are not saved
    // with the map: they come and go with the mirrors.
    internal static class MapPins
    {
        private static readonly Dictionary<ZDOID,Minimap.PinData> Pins=new Dictionary<ZDOID,Minimap.PinData>();
        private static readonly List<(ZDOID a,ZDOID b)> Pairs=new List<(ZDOID,ZDOID)>();
        private static readonly List<RectTransform> Lines=new List<RectTransform>();
        private static readonly System.Reflection.MethodInfo Explored=HarmonyLib.AccessTools.Method(typeof(Minimap),"IsExplored");
        private static float _next;
        private static readonly HashSet<ZDOID> Seen=new HashSet<ZDOID>();
        private static readonly List<ZDOID> Old=new List<ZDOID>();
        internal static int Count=>Pins.Count;

        internal static void Tick()
        {
            if(Time.time<_next)return;
            _next=Time.time+1f;
            Minimap map=Minimap.instance;
            if(map==null||!Plugin.ShowOnMap.Value||Player.m_localPlayer==null){Clear();return;}
            Sprite icon=ZNetScene.instance?.GetPrefab(PortalPrefab.Name)?.GetComponent<Piece>()?.m_icon;
            Seen.Clear();Pairs.Clear();
            foreach(ZDO zdo in Far.Portals)
            {
                if(zdo==null||!zdo.IsValid())continue;
                Vector3 at=zdo.GetPosition();
                if(at.y>3000||!IsExplored(map,at))continue; // (not dungeons in the sky, and only land the player has seen)
                Seen.Add(zdo.m_uid);
                string name=zdo.GetString(LocalPortal.NameKey).RemoveRichTextTags();
                if(!Pins.TryGetValue(zdo.m_uid,out Minimap.PinData pin)||pin.m_name!=name||(pin.m_pos-at).sqrMagnitude>0.01f)
                {
                    if(pin!=null)map.RemovePin(pin);
                    pin=map.AddPin(at,Minimap.PinType.Icon4,name,false,false);
                    if(icon!=null)pin.m_icon=icon;
                    Pins[zdo.m_uid]=pin;
                }
                ZDOID other=LocalPortal.PartnerOf(zdo,out _);
                if(!other.IsNone()&&zdo.m_uid.CompareTo(other)<0)Pairs.Add((zdo.m_uid,other));
            }
            Old.Clear();
            foreach(var pair in Pins)if(!Seen.Contains(pair.Key))Old.Add(pair.Key);
            foreach(ZDOID id in Old){map.RemovePin(Pins[id]);Pins.Remove(id);}
            Pairs.RemoveAll(p=>!Pins.ContainsKey(p.a)||!Pins.ContainsKey(p.b));
        }
        private static bool IsExplored(Minimap map,Vector3 at)
        {
            if(Explored==null)return true;
            try{return (bool)Explored.Invoke(map,new object[]{at});}catch{return true;}
        }

        // Each frame the big map is open: the lines between pairs, where their pins are.
        internal static void Draw()
        {
            Minimap map=Minimap.instance;
            bool show=map!=null&&Pairs.Count>0&&map.m_mode==Minimap.MapMode.Large&&map.m_pinRootLarge!=null&&Plugin.ShowOnMap.Value;
            int used=0;
            if(show)
            {
                RawImage image=map.m_mapImageLarge;
                Rect uv=image.uvRect,rect=image.rectTransform.rect;
                Vector2 Gui(Vector3 p)
                {
                    float half=map.m_textureSize/2f;
                    float mx=(p.x/map.m_pixelSize+half)/map.m_textureSize,my=(p.z/map.m_pixelSize+half)/map.m_textureSize;
                    return new Vector2((mx-uv.xMin)/uv.width*rect.width,(my-uv.yMin)/uv.height*rect.height);
                }
                RectTransform pinShape=map.m_pinPrefab!=null?map.m_pinPrefab.transform as RectTransform:null;
                foreach(var (a,b) in Pairs)
                {
                    if(!Pins.TryGetValue(a,out var pa)||!Pins.TryGetValue(b,out var pb))continue;
                    Vector2 from=Gui(pa.m_pos),to=Gui(pb.m_pos),d=to-from;
                    if(d.magnitude<4)continue;
                    if(used>=Lines.Count)Lines.Add(NewLine(map.m_pinRootLarge,pinShape));
                    RectTransform line=Lines[used++];
                    if(line.parent!=map.m_pinRootLarge)line.SetParent(map.m_pinRootLarge,false);
                    line.SetAsFirstSibling(); // (under the pins)
                    line.gameObject.SetActive(true);
                    line.anchoredPosition=from;
                    line.sizeDelta=new Vector2(d.magnitude,2.5f);
                    line.localRotation=Quaternion.Euler(0,0,Mathf.Atan2(d.y,d.x)*Mathf.Rad2Deg);
                }
            }
            for(int i=used;i<Lines.Count;i++)if(Lines[i]!=null&&Lines[i].gameObject.activeSelf)Lines[i].gameObject.SetActive(false);
        }
        private static RectTransform NewLine(RectTransform parent,RectTransform like)
        {
            var go=new GameObject("LookingGlassLine",typeof(RectTransform),typeof(Image));
            var t=(RectTransform)go.transform;
            t.SetParent(parent,false);
            t.anchorMin=like!=null?like.anchorMin:Vector2.zero;t.anchorMax=like!=null?like.anchorMax:Vector2.zero;
            t.pivot=new Vector2(0,0.5f);
            var image=go.GetComponent<Image>();
            image.color=new Color(0.78f,0.74f,1f,0.55f);
            image.raycastTarget=false;
            return t;
        }

        private static void Clear()
        {
            if(Minimap.instance!=null)foreach(var pin in Pins.Values)Minimap.instance.RemovePin(pin);
            Pins.Clear();Pairs.Clear();
        }
        internal static void Stop()
        {
            Clear();
            foreach(RectTransform line in Lines)if(line!=null)Object.Destroy(line.gameObject);
            Lines.Clear();
        }
    }
}
