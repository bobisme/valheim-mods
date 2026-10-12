using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using Object=UnityEngine.Object;

namespace BuildShapes
{
    // The Hallwright settings in the game's own look (GameUi: the crafting panel's wood, its tabs and buttons, the game's fonts).
    // The window is a list of rows: the same list builds it and gives the signature that says when to build it again, so it is
    // rebuilt only when what it shows changes. A click only changes a setting; anything that changes the world (Plan shell, the
    // ground) waits for Update. Falls back to the old panel if the game's interface parts can't be found.
    public sealed partial class Plugin
    {
        private sealed class HallRow
        {
            internal string Kind,Text;
            internal string[] Options;
            internal int Chosen;
            internal bool On,Enabled=true;
            internal Color Color;
            internal Action<int> Pick;
            internal Action Click;
            internal List<(string text,bool enabled,Action click)> Buttons;
            internal string Key=>Kind+"|"+Text+"|"+(Options==null?"":string.Join(",",Options))+"|"+Chosen+"|"+On+"|"+Enabled+"|"+(Buttons==null?"":string.Join(",",Buttons.Select(b=>b.text+b.enabled)));
        }
        private RectTransform _hallWindow,_hallWindowList;
        private string _hallWindowShown;
        private float _hallWindowOpened;
        private Action _hallWindowAction;
        private bool _hallWindowFallback;
        private const float HallWindowWidth=720f;

        private void HallChanged()=>QueueHallPreview();
        // Runs from a click: the world changes in TickHallWindow, not while the window is being drawn.
        private void HallLater(Action action)=>_hallWindowAction=action;

        private List<HallRow> HallRows()
        {
            var rows=new List<HallRow>();
            void Head(string text)=>rows.Add(new HallRow{Kind="head",Text=text});
            void Hint(string text)=>rows.Add(new HallRow{Kind="hint",Text=text});
            void Note(string text,Color color)=>rows.Add(new HallRow{Kind="note",Text=text,Color=color});
            void Tabs(string[] options,int chosen,Action<int> pick)=>rows.Add(new HallRow{Kind="tabs",Options=options,Chosen=chosen,Pick=pick});
            void Toggle(string text,bool on,Action click)=>rows.Add(new HallRow{Kind="toggle",Text=text,On=on,Click=click});
            void Buttons(params (string,bool,Action)[] buttons)=>rows.Add(new HallRow{Kind="buttons",Buttons=buttons.ToList()});
            void Gap()=>rows.Add(new HallRow{Kind="gap"});

            if(_hallDesign!=null)Note($"{_hallDesign.Cells.Count*4} / {HallLayout.MaximumCells*4:N0} m² · {_hallDesign.Wings.Count} roof wings · {_output.Count} / {HallLayout.MaximumParts:N0} pieces",GameUi.Text);
            Tabs(new[]{"Timber hall","Longhouse","Stave temple"},HallStyle,i=>{if(i!=HallStyle){SetHallStyle(i);HallChanged();}});
            if(_staveTemple)
            {
                Gap();Head("Open chamber height");
                Tabs(new[]{"4 m","6 m","8 m"},(_staveHeight-4)/2,i=>{_staveHeight=i*2+4;HallChanged();});
                Head("Tower crowns");
                Tabs(new[]{"One","Two"},_staveCrowns-1,i=>{_staveCrowns=i+1;HallChanged();});
                Toggle("Open surrounding galleries",_staveGallery,()=>{_staveGallery=!_staveGallery;HallChanged();});
                Hint("The chamber must be at least 2 m taller than side wings. One crown: a 6 m-wide chamber. Two: 10 m and core-wood framing. Galleries reserve a 2 m walk around the chamber; the tower stays open inside.");
                if(_hallDesign?.Sanctum!=null)Hint($"Chamber {_hallDesign.Sanctum.Width*2} × {_hallDesign.Sanctum.Length*2} m · roof peak {_hallDesign.RoofHeight:0.#} m above the floor");
            }
            Gap();Head("Materials");
            Tabs(HallMaterials,_hallMaterialMode,i=>{_hallMaterialMode=i;HallChanged();});
            Hint("Auto uses unlocked pieces; darkwood adds detail, core wood provides stronger framing.");
            Gap();Head($"{(_staveTemple?"Gallery / side-wing":"Wall")} height");
            Tabs(new[]{"2 m","3 m","4 m"},_hallHeight-2,i=>{_hallHeight=i+2;HallChanged();});
            if(_hallNorse)
            {
                Gap();Head("Dug into the ground");
                Tabs(new[]{"Not dug in","0.5 m","1 m","1.5 m","2 m"},Mathf.RoundToInt(_hallSink*2),i=>{_hallSink=i*0.5f;HallChanged();});
                Hint("The floor is sunk below the average ground, with a strip 1.5 m wide dug along the outside of the walls and a ramp down to each door. Nothing is heaped up. Ground changes only when you Plan shell, and can be restored. Not dug in: the floor stands on posts.");
            }
            if(!_staveTemple)
            {
                if(!_hallNorse)
                {
                    Gap();Head("Storeys");
                    Tabs(new[]{"One","Two","Three"},_hallStoreys-1,i=>{_hallStoreys=i+1;HallChanged();});
                    Toggle("Basement · 3 m stone cellar",_hallBasement,()=>{_hallBasement=!_hallBasement;HallChanged();});
                    Hint("Stairs are placed inside the footprint with landings and open headroom. A basement needs unlocked stone, dry terrain and a clear site. Ground changes only when you Plan shell.");
                }
                Gap();Head("Roof silhouette");
                Tabs(new[]{"Gabled","Tiered longhouse"},_hallTiered?1:0,i=>{_hallTiered=i==1;HallChanged();});
                Hint("Tiered: raised central roof, low side aisles and an inner colonnade. Wings under 6 m wide stay gabled.");
            }
            Gap();Head("Roof pitch");
            Tabs(new[]{"26° · low","45° · steep"},_hallRoof45?1:0,i=>{_hallRoof45=i==1;HallChanged();});
            Gap();Head(_staveTemple?"Temple details":_hallNorse?"Longhouse details":"Hall details");
            if(_hallNorse)
            {
                Toggle("Raised platforms along the long walls",_hallBenches,()=>{_hallBenches=!_hallBenches;HallChanged();});
                Toggle("Long hearth · smoke holes in the roof",_hallHearth,()=>{_hallHearth=!_hallHearth;HallChanged();});
                Toggle("Stone footing · half-height timber walls",_hallFooting,()=>{_hallFooting=!_hallFooting;HallChanged();});
            }
            else
            {
                Toggle("Deep roof overhangs · 2 m",_hallOverhang,()=>{_hallOverhang=!_hallOverhang;HallChanged();});
                Toggle("Covered entrance porch · 4 × 2 m",_hallPorch,()=>{_hallPorch=!_hallPorch;HallChanged();});
            }
            Toggle("Sweeping gable timberwork",_hallSweep,()=>{_hallSweep=!_hallSweep;HallChanged();});
            Toggle("Shuttered windows · 2 × 1 m",_hallShutters,()=>{_hallShutters=!_hallShutters;HallChanged();});
            Hint(_staveTemple?"Layered eaves extend above lower roofs where they clear. A porch needs a 4 m edge and clear space outside. All pieces use normal materials and support.":_hallNorse?"Platforms are 2 m deep in halls 10 m wide or more, 1 m at 8 m. Hearths run down the middle of wings at least 6 × 8 m, with smoke holes beside them, never above, and rows of aisle posts carry the roof. The footing is the game's stone fence, 4 stone each, built at a workbench.":"Overhangs fit around other wings and the porch. A porch needs a 4 m edge and clear ground outside. All pieces use normal materials and support.");
            if(!_staveTemple)
            {
                Gap();Head(_hallNorse?"Smoke holes":"Chimneys");
                Hint(_hallNorse?"Ctrl+Shift+click a floor tile to leave the roof above it open, as the Norse let their smoke out. Up to four, besides the ones beside the hearths. Under a 45° roof each gets a lower 26° piece that keeps the rain off and leaves a slit for the smoke.":"Ctrl+Shift+click a floor tile to open the roof above it and raise a timber flue past the ridge; set your fire under it. Up to four. Upper floors leave a shaft for it.");
                for(int i=0;i<_hallChimneyPoints.Count;i++){int index=i;Buttons(($"Remove {(_hallNorse?"smoke hole":"chimney")} {i+1}",true,(Action)(()=>{_hallChimneyPoints.RemoveAt(index);HallChanged();})));}
            }
            Gap();Head("Ridge ends");
            Tabs(new[]{"Auto","None","Dragon","Raven"},_hallCrestMode,i=>{_hallCrestMode=i;HallChanged();});
            Hint("Auto adds unlocked carvings at Ornate and above. Carvings and swept trim appear on exposed gables.");
            Gap();Head("Intricacy");
            Tabs(HallDetails,_hallDetail,i=>{_hallDetail=i;HallChanged();});
            Hint(_hallDetail==0?"Clean shell and structural trusses.":_hallDetail==1?"Gable trim and repeated knee braces.":_hallDetail==2?"Radiating gable timberwork and unlocked darkwood details.":_hallDetail==3?"Rich gable patterns, layered eaves and carved belts.":"Royal knotwork, carved darkwood panels, daylight windows and layered timber bands.");
            Gap();Head("Entrance");
            Tabs(new[]{"Auto","Door · 2 m","Gate · 3 m"},_hallEntranceMode,i=>{_hallEntranceMode=i;HallChanged();});
            Hint("Auto chooses an unlocked gate at 3 m or taller, otherwise a door.");
            if(_markers.Count>0 && _hallDoorPoints.Count==0)
                Buttons(("<",true,(Action)(()=>{_hallEntrance=(_hallEntrance+_markers.Count-1)%_markers.Count;HallChanged();})),($"Door on edge {_hallEntrance%_markers.Count+1} of {_markers.Count}",false,(Action)(()=>{})),(">",true,(Action)(()=>{_hallEntrance=(_hallEntrance+1)%_markers.Count;HallChanged();})));
            Hint("Ctrl+click near exterior walls to mark up to eight entrances; click a marker again to remove it. The first marker gets the covered porch.");
            for(int i=0;i<_hallDoorPoints.Count;i++){int index=i;Buttons(($"Remove entrance {i+1}"+(i==0?" · main":""),true,(Action)(()=>{_hallDoorPoints.RemoveAt(index);HallChanged();})));}
            if(_hallDoorPoints.Count>0)Buttons(("Clear entrance markers · use the edge selector",true,(Action)(()=>{_hallDoorPoints.Clear();HallChanged();})));
            if(!(_hallNorse && _hallSink>0))
            {
                Gap();Head(_hallBasement?"Ground floor lift above entrance terrain":"Foundation lift above highest ground");
                void Raise(float by){_hallRaise=Mathf.Clamp(Mathf.Round((_hallRaise+by)*100)/100,0,2);HallChanged();}
                Buttons(("-0.25",_hallRaise>0,(Action)(()=>Raise(-0.25f))),("-0.05",_hallRaise>0,(Action)(()=>Raise(-0.05f))),($"{_hallRaise:0.00} m",false,(Action)(()=>{})),("+0.05",_hallRaise<2,(Action)(()=>Raise(0.05f))),("+0.25",_hallRaise<2,(Action)(()=>Raise(0.25f))));
                Hint(_hallBasement?"The cellar pit follows the footprint. A level entrance apron meets its deck; courtyards remain outside the pit.":"Posts or a stone plinth bring the floor to one level.");
            }
            if(System.IO.File.Exists(HallGroundFile))
            {
                Gap();Head("Saved ground");
                Hint("The ground from the last basement or longhouse can be put back once the hall is taken down (survives F6). Ground changed since, such as a workbench pad, is left as it is.");
                Buttons(("Restore ground",true,(Action)(()=>HallLater(()=>{try{int skipped=RestoreHallGround();QueueHallPreview();Say(skipped==0?"Original terrain restored.":$"Original terrain restored, except {skipped} ground points changed since, which were left as they are.");}catch(Exception ex){Say(ex.GetBaseException().Message);}}))),
                    ("Keep the ground as it is",true,(Action)(()=>HallLater(()=>{try{RestoreHallGround(true);QueueHallPreview();Say("Ground kept; another basement or longhouse can now be planned.");}catch(Exception ex){Say(ex.GetBaseException().Message);}}))));
            }
            Gap();Head("Sharing");
            Toggle("Share my draft with other players",_shareDrafts.Value,()=>_shareDrafts.Value=!_shareDrafts.Value);
            Toggle("Include piece previews",_shareDraftPieces.Value,()=>_shareDraftPieces.Value=!_shareDraftPieces.Value);
            Hint("Viewers need BuildShapes. Cyan drafts are visual only; Plan shell creates the buildable shared ghosts.");
            Gap();Head("Preview");
            Tabs(new[]{"Materials","Ghosts","Layout"},_hallGuideOnly?2:_hallSolid?0:1,i=>{_hallGuideOnly=i==2;if(i!=2)_hallSolid=i==0;HallChanged();});
            Buttons((_hallShowRoof?"Hide roof":"Show roof",true,(Action)(()=>{_hallShowRoof=!_hallShowRoof;HallChanged();})),("Refresh unlocks and support",true,(Action)(()=>{_hallMeshes.Clear();foreach(var t in _hallTemplates.Values)if(t!=null)Object.Destroy(t);_hallTemplates.Clear();HallChanged();})));
            Hint("Layout shows the footprint, storey heights and roof outline without the pieces.");
            Gap();Head("Materials bill");
            foreach(var entry in _hallBill.OrderBy(v=>v.Key))
            {
                var item=ObjectDB.instance?.GetItemPrefab(entry.Key)?.GetComponent<ItemDrop>();
                Note($"{entry.Value} × {(item!=null?Localization.instance.Localize(item.m_itemData.m_shared.m_name):entry.Key)}",GameUi.Text);
            }
            if(_hallStations.Count>0)Hint("Build stations: "+string.Join(", ",_hallStations));
            if(_hallNote!=null)Hint(_hallNote);
            return rows;
        }
        private string HallStatus=>_hallDue>0?"Updating preview...":_numberError??_hallProblem??$"Support estimate: all {_output.Count} pieces stand. Small gable peaks are vented.";
        private bool HallCanPlan=>_hallProblem==null && _hallDue==0 && _output.Count>0;

        // Every frame while Hallwright is on: open, close and rebuild the window, and run what its clicks asked for.
        private void TickHallWindow(Player player)
        {
            if(!_hallMenu){CloseHallWindow();return;}
            if(_hallWindowFallback)return;
            if(!GameUi.Ready()){_hallWindowFallback=true;CloseHallWindow();return;}
            Action action=_hallWindowAction;_hallWindowAction=null;
            action?.Invoke();
            if(!_hallMenu){CloseHallWindow();return;}
            // E closes it, as it closes a chest (not the E that opened it).
            if(_hallWindow!=null && Time.unscaledTime>_hallWindowOpened+0.2f && ZInput.GetButtonDown("Use")){CloseHallMenu();return;}
            if(Input.GetMouseButton(0) && _hallWindow!=null)return; // never rebuild under a press
            var rows=HallRows();
            string shown=string.Join("\n",rows.Select(r=>r.Key))+"\n"+HallStatus+HallCanPlan+_staveTemple+_hallNorse;
            if(shown==_hallWindowShown && _hallWindow!=null)return;
            BuildHallWindow(rows,shown);
        }
        private void BuildHallWindow(List<HallRow> rows,string shown)
        {
            float scrolled=_hallWindowList!=null?_hallWindowList.anchoredPosition.y:0;
            bool fresh=_hallWindow==null;
            if(_hallWindow!=null)Object.Destroy(_hallWindow.gameObject);
            _hallWindowShown=shown;
            float height=Mathf.Min(960f,GameUi.ScreenSize.y-40f),w=HallWindowWidth;
            _hallWindow=GameUi.Window("BuildShapesHallwright",new Vector2(w,height));
            // At the right of the screen, so the preview stays in view.
            _hallWindow.anchoredPosition=new Vector2(Mathf.Max(0,GameUi.ScreenSize.x/2-w/2-24),0);
            if(fresh)_hallWindowOpened=Time.unscaledTime;
            GameUi.Line(_hallWindow,_staveTemple?"Stave temple":_hallNorse?"Longhouse":"Hallwright",new Vector2(30,-18),w-140,32,GameUi.Warm,GameUi.Norse);
            GameUi.Button(_hallWindow,"X",new Vector2(w-74,-20),new Vector2(46,40),()=>HallLater(CloseHallMenu));
            GameUi.Line(_hallWindow,_staveTemple?"A tall chamber beneath stacked Viking roofs":_hallNorse?"A Norse longhouse dug into the ground around a long hearth":"A timber hall fitted to your floor plan",new Vector2(30,-62),w-60,16,GameUi.Dim);
            GameUi.Braid(_hallWindow,new Vector2(0,-98),w-60);
            float bottom=150;
            RectTransform box=GameUi.Box(_hallWindow,new Vector2(22,-112),new Vector2(w-44,height-112-bottom));
            _hallWindowList=GameUi.Scroll(box,8);
            float width=_hallWindowList.sizeDelta.x-8,y=0;
            foreach(var row in rows)
            {
                switch(row.Kind)
                {
                    case "gap":y+=10;break;
                    case "head":GameUi.Line(_hallWindowList,row.Text,new Vector2(6,-y),width,20,GameUi.Warm);y+=36;break;
                    case "hint":case "note":
                    {
                        TMP_Text t=GameUi.Label(_hallWindowList,row.Text,new Vector2(6,-y),new Vector2(width-6,40),row.Kind=="hint"?15:17,row.Kind=="hint"?GameUi.Dim:row.Color);
                        float h=Mathf.Ceil(t.GetPreferredValues(row.Text,width-6,0).y)+2;
                        ((RectTransform)t.transform).sizeDelta=new Vector2(width-6,h);y+=h+6;break;
                    }
                    case "tabs":
                    {
                        int per=row.Options.Length<=3?row.Options.Length:row.Options.Length==4?4:5;
                        float bw=(width-(per-1)*6)/per;
                        for(int i=0;i<row.Options.Length;i++)
                        {
                            int index=i;
                            if(i>0 && i%per==0)y+=46;
                            GameUi.Tab(_hallWindowList,row.Options[i],new Vector2(i%per*(bw+6),-y),new Vector2(bw,40),i==row.Chosen,()=>{if(index!=row.Chosen)row.Pick(index);},16);
                        }
                        y+=48;break;
                    }
                    case "toggle":GameUi.Toggle(_hallWindowList,row.Text,row.On,new Vector2(0,-y),new Vector2(width,38),row.Click,16);y+=44;break;
                    case "buttons":
                    {
                        int n=row.Buttons.Count;float bw=(width-(n-1)*6)/n;
                        for(int i=0;i<n;i++){var b=row.Buttons[i];GameUi.Button(_hallWindowList,b.text,new Vector2(i*(bw+6),-y),new Vector2(bw,38),b.click,b.enabled,16);}
                        y+=44;break;
                    }
                }
            }
            _hallWindowList.sizeDelta=new Vector2(_hallWindowList.sizeDelta.x,Mathf.Max(0,y-6));
            _hallWindowList.anchoredPosition=new Vector2(0,Mathf.Min(scrolled,Mathf.Max(0,y-6-box.sizeDelta.y+16)));
            // Status, and the three actions, below the list.
            bool problem=_hallDue==0 && (_numberError??_hallProblem)!=null;
            TMP_Text status=GameUi.Label(_hallWindow,HallStatus,new Vector2(30,-(height-bottom+10)),new Vector2(w-60,52),16,problem?GameUi.Warn:GameUi.Text);
            status.overflowMode=TextOverflowModes.Ellipsis;
            float third=(w-60-12)/3;
            GameUi.Button(_hallWindow,"Plan shell",new Vector2(30,-(height-82)),new Vector2(third,44),()=>HallLater(()=>ConfirmHall(Player.m_localPlayer)),HallCanPlan,18);
            GameUi.Button(_hallWindow,"Edit outline",new Vector2(30+third+6,-(height-82)),new Vector2(third,44),()=>HallLater(CloseHallMenu),true,18);
            GameUi.Button(_hallWindow,"Cancel",new Vector2(30+2*(third+6),-(height-82)),new Vector2(third,44),()=>HallLater(Stop),true,18);
            GameUi.Line(_hallWindow,"Esc or E: close"+(StakeMode?" · E inside the outline: open again":" · L: open again · F4: modes"),new Vector2(30,-(height-34)),w-60,14,GameUi.Dim);
        }
        private void CloseHallWindow()
        {
            if(_hallWindow!=null)Object.Destroy(_hallWindow.gameObject);
            _hallWindow=null;_hallWindowList=null;_hallWindowShown=null;_hallWindowAction=null;
        }
        // Claude Tools' "shoot close" closes windows through a public static Close before a screenshot.
        public static void Close()=>Instance?.CloseHallMenu();
    }
}
