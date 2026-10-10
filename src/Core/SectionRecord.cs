#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Rekord przekroju RC_SECTION (bezpiecznik aktualności przekrojów) — treść Xrecordu <c>NA_RC_SECTION</c> w słowniku
    /// rozszerzeń tytułu (MText). Czysta logika (bez Teigha), testy tests/SectionRecordTests.cs.
    /// Zapis: linie „KLUCZ=wartość” (<see cref="ToLines"/>), odczyt <see cref="Parse"/> toleruje starsze / krótsze rekordy
    /// (brak klucza = wartość domyślna, nieznane klucze pomijane). Współrzędne zapisywane dokładnie („R”).
    /// Składniki odcisku: linie „C:klucz=wartość”, części: „PART=handle|rola|klucz[|x,y|rodzaj]” (handle „X8”; etap 3:
    /// pozycja domyślna części względem TOPLEFT i rodzaj opisu — RC_SECTION_UPDATE; brak = rekord etapu 1).
    /// </summary>
    public sealed class SectionRecord
    {
        public const int CurrentFormat = 2;   // 2: odcisk względem P1 cięcia (SectionFingerprint.FormatVersion 2); Parse czyta każdy format
        public const string XrecordName = "NA_RC_SECTION";

        public int Format = CurrentFormat;
        public string Guid = "";
        public string Letter = "";
        /// <summary>Punkty linii cięcia (rzut wskazany, bezwzględne) i względem MinPoint obrysu.</summary>
        public (double X, double Y) P1, P2, P1Rel, P2Rel;
        public bool CutAlongX = true;
        public int LookSign = 1;
        /// <summary>Punkt patrzenia (wskazany przy tworzeniu).</summary>
        public (double X, double Y) LookPt;
        /// <summary>Punkt wstawienia przekroju (lewy górny narożnik płyty, poziom SSL).</summary>
        public (double X, double Y, double Z) TopLeft;
        /// <summary>MinPoint obrysu rzutu wskazanego (po wyprostowaniu) w chwili tworzenia.</summary>
        public (double X, double Y) MinPoint;

        // parametry z okna
        public double Thickness = 225, NibHeight = 150, CoverBottom = 40, CoverTop = 35, CoverNib = 30, UbTolerance = 5;
        public string Ssl;
        public bool DrawPiles = true;
        /// <summary>P1: grubość / SSL z opisu płyty (null = brak opisu) i flagi „nadpisane ręcznie w oknie”.</summary>
        public double? DescThickness;
        public string DescSsl;
        public bool ThkOverridden, SslOverridden;

        // odcisk
        public string Hash = "", ResultDigest = "";
        public int Version;
        /// <summary>Etap 3: domyślne położenie tytułu (MText.Location) względem TopLeft; null = rekord sprzed etapu 3.</summary>
        public (double X, double Y)? TitleDef;
        public SortedDictionary<string, string> Components = new SortedDictionary<string, string>(StringComparer.Ordinal);

        public sealed class Part
        {
            public string Handle = "";
            public string Role = "";
            public string Key = "";
            /// <summary>Etap 3: pozycja domyślna względem TopLeft (LBL — punkt tekstu, SLAB — wierzchołek [0]); null = brak.</summary>
            public (double X, double Y)? Def;
            /// <summary>Etap 3: rodzaj opisu (LBL): DOT / BAR / TIN — <see cref="SectionLabelCarry"/>; "" = brak.</summary>
            public string Kind = "";

            /// <summary>Kopia części z nowym handle (CloneRemap) — zachowuje pola etapu 3.</summary>
            public Part WithHandle(string handle) => new Part { Handle = handle, Role = Role, Key = Key, Def = Def, Kind = Kind };
        }
        public List<Part> Parts = new List<Part>();

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static string R(double v) => v.ToString("R", Inv);
        private static string Xy(double x, double y) => R(x) + "," + R(y);

        public List<string> ToLines()
        {
            var l = new List<string>
            {
                "FORMAT=" + Format.ToString(Inv),
                "GUID=" + (Guid ?? ""),
                "LETTER=" + SectionFingerprint.Esc(Letter),
                "P1=" + Xy(P1.X, P1.Y),
                "P2=" + Xy(P2.X, P2.Y),
                "P1REL=" + Xy(P1Rel.X, P1Rel.Y),
                "P2REL=" + Xy(P2Rel.X, P2Rel.Y),
                "CUTX=" + (CutAlongX ? "1" : "0"),
                "LOOK=" + (LookSign >= 0 ? "1" : "-1"),
                "LOOKPT=" + Xy(LookPt.X, LookPt.Y),
                "TOPLEFT=" + Xy(TopLeft.X, TopLeft.Y) + "," + R(TopLeft.Z),
                "MIN=" + Xy(MinPoint.X, MinPoint.Y),
                "THK=" + R(Thickness),
                "NIBH=" + R(NibHeight),
                "CB=" + R(CoverBottom),
                "CT=" + R(CoverTop),
                "CN=" + R(CoverNib),
                "TOL=" + R(UbTolerance),
                "SSL=" + (Ssl == null ? "" : "=" + SectionFingerprint.Esc(Ssl)),
                "PILES=" + (DrawPiles ? "1" : "0"),
                "DESCTHK=" + (DescThickness.HasValue ? R(DescThickness.Value) : ""),
                "DESCSSL=" + (DescSsl == null ? "" : "=" + SectionFingerprint.Esc(DescSsl)),
                "THKOVR=" + (ThkOverridden ? "1" : "0"),
                "SSLOVR=" + (SslOverridden ? "1" : "0"),
                "HASH=" + (Hash ?? ""),
                "DIGEST=" + (ResultDigest ?? ""),
                "VER=" + Version.ToString(Inv),
            };
            if (TitleDef.HasValue) l.Add("TITLEDEF=" + Xy(TitleDef.Value.X, TitleDef.Value.Y));   // etap 3 — tylko gdy jest
            foreach (var kv in Components.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                l.Add("C:" + kv.Key + "=" + kv.Value);
            foreach (var p in Parts)
            {
                string line = "PART=" + (p.Handle ?? "") + "|" + (p.Role ?? "") + "|" + SectionFingerprint.Esc(p.Key);   // Esc zamienia też | → \p
                // etap 3: pozycja domyślna i rodzaj na końcu linii (starszy parser czyta tylko pola 0–2)
                if (p.Def.HasValue || !string.IsNullOrEmpty(p.Kind))
                    line += "|" + (p.Def.HasValue ? Xy(p.Def.Value.X, p.Def.Value.Y) : "") + "|" + SectionFingerprint.Esc(p.Kind ?? "");
                l.Add(line);
            }
            return l;
        }

        /// <summary>Odczyt rekordu; null gdy brak linii FORMAT (nie rekord przekroju). Starsze rekordy — brakujące pola domyślne.</summary>
        public static SectionRecord Parse(IEnumerable<string> lines)
        {
            if (lines == null) return null;
            var r = new SectionRecord();
            bool any = false;
            foreach (var raw in lines)
            {
                if (string.IsNullOrEmpty(raw)) continue;
                int eq = raw.IndexOf('=');
                if (eq <= 0) continue;
                string k = raw.Substring(0, eq), v = raw.Substring(eq + 1);
                try
                {
                    if (k.StartsWith("C:", StringComparison.Ordinal)) { r.Components[k.Substring(2)] = v; continue; }
                    switch (k)
                    {
                        case "FORMAT": r.Format = int.Parse(v, Inv); any = true; break;
                        case "GUID": r.Guid = v; break;
                        case "LETTER": r.Letter = SectionFingerprint.Unesc(v); break;
                        case "P1": r.P1 = Pt(v); break;
                        case "P2": r.P2 = Pt(v); break;
                        case "P1REL": r.P1Rel = Pt(v); break;
                        case "P2REL": r.P2Rel = Pt(v); break;
                        case "CUTX": r.CutAlongX = v != "0"; break;
                        case "LOOK": r.LookSign = v.StartsWith("-", StringComparison.Ordinal) ? -1 : 1; break;
                        case "LOOKPT": r.LookPt = Pt(v); break;
                        case "TOPLEFT":
                        {
                            var p = v.Split(',');
                            r.TopLeft = (D(p[0]), D(p[1]), p.Length > 2 ? D(p[2]) : 0);
                            break;
                        }
                        case "MIN": r.MinPoint = Pt(v); break;
                        case "THK": r.Thickness = D(v); break;
                        case "NIBH": r.NibHeight = D(v); break;
                        case "CB": r.CoverBottom = D(v); break;
                        case "CT": r.CoverTop = D(v); break;
                        case "CN": r.CoverNib = D(v); break;
                        case "TOL": r.UbTolerance = D(v); break;
                        case "SSL": r.Ssl = v.StartsWith("=", StringComparison.Ordinal) ? SectionFingerprint.Unesc(v.Substring(1)) : null; break;
                        case "PILES": r.DrawPiles = v != "0"; break;
                        case "DESCTHK": r.DescThickness = v.Length > 0 ? D(v) : (double?)null; break;
                        case "DESCSSL": r.DescSsl = v.StartsWith("=", StringComparison.Ordinal) ? SectionFingerprint.Unesc(v.Substring(1)) : null; break;
                        case "THKOVR": r.ThkOverridden = v == "1"; break;
                        case "SSLOVR": r.SslOverridden = v == "1"; break;
                        case "HASH": r.Hash = v; break;
                        case "DIGEST": r.ResultDigest = v; break;
                        case "VER": r.Version = int.Parse(v, Inv); break;
                        case "TITLEDEF": r.TitleDef = Pt(v); break;
                        case "PART":
                        {
                            var p = v.Split('|');
                            var part = new Part
                            {
                                Handle = p.Length > 0 ? p[0] : "", Role = p.Length > 1 ? p[1] : "",
                                Key = p.Length > 2 ? SectionFingerprint.Unesc(p[2]) : "",
                                Kind = p.Length > 4 ? SectionFingerprint.Unesc(p[4]) : ""
                            };
                            r.Parts.Add(part);
                            if (p.Length > 3 && p[3].Length > 0) part.Def = Pt(p[3]);   // uszkodzona pozycja → wyjątek niżej, część zostaje
                            break;
                        }
                    }
                }
                // pole uszkodzone — wartość domyślna (rekord z innej wersji), reszta rekordu dalej
                catch (FormatException ex) { Log.Error("SectionRecord.Parse." + k, ex); }
                catch (IndexOutOfRangeException ex) { Log.Error("SectionRecord.Parse." + k, ex); }
                catch (OverflowException ex) { Log.Error("SectionRecord.Parse." + k, ex); }
            }
            return any ? r : null;
        }

        private static double D(string s) => double.Parse(s, NumberStyles.Float, Inv);

        private static (double X, double Y) Pt(string s)
        {
            var p = s.Split(',');
            return (D(p[0]), D(p[1]));
        }

        /// <summary>Stan odcisku zapisany w rekordzie (do <see cref="SectionFingerprint.Compare"/>).</summary>
        public SectionFingerprint.State StoredState()
            => new SectionFingerprint.State
            {
                Components = new SortedDictionary<string, string>(Components, StringComparer.Ordinal),
                Hash = Hash ?? "", Digest = ResultDigest ?? "", Version = Version
            };
    }
}
