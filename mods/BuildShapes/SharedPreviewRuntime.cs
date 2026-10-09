using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace BuildShapes
{
    public sealed partial class Plugin
    {
        private const string SharedDraftRpc="Bob_BuildShapes_Draft_v1",SharedHelloRpc="Bob_BuildShapes_DraftHello_v1";
        private ConfigEntry<bool> _shareDrafts,_showSharedDrafts,_shareDraftPieces,_showSharedPieces;
        private ZRoutedRpc _sharedRpc;
        private long _sharedWorld,_sharedRevision=DateTime.UtcNow.Ticks;
        private int _sharedAwakeFrame;
        private float _sharedNextRoster,_sharedNextHello;
        private SharedPreviewCadence _sharedCadence=new SharedPreviewCadence();
        private bool _sharedDirty=true,_sharedLastPieces,_sharedLastRoof,_sharedLastLayout,_sharedLastEnabled;
        private byte[] _sharedPacket;
        private readonly HashSet<long> _sharedParticipants=new HashSet<long>();
        private readonly Dictionary<long,string> _sharedOnline=new Dictionary<long,string>();
        private SharedPreviewSet _sharedDraftSet=new SharedPreviewSet();
        private readonly Dictionary<long,List<GameObject>> _sharedLines=new Dictionary<long,List<GameObject>>();
        private readonly Dictionary<string,List<HallMesh>> _sharedMeshes=new Dictionary<string,List<HallMesh>>();
        private readonly Dictionary<Material,Material> _sharedMaterials=new Dictionary<Material,Material>();
        private Material _sharedLineMaterial,_sharedPostMaterial,_sharedFallbackMaterial;
        private readonly MaterialPropertyBlock _sharedTint=new MaterialPropertyBlock();
        private GUIStyle _sharedLabel;
        private void ConfigureSharedPreviews()
        {
            _shareDrafts=Config.Bind("Shared drafts","ShareMyDraft",true,"Show your live corner posts, outlines and shape previews to other players with BuildShapes. Visuals only; no terrain or pieces are placed.");
            _showSharedDrafts=Config.Bind("Shared drafts","ShowOthersDrafts",true,"Show nearby players' live drafts. Requires BuildShapes on designer and viewer.");
            _shareDraftPieces=Config.Bind("Shared drafts","SharePiecePreviews",true,"Include native piece meshes in your shared draft, except Hallwright Layout mode. Roof visibility is respected.");
            _showSharedPieces=Config.Bind("Shared drafts","ShowPiecePreviews",true,"Draw other players' draft meshes as cyan ghosts. Turn off for outlines/posts only. At most 4,096 nearby shared pieces render at once.");
            _sharedAwakeFrame=Time.frameCount;
        }
        private static SharedPoint SharePoint(Vector3 p)=>new SharedPoint(p.x,p.y,p.z);
        private static Vector3 ShareVector(SharedPoint p)=>new Vector3(p.X,p.Y,p.Z);
        private void DirtySharedPreview(){_sharedDirty=true;}
        private void UnregisterSharedPreviews()
        {
            if(_sharedRpc==null)return;
            var table=AccessTools.Field(typeof(ZRoutedRpc),"m_functions")?.GetValue(_sharedRpc) as IDictionary;
            table?.Remove(SharedDraftRpc.GetStableHashCode());table?.Remove(SharedHelloRpc.GetStableHashCode());_sharedRpc=null;
        }
        private void UpdateSharedPreviews()
        {
            var rpc=ZRoutedRpc.instance;var player=Player.m_localPlayer;
            long world=ZNet.World?.m_uid??0;
            if(rpc!=_sharedRpc || world!=_sharedWorld)
            {
                UnregisterSharedPreviews();ClearSharedLines();ClearSharedMeshCache();_sharedDraftSet=new SharedPreviewSet();_sharedParticipants.Clear();_sharedOnline.Clear();_sharedPacket=null;_sharedDirty=true;
                _sharedWorld=world;_sharedNextRoster=_sharedNextHello=0;_sharedCadence=new SharedPreviewCadence();
                if(rpc==null||player==null||Time.frameCount<=_sharedAwakeFrame+2)return;
                _sharedRpc=rpc;
                var table=AccessTools.Field(typeof(ZRoutedRpc),"m_functions")?.GetValue(rpc) as IDictionary;
                table?.Remove(SharedDraftRpc.GetStableHashCode());table?.Remove(SharedHelloRpc.GetStableHashCode());
                rpc.Register<ZPackage>(SharedDraftRpc,OnSharedPreview);rpc.Register<long>(SharedHelloRpc,OnSharedHello);
            }
            if(_sharedRpc==null||player==null){ClearSharedLines();return;}
            float now=Time.unscaledTime;
            if(now>=_sharedNextRoster)
            {
                _sharedNextRoster=now+0.5f;_sharedOnline.Clear();
                foreach(var person in ZNet.instance.GetPlayerList())
                {long id=person.m_characterID.UserID;if(id!=0&&id!=ZDOMan.GetSessionID()&&_sharedOnline.Count<16)_sharedOnline[id]=person.m_name;}
                _sharedParticipants.RemoveWhere(id=>!_sharedOnline.ContainsKey(id));
                foreach(long id in _sharedDraftSet.Expire(now,new HashSet<long>(_sharedOnline.Keys)))ClearSharedLines(id);
            }
            if(now>=_sharedNextHello)
            {_sharedNextHello=now+15;foreach(long id in _sharedOnline.Keys.Where(id=>!_sharedParticipants.Contains(id)))SendSharedHello(id);}
            bool active=_enabled.Value&&_shareDrafts.Value&&_tool!=Tool.None&&_markers.Count>0;
            if(active!=_sharedLastEnabled||_shareDraftPieces.Value!=_sharedLastPieces||_hallShowRoof!=_sharedLastRoof||_hallGuideOnly!=_sharedLastLayout)
            {_sharedDirty=true;_sharedLastEnabled=active;_sharedLastPieces=_shareDraftPieces.Value;_sharedLastRoof=_hallShowRoof;_sharedLastLayout=_hallGuideOnly;}
            if(!_sharedCadence.Due(now,_sharedDirty))return;
            // Stable heartbeats reuse geometry and do not rebuild the receiver's renderers.
            if(_sharedDirty||_sharedPacket==null)
            {
                _sharedDirty=false;
                try{_sharedPacket=SharedPreviewCodec.Encode(CaptureSharedPreview(active));}
                catch(Exception ex){Logger.LogWarning("Shared preview was not sent: "+ex.GetBaseException().Message);_sharedPacket=SharedPreviewCodec.Encode(CaptureSharedPreview(false));}
            }
            _sharedCadence.Sent(now);
            foreach(long id in _sharedParticipants)SendSharedPacket(id);
        }
        private SharedPreview CaptureSharedPreview(bool active)
        {
            var p=new SharedPreview{World=_sharedWorld,Revision=++_sharedRevision,Tool=active?(byte)_tool:(byte)0,Center=SharePoint(Vector3.zero)};
            if(!active)return p;
            Vector3 center=(_markers.Aggregate(Vector3.zero,(a,b)=>a+b))/_markers.Count;
            center.y=_tool==Tool.Hall&&_hallGuideHasFloor?_hallFloorY+_hallHeight*_hallStoreys+1.5f:_markers.Max(v=>v.y)+3;
            p.Center=SharePoint(center);int points=0;
            foreach(var go in _visuals)
            {
                var line=go==null?null:go.GetComponent<LineRenderer>();if(line==null||line.positionCount<2||line.positionCount>128)continue;
                if(p.Lines.Count>=SharedPreviewCodec.MaxLines||points+line.positionCount>SharedPreviewCodec.MaxPoints)break;
                var positions=new Vector3[line.positionCount];line.GetPositions(positions);points+=positions.Length;
                p.Lines.Add(new SharedStroke{Width=line.startWidth,ThroughGround=line.sharedMaterial==_hallMarkerMaterial,Points=positions.Select(SharePoint).ToArray()});
            }
            if(_shareDraftPieces.Value&&(_tool!=Tool.Hall||!_hallGuideOnly))
            for(int i=0;i<_output.Count&&p.Pieces.Count<SharedPreviewCodec.MaxPieces;i++)
            {
                if(_tool==Tool.Hall&&!_hallShowRoof&&_hallRoles[i].EndsWith("roof",StringComparison.Ordinal))continue;
                var g=_output[i];p.Pieces.Add(new SharedGhost{Prefab=g.Prefab,At=SharePoint(g.Position),X=g.Rotation.x,Y=g.Rotation.y,Z=g.Rotation.z,W=g.Rotation.w});
            }
            return p;
        }
        private void SendSharedHello(long id)
        {try{_sharedRpc?.InvokeRoutedRPC(id,SharedHelloRpc,_sharedWorld);}catch(Exception ex){Logger.LogDebug(ex.Message);}}
        private void SendSharedPacket(long id)
        {if(_sharedPacket!=null)try{_sharedRpc?.InvokeRoutedRPC(id,SharedDraftRpc,new ZPackage(_sharedPacket));}catch(Exception ex){Logger.LogDebug(ex.Message);}}
        private void OnSharedHello(long sender,long world)
        {
            if(world!=_sharedWorld||!_sharedOnline.ContainsKey(sender)||sender==ZDOMan.GetSessionID())return;
            if(_sharedParticipants.Add(sender))SendSharedHello(sender);SendSharedPacket(sender);
        }
        private void OnSharedPreview(long sender,ZPackage package)
        {
            if(!_enabled.Value||!_showSharedDrafts.Value||!_sharedOnline.ContainsKey(sender)||package==null||package.Size()>SharedPreviewCodec.MaxBytes)return;
            try
            {
                var preview=SharedPreviewCodec.Decode(package.GetArray(),_sharedWorld);
                if(_sharedDraftSet.Accept(sender,preview,Time.unscaledTime))ClearSharedLines(sender);
            }
            catch(Exception ex)when(ex is ArgumentException||ex is System.IO.IOException||ex is System.Text.DecoderFallbackException){Logger.LogDebug("Ignored invalid shared draft: "+ex.Message);}
        }
        private void ClearSharedLines(long sender)
        {if(_sharedLines.TryGetValue(sender,out var lines)){foreach(var go in lines)if(go!=null)Destroy(go);_sharedLines.Remove(sender);}}
        private void ClearSharedLines(){foreach(long id in _sharedLines.Keys.ToArray())ClearSharedLines(id);}
        private Material SharedLineMaterial(bool post)
        {
            if(_sharedLineMaterial==null)
            {
                var shader=new[]{"Hidden/Internal-Colored","Sprites/Default"}.Select(Shader.Find).FirstOrDefault(s=>s!=null&&s.isSupported);if(shader==null)return null;
                _sharedLineMaterial=new Material(shader){color=Color.white,renderQueue=3050};
                if(_sharedLineMaterial.HasProperty("_ZWrite"))_sharedLineMaterial.SetInt("_ZWrite",0);
                if(_sharedLineMaterial.HasProperty("_SrcBlend"))_sharedLineMaterial.SetInt("_SrcBlend",(int)BlendMode.SrcAlpha);
                if(_sharedLineMaterial.HasProperty("_DstBlend"))_sharedLineMaterial.SetInt("_DstBlend",(int)BlendMode.OneMinusSrcAlpha);
                if(_sharedLineMaterial.HasProperty("_ZTest"))_sharedLineMaterial.SetInt("_ZTest",(int)CompareFunction.LessEqual);
                _sharedPostMaterial=new Material(_sharedLineMaterial);if(_sharedPostMaterial.HasProperty("_ZTest"))_sharedPostMaterial.SetInt("_ZTest",(int)CompareFunction.Always);
            }
            return post?_sharedPostMaterial:_sharedLineMaterial;
        }
        private void BuildSharedLines(long sender,SharedPreview preview)
        {
            var lines=new List<GameObject>();_sharedLines[sender]=lines;
            foreach(var stroke in preview.Lines)
            {
                var material=SharedLineMaterial(stroke.ThroughGround);if(material==null)break;
                var go=new GameObject("BuildShapes shared draft "+sender);go.layer=LayerMask.NameToLayer("Ignore Raycast");
                var line=go.AddComponent<LineRenderer>();line.useWorldSpace=true;line.sharedMaterial=material;line.startColor=line.endColor=new Color(0.25f,0.78f,0.95f,0.65f);
                line.startWidth=line.endWidth=stroke.Width;line.positionCount=stroke.Points.Length;line.SetPositions(stroke.Points.Select(ShareVector).ToArray());line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;lines.Add(go);
            }
        }
        private List<HallMesh> SharedMeshes(string name)
        {
            if(_sharedMeshes.TryGetValue(name,out var cached))return cached;
            var prefab=ZNetScene.instance?.GetPrefab(name);var piece=prefab==null?null:prefab.GetComponent<Piece>();
            if(_sharedMeshes.Count>=1024)_sharedMeshes.Clear();
            return _sharedMeshes[name]=piece==null||prefab.GetComponentInChildren<TerrainOp>(true)!=null||prefab.GetComponentInChildren<TerrainModifier>(true)!=null?new List<HallMesh>():ReadHallMeshes(prefab);
        }
        private Material SharedGhostMaterial(Material source)
        {
            if(_sharedMaterials.TryGetValue(source,out var cached))return cached;
            if(_sharedFallbackMaterial==null){var shader=new[]{"Legacy Shaders/Transparent/Diffuse","Sprites/Default"}.Select(Shader.Find).FirstOrDefault(s=>s!=null&&s.isSupported);if(shader==null)return null;_sharedFallbackMaterial=new Material(shader){color=new Color(0.4f,0.8f,0.95f,0.35f),renderQueue=3000};}
            if(_sharedMaterials.Count>=512)return _sharedFallbackMaterial;
            var material=new Material(_sharedFallbackMaterial);if(source.HasProperty("_MainTex"))material.mainTexture=source.mainTexture;_sharedMaterials[source]=material;return material;
        }
        private void LateUpdate()
        {
            if(Player.m_localPlayer==null)return;
            bool show=_enabled.Value&&_showSharedDrafts.Value;int groups=0,pieces=4096;
            foreach(var pair in _sharedDraftSet.Entries.OrderBy(p=>(ShareVector(p.Value.Preview.Center)-Player.m_localPlayer.transform.position).sqrMagnitude))
            {
                var preview=pair.Value.Preview;bool visible=show&&groups<4&&(ShareVector(preview.Center)-Player.m_localPlayer.transform.position).sqrMagnitude<120*120;
                if(visible)groups++;
                if(!_sharedLines.TryGetValue(pair.Key,out var lines)&&visible){BuildSharedLines(pair.Key,preview);lines=_sharedLines[pair.Key];}
                if(lines!=null)foreach(var go in lines)if(go!=null&&go.activeSelf!=visible)go.SetActive(visible);
                if(!visible||!_showSharedPieces.Value||preview.Pieces.Count>pieces)continue;pieces-=preview.Pieces.Count;
                _sharedTint.Clear();_sharedTint.SetColor("_Color",new Color(0.4f,0.8f,0.95f,0.35f));
                foreach(var ghost in preview.Pieces)
                {
                    var rotation=new Quaternion(ghost.X,ghost.Y,ghost.Z,ghost.W).normalized;
                    var matrix=Matrix4x4.TRS(ShareVector(ghost.At),rotation,Vector3.one);
                    foreach(var mesh in SharedMeshes(ghost.Prefab))for(int sub=0;sub<Math.Min(mesh.Mesh.subMeshCount,mesh.Materials.Length);sub++)
                    {var source=mesh.Materials[sub];if(source==null)continue;var material=SharedGhostMaterial(source);if(material!=null)Graphics.DrawMesh(mesh.Mesh,matrix*mesh.Local,material,0,null,sub,_sharedTint,ShadowCastingMode.Off,false);}
                }
            }
        }
        private void DrawSharedLabels()
        {
            if(!_enabled.Value||!_showSharedDrafts.Value||Player.m_localPlayer==null||Camera.main==null||Menu.IsVisible()||InventoryGui.IsVisible())return;
            if(_sharedLabel==null)_sharedLabel=new GUIStyle(GUI.skin.label){alignment=TextAnchor.MiddleCenter,fontSize=14,richText=false,normal={textColor=new Color(0.55f,0.85f,1)}};
            foreach(var pair in _sharedDraftSet.Entries)
            {
                if(!_sharedLines.TryGetValue(pair.Key,out var lines)||lines.Count==0||!lines[0].activeSelf)continue;
                Vector3 screen=Camera.main.WorldToScreenPoint(ShareVector(pair.Value.Preview.Center));if(screen.z<=0)continue;
                _sharedOnline.TryGetValue(pair.Key,out string name);GUI.Label(new Rect(screen.x-200,Screen.height-screen.y-18,400,28),(name??"Player")+" · "+((Tool)pair.Value.Preview.Tool)+" draft",_sharedLabel);
            }
        }
        private void ClearSharedMeshCache()
        {
            _sharedMeshes.Clear();foreach(var material in _sharedMaterials.Values)if(material!=null)Destroy(material);_sharedMaterials.Clear();
            if(_sharedFallbackMaterial!=null)Destroy(_sharedFallbackMaterial);_sharedFallbackMaterial=null;
        }
        private void DestroySharedPreviews()
        {
            if(_sharedRpc!=null){try{_sharedPacket=SharedPreviewCodec.Encode(CaptureSharedPreview(false));foreach(long id in _sharedParticipants)SendSharedPacket(id);}catch(Exception){} }
            UnregisterSharedPreviews();ClearSharedLines();_sharedDraftSet.Entries.Clear();ClearSharedMeshCache();
            if(_sharedLineMaterial!=null)Destroy(_sharedLineMaterial);if(_sharedPostMaterial!=null)Destroy(_sharedPostMaterial);
        }
    }
}
