using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MeadowGolf
{
    // No native gameplay scripts, renderers, audio, or ZNetViews enter this private physics scene.
    // Actual nearby surface colliders are copied at a bounded rate, then a silent ball is
    // simulated with the same launch, friction, drag, spin, and settling rules as the live one.
    internal sealed class ShotPreview:IDisposable
    {
        private readonly Collider[] _near=new Collider[128];
        private readonly Dictionary<int,GameObject> _surfaces=new Dictionary<int,GameObject>();
        private readonly HashSet<int> _seen=new HashSet<int>(),_visited=new HashSet<int>();
        private readonly List<int> _removed=new List<int>();
        private Vector3[] _points=new Vector3[202],_working=new Vector3[202];
        private IEnumerator _job;private readonly Stopwatch _slice=new Stopwatch();
        private Vector3 _jobOrigin,_jobDirection;private int _jobMode;
        private double _cpu,_maxSlice;
        private Scene _scene;private PhysicsScene _physics;private Rigidbody _body;
        private Vector3 _origin,_direction;
        private float _power,_nextPrediction,_nextGeometry;private int _mode=-1;
        private bool _geometryComplete;
        internal int Count {get;private set;}
        internal bool Complete {get;private set;}
        internal string Reason {get;private set;}="";
        internal float Distance {get;private set;}
        internal double Milliseconds {get;private set;}
        internal double MaximumSlice {get;private set;}
        internal bool DisplayComplete {get;private set;}
        internal float PredictedPower=>_power;
        internal int SurfaceCount=>_surfaces.Count;
        internal Vector3 End=>Count>0?_points[Count-1]:Vector3.zero;
        internal void Draw(LineRenderer line,GolfBall ball,int mode,float power,Vector3 direction)
        {
            Vector3 origin=ball.Body.position;
            if(_job!=null&&(mode!=_jobMode||(origin-_jobOrigin).sqrMagnitude>.25f||Vector3.Dot(direction,_jobDirection)<.99f))CancelJob();
            if(_job==null&&Time.unscaledTime>=_nextPrediction&&
                (Count==0||mode!=_mode||Mathf.Abs(power-_power)>.015f||(origin-_origin).sqrMagnitude>.0025f||Vector3.Dot(direction,_direction)<.99995f||Time.unscaledTime>=_nextGeometry))
            {
                _jobOrigin=origin;_jobDirection=direction;_jobMode=mode;
                _job=Run(ball,mode,power,direction,origin);_nextPrediction=Time.unscaledTime+.1f;
            }
            if(_job!=null)
            {
                _slice.Restart();
                if(!_job.MoveNext()){(_job as IDisposable)?.Dispose();_job=null;}
                _slice.Stop();
            }
            DisplayComplete=Complete&&Count>0&&mode==_mode&&Mathf.Abs(power-_power)<.2f&&(origin-_origin).sqrMagnitude<.01f&&Vector3.Dot(direction,_direction)>.999f;
            if(DisplayComplete)
            {
                line.positionCount=Count;
                for(int i=0;i<Count;i++)line.SetPosition(i,_points[i]+Vector3.up*.035f);
            }
            else {line.positionCount=2;line.SetPosition(0,origin+Vector3.up*.035f);line.SetPosition(1,origin+direction*1.5f+Vector3.up*.035f);}
        }
        internal void Pause(){CancelJob();DisplayComplete=false;}
        private void CancelJob(){(_job as IDisposable)?.Dispose();_job=null;if(_body!=null)_body.Sleep();}
        // Explicit developer diagnostics can run to completion. Gameplay always advances one
        // approximately 2 ms slice per rendered frame, including collider copying.
        internal void Predict(GolfBall ball,int mode,float power,Vector3 direction,bool force=false,Vector3? start=null)
        {
            CancelJob();_slice.Restart();var job=Run(ball,mode,power,direction,start??ball.Body.position);
            while(job.MoveNext())_slice.Restart();(job as IDisposable)?.Dispose();_slice.Stop();
        }
        private bool Budget()
        {
            if(_slice.Elapsed.TotalMilliseconds<2)return false;
            FinishSlice();return true;
        }
        private void FinishSlice()
        {double elapsed=_slice.Elapsed.TotalMilliseconds;_cpu+=elapsed;_maxSlice=Math.Max(_maxSlice,elapsed);_slice.Stop();}
        private IEnumerator Run(GolfBall ball,int mode,float power,Vector3 direction,Vector3 origin)
        {
            _cpu=_maxSlice=0;Ensure(ball);
            _seen.Clear();_visited.Clear();_geometryComplete=true;Reason="";
            foreach(var proxy in _surfaces.Values)if(proxy!=null){proxy.SetActive(false);if(Budget())yield return null;}
            _body.position=origin;_body.rotation=Quaternion.identity;ShotPhysics.Launch(_body,mode,power,direction);
            int count=1;_working[0]=origin;bool complete=false;float still=0;
            ZDO cup=ball.Data!=null?ZDOMan.instance?.GetZDO(ball.Data.GetZDOID(GolfWorld.CupKey)):null;
            float dt=Mathf.Clamp(Time.fixedDeltaTime,.01f,.05f);int maxSteps=Mathf.Min(1600,Mathf.CeilToInt(24f/dt));
            for(int i=0;i<maxSteps;i++)
            {
                // Discover colliders along the next swept sphere, rather than copying a huge
                // box containing every storey of a hall. This follows ricochets and slopes too.
                Vector3 from=_body.position,to=from+_body.linearVelocity*dt+Physics.gravity*(dt*dt*.5f);
                int found=Physics.OverlapCapsuleNonAlloc(from,to,ShotPhysics.Radius+.08f,_near,ShotPhysics.CollisionSurfaces,QueryTriggerInteraction.Ignore);
                if(found==_near.Length){_geometryComplete=false;Reason="Too many surfaces at the ball";}
                for(int j=0;j<found;j++)
                {
                    Collider source=_near[j];_near[j]=null;
                    if(source!=null&&_visited.Add(source.GetInstanceID()))CopySurface(source);
                    if(Budget())yield return null;
                }
                ShotPhysics.Roll(_body,_physics,dt,ref still);_physics.Simulate(dt);
                Vector3 pos=_body.position,delta=pos-origin;
                if((pos-_working[count-1]).sqrMagnitude>.16f&&count<_working.Length-1)_working[count++]=pos;
                if(new Vector2(delta.x,delta.z).sqrMagnitude>10000||Mathf.Abs(delta.y)>40){Reason="Shot travels beyond the preview limit";break;}
                if(cup!=null)
                {
                    Vector3 c=pos-(cup.GetPosition()+Vector3.up*ShotPhysics.Radius);
                    if(Rules.Captures(new Vector2(c.x,c.z).magnitude,c.y,_body.linearVelocity.magnitude,1)){complete=_geometryComplete;break;}
                }
                if(_body.IsSleeping()){complete=_geometryComplete;break;}
                if(Budget())yield return null;
            }
            _removed.Clear();foreach(var pair in _surfaces)if(!_seen.Contains(pair.Key))_removed.Add(pair.Key);
            foreach(int id in _removed){var old=_surfaces[id];if(old!=null)UnityEngine.Object.Destroy(old);_surfaces.Remove(id);if(Budget())yield return null;}
            _nextGeometry=Time.unscaledTime+1.25f;
            if(complete)
            {
                _working[count++]=_body.position;Vector3 d=_body.position-origin;Distance=new Vector2(d.x,d.z).magnitude;
            }
            else {count=2;_working[0]=origin;_working[1]=origin+direction*1.5f;Distance=0;}
            Vector3[] prior=_points;_points=_working;_working=prior;
            _origin=origin;_direction=direction;_power=power;_mode=mode;Complete=complete;Count=count;
            _body.Sleep();FinishSlice();Milliseconds=_cpu;MaximumSlice=_maxSlice;
        }
        private void Ensure(GolfBall ball)
        {
            if(_body!=null)return;
            _scene=SceneManager.CreateScene("MeadowGolfAim-"+Guid.NewGuid().ToString("N"),new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _physics=_scene.GetPhysicsScene();
            var go=new GameObject("Silent preview ball"){layer=LayerMask.NameToLayer("item")};SceneManager.MoveGameObjectToScene(go,_scene);go.AddComponent<GolfPreviewLifetime>();
            _body=go.AddComponent<Rigidbody>();ShotPhysics.Configure(_body);_body.interpolation=RigidbodyInterpolation.None;
            var sphere=go.AddComponent<SphereCollider>();sphere.radius=ShotPhysics.Radius;sphere.sharedMaterial=ball.GetComponent<SphereCollider>().sharedMaterial;
        }
        private void CopySurface(Collider source)
        {
                if(source==null||source.GetComponentInParent<Character>()!=null||source.GetComponentInParent<GolfMarker>()!=null||source.GetComponentInParent<GolfBall>()!=null)return;
                if(source.attachedRigidbody!=null&&!source.attachedRigidbody.isKinematic){_geometryComplete=false;Reason="Moving surface: "+source.name;}
                int id=source.GetInstanceID();_seen.Add(id);
                if(_surfaces.Count>=1024&&!_surfaces.ContainsKey(id)){_geometryComplete=false;Reason="Too many surfaces on the path";return;}
                if(_surfaces.TryGetValue(id,out var existing)&&existing!=null)
                {
                    existing.transform.SetPositionAndRotation(source.transform.position,source.transform.rotation);existing.transform.localScale=source.transform.lossyScale;
                    Collider collider=existing.GetComponent<Collider>();
                    if(source is BoxCollider oldBox&&collider is BoxCollider boxCopy){boxCopy.center=oldBox.center;boxCopy.size=oldBox.size;}
                    else if(source is SphereCollider oldSphere&&collider is SphereCollider sphereCopy){sphereCopy.center=oldSphere.center;sphereCopy.radius=oldSphere.radius;}
                    else if(source is CapsuleCollider oldCapsule&&collider is CapsuleCollider capsuleCopy){capsuleCopy.center=oldCapsule.center;capsuleCopy.radius=oldCapsule.radius;capsuleCopy.height=oldCapsule.height;capsuleCopy.direction=oldCapsule.direction;}
                    else if(source is MeshCollider oldMesh&&collider is MeshCollider meshCopy)
                    {
                        // Terrain edits can rebuild the same Mesh object in place.
                        meshCopy.sharedMesh=null;meshCopy.cookingOptions=oldMesh.cookingOptions;meshCopy.sharedMesh=oldMesh.sharedMesh;meshCopy.convex=oldMesh.convex;
                    }
                    existing.GetComponent<Collider>().sharedMaterial=source.sharedMaterial;existing.SetActive(true);return;
                }
                var go=new GameObject("Aim surface"){layer=source.gameObject.layer};SceneManager.MoveGameObjectToScene(go,_scene);
                go.transform.SetPositionAndRotation(source.transform.position,source.transform.rotation);go.transform.localScale=source.transform.lossyScale;
                Collider copy=null;
                if(source is BoxCollider box){var c=go.AddComponent<BoxCollider>();c.center=box.center;c.size=box.size;copy=c;}
                else if(source is SphereCollider sphere){var c=go.AddComponent<SphereCollider>();c.center=sphere.center;c.radius=sphere.radius;copy=c;}
                else if(source is CapsuleCollider capsule){var c=go.AddComponent<CapsuleCollider>();c.center=capsule.center;c.radius=capsule.radius;c.height=capsule.height;c.direction=capsule.direction;copy=c;}
                else if(source is MeshCollider mesh){var c=go.AddComponent<MeshCollider>();c.cookingOptions=mesh.cookingOptions;c.sharedMesh=mesh.sharedMesh;c.convex=mesh.convex;copy=c;}
                else {_geometryComplete=false;Reason="Unsupported collider: "+source.GetType().Name;UnityEngine.Object.Destroy(go);return;}
                copy.sharedMaterial=source.sharedMaterial;_surfaces[id]=go;
        }
        public void Dispose()
        {
            CancelJob();if(_scene.IsValid())SceneManager.UnloadSceneAsync(_scene);
            _body=null;_surfaces.Clear();Count=0;
        }
    }
    // Unity may stop a tool coroutine on reload without disposing its nested enumerators.
    // Abandoned diagnostic scenes therefore also have an independent owner lifetime.
    internal sealed class GolfPreviewLifetime:MonoBehaviour
    {
        private Plugin _owner;
        private void Awake(){_owner=Plugin.Instance;}
        private void Update()
        {
            if(_owner!=null)return;
            Scene scene=gameObject.scene;if(scene.IsValid()&&scene.isLoaded)SceneManager.UnloadSceneAsync(scene);
            enabled=false;
        }
    }
}
