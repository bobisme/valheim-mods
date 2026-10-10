using System.Collections.Generic;
using UnityEngine;

namespace LocalPortals
{
    // The portal's meshes, made in code: the flat elliptical surface and the tube of the ring around it.
    internal static class Shapes
    {
        // The glass, facing +z: the arched opening. Its texture covers the opening's bounding box, with u running to the right
        // of someone looking at the front (the portal's -x), which is how the view behind it is drawn.
        internal static Mesh Surface()
        {
            float hw=(float)Policy.HalfWidth,y0=(float)Policy.Bottom,y1=(float)Policy.Top;
            var vertices=new List<Vector3>{new Vector3(0,(float)((Policy.Bottom+Policy.Spring)/2),0)};
            foreach(var (x,y) in Policy.Edge(0,40))vertices.Add(new Vector3((float)x,(float)y,0));
            var uv=new List<Vector2>();
            foreach(Vector3 v in vertices)uv.Add(new Vector2((hw-v.x)/(2*hw),(v.y-y0)/(y1-y0)));
            var triangles=new List<int>();
            int n=vertices.Count-1;
            for(int i=0;i<n;i++)Triangle(triangles,vertices,0,1+i,1+(i+1)%n,Vector3.forward); // (the path's ends meet along the bottom)
            Mesh mesh=Build("LocalPortalSurface",vertices,uv,triangles);
            var white=new Color[vertices.Count];
            for(int i=0;i<white.Length;i++)white[i]=Color.white; // particle shaders tint by vertex colour
            mesh.colors=white;
            return mesh;
        }

        // A moulding swept along the edge of the glass: between `inner` and `outer` metres out from the edge, from z0 back to
        // z1 front, closed at both ends on the ground. The wood's grain runs along it.
        internal static Mesh Moulding(float inner,float outer,float z0,float z1,float bottom=0)
        {
            var edge=Policy.Edge(0,28,bottom);
            int n=edge.Count;
            var at=new Vector2[n];var normal=new Vector2[n];var along=new float[n];
            for(int i=0;i<n;i++)at[i]=new Vector2((float)edge[i].x,(float)edge[i].y);
            for(int i=0;i<n;i++)
            {
                Vector2 d=(at[Mathf.Min(i+1,n-1)]-at[Mathf.Max(i-1,0)]).normalized;
                normal[i]=new Vector2(-d.y,d.x); // outward: the path runs clockwise
                along[i]=i==0?0:along[i-1]+Vector2.Distance(at[i-1],at[i]);
            }
            // offsets follow the ellipse's normal; on the straight sides that is exactly sideways
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            Vector3 P(int i,float off,float z)=>new Vector3(at[i].x+normal[i].x*off,at[i].y+normal[i].y*off,z);
            void Strip(float offA,float zA,float offB,float zB,Vector3 facing,bool byOffset)
            {
                int start=vertices.Count;
                for(int i=0;i<n;i++)
                {
                    vertices.Add(P(i,offA,zA));vertices.Add(P(i,offB,zB));
                    float across=byOffset?Mathf.Abs(offB-offA):Mathf.Abs(zB-zA);
                    uv.Add(new Vector2(along[i]/1.2f,0));uv.Add(new Vector2(along[i]/1.2f,across/1.2f));
                }
                for(int i=0;i<n-1;i++)
                {
                    int a=start+i*2,b=a+2;
                    Vector3 face=facing; // front and back face along z; Vector3.right marks the outer side, left the inner
                    if(facing==Vector3.right)face=(Vector3)normal[i];
                    else if(facing==Vector3.left)face=-(Vector3)normal[i];
                    Triangle(triangles,vertices,a,b,a+1,face);
                    Triangle(triangles,vertices,a+1,b,b+1,face);
                }
            }
            Strip(inner,z1,outer,z1,Vector3.forward,true);   // front
            Strip(inner,z0,outer,z0,Vector3.back,true);      // back
            Strip(outer,z0,outer,z1,Vector3.right,false);    // outer side
            Strip(inner,z0,inner,z1,Vector3.left,false);     // inner side
            foreach(int i in new[]{0,n-1})                    // the feet of the moulding
            {
                int s=vertices.Count;
                vertices.Add(P(i,inner,z0));vertices.Add(P(i,outer,z0));vertices.Add(P(i,inner,z1));vertices.Add(P(i,outer,z1));
                uv.Add(new Vector2(0,0));uv.Add(new Vector2(0.2f,0));uv.Add(new Vector2(0,0.2f));uv.Add(new Vector2(0.2f,0.2f));
                Triangle(triangles,vertices,s,s+1,s+3,Vector3.down);Triangle(triangles,vertices,s,s+3,s+2,Vector3.down);
            }
            return Build("LocalPortalMoulding",vertices,uv,triangles);
        }

        // A flat board the shape of the glass grown by `grow`, from z0 back to z1 front.
        internal static Mesh Board(float grow,float z0,float z1,float bottom)
        {
            var edge=Policy.Edge(grow,28,bottom);
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            float cy=(float)((bottom+Policy.Spring)/2);
            foreach(float z in new[]{z1,z0})
            {
                int c=vertices.Count;
                vertices.Add(new Vector3(0,cy,z));uv.Add(new Vector2(0.5f,cy/1.2f));
                foreach(var (x,y) in edge){vertices.Add(new Vector3((float)x,(float)y,z));uv.Add(new Vector2((float)x/1.2f,(float)y/1.2f));}
                int m=edge.Count;
                for(int i=0;i<m;i++)Triangle(triangles,vertices,c,c+1+i,c+1+(i+1)%m,z==z1?Vector3.forward:Vector3.back);
            }
            int ring=edge.Count;
            for(int i=0;i<ring;i++)
            {
                int j=(i+1)%ring,s=vertices.Count;
                Vector3 a0=new Vector3((float)edge[i].x,(float)edge[i].y,z0),b0=new Vector3((float)edge[j].x,(float)edge[j].y,z0);
                vertices.Add(a0);vertices.Add(b0);vertices.Add(new Vector3(a0.x,a0.y,z1));vertices.Add(new Vector3(b0.x,b0.y,z1));
                float len=Vector3.Distance(a0,b0)/1.2f;
                uv.Add(new Vector2(0,0));uv.Add(new Vector2(len,0));uv.Add(new Vector2(0,0.05f));uv.Add(new Vector2(len,0.05f));
                Vector3 mid=(a0+b0)/2,outward=new Vector3(mid.x,mid.y-cy,0);
                Triangle(triangles,vertices,s,s+1,s+3,outward);Triangle(triangles,vertices,s,s+3,s+2,outward);
            }
            return Build("LocalPortalBoard",vertices,uv,triangles);
        }

        // A box of the given size centred on the origin, its texture laid on at one repeat per uvMetres on every face (no stretching).
        internal static Mesh Box(Vector3 size,float uvMetres=1f)
        {
            var vertices=new List<Vector3>();
            var uv=new List<Vector2>();
            var triangles=new List<int>();
            Vector3 h=size/2;
            foreach(Vector3 n in new[]{Vector3.right,Vector3.left,Vector3.up,Vector3.down,Vector3.forward,Vector3.back})
            {
                // two axes across the face, the longer one along u so wood grain runs along the piece
                Vector3 u=Mathf.Abs(n.y)>0.5f?Vector3.right:Vector3.up,v=Vector3.Cross(n,u);
                float su=Mathf.Abs(Vector3.Dot(size,u)),sv=Mathf.Abs(Vector3.Dot(size,v));
                if(sv>su){Vector3 t=u;u=v;v=t;float ts=su;su=sv;sv=ts;}
                Vector3 centre=Vector3.Scale(n,h);
                int i=vertices.Count;
                for(int k=0;k<4;k++)
                {
                    float du=(k&1)==0?-0.5f:0.5f,dv=(k&2)==0?-0.5f:0.5f;
                    vertices.Add(centre+u*du*su+v*dv*sv);
                    uv.Add(new Vector2((du+0.5f)*su/uvMetres,(dv+0.5f)*sv/uvMetres));
                }
                Triangle(triangles,vertices,i,i+1,i+3,n);
                Triangle(triangles,vertices,i,i+3,i+2,n);
            }
            return Build("LocalPortalBox",vertices,uv,triangles);
        }

        // Adds a triangle wound so its front faces the given way (Unity draws clockwise-seen faces).
        private static void Triangle(List<int> triangles,List<Vector3> v,int i0,int i1,int i2,Vector3 facing)
        {
            Vector3 normal=Vector3.Cross(v[i1]-v[i0],v[i2]-v[i0]);
            if(Vector3.Dot(normal,facing)>=0){triangles.Add(i0);triangles.Add(i1);triangles.Add(i2);}
            else{triangles.Add(i0);triangles.Add(i2);triangles.Add(i1);}
        }
        private static Mesh Build(string name,List<Vector3> vertices,List<Vector2> uv,List<int> triangles)
        {
            var mesh=new Mesh{name=name};
            mesh.SetVertices(vertices);
            mesh.SetUVs(0,uv);
            mesh.SetTriangles(triangles,0);
            mesh.RecalculateNormals(); // (box faces have their own corners, so edges stay sharp)
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        // A soft glowing film for a portal with nothing to show: brighter towards the rim.
        internal static Texture2D Film(Color colour)
        {
            const int size=128;
            var texture=new Texture2D(size,size,TextureFormat.RGBA32,false){name="LocalPortalFilm",wrapMode=TextureWrapMode.Clamp};
            var pixels=new Color[size*size];
            for(int y=0;y<size;y++)
                for(int x=0;x<size;x++)
                {
                    float u=x/(size-1f)*2-1,v=y/(size-1f)*2-1;
                    float r=Mathf.Clamp01(Mathf.Sqrt(u*u+v*v));
                    float swirl=0.5f+0.5f*Mathf.Sin(Mathf.Atan2(v,u)*3+r*9);
                    float glow=Mathf.Lerp(0.12f,0.9f,r*r)*(0.8f+0.2f*swirl);
                    pixels[y*size+x]=new Color(colour.r*glow,colour.g*glow,colour.b*glow,1);
                }
            texture.SetPixels(pixels);
            texture.Apply(false,true);
            return texture;
        }
    }
}
