using UnityEngine;

namespace Factorheim
{
    // The logistics pieces' looks, from primitives: a belt is a rubber bed between two painted rails with an arrow on it (bronze, iron,
    // eitr that glows); an inserter is a post on a round foot with an arm that swings over the top (named "arm", its end "claw": the
    // Inserter turns them).
    internal static class LogisticsModels
    {
        internal static readonly Color Bronze=new Color(0.72f,0.45f,0.2f),Iron=new Color(0.42f,0.45f,0.5f),Eitr=new Color(0.32f,0.2f,0.62f),
            EitrGlow=new Color(0.55f,0.4f,1f),Rubber=new Color(0.07f,0.07f,0.075f),Dark=new Color(0.15f,0.15f,0.16f);

        internal static void Belt(Transform root,Color rail,Color arrow,float glow)
        {
            var b=new Look.Builder(root);
            Material r=Look.Paint(rail,0.6f,0.4f),bed=Look.Paint(Rubber,0.05f,0.2f),a=Look.Paint(arrow,0.3f,0.5f,glow),dark=Look.Paint(Dark,0.5f,0.3f);
            b.Box("foot",new Vector3(0,0.03f,0),new Vector3(0.9f,0.06f,0.96f),dark);
            b.Box("bed",new Vector3(0,0.1f,0),new Vector3(0.88f,0.04f,1f),bed);
            b.Box("rail L",new Vector3(-0.47f,0.07f,0),new Vector3(0.06f,0.14f,1f),r);
            b.Box("rail R",new Vector3(0.47f,0.07f,0),new Vector3(0.06f,0.14f,1f),r);
            Arrow(b,a,0f,0.123f);
        }
        private static void Arrow(Look.Builder b,Material m,float x,float y)
        {
            b.Box("arrow L",new Vector3(x-0.07f,y,-0.02f),new Vector3(0.04f,0.006f,0.24f),m,new Vector3(0,45,0));
            b.Box("arrow R",new Vector3(x+0.07f,y,-0.02f),new Vector3(0.04f,0.006f,0.24f),m,new Vector3(0,-45,0));
        }

        internal static void Splitter(Transform root)
        {
            var b=new Look.Builder(root);
            Material r=Look.Paint(Iron,0.6f,0.4f),bed=Look.Paint(Rubber,0.05f,0.2f),a=Look.Paint(Look.Hazard,0.3f,0.5f),dark=Look.Paint(Dark,0.5f,0.3f);
            b.Box("foot",new Vector3(0,0.03f,0),new Vector3(1.9f,0.06f,0.96f),dark);
            foreach(float x in new[]{-0.5f,0.5f})
            {
                b.Box("bed",new Vector3(x,0.1f,0),new Vector3(0.88f,0.04f,1f),bed);
                Arrow(b,a,x,0.123f);
            }
            b.Box("rail L",new Vector3(-0.97f,0.07f,0),new Vector3(0.06f,0.14f,1f),r);
            b.Box("rail R",new Vector3(0.97f,0.07f,0),new Vector3(0.06f,0.14f,1f),r);
            b.Box("divider",new Vector3(0,0.1f,0),new Vector3(0.06f,0.2f,1f),r);
            b.Box("gearbox",new Vector3(0,0.3f,0),new Vector3(0.36f,0.26f,0.3f),Look.Paint(Look.Hazard,0.4f,0.4f));
            b.Tube("shaft",new Vector3(0,0.3f,0),0.12f,0.5f,dark,'x');
        }

        internal static void Inserter(Transform root,Color arm,Color foot,float reach,float glow=0f)
        {
            var b=new Look.Builder(root);
            Material am=Look.Paint(arm,0.4f,0.5f,glow),fm=Look.Paint(foot,0.6f,0.35f),dark=Look.Paint(Dark,0.5f,0.3f);
            b.Tube("foot",new Vector3(0,0.06f,0),0.5f,0.12f,fm,'y');
            b.Tube("post",new Vector3(0,0.26f,0),0.1f,0.34f,dark,'y');
            b.Ball("joint",new Vector3(0,0.43f,0),Vector3.one*0.14f,am);
            Look.Builder a=b.Group("arm",new Vector3(0,0.43f,0));
            a.Box("beam",new Vector3(0,0,-reach*0.5f),new Vector3(0.06f,0.06f,reach),am);
            Look.Builder claw=a.Group("claw",new Vector3(0,0,-reach));
            claw.Box("wrist",new Vector3(0,-0.03f,0),new Vector3(0.08f,0.1f,0.08f),dark);
            claw.Box("finger L",new Vector3(-0.05f,-0.1f,0),new Vector3(0.025f,0.1f,0.06f),dark);
            claw.Box("finger R",new Vector3(0.05f,-0.1f,0),new Vector3(0.025f,0.1f,0.06f),dark);
            // what you hit and stand on: the foot and post only (the arm reaches over its neighbours)
            var hit=new GameObject("hit");hit.transform.SetParent(root,false);
            var box=hit.AddComponent<BoxCollider>();box.center=new Vector3(0,0.25f,0);box.size=new Vector3(0.5f,0.5f,0.5f);
        }
    }
}
