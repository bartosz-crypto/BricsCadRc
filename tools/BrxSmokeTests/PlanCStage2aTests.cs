// PLAN C ETAP 2a — testy dymne warstwy BarEntity / BarXData w prawdziwym BricsCAD V25.
// Uruchamiane skryptem planc-e2a.scr (run-planc-e2a.ps1). S1–S4 z docs/PLAN_C_etap2.md (sekcja 6.1).
//
// Dostęp do wtyczki WYŁĄCZNIE przez refleksję na zestawie BricsCadRc wczytanym NETLOAD-em — tak jak w etapie 1
// (bez typów wtyczki w czasie kompilacji — jedna kopia wtyczki w procesie).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using Bricscad.ApplicationServices;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;

namespace BricsCadRc.BrxSmokeTests
{
    public class PlanCStage2aTests
    {
        // ----------------------------------------------------------------
        // Stałe testu
        // ----------------------------------------------------------------

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private const double GeoTol      = 1e-6;   // tolerancja wierzchołków obrysu [mm]
        private const string ResultsFile = "planc-e2a-results.txt";
        private const string Prefix      = "[RC E2A]";
        private const string XApp        = "RC_SINGLE_BAR";
        private const int    HeadLen     = 14;     // [0]–[13]
        // W4d (runda 2): pręt z PlaceBar ma znacznik geometrii W4d — polilinia [14] = 1, [15] = 2021; pręt-blok [14] = 2, [15] = 2021
        private const int    PlacedPolyLen  = 16;
        private const int    V2Len       = 15;     // [0]–[13] + [14] wersja formatu
        private const short  TcInt16     = 1070;
        private const short  TcString    = 1000;
        private const short  TcHandle    = 1005;
        private const string TestBlkName = "RC_E2A_TEST_BLK";

        private const string SingleBarType = "BricsCadRc.Core.SingleBarEngine";
        private const string EngineType    = "BricsCadRc.Core.TestBarBlockEngine";
        private const string BarEntityType = "BricsCadRc.Core.BarEntity";
        private const string BarXDataType  = "BricsCadRc.Core.BarXData";
        private const string BarDataType   = "BricsCadRc.Core.BarData";
        private const string BarKindsType  = "BricsCadRc.Core.BarKinds";

        // BarKinds (flagi) — wartości z planu
        private const int KPolyline = 1, KBlock = 2, KOther = 4;

        private static readonly string[] ExpectedSteps = { "SETUP", "S1", "S2", "S3", "S4" };

        // ----------------------------------------------------------------
        // Stan sesji testowej
        // ----------------------------------------------------------------

        private static readonly List<string> _results  = new List<string>();
        private static readonly List<string> _executed = new List<string>();
        private static int _stepStart;

        private sealed class AssertFail : System.Exception
        {
            public AssertFail(string msg) : base(msg) { }
        }

        // ================================================================
        // Komendy
        // ================================================================

        [CommandMethod("RCSMOKE_E2A_SETUP", CommandFlags.Modal)]
        public void Setup()
        {
            _results.Clear();
            _executed.Clear();

            const string step = "SETUP";
            Run(step, db =>
            {
                Check(step, "wtyczka BricsCadRc wczytana (jedna kopia)", () =>
                {
                    var all = AppDomain.CurrentDomain.GetAssemblies()
                                       .Where(a => a.GetName().Name == "NaSmartRc").ToList();
                    var p = Plugin;
                    if (all.Count != 1)
                        throw new AssertFail($"wczytane kopie BricsCadRc: {all.Count} ({string.Join("; ", all.Select(Loc))})");
                    return "Location=" + Loc(p);
                });

                Check(step, "typy etapu 2a w wtyczce (BarEntity, BarXData, BarRecordRules, BarKind, BarKinds)", () =>
                {
                    foreach (var tn in new[] { BarEntityType, BarXDataType, "BricsCadRc.Core.BarRecordRules",
                                               "BricsCadRc.Core.BarKind", BarKindsType })
                        Plugin.GetType(tn, true);
                    return "OK";
                });

                Check(step, "czyszczenie przestrzeni modelu", () =>
                {
                    int n = 0;
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                        foreach (ObjectId id in ms)
                        {
                            if (id.IsErased) continue;
                            tr.GetObject(id, OpenMode.ForWrite).Erase();
                            n++;
                        }
                        tr.Commit();
                    }
                    return "usunięto " + n.ToString(Inv);
                });

                Check(step, "SingleBarEngine.EnsureAppIdRegistered", () =>
                {
                    Call(SingleBarType, "EnsureAppIdRegistered", db);
                    return "OK";
                });
            });
        }

        // ---- S1: PlaceBar (00, 21, 44) → R0; BarEntity.Write(Read()) → R1 == R0 pole po polu ----
        [CommandMethod("RCSMOKE_E2A_S1", CommandFlags.Modal)]
        public void S1()
        {
            const string step = "S1";
            Run(step, db =>
            {
                var defs = new[]
                {
                    new { Key = "00",  Bar = NewBar("H12-91", 12, "00", 3000, 0, 0, 0, 0, 3000, false, "B1", "BOT"), At = new Point3d(0, 0, 0) },
                    new { Key = "21",  Bar = NewBar("H12-92", 12, "21", 400, 300, 450, 0, 0, 1200, true, "T2", "TOP"), At = new Point3d(0, 2000, 0) },
                    new { Key = "44",  Bar = NewBar("H16-93", 16, W4Compat.RingCode, 600, 0, 0, 0, 0, 1885, false, "B2", "BOT"), At = new Point3d(0, 4000, 0) },
                };

                foreach (var d in defs)
                {
                    var def = d;
                    ObjectId id = ObjectId.Null;
                    Check(step, def.Key + " PlaceBar", () =>
                    {
                        id = (ObjectId)Call(SingleBarType, "PlaceBar", db, def.Bar, def.At);
                        return True(!id.IsNull, "uchwyt=" + id.Handle);
                    });
                    if (id.IsNull) continue;
                    RoundTrip(step, db, def.Key, id, KPolyline, PlacedPolyLen);
                }

                // Wariant z prawdziwym LabelHandle (1005): pręt 00 + Line jako „etykieta”
                ObjectId barId = ObjectId.Null, lblId = ObjectId.Null;
                Check(step, "00+etykieta: PlaceBar + LabelHandle (1005) przez SingleBarEngine.WriteXData", () =>
                {
                    var bar = NewBar("H12-94", 12, "00", 2500, 0, 0, 0, 0, 2500, false, "B1", "BOT");
                    barId = (ObjectId)Call(SingleBarType, "PlaceBar", db, bar, new Point3d(0, 6000, 0));
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        lblId = AddEntity(tr, db, new Line(new Point3d(0, 6500, 0), new Point3d(500, 6500, 0)));
                        var pl = (Entity)tr.GetObject(barId, OpenMode.ForWrite);
                        var b = Call(SingleBarType, "ReadBarXData", pl);
                        SetProp(b, "LabelHandle", lblId.Handle.ToString());
                        Call(SingleBarType, "WriteXData", pl, b);
                        tr.Commit();
                    }
                    var r = ReadRecord(db, barId);
                    return Int("długość rekordu", r.Length, PlacedPolyLen) + "; " + Tc("[13]", r[13], TcHandle)
                           + "; " + True(SameHandle(r[13].Value, lblId.Handle), "[13]=" + Desc(r[13]) + " etykieta=" + lblId.Handle);
                });
                if (!barId.IsNull) RoundTrip(step, db, "00+etykieta", barId, KPolyline, PlacedPolyLen);
            });
        }

        // ---- S2: stare rekordy — 8 pól oraz [13] jako 1000 → Read OK → Write → 14 pól, [13] = 1005 ----
        [CommandMethod("RCSMOKE_E2A_S2", CommandFlags.Modal)]
        public void S2()
        {
            const string step = "S2";
            Run(step, db =>
            {
                // ---- rekord 8-polowy ----
                ObjectId id8 = ObjectId.Null;
                Check(step, "polilinia ze starym rekordem 8-polowym", () =>
                {
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var pl = NewPoly(new Point3d(0, 20000, 0), new Point3d(2000, 20000, 0));
                        id8 = AddEntity(tr, db, pl);
                        pl.XData = new ResultBuffer(
                            new TypedValue((int)DxfCode.ExtendedDataRegAppName,  XApp),
                            new TypedValue((int)DxfCode.ExtendedDataAsciiString, "H10-95"),
                            new TypedValue((int)DxfCode.ExtendedDataInteger16,   (short)10),
                            new TypedValue((int)DxfCode.ExtendedDataAsciiString, "00"),
                            new TypedValue((int)DxfCode.ExtendedDataReal,        2000.0),
                            new TypedValue((int)DxfCode.ExtendedDataReal,        0.0),
                            new TypedValue((int)DxfCode.ExtendedDataReal,        0.0),
                            new TypedValue((int)DxfCode.ExtendedDataAsciiString, "B2"));
                        tr.Commit();
                    }
                    return Int("długość rekordu", ReadRecord(db, id8).Length, 8);
                });
                if (!id8.IsNull)
                {
                    Check(step, "8 pól: BarEntity.Read", () =>
                    {
                        using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                        {
                            var be = OpenBar(tr, id8, OpenMode.ForRead, KPolyline);
                            if (be == null) throw new AssertFail("BarEntity.Open zwrócił null");
                            var b = Inst(be, "Read");
                            if (b == null) throw new AssertFail("Read zwrócił null");
                            return Eq("Mark", Prop(b, "Mark"), "H10-95") + "; " + Eq("Diameter", Prop(b, "Diameter"), 10)
                                 + "; " + Eq("LengthA", Prop(b, "LengthA"), 2000.0) + "; " + Eq("LayerCode", Prop(b, "LayerCode"), "B2")
                                 + "; " + Eq("Position", Prop(b, "Position"), "BOT") + "; " + Eq("TotalLength", Prop(b, "TotalLength"), 2000.0)
                                 + "; " + Eq("LabelHandle", Prop(b, "LabelHandle"), "");
                        }
                    });
                    Check(step, "8 pól: Write(Read()) → 14 pól, [13] pusty 1000", () =>
                    {
                        WriteBack(db, id8, KPolyline);
                        var r = ReadRecord(db, id8);
                        return Int("długość rekordu", r.Length, HeadLen)
                             + "; " + Tc("[8]", r[8], TcString) + " " + Eq("[8]", r[8].Value, "BOT")
                             + "; " + Eq("[11]", r[11].Value, 2000.0) + "; " + Eq("[12]", r[12].Value, (short)0)
                             + "; " + Tc("[13]", r[13], TcString) + " " + Eq("[13]", r[13].Value, "");
                    });
                }

                // ---- rekord 14-polowy z [13] jako tekst 1000 (stary rysunek) ----
                ObjectId id14 = ObjectId.Null, lblId = ObjectId.Null;
                TypedValue[] before = null;
                Check(step, "polilinia z [13] jako 1000 (tekst handle etykiety)", () =>
                {
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        lblId = AddEntity(tr, db, new Line(new Point3d(0, 22500, 0), new Point3d(500, 22500, 0)));
                        var pl = NewPoly(new Point3d(0, 22000, 0), new Point3d(3000, 22000, 0));
                        id14 = AddEntity(tr, db, pl);
                        // Stary zapis: handle tekstem, małe litery, bez wiodących zer
                        string oldHex = lblId.Handle.Value.ToString("x", Inv);
                        pl.XData = new ResultBuffer(
                            new TypedValue((int)DxfCode.ExtendedDataRegAppName,  XApp),
                            new TypedValue((int)DxfCode.ExtendedDataAsciiString, "H12-96"),
                            new TypedValue((int)DxfCode.ExtendedDataInteger16,   (short)12),
                            new TypedValue((int)DxfCode.ExtendedDataAsciiString, "00"),
                            new TypedValue((int)DxfCode.ExtendedDataReal,        3000.0),
                            new TypedValue((int)DxfCode.ExtendedDataReal,        0.0),
                            new TypedValue((int)DxfCode.ExtendedDataReal,        0.0),
                            new TypedValue((int)DxfCode.ExtendedDataAsciiString, "B1"),
                            new TypedValue((int)DxfCode.ExtendedDataAsciiString, "BOT"),
                            new TypedValue((int)DxfCode.ExtendedDataReal,        0.0),
                            new TypedValue((int)DxfCode.ExtendedDataReal,        0.0),
                            new TypedValue((int)DxfCode.ExtendedDataReal,        3000.0),
                            new TypedValue((int)DxfCode.ExtendedDataInteger16,   (short)0),
                            new TypedValue((int)DxfCode.ExtendedDataAsciiString, oldHex));
                        tr.Commit();
                    }
                    before = ReadRecord(db, id14);
                    return Int("długość rekordu", before.Length, HeadLen) + "; " + Tc("[13]", before[13], TcString)
                           + " [13]=" + Desc(before[13]);
                });
                if (!id14.IsNull && before != null)
                {
                    Check(step, "[13] 1000: BarEntity.Read → LabelHandle = X8 etykiety", () =>
                    {
                        using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                        {
                            var be = OpenBar(tr, id14, OpenMode.ForRead, KPolyline);
                            if (be == null) throw new AssertFail("BarEntity.Open zwrócił null");
                            var b = Inst(be, "Read");
                            if (b == null) throw new AssertFail("Read zwrócił null");
                            return Eq("LabelHandle", Prop(b, "LabelHandle"), lblId.Handle.Value.ToString("X8", Inv))
                                 + "; " + Eq("Mark", Prop(b, "Mark"), "H12-96");
                        }
                    });
                    Check(step, "[13] 1000: Write(Read()) → 14 pól, [13] = 1005 z tym samym handle, [1]–[12] bez zmian", () =>
                    {
                        WriteBack(db, id14, KPolyline);
                        var r = ReadRecord(db, id14);
                        var sb = new StringBuilder();
                        sb.Append(Int("długość rekordu", r.Length, HeadLen));
                        sb.Append("; ").Append(Tc("[13]", r[13], TcHandle));
                        sb.Append("; ").Append(True(SameHandle(r[13].Value, lblId.Handle), "[13]=" + Desc(r[13]) + " etykieta=" + lblId.Handle));
                        for (int i = 0; i < 13; i++) SameField(i, before[i], r[i]);
                        sb.Append("; [0]–[12] identyczne");
                        return sb.ToString();
                    });
                }
            });
        }

        // ---- S3: pręt-blok v2 — 15 pól; SingleBarEngine.WriteXData → nadal 15; WriteXDataV2 2× → 15 ----
        [CommandMethod("RCSMOKE_E2A_S3", CommandFlags.Modal)]
        public void S3()
        {
            const string step = "S3";
            Run(step, db =>
            {
                ObjectId id = ObjectId.Null;
                TypedValue[] r0 = null;
                Check(step, "TestBarBlockEngine.Create → 15 pól, [14] = Int16 2", () =>
                {
                    var t = (ValueTuple<ObjectId, string, double>)Call(EngineType, "Create", db,
                                new Point3d(0, 30000, 0), new Point3d(3000, 30000, 0), false);
                    id = t.Item1;
                    r0 = ReadRecord(db, id);
                    return "uchwyt=" + id.Handle + "; " + Int("długość rekordu", r0.Length, V2Len) + "; " + Ver2(r0);
                });
                if (id.IsNull || r0 == null) return;

                Check(step, "SingleBarEngine.WriteXData(br, bar) → nadal 15, [14] = 2, [0]–[13] bez zmian", () =>
                {
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var br = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                        var bar = Call(SingleBarType, "ReadBarXData", br);
                        Call(SingleBarType, "WriteXData", br, bar);
                        tr.Commit();
                    }
                    var r = ReadRecord(db, id);
                    for (int i = 0; i < HeadLen; i++) SameField(i, r0[i], r[i]);
                    return Int("długość rekordu", r.Length, V2Len) + "; " + Ver2(r);
                });

                Check(step, "TestBarBlockEngine.WriteXDataV2 2× → 15, [14] = 2", () =>
                {
                    for (int k = 0; k < 2; k++)
                    {
                        using (var tr = db.TransactionManager.StartTransaction())
                        {
                            var br = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                            var bar = Call(SingleBarType, "ReadBarXData", br);
                            Call(EngineType, "WriteXDataV2", br, bar);
                            tr.Commit();
                        }
                    }
                    var r = ReadRecord(db, id);
                    for (int i = 0; i < HeadLen; i++) SameField(i, r0[i], r[i]);
                    return Int("długość rekordu", r.Length, V2Len) + "; " + Ver2(r);
                });

                Check(step, "BarEntity(Block).Write(Read()) → 15, [14] = 2", () =>
                {
                    WriteBack(db, id, KBlock);
                    var r = ReadRecord(db, id);
                    for (int i = 0; i < HeadLen; i++) SameField(i, r0[i], r[i]);
                    return Int("długość rekordu", r.Length, V2Len) + "; " + Ver2(r);
                });

                Check(step, "TestBarBlockEngine.IsV2 = true po zapisach", () =>
                {
                    using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                        return True((bool)Call(EngineType, "IsV2", tr.GetObject(id, OpenMode.ForRead)), "IsV2");
                });

                // Ogon [15], [16] (przyszłe pola) — zachowany przy zapisie nagłówka
                Check(step, "ogon [15]–[16] zachowany przez SingleBarEngine.WriteXData i WriteXDataV2", () =>
                {
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var br = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                        var list = ReadRecord(br).ToList();
                        list.Add(new TypedValue((int)DxfCode.ExtendedDataAsciiString, "OGON15"));
                        list.Add(new TypedValue((int)DxfCode.ExtendedDataReal, 16.5));
                        br.XData = new ResultBuffer(list.ToArray());
                        var bar = Call(SingleBarType, "ReadBarXData", br);
                        Call(SingleBarType, "WriteXData", br, bar);
                        Call(EngineType, "WriteXDataV2", br, bar);
                        tr.Commit();
                    }
                    var r = ReadRecord(db, id);
                    return Int("długość rekordu", r.Length, 17) + "; " + Ver2(r)
                         + "; " + Eq("[15]", r[15].Value, "OGON15") + "; " + Eq("[16]", r[16].Value, 16.5);
                });
            });
        }

        // ---- S4: KindOf / From; OutlineVertices bloku obróconego o 30° i odbitego ----
        [CommandMethod("RCSMOKE_E2A_S4", CommandFlags.Modal)]
        public void S4()
        {
            const string step = "S4";
            Run(step, db =>
            {
                ObjectId plId = ObjectId.Null, v2Id = ObjectId.Null, brNoVer = ObjectId.Null, brVer1 = ObjectId.Null,
                         lineBar = ObjectId.Null, lineNo = ObjectId.Null;

                Check(step, "przygotowanie encji", () =>
                {
                    var bar = NewBar("H12-97", 12, "00", 2000, 0, 0, 0, 0, 2000, false, "B1", "BOT");
                    plId = (ObjectId)Call(SingleBarType, "PlaceBar", db, bar, new Point3d(0, 40000, 0));
                    var t = (ValueTuple<ObjectId, string, double>)Call(EngineType, "Create", db,
                                new Point3d(0, 41000, 0), new Point3d(2000, 41000, 0), false);
                    v2Id = t.Item1;

                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var btrId = EnsureTestBlock(tr, db);

                        var br1 = new BlockReference(new Point3d(0, 42000, 0), btrId);
                        brNoVer = AddEntity(tr, db, br1);
                        Call(SingleBarType, "WriteXData", br1, bar);                       // 14 pól, bez [14]

                        var br2 = new BlockReference(new Point3d(0, 43000, 0), btrId);
                        brVer1 = AddEntity(tr, db, br2);
                        Call(SingleBarType, "WriteXData", br2, bar);
                        var l2 = ReadRecord(br2).ToList();
                        l2.Add(new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)1)); // [14] = 1
                        br2.XData = new ResultBuffer(l2.ToArray());

                        var ln = new Line(new Point3d(0, 44000, 0), new Point3d(2000, 44000, 0));
                        lineBar = AddEntity(tr, db, ln);
                        Call(SingleBarType, "WriteXData", ln, bar);

                        lineNo = AddEntity(tr, db, new Line(new Point3d(0, 45000, 0), new Point3d(2000, 45000, 0)));
                        tr.Commit();
                    }
                    return "OK";
                });

                Check(step, "KindOf(polilinia RC_SINGLE_BAR) = Polyline", () => Kind(db, plId, "Polyline"));
                Check(step, "KindOf(pręt-blok v2) = Block", () => Kind(db, v2Id, "Block"));
                Check(step, "KindOf(BlockReference z RC_SINGLE_BAR bez [14]) = null", () => Kind(db, brNoVer, null));
                Check(step, "KindOf(BlockReference z [14] = Int16 1) = null", () => Kind(db, brVer1, null));
                Check(step, "KindOf(Line z RC_SINGLE_BAR) = Other", () => Kind(db, lineBar, "Other"));
                Check(step, "KindOf(Line bez XData) = null", () => Kind(db, lineNo, null));

                Check(step, "From: filtr BarKinds", () =>
                {
                    using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                    {
                        var pl = tr.GetObject(plId, OpenMode.ForRead);
                        var v2 = tr.GetObject(v2Id, OpenMode.ForRead);
                        var ln = tr.GetObject(lineBar, OpenMode.ForRead);
                        var nv = tr.GetObject(brNoVer, OpenMode.ForRead);
                        True(From(pl, KBlock) == null, "From(polilinia, Block) != null");
                        True(From(pl, KPolyline | KBlock) != null, "From(polilinia, Polyline|Block) == null");
                        True(From(v2, KPolyline) == null, "From(v2, Polyline) != null");
                        True(From(v2, KPolyline | KBlock) != null, "From(v2, Polyline|Block) == null");
                        True(From(ln, KPolyline | KBlock) == null, "From(Line, Polyline|Block) != null");
                        True(From(ln, KOther) != null, "From(Line, Other) == null");
                        True(From(nv, KPolyline | KBlock | KOther) == null, "From(blok bez [14], wszystkie) != null");
                        var be = From(v2, KBlock);
                        return Eq("Kind", Convert.ToString(Prop(be, "Kind"), Inv), "Block");
                    }
                });

                Check(step, "Open(ForWrite) obcej encji (Line bez XData) → null", () =>
                {
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var be = OpenBar(tr, lineNo, OpenMode.ForWrite, KPolyline | KBlock | KOther);
                        tr.Commit();
                        return True(be == null, "Open zwrócił pręt");
                    }
                });

                Check(step, "OutlineVertices polilinii = wierzchołki polilinii", () =>
                {
                    using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                    {
                        var pl = (Polyline)tr.GetObject(plId, OpenMode.ForRead);
                        var exp = new List<Point3d>();
                        for (int i = 0; i < pl.NumberOfVertices; i++)
                        {
                            var p = pl.GetPoint2dAt(i);
                            exp.Add(new Point3d(p.X, p.Y, 0));
                        }
                        var be = OpenBar(tr, plId, OpenMode.ForRead, KPolyline);
                        var act = Outline(be, tr);
                        return Pts(act, exp);
                    }
                });

                // Blok v2: obrót o 30° wokół (500, 41000), potem odbicie względem prostej (0,50000)–(1000,51500)
                var origin = new Point3d(0, 41000, 0);
                var rot    = Matrix3d.Rotation(30.0 * Math.PI / 180.0, Vector3d.ZAxis, new Point3d(500, 41000, 0));
                var mir    = Matrix3d.Mirroring(new Line3d(new Point3d(0, 50000, 0), new Point3d(1000, 51500, 0)));
                Check(step, "TransformBy: obrót 30° i Matrix3d.Mirroring", () =>
                {
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var br = (BlockReference)tr.GetObject(v2Id, OpenMode.ForWrite);
                        br.TransformBy(rot);
                        br.TransformBy(mir);
                        var sc = br.ScaleFactors;
                        string info = "Rotation=" + F(br.Rotation * 180 / Math.PI) + "° ScaleFactors=(" + F(sc.X) + "," + F(sc.Y) + "," + F(sc.Z) + ")";
                        tr.Commit();
                        return info;
                    }
                });
                Check(step, "OutlineVertices bloku obróconego i odbitego = oczekiwane ±1e-6", () =>
                {
                    using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                    {
                        var br  = (BlockReference)tr.GetObject(v2Id, OpenMode.ForRead);
                        var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                        var polys = new List<Polyline>();
                        foreach (ObjectId eid in btr)
                        {
                            if (eid.IsErased) continue;
                            if (tr.GetObject(eid, OpenMode.ForRead) is Polyline bp) polys.Add(bp);
                        }
                        if (polys.Count != 1) throw new AssertFail("polilinie w BTR: " + polys.Count.ToString(Inv) + " (oczekiwano 1)");
                        // Oczekiwane niezależnie od BlockTransform: lokalne → przesunięcie do origin (obrót 0) → obrót → odbicie
                        var exp = new List<Point3d>();
                        for (int i = 0; i < polys[0].NumberOfVertices; i++)
                        {
                            var p = polys[0].GetPoint2dAt(i);
                            var w = new Point3d(origin.X + p.X, origin.Y + p.Y, 0);
                            exp.Add(w.TransformBy(rot).TransformBy(mir));
                        }
                        var be  = OpenBar(tr, v2Id, OpenMode.ForRead, KBlock);
                        var act = Outline(be, tr);
                        return Pts(act, exp);
                    }
                });
                Check(step, "KindOf(v2 po obrocie i odbiciu) = Block, rekord 15", () =>
                    Kind(db, v2Id, "Block") + "; " + Int("długość rekordu", ReadRecord(db, v2Id).Length, V2Len));
            });
        }

        // ---- Raport końcowy ----
        [CommandMethod("RCSMOKE_E2A_REPORT", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void Report()
        {
            foreach (var st in ExpectedSteps)
                if (!_executed.Contains(st))
                    Add("FAIL", st, "krok nie został wykonany", "skrypt nie dotarł do komendy kroku albo ją pominął");

            int pass = _results.Count(r => r.StartsWith("PASS", StringComparison.Ordinal));
            int fail = _results.Count(r => r.StartsWith("FAIL", StringComparison.Ordinal));
            int skip = _results.Count(r => r.StartsWith("SKIP", StringComparison.Ordinal));
            Flush(new[] { "", $"PODSUMOWANIE: PASS={pass} FAIL={fail} SKIP={skip}", "END" });
            Msg($"\n{Prefix} Raport: {Path.Combine(OutDir, ResultsFile)}  PASS={pass} FAIL={fail} SKIP={skip}\n");
        }

        // ================================================================
        // Wspólne kroki testowe
        // ================================================================

        /// <summary>R0 = rekord po utworzeniu; BarEntity.Open(ForWrite).Write(Read()) → R1 == R0 pole po polu (typ + wartość).</summary>
        private static void RoundTrip(string step, Database db, string key, ObjectId id, int accept, int expLen)
        {
            TypedValue[] r0 = null;
            Check(step, key + " R0: rekord RC_SINGLE_BAR, " + expLen + " pól", () =>
            {
                r0 = ReadRecord(db, id);
                return Int("długość rekordu", r0.Length, expLen);
            });
            if (r0 == null) return;
            Check(step, key + " BarEntity.Write(Read()) → R1 == R0 (typ i wartość każdego pola)", () =>
            {
                WriteBack(db, id, accept);
                var r1 = ReadRecord(db, id);
                Int("długość R1", r1.Length, r0.Length);
                for (int i = 0; i < r0.Length; i++) SameField(i, r0[i], r1[i]);
                return "pola=" + r1.Length.ToString(Inv) + "; [13]=" + Desc(r1[13]);
            });
        }

        /// <summary>BarEntity.Open(ForWrite) → Write(Read()) w jednej transakcji.</summary>
        private static void WriteBack(Database db, ObjectId id, int accept)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var be = OpenBar(tr, id, OpenMode.ForWrite, accept);
                if (be == null) throw new AssertFail("BarEntity.Open(ForWrite) zwrócił null");
                var bar = Inst(be, "Read");
                if (bar == null) throw new AssertFail("BarEntity.Read zwrócił null");
                Inst(be, "Write", bar);
                tr.Commit();
            }
        }

        private static string Kind(Database db, ObjectId id, string exp)
        {
            if (id.IsNull) throw new AssertFail("brak encji (przygotowanie nie powiodło się)");
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var e = tr.GetObject(id, OpenMode.ForRead);
                var k = Call(BarEntityType, "KindOf", e);
                string act = k == null ? null : Convert.ToString(k, Inv);
                if (act != exp) throw new AssertFail($"KindOf: jest {act ?? "null"}, oczekiwano {exp ?? "null"}");
                return "KindOf=" + (act ?? "null");
            }
        }

        private static object From(DBObject e, int accept)
            => Call(BarEntityType, "From", e, Kinds(accept));

        private static object OpenBar(Transaction tr, ObjectId id, OpenMode mode, int accept)
            => Call(BarEntityType, "Open", tr, id, mode, Kinds(accept));

        private static List<Point3d> Outline(object be, Transaction tr)
        {
            if (be == null) throw new AssertFail("BarEntity null");
            var o = (IEnumerable<Point3d>)Inst(be, "OutlineVertices", tr);
            return o.ToList();
        }

        private static object Kinds(int v) => Enum.ToObject(Plugin.GetType(BarKindsType, true), v);

        private static object NewBar(string mark, int d, string shape, double a, double b, double c, double dd, double e,
                                     double total, bool overridden, string layer, string pos)
        {
            var bar = Activator.CreateInstance(Plugin.GetType(BarDataType, true));
            SetProp(bar, "Mark", mark);
            SetProp(bar, "Diameter", d);
            SetProp(bar, "ShapeCode", shape);
            SetProp(bar, "LengthA", a);
            SetProp(bar, "LengthB", b);
            SetProp(bar, "LengthC", c);
            SetProp(bar, "LengthD", dd);
            SetProp(bar, "LengthE", e);
            SetProp(bar, "TotalLength", total);
            SetProp(bar, "LengthOverridden", overridden);
            SetProp(bar, "LayerCode", layer);
            SetProp(bar, "Position", pos);
            return bar;
        }

        private static Polyline NewPoly(Point3d a, Point3d b)
        {
            var pl = new Polyline();
            pl.AddVertexAt(0, new Point2d(a.X, a.Y), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(b.X, b.Y), 0, 0, 0);
            return pl;
        }

        private static ObjectId AddEntity(Transaction tr, Database db, Entity e)
        {
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            e.SetDatabaseDefaults(db);
            var id = ms.AppendEntity(e);
            tr.AddNewlyCreatedDBObject(e, true);
            return id;
        }

        /// <summary>Zwykły blok testowy (linia) — dla BlockReference, który nie jest prętem v2.</summary>
        private static ObjectId EnsureTestBlock(Transaction tr, Database db)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (bt.Has(TestBlkName)) return bt[TestBlkName];
            bt.UpgradeOpen();
            var btr = new BlockTableRecord { Name = TestBlkName, Origin = Point3d.Origin };
            var id = bt.Add(btr);
            tr.AddNewlyCreatedDBObject(btr, true);
            var ln = new Line(Point3d.Origin, new Point3d(1000, 0, 0));
            btr.AppendEntity(ln);
            tr.AddNewlyCreatedDBObject(ln, true);
            return id;
        }

        private static TypedValue[] ReadRecord(Database db, ObjectId id)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                return ReadRecord((Entity)tr.GetObject(id, OpenMode.ForRead));
        }

        private static TypedValue[] ReadRecord(Entity e)
        {
            using (var rb = e.GetXDataForApplication(XApp))
                return rb?.AsArray() ?? new TypedValue[0];
        }

        /// <summary>Pole i: ten sam kod typu, ten sam typ .NET wartości i ta sama wartość.</summary>
        private static void SameField(int i, TypedValue a, TypedValue b)
        {
            if (a.TypeCode != b.TypeCode)
                throw new AssertFail($"[{i}] typ: jest {b.TypeCode}, oczekiwano {a.TypeCode} ({Desc(b)} vs {Desc(a)})");
            var ta = a.Value?.GetType(); var tb = b.Value?.GetType();
            if (ta != tb)
                throw new AssertFail($"[{i}] typ wartości: jest {tb?.Name ?? "null"}, oczekiwano {ta?.Name ?? "null"}");
            if (!Equals(a.Value, b.Value))
                throw new AssertFail($"[{i}] wartość: jest {Desc(b)}, oczekiwano {Desc(a)}");
        }

        private static string Ver2(TypedValue[] r)
        {
            if (r.Length <= 14) throw new AssertFail("brak [14]");
            var tv = r[14];
            if (tv.TypeCode != TcInt16 || !(tv.Value is short s) || s != 2)
                throw new AssertFail("[14]=" + Desc(tv) + ", oczekiwano Int16 2");
            return "[14]=Int16 2";
        }

        private static bool SameHandle(object v, Handle h)
        {
            if (v is Handle hv) return hv.Value == h.Value;
            return v != null && long.TryParse(v.ToString(), NumberStyles.HexNumber, Inv, out long x) && x == h.Value;
        }

        private static string Desc(TypedValue tv)
            => tv.TypeCode.ToString(Inv) + ":" + (tv.Value == null ? "null" : tv.Value.GetType().Name + " " + Convert.ToString(tv.Value, Inv));

        // ================================================================
        // Infrastruktura: refleksja, wyniki, pliki (jak w etapie 1)
        // ================================================================

        private static Assembly _plugin;

        private static Assembly Plugin
        {
            get
            {
                if (_plugin != null) return _plugin;
                var all = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "NaSmartRc").ToList();
                if (all.Count == 0) throw new InvalidOperationException("Wtyczka NaSmartRc nie jest wczytana (NETLOAD bin\\NaSmartRc.dll).");
                _plugin = all.FirstOrDefault(a => Loc(a).EndsWith("\\bin\\NaSmartRc.dll", StringComparison.OrdinalIgnoreCase)) ?? all[0];
                return _plugin;
            }
        }

        private static string Loc(Assembly a)
        {
            try { return (a.Location ?? "").Replace('/', '\\'); }
            catch (System.Exception ex) { return "? (" + ex.Message + ")"; }
        }

        private static object Call(string typeName, string method, params object[] args)
        {
            var t = Plugin.GetType(typeName, true);
            var cands = t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                         .Where(m => m.Name == method && m.GetParameters().Length == args.Length).ToList();
            if (cands.Count != 1)
                throw new InvalidOperationException($"{typeName}.{method}/{args.Length}: znaleziono {cands.Count} przeciążeń");
            return Invoke(cands[0], null, args);
        }

        private static object Inst(object target, string method, params object[] args)
        {
            var t = target.GetType();
            var cands = t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                         .Where(m => m.Name == method && m.GetParameters().Length == args.Length).ToList();
            if (cands.Count != 1)
                throw new InvalidOperationException($"{t.FullName}.{method}/{args.Length}: znaleziono {cands.Count} przeciążeń");
            return Invoke(cands[0], target, args);
        }

        private static object Invoke(MethodInfo mi, object target, object[] args)
        {
            try { return mi.Invoke(target, args); }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
                throw;
            }
        }

        private static object Prop(object o, string name)
        {
            var pi = o.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (pi != null) return pi.GetValue(o);
            var fi = o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fi != null) return fi.GetValue(o);
            throw new InvalidOperationException(o.GetType().FullName + ": brak pola/właściwości " + name);
        }

        private static void SetProp(object o, string name, object value)
        {
            var pi = o.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (pi == null) throw new InvalidOperationException(o.GetType().FullName + ": brak właściwości " + name);
            pi.SetValue(o, value);
        }

        private static string OutDir => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        private static Database ActiveDb() => Application.DocumentManager.MdiActiveDocument.Database;

        private static void Msg(string text)
        {
            try { Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(text); }
            catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine("PlanCStage2aTests.Msg: " + ex); }
        }

        private static void Run(string step, Action<Database> body)
        {
            _stepStart = _results.Count;
            if (!_executed.Contains(step)) _executed.Add(step);
            try { body(ActiveDb()); }
            catch (System.Exception ex) { Add("FAIL", step, "nieoczekiwany wyjątek kroku", ex.ToString()); }
            var lines = _results.Skip(_stepStart).ToList();
            Msg("\n" + Prefix + " " + step + ":\n" + string.Join("\n", lines) + "\n");
            Flush(null);
        }

        private static void Check(string step, string name, Func<string> body)
        {
            try { Add("PASS", step, name, body() ?? ""); }
            catch (AssertFail af) { Add("FAIL", step, name, af.Message); }
            catch (System.Exception ex) { Add("FAIL", step, name, "wyjątek: " + ex); }
        }

        private static void Add(string status, string step, string name, string details)
            => _results.Add($"{status} | {step} | {name} | {(details ?? "").Replace("\r", " ").Replace("\n", " ")}");

        /// <summary>Zapis wyników (po każdym kroku, żeby przy zawieszeniu skryptu zostały wyniki częściowe).</summary>
        private static void Flush(IEnumerable<string> tail)
        {
            try
            {
                var lines = new List<string>
                {
                    "PLAN C ETAP 2a — testy dymne BricsCAD (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv) + ")",
                    "Wtyczka: " + SafePluginLoc(),
                    "Kroki wykonane: " + string.Join(", ", _executed),
                    "",
                };
                lines.AddRange(_results);
                if (tail != null) lines.AddRange(tail);
                File.WriteAllLines(Path.Combine(OutDir, ResultsFile), lines, new UTF8Encoding(false));
            }
            catch (System.Exception ex) { Msg("\n" + Prefix + " Błąd zapisu wyników: " + ex.Message + "\n"); }
        }

        private static string SafePluginLoc()
        {
            try { return Loc(Plugin); }
            catch (System.Exception ex) { return "BRAK (" + ex.Message + ")"; }
        }

        // ---- asercje elementarne (rzucają AssertFail, zwracają opis) ----

        private static string Int(string what, int act, int exp)
        {
            if (act != exp) throw new AssertFail($"{what}: jest {act.ToString(Inv)}, oczekiwano {exp.ToString(Inv)}");
            return $"{what}={act.ToString(Inv)}";
        }

        private static string Eq(string what, object act, object exp)
        {
            if (!Equals(act, exp))
                throw new AssertFail($"{what}: jest {Show(act)}, oczekiwano {Show(exp)}");
            return $"{what}={Show(act)}";
        }

        private static string Tc(string what, TypedValue tv, short exp)
        {
            if (tv.TypeCode != exp) throw new AssertFail($"{what} typ: jest {Desc(tv)}, oczekiwano {exp.ToString(Inv)}");
            return $"{what} typ={exp.ToString(Inv)}";
        }

        private static string Pts(List<Point3d> act, List<Point3d> exp)
        {
            if (exp.Count == 0) throw new AssertFail("brak oczekiwanych wierzchołków");
            Int("liczba wierzchołków", act.Count, exp.Count);
            double max = 0;
            for (int i = 0; i < exp.Count; i++)
            {
                double d = act[i].DistanceTo(exp[i]);
                if (d > GeoTol) throw new AssertFail($"wierzchołek {i}: jest {P(act[i])}, oczekiwano {P(exp[i])} (odchyłka {d.ToString("E2", Inv)})");
                max = Math.Max(max, d);
            }
            return "wierzchołki=" + exp.Count.ToString(Inv) + " max odchyłka=" + max.ToString("E2", Inv)
                   + " pierwszy=" + P(act[0]);
        }

        private static string True(bool cond, string detail)
        {
            if (!cond) throw new AssertFail(detail);
            return detail;
        }

        private static string Show(object o)
            => o == null ? "null" : o.GetType().Name + " " + Convert.ToString(o, Inv);

        private static string F(double d) => d.ToString("0.###", Inv);
        private static string P(Point3d p) => "(" + p.X.ToString("0.######", Inv) + "," + p.Y.ToString("0.######", Inv) + ")";
    }
}
