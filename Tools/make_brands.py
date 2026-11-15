"""Draws the trackside sponsor atlas: 16 invented brands, each as a full-colour board (column 0) and a white
transparent "painted" version for run-off tarmac, bridges and walls (column 1). Cells are 1024x128 (8:1 like the
barrier boards). Output: Assets/_Game/Textures/BrandAtlas.png. All brands are fictional.
Run: python Tools/make_brands.py"""
import math, os
from PIL import Image, ImageDraw, ImageFont, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
FONTS = os.path.join(HERE, "..", "Assets", "_Game", "Fonts")
OUT = os.path.join(HERE, "..", "Assets", "_Game", "Textures", "BrandAtlas.png")
CW, CH, S = 1024, 128, 2          # cell size, supersampling factor

def font(name, size): return ImageFont.truetype(os.path.join(FONTS, name), size * S)
WORD = "BarlowCondensed-BoldItalic.ttf"; TAG = "BarlowCondensed-SemiBold.ttf"; BEBAS = "BebasNeue-Regular.ttf"

# name, tagline, background, foreground, accent, mark, word font
BRANDS = [
    ("VELTRO", "PERFORMANCE TYRES", (196, 16, 22), (255, 255, 255), (255, 210, 0), "tyre", WORD),
    ("NORDAX", "PRIVATE BANKING", (10, 28, 82), (255, 255, 255), (200, 170, 90), "crest", BEBAS),
    ("KAIRO", "ENERGY", (245, 192, 10), (18, 18, 22), (210, 30, 30), "flame", WORD),
    ("ALTUS", "SWISS CHRONOMETERS", (14, 14, 16), (235, 235, 235), (210, 175, 90), "crown", BEBAS),
    ("QUANTA", "5G NETWORKS", (0, 120, 112), (255, 255, 255), (150, 255, 220), "waves", WORD),
    ("MERIDIAN", "AIRWAYS", (240, 240, 242), (20, 40, 110), (200, 20, 30), "wing", WORD),
    ("HALCYON", "CLOUD COMPUTING", (70, 20, 110), (255, 255, 255), (0, 220, 255), "hex", BEBAS),
    ("ORBITA", "GLOBAL LOGISTICS", (232, 100, 12), (255, 255, 255), (30, 30, 30), "orbit", WORD),
    ("SOLENNE", "SPARKLING WATER", (20, 120, 50), (255, 255, 255), (190, 240, 120), "drop", WORD),
    ("BRAVEN", "ENERGY DRINK", (34, 36, 42), (255, 205, 20), (255, 60, 40), "bolt", BEBAS),
    ("FERROX", "SYNTHETIC LUBRICANTS", (0, 70, 160), (255, 255, 255), (240, 40, 40), "drop2", WORD),
    ("LUMIO", "CAMERAS & OPTICS", (250, 250, 250), (15, 15, 15), (240, 0, 120), "lens", BEBAS),
    ("ZENTRA", "INSURANCE", (120, 0, 30), (255, 255, 255), (255, 190, 200), "shield", WORD),
    ("ARCOS", "MOTOR OILS", (16, 16, 18), (255, 255, 255), (0, 200, 90), "arcs", WORD),
    ("CIMBA", "CAFE DO BRASIL", (90, 50, 30), (250, 230, 200), (230, 160, 60), "bean", BEBAS),
    ("PRAIA", "GUARANA SODA", (0, 150, 70), (255, 230, 0), (255, 255, 255), "sun", WORD),
]

def mark(d, kind, cx, cy, r, fg, ac):
    """Simple, bold logo marks (vector shapes), centred at cx,cy with radius r."""
    w = max(2, int(r * 0.16))
    if kind == "tyre":
        d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=fg, width=w * 2)
        d.ellipse([cx - r * .45, cy - r * .45, cx + r * .45, cy + r * .45], fill=ac)
        for a in range(0, 360, 30):
            x, y = cx + math.cos(math.radians(a)) * r * .72, cy + math.sin(math.radians(a)) * r * .72
            d.ellipse([x - w * .6, y - w * .6, x + w * .6, y + w * .6], fill=fg)
    elif kind == "crest":
        d.polygon([(cx - r * .8, cy - r), (cx + r * .8, cy - r), (cx + r * .8, cy + r * .1), (cx, cy + r), (cx - r * .8, cy + r * .1)], fill=ac)
        d.polygon([(cx - r * .5, cy - r * .7), (cx + r * .5, cy - r * .7), (cx + r * .5, cy), (cx, cy + r * .6), (cx - r * .5, cy)], fill=fg)
    elif kind == "flame":
        d.polygon([(cx, cy - r), (cx + r * .7, cy + r * .2), (cx + r * .3, cy + r), (cx - r * .3, cy + r), (cx - r * .7, cy + r * .2)], fill=ac)
        d.polygon([(cx, cy - r * .3), (cx + r * .35, cy + r * .4), (cx, cy + r * .9), (cx - r * .35, cy + r * .4)], fill=fg)
    elif kind == "crown":
        pts = [(cx - r, cy + r * .6), (cx - r, cy - r * .5), (cx - r * .5, cy + r * .05), (cx, cy - r * .9), (cx + r * .5, cy + r * .05), (cx + r, cy - r * .5), (cx + r, cy + r * .6)]
        d.polygon(pts, fill=ac); d.rectangle([cx - r, cy + r * .7, cx + r, cy + r * .9], fill=ac)
    elif kind == "waves":
        for k in range(3):
            rr = r * (0.4 + k * 0.3)
            d.arc([cx - rr - r * .6, cy - rr, cx + rr - r * .6, cy + rr], -50, 50, fill=fg if k != 1 else ac, width=w)
    elif kind == "wing":
        for k in range(4):
            y = cy - r * .6 + k * r * .38
            d.polygon([(cx - r, y), (cx + r * (1 - k * .2), y - r * .25), (cx + r * (0.8 - k * .2), y + r * .12), (cx - r, y + r * .2)], fill=ac if k == 0 else fg)
    elif kind == "hex":
        pts = [(cx + math.cos(math.radians(a)) * r, cy + math.sin(math.radians(a)) * r) for a in range(30, 390, 60)]
        d.polygon(pts, outline=ac, width=w); pts2 = [(cx + math.cos(math.radians(a)) * r * .5, cy + math.sin(math.radians(a)) * r * .5) for a in range(30, 390, 60)]
        d.polygon(pts2, fill=fg)
    elif kind == "orbit":
        d.ellipse([cx - r * .45, cy - r * .45, cx + r * .45, cy + r * .45], fill=fg)
        d.arc([cx - r, cy - r * .5, cx + r, cy + r * .5], 200, 520, fill=ac, width=w)
    elif kind in ("drop", "drop2"):
        d.polygon([(cx, cy - r), (cx + r * .62, cy + r * .2), (cx - r * .62, cy + r * .2)], fill=ac if kind == "drop" else fg)
        d.ellipse([cx - r * .66, cy - r * .2, cx + r * .66, cy + r * .95], fill=ac if kind == "drop" else fg)
        if kind == "drop2": d.ellipse([cx - r * .25, cy + r * .2, cx + r * .1, cy + r * .55], fill=ac)
    elif kind == "bolt":
        d.polygon([(cx + r * .2, cy - r), (cx - r * .6, cy + r * .15), (cx - r * .05, cy + r * .15), (cx - r * .25, cy + r), (cx + r * .6, cy - r * .2), (cx + r * .05, cy - r * .2)], fill=ac)
    elif kind == "lens":
        for k, c in enumerate((ac, fg, ac)):
            rr = r * (1 - k * .3); d.ellipse([cx - rr, cy - rr, cx + rr, cy + rr], outline=c, width=w)
        d.ellipse([cx - r * .15, cy - r * .15, cx + r * .15, cy + r * .15], fill=fg)
    elif kind == "shield":
        d.polygon([(cx - r * .8, cy - r * .8), (cx, cy - r), (cx + r * .8, cy - r * .8), (cx + r * .7, cy + r * .3), (cx, cy + r), (cx - r * .7, cy + r * .3)], fill=fg)
        d.polygon([(cx, cy - r * .6), (cx + r * .45, cy - r * .45), (cx + r * .4, cy + r * .2), (cx, cy + r * .6)], fill=ac)
    elif kind == "arcs":
        for k in range(3):
            rr = r * (0.4 + k * 0.3); d.arc([cx - rr, cy - rr, cx + rr, cy + rr], 180, 360, fill=ac if k == 2 else fg, width=w)
    elif kind == "bean":
        d.ellipse([cx - r * .65, cy - r, cx + r * .65, cy + r], fill=ac)
        d.arc([cx - r * .3, cy - r * .9, cx + r * .3, cy + r * .9], 100, 260, fill=fg, width=w)
    elif kind == "sun":
        d.ellipse([cx - r * .5, cy - r * .5, cx + r * .5, cy + r * .5], fill=ac)
        for a in range(0, 360, 30):
            x0, y0 = cx + math.cos(math.radians(a)) * r * .65, cy + math.sin(math.radians(a)) * r * .65
            x1, y1 = cx + math.cos(math.radians(a)) * r, cy + math.sin(math.radians(a)) * r
            d.line([(x0, y0), (x1, y1)], fill=ac, width=w)

def cell(brand, painted):
    name, tag, bg, fg, ac, kind, wf = brand
    W, H = CW * S, CH * S
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0) if painted else bg + (255,))
    d = ImageDraw.Draw(img)
    if painted: fg, ac = (255, 255, 255), (255, 255, 255)
    else:
        # subtle diagonal sheen + accent stripe like real LED/vinyl boards
        for x in range(0, W, 6 * S):
            d.line([(x, 0), (x - H, H)], fill=tuple(min(255, c + 6) for c in bg) + (255,), width=S)
        d.rectangle([0, H - 7 * S, W, H], fill=ac + (255,))
    big = font(wf, 92 if wf == WORD else 104)
    small = font(TAG, 26)
    tw = d.textlength(name, font=big); sw = d.textlength(tag, font=small)
    r = 44 * S; gap = 26 * S
    total = 2 * r + gap + max(tw, sw)
    x0 = (W - total) / 2
    mark(d, kind, x0 + r, H / 2 - 2 * S, r, fg + (255,), ac + (255,))
    tx = x0 + 2 * r + gap
    d.text((tx, H / 2 - 66 * S), name, font=big, fill=fg + (255,))
    d.text((tx + 4 * S, H / 2 + 30 * S), tag, font=small, fill=(ac if not painted else fg) + (255,))
    return img.resize((CW, CH), Image.LANCZOS)

atlas = Image.new("RGBA", (CW * 2, CH * 16), (0, 0, 0, 0))
for i, b in enumerate(BRANDS):
    atlas.paste(cell(b, False), (0, i * CH))
    atlas.paste(cell(b, True), (CW, i * CH))
atlas.save(OUT)
print("wrote", OUT, atlas.size)
