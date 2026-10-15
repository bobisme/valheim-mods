using System.Collections.Generic;

namespace BuildKit
{
    // A carved or woven piece's look as plain lists (points, normals, uv, vertex colour, triangles by material slot), turned into a
    // mesh in the game. Slots: Main (shaded per piece by vertex colour), Boards (the game's own wood), Trim (the frame or the withies).
    public sealed class PartMesh
    {
        public const int Main = 0, Boards = 1, Trim = 2, Slots = 3; // (material slots)
        public readonly List<float> V = new List<float>(), N = new List<float>(), Uv = new List<float>(), C = new List<float>();
        public readonly List<int>[] T = new List<int>[Slots];
        public PartMesh() { for (int i = 0; i < Slots; i++) T[i] = new List<int>(); }
        public int Count => V.Count / 3;

        // (a point, its normal's parts, and faces shown toward a way)
        public int AddPoint(float x, float y, float z, float nx, float ny, float nz, float u, float v, float r, float g, float b)
        {
            V.Add(x); V.Add(y); V.Add(z); N.Add(nx); N.Add(ny); N.Add(nz); Uv.Add(u); Uv.Add(v); C.Add(r); C.Add(g); C.Add(b);
            return Count - 1;
        }
        public float Nx(int i) => N[3 * i]; public float Ny(int i) => N[3 * i + 1]; public float Nz(int i) => N[3 * i + 2];
        public float X(int i) => V[3 * i]; public float Y(int i) => V[3 * i + 1]; public float Z(int i) => V[3 * i + 2];
        public void QuadFacing(int slot, int a, int b, int c, int d, float nx, float ny, float nz) { TriFacing(slot, a, b, c, nx, ny, nz); TriFacing(slot, a, c, d, nx, ny, nz); }
        public void TriFacing(int slot, int a, int b, int c, float nx, float ny, float nz)
        {
            float ux = V[3 * b] - V[3 * a], uy = V[3 * b + 1] - V[3 * a + 1], uz = V[3 * b + 2] - V[3 * a + 2];
            float vx = V[3 * c] - V[3 * a], vy = V[3 * c + 1] - V[3 * a + 1], vz = V[3 * c + 2] - V[3 * a + 2];
            float cx = uy * vz - uz * vy, cy = uz * vx - ux * vz, cz = ux * vy - uy * vx;
            if (cx * nx + cy * ny + cz * nz >= 0) T[slot].AddRange(new[] { a, b, c }); else T[slot].AddRange(new[] { a, c, b }); // (as the turf's, which shows)
        }
    }
}
