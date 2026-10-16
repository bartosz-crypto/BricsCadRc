"""
Szablon RC (decyzja użytkownika 2026-10-14): z przykładowego rysunku RC (PLOT 30) zostaje
  • w modelu: tytuły rzutów „REINFORCEMENT DETAILS … BOTTOM / TOP LAYER” i detale PH1–PH9 (obszar detali),
    opisy detali wyzerowane („EXTRA BARS (N/A)”, „APPLICABLE FOR PILES N/A” — RC_PUNCHING_AUTO wpisze właściwe),
  • layout arkusza czysty (blok A1-BL, ramki, notatki ogólne, tabela rewizji, ustawienia wydruku) — bez rzutni,
    notatek „Note:” i chmurek rewizji,
  • style tekstu / wymiarów / warstwy / bloki.
Wynik: src/Resources/rc-template.dxf.gz (EmbeddedResource).
Użycie: python build_template.py <wejście.dxf> <wyjście.dxf.gz>
"""
import sys, re, gzip, io
import ezdxf
from ezdxf import bbox

def normalize_title(t):
    """Jak PhDetailRules.NormalizeTitle (C#): łamanie po „PHn” przed „EXTRA BARS”, akapity do środka, bez ^I
    (decyzja użytkownika 2026-10-16)."""
    t = re.sub(r'(PILE\s+CONDITION\s+PH\s*[1-9])(?!\d)[ \t]+(?=EXTRA\s+BARS)', lambda m: m.group(1) + '\\P', t, flags=re.I)
    t = re.sub(r'\\p([^;\\]*);', lambda m: '\\p' + re.sub(r'(^x|^|,)q[^,]*', lambda k: k.group(1) + 'qc', m.group(1)) + ';', t)
    t = t.replace('^I', '')
    if 'qc;' not in t and 'qc,' not in t:
        t = '\\pxqc;' + t
    return t

src, out = sys.argv[1], sys.argv[2]
doc = ezdxf.readfile(src)
msp = doc.modelspace()
cache = bbox.Cache()

# obszar detali PH: obwiednia tytułów detali „PILE CONDITION PH”
ph_titles = [e for e in msp.query('MTEXT') if re.search(r'PILE CONDITION PH\d', e.plain_text())]
xs = [e.dxf.insert.x for e in ph_titles]; ys = [e.dxf.insert.y for e in ph_titles]
x0, x1 = min(xs) - 2500, max(xs) + 6000
y0, y1 = min(ys) - 2000, max(ys) + 6000

keep = 0
for e in list(msp):
    b = bbox.extents([e], cache=cache)
    c = b.center if b.has_data else None
    is_title = e.dxftype() == 'MTEXT' and 'REINFORCEMENT DETAILS' in e.plain_text()
    in_ph = c is not None and x0 < c.x < x1 and y0 < c.y < y1
    if is_title or in_ph:
        keep += 1
        if e.dxftype() == 'MTEXT' and e.dxf.layer == 'AP-TEXT':
            t = e.text
            t = re.sub(r'EXTRA BARS \([^)]*\)', 'EXTRA BARS (N/A)', t)
            t = re.sub(r'APPLICABLE\s+FOR\s+PILE(?:/S|S)?[^\\}]*', 'APPLICABLE FOR PILES N/A', t)
            e.text = normalize_title(t) if re.search(r'PILE\s+CONDITION\s+PH\d', t) else t
    else:
        msp.delete_entity(e)
print('model: kept', keep, 'PH area', round(x0), round(y0), round(x1), round(y1))

# layout arkusza: czysty (decyzja 2026-10-14, screen Bartka) — bez rzutni (poza rzutnią całego arkusza), bez notatek „Note:”
# do przekrojów / BBS i bez chmurek rewizji z odwołaniami do rysunków innego projektu
removed = 0
for lay in doc.layouts:
    if lay.name == 'Model':
        continue
    vps = [e for e in lay if e.dxftype() == 'VIEWPORT']
    main = min(vps, key=lambda v: v.dxf.id) if vps else None
    for e in list(lay):
        drop = (e.dxftype() == 'VIEWPORT' and e is not main) \
            or (e.dxftype() == 'MTEXT' and e.plain_text().strip().upper().startswith('NOTE:')) \
            or e.dxf.layer.upper() == 'SD-REV CLOUD'
        if drop:
            lay.delete_entity(e); removed += 1
print('layout: removed', removed)

buf = io.StringIO()
doc.write(buf)
with gzip.open(out, 'wt', encoding='utf-8', newline='') as f:
    f.write(buf.getvalue())
print('written', out)
