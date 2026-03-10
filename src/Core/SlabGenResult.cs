using System.Collections.Generic;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace BricsCadRc.Core
{
    /// <summary>Wynik generowania ukladu pretow z polilinii.</summary>
    public class SlabGenResult
    {
        public List<ObjectId> BarIds { get; set; } = new List<ObjectId>();

        /// <summary>Lewy-dolny naroznik wygenerowanych pretow (w jednostkach rysunku).</summary>
        public Point3d MinPoint { get; set; }

        /// <summary>Prawy-gorny naroznik wygenerowanych pretow (w jednostkach rysunku).</summary>
        public Point3d MaxPoint { get; set; }

        public int Count => BarIds.Count;
    }
}
