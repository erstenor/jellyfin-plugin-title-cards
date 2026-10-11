#!/usr/bin/env python3
"""Render README example cards for library types without real artwork to hand.

Generates original abstract artwork (album and audiobook covers, book covers, landscape
"photos"), then builds cards from it with the same recipe as the plugin: text check behind the
title, largest fillable layout, square/portrait art shown whole on a blurred fill, alpha 0x78
layer and the title in DejaVu Sans Bold 112px on Jellyfin's baseline rule.

Also draws Jellyfin's default card for a view with no image (used for Live TV in the
before/after image): the dark theme's colour for the name, with the Material icon centred.

    pip install pillow rapidocr onnxruntime
    python build/render_examples.py out/ --icon-font path/to/MaterialIcons-Regular.ttf

The icon font ships with jellyfin-web (material-design-icons-iconfont, Apache-2.0).
"""

import argparse
import logging
import math
import random
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont, ImageOps

FONT = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
CARD = (960, 540)
LAYOUTS = {
    4: [(0, 0, 480, 270), (480, 0, 960, 270), (0, 270, 480, 540), (480, 270, 960, 540)],
    3: [(0, 0, 480, 270), (480, 0, 960, 270), (0, 270, 960, 540)],
    2: [(0, 0, 480, 540), (480, 0, 960, 540)],
    1: [(0, 0, 960, 540)],
}
MARGIN = 24
# jellyfin-web dark theme .defaultCardBackground1..5
DEFAULT_CARD_COLOURS = ["#00455c", "#44bae1", "#00a4db", "#1c4c5c", "#007ea8"]
PALETTES = [
    [(244, 162, 89), (217, 72, 95), (79, 37, 103)],
    [(42, 157, 143), (233, 196, 106), (38, 70, 83)],
    [(255, 209, 102), (6, 214, 160), (17, 138, 178)],
    [(239, 71, 111), (255, 214, 165), (40, 30, 60)],
    [(131, 197, 190), (237, 246, 249), (0, 109, 119)],
    [(255, 112, 67), (255, 238, 173), (35, 35, 35)],
    [(124, 77, 255), (0, 229, 255), (20, 16, 48)],
    [(180, 220, 120), (60, 120, 80), (240, 240, 220)],
]


# ---------------------------------------------------------------- generated artwork

def gradient(size, top, bottom, diagonal=False):
    width, height = size
    base = Image.new("RGB", (1, 256))
    for y in range(256):
        t = y / 255
        base.putpixel((0, y), tuple(int(a + (b - a) * t) for a, b in zip(top, bottom)))
    if not diagonal:
        return base.resize(size, Image.Resampling.BILINEAR)
    # Rotate an oversized gradient and crop the middle, so no empty corners show.
    side = int(math.hypot(width, height)) + 2
    image = base.resize((side, side), Image.Resampling.BILINEAR).rotate(35, resample=Image.Resampling.BICUBIC)
    left, top = (side - width) // 2, (side - height) // 2
    return image.crop((left, top, left + width, top + height))


def grain(image, rng, amount=10):
    noise = Image.effect_noise(image.size, amount).convert("RGB")
    return Image.blend(image, noise, 0.06)


def album_cover(rng, style):
    size = (1000, 1000)
    a, b, c = rng.choice(PALETTES)
    image = gradient(size, a, c, diagonal=True)
    draw = ImageDraw.Draw(image, "RGBA")
    if style == 0:  # concentric rings
        for r in range(480, 20, -36):
            colour = b if (r // 36) % 2 else a
            draw.ellipse((500 - r, 500 - r, 500 + r, 500 + r), outline=colour + (200,), width=14)
    elif style == 1:  # sun over horizon bands
        draw.ellipse((260, 180, 740, 660), fill=b + (255,))
        for i, y in enumerate(range(560, 1000, 44)):
            draw.rectangle((0, y, 1000, y + 22 + i * 2), fill=c + (255,))
    elif style == 2:  # bokeh
        for _ in range(40):
            x, y, r = rng.randint(0, 1000), rng.randint(0, 1000), rng.randint(20, 140)
            draw.ellipse((x - r, y - r, x + r, y + r), fill=rng.choice([a, b]) + (rng.randint(60, 150),))
        image = image.filter(ImageFilter.GaussianBlur(6))
    elif style == 3:  # waves
        for k in range(14):
            points = [(x, 500 + 260 * math.sin(x / 140 + k * 0.45) * math.cos(k / 5) + (k - 7) * 30) for x in range(0, 1001, 10)]
            draw.line(points, fill=(b if k % 2 else a) + (220,), width=10)
    elif style == 4:  # triangles
        for _ in range(26):
            x, y = rng.randint(-100, 1000), rng.randint(-100, 1000)
            s = rng.randint(120, 380)
            draw.polygon([(x, y), (x + s, y + rng.randint(-60, 60)), (x + rng.randint(0, s), y + s)],
                         fill=rng.choice([a, b, c]) + (rng.randint(110, 210),))
    else:  # halftone-ish grid of dots fading out
        for gx in range(0, 1000, 50):
            for gy in range(0, 1000, 50):
                r = 22 * (1 - math.hypot(gx - 300, gy - 700) / 1100)
                if r > 2:
                    draw.ellipse((gx + 25 - r, gy + 25 - r, gx + 25 + r, gy + 25 + r), fill=b + (230,))
    return grain(image, rng)


def book_cover(rng):
    size = (1000, 1500)
    a, b, c = rng.choice(PALETTES)
    image = gradient(size, c, a)
    draw = ImageDraw.Draw(image, "RGBA")
    kind = rng.randint(0, 3)
    if kind == 0:  # moon over a dark block
        draw.ellipse((180, 300, 820, 940), fill=b + (255,))
        draw.rectangle((0, 1000, 1000, 1500), fill=c + (255,))
    elif kind == 1:  # bold stripes
        for i in range(8):
            draw.rectangle((0, 260 + i * 120, 1000, 320 + i * 120), fill=(b if i % 2 else a) + (235,))
    elif kind == 2:  # overlapping circles
        for i, (x, y, r) in enumerate([(380, 620, 300), (640, 820, 260), (460, 1020, 200)]):
            draw.ellipse((x - r, y - r, x + r, y + r), fill=[a, b, c][i] + (210,))
    else:  # mountain peak
        draw.polygon([(0, 1500), (500, 520), (1000, 1500)], fill=b + (255,))
        draw.polygon([(320, 870), (500, 520), (680, 870), (580, 800), (500, 860), (420, 800)], fill=(250, 250, 250, 255))
    draw.rectangle((40, 40, 960, 1460), outline=(255, 255, 255, 90), width=6)
    return grain(image, rng)


def landscape(rng):
    width, height = 1600, 900
    sky_top, sky_bottom, ground = rng.choice([
        ((24, 40, 82), (247, 150, 70), (30, 30, 46)),
        ((90, 160, 220), (220, 236, 245), (40, 70, 50)),
        ((10, 14, 40), (60, 50, 120), (12, 12, 24)),
        ((120, 80, 160), (255, 190, 120), (60, 40, 70)),
    ])
    image = gradient((width, height), sky_top, sky_bottom)
    draw = ImageDraw.Draw(image, "RGBA")
    sun_x, sun_y = rng.randint(300, 1300), rng.randint(250, 500)
    draw.ellipse((sun_x - 90, sun_y - 90, sun_x + 90, sun_y + 90), fill=(255, 236, 200, 230))
    for layer in range(4):
        base = 520 + layer * 90
        shade = tuple(int(g + (s - g) * (0.6 - layer * 0.18)) for g, s in zip(ground, sky_bottom))
        y = base
        points = [(0, height)]
        for x in range(0, width + 40, 40):
            y = max(base - 220, min(base + 60, y + rng.randint(-45, 45)))
            points.append((x, y))
        points.append((width, height))
        draw.polygon(points, fill=shade + (255,))
    if rng.random() < 0.5:  # lake
        draw.rectangle((0, 780, width, height), fill=sky_bottom + (140,))
    return grain(image.filter(ImageFilter.GaussianBlur(1)), rng)


# ---------------------------------------------------------------- card recipe

def title_font(title):
    font = ImageFont.truetype(FONT, 112)
    width = font.getlength(title)
    if width > CARD[0] * 0.95:
        font = ImageFont.truetype(FONT, int(0.9 * CARD[0] * 112 / width))
    return font


def title_origin(font):
    x_box = font.getbbox("x", anchor="ls")
    return CARD[0] / 2, CARD[1] / 2 + (x_box[3] - x_box[1]) / 2


def title_band(title):
    font = title_font(title)
    x, y = title_origin(font)
    left, top, right, bottom = font.getbbox(title, anchor="ms")
    return (x + left - MARGIN, y + top - MARGIN, x + right + MARGIN, y + bottom + MARGIN)


def overlaps(a, b):
    return a[0] < b[2] and b[0] < a[2] and a[1] < b[3] and b[1] < a[3]


def fit_tile(image, size):
    ratio = (image.width / image.height) / (size[0] / size[1])
    if 0.8 <= ratio <= 1.25:
        return ImageOps.fit(image, size, method=Image.Resampling.LANCZOS)
    background = ImageOps.fit(image, size, method=Image.Resampling.LANCZOS)
    background = background.filter(ImageFilter.GaussianBlur(size[0] / 24)).point(lambda v: int(v * 0.55))
    foreground = ImageOps.contain(image, size, method=Image.Resampling.LANCZOS)
    background.paste(foreground, ((size[0] - foreground.width) // 2, (size[1] - foreground.height) // 2))
    return background


def text_boxes(ocr, image, size):
    scale = max(1.0, 960 / size[0])
    tile = fit_tile(image, (round(size[0] * scale), round(size[1] * scale)))
    return [tuple(v / scale for v in box) for box in ocr(tile)]


def pick(images, title, ocr):
    band = title_band(title)
    for count in (4, 3, 2, 1):
        layout = LAYOUTS[count]
        placed = [None] * count
        for image in images:
            for position, box in enumerate(layout):
                if placed[position] is not None:
                    continue
                size = (box[2] - box[0], box[3] - box[1])
                clipped = (max(band[0], box[0]) - box[0], max(band[1], box[1]) - box[1],
                           min(band[2], box[2]) - box[0], min(band[3], box[3]) - box[1])
                keepout = clipped if overlaps(band, box) else None
                if keepout is None or not any(overlaps(t, keepout) for t in text_boxes(ocr, image, size)):
                    placed[position] = image
                    break
            if all(p is not None for p in placed):
                return list(zip(placed, layout))
    return []


def compose(tiles, title):
    card = Image.new("RGB", CARD)
    for image, box in tiles:
        card.paste(fit_tile(image, (box[2] - box[0], box[3] - box[1])), box[:2])
    card = Image.alpha_composite(card.convert("RGBA"), Image.new("RGBA", CARD, (0, 0, 0, 0x78)))
    font = title_font(title)
    ImageDraw.Draw(card).text(title_origin(font), title, font=font, fill="white", anchor="ms")
    return card.convert("RGB")


def default_card(name, icon_font, codepoint):
    """Jellyfin web's card for a view with no image (dark theme)."""
    character = str(ord(name[len(name) // 2]))
    index = int(str(sum(int(d) for d in character))[-1]) % 5
    card = Image.new("RGB", CARD, DEFAULT_CARD_COLOURS[index])
    font = ImageFont.truetype(icon_font, int(CARD[0] * 0.22))
    ImageDraw.Draw(card).text((CARD[0] / 2, CARD[1] / 2), codepoint, font=font, fill="white", anchor="mm")
    return card


def make_ocr():
    from rapidocr import RapidOCR

    logging.getLogger("RapidOCR").setLevel(logging.ERROR)
    engine = RapidOCR(params={"Global.log_level": "error"})

    def detect(image):
        result = engine(image)
        if result.boxes is None:
            return []
        out = []
        for box, score in zip(result.boxes, result.scores or []):
            if float(score) >= 0.5:
                xs, ys = [float(p[0]) for p in box], [float(p[1]) for p in box]
                out.append((min(xs), min(ys), max(xs), max(ys)))
        return out

    return detect


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("out", type=Path)
    parser.add_argument("--icon-font", required=True)
    parser.add_argument("--seed", type=int, default=12)
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)
    rng = random.Random(args.seed)
    ocr = make_ocr()

    libraries = {
        "Music": [album_cover(rng, i % 6) for i in range(8)],
        "Audiobooks": [album_cover(rng, (i + 3) % 6) for i in range(8)],
        "Books": [book_cover(rng) for _ in range(8)],
        "Photos": [landscape(rng) for _ in range(8)],
    }
    for title, images in libraries.items():
        rng.shuffle(images)
        tiles = pick(images, title, ocr)
        compose(tiles, title).save(args.out / f"example-{title.lower()}.png")
        print(f"{title}: {len(tiles)} tiles")

    # Material Icons "live_tv" is U+E639.
    default_card("Live TV", args.icon_font, "").save(args.out / "default-livetv.png")
    print("default Live TV card")


if __name__ == "__main__":
    main()
