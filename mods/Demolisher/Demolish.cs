using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Demolisher
{
    // What the outline takes down: every loaded piece inside it that the hammer could remove from here, tinted red while the stake is in
    // hand. Yes in the confirmation removes them top first, a few each frame, by the hammer's own path (materials drop, wards hold).
    public sealed partial class Plugin
    {
        private readonly List<Piece> _doomed=new List<Piece>();
        private readonly Dictionary<string,int> _skipped=new Dictionary<string,int>();
        private readonly HashSet<Piece> _tinted=new HashSet<Piece>();
        private readonly Queue<Piece> _queue=new Queue<Piece>();
        private readonly List<Piece> _found=new List<Piece>();
        private float _nextScan;
        private bool _asking;
        private int _removed,_kept;
        private readonly List<Vector3> _bolts=new List<Vector3>();
        private float _strikeAt;
        private static readonly int ColorId=Shader.PropertyToID("_Color"),EmissionId=Shader.PropertyToID("_EmissionColor");
        private static readonly Color DoomedColor=new Color(1f,0.3f,0.25f);

        private void Scan(Player player)
        {
            _nextScan=Time.unscaledTime+0.5f;
            _doomed.Clear();_skipped.Clear();
            if(!_closed || _corners.Count<3){Untint();return;}
            var flat=Flat(_corners);
            var (cx,cz,radius)=Area.Circle(flat);
            _found.Clear();
            Piece.GetAllPiecesInRadius(new Vector3((float)cx,player.transform.position.y,(float)cz),100000f,_found);
            foreach(Piece piece in _found)
            {
                if(piece==null || IsStake(piece))continue;
                Vector3 at=piece.transform.position;
                double dx=at.x-cx,dz=at.z-cz;
                if(dx*dx+dz*dz>(radius+1)*(radius+1) || !Area.Inside(flat,at.x,at.z))continue;
                string why=WhyNot(piece,player);
                if(why==null)_doomed.Add(piece);
                else if(why.Length>0)_skipped[why]=_skipped.TryGetValue(why,out int n)?n+1:1;
            }
            Tint();
        }

        // Why the hammer couldn't remove this piece from where the player stands (null: it could; "": not a structure, not counted).
        private static string WhyNot(Piece piece,Player player)
        {
            ZNetView view=piece.GetComponent<ZNetView>();
            if(view==null || !view.IsValid())return "";
            if(piece.GetComponentInChildren<Ship>()!=null || piece.GetComponentInChildren<Vagon>()!=null)return "ships or carts";
            if(piece.GetComponent<Character>()!=null)return "";
            if(!piece.IsPlacedByPlayer())return "not built by a player";
            if(!piece.m_canBeRemoved)return "that can't be removed";
            Vector3 at=piece.transform.position;
            if(Location.IsInsideNoBuildLocation(at))return "in a no-build zone";
            if(!PrivateArea.CheckAccess(at,0,false,false))return "protected by a ward";
            if(!player.NoCostCheat() && piece.m_craftingStation!=null && ZoneSystem.instance!=null && !ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoWorkbench) &&
                !CraftingStation.HaveBuildStationInRange(piece.m_craftingStation.m_name,player.transform.position))return "needing a crafting station near you";
            if(!piece.CanBeRemoved())return "chests with items";
            return null;
        }

        private void Tint()
        {
            MaterialMan materials=MaterialMan.instance;
            if(materials==null)return;
            var keep=new HashSet<Piece>(_doomed);
            foreach(Piece piece in _tinted.Where(p=>!keep.Contains(p)).ToList())Untint(piece);
            // Every scan, not once: the hammer's hover highlight puts a piece's own colour back.
            foreach(Piece piece in _doomed)
            {
                materials.SetValue(piece.gameObject,ColorId,DoomedColor);
                materials.SetValue(piece.gameObject,EmissionId,DoomedColor*0.25f);
                _tinted.Add(piece);
            }
        }
        private void Untint(Piece piece)
        {
            _tinted.Remove(piece);
            if(piece==null || MaterialMan.instance==null)return;
            MaterialMan.instance.ResetValue(piece.gameObject,ColorId);
            MaterialMan.instance.ResetValue(piece.gameObject,EmissionId);
        }
        private void Untint(){foreach(Piece piece in _tinted.ToList())Untint(piece);}

        private void Ask(Player player)
        {
            if(!Close(player))return;
            var (title,body)=Area.Question(_doomed.Count,_skipped);
            if(_doomed.Count==0){player.Message(MessageHud.MessageType.Center,title+(_skipped.Count>0?": "+string.Join(", ",_skipped.Select(s=>$"{s.Value} {s.Key}")):""));return;}
            _asking=true;
            UnifiedPopup.Push(new YesNoPopup(title,body,()=>{UnifiedPopup.Pop();_asking=false;Demolish();},()=>{UnifiedPopup.Pop();_asking=false;},false));
        }
        private void Demolish()
        {
            Player player=Player.m_localPlayer;
            if(player==null || !_closed)return;
            Scan(player);   // what's there now, not when the question was asked
            // Workshops last: pieces needing a crafting station near are only removed while it stands.
            foreach(Piece piece in _doomed.OrderBy(p=>p.GetComponent<CraftingStation>()!=null?1:0).ThenByDescending(p=>p.transform.position.y))_queue.Enqueue(piece);
            _removed=_kept=0;
            _bolts.Clear();
            _strikeAt=0;
            if(_lightning.Value && _doomed.Count>0)
            {
                _bolts.AddRange(BoltPoints(_doomed));
                Buildup(_bolts[0]);
                _strikeAt=Time.time+_lightningDelay.Value;
            }
            else Untint();
        }
        // Thor's lightning, as the Obliterator calls it: its lever effect first (distant thunder, sparks), then its bolt (lightningAOE)
        // on the tops of what comes down, one per 12 m or so, at most six, and the building goes as they hit. Both are network objects,
        // so everyone near sees and hears them; the bolt's damage (400 lightning, dealt only by its owner) is taken off here.
        private static List<Vector3> BoltPoints(List<Piece> doomed)
        {
            var points=new List<Vector3>();
            foreach(Piece piece in doomed.Where(p=>p!=null).OrderByDescending(p=>p.transform.position.y))
            {
                Vector3 at=piece.transform.position;
                if(points.Any(q=>new Vector2(q.x-at.x,q.z-at.z).sqrMagnitude<12*12))continue;
                points.Add(at);
                if(points.Count==6)break;
            }
            return points;
        }
        internal static bool Buildup(Vector3 at)=>Spawn("vfx_lever",at)!=null;
        internal static bool Bolt(Vector3 at)
        {
            GameObject bolt=Spawn("lightningAOE",at);
            if(bolt==null)return false;
            foreach(Aoe aoe in bolt.GetComponentsInChildren<Aoe>(true))Object.DestroyImmediate(aoe);
            return true;
        }
        // Louder for us: full volume three times as far out (the clips already play at full volume).
        private static GameObject Spawn(string name,Vector3 at)
        {
            GameObject prefab=ZNetScene.instance!=null?ZNetScene.instance.GetPrefab(name):null;
            if(prefab==null)return null;
            GameObject made=Object.Instantiate(prefab,at,Quaternion.identity);
            foreach(AudioSource audio in made.GetComponentsInChildren<AudioSource>(true))
            {
                audio.minDistance*=3;
                audio.maxDistance=Mathf.Max(audio.maxDistance,audio.minDistance*2);
            }
            return made;
        }
        private void RunQueue(Player player)
        {
            if(_queue.Count==0)return;
            if(Time.time<_strikeAt)return;   // the buildup
            if(_bolts.Count>0){foreach(Vector3 at in _bolts)Bolt(at);_bolts.Clear();Untint();}
            for(int i=0;i<_perFrame.Value && _queue.Count>0;i++)
            {
                Piece piece=_queue.Dequeue();
                if(piece==null)continue;   // already gone (fell with what held it up)
                if(WhyNot(piece,player)!=null){_kept++;continue;}
                Remove(piece);_removed++;
            }
            if(_queue.Count>0)return;
            player.Message(MessageHud.MessageType.Center,(_removed==1?"Demolished 1 structure":$"Demolished {_removed} structures")+(_kept>0?$" ({_kept} left standing)":""));
            ClearOutline();
        }
        // As the hammer removes a piece (Player.RemovePiece), without its ray, stamina or durability.
        private static void Remove(Piece piece)
        {
            piece.GetComponent<IRemoved>()?.OnRemoved();
            WearNTear wear=piece.GetComponent<WearNTear>();
            if(wear!=null){wear.Remove();return;}
            ZNetView view=piece.GetComponent<ZNetView>();
            view.ClaimOwnership();
            piece.DropResources();
            piece.m_placeEffect.Create(piece.transform.position,piece.transform.rotation,piece.gameObject.transform);
            ZNetScene.instance.Destroy(piece.gameObject);
        }
    }
}
