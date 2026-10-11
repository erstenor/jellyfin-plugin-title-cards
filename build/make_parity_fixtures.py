#!/usr/bin/env python3
"""Make fixtures for DetectorParityTests: real artwork plus RapidOCR's verdict on it.

Downloads Live TV guide images and library backdrops from a Jellyfin server, fits each to a
960x540 card, runs RapidOCR (detection + recognition, confidence >= 0.5) and records whether any
text overlaps the "Live TV" title band. The plugin's detector is then checked against these
verdicts. The artwork belongs to its owners, so keep the fixtures out of the repository.

    pip install pillow rapidocr onnxruntime
    JELLYFIN_URL=http://localhost:8096 JELLYFIN_API_KEY=... python build/make_parity_fixtures.py fixtures/
    TITLECARDS_FIXTURES=fixtures dotnet test

Needs DejaVu Sans Bold (fonts-dejavu-core) to place the title band as Jellyfin does on Linux.
"""

import io
import json
import logging
import os
import random
import sys
import urllib.parse
import urllib.request
from pathlib import Path

from PIL import Image, ImageFont, ImageOps

FONT = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
TITLE = "Live TV"
MARGIN = 24
LIVE_TV_IMAGES = 45
BACKDROPS_PER_LIBRARY = 10


def main() -> None:
    out = Path(sys.argv[1] if len(sys.argv) > 1 else "fixtures")
    out.mkdir(parents=True, exist_ok=True)
    base = os.environ.get("JELLYFIN_URL", "http://localhost:8096").rstrip("/")
    headers = {"Authorization": f'MediaBrowser Token="{os.environ["JELLYFIN_API_KEY"]}"'}

    def get(path, query=None, raw=False):
        url = base + path + ("?" + urllib.parse.urlencode(query) if query else "")
        data = urllib.request.urlopen(urllib.request.Request(url, headers=headers), timeout=30).read()
        return data if raw else json.loads(data)

    from rapidocr import RapidOCR

    logging.getLogger("RapidOCR").setLevel(logging.ERROR)
    engine = RapidOCR(params={"Global.log_level": "error"})

    # Title band exactly as the plugin draws it: 112px, centred, baseline at H/2 + x-height/2.
    font = ImageFont.truetype(FONT, 112)
    x_box = font.getbbox("x", anchor="ls")
    baseline = 270 + (x_box[3] - x_box[1]) / 2
    left, top, right, bottom = font.getbbox(TITLE, anchor="ms")
    band = (480 + left - MARGIN, baseline + top - MARGIN, 480 + right + MARGIN, baseline + bottom + MARGIN)

    user_id = next(u for u in get("/Users") if u.get("Policy", {}).get("IsAdministrator"))["Id"]
    programs = [
        p for p in get("/LiveTv/Programs", {"UserId": user_id, "IsAiring": "true", "Limit": "300",
                                            "Fields": "PrimaryImageAspectRatio"}).get("Items", [])
        if (p.get("ImageTags") or {}).get("Primary") and (p.get("PrimaryImageAspectRatio") or 0) > 1.3
    ]
    random.Random(7).shuffle(programs)
    picked, seen = [], set()
    for program in programs:
        if program["Name"] not in seen and len(picked) < LIVE_TV_IMAGES:
            seen.add(program["Name"])
            picked.append((program["Id"], program["Name"], f"/Items/{program['Id']}/Images/Primary"))
    for view in get("/UserViews", {"UserId": user_id}).get("Items", []):
        if view.get("CollectionType") in ("tvshows", "movies"):
            items = get("/Items", {"UserId": user_id, "ParentId": view["Id"], "Recursive": "true",
                                   "IncludeItemTypes": "Series,Movie", "SortBy": "Random",
                                   "Limit": str(BACKDROPS_PER_LIBRARY), "Fields": "BackdropImageTags"})["Items"]
            picked += [(i["Id"], i["Name"], f"/Items/{i['Id']}/Images/Backdrop/0") for i in items if i.get("BackdropImageTags")]

    expected = {"band": list(band), "images": []}
    for item_id, name, path in picked:
        source = Image.open(io.BytesIO(get(path, {"maxWidth": "1920"}, raw=True))).convert("RGB")
        image = ImageOps.fit(source, (960, 540), method=Image.Resampling.LANCZOS)
        image.save(out / f"{item_id}.png")
        result = engine(image)
        boxes = []
        if result.boxes is not None:
            for box, score in zip(result.boxes, result.scores or []):
                if float(score) >= 0.5:
                    xs, ys = [float(p[0]) for p in box], [float(p[1]) for p in box]
                    boxes.append((min(xs), min(ys), max(xs), max(ys)))
        reject = any(b[0] < band[2] and band[0] < b[2] and b[1] < band[3] and band[1] < b[3] for b in boxes)
        expected["images"].append({"file": f"{item_id}.png", "name": name, "reject": reject, "boxes": boxes})

    (out / "expected.json").write_text(json.dumps(expected, indent=1))
    rejected = sum(entry["reject"] for entry in expected["images"])
    print(f"{len(expected['images'])} images in {out}, {rejected} with text behind the title per RapidOCR")


if __name__ == "__main__":
    main()
