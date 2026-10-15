using System.Collections.Generic;
using UnityEngine;

namespace WrongUniverse
{
    // Bends a player's arm so the hand reaches a point: a two-bone solve on the upper arm and forearm, after the animation has posed
    // them (Quad's Guns, Arms.cs). The animation puts the arm back by itself the next frame, so nothing needs undoing.
    internal static class Arms
    {
        // The forearm bone above a hand joint (the game's joint is the hand or a child of it), or null.
        internal static Transform Fore(Transform joint)
        {
            for(Transform t=joint;t!=null;t=t.parent)
                if(t.name.IndexOf("ForeArm",System.StringComparison.OrdinalIgnoreCase)>=0)return t;
            return null;
        }
        internal static Transform Upper(Transform joint)=>Fore(joint)?.parent;
        // The hand bone itself (the forearm's child on the way down to the joint).
        internal static Transform Hand(Transform joint)
        {
            Transform fore=Fore(joint);
            if(fore==null)return null;
            Transform t=joint;
            while(t!=null&&t.parent!=fore)t=t.parent;
            return t;
        }
        // How far the arm reaches (upper arm and forearm), or 0.
        internal static float Length(Transform joint)
        {
            Transform fore=Fore(joint),upper=fore!=null?fore.parent:null;
            return upper==null?0f:(fore.position-upper.position).magnitude+(joint.position-fore.position).magnitude;
        }

        // Moves the hand joint to the target if the arm can reach (as near as it can when it can't); pole is the way the elbow points.
        internal static bool Reach(Transform joint,Vector3 target,Vector3 pole)
        {
            Transform fore=Fore(joint),upper=fore!=null?fore.parent:null;
            if(upper==null)return false;
            Vector3 s=upper.position,e=fore.position,h=joint.position;
            float a=(e-s).magnitude,b=(h-e).magnitude;
            if(a<0.01f||b<0.01f)return false;
            Vector3 to=target-s;
            float d=Mathf.Clamp(to.magnitude,Mathf.Abs(a-b)+0.01f,(a+b)*0.999f);
            Vector3 dir=to.normalized;
            Vector3 side=Vector3.ProjectOnPlane(pole,dir);
            if(side.sqrMagnitude<0.0001f)side=Vector3.down;
            side.Normalize();
            float along=(a*a-b*b+d*d)/(2f*d);
            float out_=Mathf.Sqrt(Mathf.Max(0f,a*a-along*along));
            Vector3 elbow=s+dir*along+side*out_;
            upper.rotation=Quaternion.FromToRotation(e-s,elbow-s)*upper.rotation;   // the upper arm points at the new elbow
            fore.rotation=Quaternion.FromToRotation(joint.position-fore.position,s+dir*d-fore.position)*fore.rotation;   // the forearm at the hand's place
            return true;
        }

        // Puts the hand joint exactly here, turned this way: the wrist is solved for (the hand bone reaches the spot that leaves the
        // joint at pos once it is turned), then the hand is turned.
        internal static bool Place(Transform joint,Vector3 pos,Quaternion rot,Vector3 pole)
        {
            Transform hand=Hand(joint);
            if(hand==null)return false;
            Quaternion inv=Quaternion.Inverse(hand.rotation);
            Quaternion rel=inv*joint.rotation;
            Vector3 off=inv*(joint.position-hand.position);
            Quaternion handRot=rot*Quaternion.Inverse(rel);
            if(!Reach(hand,pos-handRot*off,pole))return false;
            hand.rotation=handRot;
            return true;
        }

        // The game's animator doesn't pose every character every frame. On a frame it skipped, the arm still holds what we bent it to
        // last frame, and bending that again lands somewhere else (the arms twitch): so each frame starts from the animation's last
        // real pose. A bone that isn't where we left it was posed by the animator: that is the new start.
        private static readonly Dictionary<Transform,Quaternion[]> Poses=new Dictionary<Transform,Quaternion[]>();   // bone -> { the animation's, ours }
        internal static void FromAnimation(Transform joint)
        {
            Transform hand=Hand(joint),fore=hand!=null?hand.parent:null,upper=fore!=null?fore.parent:null;
            if(upper==null)return;
            foreach(Transform b in new[]{upper,fore,hand})
            {
                if(!Poses.TryGetValue(b,out Quaternion[] q)){Poses[b]=new[]{b.localRotation,b.localRotation};continue;}
                if(Quaternion.Angle(b.localRotation,q[1])>0.01f)q[0]=b.localRotation;   // (the animator wrote it)
                else b.localRotation=q[0];
            }
        }
        // After bending: what we left, to tell next frame whether the animator has been since.
        internal static void Leave(Transform joint)
        {
            Transform hand=Hand(joint),fore=hand!=null?hand.parent:null,upper=fore!=null?fore.parent:null;
            if(upper==null)return;
            foreach(Transform b in new[]{upper,fore,hand})if(Poses.TryGetValue(b,out Quaternion[] q))q[1]=b.localRotation;
        }
        internal static void Prune()
        {
            if(Poses.Count<64)return;
            var gone=new List<Transform>();
            foreach(Transform t in Poses.Keys)if(t==null)gone.Add(t);
            foreach(Transform t in gone)Poses.Remove(t);
        }
        internal static void Clear()=>Poses.Clear();

        // Turns the hand at the wrist so its joint has this rotation (the fingers close round a grip the way the item's grip was tuned).
        internal static void Turn(Transform joint,Quaternion want)
        {
            Transform hand=Hand(joint);
            if(hand==null)return;
            hand.rotation=want*Quaternion.Inverse(joint.rotation)*hand.rotation;
        }
    }
}
