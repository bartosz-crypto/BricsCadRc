// TEMPORARY - REMOVE BEFORE RELEASE

using System;
using System.Globalization;
using Bricscad.ApplicationServices;
using BricsCadRc.Core;
using BricsCadRc.Dialogs;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;
using Polyline = Teigha.DatabaseServices.Polyline;

namespace BricsCadRc.Commands
{
    // TEMPORARY - REMOVE BEFORE RELEASE
    public class TestCommands
    {
        private const string TestLayer = "RC_TEST";

        [CommandMethod("RC_TEST_GEOMETRY", CommandFlags.Modal)]
        public void TestGeometry()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed  = doc.Editor;
            var db  = doc.Database;

            // ── HARDCODED DUMP: shape 51, A=400 B=300 C=160 d=12 ─────────────────
            {
                var rawPts = BarGeometryBuilder.GetLocalPoints(
                    "51", new[] { 400.0, 300.0, 160.0 }, 12.0);
                ed.WriteMessage($"\n--- GetLocalPoints(\"51\", [400,300,160], d=12) ---");
                ed.WriteMessage($"\nCount = {rawPts.Count}");
                for (int i = 0; i < rawPts.Count; i++)
                    ed.WriteMessage(
                        $"\n  [{i:D2}]  X={rawPts[i].X.ToString("F2", CultureInfo.InvariantCulture)}" +
                        $"  Y={rawPts[i].Y.ToString("F2", CultureInfo.InvariantCulture)}");
                ed.WriteMessage($"\n--- END DUMP ---\n");
            }
            // ─────────────────────────────────────────────────────────────────────

            // 1. Otwórz ShapePickerDialog
            var dlg = new ShapePickerDialog();
            if (Application.ShowModalWindow(dlg) != true) return;

            BarShape  shape  = dlg.SelectedShape;
            double[]  pvals  = dlg.ParameterValues;
            double    dia    = dlg.Diameter;

            if (shape == null || pvals == null) return;

            // 2. Wypisz info w command line
            ed.WriteMessage($"\n--- RC_TEST_GEOMETRY ---");
            ed.WriteMessage($"\nShape code : {shape.Code}  ({shape.Name})");
            ed.WriteMessage($"\nDiameter   : {dia} mm");
            for (int i = 0; i < shape.Parameters.Length; i++)
                ed.WriteMessage($"\n{shape.Parameters[i]}          : {pvals[i]:F2} mm");

            // 3. Zbuduj polilinię w (0,0,0) kierunek (1,0,0)
            Polyline pline = SingleBarEngine.Build(
                shape, pvals, dia,
                new Point3d(0, 0, 0),
                new Vector3d(1, 0, 0));

            // Wypisz węzły
            int n = pline.NumberOfVertices;
            ed.WriteMessage($"\nVertex count : {n}");
            for (int i = 0; i < n; i++)
            {
                Point2d p = pline.GetPoint2dAt(i);
                ed.WriteMessage(
                    $"\n  [{i}] ({p.X.ToString("F2", CultureInfo.InvariantCulture)}, " +
                          $"{p.Y.ToString("F2", CultureInfo.InvariantCulture)})");
            }

            // TEMPORARY — detailed vertex dump for shape codes 51 and 63
            if (shape.Code == "51" || shape.Code == "63")
            {
                ed.WriteMessage($"\n--- TEMPORARY VERTEX DUMP (shape {shape.Code}) ---");
                int vtxCount = pline.NumberOfVertices;
                for (int i = 0; i < vtxCount; i++)
                {
                    Point2d pt = pline.GetPoint2dAt(i); // TEMPORARY
                    ed.WriteMessage(                     // TEMPORARY
                        $"\n  vtx[{i}]  X={pt.X.ToString("F4", CultureInfo.InvariantCulture)}" +
                        $"  Y={pt.Y.ToString("F4", CultureInfo.InvariantCulture)}"); // TEMPORARY
                }
                ed.WriteMessage($"\n--- END VERTEX DUMP ---"); // TEMPORARY
            }
            // END TEMPORARY

            // 4. Dodaj do rysunku na warstwie RC_TEST
            using var tr = db.TransactionManager.StartTransaction();

            EnsureTestLayer(db, tr);
            pline.Layer = TestLayer;
            pline.ColorIndex = 1; // czerwony — łatwy do zobaczenia

            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            space.AppendEntity(pline);
            tr.AddNewlyCreatedDBObject(pline, true);

            tr.Commit();

            ed.WriteMessage($"\nPolyline drawn on layer '{TestLayer}'.\n");
        }

        private static void EnsureTestLayer(Database db, Transaction tr)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(TestLayer)) return;

            lt.UpgradeOpen();
            var rec = new LayerTableRecord { Name = TestLayer };
            lt.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }
    }
}
