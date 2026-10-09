using System.Linq;
using UnityEngine;

namespace BuildShapes
{
    public sealed partial class Plugin
    {
        private Vector2 _hallScroll;
        private static readonly string[] HallMaterials={"Auto","Timber","Core wood","Stone base","Darkwood"};
        private static readonly string[] HallDetails={"Simple","Crafted","Ornate","Grand","King’s hall"};
        private void DrawHallMenu()=>DrawOptionsWindow(ref _hallRect,ref _hallRectPlaced,790,194741,HallContents);
        private void HallContents(int id)
        {
            GUILayout.BeginArea(new Rect(20,14,_hallRect.width-40,_hallRect.height-28));
            GUILayout.BeginHorizontal();GUILayout.Label(_staveTemple?"Stave temple":"Hallwright",_menuTitle,GUILayout.Height(32));GUILayout.FlexibleSpace();
            if(GUILayout.Button("×",_menuClose,GUILayout.Width(38),GUILayout.Height(34)))CloseHallMenu();GUILayout.EndHorizontal();
            GUILayout.Label(_staveTemple?"A tall chamber beneath stacked Viking roofs":"A timber hall fitted to your floor plan",_menuHint);
            _hallScroll=GUILayout.BeginScrollView(_hallScroll);
            GUILayout.Space(12);
            if(_hallDesign!=null)GUILayout.Label($"{_hallDesign.Cells.Count*4} / {HallLayout.MaximumCells*4:N0} m² · {_hallDesign.Wings.Count} roof wings · {_output.Count} / {HallLayout.MaximumParts:N0} pieces",_menuText);
            bool changed=false;
            int style=GUILayout.SelectionGrid(_staveTemple?1:0,new[]{"Longhouse","Stave temple"},2,_menuButton,GUILayout.Height(34));
            if((style==1)!=_staveTemple){_staveTemple=style==1;if(_staveTemple){_hallStoreys=1;_hallBasement=false;}changed=true;}
            if(_staveTemple)
            {
                GUILayout.Space(12);GUILayout.Label("Open chamber height",_menuText);
                int chamber=GUILayout.SelectionGrid((_staveHeight-4)/2,new[]{"4 m","6 m","8 m"},3,_menuButton,GUILayout.Height(34))*2+4;
                if(chamber!=_staveHeight){_staveHeight=chamber;changed=true;}
                GUILayout.Label("Tower crowns",_menuText);
                int crowns=GUILayout.SelectionGrid(_staveCrowns-1,new[]{"One","Two"},2,_menuButton,GUILayout.Height(34))+1;
                if(crowns!=_staveCrowns){_staveCrowns=crowns;changed=true;}
                bool gallery=GUILayout.Toggle(_staveGallery," Open surrounding galleries");
                if(gallery!=_staveGallery){_staveGallery=gallery;changed=true;}
                GUILayout.Label("The chamber must be at least 2 m taller than side wings. One crown: a 6 m-wide chamber. Two: 10 m and core-wood framing. Galleries reserve a 2 m walk around the chamber; the tower stays open inside.",_menuHint);
                if(_hallDesign?.Sanctum!=null)GUILayout.Label($"Chamber {_hallDesign.Sanctum.Width*2} × {_hallDesign.Sanctum.Length*2} m · roof peak {_hallDesign.RoofHeight:0.#} m above the floor",_menuHint);
            }
            GUILayout.Space(10);GUILayout.Label("Materials",_menuText);
            int material=GUILayout.SelectionGrid(_hallMaterialMode,HallMaterials,3,_menuButton,GUILayout.Height(76));
            if(material!=_hallMaterialMode){_hallMaterialMode=material;changed=true;}
            GUILayout.Label("Auto uses unlocked pieces; darkwood adds detail, core wood provides stronger framing.",_menuHint);
            GUILayout.Space(12);GUILayout.Label($"{(_staveTemple?"Gallery / side-wing":"Wall")} height: {_hallHeight} m",_menuText);
            int height=Mathf.RoundToInt(GUILayout.HorizontalSlider(_hallHeight,2,4));
            if(height!=_hallHeight){_hallHeight=height;changed=true;}
            GUILayout.BeginHorizontal();foreach(int h in new[]{2,3,4})if(GUILayout.Button(h+" m",_menuButton)){_hallHeight=h;changed=true;}GUILayout.EndHorizontal();
            if(!_staveTemple)
            {
            GUILayout.Space(12);GUILayout.Label("Storeys",_menuText);
            int storeys=GUILayout.SelectionGrid(_hallStoreys-1,new[]{"One","Two","Three"},3,_menuButton,GUILayout.Height(34))+1;
            if(storeys!=_hallStoreys){_hallStoreys=storeys;changed=true;}
            bool basement=GUILayout.Toggle(_hallBasement," Basement · 3 m stone cellar");
            if(basement!=_hallBasement){_hallBasement=basement;changed=true;}
            GUILayout.Label("Stairs are placed inside the footprint with landings and open headroom. A basement needs unlocked stone, dry terrain and a clear site. Ground changes only when you Plan shell.",_menuHint);
            GUILayout.Space(12);GUILayout.Label("Roof silhouette",_menuText);
            int roof=GUILayout.SelectionGrid(_hallTiered?1:0,new[]{"Gabled","Tiered longhouse"},2,_menuButton,GUILayout.Height(34));
            if((roof==1)!=_hallTiered){_hallTiered=roof==1;changed=true;}
            GUILayout.Label("Tiered: raised central roof, low side aisles and an inner colonnade. Wings under 6 m wide stay gabled.",_menuHint);
            }
            GUILayout.Space(12);GUILayout.Label("Roof pitch",_menuText);
            int pitch=GUILayout.SelectionGrid(_hallRoof45?1:0,new[]{"26° · low","45° · steep"},2,_menuButton,GUILayout.Height(34));
            if((pitch==1)!=_hallRoof45){_hallRoof45=pitch==1;changed=true;}
            GUILayout.Space(12);GUILayout.Label(_staveTemple?"Temple details":"Longhouse details",_menuText);
            bool DetailSwitch(bool value,string label)
            {
                if(!GUILayout.Button((value?"On · ":"Off · ")+label,value?_menuSelected:_menuButton))return value;
                changed=true;return !value;
            }
            _hallOverhang=DetailSwitch(_hallOverhang,"Deep roof overhangs · 2 m");
            _hallPorch=DetailSwitch(_hallPorch,"Covered entrance porch · 4 × 2 m");
            _hallSweep=DetailSwitch(_hallSweep,"Sweeping gable timberwork");
            GUILayout.Label(_staveTemple?"Layered eaves extend above lower roofs where they clear. A porch needs a 4 m edge and clear space outside. All pieces use normal materials and support.":"Overhangs fit around other wings and the porch. A porch needs a 4 m edge and clear ground outside. All pieces use normal materials and support.",_menuHint);
            GUILayout.Label("Ridge ends",_menuText);
            int crest=GUILayout.SelectionGrid(_hallCrestMode,new[]{"Auto","None","Dragon","Raven"},2,_menuButton,GUILayout.Height(70));
            if(crest!=_hallCrestMode){_hallCrestMode=crest;changed=true;}
            GUILayout.Label("Auto adds unlocked carvings at Ornate and above. Carvings and swept trim appear on exposed gables.",_menuHint);
            GUILayout.Space(12);GUILayout.Label("Intricacy: "+HallDetails[_hallDetail],_menuText);
            int detail=Mathf.RoundToInt(GUILayout.HorizontalSlider(_hallDetail,0,4));
            if(detail!=_hallDetail){_hallDetail=detail;changed=true;}
            GUILayout.Label(_hallDetail==0?"Clean shell and structural trusses.":_hallDetail==1?"Gable trim and repeated knee braces.":_hallDetail==2?"Radiating gable timberwork and unlocked darkwood details.":_hallDetail==3?"Rich gable patterns, layered eaves and carved belts.":"Royal knotwork, carved darkwood panels, daylight windows and layered timber bands.",_menuHint);
            GUILayout.Space(12);GUILayout.Label("Entrance",_menuText);
            int opening=GUILayout.SelectionGrid(_hallEntranceMode,new[]{"Auto","Door · 2 m","Gate · 3 m"},3,_menuButton,GUILayout.Height(34));
            if(opening!=_hallEntranceMode){_hallEntranceMode=opening;changed=true;}
            GUILayout.Label("Auto chooses an unlocked gate at 3 m or taller, otherwise a door.",_menuHint);
            if(_markers.Count>0 && _hallDoorPoints.Count==0)
            {
                GUILayout.BeginHorizontal();
                if(GUILayout.Button("‹",_menuButton,GUILayout.Width(42))){_hallEntrance=(_hallEntrance+_markers.Count-1)%_markers.Count;changed=true;}
                GUILayout.Label($"Edge {_hallEntrance%_markers.Count+1} of {_markers.Count}",_menuText);
                if(GUILayout.Button("›",_menuButton,GUILayout.Width(42))){_hallEntrance=(_hallEntrance+1)%_markers.Count;changed=true;}
                GUILayout.EndHorizontal();
            }
            GUILayout.Label("Ctrl+click near exterior walls to mark up to eight entrances; click a marker again to remove it. The first marker gets the covered porch. Edit outline returns to the ground.",_menuHint);
            for(int i=0;i<_hallDoorPoints.Count;i++)
            {
                GUILayout.BeginHorizontal();GUILayout.Label($"Entrance {i+1}"+(i==0?" · main":""),_menuText);
                bool remove=GUILayout.Button("Remove",_menuButton,GUILayout.Width(90));GUILayout.EndHorizontal();
                if(remove){_hallDoorPoints.RemoveAt(i);changed=true;break;}
            }
            if(_hallDoorPoints.Count>0 && GUILayout.Button("Clear entrance markers · use edge selector",_menuButton)){_hallDoorPoints.Clear();changed=true;}
            GUILayout.Space(12);GUILayout.Label(_hallBasement?"Ground floor lift above entrance terrain":"Foundation lift above highest ground",_menuText);
            float raise=_hallRaise;changed|=NumberControlRow("Floor raise","m",0,2,0.05f,ref raise);_hallRaise=raise;
            GUILayout.Label(_hallBasement?"The cellar pit follows the footprint. A level entrance apron meets its deck; courtyards remain outside the pit.":"Posts or a stone plinth bring the floor to one level.",_menuHint);
            if(System.IO.File.Exists(HallGroundFile))
            {
                GUILayout.Label("Saved basement ground recovery (survives F6)",_menuHint);
                if(GUILayout.Button("Restore ground · clear built cellar first",_menuButton))try{RestoreHallGround();changed=true;Say("Original terrain restored.");}catch(System.Exception ex){Say(ex.GetBaseException().Message);}
                if(GUILayout.Button("Keep excavation · discard recovery",_menuButton))try{RestoreHallGround(true);Say("Excavation kept; another basement can now be planned.");}catch(System.Exception ex){Say(ex.GetBaseException().Message);}
            }
            GUILayout.Space(10);
            GUILayout.Label("Sharing",_menuText);
            _shareDrafts.Value=GUILayout.Toggle(_shareDrafts.Value,"Share my draft with other players");
            _shareDraftPieces.Value=GUILayout.Toggle(_shareDraftPieces.Value,"Include piece previews (Layout sends guides only)");
            GUILayout.Label("Viewers need BuildShapes. Cyan drafts are visual only; Plan shell creates the buildable shared ghosts.",_menuHint);
            GUILayout.Space(10);GUILayout.Label("Preview",_menuText);
            int preview=GUILayout.SelectionGrid(_hallGuideOnly?2:_hallSolid?0:1,new[]{"Materials","Ghosts","Layout"},3,_menuButton,GUILayout.Height(34));
            _hallGuideOnly=preview==2;if(preview!=2)_hallSolid=preview==0;
            GUILayout.Label("Layout shows the footprint, storey heights and roof outline. Tall amber posts mark terrain corners even on slopes.",_menuHint);
            if(GUILayout.Button(_hallShowRoof?"Hide roof":"Show roof",_menuButton)){_hallShowRoof=!_hallShowRoof;changed=true;}
            if(GUILayout.Button("Refresh unlocks and support",_menuButton)){_hallMeshes.Clear();changed=true;}
            if(changed)QueueHallPreview();
            GUILayout.Space(12);GUILayout.Label("Materials bill",_menuText);
            foreach(var entry in _hallBill.OrderBy(v=>v.Key))
            {
                var item=ObjectDB.instance?.GetItemPrefab(entry.Key)?.GetComponent<ItemDrop>();
                string name=item!=null?Localization.instance.Localize(item.m_itemData.m_shared.m_name):entry.Key;
                GUILayout.Label($"{entry.Value} × {name}",_menuHint);
            }
            if(_hallStations.Count>0)GUILayout.Label("Build stations: "+string.Join(", ",_hallStations),_menuHint);
            if(_hallNote!=null)GUILayout.Label(_hallNote,_menuHint);
            GUILayout.EndScrollView();
            GUILayout.Space(10);
            string problem=_numberError??_hallProblem;
            GUILayout.Label(_hallDue>0?"Updating preview…":problem??$"Support estimate: all {_output.Count} pieces stand. Small gable peaks are vented.",problem!=null?_menuWarning:_menuText);
            GUILayout.Space(8);bool enabled=GUI.enabled;GUILayout.BeginHorizontal();
            GUI.enabled=enabled && _hallProblem==null && _hallDue==0 && _output.Count>0;
            if(GUILayout.Button("Plan shell",_menuButton,GUILayout.Height(36)))ConfirmHall(Player.m_localPlayer);
            GUI.enabled=enabled;
            if(GUILayout.Button("Edit outline",_menuButton,GUILayout.Height(36)))CloseHallMenu();
            if(GUILayout.Button("Cancel",_menuButton,GUILayout.Height(36)))Stop();
            GUILayout.EndHorizontal();GUILayout.Label("Esc: close options · L: reopen · F4: modes",_menuHint);
            GUILayout.EndArea();GUI.DragWindow(new Rect(0,0,_hallRect.width-60,50));
        }
    }
}
