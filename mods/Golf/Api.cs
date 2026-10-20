using System.Linq;
using UnityEngine;

namespace MeadowGolf
{
    // For other mods (Jarldom's caddie), found by reflection: the type "MeadowGolf.Api". Polled, read-only: nothing here changes a ball.
    // Rules: bump Version when a method is added; never change a method's signature.
    public static class Api
    {
        public const int Version=1;
        // A player's ball in play (their newest loaded, not in a closed match): where it lies, the strokes taken, whether it has come to
        // rest, whether it's holed. False with none loaded.
        public static bool BallOf(long playerId,out Vector3 at,out int strokes,out bool still,out bool done)
        {
            GolfBall ball=Ball(playerId);
            at=ball!=null?ball.transform.position:Vector3.zero;strokes=ball?.Strokes??0;still=ball!=null&&ball.Still;done=ball!=null&&ball.Done;
            return ball!=null;
        }
        // The cup that ball is played to (Vector3.zero if none is known).
        public static Vector3 CupOf(long playerId)
        {
            ZDO cup=Ball(playerId)?.Data is ZDO d?ZDOMan.instance?.GetZDO(d.GetZDOID(GolfWorld.CupKey)):null;
            return cup!=null?cup.GetPosition():Vector3.zero;
        }
        // Holding the golf club in the right hand.
        public static bool HoldsClub(Player player)=>player!=null&&Prefabs.IsClub(Prefabs.Right(player));
        private static GolfBall Ball(long playerId)=>GolfBall.Loaded.Where(b=>b!=null&&b.Data!=null&&!b.Closed&&b.Data.GetLong(GolfWorld.PlayerKey,0)==playerId)
            .OrderByDescending(b=>b.Data.GetLong("bob_golf_sequence",0)).FirstOrDefault();
    }
}
