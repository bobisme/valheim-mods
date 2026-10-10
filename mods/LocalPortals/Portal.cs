using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LocalPortals
{
    // On each local portal: which portal it is linked to, its colour, and what its surface shows.
    // Portals given the same name link to each other, like the game's portals, but only two to a name within range of each other.
    // An unnamed new portal links to the nearest unnamed, unlinked one in range and takes the other colour; that link is kept
    // in the newer portal's data only (it is the one being placed, so its builder owns it), and the older one finds it by
    // looking for whoever names it.
    public sealed class LocalPortal:MonoBehaviour,Hoverable,Interactable,TextReceiver
    {
        internal static readonly List<LocalPortal> Live=new List<LocalPortal>();
        internal static readonly Color[] Colours={new Color(0.36f,0.66f,1f),new Color(1f,0.6f,0.24f)};
        private static readonly Color Unlinked=new Color(0.55f,0.57f,0.62f);
        private const string PartnerKey="bob_lportal_partner",ColourKey="bob_lportal_colour",LinkedKey="bob_lportal_linked",NameKey="bob_lportal_name";
        private const string NameRpc="bob_lportal_setname";
        internal const int NameLength=16;

        internal ZNetView View;
        internal MeshRenderer Surface;
        internal LocalPortal Partner;       // the linked portal, while it is loaded
        internal RenderTexture Picture;     // the view through this portal, drawn by Views
        internal int PictureFrame=-1;       // the frame it was last drawn in
        internal Collider[] Solid;          // the frame's colliders
        internal Collider Back;             // the solid board behind the glass, opened by Crossing for its own player
        private Renderer[] _glow;
        private Light _light;
        private ParticleSystem _motes;
        private MeshRenderer[] _body;       // the frame, back and crest: shadow and ghost only while cut away for the camera
        private MeshFilter[] _bodyShapes;
        private UnityEngine.Rendering.ShadowCastingMode[] _bodyShadows;
        private bool _hidden;
        private Material _view;             // this portal's own surface material, showing Picture
        private MaterialPropertyBlock _block;
        private float _nextCheck,_phase;
        private int _shownColour=-2;
        private string _problem;            // why a named portal is not linked

        private void Awake()
        {
            View=GetComponent<ZNetView>();
            Surface=transform.Find("Visual/Surface")?.GetComponent<MeshRenderer>();
            Transform visual=transform.Find("Visual");
            _glow=visual!=null?visual.GetComponentsInChildren<Renderer>(true).Where(r=>r.name.StartsWith("Trim")).ToArray():new Renderer[0];
            Back=transform.Find("Frame/Back")?.GetComponent<Collider>();
            Solid=transform.Find("Frame")?.GetComponentsInChildren<Collider>(true)??new Collider[0];
            _light=transform.Find("Visual/Light")?.GetComponent<Light>();
            _motes=transform.Find("Visual/Motes")?.GetComponent<ParticleSystem>();
            _body=visual!=null?visual.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.name!="Surface"&&!r.name.StartsWith("Trim")&&!r.name.StartsWith("Gem")).ToArray():new MeshRenderer[0];
            _bodyShapes=_body.Select(r=>r.GetComponent<MeshFilter>()).ToArray();
            _bodyShadows=_body.Select(r=>r.shadowCastingMode).ToArray();
            _phase=Random.value*6.28f;
            ShowColour(-1);
            if(View==null||!View.IsValid())return; // the building ghost
            Live.Add(this);
            View.Unregister(NameRpc); // (an earlier copy of this mod may have registered it on this object)
            View.Register<string>(NameRpc,RPC_SetName);
            if(View.IsOwner()&&!View.GetZDO().GetBool(LinkedKey))Link();
        }
        private void OnDestroy()
        {
            Unfade();
            Live.Remove(this);
            if(Picture!=null){Picture.Release();Destroy(Picture);Picture=null;}
            if(_view!=null)Destroy(_view);
        }

        internal ZDOID Id=>View!=null&&View.IsValid()?View.GetZDO().m_uid:ZDOID.None;
        internal string Name=>View!=null&&View.IsValid()?View.GetZDO().GetString(NameKey):"";
        private ZDOID Stored=>View!=null&&View.IsValid()?View.GetZDO().GetZDOID(PartnerKey):ZDOID.None;
        private static bool Named(ZDO zdo)=>zdo!=null&&zdo.GetString(NameKey).Length>0;

        // Blue or orange. A named pair: the older portal is blue. A pair built together: the one built second took the other colour.
        internal int Colour
        {
            get
            {
                if(View==null||!View.IsValid())return -1;
                if(Name.Length>0&&Partner!=null)return Id.CompareTo(Partner.Id)<0?0:1;
                return View.GetZDO().GetInt(ColourKey,0);
            }
        }

        // The portal this one is linked to, loaded or not.
        internal ZDOID PartnerId()
        {
            _problem=null;
            string name=Name;
            if(name.Length>0)
            {
                LocalPortal other=OnlyOther(this,name);
                if(other==null)return ZDOID.None;
                return OnlyOther(other,name,quiet:true)==this?other.Id:ZDOID.None;
            }
            ZDOID stored=Stored;
            if(!stored.IsNone())
            {
                ZDO zdo=ZDOMan.instance.GetZDO(stored);
                if(zdo!=null&&!Named(zdo))return stored;
            }
            ZDOID me=Id;
            foreach(LocalPortal other in Live)
                if(other!=this&&other.Stored==me&&other.Name.Length==0)return other.Id;
            return ZDOID.None;
        }
        // The one other portal in range with this name, or none (and why) if there are none or more than one.
        private LocalPortal OnlyOther(LocalPortal p,string name,bool quiet=false)
        {
            LocalPortal found=null;
            int count=0;
            foreach(LocalPortal other in Live)
            {
                if(other==p||other.View==null||!other.View.IsValid()||other.Name!=name)continue;
                if(Vector3.Distance(other.transform.position,p.transform.position)>Policy.LinkRange)continue;
                found=other;count++;
            }
            if(!quiet&&count==0)_problem="No other mirror named \""+name+"\" within "+Policy.LinkRange+" m";
            if(count>1){if(!quiet)_problem="More than two mirrors named \""+name+"\" nearby: a name links just two";return null;}
            if(!quiet&&count==1&&found!=null&&OnlyOther(found,name,quiet:true)!=p)_problem="More than two mirrors named \""+name+"\" nearby: a name links just two";
            return found;
        }

        private void Link()
        {
            ZDO zdo=View.GetZDO();
            zdo.Set(LinkedKey,true);
            LocalPortal best=null;
            float bestDistance=(float)Policy.LinkRange;
            foreach(LocalPortal other in Live)
            {
                if(other==this||other.View==null||!other.View.IsValid()||other.Name.Length>0)continue;
                float d=Vector3.Distance(other.transform.position,transform.position);
                if(d<=bestDistance&&other.PartnerId().IsNone()){best=other;bestDistance=d;}
            }
            if(best==null)return;
            zdo.Set(PartnerKey,best.Id);
            zdo.Set(ColourKey,Policy.ColourFor(best.Colour));
            Plugin.Log($"Linked a new local portal to one {bestDistance:0.#} m away");
        }

        private void Update()
        {
            if(View==null||!View.IsValid())return;
            if(Time.time>=_nextCheck)
            {
                _nextCheck=Time.time+0.5f;
                ZDOID id=PartnerId();
                Partner=null;
                if(!id.IsNone())
                    foreach(LocalPortal other in Live)
                        if(other!=this&&other.Id==id){Partner=other;break;}
                ShowColour(id.IsNone()?-1:Colour);
            }
            Breathe();
        }

        // The inlay and the light slowly brighten and dim, out of step with other portals: alive, not neon.
        private void Breathe()
        {
            if(_glow.Length==0||!_glow[0].isVisible)return;
            Color c=_shownColour>=0&&_shownColour<Colours.Length?Colours[_shownColour]:Unlinked;
            float pulse=0.78f+0.22f*Mathf.Sin(Time.time*1.15f+_phase)+0.06f*Mathf.Sin(Time.time*3.1f+_phase*2);
            float strength=_shownColour>=0?1.1f:0.25f;
            _block??=new MaterialPropertyBlock();
            foreach(Renderer glow in _glow)
            {
                glow.GetPropertyBlock(_block);
                _block.SetColor("_Color",c*0.35f);
                _block.SetColor("_EmissionColor",c*(strength*pulse));
                glow.SetPropertyBlock(_block);
            }
            if(_light!=null)_light.intensity=(_shownColour>=0?0.45f:0.15f)*pulse;
        }

        private void ShowColour(int colour)
        {
            if(colour==_shownColour)return;
            _shownColour=colour;
            Color c=colour>=0&&colour<Colours.Length?Colours[colour]:Unlinked;
            if(_light!=null)_light.color=c;
            if(_motes!=null)
            {
                var main=_motes.main;main.startColor=new Color(c.r,c.g,c.b,1);
                var emission=_motes.emission;emission.rateOverTime=colour>=0?5:1.5f;
            }
            _block??=new MaterialPropertyBlock();
            foreach(Renderer glow in _glow)
            {
                glow.GetPropertyBlock(_block);
                _block.SetColor("_Color",c*0.35f);
                _block.SetColor("_EmissionColor",c*(colour>=0?1.1f:0.25f));
                glow.SetPropertyBlock(_block);
            }
            if(Surface!=null&&PictureFrame<0&&Views.Film(colour) is Material film)Surface.sharedMaterial=film;
        }

        // Called by Views each frame the camera sees this portal: the view if it was just drawn, or the film.
        internal void ShowPicture(bool drawn,Vector4 window=default)
        {
            if(Surface==null)return;
            if(drawn&&Picture!=null)
            {
                if(_view==null)_view=Views.ViewMaterial();
                if(_view==null)return;
                if(_view.mainTexture!=Picture)_view.mainTexture=Picture;
                // the picture covers only the part of the glass on screen: (u0, v0, u1, v1) of the whole
                Vector2 scale=new Vector2(1/Mathf.Max(0.001f,window.z-window.x),1/Mathf.Max(0.001f,window.w-window.y));
                _view.mainTextureScale=scale;
                _view.mainTextureOffset=new Vector2(-window.x*scale.x,-window.y*scale.y);
                if(Surface.sharedMaterial!=_view)Surface.sharedMaterial=_view;
            }
            else
            {
                PictureFrame=-1;
                Material film=Views.Film(Partner!=null||_shownColour>=0?_shownColour:-1);
                if(film!=null&&Surface.sharedMaterial!=film)Surface.sharedMaterial=film;
            }
        }

        // Cut away for the camera: the wood fades to a faint glow in the mirror's colour (its inlay, gem and motes stay as
        // they are, and it still casts its shadow), so the camera sees through it and the mirror is still seen to be there.
        // It fades back to solid wood when the camera no longer looks through it.
        internal bool Hidden=>_hidden;
        internal float Faded=>_fade;
        private const float FadeTime=0.35f;
        private float _fade;                // 0 solid wood, 1 all glow
        private bool _realOff;              // the wood's own renderers drawing their shadow only
        private MaterialPropertyBlock _fadeBlock;
        internal void Hide(bool hide)=>_hidden=hide;
        internal static float HoldFade=-1;
        internal static Color FadeTint=new Color(0.25f,0.2f,0.13f,1); // the standard shader lights wood brighter than the piece shader: darkened to match  // for the lportal command: every mirror held at this much faded
        // Solid at once (the mod stopping, or this copy of the portal script going away).
        internal void Unfade()
        {
            _hidden=false;_fade=0;
            if(!_realOff||_body==null)return;
            _realOff=false;
            for(int i=0;i<_body.Length;i++)if(_body[i]!=null)_body[i].shadowCastingMode=_bodyShadows[i];
        }

        private void LateUpdate()
        {
            if(_body==null)return;
            float target=_hidden?1:0;
            if(HoldFade>=0)_fade=target=Mathf.Clamp01(HoldFade);
            if(_fade==target&&_fade==0&&!_realOff)return;
            _fade=Mathf.MoveTowards(_fade,target,Time.deltaTime/FadeTime);
            bool off=_fade>0;
            if(off!=_realOff)
            {
                _realOff=off;
                for(int i=0;i<_body.Length;i++)
                    if(_body[i]!=null)_body[i].shadowCastingMode=off?UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly:_bodyShadows[i];
            }
            if(!off)return;
            float k=Mathf.SmoothStep(0,1,_fade);
            Color glow=(_shownColour>=0&&_shownColour<Colours.Length?Colours[_shownColour]:Unlinked)*(0.16f*k);
            Material ghost=k>0.01f?Views.Ghost():null;
            _fadeBlock??=new MaterialPropertyBlock();
            for(int i=0;i<_body.Length;i++)
            {
                MeshRenderer r=_body[i];
                MeshFilter f=_bodyShapes[i];
                if(r==null||f==null||f.sharedMesh==null||!r.gameObject.activeInHierarchy)continue;
                Mesh mesh=f.sharedMesh;
                Matrix4x4 at=r.localToWorldMatrix;
                Material[] wood=r.sharedMaterials;
                for(int sub=0;sub<mesh.subMeshCount;sub++)
                {
                    if(k<0.99f)
                    {
                        Material source=wood.Length>0?wood[Mathf.Min(sub,wood.Length-1)]:null;
                        Material fading=Views.Fading(source);
                        if(fading!=null)
                        {
                            Color c=source.HasProperty("_Color")?source.color:Color.white;
                            c*=FadeTint;c.a=1-k;
                            _fadeBlock.Clear();
                            _fadeBlock.SetColor("_Color",c);
                            Graphics.DrawMesh(mesh,at,fading,r.gameObject.layer,null,sub,_fadeBlock,UnityEngine.Rendering.ShadowCastingMode.Off,true);
                        }
                    }
                    if(ghost!=null)
                    {
                        _fadeBlock.Clear();
                        _fadeBlock.SetColor("_Color",glow);
                        Graphics.DrawMesh(mesh,at,ghost,r.gameObject.layer,null,sub,_fadeBlock,UnityEngine.Rendering.ShadowCastingMode.Off,false);
                    }
                }
            }
        }

        public string GetHoverName()=>PortalPrefab.DisplayName;
        public float GetHoverOffset()=>0;
        public string GetHoverText()
        {
            if(View==null||!View.IsValid())return PortalPrefab.DisplayName;
            string name=Name.RemoveRichTextTags();
            ZDOID id=PartnerId();
            string title=PortalPrefab.DisplayName+(name.Length>0?" \""+name+"\"":"");
            string use="\n[<color=yellow><b>$KEY_Use</b></color>] Name it (mirrors with the same name are linked)";
            string text;
            if(id.IsNone())
                text=title+"\n<color=#aaaaaa>"+(_problem??"Not linked: build another within "+Policy.LinkRange+" m, or give two the same name")+"</color>";
            else
            {
                string mine=Colour==1?"<color=#ffa050>orange</color>":"<color=#6aaaff>blue</color>";
                if(Partner==null)text=title+" ("+mine+")\nLinked, but the other mirror is not loaded";
                else
                {
                    float d=Vector3.Distance(Partner.transform.position,transform.position);
                    string theirs=Partner.Colour==1?"<color=#ffa050>orange</color>":"<color=#6aaaff>blue</color>";
                    text=title+" ("+mine+")\nLooks out of the "+theirs+" mirror, "+d.ToString("0")+" m away";
                }
            }
            return Localization.instance.Localize(text+use);
        }

        public bool Interact(Humanoid user,bool hold,bool alt)
        {
            if(hold||View==null||!View.IsValid())return false;
            if(!PrivateArea.CheckAccess(transform.position)){user.Message(MessageHud.MessageType.Center,"$piece_noaccess");return true;}
            TextInput.instance.RequestText(this,"$piece_portal_tag",NameLength);
            return true;
        }
        public bool UseItem(Humanoid user,ItemDrop.ItemData item)=>false;
        public string GetText()=>Name;
        public void SetText(string text)
        {
            if(View!=null&&View.IsValid())View.InvokeRPC(NameRpc,(text??"").Trim());
        }
        private void RPC_SetName(long sender,string name)
        {
            if(!View.IsValid()||!View.IsOwner())return;
            if(name.Length>NameLength)name=name.Substring(0,NameLength);
            View.GetZDO().Set(NameKey,name);
            _nextCheck=0;
        }
    }
}
