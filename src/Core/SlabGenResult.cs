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

        /// <summary>Punkty przyczepienia dotow na linii prowadzacej (lewy koniec kazdego preta poziomego / gorny pionowego).</summary>
        public List<Point3d> LeaderTickPoints { get; set; } = new List<Point3d>();
    }
}
