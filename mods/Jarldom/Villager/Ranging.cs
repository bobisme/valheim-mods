using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // Ranging for game: a hunter whose grounds hold nothing to hunt, and the stores short of meat, goes out past the colony's edge a while
    // (a lookout 30 m beyond it, a new way each time) to find a deer or a boar, brings it down and carries it home. Nothing seen, home
    // again, and not out again for a while. (The world past a player's own ground stands still: no further than the scout goes.)
    internal sealed class RangeTask : Task
    {
        public const float Beyond = 30f, Farthest = 120f, Sight = 40f, Every = 480f;
        public static readonly string[] Meats = { "RawMeat", "DeerMeat", "NeckTail", "WolfMeat", "LoxMeat", "SerpentMeat", "HareMeat", "ChickenMeat", "BugMeat", "AsksvinMeat", "VoltureMeat", "BoneMawSerpentMeat" };
        public static int Meat(Colony c) => Meats.Sum(m => Stock.Count(c, m));
        // Out today? Daylight, no raid, a bow (or a weapon) to hunt with, and less meat in the stores than a day's eating for two of every three.
        public static bool Wanted(Brain b)
        {
            Colony c = b.Colony;
            if (c == null || !c.IsOwner || b.S.Prof != Prof.Hunter || Time.time < b.NoRangeUntil || c.RaidOn) return false;
            if (c.Hour < 7 || c.Hour > 16.5) return false;
            if (Gear.Tool(b, "bow") == null && !Gear.CanFight(b)) return false;
            return Meat(c) < Mathf.Max(6, c.Population * 2 / 3);
        }
        private Vector3 _out; private bool _back; private float _until, _nextLook, _lookUntil;
        public override void Start(Brain b)
        {
            Colony c = b.Colony;
            b.NoRangeUntil = Time.time + Every;
            // (game seen from the edge, that no one's after yet: that way; else each time another way, the golden angle round, and two
            // hunters not the same way)
            var taken = c.Board.All.Where(x => x.Kind == "hunt" && (x.State == OrderState.Claimed || x.State == OrderState.Blocked)).Select(x => x.Target).ToList();
            Character seen = Character.GetAllCharacters().Where(x => Work.Prey(x) && Utils.DistanceXZ(x.transform.position, c.Position) < Farthest + 20f && x.GetComponent<ZNetView>()?.IsValid() == true
                    && !taken.Contains(Work.Key(x.GetComponent<ZNetView>())) && !Others(b, x.transform.position))
                .OrderBy(x => Utils.DistanceXZ(x.transform.position, b.Pos)).FirstOrDefault();
            float angle = (b.S.Count("ranged") * 137.5f + (b.S.Vid.GetHashCode() & 0xff) * 1.41f) % 360f;
            Vector3 dir = seen != null ? seen.transform.position - c.Position : Quaternion.Euler(0, angle, 0) * Vector3.forward; dir.y = 0; dir.Normalize();
            Vector3 look = seen != null ? seen.transform.position : c.Position + dir * Mathf.Min(c.Radius + Beyond, Farthest);
            _out = Colony.Spot(look, 12f) ?? look;
            Doing = "ranging for game to the " + ScoutTask.Compass(dir);
            _until = Time.time + 200f;
            b.S.Bump("ranged"); b.Dirty();
            b.Move.To(_out, 4f);
        }
        public override void Tick(Brain b)
        {
            Colony c = b.Colony;
            if (c == null || c.RaidOn || Time.time > _until) { Done = true; return; }
            if (!_back && Time.time >= _nextLook)
            {
                _nextLook = Time.time + 1.5f;
                var passed = c.Board.All.Where(x => x.Kind == "hunt" && (x.State == OrderState.Claimed || x.State == OrderState.Blocked)).Select(x => x.Target).ToList(); // (another's, or let be)
                Character prey = Character.GetAllCharacters().Where(x => Work.Prey(x) && Vector3.Distance(x.transform.position, b.Pos) < Sight && x.GetComponent<ZNetView>()?.IsValid() == true && !passed.Contains(Work.Key(x.GetComponent<ZNetView>())))
                    .OrderBy(x => Vector3.Distance(x.transform.position, b.Pos)).FirstOrDefault();
                if (prey != null)
                {
                    Vector3 p = prey.transform.position;
                    int id = c.Board.Post("hunt", Work.Key(prey.GetComponent<ZNetView>()), Prof.Hunter, 4, p.x, p.y, p.z, c.Now, Zone, "ranging");
                    Order o = c.Board.All.FirstOrDefault(x => x.Id == id);
                    if (o != null && c.Board.Claim(id, b.S.Vid, c.Now, out _))
                    {
                        b.Say(new[] { "There. Quiet now.", "Fresh tracks... there it is.", "Meat for the pot, if I'm quick.", "Hold still, hold still.", "There's supper." }[Random.Range(0, 5)], 4);
                        Stop(b); var hunt = new WorkTask(o); b.Task = hunt; hunt.Start(b); // (the hunt itself as the grounds' are hunted, and the kill carried home)
                        return;
                    }
                }
            }
            if (!_back)
            {
                if (b.Move.Failed || b.Move.Arrived || !b.Move.Moving && b.Move.Distance < 8f)
                {
                    // (a look about out there before turning home)
                    if (_lookUntil == 0) _lookUntil = Time.time + 20f;
                    if (Time.time < _lookUntil) { b.Ai.LookTowards(Quaternion.Euler(0, Time.time * 20f, 0) * Vector3.forward); return; }
                    _until = Time.time + 90f; // (the walk home)
                    _back = true; Doing = "coming back from ranging, empty-handed"; b.Move.To(c.Position + (b.Pos - c.Position).normalized * 8f, 4f);
                }
                else if (!b.Move.Moving) b.Move.To(_out, 4f);
                return;
            }
            if (b.Move.Arrived || b.Move.Failed || Utils.DistanceXZ(b.Pos, c.Position) < 14f) Done = true;
            else if (!b.Move.Moving) b.Move.To(c.Position, 6f);
        }
        // (another hunter out ranging that way already)
        private static bool Others(Brain b, Vector3 at) => Villagers.All.Any(o => o != b && o.Alive && o.S.Colony == b.S.Colony && o.Task is RangeTask r && Utils.DistanceXZ(r._out, at) < Sight);
        // (the zone a ranging hunt is posted under: the hunt's reach runs past the colony's edge for it)
        public const string Zone = "range";
    }
}
