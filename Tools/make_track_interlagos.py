"""Builds Assets/_Game/Tracks/Interlagos.json from the real outline (bacinger/f1-circuits, MIT) and SRTM elevation.
Output: centreline every 2 m in local metres (x east, z north, y up, start/finish at index 0, race direction),
plus named corners. Run: python Tools/make_track_interlagos.py [--plot]"""
import json, math, sys, os
HERE = os.path.dirname(os.path.abspath(__file__))
geo = json.load(open(os.path.join(HERE, "trackdata", "br-1940.geojson")))["features"][0]["geometry"]["coordinates"]
el = json.load(open(os.path.join(HERE, "trackdata", "interlagos_elev.json")))
lat0 = sum(p[1] for p in geo) / len(geo); lon0 = sum(p[0] for p in geo) / len(geo)
KX = 111320 * math.cos(math.radians(lat0)); KY = 110540
def xy(lon, lat): return ((lon - lon0) * KX, (lat - lat0) * KY)

pts = [xy(*p) for p in geo]
if pts[0] == pts[-1]: pts = pts[:-1]

def resample(poly, step, closed=True):
    P = poly + [poly[0]] if closed else poly
    out = [P[0]]; carry = 0.0
    for a, b in zip(P, P[1:]):
        seg = math.dist(a, b); t = step - carry
        while t <= seg:
            out.append((a[0] + (b[0] - a[0]) * t / seg, a[1] + (b[1] - a[1]) * t / seg)); t += step
        carry = seg - (t - step)
    if closed and math.dist(out[-1], out[0]) < step * 0.5: out.pop()
    return out

def smooth(poly, it, k=0.5):
    n = len(poly); p = list(poly)
    for _ in range(it):
        p = [((1 - k) * p[i][0] + k * 0.5 * (p[i - 1][0] + p[(i + 1) % n][0]),
              (1 - k) * p[i][1] + k * 0.5 * (p[i - 1][1] + p[(i + 1) % n][1])) for i in range(n)]
    return p

# outline: densify, gentle smoothing (keeps corner shapes, removes GPS kinks), final 2 m spacing
c = resample(pts, 4.0); c = smooth(c, 6); c = resample(c, 2.0)
n = len(c)

# elevation: SRTM samples every 20 m along the same outline -> nearest projection, then median + low-pass + grade clamp
ell = [xy(*p) for p in el["lonlat"]]; ev = el["elev"]
def nearest_elev(p):
    best = min(range(len(ell)), key=lambda i: (ell[i][0] - p[0]) ** 2 + (ell[i][1] - p[1]) ** 2)
    return ev[best]
h = [nearest_elev(p) for p in c]
def circ_median(a, w):
    m = len(a); return [sorted(a[(i + j) % m] for j in range(-w, w + 1))[w] for i in range(m)]
def circ_mean(a, w):
    m = len(a); return [sum(a[(i + j) % m] for j in range(-w, w + 1)) / (2 * w + 1) for i in range(m)]
h = circ_median(h, 15); h = circ_mean(h, 40); h = circ_mean(h, 40)
for _ in range(50):   # clamp grade to 10%
    for i in range(n):
        j = (i + 1) % n; d = h[j] - h[i]; lim = 0.10 * 2.0
        if abs(d) > lim: adj = (abs(d) - lim) / 2 * (1 if d > 0 else -1); h[i] += adj; h[j] -= adj
hmin = min(h); h = [v - hmin for v in h]

area = sum(c[i][0] * c[(i + 1) % n][1] - c[(i + 1) % n][0] * c[i][1] for i in range(n)) / 2
out = {"name": "Interlagos", "points": [[round(p[0], 3), round(hh, 3), round(p[1], 3)] for p, hh in zip(c, h)],
       "signedArea": area}
L = sum(math.dist(c[i], c[(i + 1) % n]) for i in range(n))
print(f"n={n} length={L:.0f} m  area sign={'CCW' if area > 0 else 'CW'}  height range {max(h):.1f} m  start={c[0]}")
json.dump(out, open(os.path.join(HERE, "trackdata", "interlagos_raw.json"), "w"))

if "--plot" in sys.argv:
    from PIL import Image, ImageDraw
    xs = [p[0] for p in c]; zs = [p[1] for p in c]
    W = 900; s = (W - 80) / max(max(xs) - min(xs), max(zs) - min(zs))
    im = Image.new("RGB", (W, W), "white"); d = ImageDraw.Draw(im)
    P = lambda p: (40 + (p[0] - min(xs)) * s, W - 40 - (p[1] - min(zs)) * s)
    for i in range(n):
        t = h[i] / max(h); col = (int(255 * t), 60, int(255 * (1 - t)))
        d.line([P(c[i]), P(c[(i + 1) % n])], fill=col, width=5)
    for i in range(0, n, 100): d.text(P(c[i]), str(i), fill="black")
    d.ellipse([P(c[0])[0] - 8, P(c[0])[1] - 8, P(c[0])[0] + 8, P(c[0])[1] + 8], outline="green", width=3)
    im.save(os.path.join(HERE, "trackdata", "interlagos_plot.png"))

# ---------------------------------------------------------------- runoff / wall offsets + corners -> Interlagos.json
ROAD = 14.0
P = out["points"]; c2 = [(p[0], p[2]) for p in P]
def tangent(i): a = c2[(i - 2) % n]; b = c2[(i + 2) % n]; L = math.dist(a, b); return ((b[0] - a[0]) / L, (b[1] - a[1]) / L)
def arc(i, j): d = abs(i - j); return min(d, n - d) * 2.0
# corners: (name, apex index, turn, outside runoff)
CORNERS = [("S do Senna", 157, "L", 32), ("S do Senna", 208, "R", 26), ("Curva do Sol", 300, "L", 22),
           ("Descida do Lago", 687, "L", 34), ("Ferradura", 987, "R", 24), ("Ferradura", 1055, "R", 24),
           ("Laranjinha", 1145, "R", 20), ("Pinheirinho", 1197, "L", 18), ("Bico de Pato", 1353, "R", 18),
           ("Mergulho", 1598, "L", 20), ("Juncao", 1665, "L", 26), ("Subida dos Boxes", 1850, "L", 14)]
want = {-1: [14.0] * n, 1: [14.0] * n}   # side -1 = left, +1 = right
for name, apex, turn, run in CORNERS:
    outside = 1 if turn == "L" else -1
    for i in range(apex - 70, apex + 90):
        k = 1 - min(1, abs(i - apex - 10) / 80)
        j = i % n; want[outside][j] = max(want[outside][j], 14 + (run - 14) * min(1, k * 1.6))
# never closer than 60 % of the gap to another part of the circuit
grid = {}
for i, p in enumerate(c2): grid.setdefault((int(p[0] // 40), int(p[1] // 40)), []).append(i)
def clearance(i, side):
    t = tangent(i); r = (t[1] * side, -t[0] * side); p = c2[i]
    for s in range(4, 80, 1):
        q = (p[0] + r[0] * s, p[1] + r[1] * s); gx, gz = int(q[0] // 40), int(q[1] // 40)
        for dx in (-1, 0, 1):
            for dz in (-1, 0, 1):
                for j in grid.get((gx + dx, gz + dz), []):
                    if arc(i, j) > 80 and math.dist(q, c2[j]) < ROAD / 2 + 1: return s
    return 999
walls = {}
for side in (-1, 1):
    w = [min(want[side][i], max(ROAD / 2 + 2.2, (clearance(i, side) - ROAD / 2) * 0.55 + ROAD / 2 * 0.45)) for i in range(n)]
    for _ in range(3): w = [min(w[(i + j) % n] for j in range(-12, 13)) for i in range(n)]   # erode (keep the tight spots)
    for _ in range(4): w = [sum(w[(i + j) % n] for j in range(-10, 11)) / 21 for i in range(n)]
    walls[side] = [round(v, 2) for v in w]
final = {"name": "Interlagos", "roadWidth": ROAD, "startIndex": 0,
         "px": [p[0] for p in P], "py": [p[1] for p in P], "pz": [p[2] for p in P],
         "wallLeft": walls[-1], "wallRight": walls[1],
         "cornerNames": [nm for nm, *_ in CORNERS], "cornerIndex": [i for _, i, *_ in CORNERS],
         "source": "Outline: github.com/bacinger/f1-circuits (MIT). Elevation: SRTM 30 m via OpenTopoData."}
json.dump(final, open(os.path.join(HERE, "..", "Assets", "_Game", "Tracks", "Interlagos.json"), "w"))
print("walls L min/max", min(walls[-1]), max(walls[-1]), " R min/max", min(walls[1]), max(walls[1]))
