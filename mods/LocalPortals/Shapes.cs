using System;
using System.Collections.Generic;
using UnityEngine;
using Object=UnityEngine.Object;

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
        internal static Mesh Moulding(float inner,float outer,float z0,float z1,float bottom=0,float from=0,float to=1)
        {
            var edge=Policy.Edge(0,28,bottom);
            if(from>0||to<1)edge=Part(edge,from,to);
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

        // The part of a path between two fractions of its length (cut exactly at both ends).
        private static List<(double x,double y)> Part(List<(double x,double y)> path,float from,float to)
        {
            var length=new List<double>{0};
            for(int i=1;i<path.Count;i++)length.Add(length[i-1]+Math.Sqrt(Sq(path[i].x-path[i-1].x)+Sq(path[i].y-path[i-1].y)));
            double total=length[length.Count-1],a=from*total,b=to*total;
            (double x,double y) At(double d)
            {
                int i=1;
                while(i<path.Count-1&&length[i]<d)i++;
                double t=(d-length[i-1])/Math.Max(1e-9,length[i]-length[i-1]);
                return (path[i-1].x+(path[i].x-path[i-1].x)*t,path[i-1].y+(path[i].y-path[i-1].y)*t);
            }
            var part=new List<(double x,double y)>{At(a)};
            for(int i=0;i<path.Count;i++)if(length[i]>a&&length[i]<b)part.Add(path[i]);
            part.Add(At(b));
            return part;
        }
        private static double Sq(double v)=>v*v;

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
            var white=new Color[vertices.Count];
            for(int i=0;i<white.Length;i++)white[i]=Color.white; // (the ghost drawn while a mirror is cut away tints by vertex colour)
            mesh.colors=white;
            return mesh;
        }

        // A tube along a path (a root or a twig): `radius` at each point, round in `sides`, its bark running along it; both ends
        // closed. Each point's ring is turned to follow the path (parallel transport), so it does not twist.
        internal static Mesh Tube(IList<Vector3> path,IList<float> radius,int sides=7)
        {
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            int n=path.Count;
            Vector3 forward=(path[1]-path[0]).normalized;
            Vector3 side=Vector3.Cross(forward,Mathf.Abs(forward.y)<0.9f?Vector3.up:Vector3.right).normalized;
            float along=0;
            for(int i=0;i<n;i++)
            {
                Vector3 next=(path[Mathf.Min(i+1,n-1)]-path[Mathf.Max(i-1,0)]).normalized;
                side=(side-next*Vector3.Dot(side,next)).normalized; // carried along, kept square to the path
                Vector3 up=Vector3.Cross(next,side);
                if(i>0)along+=Vector3.Distance(path[i-1],path[i]);
                for(int k=0;k<=sides;k++)
                {
                    float a=k*Mathf.PI*2/sides;
                    vertices.Add(path[i]+(side*Mathf.Cos(a)+up*Mathf.Sin(a))*radius[i]);
                    uv.Add(new Vector2(k/(float)sides*0.35f,along/0.7f));
                }
            }
            int ring=sides+1;
            for(int i=0;i<n-1;i++)
                for(int k=0;k<sides;k++)
                {
                    int a=i*ring+k,b=a+ring;
                    Vector3 outward=vertices[a]-path[i];
                    Triangle(triangles,vertices,a,b,a+1,outward);
                    Triangle(triangles,vertices,a+1,b,b+1,outward);
                }
            foreach(int end in new[]{0,n-1})
            {
                int c=vertices.Count;
                vertices.Add(path[end]);uv.Add(new Vector2(0.5f,0.5f));
                Vector3 facing=end==0?path[0]-path[1]:path[n-1]-path[n-2];
                for(int k=0;k<sides;k++)Triangle(triangles,vertices,c,end*ring+k,end*ring+k+1,facing);
            }
            return Build("LocalPortalTube",vertices,uv,triangles);
        }

        // A crystal: a six-sided prism `length` long (along y) and `radius` round, pointed at both ends (the top point longer).
        internal static Mesh Crystal(float radius,float length,int sides=6)
        {
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            float body=length*0.5f,tip=length*0.32f,foot=length*0.18f; // straight middle, long top point, short bottom point
            float y0=-length/2,y1=y0+foot,y2=y1+body,y3=y2+tip;
            for(int k=0;k<sides;k++)
            {
                float a0=k*Mathf.PI*2/sides,a1=(k+1)*Mathf.PI*2/sides;
                Vector3 p0=new Vector3(Mathf.Cos(a0)*radius,0,Mathf.Sin(a0)*radius),p1=new Vector3(Mathf.Cos(a1)*radius,0,Mathf.Sin(a1)*radius);
                Vector3 outward=(p0+p1)/2;
                Face(vertices,uv,triangles,new[]{p0+Vector3.up*y1,p1+Vector3.up*y1,p1+Vector3.up*y2,p0+Vector3.up*y2},outward);  // the side
                Face(vertices,uv,triangles,new[]{p0+Vector3.up*y2,p1+Vector3.up*y2,Vector3.up*y3},outward+Vector3.up*radius);       // the top point
                Face(vertices,uv,triangles,new[]{p1+Vector3.up*y1,p0+Vector3.up*y1,Vector3.up*y0},outward-Vector3.up*radius);       // the bottom point
            }
            return Build("LocalPortalCrystal",vertices,uv,triangles);
        }
        private static void Face(List<Vector3> vertices,List<Vector2> uv,List<int> triangles,Vector3[] corners,Vector3 facing)
        {
            int s=vertices.Count;
            foreach(Vector3 c in corners){vertices.Add(c);uv.Add(new Vector2(c.x+c.z,c.y));}
            for(int i=1;i<corners.Length-1;i++)Triangle(triangles,vertices,s,s+i,s+i+1,facing);
        }

        // Thin raised strokes on a flat face (runes): each stroke from (x0,y0) to (x1,y1) in the face's x,y, `width` wide and
        // standing `depth` out of it at z.
        internal static Mesh Strokes(IEnumerable<(Vector2 a,Vector2 b)> strokes,float width,float z,float depth)
        {
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            foreach(var (a,b) in strokes)
            {
                Vector2 d=(b-a).normalized,n=new Vector2(-d.y,d.x)*(width/2);
                Vector2 a2=a-d*(width/2),b2=b+d*(width/2); // (square ends overlap at the joins)
                Vector3 P(Vector2 p,float zz)=>new Vector3(p.x,p.y,zz);
                Vector3[] front={P(a2-n,z+depth),P(b2-n,z+depth),P(b2+n,z+depth),P(a2+n,z+depth)};
                Face(vertices,uv,triangles,front,Vector3.forward);
                Face(vertices,uv,triangles,new[]{P(a2-n,z),P(b2-n,z),P(b2-n,z+depth),P(a2-n,z+depth)},new Vector3(-n.x,-n.y,0));
                Face(vertices,uv,triangles,new[]{P(a2+n,z),P(b2+n,z),P(b2+n,z+depth),P(a2+n,z+depth)},new Vector3(n.x,n.y,0));
                Face(vertices,uv,triangles,new[]{P(b2-n,z),P(b2+n,z),P(b2+n,z+depth),P(b2-n,z+depth)},new Vector3(d.x,d.y,0));
                Face(vertices,uv,triangles,new[]{P(a2-n,z),P(a2+n,z),P(a2+n,z+depth),P(a2-n,z+depth)},new Vector3(-d.x,-d.y,0));
            }
            return Build("LocalPortalStrokes",vertices,uv,triangles);
        }

        // ---- rough stone, for the wild looking-glass ----

        // Smooth noise along a line, about -1..1 (sines of unrelated rates), and a fixed scatter for one corner, -1..1.
        internal static float Wobble(float s,int seed)=>0.5f*Mathf.Sin(s*1.7f+seed*1.3f)+0.3f*Mathf.Sin(s*3.9f+seed*2.3f)+0.2f*Mathf.Sin(s*7.1f+seed*4.1f);
        internal static float Scatter(int i,int k,int seed)
        {
            float v=Mathf.Sin(i*12.9898f+k*78.233f+seed*37.719f)*43758.5453f;
            return (v-Mathf.Floor(v))*2-1;
        }

        // Broken rock round the edge of the glass: a lumpy, chunky ring swept up from below the ground on the left, over the arch
        // and down on the right, never reaching into the opening, thick and thin by turns, its faces flat like split stone.
        internal static Mesh RockRing(int seed,float bottom=-0.3f)
        {
            var edge=Policy.Edge(0,40,bottom);
            int m=edge.Count;
            var at=new Vector2[m];var along=new float[m];
            for(int i=0;i<m;i++){at[i]=new Vector2((float)edge[i].x,(float)edge[i].y);along[i]=i==0?0:along[i-1]+Vector2.Distance(at[i-1],at[i]);}
            float total=along[m-1];
            const int sides=7;const float step=0.15f;
            var rings=new List<Vector3[]>();
            int count=Mathf.CeilToInt(total/step);
            for(int i=0;i<=count;i++)
            {
                float d=Mathf.Min(total,i*step);
                int j=1;while(j<m-1&&along[j]<d)j++;
                float t=(d-along[j-1])/Mathf.Max(1e-5f,along[j]-along[j-1]);
                Vector2 p=Vector2.Lerp(at[j-1],at[j],t);
                Vector2 dir=(at[j]-at[j-1]).normalized,normal=new Vector2(-dir.y,dir.x); // outward (the path runs clockwise)
                float a=0.11f+0.17f*(Wobble(d*0.9f,seed)*0.5f+0.5f),b=0.12f+0.11f*(Wobble(d*1.3f,seed+5)*0.5f+0.5f);
                if(Scatter(i,99,seed)>0.55f){a*=1.7f;b*=1.25f;} // here and there a rock juts out
                float zc=-0.05f+0.05f*Wobble(d*0.7f,seed+9);
                var ring=new Vector3[sides];
                for(int k=0;k<sides;k++)
                {
                    float th=k*Mathf.PI*2/sides+0.25f*Scatter(i,k+50,seed);
                    float c=Mathf.Cos(th),sn=Mathf.Sin(th),bump=1+0.36f*Scatter(i,k,seed);
                    float o=a+a*c*bump;
                    if(c<0)o=Mathf.Max(o,0.01f+0.03f*(1+c)); // the inner side keeps clear of the glass
                    ring[k]=new Vector3(p.x+normal.x*o,p.y+normal.y*o,zc+b*sn*bump);
                }
                rings.Add(ring);
            }
            var vertices=new List<Vector3>();var triangles=new List<int>();
            for(int i=0;i<rings.Count-1;i++)
                for(int k=0;k<sides;k++)
                {
                    int k2=(k+1)%sides;
                    Vector3 mid=(rings[i][0]+rings[i][3])/2;
                    Quad(vertices,triangles,rings[i][k],rings[i+1][k],rings[i+1][k2],rings[i][k2],rings[i][k]-mid);
                }
            foreach(int e in new[]{0,rings.Count-1})
            {
                Vector3 centre=Vector3.zero;foreach(Vector3 v in rings[e])centre+=v/sides;
                for(int k=0;k<sides;k++)Tri(vertices,triangles,centre,rings[e][k],rings[e][(k+1)%sides],Vector3.down);
            }
            return Stone("LocalPortalRockRing",vertices,triangles);
        }

        // A boulder: a squashed ball of rough faces, `radius` round, every corner pushed in or out a little.
        internal static Mesh Boulder(float radius,int seed,float squash=0.72f)
        {
            const int rows=5,cols=8;
            var grid=new Vector3[rows+1,cols];
            for(int r=0;r<=rows;r++)
                for(int c=0;c<cols;c++)
                {
                    float lat=Mathf.PI*(r/(float)rows-0.5f),lon=Mathf.PI*2*(c+0.5f*(r%2))/cols;
                    float k=radius*(1+0.3f*Scatter(r,c,seed));
                    grid[r,c]=new Vector3(Mathf.Cos(lat)*Mathf.Cos(lon)*k,Mathf.Sin(lat)*k*squash,Mathf.Cos(lat)*Mathf.Sin(lon)*k);
                }
            var vertices=new List<Vector3>();var triangles=new List<int>();
            for(int r=0;r<rows;r++)
                for(int c=0;c<cols;c++)
                {
                    int c2=(c+1)%cols;
                    Vector3 a=grid[r,c],b=grid[r,c2],d=grid[r+1,c],e=grid[r+1,c2];
                    Tri(vertices,triangles,a,b,e,(a+b+e)/3);Tri(vertices,triangles,a,e,d,(a+e+d)/3);
                }
            return Stone("LocalPortalBoulder",vertices,triangles);
        }

        // A slab of rock behind the glass: the board's outline grown a little, flat in front, lumpy behind.
        internal static Mesh RockBack(int seed,float z0,float z1,float bottom)
        {
            Mesh board=Board(0.12f,z0,z1,bottom);
            var points=board.vertices;
            for(int i=0;i<points.Length;i++)
                if(points[i].z<(z0+z1)/2)points[i].z-=0.05f+0.06f*(Wobble(points[i].x*4+points[i].y*2.5f,seed)*0.5f+0.5f);
            var vertices=new List<Vector3>();var triangles=new List<int>();
            int[] t=board.triangles;
            for(int i=0;i<t.Length;i+=3){int s=vertices.Count;vertices.Add(points[t[i]]);vertices.Add(points[t[i+1]]);vertices.Add(points[t[i+2]]);triangles.Add(s);triangles.Add(s+1);triangles.Add(s+2);}
            Object.Destroy(board);
            return Stone("LocalPortalRockBack",vertices,triangles);
        }

        private static void Tri(List<Vector3> vertices,List<int> triangles,Vector3 a,Vector3 b,Vector3 c,Vector3 facing)
        {
            int s=vertices.Count;
            vertices.Add(a);vertices.Add(b);vertices.Add(c);
            Triangle(triangles,vertices,s,s+1,s+2,facing);
        }
        private static void Quad(List<Vector3> vertices,List<int> triangles,Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 facing)
        {
            Tri(vertices,triangles,a,b,c,facing);Tri(vertices,triangles,a,c,d,facing);
        }
        // Every face its own corners (flat, like split stone), its texture laid on from the side it faces most.
        private static Mesh Stone(string name,List<Vector3> vertices,List<int> triangles)
        {
            var uv=new List<Vector2>();
            for(int i=0;i<vertices.Count;i+=3)
            {
                Vector3 n=Vector3.Cross(vertices[i+1]-vertices[i],vertices[i+2]-vertices[i]);
                n=new Vector3(Mathf.Abs(n.x),Mathf.Abs(n.y),Mathf.Abs(n.z));
                for(int k=0;k<3;k++)
                {
                    Vector3 v=vertices[i+k];
                    uv.Add(n.x>=n.y&&n.x>=n.z?new Vector2(v.z,v.y):n.y>=n.z?new Vector2(v.x,v.z):new Vector2(v.x,v.y));
                }
            }
            for(int i=0;i<uv.Count;i++)uv[i]/=1.6f;
            return Build(name,vertices,uv,triangles);
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
