using System;
using System.Linq;

namespace PolygonLeveler
{
    // Only selected height data is captured: paint and neighboring vertices remain independent.
    internal sealed class TerrainSnapshot
    {
        private readonly int[] _indices;
        private readonly float[] _levels, _smooth;
        private readonly bool[] _modified;

        internal TerrainSnapshot(int[] indices, float[] levels, float[] smooth, bool[] modified)
        {
            if (levels.Length != smooth.Length || levels.Length != modified.Length ||
                indices.Length == 0 || indices.Length > Geometry.MaxVertices ||
                indices.Any(i => i < 0 || i >= levels.Length) || indices.Distinct().Count() != indices.Length)
                throw new ArgumentException("Invalid terrain snapshot.");
            _indices = (int[])indices.Clone();
            _levels = indices.Select(i => levels[i]).ToArray();
            _smooth = indices.Select(i => smooth[i]).ToArray();
            _modified = indices.Select(i => modified[i]).ToArray();
        }

        internal bool Matches(float[] levels, float[] smooth, bool[] modified) =>
            _indices.All(i => i < levels.Length && i < smooth.Length && i < modified.Length) &&
            Enumerable.Range(0, _indices.Length).All(n => levels[_indices[n]].Equals(_levels[n]) &&
                smooth[_indices[n]].Equals(_smooth[n]) && modified[_indices[n]] == _modified[n]);

        internal void Restore(float[] levels, float[] smooth, bool[] modified)
        {
            if (_indices.Any(i => i >= levels.Length || i >= smooth.Length || i >= modified.Length))
                throw new ArgumentException("Terrain grid changed.");
            for (int n = 0; n < _indices.Length; n++)
            { int i = _indices[n]; levels[i] = _levels[n]; smooth[i] = _smooth[n]; modified[i] = _modified[n]; }
        }
    }
}
