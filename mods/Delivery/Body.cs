using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Delivery
{
    // The courier you see: the game's own player model, dressed and coloured from his seed, moved by Run and animated through the
    // player's own animator. Only you see him: he isn't saved or sent anywhere (nothing of his can be lost, and he can't be hurt).
    internal static class Body
    {
        internal static GameObject Make(Courier c,Vector3 at,Quaternion facing)
        {
            GameObject prefab=ZNetScene.instance!=null?ZNetScene.instance.GetPrefab("Player"):null;
            if(prefab==null)return null;
            var holder=new GameObject("DeliveryHolder");
            holder.SetActive(false);
            GameObject go;
            try
            {
                go=Object.Instantiate(prefab,at,facing,holder.transform);
                go.name="DeliveryCourier";
                Strip(go);
                Transform visual=go.transform.Find("Visual");
                if(visual!=null)visual.gameObject.AddComponent<Quiet>();
                bool was=ZNetView.m_forceDisableInit;
                ZNetView.m_forceDisableInit=true; // (its ZNetView removes itself instead of making a saved object)
                try{go.transform.SetParent(null,true);}
                finally{ZNetView.m_forceDisableInit=was;}
            }
            finally{Object.Destroy(holder);}
            Dress(go,c);
            Animator anim=go.GetComponentInChildren<Animator>();
            if(anim!=null){anim.applyRootMotion=false;anim.cullingMode=AnimatorCullingMode.AlwaysAnimate;}
            return go;
        }

        // Everything but the look: no physics, no colliders, no player, no saving.
        private static void Strip(GameObject go)
        {
            for(int pass=0;pass<3;pass++)
            {
                foreach(MonoBehaviour b in go.GetComponentsInChildren<MonoBehaviour>(true))
                    if(b!=null&&!(b is VisEquipment)&&!(b is ZNetView))Object.DestroyImmediate(b);
                foreach(Collider col in go.GetComponentsInChildren<Collider>(true))if(col!=null)Object.DestroyImmediate(col);
                foreach(Rigidbody rb in go.GetComponentsInChildren<Rigidbody>(true))if(rb!=null)Object.DestroyImmediate(rb);
                foreach(AudioSource a in go.GetComponentsInChildren<AudioSource>(true))if(a!=null)Object.DestroyImmediate(a);
            }
        }

        private static readonly string[] Skins={"0.95,0.8,0.68","0.82,0.62,0.48","0.62,0.44,0.32","1,0.86,0.76"};
        private static readonly string[] Hairs={"0.85,0.6,0.25","0.45,0.28,0.12","0.15,0.1,0.06","0.95,0.85,0.55","0.7,0.25,0.1"};
        private static void Dress(GameObject go,Courier c)
        {
            VisEquipment vis=go.GetComponent<VisEquipment>();
            if(vis==null)return;
            var rng=new System.Random(c.Seed);
            vis.SetModel(0);
            vis.SetSkinColor(Vec(Skins[rng.Next(Skins.Length)]));
            vis.SetHairColor(Vec(Hairs[rng.Next(Hairs.Length)]));
            vis.SetHairItem(Pick(rng,"Hair",20,"Hair1"));
            vis.SetBeardItem(Pick(rng,"Beard",20,"Beard1"));
            vis.SetChestItem(Hash(ObjectDB.instance?.GetItemPrefab("ArmorTunic1")!=null?"ArmorTunic1":"ArmorRagsChest"));
            vis.SetLegItem(Hash("ArmorLeatherLegs"));
            vis.SetShoulderItem(Hash("CapeDeerHide"),0,1);
        }
        // His tunic, dyed bright orange so he's seen coming (Hildir's plain tunic, once the game has put it on him).
        private static readonly AccessTools.FieldRef<VisEquipment,System.Collections.Generic.List<GameObject>> ChestParts=AccessTools.FieldRefAccess<VisEquipment,System.Collections.Generic.List<GameObject>>("m_chestItemInstances");
        internal static bool Dye(GameObject go)
        {
            VisEquipment vis=go!=null?go.GetComponent<VisEquipment>():null;
            var parts=vis!=null?ChestParts(vis):null;
            if(parts==null||parts.Count==0||vis.m_bodyModel==null)return false;
            // the cloth is a texture painted on the body; the fringes and trim are meshes of their own
            Material body=vis.m_bodyModel.material;
            if(body.HasProperty("_ChestTex"))
            {
                Texture2D dyed=Dyed(body.GetTexture("_ChestTex"));
                if(dyed!=null)body.SetTexture("_ChestTex",dyed);
            }
            foreach(GameObject part in parts)
            {
                if(part==null)continue;
                foreach(Renderer r in part.GetComponentsInChildren<Renderer>(true))
                    foreach(Material m in r.materials)
                        if(m.HasProperty("_Color"))m.color=new Color(1f,0.42f,0.05f);
            }
            return true;
        }
        // The tunic's texture in orange: its light and shade kept, its colour gone (made once).
        private static Texture2D _dyed;
        private static Texture _dyedFrom;
        internal static void Forget(){if(_dyed!=null)Object.Destroy(_dyed);_dyed=null;_dyedFrom=null;}
        private static Texture2D Dyed(Texture source)
        {
            if(source==null)return null;
            if(_dyed!=null&&_dyedFrom==source)return _dyed;
            RenderTexture rt=RenderTexture.GetTemporary(source.width,source.height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            RenderTexture was=RenderTexture.active;
            try
            {
                Graphics.Blit(source,rt);
                RenderTexture.active=rt;
                var t=new Texture2D(source.width,source.height,TextureFormat.RGBA32,true){name="DeliveryOrangeTunic",hideFlags=HideFlags.HideAndDontSave};
                t.ReadPixels(new Rect(0,0,source.width,source.height),0,0);
                Color32[] px=t.GetPixels32();
                for(int i=0;i<px.Length;i++)
                {
                    Color32 c=px[i];
                    float lum=(0.3f*c.r+0.59f*c.g+0.11f*c.b)/255f;
                    float v=Mathf.Clamp01(0.35f+lum*1.25f);
                    px[i]=new Color32((byte)(255*v),(byte)(255*v*0.45f),(byte)(255*v*0.08f),c.a);
                }
                t.SetPixels32(px);t.Apply(true);
                Forget();
                _dyed=t;_dyedFrom=source;
                return t;
            }
            catch(System.Exception e){Plugin.Log("Could not dye the tunic: "+e.Message);return null;}
            finally{RenderTexture.active=was;RenderTexture.ReleaseTemporary(rt);}
        }
        private static int Pick(System.Random rng,string prefix,int most,string fallback)
        {
            var have=Enumerable.Range(1,most).Select(i=>prefix+i).Where(n=>ObjectDB.instance!=null&&ObjectDB.instance.GetItemPrefab(n)!=null).ToList();
            return Hash(have.Count>0?have[rng.Next(have.Count)]:fallback);
        }
        private static int Hash(string prefab)=>ObjectDB.instance!=null&&ObjectDB.instance.GetItemPrefab(prefab)!=null?prefab.GetStableHashCode():0;
        private static Vector3 Vec(string s){float[] v=s.Split(',').Select(x=>float.Parse(x,System.Globalization.CultureInfo.InvariantCulture)).ToArray();return new Vector3(v[0],v[1],v[2]);}
    }

    // Takes the animation events the player's animator sends (footsteps, attacks): the courier's body has nothing to hear them.
    internal sealed class Quiet:MonoBehaviour
    {
        private void OnAnimatorMove(){}
        public void FootStep(AnimationEvent e){}
        public void Speed(float s){}
        public void Chain(){}
        public void Hit(){}
        public void OnAttackTrigger(){}
        public void Jump(){}
        public void Land(){}
        public void TakeOff(){}
        public void Stop(AnimationEvent e){}
        public void DodgeMortal(){}
        public void TrailOn(){}
        public void TrailOff(){}
        public void GPower(){}
        public void Die(){}
    }
}
