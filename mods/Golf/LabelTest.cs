using System;
using System.Collections;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MeadowGolf
{
    public sealed partial class Plugin
    {
        // Exercise native Shift+E / TextInput and owner RPCs on one unsaved invisible
        // marker. Never change a real course, ball, building, player position or terrain.
        private IEnumerator LabelTest(Action<JObject> output)
        {
            Player player=Player.m_localPlayer;
            if(TextInput.instance==null||TextInput.instance.m_panel.activeSelf||!CanPlay(player))
                throw new InvalidOperationException("Close menus and finish any text input before testing.");
            GameObject fixture=null;
            try
            {
                fixture=UnityEngine.Object.Instantiate(Prefabs.TeePrefab,player.transform.position+player.transform.right*2,Quaternion.identity);
                fixture.AddComponent<GolfTestLifetime>().Extend(30);
                foreach(Renderer renderer in fixture.GetComponentsInChildren<Renderer>())renderer.enabled=false;
                foreach(Collider collider in fixture.GetComponentsInChildren<Collider>())collider.enabled=false;
                Piece piece=fixture.GetComponent<Piece>();
                piece.SetCreator(player.GetPlayerID(),Splatform.PlatformManager.DistributionPlatform.LocalUser.PlatformUserID);
                GolfMarker marker=fixture.GetComponent<GolfMarker>();ZDO data=marker.View.GetZDO();data.Persistent=false;
                data.Set(GolfWorld.LabelKey,"Rename check "+System.Guid.NewGuid().ToString("N").Substring(0,8)+":4:5");
                if(!marker.Interact(player,false,true)||!TextInput.instance.m_panel.activeSelf)
                    throw new InvalidOperationException("Native Shift+E did not open the marker editor.");
                TextInput.instance.m_inputField.text="test2";TextInput.instance.OnEnter();
                yield return new WaitForSecondsRealtime(.2f);
                if(marker.GetText()!="test2:4:5"||marker.LastLabelResult!="Saved Golf tee: test2:4:5")
                    throw new InvalidOperationException("Plain-name save or owner confirmation failed.");
                marker.SetText(" Full renamed : 2 : 4 ");yield return new WaitForSecondsRealtime(.2f);
                if(marker.GetText()!="Full renamed:2:4")throw new InvalidOperationException("Explicit hole/par save failed.");
                marker.SetText("bad:19:3");yield return new WaitForSecondsRealtime(.1f);
                if(marker.GetText()!="Full renamed:2:4")throw new InvalidOperationException("Invalid input changed the marker.");
                data.Set(GolfMatches.Open,true);marker.SetText("blocked");yield return new WaitForSecondsRealtime(.2f);
                if(marker.GetText()!="Full renamed:2:4"||!marker.LastLabelResult.Contains("open match"))
                    throw new InvalidOperationException("Open-match rejection was silent or changed the label.");
                data.Set(GolfMatches.Open,false);
                data.Set(ZDOVars.s_creator,player.GetPlayerID()+1);
                marker.SetText("blocked");yield return new WaitForSecondsRealtime(.2f);
                if(marker.GetText()!="Full renamed:2:4"||!marker.LastLabelResult.Contains("builder"))
                    throw new InvalidOperationException("Builder rejection was silent or changed the label.");
                output(new JObject{["nativeEditor"]=true,["plainNameSaved"]=true,["holeParPreserved"]=true,
                    ["fullLabelSaved"]=true,["invalidInputRejected"]=true,["ownerConfirmation"]=true,
                    ["openMatchRejected"]=true,["otherBuilderRejected"]=true,["realCoursesModified"]=false});
            }
            finally
            {
                if(fixture!=null)
                {
                    ZNetView view=fixture.GetComponent<ZNetView>();
                    if(view!=null&&view.IsValid()){view.ClaimOwnership();ZNetScene.instance.Destroy(fixture);}
                    else Destroy(fixture);
                }
            }
        }
    }
}
