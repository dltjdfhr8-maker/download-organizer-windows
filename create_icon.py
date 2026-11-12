"""Build the Windows icon for DownloadOrganizer (Pillow is needed only to rebuild)."""

from pathlib import Path

from PIL import Image, ImageDraw


HERE = Path(__file__).resolve().parent
SCALE = 4
SIZE = 256


def box(x0, y0, x1, y1):
    return tuple(round(v * SCALE) for v in (x0, y0, x1, y1))


canvas = Image.new("RGBA", (SIZE * SCALE, SIZE * SCALE), (0, 0, 0, 0))
background = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
gradient = ImageDraw.Draw(background)
for y in range(background.height):
    fraction = y / (background.height - 1)
    top = (35, 149, 222)
    bottom = (17, 84, 166)
    color = tuple(round(a + (b - a) * fraction) for a, b in zip(top, bottom))
    gradient.line((0, y, background.width, y), fill=color + (255,))

mask = Image.new("L", canvas.size, 0)
mask_draw = ImageDraw.Draw(mask)
mask_draw.rounded_rectangle(box(8, 8, 248, 248), radius=54 * SCALE, fill=255)
canvas.paste(background, (0, 0), mask)

draw = ImageDraw.Draw(canvas)
# One broad, light folder remains recognizable at small notification-area sizes.
draw.rounded_rectangle(box(47, 79, 125, 123), radius=11 * SCALE, fill=(231, 246, 255, 255))
draw.rounded_rectangle(box(47, 101, 209, 199), radius=18 * SCALE, fill=(245, 251, 255, 255))
# The dark arrow describes the action: an arriving download goes into the folder.
draw.rounded_rectangle(box(119, 119, 137, 158), radius=4 * SCALE, fill=(20, 91, 173, 255))
draw.polygon(
    [(99 * SCALE, 150 * SCALE), (157 * SCALE, 150 * SCALE), (128 * SCALE, 180 * SCALE)],
    fill=(20, 91, 173, 255),
)

icon = canvas.resize((SIZE, SIZE), Image.Resampling.LANCZOS)
icon.save(HERE / "DownloadOrganizer-icon.png")
icon.save(
    HERE / "DownloadOrganizer.ico",
    format="ICO",
    sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
)
