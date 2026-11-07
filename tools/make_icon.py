"""Draw the original monitor/sun mark and package Windows icon sizes. Requires Pillow."""
from pathlib import Path
from PIL import Image, ImageDraw
import math

root = Path(__file__).resolve().parents[1]
assets = root / "assets"
assets.mkdir(exist_ok=True)
size, factor = 256, 4
image = Image.new("RGBA", (size * factor, size * factor))
draw = ImageDraw.Draw(image)

def box(values):
    return tuple(round(v * factor) for v in values)

frame = "#6e7569"
amber = "#E4B56B"
draw.rounded_rectangle(box((16, 24, 240, 200)), radius=24 * factor,
                       fill="#202121", outline=frame, width=12 * factor)
draw.line(box((128, 200, 128, 232)), fill=frame, width=12 * factor)
draw.rounded_rectangle(box((80, 226, 176, 238)), radius=6 * factor, fill=frame)
draw.ellipse(box((100, 84, 156, 140)), fill=amber)
for index in range(8):
    angle = index * math.pi / 4
    x1, y1 = 128 + 43 * math.cos(angle), 112 + 43 * math.sin(angle)
    x2, y2 = 128 + 53 * math.cos(angle), 112 + 53 * math.sin(angle)
    draw.line(box((x1, y1, x2, y2)), fill=amber, width=9 * factor)
    for x, y in ((x1, y1), (x2, y2)):
        draw.ellipse(box((x-4.5, y-4.5, x+4.5, y+4.5)), fill=amber)
image = image.resize((256, 256), Image.Resampling.LANCZOS)
image.save(assets / "Brightness.png")
image.save(assets / "Brightness.ico", sizes=[(s, s) for s in (16, 20, 24, 32, 40, 48, 64, 128, 256)])
print("Generated Brightness.ico (9 sizes) and Brightness.png")
