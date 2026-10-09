using System;
using System.IO;
using System.Linq;
using BepInEx;
using UnityEngine;

namespace BuildShapes
{
    public sealed partial class Plugin
    {
        private long _hallDraftWorld,_hallDraftPlayer;
        private string _hallDraftLast;
        private bool _hallDraftSubmitted;
        private static string HallDraftPath(long world,long player)=>Path.Combine(Paths.ConfigPath,$"hallwright-draft-{world}-{player}.json");
        private string CurrentHallDraftPath=>Player.m_localPlayer==null || ZNet.instance==null?null:HallDraftPath(ZNet.instance.GetWorldUID(),Player.m_localPlayer.GetPlayerID());
        private static float[] DraftPoint(Vector3 p)=>new[]{p.x,p.y,p.z};
        private static Vector3 DraftVector(float[] p)=>new Vector3(p[0],p[1],p[2]);
        private void StartHallDraft()
        {_hallDraftWorld=ZNet.instance.GetWorldUID();_hallDraftPlayer=Player.m_localPlayer.GetPlayerID();_hallDraftLast=null;_hallDraftSubmitted=false;}
        private void SaveHallDraft()
        {
            if(_tool!=Tool.Hall || _markers.Count==0 || _hallDraftSubmitted)return;
            if(_hallDraftWorld==0)StartHallDraft();
            var draft=new HallDraft{World=_hallDraftWorld,Player=_hallDraftPlayer,Origin=DraftPoint(_hallOrigin),Yaw=_hallFrame.eulerAngles.y,
                Corners=_markers.Select(DraftPoint).ToArray(),Doors=_hallDoorPoints.Select(p=>DraftPoint(V(p))).ToArray(),Raise=_hallRaise,
                Height=_hallHeight,Detail=_hallDetail,Entrance=_hallEntrance,Material=_hallMaterialMode,Opening=_hallEntranceMode,Crest=_hallCrestMode,Storeys=_hallStoreys,
                Roof45=_hallRoof45,Solid=_hallSolid,ShowRoof=_hallShowRoof,Tiered=_hallTiered,Overhang=_hallOverhang,Porch=_hallPorch,Sweep=_hallSweep,Basement=_hallBasement,GuideOnly=_hallGuideOnly};
            string text=Newtonsoft.Json.JsonConvert.SerializeObject(draft);
            if(text==_hallDraftLast)return;
            try{HallDraftStore.Write(HallDraftPath(_hallDraftWorld,_hallDraftPlayer),draft);_hallDraftLast=text;}
            catch(Exception ex){Logger.LogWarning("Could not save Hallwright draft: "+ex.GetBaseException().Message);}
        }
        private bool ResumeHallDraft()
        {
            try
            {
                var draft=HallDraftStore.Read(CurrentHallDraftPath,ZNet.instance.GetWorldUID(),Player.m_localPlayer.GetPlayerID());
                Stop();_tool=Tool.Hall;StartHallDraft();_hallOrigin=DraftVector(draft.Origin);_hallFrame=Quaternion.Euler(0,draft.Yaw,0);
                _markers.AddRange(draft.Corners.Select(DraftVector));_hallDoorPoints.AddRange(draft.Doors.Select(p=>V(DraftVector(p))));
                _hallRaise=draft.Raise;_hallHeight=draft.Height;_hallDetail=draft.Detail;_hallEntrance=draft.Entrance;_hallMaterialMode=draft.Material;
                _hallEntranceMode=draft.Opening;_hallCrestMode=draft.Crest;_hallStoreys=draft.Storeys;_hallRoof45=draft.Roof45;_hallSolid=draft.Solid;
                _hallShowRoof=draft.ShowRoof;_hallTiered=draft.Tiered;_hallOverhang=draft.Overhang;_hallPorch=draft.Porch;_hallSweep=draft.Sweep;_hallBasement=draft.Basement;_hallGuideOnly=draft.GuideOnly;
                BuildHallPreview();OpenHallMenu();Say("Saved Hallwright draft reopened. Preview only; no ground or shared plans changed.");return true;
            }
            catch(Exception ex){Say(ex.GetBaseException().Message);return false;}
        }
        private void DiscardHallDraft()
        {
            if(_hallDraftWorld!=0)try{File.Delete(HallDraftPath(_hallDraftWorld,_hallDraftPlayer));}catch(Exception ex){Logger.LogWarning(ex.Message);}
            _hallDraftLast=null;
        }
    }
}
