using UnityEngine;

namespace Jarldom
{
    // A caddie (with Meadow Golf): a villager following the jarl as caddie, while the jarl has a ball in play near them and the club in
    // hand. They keep off to one side behind the jarl (out of the line of the shot), hand over the club at the ball with a word on what
    // the lie wants (drive, chip or putt, by the distance to the cup), and when the ball comes to rest have their say on the shot: a
    // heckle for one that came no nearer, or cost a stroke in the water; praise for one that went close; a cheer for the cup. All of it
    // only looked at, never touched: Golf's own rules play the ball.
    internal static class Caddie
    {
        public const float Near = 40f, AtBall = 3.5f;
        internal sealed class State
        {
            public int Strokes = -1, Handed = -1; public bool Pending; public float ShotAt, Before = -1, NextWord; public string Doing = "";
        }
        public static bool Is(Brain b) => b.View.GetZDO().GetBool(Follow.CaddieKey, false);

        // From the follow task: true while caddying (the follow's own walking left off).
        public static bool Tick(Brain b, Player l, State st)
        {
            if (!Is(b) || !Compat.GolfPresent) return false;
            long id = l.GetPlayerID();
            if (!Compat.GolfBall(id, out Vector3 ball, out int strokes, out bool still, out bool done) || Utils.DistanceXZ(ball, l.transform.position) > Near) { st.Strokes = -1; st.Pending = false; return false; }
            Vector3 cup = Compat.GolfCup(id);
            float toCup = cup != Vector3.zero ? Utils.DistanceXZ(ball, cup) : -1;
            st.Doing = $"caddying for {l.GetPlayerName()}";
            if (st.Strokes < 0 || strokes < st.Strokes) { st.Strokes = strokes; st.Handed = -1; st.Pending = false; st.Before = toCup; } // (a new hole, a new card)
            // (a shot taken: judged once the ball is at rest)
            if (strokes > st.Strokes && !st.Pending) { st.Pending = true; st.ShotAt = Time.time; st.Strokes = strokes; }
            if (st.Pending && (done || still) && Time.time - st.ShotAt > 1.5f)
            {
                st.Pending = false;
                bool penalty = strokes > st.Strokes; // (strokes again since the shot: the water's, back to the lie)
                st.Strokes = strokes;
                Judge(b, l, done, penalty, st.Before, toCup);
                st.NextWord = Time.time + 4f; // (their say on the shot heard before the club's handed again)
                if (!done) st.Before = toCup;
            }
            else if (!st.Pending && still && !done) st.Before = toCup;
            // (where to stand: off the jarl's left, a little behind; or up to them at the ball, to hand the club)
            Vector3 lp = l.transform.position;
            bool hand = still && !done && !st.Pending && Time.time > st.NextWord && st.Handed != strokes && Compat.HoldsClub(l) && Utils.DistanceXZ(lp, ball) < AtBall;
            Vector3 spot = hand ? lp - l.transform.right * 1.3f : lp - l.transform.forward * 2.5f - l.transform.right * 2.2f;
            float d = Utils.DistanceXZ(b.Pos, spot);
            if (d > 1.2f) { if (!b.Move.Moving || Vector3.Distance(b.Move.Goal, spot) > 1.5f) b.Move.To(spot, 0.8f, d > 12f); return true; }
            if (b.Move.Moving) b.Move.Stop();
            Vector3 look = (hand ? lp : ball) - b.Pos; look.y = 0; if (look.sqrMagnitude > 0.01f) b.Ai.LookTowards(look.normalized);
            if (hand)
            {
                st.Handed = strokes;
                Body_.Anim(b.Body)?.SetTrigger("interact");
                b.Say(Advice(toCup, strokes), 4);
            }
            return true;
        }

        private static string Pick(string[] lines) => lines[Random.Range(0, lines.Length)];
        // The word with the club: what the lie wants.
        private static string Advice(float toCup, int strokes)
        {
            if (strokes == 0) return Pick(new[] { "Your club, jarl. Drive it.", "First shot. Make it a long one.", "Here. Odin's watching.", "Your club. Straight down the meadow." });
            if (toCup < 0) return Pick(new[] { "Your club, jarl.", "Here. Make it a good one.", "Your club. I'm watching." }); // (the cup not loaded here)
            if (toCup > 45f) return Pick(new[] { "Still a long way. Drive it.", "Another drive, jarl. Hit it hard.", "Your club. Long and straight." });
            if (toCup > 10f) return Pick(new[] { "A chip, I'd say.", "Chip it up close.", "Gently now. A chip.", "Your club. Loft it onto the grass." });
            return Pick(new[] { "Just a putt. Easy.", "Putt it. Softly, jarl.", "Read the slope. Then putt.", "A short one. Don't miss it." });
        }
        // The word after the shot.
        private static void Judge(Brain b, Player l, bool holed, bool penalty, float before, float after)
        {
            string name = l.GetPlayerName();
            if (holed) { Body_.Anim(b.Body)?.SetTrigger("emote_cheer"); b.Say(Pick(new[] { "In the cup! Skål!", "In! The skalds will hear of that.", $"In it goes! Well struck, {name}.", "In the cup. Next hole, jarl." }), 5); return; }
            if (penalty) { b.Say(Pick(new[] { "Into the water. That's a stroke gone.", "Wet ball, jarl. Ægir thanks you.", "The fish have it. Back you go.", "Did you mean to feed the fish?" }), 5); return; }
            if (before < 0 || after < 0) return;
            if (after > before - 1f) { Body_.Anim(b.Body)?.SetTrigger("emote_laugh"); b.Say(Pick(new[] { "No nearer than before, jarl.", "My grandmother hits further, and she's in Valhalla.", "Was that a swing or a sneeze?", "The cup is the other way, jarl.", "A troll would have done better. Blindfolded." }), 5); return; }
            if (after < 3f || after < before * 0.25f) b.Say(Pick(new[] { "Close! Well struck.", "Now that's a shot.", "Right up by the cup!", "Ha! Odin guided that one." }), 4);
        }
    }
}
