"""Rebuild the original geometric app icon and two-note completion sound. Standard library only."""
import math
import struct
import wave
import zlib
from pathlib import Path

root = Path(__file__).resolve().parents[1]
assets = root / "src" / "StillFocus.App" / "Assets"
assets.mkdir(parents=True, exist_ok=True)

def chunk(kind, data):
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xffffffff)

def icon_png(n):
    rows = []
    for y in range(n):
        row = bytearray()
        for x in range(n):
            u, v = (x + .5) / n, (y + .5) / n
            # Rounded navy tile with an open mint clock ring and two hands.
            qx, qy = max(abs(u - .5) - .29, 0), max(abs(v - .5) - .29, 0)
            inside = qx * qx + qy * qy <= .18 ** 2
            r = math.hypot(u - .5, v - .5)
            angle = math.atan2(v - .5, u - .5)
            ring = .265 < r < .308 and not (-1.7 < angle < -1.0)
            hands = (.477 < u < .523 and .30 < v < .52) or (.49 < u < .665 and .477 < v < .523)
            pixel = (134, 226, 180, 255) if ring or hands else (29, 42, 60, 255) if inside else (0, 0, 0, 0)
            row.extend(pixel)
        rows.append(b"\0" + row)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", n,n,8,6,0,0,0)) + chunk(b"IDAT",zlib.compress(b"".join(rows),9)) + chunk(b"IEND",b"")

sizes = (16, 24, 32, 48, 64, 128, 256)
pngs = [icon_png(n) for n in sizes]
offset = 6 + 16 * len(sizes)
entries = []
for n, png in zip(sizes, pngs):
    entries.append(struct.pack("<BBBBHHII", n % 256, n % 256, 0,0,1,32,len(png),offset))
    offset += len(png)
(assets / "app.ico").write_bytes(struct.pack("<HHH",0,1,len(sizes)) + b"".join(entries) + b"".join(pngs))
rate = 22050
samples = bytearray()
for i in range(int(rate * 1.4)):
    t = i / rate
    value = 0
    for start, frequency in ((0,660),(.22,880)):
        dt = t - start
        if dt >= 0:
            value += math.sin(2 * math.pi * frequency * dt) * min(dt / .012,1) * math.exp(-dt * 5) * .18
    samples.extend(struct.pack("<h", int(max(-1,min(1,value))*32767)))
with wave.open(str(assets / "complete.wav"),"wb") as output:
    output.setparams((1,2,rate,0,"NONE","not compressed"))
    output.writeframes(samples)
print("Generated original app.ico and complete.wav")
