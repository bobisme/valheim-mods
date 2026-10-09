using UnityEngine;

namespace MeadowGolf
{
    // Both the live ball and the isolated aim simulation use these exact rules.
    internal static class ShotPhysics
    {
        internal static int SoundsCreated;internal static ZSFX LastSound;internal static string LastSoundName="";
        internal const float Radius=.12f;
        internal static readonly int Surfaces=LayerMask.GetMask("terrain","piece","static_solid","Default","Default_small");
        internal static readonly int CollisionSurfaces=CollisionMask();
        private static int CollisionMask()
        {
            int mask=0,item=LayerMask.NameToLayer("item");
            for(int i=0;i<32;i++)if(!Physics.GetIgnoreLayerCollision(item,i))mask|=1<<i;
            return mask;
        }
        internal static void Configure(Rigidbody body)
        {
            body.mass=.12f;body.linearDamping=.16f;body.angularDamping=.35f;
            // Unity's default of 7 rad/s makes even a gentle putt skid instead of roll.
            body.maxAngularVelocity=240f;
            body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            body.interpolation=RigidbodyInterpolation.Interpolate;
        }
        internal static void Launch(Rigidbody body,int mode,float power,Vector3 direction)
        {
            float angle=(float)Rules.Loft(mode)*Mathf.Deg2Rad;
            Vector3 velocity=(direction*Mathf.Cos(angle)+Vector3.up*Mathf.Sin(angle))*(float)Rules.Speed(mode,power);
            body.WakeUp();body.linearVelocity=velocity;
            body.angularVelocity=Vector3.Cross(Vector3.up,direction)*(mode==2?velocity.magnitude/Radius:8f);
        }
        internal static void Roll(Rigidbody body,PhysicsScene scene,float dt,ref float stillTime)
        {
            if(body.linearVelocity.sqrMagnitude<.09f)stillTime+=dt;else stillTime=0;
            if(scene.SphereCast(body.position+Vector3.up*.025f,.105f,Vector3.down,out RaycastHit ground,.10f,Surfaces,QueryTriggerInteraction.Ignore)&&ground.normal.y>.2f)
            {
                Vector3 tangent=Vector3.ProjectOnPlane(body.linearVelocity,ground.normal);
                body.linearVelocity-=tangent-Vector3.MoveTowards(tangent,Vector3.zero,.65f*dt);
                if(stillTime>.65f&&ground.normal.y>.97f)
                {body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;body.Sleep();}
            }
        }
        internal static void Sound(string name,Vector3 position,float volume=1f,float pitch=1f)
        {
            GameObject prefab=ZNetScene.instance?.GetPrefab(name);
            if(prefab!=null)
            {var sound=Object.Instantiate(prefab,position,Quaternion.identity).GetComponent<ZSFX>();sound?.SetVolumeModifier(volume);sound?.SetPitchModifier(pitch);
                SoundsCreated++;LastSound=sound;LastSoundName=name;}
        }
    }
}
