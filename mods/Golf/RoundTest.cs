using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MeadowGolf
{
    public sealed partial class Plugin
    {
        // Real host RPCs, owner shots, native animations and cup captures on tiny temporary holes.
        // No terrain edits, teleports, structure modification or injected scores.
        private IEnumerator RoundTest(int holes,Action<JObject> output)
        {
            if(_roundTest)throw new InvalidOperationException("A round test is already running.");
            Player p=Player.m_localPlayer;var fixtures=new List<GameObject>();var tees=new List<GolfMarker>();var cups=new List<GolfMarker>();
            ItemDrop.ItemData prior=Prefabs.Right(p),club=p.GetInventory().GetAllItems().FirstOrDefault(Prefabs.IsClub);
            if(club==null)throw new InvalidOperationException("Keep a Meadow golf club in your inventory for this test.");
            Vector3 forward=p.transform.forward;forward.y=0;forward.Normalize();Vector3 position=p.transform.position+forward*1.5f+Vector3.up*1.3f;
            string course="Golf check "+System.Guid.NewGuid().ToString("N").Substring(0,8);
            bool priorCard=_card;_card=true;_roundTest=true;_roundCancel=false;_roundHole=1;
            try
            {
                var slab=new GameObject("Disposable Golf match slab"){layer=LayerMask.NameToLayer("piece")};fixtures.Add(slab);slab.AddComponent<GolfTestLifetime>().Extend(120);
                slab.transform.position=position+forward*.5f-Vector3.up*.05f;var flat=slab.AddComponent<BoxCollider>();flat.size=new Vector3(1.5f,.1f,1.5f);
                p.EquipItem(club,false);yield return new WaitForSecondsRealtime(.4f);
                for(int h=1;h<=holes;h++)
                {
                    var tee=UnityEngine.Object.Instantiate(Prefabs.TeePrefab,position,Quaternion.LookRotation(forward));fixtures.Add(tee);
                    var cup=UnityEngine.Object.Instantiate(Prefabs.CupPrefab,position+forward*.5f,Quaternion.identity);fixtures.Add(cup);
                    foreach(var go in new[]{tee,cup})
                    {go.GetComponent<Piece>().SetCreator(p.GetPlayerID(),Splatform.PlatformManager.DistributionPlatform.LocalUser.PlatformUserID);go.GetComponent<ZNetView>().GetZDO().Set(GolfWorld.LabelKey,$"{course}:{h}:3");go.GetComponent<ZNetView>().GetZDO().Set("bob_golf_test",p.GetPlayerID());go.GetComponent<ZNetView>().GetZDO().Persistent=false;go.AddComponent<GolfTestLifetime>().Extend(120);foreach(var renderer in go.GetComponentsInChildren<Renderer>())renderer.enabled=false;}
                    tees.Add(tee.GetComponent<GolfMarker>());cups.Add(cup.GetComponent<GolfMarker>());
                }
                GolfMatches.RequestMatch(holes);float timeout=Time.unscaledTime+20;
                GolfBall ball;
                while((ball=MyBall())==null||ball.Data.GetString(GolfWorld.LabelKey,"")!=$"{course}:1:3")
                {if(_roundCancel||Time.unscaledTime>timeout)throw new InvalidOperationException("Match did not start within 20 s. Check the player stayed near the first tee with the club.");yield return null;}
                string id=ball.Data.GetString(GolfMatches.Id,"");
                if(id.Length!=32||ball.Data.GetInt(GolfMatches.Length,0)!=holes)throw new InvalidOperationException("Missing saved match identity / course length.");
                ZDOID firstId=ball.Data.m_uid;
                yield return new WaitForSecondsRealtime(1.1f);GolfWorld.Start(tees[1],cups[1]);yield return new WaitForSecondsRealtime(.3f);
                if(MyBall()?.Data?.m_uid!=firstId)throw new InvalidOperationException("Match allowed skipping an unfinished hole.");
                yield return new WaitForSecondsRealtime(1.1f);GolfMatches.RequestMatch(0,true);yield return new WaitForSecondsRealtime(.3f);
                if(MyBall()?.Data?.m_uid!=firstId)throw new InvalidOperationException("Joining twice replaced an existing participant card.");
                for(int h=1;h<=holes;h++)
                {
                    _roundHole=h;if(_roundCancel)throw new InvalidOperationException("Round test canceled.");
                    if(h>1)
                    {
                        GolfWorld.Start(tees[h-1]);timeout=Time.unscaledTime+10;
                        while((ball=MyBall())==null||ball.Data.GetString(GolfWorld.LabelKey,"")!=$"{course}:{h}:3")
                        {if(_roundCancel||Time.unscaledTime>timeout)throw new InvalidOperationException($"Hole {h} did not start.");yield return null;}
                    }
                    timeout=Time.unscaledTime+10;
                    while(!ball.Still){if(_roundCancel||Time.unscaledTime>timeout)throw new InvalidOperationException("Ball failed to settle.");yield return null;}
                    Swing(p,ball,2,0,forward);timeout=Time.unscaledTime+10;
                    while(!ball.Done){if(_roundCancel||Time.unscaledTime>timeout)throw new InvalidOperationException($"Hole {h} did not capture: {ball.LastRejectedShot}; strokes={ball.Strokes}; closed={ball.Closed}; player distance={Vector3.Distance(p.transform.position,ball.transform.position):0.00}.");yield return null;}
                    var rows=Rules.ReadCard(ball.Data.GetString(GolfWorld.CardKey,""));
                    if(rows.Count!=h||rows.Any(row=>row.Strokes!=1)||ball.Data.GetString(GolfMatches.Id,"")!=id)throw new InvalidOperationException("Scores / match identity changed across holes.");
                    yield return new WaitForSecondsRealtime(.35f);
                }
                if(GolfMatches.State(ball.Data)!="Finished")throw new InvalidOperationException("Round did not finish at its declared length.");
                string card=ball.Data.GetString(GolfWorld.CardKey,"");
                yield return new WaitForSecondsRealtime(1.1f);GolfMatches.RefreshBoard();yield return new WaitForSecondsRealtime(.3f);
                if(!GolfMatches.Board.Any(row=>row.Player==p.GetPlayerID()&&row.Card==card))throw new InvalidOperationException("Host scoreboard did not retain the final card.");
                GolfMatches.StopRound();yield return new WaitForSecondsRealtime(.3f);if(!ball.Closed||ball.Data.GetString(GolfWorld.CardKey,"")!=card)throw new InvalidOperationException("Stopping your round lost its card.");
                yield return new WaitForSecondsRealtime(1.1f);GolfMatches.EndMatch();yield return new WaitForSecondsRealtime(.3f);
                if(!ball.Closed||tees[0].View.GetZDO().GetBool(GolfMatches.Open,false)||ball.Data.GetString(GolfWorld.CardKey,"")!=card)throw new InvalidOperationException("Ending the match lost the card or left the ball active.");
                output(new JObject{["holes"]=holes,["actualCompletedHoles"]=Rules.ReadCard(card).Count,["strokes"]=holes,["card"]=card,["match"]=id,["finished"]=true,["ended"]=ball.Closed,["scoreboard"]=true,["stopOwnRound"]=true,["holeSkipRejected"]=true,["duplicateJoinRejected"]=true,["structuresModified"]=false});
            }
            finally
            {
                _roundTest=false;_card=priorCard;
                foreach(var go in fixtures)if(go!=null){var view=go.GetComponent<ZNetView>();if(view!=null&&view.IsValid()){view.ClaimOwnership();ZNetScene.instance.Destroy(go);}else Destroy(go);}
                if(p!=null&&prior!=null&&p.GetInventory().ContainsItem(prior))p.EquipItem(prior,false);
                else if(p!=null)p.UnequipItem(club,false);
            }
        }
    }
}
