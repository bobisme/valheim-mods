using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PassengerCart
{
    // A "pcart" command for Claude Tools, found while the game runs (no reference), to check the cart without building one by hand.
    internal static class Tools
    {
        private const string ClaudeToolsGuid="com.dhack.claudetools";
        private static BaseUnityPlugin _tools;
        private static float _nextCheck;

        internal static void Tick()
        {
            if(Time.unscaledTime<_nextCheck)return;
            _nextCheck=Time.unscaledTime+5;
            BaseUnityPlugin found=BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(ClaudeToolsGuid,out PluginInfo info)?info.Instance:null;
            if(found==_tools)return;
            _tools=found;
            if(found==null)return;
            try
            {
                found.GetType().GetMethod("RegisterCommand",BindingFlags.Public|BindingFlags.Static)?.Invoke(null,new object[]{Plugin.Name,"pcart",
                    "pcart place [ahead=6] [right=0] [vanilla] | info | sit <0-3> | stand | shove <impulse> [seconds=4] | remove: test the passenger cart. place puts one (or a vanilla cart) ahead of the player (negative: behind, left); info lists nearby carts; sit seats the player; stand gets up and puts the player back where sit found them; shove knocks every cart within 20 m sideways and reports how far each rolled; remove takes away the carts this command placed",
                    (Func<string[],Action<JObject>,Action<string>,IEnumerator>)Run});
                Plugin.Log("Claude Tools found: pcart command added");
            }
            catch(Exception e){Plugin.Log("Could not add the pcart command to Claude Tools: "+e.Message);}
        }
        internal static void Stop()
        {
            try{_tools?.GetType().GetMethod("UnregisterAll",BindingFlags.Public|BindingFlags.Static)?.Invoke(null,new object[]{Plugin.Name});}catch(Exception){}
            _tools=null;
        }

        private const string TestKey="bob_pcart_test";
        private static Vector3 _before;
        private static Quaternion _beforeTurn;
        private static float _beforeAt=-1000;
        private static IEnumerator Run(string[] args,Action<JObject> output,Action<string> error)
        {
            Player me=Player.m_localPlayer;
            if(me==null){error("pcart: no player");return null;}
            string sub=args.Length>1?args[1]:"info";
            switch(sub)
            {
                case "place":
                {
                    float metres=args.Length>2&&float.TryParse(args[2],out float m)?m:6;
                    float right=args.Length>3&&float.TryParse(args[3],out float r)?r:0;
                    bool vanilla=args.Contains("vanilla");
                    GameObject prefab=vanilla?ZNetScene.instance.GetPrefab("Cart"):CartPrefab.Prefab;
                    if(prefab==null){error("pcart place: prefab missing");break;}
                    Vector3 ahead=me.transform.forward;ahead.y=0;ahead.Normalize();
                    Vector3 at=me.transform.position+ahead*metres+Vector3.Cross(Vector3.up,ahead)*right;
                    if(ZoneSystem.instance.GetSolidHeight(at,out float height))at.y=height+0.4f;
                    GameObject cart=Object.Instantiate(prefab,at,Quaternion.LookRotation(Vector3.Cross(ahead,Vector3.up)));
                    cart.GetComponent<ZNetView>()?.GetZDO()?.Set(TestKey,true);
                    output(new JObject{["placed"]=vanilla?"Cart":CartPrefab.Name,["at"]=V(at)});
                    break;
                }
                case "info":output(new JObject{["carts"]=new JArray(Carts(30).Select(Describe))});break;
                case "sit":
                {
                    int index=args.Length>2&&int.TryParse(args[2],out int i)?i:0;
                    Carriage cart=Carts(15).Select(v=>v.GetComponent<Carriage>()).FirstOrDefault(c=>c!=null);
                    if(cart==null||index<0||index>=cart.Seats.Length){error("pcart sit: no passenger cart within 15 m, or no such seat");break;}
                    if(me.IsAttached()){error("pcart sit: already sitting");break;}
                    _before=me.transform.position;_beforeTurn=me.transform.rotation;_beforeAt=Time.time;
                    cart.Seats[index].Sit(me);
                    return Settle(output,"sat",cart.gameObject);
                }
                case "stand":
                    if(!me.IsAttached()){error("pcart stand: not sitting");break;}
                    me.AttachStop();
                    var stood=new JObject{["stood"]=V(me.transform.position)};
                    if(Time.time-_beforeAt<120){me.transform.SetPositionAndRotation(_before,_beforeTurn);stood["returned"]=V(_before);} // a test should not move the player
                    _beforeAt=-1000;
                    output(stood);
                    break;
                case "shove":
                {
                    float impulse=args.Length>2&&float.TryParse(args[2],out float j)?j:300;
                    float seconds=args.Length>3&&float.TryParse(args[3],out float s)?s:4;
                    return Shove(impulse,seconds,output);
                }
                case "remove":
                {
                    int removed=0;
                    foreach(ZNetView view in Carts(60).Where(v=>v.GetZDO().GetBool(TestKey)).ToList())
                    {
                        Container box=view.GetComponentInChildren<Container>();
                        if(box!=null&&box.GetInventory().NrOfItems()>0)continue; // never throw away cargo
                        if(!view.IsOwner())view.ClaimOwnership();
                        ZNetScene.instance.Destroy(view.gameObject);removed++;
                    }
                    output(new JObject{["removed"]=removed});
                    break;
                }
                default:error("pcart: place, info, sit, stand, shove or remove");break;
            }
            return null;
        }

        private static IEnumerator Settle(Action<JObject> output,string what,GameObject cart)
        {
            yield return new WaitForSeconds(1);
            Player me=Player.m_localPlayer;
            output(new JObject{[what]=me!=null&&me.IsAttached(),["player"]=me!=null?V(me.transform.position):null,["cart"]=Describe(cart.GetComponent<ZNetView>())});
        }

        // Every cart nearby gets the same sideways knock at its top; the steadier one rolls less and settles sooner.
        private static IEnumerator Shove(float impulse,float seconds,Action<JObject> output)
        {
            var carts=Carts(20).Where(v=>v.IsOwner()).Select(v=>v.GetComponent<Rigidbody>()).Where(b=>b!=null).ToList();
            var maxRoll=new float[carts.Count];
            for(int i=0;i<carts.Count;i++)
            {
                Rigidbody body=carts[i];
                body.WakeUp();
                body.AddForceAtPosition(body.transform.right*impulse,body.worldCenterOfMass+Vector3.up*1.2f,ForceMode.Impulse);
            }
            for(float t=0;t<seconds;t+=Time.fixedDeltaTime)
            {
                for(int i=0;i<carts.Count;i++)if(carts[i]!=null)maxRoll[i]=Mathf.Max(maxRoll[i],Vector3.Angle(carts[i].transform.up,Vector3.up));
                yield return new WaitForFixedUpdate();
            }
            var results=new JArray();
            for(int i=0;i<carts.Count;i++)
            {
                if(carts[i]==null)continue;
                JObject o=Describe(carts[i].GetComponent<ZNetView>());
                o["maxTilt"]=Math.Round(maxRoll[i],1);
                results.Add(o);
            }
            output(new JObject{["shove"]=impulse,["carts"]=results});
        }

        private static System.Collections.Generic.IEnumerable<ZNetView> Carts(float radius)
        {
            Vector3 at=Player.m_localPlayer.transform.position;
            return Object.FindObjectsByType<Vagon>(FindObjectsSortMode.None)
                .Where(v=>v!=null&&Vector3.Distance(v.transform.position,at)<radius)
                .Select(v=>v.GetComponent<ZNetView>()).Where(v=>v!=null&&v.IsValid());
        }
        private static JObject Describe(ZNetView view)
        {
            Rigidbody body=view.GetComponent<Rigidbody>();
            WearNTear wear=view.GetComponent<WearNTear>();
            Carriage carriage=view.GetComponent<Carriage>();
            return new JObject{
                ["prefab"]=Utils.GetPrefabName(view.gameObject),["at"]=V(view.transform.position),
                ["tilt"]=Math.Round(Vector3.Angle(view.transform.up,Vector3.up),1),
                ["mass"]=body!=null?Math.Round(view.GetComponentsInChildren<Rigidbody>().Sum(b=>b.mass),1):0,
                ["centerOfMass"]=body!=null?V(body.centerOfMass):null,["angularDamping"]=body!=null?body.angularDamping:0,
                ["health"]=wear!=null?Math.Round(wear.GetHealthPercentage()*wear.m_health):0,["maxHealth"]=wear!=null?wear.m_health:0,
                ["seats"]=carriage!=null?carriage.Seats.Length:0,["passengers"]=carriage!=null?carriage.Passengers():0,
                ["test"]=view.GetZDO().GetBool(TestKey)};
        }
        private static JArray V(Vector3 v)=>new JArray(Math.Round(v.x,2),Math.Round(v.y,2),Math.Round(v.z,2));
    }
}
