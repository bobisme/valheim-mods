using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MeadowGolf
{
    public sealed partial class Plugin
    {
        private GameObject _nativeUI;private Canvas _canvas;private Sprite _wood,_sunken;
        private readonly List<Image> _images=new List<Image>();private readonly List<TextMeshProUGUI> _texts=new List<TextMeshProUGUI>();
        private int _imageUsed,_textUsed;private float _uiUnits;private TMP_FontAsset _nativeFont,_titleFont;
        private bool BeginNative(float scale)
        {
            if(Event.current.type!=EventType.Repaint)return false;
            if(_nativeUI==null)
            {
                var inventory=InventoryGui.instance;if(inventory?.m_player==null)return false;
                _canvas=inventory.GetComponentInParent<Canvas>();if(_canvas==null)return false;
                var images=inventory.m_player.GetComponentsInChildren<Image>(true);
                _wood=images.Select(i=>i.sprite).FirstOrDefault(s=>s!=null&&s.name=="woodpanel_playerinventory");
                _sunken=images.Select(i=>i.sprite).FirstOrDefault(s=>s!=null&&s.name=="sunken");
                _nativeFont=inventory.m_recipeDecription?.font;_titleFont=inventory.m_recipeName?.font??inventory.m_playerName?.font;
                _nativeUI=new GameObject("MeadowGolfUI",typeof(RectTransform));_nativeUI.transform.SetParent(_canvas.transform,false);
                var root=(RectTransform)_nativeUI.transform;root.anchorMin=Vector2.zero;root.anchorMax=Vector2.one;root.offsetMin=root.offsetMax=Vector2.zero;
            }
            _nativeUI.SetActive(true);_imageUsed=_textUsed=0;_uiUnits=scale/Mathf.Max(.01f,_canvas.scaleFactor);return true;
        }
        private void EndNative()
        {
            for(int i=_imageUsed;i<_images.Count;i++)_images[i].gameObject.SetActive(false);
            for(int i=_textUsed;i<_texts.Count;i++)_texts[i].gameObject.SetActive(false);
        }
        private void Place(RectTransform rt,Rect r)
        {rt.anchorMin=rt.anchorMax=new Vector2(0,1);rt.pivot=new Vector2(0,1);rt.anchoredPosition=new Vector2(r.x,-r.y)*_uiUnits;rt.sizeDelta=new Vector2(r.width,r.height)*_uiUnits;}
        private Image UIBox(Rect r,Sprite sprite,Color color)
        {
            Image image;
            if(_imageUsed<_images.Count)image=_images[_imageUsed];
            else{var go=new GameObject("Golf panel",typeof(RectTransform),typeof(Image));go.transform.SetParent(_nativeUI.transform,false);image=go.GetComponent<Image>();image.raycastTarget=false;_images.Add(image);}
            _imageUsed++;image.gameObject.SetActive(true);image.transform.SetAsLastSibling();Place(image.rectTransform,r);
            image.sprite=sprite;image.type=sprite==null?Image.Type.Simple:Image.Type.Sliced;image.color=color;return image;
        }
        private void Panel(Rect r,bool inset=false)=>UIBox(r,inset?_sunken:_wood,inset?Color.white:new Color(.4f,.4f,.4f,1f));
        private void NativeLabel(Rect r,string value,GUIStyle style)
        {
            TextMeshProUGUI text;
            if(_textUsed<_texts.Count)text=_texts[_textUsed];
            else{var go=new GameObject("Golf text",typeof(RectTransform),typeof(TextMeshProUGUI));go.transform.SetParent(_nativeUI.transform,false);text=go.GetComponent<TextMeshProUGUI>();text.raycastTarget=false;text.richText=false;text.textWrappingMode=TextWrappingModes.NoWrap;text.overflowMode=TextOverflowModes.Ellipsis;text.alignment=TextAlignmentOptions.TopLeft;_texts.Add(text);}
            _textUsed++;text.gameObject.SetActive(true);text.transform.SetAsLastSibling();Place(text.rectTransform,r);
            TMP_FontAsset font=style==_heading?_titleFont:_nativeFont;if(font!=null)text.font=font;text.fontSize=style.fontSize*_uiUnits;text.fontStyle=style.fontStyle==FontStyle.Bold?FontStyles.Bold:FontStyles.Normal;
            text.color=style.normal.textColor;text.text=value;
        }
    }
}
