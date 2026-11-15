"""
Renders the race commentary with Microsoft neural voices (edge-tts) into
Assets/_Game/Audio/Commentary as MP3 + a lines.json manifest (file -> subtitle).
Only these fixed script lines are sent to the TTS service. Re-run to regenerate.
    python Tools/gen_commentary.py
"""
import asyncio, json, os, glob
import edge_tts

OUT = os.path.join(os.path.dirname(__file__), '..', 'Assets', '_Game', 'Audio', 'Commentary')
COMMENTATOR = 'en-GB-RyanNeural'
DRIVER = 'en-AU-WilliamNeural'   # distinct from the commentator; radio-filtered below

# (key, text, voice, rate, pitch)  -- big moments get more pace and lift
CALM, HYPE, MAX = ('+22%', '+2Hz'), ('+32%', '+7Hz'), ('+40%', '+12Hz')
LINES = [
    ('intro_1', "Welcome to a soaking wet afternoon here! The grip is low, the spray is heavy, and the crowd... are on their feet!", *CALM),
    ('intro_2', "What a day for it! Rain hammering down, standing water everywhere, this is going to be absolute chaos!", *CALM),
    ('lights_1', "Five red lights...", *CALM),
    ('lights_2', "Here we go, lights coming on...", *CALM),
    ('go_1', "And it's lights out, and away we go!", *MAX),
    ('go_2', "LIGHTS OUT! They're away, and it's tight into turn one!", *MAX),
    ('go_3', "GO GO GO! Wheel to wheel off the line!", *MAX),
    ('gain_1', "Oh what a move! Up the inside, and that's a place gained!", *HYPE),
    ('gain_2', "Brilliant! Brave, brave overtake in these conditions!", *HYPE),
    ('gain_3', "He's through! Absolutely sensational!", *HYPE),
    ('lose_1', "Ooh, and he's lost a place there.", *CALM),
    ('lose_2', "Under pressure, and he can't hold them off!", *CALM),
    ('lead_1', "And NOW, he leads the race! What a drive!", *MAX),
    ('lead_2', "Into the lead! The crowd are going wild!", *MAX),
    ('fastest_1', "Fastest lap of the race! Purple, purple, purple!", *HYPE),
    ('fastest_2', "That's a stunning lap, the quickest of anyone so far!", *HYPE),
    ('halfway_1', "We're halfway through, and the rain just will not stop!", *CALM),
    ('finallap_1', "FINAL LAP! One more time around, everything on the line!", *MAX),
    ('finallap_2', "Last lap! Hold your nerve, hold your nerve!", *MAX),
    ('spin_1', "Oh, he's around! Massive moment in the wet!", *HYPE),
    ('contact_1', "Contact! Oh, that's going to hurt!", *HYPE),
    ('crash_1', "Oh no! Huge accident! That's the end of his race!", *MAX),
    ('crash_2', "He's lost it, and he's in the wall! Race over!", *MAX),
    ('aicrash_1', "BIG crash out there! That car is destroyed, debris everywhere!", *MAX),
    ('aicrash_2', "Oh my word, what a shunt! He's out, and there's fire!", *MAX),
    ('penalty_1', "And the stewards have seen enough, that's a five second penalty for contact!", *HYPE),
    ('penalty_2', "Penalty! Five seconds for running into the back of a rival!", *HYPE),
    ('penalty_you_1', "Oh dear, that's a penalty for us! Five seconds for contact, careful now!", *HYPE),
    ('win_1', "And he's done it! VICTORY! An incredible drive in the rain!", *MAX),
    ('win_2', "YES! He takes the chequered flag! What a race, what a win!", *MAX),
    ('win_3', "Across the line, and it's a WIN! Unbelievable!", *MAX),
    ('podium_1', "It's a podium! A brilliant result in brutal conditions!", *HYPE),
    ('podium_2', "Onto the podium! He'll be delighted with that!", *HYPE),
    ('finish_1', "Across the line, and the chequered flag is out.", *CALM),
    ('finish_2', "And that's the flag. A tough afternoon, but he brings it home.", *CALM),
    ('practice_1', "Free practice is underway. The track is yours, push when you're ready!", *CALM),
    ('pb_1', "Personal best! That's a mega lap!", *HYPE),
    ('pb_2', "Faster again! The lap times keep tumbling!", *HYPE),
]
RADIO = [
    ('yes_1', "Yes! We won it! P1, P1! Thank you guys!", DRIVER, '+12%', '+4Hz'),
    ('yes_2', "Woooo! Race winner! What a car, what a team!", DRIVER, '+12%', '+4Hz'),
    ('yes_3', "That's P1! Unbelievable job everyone, thank you!", DRIVER, '+12%', '+4Hz'),
]
# team-radio sound: narrow band, compressed, a little grit
RADIO_FX = "highpass=f=450,lowpass=f=3200,acompressor=threshold=0.1:ratio=6:attack=5:release=60,acrusher=bits=10:mix=0.25,volume=1.6"


async def render(key, text, voice, rate, pitch):
    path = os.path.join(OUT, key + '.mp3')
    await edge_tts.Communicate(text, voice, rate=rate, pitch=pitch).save(path)
    return key, text


def radio_fx(key):
    import subprocess
    src = os.path.join(OUT, key + '.mp3'); tmp = src + '.tmp.mp3'
    subprocess.run(['ffmpeg', '-y', '-loglevel', 'error', '-i', src, '-af', RADIO_FX, tmp], check=True)
    os.replace(tmp, src)


async def main():
    import sys
    radio_only = '--radio' in sys.argv
    os.makedirs(OUT, exist_ok=True)
    for old in glob.glob(os.path.join(OUT, '*.wav')) + glob.glob(os.path.join(OUT, '*.wav.meta')):
        os.remove(old)
    jobs = ([] if radio_only else [render(k, t, COMMENTATOR, r, p) for (k, t, r, p) in LINES]) + [render(k, t, v, r, p) for (k, t, v, r, p) in RADIO]
    mpath = os.path.join(OUT, 'lines.json')
    manifest = json.load(open(mpath, encoding='utf-8')) if radio_only and os.path.exists(mpath) else {}
    for k, t in await asyncio.gather(*jobs):
        manifest[k] = t
    for k, *_ in RADIO: radio_fx(k)
    with open(os.path.join(OUT, 'lines.json'), 'w', encoding='utf-8') as f:
        json.dump(manifest, f, indent=1)
    print(len(manifest), 'lines rendered')

asyncio.run(main())
