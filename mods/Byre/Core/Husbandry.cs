using System;

namespace Byre
{
    // The herd's rules, with no Unity in them: when wool or milk is ready again, how much an animal gives, who runs from whom, who walks to
    // the trough. The animal's ZDO keeps only when it was last shorn or milked (one long); everything else is worked out from that here.
    public static class Husbandry
    {
        public const float HungryFactor = 2f;   // (a hungry animal grows its wool, makes its milk, this many times slower)
        public const float TroughReach = 12f;   // (a hungry tame animal this near a trough with its food in walks to it)
        public const float TroughEatRange = 1.6f;

        // Seconds for the product to come back: the kind's period, longer while the animal is hungry.
        public static double Period(Species s, float dayLength, bool fed) => s.PeriodDays * dayLength * (fed ? 1 : HungryFactor);

        // Ready to shear or milk? last: game seconds when it last was (0: never: a wild-born animal comes in full fleece). A clock that went
        // back (a world's time set back by an admin) counts as ready: the animal is never locked away till the clock catches up.
        public static bool Ready(double last, double now, double period) => last <= 0 || now < last || now - last >= period;
        public static bool Ready(Species s, double last, double now, float dayLength, bool fed) => Ready(last, now, Period(s, dayLength, fed));

        // Seconds still to wait (0 when ready).
        public static double Remaining(Species s, double last, double now, float dayLength, bool fed) =>
            Ready(s, last, now, dayLength, fed) ? 0 : Period(s, dayLength, fed) - (now - last);

        // How much one harvest gives: fed, from YieldMin to YieldMax (roll from 0 to 1); hungry, YieldHungry.
        public static int Yield(Species s, bool fed, double roll)
        {
            if (!fed) return s.YieldHungry;
            int span = s.YieldMax - s.YieldMin + 1;
            return s.YieldMin + Math.Min(span - 1, Math.Max(0, (int)(roll * span)));
        }

        // Only a grown, tamed animal can be shorn, milked or plucked.
        public static bool CanHarvest(bool tamed, bool young) => tamed && !young;

        // A wild animal runs from a player this near (half as near for one crouching); a tame one never does.
        public static bool Flees(Species s, bool tamed, float distance, bool crouching) => !tamed && distance < (crouching ? s.FleeRange / 2 : s.FleeRange);

        // A tame, hungry animal not already eating from the ground walks to a trough within reach.
        public static bool SeeksTrough(bool tamed, bool hungry, float distance) => tamed && hungry && distance <= TroughReach;

        // ---- S2: the goose's guard, the pig's truffles ----
        public const float GuardRange = 8f;     // (a tame goose goes for a stranger this near)
        public const float ChaseStop = 1.6f;    // (and stops this far short of it: a honk in its face, no harm)
        public const float ChaseGiveUp = 12f;   // (one that gets this far off is let go)
        public const float ChaseSeconds = 5f, ChaseRest = 10f;

        // A stranger to a tame grown goose: a player who has never petted it, or a wild creature (not a tame one, nothing of the players').
        public static bool Stranger(bool isPlayer, bool knownPlayer, bool tamed, bool playersSide) => isPlayer ? !knownPlayer : !tamed && !playersSide;
        // Does it go for one this near? Only a tame grown goose, only within the guard range, never while resting after a chase.
        public static bool Chases(Species s, bool tamed, bool young, bool stranger, float distance, bool resting) =>
            s != null && s.Guards && tamed && !young && stranger && !resting && distance <= GuardRange && distance > ChaseStop;
        // Still after it? Not once it's close enough, far enough or the chase has gone on long enough.
        public static bool KeepsChasing(float distance, float seconds) => distance > ChaseStop && distance < ChaseGiveUp && seconds < ChaseSeconds;

        public const float RootCheckSeconds = 60f;  // (a following pig in the forest noses about once a minute of game time)
        public const float RootGapSeconds = 180f;   // (and never finds two within three minutes)
        // A truffle this check? A tame grown pig, led (following someone), in the Black Forest, past the gap since its last; then a roll
        // (0 to 1) under one in minutes: on average one truffle each that many minutes in the forest.
        public static bool Roots(Species s, bool tamed, bool young, bool following, bool inForest, double sinceLast, double roll, float minutes) =>
            s != null && s.Roots && tamed && !young && following && inForest && sinceLast >= RootGapSeconds && minutes > 0 && roll < RootCheckSeconds / 60.0 / minutes;

        // Game hours (a day is 24 of them) for the hover: "5 h", "1 d 4 h".
        public static string Hours(double seconds, float dayLength)
        {
            int hours = (int)Math.Ceiling(Math.Max(0, seconds) / (dayLength / 24.0));
            if (hours < 24) return hours + " h";
            return hours / 24 + " d" + (hours % 24 > 0 ? " " + hours % 24 + " h" : "");
        }

        // ZNet ticks (DateTime ticks) to game seconds, as the ZDO keeps them.
        public static double Seconds(long ticks) => ticks / 1e7;
        public static long Ticks(double seconds) => (long)(seconds * 1e7);
    }
}
