#!/usr/bin/env python3
"""Download openly licensed artwork for the README's example cards, with credits.

    python build/fetch_example_art.py art/
    python build/fetch_example_art.py art/ --only audiobooks --from-offset 13000   # top up one library
    python build/render_examples.py out/ --sources art/ --icon-font MaterialIcons-Regular.ttf

Writes art/<library>/*.jpg and art/credits.json (title, source page, licence per image).

Sources, all public domain or CC0:
- Books: Standard Ebooks covers (CC0; cover paintings are public domain).
- Audiobooks: LibriVox cover art, hosted on the Internet Archive ("CD cover art ... in the public
  domain", LibriVox copyright policy).
- Music: record labels published before 1929 (public domain in the US) and NASA's Voyager
  Golden Record cover, from Wikimedia Commons.
- Photos: NASA photographs of Earth (NASA imagery is generally not copyrighted).
"""

import argparse
import io
import json
import re
import sys
import urllib.parse
import urllib.request
from pathlib import Path

from PIL import Image

HEADERS = {"User-Agent": "TitleCardsReadme/0.1 (https://github.com/erstenor/jellyfin-plugin-title-cards)"}
PER_LIBRARY = 10
MUSIC_FILES = [
    "File:The Sounds of Earth Record Cover - GPN-2000-001978.jpg",
    "File:The Sounds of Earth - GPN-2000-001976.jpg",
    "File:DutchDalyGandTlabel.jpg",
    "File:Label of salam-e shah 1910.jpg",
    "File:Jacob Hoffman Doina and Hora label.tif",
    "File:Pussy Cat Rag Halfway House Orch Label OKeh.jpg",
    "File:Ostrich Walk Tram Bix Okeh 40822.jpg",
]
MUSIC_SEARCH = 'Label of a 78 rpm disc recording "On Honolulu Bay"'
NASA_QUERIES = ["earth observation landscape", "aurora from space station", "earth from orbit coastline"]


def get(url, raw=False):
    with urllib.request.urlopen(urllib.request.Request(url, headers=HEADERS), timeout=60) as response:
        data = response.read()
    return data if raw else json.loads(data)


def save(out, library, name, data, credit, credits):
    image = Image.open(io.BytesIO(data)).convert("RGB")
    image.thumbnail((1600, 1600))
    path = out / library / (re.sub(r"[^A-Za-z0-9]+", "-", name).strip("-")[:60] + ".jpg")
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path, quality=92)
    credits.setdefault(library, []).append({"file": path.name, **credit})
    print(f"{library}: {credit['title']}")


def commons(titles):
    query = urllib.parse.urlencode({
        "action": "query", "format": "json", "titles": "|".join(titles), "prop": "imageinfo",
        "iiprop": "url|extmetadata", "iiurlwidth": "1600", "iiextmetadatafilter": "LicenseShortName|ObjectName|DateTimeOriginal",
    })
    return get("https://commons.wikimedia.org/w/api.php?" + query)["query"]["pages"].values()


def music(out, credits):
    search = get("https://commons.wikimedia.org/w/api.php?" + urllib.parse.urlencode({
        "action": "query", "format": "json", "list": "search", "srsearch": MUSIC_SEARCH, "srnamespace": "6", "srlimit": "1"}))
    titles = MUSIC_FILES + [hit["title"] for hit in search["query"]["search"]]
    for page in commons(titles):
        info = page["imageinfo"][0]
        meta = info["extmetadata"]
        if not meta.get("LicenseShortName", {}).get("value", "").startswith("Public domain"):
            continue
        name = re.sub(r"<[^>]+>", "", meta.get("ObjectName", {}).get("value", page["title"][5:]))
        save(out, "music", name, get(info["thumburl"], raw=True),
             {"title": name, "source": info["descriptionurl"], "license": "Public domain"}, credits)


def books(out, credits):
    feed = get("https://standardebooks.org/feeds/atom/new-releases", raw=True).decode("utf-8")
    for entry in re.findall(r"<entry>(.*?)</entry>", feed, re.S)[:PER_LIBRARY]:
        title = re.sub(r"<[^>]+>", "", re.search(r"<title[^>]*>(.*?)</title>", entry, re.S).group(1)).strip()
        page = re.search(r'<link[^>]+href="(https://standardebooks.org/ebooks/[^"]+)"', entry).group(1)
        thumb = re.search(r'(https://standardebooks.org/[^"]*cover-thumbnail\.jpg)', entry)
        cover = thumb.group(1).replace("cover-thumbnail.jpg", "cover.jpg") if thumb else page + "/downloads/cover.jpg"
        save(out, "books", title, get(cover, raw=True),
             {"title": title, "source": page, "license": "CC0 (Standard Ebooks); painting public domain"}, credits)


def audiobooks(out, credits, start=12000):
    # Early catalogue entries have no Internet Archive link; later ones do. The feed is paged
    # in small requests, as large extended requests can fail. Many items' covers are missing from
    # Internet Archive storage, so this can take a while.
    have = {entry["title"] for entry in credits.get("audiobooks", [])}
    for offset in range(start, start + 6000, 20):
        try:
            books = get(f"https://librivox.org/api/feed/audiobooks?format=json&extended=1&limit=20&offset={offset}")["books"]
        except Exception as error:
            print(f"audiobooks: feed page {offset}: {error}", file=sys.stderr)
            continue
        for book in books:
            if len(credits.get("audiobooks", [])) >= PER_LIBRARY:
                return
            identifier = (book.get("url_iarchive") or "").rstrip("/").split("/")[-1]
            if not identifier or ".poem" in identifier or book["title"] in have:  # single poems mostly use a generic cover
                continue
            try:
                metadata = get(f"https://archive.org/metadata/{identifier}")
                files = metadata.get("files", [])
                # The cover is <name>.jpg; skip thumbnails, the "itemimage" copy and the archive's own tile.
                jpegs = [f for f in files if f.get("name", "").lower().endswith(".jpg")
                         and not any(part in f["name"].lower() for part in ("thumb", "itemimage", "__ia"))]
                if not jpegs:
                    continue
                cover = urllib.parse.quote(max(jpegs, key=lambda f: int(f.get("size", 0)))["name"])
                # Storage servers sometimes fail for an item, so try each server it lists.
                servers = dict.fromkeys([metadata.get("d2"), metadata.get("d1"), *(metadata.get("workable_servers") or [])])
                urls = [f"https://{server}{metadata['dir']}/{cover}" for server in servers if server and metadata.get("dir")]
                data = None
                for url in urls + [f"https://archive.org/download/{identifier}/{cover}"]:
                    try:
                        data = get(url, raw=True)
                        break
                    except Exception:
                        continue
                if data is None or min(Image.open(io.BytesIO(data)).size) < 500:
                    continue
            except Exception as error:
                print(f"audiobooks: {identifier}: {error}", file=sys.stderr)
                continue
            save(out, "audiobooks", book["title"], data,
                 {"title": book["title"], "source": book.get("url_librivox") or book["url_iarchive"], "license": "Public domain (LibriVox)"}, credits)


def photos(out, credits):
    for query in NASA_QUERIES:
        result = get("https://images-api.nasa.gov/search?" + urllib.parse.urlencode({"q": query, "media_type": "image"}))
        for item in result["collection"]["items"][:6]:
            if len(credits.get("photos", [])) >= PER_LIBRARY:
                return
            data = item["data"][0]
            # The item's href is its asset list: URLs of the original and resized images.
            assets = get(item["href"])
            large = next((url for url in assets if url.endswith("~large.jpg")), None) or next((url for url in assets if url.endswith(".jpg")), None)
            if not large:
                continue
            image = Image.open(io.BytesIO(get(large.replace("http://", "https://"), raw=True)))
            if image.width / image.height < 1.3:
                continue
            buffer = io.BytesIO()
            image.convert("RGB").save(buffer, "JPEG", quality=92)
            save(out, "photos", data["title"], buffer.getvalue(),
                 {"title": data["title"], "source": f"https://images.nasa.gov/details/{data['nasa_id']}", "license": "Public domain (NASA)"}, credits)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("out", type=Path, nargs="?", default=Path("art"))
    parser.add_argument("--only", nargs="+", choices=["books", "audiobooks", "music", "photos"])
    parser.add_argument("--from-offset", type=int, default=12000, help="LibriVox catalogue position to start at")
    args = parser.parse_args()
    out = args.out
    out.mkdir(parents=True, exist_ok=True)
    credits_path = out / "credits.json"
    # Keep what earlier runs fetched, so one library can be topped up on its own.
    credits = json.loads(credits_path.read_text()) if credits_path.exists() else {}
    for fetch in (books, audiobooks, music, photos):
        if args.only and fetch.__name__ not in args.only:
            continue
        if not args.only:
            credits.pop(fetch.__name__, None)
        try:
            if fetch is audiobooks:
                fetch(out, credits, args.from_offset)
            else:
                fetch(out, credits)
        except Exception as error:  # keep whatever the other sources gave
            print(f"{fetch.__name__}: {error}", file=sys.stderr)
        credits_path.write_text(json.dumps(credits, indent=1))
    print({library: len(items) for library, items in credits.items()})


if __name__ == "__main__":
    main()
