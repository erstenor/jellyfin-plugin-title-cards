#!/usr/bin/env python3
"""Draw the plugin logo: a Title Cards card made of abstract tiles, so it shows what the plugin
does without using anyone's artwork.

    python build/make_logo.py [output.png]

Needs Pillow and DejaVu Sans Bold (the font Jellyfin's library images use on Linux).
"""

import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

WIDTH, HEIGHT = 960, 540
FONT = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
# Jellyfin's purple and blue, plus two warmer tones, as diagonal gradients.
TILES = [
    ((0, 0, 480, 270), (170, 92, 195), (82, 40, 130)),
    ((480, 0, 960, 270), (0, 164, 220), (20, 70, 140)),
    ((0, 270, 480, 540), (235, 120, 70), (150, 40, 80)),
    ((480, 270, 960, 540), (60, 170, 130), (20, 80, 90)),
]


def gradient(size, start, end):
    width, height = size
    tile = Image.new("RGB", size)
    pixels = tile.load()
    for y in range(height):
        for x in range(width):
            t = (x / width + y / height) / 2
            pixels[x, y] = tuple(int(a + (b - a) * t) for a, b in zip(start, end))
    return tile


def main() -> None:
    output = Path(sys.argv[1] if len(sys.argv) > 1 else "logo.png")
    card = Image.new("RGB", (WIDTH, HEIGHT))
    for (left, top, right, bottom), start, end in TILES:
        tile = gradient((right - left, bottom - top), start, end)
        # A soft shape per tile, so it reads as a picture rather than a flat swatch.
        shape = Image.new("L", tile.size, 0)
        ImageDraw.Draw(shape).ellipse((tile.width * 0.45, tile.height * 0.15, tile.width * 1.1, tile.height * 1.2), fill=90)
        tile = Image.composite(Image.new("RGB", tile.size, (255, 255, 255)), tile, shape.filter(ImageFilter.GaussianBlur(40)))
        card.paste(tile, (left, top))

    # Same finish as a card: black layer at alpha 0x78, title centred on Jellyfin's baseline rule.
    card = Image.alpha_composite(card.convert("RGBA"), Image.new("RGBA", card.size, (0, 0, 0, 0x78)))
    title = "Title Cards"
    font = ImageFont.truetype(FONT, 112)
    x_box = font.getbbox("x", anchor="ls")
    ImageDraw.Draw(card).text((WIDTH / 2, HEIGHT / 2 + (x_box[3] - x_box[1]) / 2), title, font=font, fill="white", anchor="ms")
    card.convert("RGB").save(output, optimize=True)
    print(output)


if __name__ == "__main__":
    main()
