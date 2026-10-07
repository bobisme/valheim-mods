using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace ChestSearch
{
    internal sealed class Window
    {
        private RectTransform _root;
        private TMP_InputField _search;
        private TMP_Text _title,_summary,_status,_source,_pages;
        private readonly List<InventoryElement> _cells=new List<InventoryElement>();
        private List<Entry> _rows=new List<Entry>();
        private int _page;
        private InventoryGui _gui;
        private Action _changed,_cancel;
        private Action<Entry,Vector2i> _drop;
        private Entry _drag;
        private GameObject _dragIcon;
        private float _nextPlace;
        private readonly List<RaycastResult> _hits=new List<RaycastResult>();
        private static readonly AccessTools.FieldRef<InventoryGui,GameObject> NativeDrag=AccessTools.FieldRefAccess<InventoryGui,GameObject>("m_dragGo");
        private static readonly System.Reflection.MethodInfo Hover=AccessTools.Method(typeof(InventoryGrid),"GetHoveredElement"),Position=AccessTools.Method(typeof(InventoryGrid),"GetElementPos");
        internal bool Visible=>_root!=null&&_root.gameObject.activeSelf;
        internal bool Dragging=>_drag!=null;
        internal bool Typing=>Visible&&_search!=null&&_search.isFocused;
        internal string Query=>_search!=null?_search.text:"";
        private const int Columns=6,RowsPerPage=4;
        private float _space;
        internal void Show(InventoryGui gui,Action changed,Action<Entry,Vector2i> drop,Action cancel)
        {
            _gui=gui;_changed=changed;_drop=drop;_cancel=cancel;
            if(_root==null)Build(gui);
            _page=0;_root.gameObject.SetActive(true);_root.SetAsLastSibling();_nextPlace=0;
            _search.text="";_search.ActivateInputField();_search.Select();
        }
        private RectTransform Rect(string name,Transform parent,float x,float y,float w,float h)
        {
            var go=new GameObject(name,typeof(RectTransform));var rect=go.GetComponent<RectTransform>();rect.SetParent(parent,false);
            rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(w,h);return rect;
        }
        private TMP_Text Text(string name,Transform parent,float x,float y,float w,float h,string text,float size=18)
        {
            RectTransform rect=Rect(name,parent,x,y,w,h);var label=rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font=_gui.m_containerName.font;label.fontSharedMaterial=_gui.m_containerName.fontSharedMaterial;
            label.fontSize=size;label.color=new Color(0.95f,0.9f,0.8f);label.text=text;label.raycastTarget=false;label.richText=false;
            return label;
        }
        private void Button(string name,float x,float y,float w,string text,Action action)
        {
            RectTransform rect=Rect(name,_root,x,y,w,34);var image=rect.gameObject.AddComponent<Image>();image.color=new Color(0.25f,0.2f,0.14f,1);
            var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=image;button.onClick.AddListener(()=>action());
            TMP_Text label=Text("Label",rect,0,0,w,34,text);label.alignment=TextAlignmentOptions.Center;
        }
        private void Build(InventoryGui gui)
        {
            _space=Mathf.Clamp(gui.ContainerGrid.m_elementSpace,60,80);
            float width=Columns*_space+48,height=RowsPerPage*_space+250;
            _root=Rect("Bob_ChestSearch",gui.m_container.parent,0,0,width,height);
            var bg=_root.gameObject.AddComponent<Image>();Image native=gui.m_container.GetComponent<Image>();
            if(native!=null){bg.sprite=native.sprite;bg.type=native.type;}bg.color=new Color(0.11f,0.09f,0.06f,0.98f);
            _title=Text("Title",_root,20,14,width-95,32,"Nearby chests",24);
            Button("Close",width-58,12,38,"×",()=>Plugin.Instance.Close(true));
            RectTransform search=Rect("Search",_root,20,56,width-40,38);search.gameObject.SetActive(false);
            var searchBg=search.gameObject.AddComponent<Image>();searchBg.color=new Color(0.22f,0.19f,0.14f,1);
            RectTransform viewport=Rect("Viewport",search,10,0,width-60,38);viewport.gameObject.AddComponent<RectMask2D>();
            TMP_Text text=Text("Text",viewport,0,0,width-60,38,"");text.alignment=TextAlignmentOptions.MidlineLeft;
            TMP_Text placeholder=Text("Placeholder",viewport,0,0,width-60,38,"Search item names…");placeholder.color=new Color(0.65f,0.62f,0.55f);placeholder.alignment=TextAlignmentOptions.MidlineLeft;
            _search=search.gameObject.AddComponent<TMP_InputField>();_search.textViewport=viewport;_search.textComponent=(TextMeshProUGUI)text;
            _search.placeholder=placeholder;_search.targetGraphic=searchBg;_search.characterLimit=80;_search.lineType=TMP_InputField.LineType.SingleLine;
            _search.onValueChanged.AddListener(s=>{_page=0;_changed?.Invoke();});search.gameObject.SetActive(true);
            _summary=Text("Summary",_root,20,99,width-40,26,"",15);
            for(int i=0;i<Policy.PageSize;i++)
            {
                int index=i;
                GameObject go=UnityEngine.Object.Instantiate(gui.ContainerGrid.m_elementPrefab,_root);
                var rect=go.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);
                rect.anchoredPosition=new Vector2(24+i%Columns*_space,-135-i/Columns*_space);
                InventoryElement cell=go.GetComponent<InventoryElement>();cell.Initialize(i%Columns,i/Columns);
                cell.m_selected.SetActive(false);cell.m_equiped.gameObject.SetActive(false);cell.m_queued.gameObject.SetActive(false);
                cell.m_food.gameObject.SetActive(false);cell.m_noteleport.gameObject.SetActive(false);cell.m_durability.gameObject.SetActive(false);
                Transform binding=go.transform.Find("binding");if(binding!=null)binding.gameObject.SetActive(false);
                UIInputHandler input=go.GetComponentInChildren<UIInputHandler>();
                input.m_onLeftDown=handler=>StartDrag(index);input.m_onRightDown=handler=>CancelDrag();
                input.m_onPointerEnter=handler=>{int row=_page*Policy.PageSize+index;_source.text=row<_rows.Count?_rows[row].Source:"";};
                _cells.Add(cell);
            }
            float bottom=135+RowsPerPage*_space;
            _source=Text("Source",_root,20,bottom,width-40,24,"",15);
            Button("Previous",20,bottom+27,75,"‹",()=>Page(-1));
            Button("Next",width-95,bottom+27,75,"›",()=>Page(1));
            _pages=Text("Pages",_root,105,bottom+27,width-210,34,"",16);_pages.alignment=TextAlignmentOptions.Center;
            _status=Text("Status",_root,20,bottom+68,width-40,42,"Drag a stack into your inventory. Right-click cancels.",15);
            _status.textWrappingMode=TextWrappingModes.Normal;
        }
        private void Page(int delta)
        {CancelDrag();_page=Mathf.Clamp(_page+delta,0,Math.Max(0,(_rows.Count-1)/Policy.PageSize));Render();}
        internal void Rows(List<Entry> rows,int count,float radius,bool limited)
        {
            _rows=rows;_page=Mathf.Clamp(_page,0,Math.Max(0,(rows.Count-1)/Policy.PageSize));
            _summary.text=count+" chests within "+radius.ToString("0")+" m · "+rows.Count+" stacks"+(limited?" (limit reached)":"");Render();
        }
        private void Render()
        {
            if(!Visible)return;
            for(int i=0;i<_cells.Count;i++)
            {
                InventoryElement cell=_cells[i];int row=_page*Policy.PageSize+i;bool found=row<_rows.Count;
                cell.m_icon.enabled=found;cell.m_amount.enabled=found;cell.m_quality.enabled=found&&_rows[row].Preview.m_quality>1;cell.m_button.interactable=found;
                if(!found){cell.m_tooltip.Set("","",null);continue;}
                Entry entry=_rows[row];cell.m_icon.sprite=entry.Preview.GetIcon();cell.m_icon.color=Color.white;
                cell.m_amount.text=entry.Preview.m_stack.ToString();cell.m_quality.text=entry.Preview.m_quality.ToString();
                cell.m_tooltip.Set(entry.Preview.m_shared.m_name,entry.Preview.GetTooltip()+"\n\n"+entry.Source,null);
            }
            _pages.text=_rows.Count==0?"No matches":(_page+1)+" / "+((_rows.Count-1)/Policy.PageSize+1);
        }
        private void StartDrag(int index)
        {
            int row=_page*Policy.PageSize+index;
            if(row>=_rows.Count||_drag!=null||Plugin.Suppress||NativeDrag(_gui)!=null)return;
            _drag=_rows[row];_dragIcon=UnityEngine.Object.Instantiate(_gui.m_dragItemPrefab,_gui.transform);
            _dragIcon.name="Bob_ChestSearchDrag";
            foreach(Graphic graphic in _dragIcon.GetComponentsInChildren<Graphic>(true))graphic.raycastTarget=false;
            var group=_dragIcon.AddComponent<CanvasGroup>();group.blocksRaycasts=false;group.interactable=false;
            _dragIcon.transform.Find("icon").GetComponent<Image>().sprite=_drag.Preview.GetIcon();
            _dragIcon.transform.Find("name").GetComponent<TMP_Text>().text=_drag.Name;
            _dragIcon.transform.Find("amount").GetComponent<TMP_Text>().text=_drag.Preview.m_stack.ToString();
            _search.DeactivateInputField();
        }
        internal void Tick()
        {
            if(!Visible)return;
            _root.SetAsLastSibling();
            if(Time.unscaledTime>=_nextPlace){Place();_nextPlace=Time.unscaledTime+1;}
            if(_drag!=null)
            {
                if(_dragIcon!=null){_dragIcon.transform.position=ZInput.pointerPosition;_dragIcon.transform.SetAsLastSibling();}
                if(Input.GetMouseButtonDown(1)){CancelDrag();return;}
                if(Input.GetMouseButtonUp(0))
                {
                    Entry drag=_drag;Vector2i? target=Target();CancelDrag();if(target.HasValue)_drop(drag,target.Value);
                }
            }
        }
        private Vector2i? Target()
        {
            InventoryGrid grid=_gui.m_playerGrid;
            if(grid==null||!grid.gameObject.activeInHierarchy||EventSystem.current==null)return null;
            _hits.Clear();var pointer=new PointerEventData(EventSystem.current){position=ZInput.pointerPosition};
            EventSystem.current.RaycastAll(pointer,_hits);
            if(_hits.Count==0||_hits[0].gameObject.GetComponentInParent<InventoryGrid>()!=grid)return null;
            object element=Hover.Invoke(grid,null);if(element==null)return null;
            Vector2i pos=(Vector2i)Position.Invoke(grid,new[]{element});return pos.x>=0&&pos.y>=0?pos:(Vector2i?)null;
        }
        private void Place()
        {
            _root.localScale=Vector3.one;
            var player=new Vector3[4];_gui.m_player.GetWorldCorners(player);
            var mine=new Vector3[4];_root.GetWorldCorners(mine);
            float width=mine[2].x-mine[1].x,height=mine[1].y-mine[0].y;
            float fit=Mathf.Min(1,(Screen.height-20)/height,(Screen.width-20)/width);
            if(fit<1){_root.localScale=Vector3.one*fit;_root.GetWorldCorners(mine);width=mine[2].x-mine[1].x;height=mine[1].y-mine[0].y;}
            Vector3 topLeft=player[2]+new Vector3(35,0,0);
            topLeft.x=Mathf.Clamp(topLeft.x,10,Mathf.Max(10,Screen.width-width-10));
            topLeft.y=Mathf.Clamp(topLeft.y,height+10,Mathf.Max(height+10,Screen.height-10));
            _root.position+=topLeft-mine[1];
        }
        internal void Status(string text)
        {if(Visible)_status.text=text??"Drag a stack into your inventory. Right-click cancels.";}
        private void CancelDrag()
        {
            if(_drag!=null)_cancel?.Invoke();_drag=null;
            if(_dragIcon!=null)UnityEngine.Object.Destroy(_dragIcon);_dragIcon=null;
        }
        internal void Close(){CancelDrag();if(_root!=null)_root.gameObject.SetActive(false);_rows.Clear();}
        internal void Destroy(){Close();if(_root!=null)UnityEngine.Object.Destroy(_root.gameObject);_root=null;_cells.Clear();}
        internal void BlockTyping()
        {if(Typing){ZInput.ResetButtonStatus("Use");ZInput.ResetButtonStatus("Inventory");}}
    }
}
