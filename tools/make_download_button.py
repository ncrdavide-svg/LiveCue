"""Generates docs/download-button.png, the big download button at the top of the README.

The LIVE suite's GO green (same button as LiveSlides and the other LIVE apps), rounded, with a download arrow and two lines of text. Drawn at 2x and shown
at half size in the README, so it stays sharp on high-DPI screens.

Usage: python tools/make_download_button.py   (needs Pillow; uses Segoe UI, on every Windows)
"""
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "docs" / "download-button.png"

GO_TOP = (102, 222, 128)
GO_BOTTOM = (34, 160, 70)
DARK = (14, 60, 28)
WHITE = (255, 255, 255)

W, H = 880, 170  # 2x of the size shown in the README (440 x 85)


def main():
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    gradient = Image.new("RGBA", (1, H))
    for y in range(H):
        t = y / (H - 1)
        gradient.putpixel((0, y), tuple(round(a + (b - a) * t) for a, b in zip(GO_TOP, GO_BOTTOM)) + (255,))
    mask = Image.new("L", (W, H), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, W - 1, H - 1), radius=34, fill=255)
    img.paste(gradient.resize((W, H)), (0, 0), mask)
    d = ImageDraw.Draw(img)

    # Download arrow in a white circle.
    cx, cy, r = 92, H // 2, 52
    d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=WHITE)
    d.rounded_rectangle((cx - 9, cy - 32, cx + 9, cy + 8), radius=6, fill=GO_BOTTOM)
    d.polygon([(cx - 28, cy), (cx + 28, cy), (cx, cy + 30)], fill=GO_BOTTOM)
    d.rounded_rectangle((cx - 30, cy + 34, cx + 30, cy + 42), radius=4, fill=GO_BOTTOM)

    big = ImageFont.truetype("segoeuib.ttf", 56)
    small = ImageFont.truetype("segoeui.ttf", 32)
    d.text((176, 30), "SCARICA LIVECUE", font=big, fill=WHITE)
    d.text((178, 104), "Installer per Windows 10/11  ·  ultima versione", font=small, fill=(232, 255, 238))

    OUT.parent.mkdir(parents=True, exist_ok=True)
    img.save(OUT)
    print(f"Wrote {OUT}")


if __name__ == "__main__":
    main()
