using System;

namespace Jarldom
{
    // Coming home by sea at night: a ship within 250 m of the colony's edge, under way and heading in, between dusk and dawn, once a
    // return (ten minutes of the world's clock before the next). It lights the colony's beacon.
    public static class Return
    {
        public const float Reach = 250f, MinSpeed = 1.5f, Heading = 0.5f; // (heading within 60 degrees of the colony)
        public const double Cooldown = 600; // (world seconds)
        public static bool Night(double hour) => hour >= Schedule.HallAt || hour < Schedule.WakeAt;
        public static bool Detect(float sx, float sz, float vx, float vz, float cx, float cz, float radius, bool night, double now, double last)
        {
            if (!night || last > 0 && now - last < Cooldown) return false;
            float dx = cx - sx, dz = cz - sz, dist = (float)Math.Sqrt(dx * dx + dz * dz);
            if (dist - radius > Reach || dist < radius * 0.5f) return false; // (too far out yet, or already in)
            float speed = (float)Math.Sqrt(vx * vx + vz * vz);
            if (speed < MinSpeed || dist < 0.01f) return false;
            return (vx * dx + vz * dz) / (speed * dist) >= Heading;
        }
    }
}
