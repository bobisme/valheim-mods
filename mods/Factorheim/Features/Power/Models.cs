using System.Linq;
using UnityEngine;

namespace Factorheim
{
    // The power pieces' looks, from primitives (Look.Builder). Moving parts are found by name: "flywheel", particle systems, "light", "gauge".
    internal static class PowerModels
    {
        private static Material Wood=>Look.Paint(new Color(0.36f,0.25f,0.16f),0f,0.15f);
        private static Material DarkWood=>Look.Paint(new Color(0.22f,0.15f,0.1f),0f,0.15f);
        private static Material Iron=>Look.Paint(new Color(0.24f,0.24f,0.25f),0.7f,0.4f);
        private static Material Rust=>Look.Paint(new Color(0.34f,0.2f,0.12f),0.4f,0.25f);
        private static Material Copper=>Look.Slot("copper");
        private static Material Brass=>Look.Slot("brass");
        private static Material Stone=>Look.Paint(new Color(0.42f,0.41f,0.39f),0f,0.1f);
        private static Material Ceramic=>Look.Paint(new Color(0.85f,0.83f,0.78f),0f,0.7f);

        // A pole 4 m tall with a crossarm and two insulators, wires meet at 3.8 m.
        internal static void WoodPole(Transform root)
        {
            var b=new Look.Builder(root);
            b.Tube("post",new Vector3(0,1.95f,0),0.18f,3.9f,Wood,'y');
            b.Box("arm",new Vector3(0,3.62f,0),new Vector3(1.0f,0.1f,0.1f),DarkWood);
            b.Box("brace1",new Vector3(-0.22f,3.42f,0),new Vector3(0.05f,0.45f,0.05f),DarkWood,new Vector3(0,0,-45));
            b.Box("brace2",new Vector3(0.22f,3.42f,0),new Vector3(0.05f,0.45f,0.05f),DarkWood,new Vector3(0,0,45));
            foreach(float x in new[]{-0.4f,0f,0.4f})
            {
                b.Tube("insulator",new Vector3(x,3.74f,0),0.08f,0.14f,Ceramic,'y');
                b.Tube("cap",new Vector3(x,3.82f,0),0.05f,0.04f,Copper,'y');
            }
            b.Box("foot",new Vector3(0,0.1f,0),new Vector3(0.36f,0.2f,0.36f),Stone);
        }

        // An iron lattice tower 7.5 m tall.
        internal static void IronPole(Transform root)
        {
            var b=new Look.Builder(root);
            float h=7f;
            foreach(var (x,z) in new[]{(-1,-1),(1,-1),(1,1),(-1,1)})
            {
                // legs lean in from a 1.2 m base to a 0.4 m top
                Vector3 bottom=new Vector3(x*0.6f,0,z*0.6f),top=new Vector3(x*0.2f,h,z*0.2f);
                GameObject leg=b.Box("leg",(bottom+top)/2,new Vector3(0.07f,Vector3.Distance(bottom,top),0.07f),Iron);
                leg.transform.localRotation=Quaternion.FromToRotation(Vector3.up,top-bottom);
            }
            for(int i=1;i<=6;i++)
            {
                float y=i*h/7f,w=Mathf.Lerp(1.2f,0.4f,y/h);
                b.Box("ring_a",new Vector3(0,y,w/2),new Vector3(w,0.05f,0.05f),Iron);
                b.Box("ring_b",new Vector3(0,y,-w/2),new Vector3(w,0.05f,0.05f),Iron);
                b.Box("ring_c",new Vector3(w/2,y,0),new Vector3(0.05f,0.05f,w),Iron);
                b.Box("ring_d",new Vector3(-w/2,y,0),new Vector3(0.05f,0.05f,w),Iron);
            }
            b.Box("arm",new Vector3(0,h+0.05f,0),new Vector3(1.8f,0.12f,0.12f),Iron);
            foreach(float x in new[]{-0.8f,-0.3f,0.3f,0.8f})b.Tube("insulator",new Vector3(x,h+0.18f,0),0.1f,0.2f,Ceramic,'y');
            b.Box("footing",new Vector3(0,0.08f,0),new Vector3(1.5f,0.16f,1.5f),Stone);
        }

        // A boiler lying on a firebox with a chimney, and a one-cylinder engine turning a big flywheel. 3.4 m × 2 m.
        internal static void SteamEngine(Transform root)
        {
            var b=new Look.Builder(root);
            b.Box("plinth",new Vector3(0,0.12f,0),new Vector3(3.4f,0.24f,2.0f),Stone);
            b.Box("firebox",new Vector3(-0.7f,0.6f,0),new Vector3(1.8f,0.72f,1.3f),Rust);
            b.Box("firedoor",new Vector3(-1.61f,0.55f,0),new Vector3(0.04f,0.36f,0.5f),Look.Paint(new Color(1f,0.45f,0.1f),0f,0.3f,2.5f));
            b.Tube("boiler",new Vector3(-0.7f,1.45f,0),1.2f,2.0f,Iron,'x');
            foreach(float x in new[]{-1.5f,-0.7f,0.1f})b.Tube("band",new Vector3(x,1.45f,0),1.24f,0.08f,Brass,'x');
            b.Tube("chimney",new Vector3(-1.3f,2.55f,0),0.32f,1.4f,Iron,'y');
            b.Tube("chimneycap",new Vector3(-1.3f,3.28f,0),0.42f,0.08f,Rust,'y');
            b.Tube("dome",new Vector3(-0.3f,2.1f,0),0.36f,0.3f,Brass,'y');
            b.Tube("steampipe",new Vector3(0.3f,1.85f,0),0.12f,1.0f,Copper,'x');
            b.Box("cylinder",new Vector3(0.9f,1.0f,0),new Vector3(0.7f,0.5f,0.5f),Iron);
            b.Box("frame",new Vector3(1.2f,0.55f,0.2f),new Vector3(1.2f,0.5f,0.2f),Rust);
            var wheel=b.Group("flywheel",new Vector3(1.35f,1.05f,0.7f),new Vector3(90,0,0));
            Look.Shape(PrimitiveType.Cylinder,wheel.Root,"rim",Vector3.zero,new Vector3(1.6f,0.06f,1.6f),Iron);
            Look.Shape(PrimitiveType.Cylinder,wheel.Root,"hub",Vector3.zero,new Vector3(0.3f,0.12f,0.3f),Brass);
            for(int i=0;i<3;i++)wheel.Box("spoke",Vector3.zero,new Vector3(1.5f,0.05f,0.1f),Rust,new Vector3(0,i*60,0));
            b.Tube("axle",new Vector3(1.35f,1.05f,0.45f),0.1f,0.6f,Iron,'z');
            Puffs(b.Point("steam",new Vector3(-1.3f,3.35f,0)),new Color(0.9f,0.9f,0.92f,0.45f),0.7f,7f);
            Puffs(b.Point("smoke",new Vector3(0.35f,1.95f,0.15f)),new Color(1f,1f,1f,0.35f),0.35f,4f);
        }

        // A caged glass drum around a burning Surtling core, on an iron base. 2 m × 2 m.
        internal static void Surtling(Transform root)
        {
            var b=new Look.Builder(root);
            b.Box("base",new Vector3(0,0.2f,0),new Vector3(2f,0.4f,2f),Iron);
            b.Box("lid",new Vector3(0,2.15f,0),new Vector3(1.6f,0.2f,1.6f),Iron);
            b.Tube("glass",new Vector3(0,1.27f,0),1.2f,1.55f,Look.Glow(new Color(1f,0.5f,0.1f),0.18f),'y');
            b.Ball("core",new Vector3(0,1.27f,0),Vector3.one*0.55f,Look.Unlit(new Color(1f,0.55f,0.15f)));
            for(int i=0;i<8;i++)
            {
                float a=i*Mathf.PI/4f;
                b.Box("bar",new Vector3(Mathf.Cos(a)*0.7f,1.27f,Mathf.Sin(a)*0.7f),new Vector3(0.06f,1.7f,0.06f),Rust);
            }
            Look.AddLight(b.Point("light",new Vector3(0,1.3f,0)),new Color(1f,0.5f,0.15f),7f,0f);
            Puffs(b.Point("smoke",new Vector3(0,2.3f,0)),new Color(0.35f,0.3f,0.28f,0.4f),0.4f,3f);
        }

        // A domed reactor between four pillars, eitr glowing violet in its heart. 3 m × 3 m.
        internal static void Reactor(Transform root)
        {
            var b=new Look.Builder(root);
            Material glow=Look.Unlit(new Color(0.62f,0.42f,1f));
            b.Box("base",new Vector3(0,0.25f,0),new Vector3(3f,0.5f,3f),Look.Paint(new Color(0.12f,0.12f,0.15f),0.6f,0.5f));
            b.Tube("drum",new Vector3(0,1.2f,0),1.8f,1.4f,Iron,'y');
            b.Ball("dome",new Vector3(0,1.9f,0),new Vector3(1.8f,1.2f,1.8f),Iron);
            b.Tube("window",new Vector3(0,1.2f,0),1.84f,0.35f,glow,'y');
            b.Ball("heart",new Vector3(0,2.45f,0),Vector3.one*0.5f,glow);
            foreach(var (x,z) in new[]{(-1,-1),(1,-1),(1,1),(-1,1)})
            {
                b.Box("pillar",new Vector3(x*1.25f,1.3f,z*1.25f),new Vector3(0.3f,2.1f,0.3f),Look.Paint(new Color(0.18f,0.18f,0.22f),0.5f,0.4f));
                b.Ball("node",new Vector3(x*1.25f,2.45f,z*1.25f),Vector3.one*0.25f,glow);
            }
            Look.AddLight(b.Point("light",new Vector3(0,2.6f,0)),new Color(0.6f,0.4f,1f),10f,0f);
            Puffs(b.Point("steam",new Vector3(0,2.7f,0)),new Color(0.7f,0.55f,1f,0.3f),0.5f,4f);
        }

        // A tall iron cabinet with copper terminals and a glowing charge gauge on its front. 1.6 m × 1.2 m.
        internal static void Accumulator(Transform root)
        {
            var b=new Look.Builder(root);
            b.Box("body",new Vector3(0,0.9f,0),new Vector3(1.6f,1.8f,1.2f),Look.Paint(new Color(0.16f,0.2f,0.26f),0.5f,0.45f));
            b.Box("trim",new Vector3(0,1.82f,0),new Vector3(1.66f,0.06f,1.26f),Brass);
            foreach(float x in new[]{-0.45f,0.45f})
            {
                b.Tube("terminal",new Vector3(x,1.95f,0),0.16f,0.24f,Copper,'y');
                b.Tube("insulator",new Vector3(x,1.88f,0),0.24f,0.08f,Ceramic,'y');
            }
            b.Box("gaugeback",new Vector3(0,0.9f,0.605f),new Vector3(0.3f,1.4f,0.02f),Look.Paint(Color.black,0f,0.2f));
            // (the gauge grows upward from its bottom: its pivot is there, and the bar's scale.y is the charge)
            var gauge=b.Group("gauge",new Vector3(0,0.22f,0.62f));
            gauge.Box("bar",new Vector3(0,0.67f,0),new Vector3(0.22f,1.34f,0.02f),Look.Unlit(new Color(0.3f,0.85f,1f)));
        }

        // Puffs of steam or smoke rising: a particle system, off until its generator runs (emission switched by PowerSource).
        private static void Puffs(Transform at,Color color,float size,float rate)
        {
            at.localRotation=Quaternion.Euler(-90,0,0);   // (a cone emits along its +z: up)
            ParticleSystem ps=at.gameObject.AddComponent<ParticleSystem>();
            ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;
            main.playOnAwake=false;main.loop=true;main.startLifetime=2.4f;main.startSpeed=1.1f;main.startSize=size;
            main.startColor=color;main.simulationSpace=ParticleSystemSimulationSpace.World;main.maxParticles=48;main.gravityModifier=-0.04f;
            var em=ps.emission;em.rateOverTime=rate;em.enabled=false;
            var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Cone;shape.angle=14f;shape.radius=0.08f;
            var grow=ps.sizeOverLifetime;grow.enabled=true;grow.size=new ParticleSystem.MinMaxCurve(1f,AnimationCurve.Linear(0f,0.5f,1f,2.4f));
            var fade=ps.colorOverLifetime;fade.enabled=true;
            var g=new Gradient();
            g.SetKeys(new[]{new GradientColorKey(Color.white,0f),new GradientColorKey(Color.white,1f)},
                new[]{new GradientAlphaKey(0f,0f),new GradientAlphaKey(1f,0.15f),new GradientAlphaKey(0f,1f)});
            fade.color=g;
            var r=at.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial=SmokeMaterial()??Look.Glow(new Color(color.r,color.g,color.b),0.25f);
            r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        // The game's own smoke (the smelter's chimney), so puffs look soft rather than square.
        private static Material SmokeMaterial()
        {
            GameObject smelter=ZNetScene.instance!=null?ZNetScene.instance.GetPrefab("smelter"):null;
            if(smelter==null)return null;
            var rs=smelter.GetComponentsInChildren<ParticleSystemRenderer>(true);
            return rs.Select(x=>x.sharedMaterial).FirstOrDefault(m=>m!=null&&m.name.ToLowerInvariant().Contains("smoke"))??rs.Select(x=>x.sharedMaterial).FirstOrDefault(m=>m!=null);
        }
    }
}
