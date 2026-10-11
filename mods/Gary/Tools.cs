using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Gary
{
    // A "gary" command for Claude Tools, found while the game runs (no reference): where Gary is, and the shape of his head for the crown.
    internal static class Tools
    {
        private const string ClaudeToolsGuid="com.quad.claudetools";
        private const string OldClaudeToolsGuid="com.dhack.claudetools"; // (Claude Tools before 1.2.2)
        private static BaseUnityPlugin _tools;
        private static float _nextCheck;

        internal static void Tick()
        {
            if(Time.unscaledTime<_nextCheck)return;
            _nextCheck=Time.unscaledTime+5;
            BaseUnityPlugin found=BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(ClaudeToolsGuid,out PluginInfo info)||BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(OldClaudeToolsGuid,out info)?info.Instance:null;
            if(found==_tools)return;
            _tools=found;
            if(found==null)return;
            try
            {
                found.GetType().GetMethod("RegisterCommand",BindingFlags.Public|BindingFlags.Static)?.Invoke(null,new object[]{Plugin.Name,"gary",
                    "gary info | shot [yaw=0] [pitch=10] [dist=1.6] [up=0.1] [plain]: where the Garys near the player are (position, facing, size); shot pictures the nearest Gary alone (nothing else drawn), from around his head (yaw 0: his face), saved to claude/shots | crown [x y z] [tiltX tiltY tiltZ] [size]: move the crown (metres from the head bone at rest, in Gary's frame) to try a fit",
                    (Func<string[],Action<JObject>,Action<string>,IEnumerator>)Run});
            }
            catch(Exception e){Plugin.Instance?.Error(e);}
        }
        internal static void Stop()
        {
            try{_tools?.GetType().GetMethod("UnregisterAll",BindingFlags.Public|BindingFlags.Static)?.Invoke(null,new object[]{Plugin.Name});}catch(Exception){}
            _tools=null;
        }

        private static IEnumerator Run(string[] args,Action<JObject> output,Action<string> error)
        {
            Player me=Player.m_localPlayer;
            if(me==null){error("gary: no player");return null;}
            var garys=Companion.States.Values.Select(s=>s.Body).Where(c=>c!=null&&Vector3.Distance(c.transform.position,me.transform.position)<60).ToList();
            if(garys.Count==0){error("gary: no Gary within 60 m");return null;}
            string sub=args.Length>1?args[1]:"info";
            if(sub=="info"){output(new JObject{["garys"]=new JArray(garys.Select(Describe))});return null;}
            if(sub=="shot")
            {
                float F(int n,float d)=>args.Length>n&&float.TryParse(args[n],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float f)?f:d;
                output(Shot(garys[0],F(2,0),F(3,10),F(4,1.6f),F(5,0.1f),args.Contains("plain")));return null;
            }
            if(sub=="crown")
            {
                float F(int n,float d)=>args.Length>n&&float.TryParse(args[n],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float f)?f:d;
                GaryVisuals.CrownAt=new Vector3(F(2,GaryVisuals.CrownAt.x),F(3,GaryVisuals.CrownAt.y),F(4,GaryVisuals.CrownAt.z));
                GaryVisuals.CrownTilt=new Vector3(F(5,GaryVisuals.CrownTilt.x),F(6,GaryVisuals.CrownTilt.y),F(7,GaryVisuals.CrownTilt.z));
                GaryVisuals.CrownSize=F(8,GaryVisuals.CrownSize);GaryVisuals.PlaceCrowns();
                output(new JObject{["at"]=V(GaryVisuals.CrownAt),["tilt"]=V(GaryVisuals.CrownTilt),["size"]=GaryVisuals.CrownSize});return null;
            }
            error("gary: info | shot | crown");return null;
        }
        private static JObject Describe(Character c)=>new JObject{
            ["position"]=V(c.transform.position),["yaw"]=Math.Round(c.transform.eulerAngles.y,1),["scale"]=V(c.transform.localScale),
            ["head"]=V(GaryVisuals.HeadBone(c)?.position??Vector3.zero),["crown"]=V(GaryVisuals.CrownOf(c)?.transform.position??Vector3.zero)};
        // Gary alone (nothing else drawn: no walls or plants in the way), from a camera around his head: yaw 0 is his face.
        private static JObject Shot(Character c,float yaw,float pitch,float dist,float up,bool plain)
        {
            Transform head=GaryVisuals.HeadBone(c)??c.transform;
            const int layer=31;
            var moved=new List<(GameObject go,int layer)>();
            foreach(Renderer r in c.GetComponentsInChildren<Renderer>(true)){moved.Add((r.gameObject,r.gameObject.layer));r.gameObject.layer=layer;}
            // "plain": with a wild greydwarf's colouring, for comparing.
            var tinted=new List<(Renderer r,MaterialPropertyBlock block)>();
            if(plain&&Companion.States.TryGetValue(c,out Companion.State st))
                foreach(var pair in st.Original)if(pair.Key!=null){var now=new MaterialPropertyBlock();pair.Key.GetPropertyBlock(now);tinted.Add((pair.Key,now));pair.Key.SetPropertyBlock(pair.Value);}
            var holder=new GameObject("gary test camera");var texture=new RenderTexture(900,900,24);
            try
            {
                Vector3 focus=head.position+c.transform.up*up;
                Quaternion turn=c.transform.rotation*Quaternion.Euler(pitch,180+yaw,0);
                Camera cam=holder.AddComponent<Camera>();cam.cullingMask=1<<layer;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(0.55f,0.62f,0.7f);
                cam.fieldOfView=35;cam.nearClipPlane=0.05f;cam.targetTexture=texture;
                holder.transform.SetPositionAndRotation(focus-turn*Vector3.forward*dist,turn);
                cam.Render();
                RenderTexture before=RenderTexture.active;RenderTexture.active=texture;
                var image=new Texture2D(900,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,900,900),0,0);image.Apply();RenderTexture.active=before;
                string dir=System.IO.Path.Combine(Paths.BepInExRootPath,"claude","shots");System.IO.Directory.CreateDirectory(dir);
                string file=System.IO.Path.Combine(dir,$"gary-{DateTime.Now:HHmmss-fff}-{yaw:0}-{pitch:0}{(plain?"-plain":"")}.png");
                System.IO.File.WriteAllBytes(file,image.EncodeToPNG());UnityEngine.Object.Destroy(image);
                return new JObject{["shot"]=file};
            }
            finally
            {
                foreach(var (go,old) in moved)if(go!=null)go.layer=old;
                foreach(var (r,block) in tinted)if(r!=null)r.SetPropertyBlock(block);
                UnityEngine.Object.Destroy(holder);texture.Release();UnityEngine.Object.Destroy(texture);
            }
        }
        private static JArray V(Vector3 v)=>new JArray(Math.Round(v.x,3),Math.Round(v.y,3),Math.Round(v.z,3));
    }
}
