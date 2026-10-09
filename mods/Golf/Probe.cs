using UnityEngine;

namespace MeadowGolf
{
    // Disposable Creative physics probe: no save/network/item components, never edits terrain.
    internal sealed class GolfProbe:MonoBehaviour
    {
        internal Rigidbody Body;private float _still;private float _expires;
        private void Awake(){_expires=Time.time+30;}
        private void Update(){if(Plugin.Instance==null||Time.time>_expires)Destroy(gameObject);}
        private void FixedUpdate(){if(Body!=null&&!Body.IsSleeping())ShotPhysics.Roll(Body,Physics.defaultPhysicsScene,Time.fixedDeltaTime,ref _still);}
        internal static GolfProbe Create(GolfBall source,Vector3? start=null)
        {
            var go=new GameObject("Disposable Golf playtest"){layer=LayerMask.NameToLayer("item")};
            go.transform.position=start??source.Body.position;
            var probe=go.AddComponent<GolfProbe>();probe.Body=go.AddComponent<Rigidbody>();ShotPhysics.Configure(probe.Body);
            var sphere=go.AddComponent<SphereCollider>();sphere.radius=ShotPhysics.Radius;sphere.sharedMaterial=source.GetComponent<SphereCollider>().sharedMaterial;
            foreach(Character c in Character.GetAllCharacters())if(c!=null)foreach(var collider in c.GetComponentsInChildren<Collider>())Physics.IgnoreCollision(sphere,collider);
            foreach(GolfMarker m in GolfMarker.Loaded)if(m!=null)foreach(var collider in m.GetComponentsInChildren<Collider>())Physics.IgnoreCollision(sphere,collider);
            foreach(GolfBall b in GolfBall.Loaded)if(b!=null)Physics.IgnoreCollision(sphere,b.GetComponent<Collider>());
            return probe;
        }
    }
    internal sealed class GolfTestLifetime:MonoBehaviour
    {
        private Plugin _owner;private float _expires;
        private void Awake(){_owner=Plugin.Instance;_expires=Time.time+30;}
        private void Update(){if(_owner==null||Time.time>_expires)Destroy(gameObject);}
    }
}
