using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Shieldwall
{
    // What every player near a siege sees, drawn on their own game: a red beacon over the rift, a glowing line along the road the
    // horde will take to the stone (it bends as you build walls), and where it will have to break through if the road is shut.
    internal static class Route
    {
        private sealed class Drawn
        {
            internal GameObject Root;internal LineRenderer Road,Breach,Beacon;internal Light RiftLight,BreachLight;
            internal float NextPath;internal Minimap.PinData Pin;internal Vector3 Rift;
        }
        private static readonly Dictionary<Warstone,Drawn> Shown=new Dictionary<Warstone,Drawn>();
        private static readonly List<Vector3> Path=new List<Vector3>();
        private static Material _material;
        private static float _next;

        internal static void Tick()
        {
            Player me=Player.m_localPlayer;
            foreach(Warstone gone in Shown.Keys.Where(w=>w==null||w.Phase==Phase.Idle||me==null||Vector3.Distance(w.transform.position,me.transform.position)>300).ToList())Hide(gone);
            if(me==null)return;
            foreach(var pair in Shown)Pulse(pair.Value);
            if(Time.time<_next)return;
            _next=Time.time+0.5f;
            foreach(Warstone stone in Warstone.Loaded)
            {
                if(stone==null||stone.Phase==Phase.Idle||Vector3.Distance(stone.transform.position,me.transform.position)>300)continue;
                if(!Shown.TryGetValue(stone,out Drawn drawn))Shown[stone]=drawn=Make(stone);
                if(Time.time>=drawn.NextPath)Redraw(stone,drawn);
            }
        }

        private static Material Material()
        {
            if(_material!=null)return _material;
            Shader shader=Shader.Find("Sprites/Default")??Shader.Find("Legacy Shaders/Particles/Alpha Blended")??Shader.Find("Particles/Standard Unlit");
            if(shader==null)return null;
            _material=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
            return _material;
        }
        private static LineRenderer Line(Transform parent,string name,Color color,float width)
        {
            var go=new GameObject(name);go.transform.SetParent(parent,false);
            var line=go.AddComponent<LineRenderer>();
            line.sharedMaterial=Material();line.useWorldSpace=true;line.widthMultiplier=width;line.numCornerVertices=2;line.numCapVertices=2;
            line.startColor=line.endColor=color;line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;line.receiveShadows=false;
            line.positionCount=0;
            return line;
        }
        private static Drawn Make(Warstone stone)
        {
            var drawn=new Drawn{Root=new GameObject("ShieldwallRoute")};
            drawn.Road=Line(drawn.Root.transform,"Road",new Color(1f,0.55f,0.15f,0.5f),0.18f);
            drawn.Breach=Line(drawn.Root.transform,"Breach",new Color(1f,0.15f,0.1f,0.75f),0.16f);
            drawn.Beacon=Line(drawn.Root.transform,"Beacon",new Color(1f,0.2f,0.1f,0.6f),0.9f);
            drawn.Beacon.widthCurve=new AnimationCurve(new Keyframe(0,1.1f),new Keyframe(0.3f,0.6f),new Keyframe(1,0.15f));
            // Bright at the rift, fading into the sky.
            var fade=new Gradient();
            fade.SetKeys(new[]{new GradientColorKey(new Color(1f,0.3f,0.1f),0),new GradientColorKey(new Color(1f,0.1f,0.05f),1)},
                new[]{new GradientAlphaKey(0.75f,0),new GradientAlphaKey(0.35f,0.35f),new GradientAlphaKey(0,1)});
            drawn.Beacon.colorGradient=fade;
            drawn.RiftLight=Glow(drawn.Root.transform,new Color(1f,0.25f,0.1f),14,3);
            drawn.BreachLight=Glow(drawn.Root.transform,new Color(1f,0.1f,0.05f),5,2);
            drawn.BreachLight.enabled=false;
            return drawn;
        }
        private static Light Glow(Transform parent,Color color,float range,float intensity)
        {
            var go=new GameObject("Glow");go.transform.SetParent(parent,false);
            Light light=go.AddComponent<Light>();light.type=LightType.Point;light.color=color;light.range=range;light.intensity=intensity;light.shadows=LightShadows.None;
            return light;
        }
        private static void Redraw(Warstone stone,Drawn drawn)
        {
            drawn.NextPath=Time.time+2;
            Vector3 rift=stone.Rift,goal=stone.transform.position;
            if(drawn.Rift!=rift)
            {
                drawn.Rift=rift;
                drawn.Beacon.positionCount=2;drawn.Beacon.SetPositions(new[]{rift,rift+Vector3.up*40});
                drawn.RiftLight.transform.position=rift+Vector3.up*3;
                if(drawn.Pin!=null&&Minimap.instance!=null)Minimap.instance.RemovePin(drawn.Pin);
                if(Minimap.instance!=null)drawn.Pin=Minimap.instance.AddPin(rift,Minimap.PinType.Icon3,"Rift",false,false);
            }
            if(Pathfinding.instance==null||!Pathfinding.instance.GetPath(rift,goal,Path,Pathfinding.AgentType.Humanoid,false,true)||Path.Count<2)
            {
                // No road found yet (the ground still loading): a straight line until there is.
                Path.Clear();Path.Add(rift);Path.Add(goal);
            }
            var points=Hug(Path);
            drawn.Road.positionCount=points.Count;drawn.Road.SetPositions(points.ToArray());
            Vector3 end=Path[Path.Count-1];
            bool shut=Utils.DistanceXZ(end,goal)>Policy.Reach+1;
            if(shut)
            {
                // The road stops short: the horde will break through from here.
                drawn.Breach.positionCount=2;drawn.Breach.SetPositions(new[]{end+Vector3.up*0.4f,goal+Vector3.up*0.6f});
                drawn.BreachLight.enabled=true;drawn.BreachLight.transform.position=end+Vector3.up*1.2f;
            }
            else{drawn.Breach.positionCount=0;drawn.BreachLight.enabled=false;}
        }
        // Follow the ground between the path's corners so the line does not cut through hills.
        private static List<Vector3> Hug(List<Vector3> corners)
        {
            var points=new List<Vector3>();
            for(int i=0;i<corners.Count-1;i++)
            {
                Vector3 a=corners[i],b=corners[i+1];
                int steps=Mathf.Max(1,Mathf.CeilToInt(Utils.DistanceXZ(a,b)/2));
                for(int s=0;s<steps;s++)
                {
                    Vector3 p=Vector3.Lerp(a,b,s/(float)steps);
                    if(ZoneSystem.instance.GetGroundHeight(p,out float ground)&&Mathf.Abs(ground-p.y)<1.5f)p.y=Mathf.Max(p.y,ground);
                    points.Add(p+Vector3.up*0.3f);
                }
            }
            points.Add(corners[corners.Count-1]+Vector3.up*0.3f);
            return points;
        }
        private static void Pulse(Drawn drawn)
        {
            float t=0.65f+0.35f*Mathf.Sin(Time.time*3);
            if(drawn.Road!=null){Color c=drawn.Road.startColor;c.a=0.5f*t;drawn.Road.startColor=drawn.Road.endColor=c;}
            if(drawn.RiftLight!=null)drawn.RiftLight.intensity=2+1.5f*t;
        }
        private static void Hide(Warstone stone)
        {
            if(!Shown.TryGetValue(stone,out Drawn drawn))return;
            if(drawn.Pin!=null&&Minimap.instance!=null)Minimap.instance.RemovePin(drawn.Pin);
            if(drawn.Root!=null)Object.Destroy(drawn.Root);
            Shown.Remove(stone);
        }
        internal static void Clear()
        {
            foreach(Warstone stone in Shown.Keys.ToList())Hide(stone);
            Shown.Clear();
            if(_material!=null)Object.Destroy(_material);_material=null;
        }
    }
}
