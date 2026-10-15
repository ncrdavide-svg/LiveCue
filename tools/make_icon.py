"""Generates src/LiveCue.App/Assets/LiveCue.ico (and a PNG preview).

The icon: a dark tile (like the app's UI) with a cue list on the left, the current cue
highlighted, and the green GO "play" triangle on the right. Small sizes (<= 32 px) use a
simplified drawing so it stays readable in the taskbar and title bar.

Usage: python tools/make_icon.py
"""
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
OUT_ICO = ROOT / "src" / "LiveCue.App" / "Assets" / "LiveCue.ico"
OUT_PNG = ROOT / "src" / "LiveCue.App" / "Assets" / "LiveCue.png"

SUPERSAMPLE = 1024  # drawn at this size, then downscaled with Lanczos for smooth edges

TILE_TOP = (52, 57, 66)
TILE_BOTTOM = (22, 24, 28)
BAR_DIM = (98, 106, 122)
BAR_CURRENT = (255, 179, 0)  # amber: the cue that GO will fire next
GO_TOP = (102, 222, 128)
GO_BOTTOM = (34, 160, 70)


def vertical_gradient(size, top, bottom):
    gradient = Image.new("RGBA", (1, size))
    for y in range(size):
        t = y / (size - 1)
        gradient.putpixel((0, y), tuple(round(a + (b - a) * t) for a, b in zip(top, bottom)) + (255,))
    return gradient.resize((size, size))


def rounded_mask(size, box, radius):
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle(box, radius=radius, fill=255)
    return mask


def triangle_mask(size, points):
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).polygon(points, fill=255)
    return mask


def draw(simplified: bool) -> Image.Image:
    s = SUPERSAMPLE
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))

    # Tile: fills almost the whole canvas, as Windows icons do.
    margin = s * 0.03
    tile_box = (margin, margin, s - margin, s - margin)
    img.paste(vertical_gradient(s, TILE_TOP, TILE_BOTTOM), (0, 0), rounded_mask(s, tile_box, s * 0.2))

    d = ImageDraw.Draw(img)
    if simplified:
        # Two thick bars + a big triangle: still reads as "list + play" at 16 px.
        bars = [(0.36, BAR_DIM), (0.56, BAR_CURRENT)]
        bar_x0, bar_x1, bar_h = 0.15, 0.40, 0.13
        tri = [(0.47, 0.22), (0.47, 0.78), (0.88, 0.50)]
    else:
        bars = [(0.30, BAR_DIM), (0.47, BAR_CURRENT), (0.64, BAR_DIM)]
        bar_x0, bar_x1, bar_h = 0.15, 0.44, 0.095
        tri = [(0.52, 0.25), (0.52, 0.75), (0.86, 0.50)]

    for y, color in bars:
        box = (s * bar_x0, s * (y - bar_h / 2), s * bar_x1, s * (y + bar_h / 2))
        d.rounded_rectangle(box, radius=s * bar_h / 2, fill=color)

    points = [(s * x, s * y) for x, y in tri]
    img.paste(vertical_gradient(s, GO_TOP, GO_BOTTOM), (0, 0), triangle_mask(s, points))
    return img


def main():
    full = draw(simplified=False)
    small = draw(simplified=True)

    sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
    frames = [(small if size <= 32 else full).resize((size, size), Image.LANCZOS) for size in sizes]

    OUT_ICO.parent.mkdir(parents=True, exist_ok=True)
    frames[-1].save(OUT_ICO, format="ICO", sizes=[(f.width, f.height) for f in frames], append_images=frames[:-1])
    full.resize((512, 512), Image.LANCZOS).save(OUT_PNG)
    print(f"Wrote {OUT_ICO} and {OUT_PNG}")


if __name__ == "__main__":
    main()
