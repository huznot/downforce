"""Cuts the store trailer and Google Play graphics from the recordings made by OWR > Record Trailer.

Outputs (in Builds/Store/):
  Downforce_Trailer.mp4           1920x1080 30fps H.264/AAC (~45 s)  -> upload to YouTube, paste the link in Play Console
  screenshots/*.png               1920x1080 phone/tablet screenshots
  feature_graphic_1024x500.png    Play feature graphic
  icon_512.png                    Play store icon
"""
import os, subprocess, sys
from PIL import Image, ImageDraw, ImageFont, ImageFilter, ImageEnhance

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REC = os.path.join(ROOT, 'Recordings')
OUT = os.path.join(ROOT, 'Builds', 'Store')
RACE = os.path.join(REC, 'trailer_race.mp4')
CELEB = os.path.join(REC, 'trailer_celebration.mp4')
FONT = os.path.join(ROOT, 'Assets', '_Game', 'Fonts', 'BebasNeue-Regular.ttf')
FONT_FF = FONT.replace('\\', '/').replace(':', '\\:')
CREAM = '0xF3E6C8'

# (source, start, duration, caption)
SHOTS = [
    (RACE, 4.0, 3.0, ''),
    (RACE, 22.0, 5.4, ''),                      # five red lights... lights out
    (RACE, 27.4, 4.4, 'FEEL EVERY CORNER'),     # T-cam off the line
    (RACE, 33.2, 3.2, ''),                      # in the spray of the car ahead
    (RACE, 39.0, 3.4, ''),                      # side tracking
    (RACE, 45.0, 3.4, ''),                      # helicopter
    (RACE, 50.6, 4.4, 'SURVIVE THE CHAOS'),     # the crash
    (RACE, 57.5, 5.0, ''),                      # onboard with the HUD
    (RACE, 65.5, 3.6, ''),                      # rear-wing view
    (CELEB, 0.6, 4.0, ''),                      # chequered flag
    (CELEB, 6.6, 5.0, 'TAKE THE WIN'),          # fists up
]
FADE = 0.12


def run(args):
    print(' '.join(a if ' ' not in a else f'"{a}"' for a in args[:6]), '...')
    subprocess.run(args, check=True)


def trailer():
    os.makedirs(OUT, exist_ok=True)
    inputs, parts = [], []
    # title card: blurred, darkened grid shot with the logo
    inputs += ['-ss', '1.0', '-t', '3.0', '-i', RACE]
    parts.append(
        f"[0:v]scale=1920:1080,boxblur=12:2,eq=brightness=-0.18:saturation=0.8,fps=30,"
        f"drawtext=fontfile='{FONT_FF}':text='DOWNFORCE':fontsize=260:fontcolor={CREAM}:x=(w-tw)/2:y=(h-th)/2-60:shadowcolor=0x00000088:shadowx=8:shadowy=8,"
        f"drawtext=fontfile='{FONT_FF}':text='WET RACE':fontsize=80:fontcolor={CREAM}:x=(w-tw)/2:y=(h/2)+110,"
        f"fade=t=in:st=0:d=0.8,fade=t=out:st=2.6:d=0.4,setsar=1[v0];"
        f"[0:a]afade=t=in:st=0:d=0.8,afade=t=out:st=2.6:d=0.4,volume=0.7[a0]")
    for i, (src, st, du, cap) in enumerate(SHOTS, start=1):
        inputs += ['-ss', f'{st}', '-t', f'{du}', '-i', src]
        text = ''
        if cap:
            text = (f",drawtext=fontfile='{FONT_FF}':text='{cap}':fontsize=96:fontcolor={CREAM}:x=90:y=h-200:"
                    f"shadowcolor=0x000000AA:shadowx=5:shadowy=5:alpha='if(lt(t,0.3),t/0.3,if(gt(t,{du}-0.4),({du}-t)/0.4,1))'")
        parts.append(
            f"[{i}:v]scale=1920:1080,fps=30{text},fade=t=in:st=0:d={FADE},fade=t=out:st={du - FADE}:d={FADE},setsar=1[v{i}];"
            f"[{i}:a]afade=t=in:st=0:d=0.05,afade=t=out:st={du - 0.08}:d=0.08[a{i}]")
    n = len(SHOTS) + 1
    # end card
    inputs += ['-ss', '8.0', '-t', '4.0', '-i', CELEB]
    parts.append(
        f"[{n}:v]scale=1920:1080,boxblur=14:2,eq=brightness=-0.22:saturation=0.7,fps=30,"
        f"drawtext=fontfile='{FONT_FF}':text='DOWNFORCE':fontsize=240:fontcolor={CREAM}:x=(w-tw)/2:y=(h-th)/2-70:shadowcolor=0x00000088:shadowx=8:shadowy=8,"
        f"drawtext=fontfile='{FONT_FF}':text='RACE IN THE RAIN':fontsize=72:fontcolor={CREAM}:x=(w-tw)/2:y=(h/2)+100,"
        f"fade=t=in:st=0:d=0.5,fade=t=out:st=3.3:d=0.7,setsar=1[v{n}];"
        f"[{n}:a]afade=t=in:st=0:d=0.3,afade=t=out:st=2.8:d=1.2,volume=0.8[a{n}]")
    concat = ''.join(f'[v{i}][a{i}]' for i in range(n + 1)) + f'concat=n={n + 1}:v=1:a=1[v][a]'
    script = os.path.join(OUT, '_trailer_filter.txt')
    with open(script, 'w', encoding='utf-8') as f:
        f.write(';\n'.join(parts) + ';\n' + concat)
    out = os.path.join(OUT, 'Downforce_Trailer.mp4')
    run(['ffmpeg', '-v', 'error', '-y'] + inputs + ['-filter_complex_script', script, '-map', '[v]', '-map', '[a]',
         '-c:v', 'libx264', '-preset', 'slow', '-crf', '18', '-pix_fmt', 'yuv420p', '-c:a', 'aac', '-b:a', '192k',
         '-movflags', '+faststart', out])
    os.remove(script)
    print('trailer ->', out)


def frame(src, t, path):
    run(['ffmpeg', '-v', 'error', '-y', '-ss', f'{t}', '-i', src, '-frames:v', '1', '-vf', 'scale=1920:1080', path])


def graphics():
    shots = os.path.join(OUT, 'screenshots')
    os.makedirs(shots, exist_ok=True)
    picks = [(RACE, 29.6, '01_tcam'), (RACE, 24.5, '02_lights'), (RACE, 40.6, '03_tracking'), (RACE, 51.6, '04_crash'),
             (RACE, 59.5, '05_cockpit'), (RACE, 46.4, '06_helicopter'), (CELEB, 2.6, '07_flag'), (CELEB, 9.2, '08_celebration'),
             (RACE, 34.6, '09_spray'), (RACE, 5.0, '10_grid')]
    for src, t, name in picks:
        frame(src, t, os.path.join(shots, name + '.png'))

    font_big = ImageFont.truetype(FONT, 150)
    font_small = ImageFont.truetype(FONT, 44)
    cream = (243, 230, 200)

    # feature graphic 1024x500: side tracking shot, warm gradient on the left with the logo
    base = Image.open(os.path.join(shots, '03_tracking.png')).convert('RGB')
    w = 1024; h = 500
    scale = h / 1080 * 1.25
    img = base.resize((int(1920 * scale), int(1080 * scale)), Image.LANCZOS)
    x0 = min((img.width - w) // 2 + 120, img.width - w); y0 = (img.height - h) // 2
    fg = img.crop((x0, y0, x0 + w, y0 + h))
    grad = Image.new('L', (w, h))
    gd = ImageDraw.Draw(grad)
    for x in range(w):
        gd.line([(x, 0), (x, h)], fill=int(235 * max(0.0, 1 - x / 560) ** 1.4))
    tint = Image.new('RGB', (w, h), (18, 34, 40))
    fg = Image.composite(tint, fg, grad)
    d = ImageDraw.Draw(fg)
    d.text((48, 120), 'DOWNFORCE', font=font_big, fill=cream)
    d.rectangle([52, 290, 300, 296], fill=(255, 122, 26))
    d.text((52, 312), 'WET RACE', font=font_small, fill=cream)
    fg.save(os.path.join(OUT, 'feature_graphic_1024x500.png'))

    # icon 512x512: T-cam crop (helmet under the halo), punchy grade + orange accent bar
    t = Image.open(os.path.join(shots, '01_tcam.png')).convert('RGB')
    side = 900
    cx, cy = 960, 700
    ic = t.crop((cx - side // 2, cy - side // 2, cx + side // 2, cy + side // 2)).resize((512, 512), Image.LANCZOS)
    ic = ImageEnhance.Contrast(ic).enhance(1.15)
    ic = ImageEnhance.Color(ic).enhance(1.2)
    vign = Image.new('L', (512, 512), 0)
    vd = ImageDraw.Draw(vign)
    vd.ellipse([-80, -80, 592, 592], fill=255)
    vign = vign.filter(ImageFilter.GaussianBlur(60))
    dark = Image.new('RGB', (512, 512), (10, 14, 18))
    ic = Image.composite(ic, dark, vign)
    d = ImageDraw.Draw(ic)
    d.rectangle([0, 470, 512, 512], fill=(255, 122, 26))
    ic.save(os.path.join(OUT, 'icon_512.png'))
    print('graphics ->', OUT)


if __name__ == '__main__':
    for p in (RACE, CELEB):
        if not os.path.exists(p):
            sys.exit('missing recording: ' + p + '  (run OWR > Record Trailer in Unity first)')
    graphics()
    trailer()
