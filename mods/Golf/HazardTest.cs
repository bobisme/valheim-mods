using System;
using System.Collections;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MeadowGolf
{
    public sealed partial class Plugin
    {
        private IEnumerator HazardTest(Vector3 origin,Action<JObject> output)
        {
            // Unsaved independent live GolfBall; it never replaces or changes a player's card.
            var go=UnityEngine.Object.Instantiate(Prefabs.BallPrefab,origin,Quaternion.identity);GolfBall ball=go.GetComponent<GolfBall>();
            try
            {
                ZDO z=ball.Data;z.Persistent=false;z.Set(GolfWorld.PlayerKey,0L);z.Set(GolfWorld.NameKey,"Disposable hazard test");
                z.Set(GolfWorld.LabelKey,"Hazard test:1:3");z.Set(GolfWorld.LieKey,origin);z.Set(GolfWorld.StrokesKey,1);
                yield return new WaitForFixedUpdate();ShotPhysics.Launch(ball.Body,2,.6f,Vector3.forward);
                float deadline=Time.unscaledTime+8;
                while(ball.Strokes==1){if(Time.unscaledTime>deadline)throw new InvalidOperationException("Water did not return the live ball to its lie.");yield return null;}
                yield return new WaitForSecondsRealtime(.6f);
                float error=Vector3.Distance(ball.Body.position,origin);
                if(ball.Strokes!=2||error>.05f)throw new InvalidOperationException("Water recovery / penalty was not stable.");
                output(new JObject{["liveWaterPenalty"]=1,["returnError"]=error,["playerCardChanged"]=false});
            }
            finally{if(go!=null){ball.View.ClaimOwnership();ZNetScene.instance.Destroy(go);}}
        }
    }
}
