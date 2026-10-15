using System.Collections.Generic;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Factorheim
{
    // The launch's sounds (made in code), smoke and the Valkyrie who flies alongside.
    internal static class Fx
    {
        // A countdown pip, higher and longer on the last second.
        internal static AudioClip Beep(bool last)=>last
            ?Sound.Make("fh_beep_last",0.5f,t=>0.5f*Sound.Sine(t*1320f)*Sound.Env(t,0.01f,0.5f))
            :Sound.Make("fh_beep",0.16f,t=>0.45f*Sound.Sine(t*880f)*Sound.Env(t,0.005f,0.16f));

        // Engine roar: low-passed noise over a 38 Hz hum, made to loop (4 s, the ends faded into each other).
        internal static AudioClip Rumble()
        {
            float lp=0f,lp2=0f;
            return Sound.Make("fh_rumble",4f,t=>
            {
                lp+=(Sound.Noise()-lp)*0.06f;lp2+=(lp-lp2)*0.08f;
                float edge=Mathf.Clamp01(t/0.25f)*Mathf.Clamp01((4f-t)/0.25f);
                return (lp2*2.6f+0.25f*Sound.Sine(t*38f)+0.12f*Sound.Sine(t*57f))*(0.6f+0.4f*edge);
            });
        }
        // The horn that greets you in Valhalla: a low open fifth, swelling.
        internal static AudioClip Horn()=>Sound.Make("fh_horn",3.2f,t=>
        {
            float env=Sound.Env(t,0.6f,3.2f);
            float vib=1f+0.004f*Mathf.Sin(t*30f);
            return env*(0.28f*Sound.Saw(t*98f*vib)+0.2f*Sound.Saw(t*147f*vib)+0.1f*Sound.Sine(t*196f));
        });

        // Billowing smoke left behind in the world (world space: the rocket climbs out of its own column).
        private static Material _smokeMat;private static Texture2D _puff;
        internal static ParticleSystem Smoke(Transform parent,Vector3 at)
        {
            var go=new GameObject("smoke");
            go.SetActive(false);
            go.transform.SetParent(parent,false);go.transform.localPosition=at;go.transform.localRotation=Quaternion.Euler(90,0,0);   // (the cone points down)
            ParticleSystem ps=go.AddComponent<ParticleSystem>();
            var main=ps.main;
            main.loop=true;main.duration=5f;main.startLifetime=new ParticleSystem.MinMaxCurve(5f,9f);main.startSpeed=new ParticleSystem.MinMaxCurve(4f,9f);
            main.startSize=new ParticleSystem.MinMaxCurve(3f,6f);main.startRotation=new ParticleSystem.MinMaxCurve(0f,6.28f);
            main.startColor=new ParticleSystem.MinMaxGradient(new Color(0.78f,0.76f,0.72f,0.75f),new Color(0.55f,0.53f,0.5f,0.7f));
            main.simulationSpace=ParticleSystemSimulationSpace.World;main.maxParticles=600;main.gravityModifier=-0.03f;main.playOnAwake=true;
            var em=ps.emission;em.rateOverTime=0f;
            var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Cone;shape.angle=40f;shape.radius=1.2f;
            var size=ps.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1f,AnimationCurve.Linear(0,0.6f,1,2.6f));
            var col=ps.colorOverLifetime;col.enabled=true;
            var g=new Gradient();
            g.SetKeys(new[]{new GradientColorKey(new Color(1f,0.75f,0.45f),0f),new GradientColorKey(Color.white,0.15f),new GradientColorKey(Color.white,1f)},
                new[]{new GradientAlphaKey(0f,0f),new GradientAlphaKey(1f,0.08f),new GradientAlphaKey(0.6f,0.5f),new GradientAlphaKey(0f,1f)});
            col.color=g;
            var vel=ps.limitVelocityOverLifetime;vel.enabled=true;vel.limit=2f;vel.dampen=0.15f;
            var r=go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial=SmokeMaterial();r.renderMode=ParticleSystemRenderMode.Billboard;r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(true);
            return ps;
        }
        private static Material SmokeMaterial()
        {
            if(_smokeMat!=null)return _smokeMat;
            _puff=new Texture2D(64,64,TextureFormat.RGBA32,false){wrapMode=TextureWrapMode.Clamp};
            for(int y=0;y<64;y++)for(int x=0;x<64;x++)
            {
                float dx=(x-31.5f)/31.5f,dy=(y-31.5f)/31.5f,d=Mathf.Sqrt(dx*dx+dy*dy);
                float n=0.75f+0.25f*Mathf.PerlinNoise(x*0.13f,y*0.13f);
                _puff.SetPixel(x,y,new Color(n,n,n,Mathf.Clamp01(1f-d)*Mathf.Clamp01(1f-d)*n));
            }
            _puff.Apply();
            _smokeMat=new Material(Shader.Find("Sprites/Default")){mainTexture=_puff};
            Look.Own(_smokeMat);Look.Own(_puff);
            return _smokeMat;
        }

        // A Valkyrie that is only a picture: the game's own (from the intro) with its flight script, networking and colliders taken off,
        // so nothing else happens when she's made (the real one picks up the local player). Animator and renderers kept: she flaps.
        internal static GameObject Valkyrie()
        {
            GameObject prefab=ZNetScene.instance?.GetPrefab("Valkyrie");
            if(prefab==null)return null;
            var holder=new GameObject("FH_ValkyrieHolder");holder.SetActive(false);
            GameObject v=Object.Instantiate(prefab,holder.transform,false);
            v.name="FH_Valkyrie";
            var keep=new HashSet<System.Type>{typeof(Transform),typeof(MeshFilter),typeof(MeshRenderer),typeof(SkinnedMeshRenderer),typeof(Animator),typeof(LODGroup)};
            for(int pass=0;pass<3;pass++)
                foreach(Component c in v.GetComponentsInChildren<Component>(true))
                    if(c!=null&&!keep.Contains(c.GetType()))Object.DestroyImmediate(c);
            v.transform.SetParent(null,false);
            Object.Destroy(holder);
            v.SetActive(true);
            return v;
        }
    }
}
