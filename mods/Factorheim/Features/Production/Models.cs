using System.Reflection;
using UnityEngine;

namespace Factorheim
{
    // The production pieces' and items' looks, made of simple shapes. Pieces: their middle at the ground, front (where things come out)
    // towards +z. Children named "spin*" turn round their own up axis while the machine works, "on*" show only then (Running).
    internal static class ProdModels
    {
        private static Material Iron=>Look.Paint(new Color(0.3f,0.31f,0.33f),0.7f,0.4f);
        private static Material DarkIron=>Look.Paint(new Color(0.14f,0.14f,0.15f),0.6f,0.35f);
        private static Material Bronze=>Look.Paint(new Color(0.62f,0.42f,0.2f),0.8f,0.55f);
        private static Material Copper=>Look.Paint(new Color(0.78f,0.4f,0.22f),0.85f,0.6f);
        private static Material Stone=>Look.Paint(new Color(0.42f,0.41f,0.39f),0f,0.1f);
        private static Material Wood=>Look.Paint(new Color(0.42f,0.29f,0.17f),0f,0.15f);
        private static Material Fire=>Look.Paint(new Color(1f,0.45f,0.1f),0f,0.3f,3f);
        private static Material Glow(Color c)=>Look.Paint(c,0f,0.5f,2.5f);

        // ---- pieces ----
        internal static void BurnerDrill(Transform root)
        {
            var b=new Look.Builder(root);
            b.Box("base",new Vector3(0,0.2f,0),new Vector3(1.9f,0.4f,1.9f),Stone);
            b.Box("body",new Vector3(0,0.9f,-0.15f),new Vector3(1.3f,1f,1.2f),DarkIron);
            b.Box("band",new Vector3(0,1.15f,-0.15f),new Vector3(1.34f,0.1f,1.24f),Bronze);
            b.Tube("chimney",new Vector3(-0.45f,1.75f,-0.5f),0.28f,0.9f,DarkIron,'y');
            Smoke(root,new Vector3(-0.45f,2.25f,-0.5f));
            b.Box("on fire",new Vector3(0,0.75f,0.46f),new Vector3(0.5f,0.25f,0.02f),Fire);
            b.Box("chute",new Vector3(0,0.55f,0.8f),new Vector3(0.5f,0.12f,0.6f),Bronze,new Vector3(-20,0,0));
            Flywheel(b,new Vector3(0.72f,0.95f,-0.15f),0.8f,Bronze,DarkIron);
            Bit(b,new Vector3(0,0.1f,-0.15f),0.5f,Iron);
        }
        internal static void ElectricDrill(Transform root)
        {
            var b=new Look.Builder(root);
            b.Box("base",new Vector3(0,0.15f,0),new Vector3(2.9f,0.3f,2.9f),DarkIron);
            foreach(var c in new[]{new Vector2(-1.2f,-1.2f),new Vector2(1.2f,-1.2f),new Vector2(-1.2f,1.2f),new Vector2(1.2f,1.2f)})
                b.Box("post",new Vector3(c.x,1.1f,c.y),new Vector3(0.22f,1.9f,0.22f),Iron);
            b.Box("top",new Vector3(0,2.1f,0),new Vector3(2.7f,0.3f,2.7f),Iron);
            b.Box("motor",new Vector3(0,2.55f,0),new Vector3(1.1f,0.6f,1.1f),DarkIron);
            b.Box("on light",new Vector3(0,2.9f,0),new Vector3(0.3f,0.12f,0.3f),Glow(new Color(0.3f,0.7f,1f)));
            Bit(b,new Vector3(0,0.3f,0),1.7f,Iron,0.7f);
            b.Box("chute",new Vector3(0,0.55f,1.45f),new Vector3(0.6f,0.12f,0.5f),Iron,new Vector3(-20,0,0));
        }
        internal static void Woodcutter(Transform root)
        {
            var b=new Look.Builder(root);
            b.Box("base",new Vector3(0,0.2f,0),new Vector3(1.9f,0.4f,1.9f),Wood);
            b.Box("body",new Vector3(0,0.8f,-0.3f),new Vector3(1.2f,0.8f,1f),DarkIron);
            b.Tube("chimney",new Vector3(0.4f,1.6f,-0.5f),0.24f,0.8f,DarkIron,'y');
            Smoke(root,new Vector3(0.4f,2.05f,-0.5f));
            b.Box("on fire",new Vector3(-0.2f,0.7f,0.21f),new Vector3(0.4f,0.2f,0.02f),Fire);
            b.Box("table",new Vector3(0,0.55f,0.5f),new Vector3(1.4f,0.1f,0.8f),Wood);
            // a saw blade standing on edge, along z
            var spin=b.Group("spin saw",new Vector3(0,0.95f,0.5f),new Vector3(0,0,90));
            spin.Tube("blade",Vector3.zero,0.75f,0.02f,Iron,'y');
            spin.Box("tooth",new Vector3(0,0,0.36f),new Vector3(0.06f,0.03f,0.06f),Iron);
            spin.Box("tooth",new Vector3(0,0,-0.36f),new Vector3(0.06f,0.03f,0.06f),Iron);
            b.Box("logs",new Vector3(-0.65f,0.6f,0.5f),new Vector3(0.25f,0.25f,0.8f),Wood);
        }
        internal static void Assembler(Transform root,Color body,Color glow)
        {
            var b=new Look.Builder(root);
            b.Box("base",new Vector3(0,0.15f,0),new Vector3(2.9f,0.3f,2.9f),DarkIron);
            b.Box("body",new Vector3(0,1.2f,0),new Vector3(2.6f,1.8f,2.6f),Look.Paint(body,0.5f,0.4f));
            b.Box("roof",new Vector3(0,2.15f,0),new Vector3(2.8f,0.15f,2.8f),DarkIron);
            b.Box("window",new Vector3(0,1.35f,1.31f),new Vector3(1.4f,0.8f,0.02f),Look.Paint(new Color(0.05f,0.06f,0.08f),0.2f,0.9f));
            b.Box("on window",new Vector3(0,1.35f,1.32f),new Vector3(1.4f,0.8f,0.02f),Glow(glow*0.6f));
            var spin=b.Group("spin wheel",new Vector3(0,2.45f,0),Vector3.zero);
            spin.Tube("hub",Vector3.zero,0.3f,0.35f,DarkIron,'y');
            for(int i=0;i<4;i++)spin.Box("arm",Vector3.zero,new Vector3(1.6f,0.1f,0.14f),Iron,new Vector3(0,i*45f,0));
            b.Box("in",new Vector3(0,0.6f,-1.32f),new Vector3(0.8f,0.4f,0.04f),Look.Paint(glow,0.3f,0.4f,0.4f));
            b.Box("out",new Vector3(0,0.6f,1.32f),new Vector3(0.8f,0.4f,0.04f),Look.Paint(glow,0.3f,0.4f,0.8f));
        }
        internal static void ChemPlant(Transform root)
        {
            var b=new Look.Builder(root);
            b.Box("base",new Vector3(0,0.15f,0),new Vector3(2.9f,0.3f,2.9f),DarkIron);
            b.Tube("tank",new Vector3(-0.65f,1.2f,-0.4f),1.1f,1.8f,Look.Paint(new Color(0.15f,0.13f,0.1f),0.5f,0.7f),'y');
            b.Tube("tank",new Vector3(0.65f,1.2f,-0.4f),1.1f,1.8f,Look.Paint(new Color(0.15f,0.13f,0.1f),0.5f,0.7f),'y');
            b.Box("pipe",new Vector3(0,1.6f,-0.4f),new Vector3(1.4f,0.2f,0.2f),Copper);
            b.Box("vat",new Vector3(0,0.8f,0.8f),new Vector3(2f,1f,1f),Iron);
            b.Box("on brew",new Vector3(0,1.31f,0.8f),new Vector3(1.8f,0.02f,0.8f),Glow(new Color(0.4f,1f,0.3f)*0.7f));
            var spin=b.Group("spin stirrer",new Vector3(0,1.6f,0.8f),Vector3.zero);
            spin.Tube("shaft",new Vector3(0,-0.1f,0),0.08f,0.8f,Iron,'y');
            spin.Box("paddle",new Vector3(0,0.25f,0),new Vector3(0.8f,0.06f,0.1f),Iron);
        }
        internal static void Pumpjack(Transform root)
        {
            var b=new Look.Builder(root);
            b.Box("base",new Vector3(0,0.15f,0),new Vector3(2.9f,0.3f,2.9f),DarkIron);
            b.Box("leg",new Vector3(-0.35f,1.2f,0),new Vector3(0.15f,2f,0.15f),Iron,new Vector3(0,0,-10));
            b.Box("leg",new Vector3(0.35f,1.2f,0),new Vector3(0.15f,2f,0.15f),Iron,new Vector3(0,0,10));
            b.Box("beam",new Vector3(0,2.25f,0),new Vector3(0.25f,0.25f,2.6f),Look.Paint(new Color(0.6f,0.15f,0.1f),0.4f,0.4f));
            b.Box("head",new Vector3(0,2.05f,1.3f),new Vector3(0.35f,0.7f,0.35f),Iron);
            b.Tube("well",new Vector3(0,0.6f,1.3f),0.25f,0.9f,DarkIron,'y');
            Flywheel(b,new Vector3(0.45f,0.9f,-1f),1f,DarkIron,Iron);
            b.Box("motor",new Vector3(-0.3f,0.6f,-1f),new Vector3(0.6f,0.6f,0.6f),Iron);
            b.Box("on light",new Vector3(-0.3f,0.95f,-1f),new Vector3(0.2f,0.1f,0.2f),Glow(new Color(1f,0.7f,0.2f)));
        }

        // A wheel standing on edge, turning round the x axis.
        private static void Flywheel(Look.Builder b,Vector3 at,float size,Material rim,Material spoke)
        {
            var spin=b.Group("spin flywheel",at,new Vector3(0,0,90));
            spin.Tube("rim",Vector3.zero,size,0.08f,rim,'y');
            spin.Box("spoke",Vector3.zero,new Vector3(size*0.95f,0.1f,0.08f),spoke);
            spin.Box("spoke",Vector3.zero,new Vector3(0.08f,0.1f,size*0.95f),spoke);
        }
        // A drill bit pointing down into the ground under the machine.
        private static void Bit(Look.Builder b,Vector3 at,float length,Material m,float width=0.35f)
        {
            var spin=b.Group("spin bit",at,Vector3.zero);
            for(int i=0;i<4;i++)
            {
                float f=1f-i/4f;
                spin.Box("flute",new Vector3(0,length*(i+0.5f)/4f,0),new Vector3(width*f,length/4f,width*f*0.3f),m,new Vector3(0,i*35f,0));
            }
            spin.Tube("shaft",new Vector3(0,length*0.5f,0),width*0.25f,length,m,'y');
        }

        // Smoke from a chimney top (the game's smelter smoke), while working.
        private static readonly FieldInfo[] SmokeFields=typeof(SmokeSpawner).GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
        private static void Smoke(Transform root,Vector3 at)
        {
            SmokeSpawner like=ZNetScene.instance?.GetPrefab("smelter")?.GetComponentInChildren<SmokeSpawner>(true);
            if(like==null)return;
            var go=new GameObject("smoke"){layer=root.gameObject.layer};
            go.transform.SetParent(root,false);go.transform.localPosition=at;
            SmokeSpawner s=go.AddComponent<SmokeSpawner>();
            foreach(FieldInfo f in SmokeFields)if(!f.IsNotSerialized&&(f.IsPublic||f.GetCustomAttribute<SerializeField>()!=null))f.SetValue(s,f.GetValue(like));
        }

        // ---- items (about 0.2-0.3 m across) ----
        internal static void Gear(Transform root)
        {
            var b=new Look.Builder(root);
            b.Tube("disc",Vector3.zero,0.2f,0.04f,Iron,'y');
            for(int i=0;i<8;i++)
            {
                float a=i*45f*Mathf.Deg2Rad;
                b.Box("tooth",new Vector3(Mathf.Sin(a)*0.11f,0,Mathf.Cos(a)*0.11f),new Vector3(0.04f,0.04f,0.04f),Iron,new Vector3(0,i*45f,0));
            }
            b.Tube("hole",new Vector3(0,0.002f,0),0.05f,0.042f,DarkIron,'y');
        }
        internal static void Wire(Transform root)
        {
            var b=new Look.Builder(root);
            b.Tube("spool",Vector3.zero,0.12f,0.18f,Wood,'x');
            b.Tube("coil",Vector3.zero,0.16f,0.13f,Copper,'x');
            b.Tube("rim",new Vector3(0.09f,0,0),0.2f,0.02f,Wood,'x');
            b.Tube("rim",new Vector3(-0.09f,0,0),0.2f,0.02f,Wood,'x');
        }
        internal static void Circuit(Transform root,Color board,Color trace,Color rune)
        {
            var b=new Look.Builder(root);
            b.Box("board",Vector3.zero,new Vector3(0.22f,0.02f,0.16f),Look.Paint(board,0.1f,0.3f));
            b.Box("trace",new Vector3(0,0.012f,0.04f),new Vector3(0.18f,0.004f,0.012f),Look.Paint(trace,0.9f,0.6f));
            b.Box("trace",new Vector3(0,0.012f,-0.04f),new Vector3(0.18f,0.004f,0.012f),Look.Paint(trace,0.9f,0.6f));
            b.Box("trace",new Vector3(-0.06f,0.012f,0),new Vector3(0.012f,0.004f,0.1f),Look.Paint(trace,0.9f,0.6f));
            b.Box("rune",new Vector3(0.04f,0.02f,0),new Vector3(0.05f,0.02f,0.05f),Glow(rune));
        }
        internal static void Engine(Transform root)
        {
            var b=new Look.Builder(root);
            b.Box("block",Vector3.zero,new Vector3(0.22f,0.14f,0.16f),Iron);
            b.Tube("cylinder",new Vector3(-0.05f,0.1f,0),0.07f,0.08f,DarkIron,'y');
            b.Tube("cylinder",new Vector3(0.05f,0.1f,0),0.07f,0.08f,DarkIron,'y');
            b.Tube("shaft",new Vector3(0.13f,0,0),0.04f,0.08f,Bronze,'x');
        }
        internal static void Bar(Transform root,Material m,Material speck=null)
        {
            var b=new Look.Builder(root);
            b.Box("bar",Vector3.zero,new Vector3(0.24f,0.07f,0.12f),m);
            if(speck!=null)b.Box("speck",new Vector3(0.05f,0.036f,0.02f),new Vector3(0.05f,0.004f,0.04f),speck);
        }
        internal static Material Plastic=>Look.Paint(new Color(0.06f,0.05f,0.05f),0.1f,0.95f);
        internal static Material FuelBrick=>Look.Paint(new Color(0.22f,0.2f,0.18f),0f,0.2f);
        internal static Material Ember=>Fire;
        internal static void EitrUnit(Transform root)
        {
            var b=new Look.Builder(root);
            b.Box("case",Vector3.zero,new Vector3(0.18f,0.04f,0.18f),DarkIron);
            b.Box("core",new Vector3(0,0.025f,0),new Vector3(0.1f,0.02f,0.1f),Glow(new Color(0.7f,0.3f,1f)));
            for(int i=0;i<4;i++)b.Box("pin",new Vector3(-0.075f+i*0.05f,0,0.1f),new Vector3(0.012f,0.01f,0.03f),Look.Paint(new Color(0.85f,0.75f,0.3f),0.9f,0.6f));
        }
    }
}
