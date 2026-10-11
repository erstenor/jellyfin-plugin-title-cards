#!/usr/bin/env python3
"""Build the installable plugin zip, and optionally add it to the repository manifest.

Publishes the plugin, keeps only what Jellyfin needs (the server provides SkiaSharp and its own
libraries), adds ONNX Runtime's native library for Linux x64 and ARM64 and the logo, and writes
meta.json.

    python build/package.py --version 0.1.0.0
        -> dist/title-cards_0.1.0.0.zip

    python build/package.py --version 0.1.0.0 --manifest --changelog "First release"
        -> also adds the version to manifest.json, pointing at the GitHub release asset
           https://github.com/<REPOSITORY>/releases/download/v<version>/title-cards_<version>.zip
"""

import argparse
import hashlib
import json
import subprocess
import tempfile
import zipfile
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PROJECT = ROOT / "src" / "Jellyfin.Plugin.TitleCards"
REPOSITORY = "erstenor/jellyfin-plugin-title-cards"
GUID = "c99c31b2-247e-4280-85a2-824757735f02"
TARGET_ABI = "12.2.0.0"
NAME = "Title Cards"
OWNER = "erstenor"
OVERVIEW = "Collage title cards for every home-screen library"
DESCRIPTION = (
    "Builds each home-screen library image as a collage of that library's own artwork, with the library "
    "name drawn the way Jellyfin draws it. Covers every library, Live TV, recordings and grouped views, "
    "refreshes nightly, and keeps logos and lettering out from behind the title (text detection on Linux "
    "x64/ARM64)."
)
IMAGE_URL = f"https://raw.githubusercontent.com/{REPOSITORY}/main/src/Jellyfin.Plugin.TitleCards/logo.png"
FILES = [
    "Jellyfin.Plugin.TitleCards.dll",
    "Microsoft.ML.OnnxRuntime.dll",
    "System.Numerics.Tensors.dll",
    # Native text detection runtime; other platforms run without the text filter.
    "runtimes/linux-x64/native/libonnxruntime.so",
    "runtimes/linux-arm64/native/libonnxruntime.so",
]


def build(version: str, changelog: str, timestamp: str) -> Path:
    with tempfile.TemporaryDirectory() as temp:
        publish = Path(temp) / "publish"
        subprocess.run(
            ["dotnet", "publish", str(PROJECT), "-c", "Release", "-o", str(publish),
             f"-p:Version={version}", f"-p:AssemblyVersion={version}", f"-p:FileVersion={version}"],
            check=True,
        )
        meta = {
            "category": "General",
            "changelog": changelog,
            "description": DESCRIPTION,
            "guid": GUID,
            "name": NAME,
            "overview": OVERVIEW,
            "owner": OWNER,
            "targetAbi": TARGET_ABI,
            "timestamp": timestamp,
            "version": version,
            "status": "Active",
            "autoUpdate": True,
            "imagePath": "",
            "assemblies": [],
        }
        dist = ROOT / "dist"
        dist.mkdir(exist_ok=True)
        archive = dist / f"title-cards_{version}.zip"
        with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as zip_file:
            for name in FILES:
                zip_file.write(publish / name, name)
            zip_file.write(PROJECT / "logo.png", "logo.png")
            zip_file.writestr("meta.json", json.dumps(meta, indent=2))
    return archive


def update_manifest(version: str, changelog: str, timestamp: str, checksum: str, archive: Path) -> Path:
    path = ROOT / "manifest.json"
    manifest = json.loads(path.read_text(encoding="utf-8")) if path.exists() else []
    entry = next((plugin for plugin in manifest if plugin["guid"] == GUID), None)
    if entry is None:
        entry = {"guid": GUID, "versions": []}
        manifest.append(entry)
    entry.update({
        "name": NAME,
        "description": DESCRIPTION,
        "overview": OVERVIEW,
        "owner": OWNER,
        "category": "General",
        "imageUrl": IMAGE_URL,
    })
    entry["versions"] = [v for v in entry["versions"] if v["version"] != version]
    entry["versions"].insert(0, {
        "version": version,
        "changelog": changelog,
        "targetAbi": TARGET_ABI,
        "sourceUrl": f"https://github.com/{REPOSITORY}/releases/download/v{version}/{archive.name}",
        "checksum": checksum,
        "timestamp": timestamp,
    })
    path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    return path


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--version", default="0.1.0.0")
    parser.add_argument("--changelog", default="")
    parser.add_argument("--manifest", action="store_true", help="add this version to manifest.json")
    args = parser.parse_args()

    timestamp = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    archive = build(args.version, args.changelog, timestamp)
    checksum = hashlib.md5(archive.read_bytes()).hexdigest()
    print(f"{archive} ({archive.stat().st_size / 1_048_576:.1f} MB, md5 {checksum})")
    if args.manifest:
        print(f"updated {update_manifest(args.version, args.changelog, timestamp, checksum, archive)}")


if __name__ == "__main__":
    main()
