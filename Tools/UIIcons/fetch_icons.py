#!/usr/bin/env python3
"""Fetch, rasterise and credit the UI icons listed in icons.json.

Sources (both free, both fetched from the authors' official GitHub repositories):
  * Google Material Icons, round style - https://github.com/google/material-design-icons (Apache-2.0)
  * game-icons.net - https://github.com/game-icons/icons (CC BY 3.0; a few authors CC0)

Every icon is rendered as a white glyph on transparency, so a UI Image tints it with the theme
colour. Material icons go to 96 px (matching the existing set), game icons to 128 px. The script
also writes CREDITS.md next to the game icons, which CC BY requires.

Usage (needs `pip install cairosvg`):
    python3 Tools/UIIcons/fetch_icons.py                    # clones the sources into a temp dir
    python3 Tools/UIIcons/fetch_icons.py --cache ~/icon-src # reuse clones between runs
Unity then imports the PNGs; the game HUD authoring scripts set them up as sprites.
"""
import argparse
import io
import json
import re
import subprocess
import sys
import tempfile
from pathlib import Path

import cairosvg
from PIL import Image

REPO_ROOT = Path(__file__).resolve().parents[2]
MANIFEST = Path(__file__).with_name("icons.json")
MATERIAL_REPO = "https://github.com/google/material-design-icons.git"
GAME_ICONS_REPO = "https://github.com/game-icons/icons.git"


def git(*args, cwd=None):
    subprocess.run(["git", *args], cwd=cwd, check=True)


def clone(url: str, dest: Path, sparse: list[str] | None = None) -> Path:
    if not dest.exists():
        git("clone", "-q", "--depth", "1", "--filter=blob:none", "--no-checkout", url, str(dest))
    if sparse:
        git("sparse-checkout", "set", "--no-cone", *sparse, cwd=dest)
    else:
        git("sparse-checkout", "disable", cwd=dest)
    git("checkout", "-q", "HEAD", cwd=dest)
    return dest


def whiten(svg: str) -> str:
    """Forces every painted shape to white and drops game-icons' black square background."""
    svg = re.sub(r'<path d="M0 0h512v512H0z"\s*/>', "", svg)
    svg = re.sub(r'fill="#[0-9a-fA-F]{3,6}"', 'fill="#fff"', svg)
    # Material SVGs carry no fill: give the root one.
    return svg.replace("<svg ", '<svg fill="#fff" ', 1)


def render(svg_path: Path, out: Path, size: int, padding: float) -> None:
    inner = round(size * (1 - 2 * padding))
    png = cairosvg.svg2png(bytestring=whiten(svg_path.read_text()).encode(), output_width=inner, output_height=inner)
    glyph = Image.open(io.BytesIO(png)).convert("RGBA")
    # Keep only coverage: pure white with the rendered alpha, so tinting is exact.
    alpha = glyph.getchannel("A")
    canvas = Image.new("RGBA", (size, size), (255, 255, 255, 0))
    white = Image.new("RGBA", glyph.size, (255, 255, 255, 255))
    white.putalpha(alpha)
    offset = (size - inner) // 2
    canvas.paste(white, (offset, offset), white)
    out.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(out, optimize=True)


def author_names(license_txt: str) -> dict[str, tuple[str, bool]]:
    """Folder name -> (display name, is CC0) from game-icons' license.txt."""
    names = {}
    for line in license_txt.splitlines():
        m = re.match(r"- ([^,]+?)(?:,\s*(\S+))?(\s*-\s*CC0)?\s*$", line)
        if m:
            display = m.group(1).strip()
            names[re.sub(r"[^a-z0-9]", "", display.lower())] = (display, bool(m.group(3)))
    return names


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--cache", type=Path, help="directory to keep the source clones in")
    args = parser.parse_args()
    manifest = json.loads(MANIFEST.read_text())
    work = args.cache or Path(tempfile.mkdtemp(prefix="ui-icons-"))
    work.mkdir(parents=True, exist_ok=True)

    material = manifest["material"]
    patterns = [f"src/*/{name}/materialiconsround/24px.svg" for name in material["icons"]]
    mdi = clone(MATERIAL_REPO, work / "material-design-icons", patterns)
    for name in material["icons"]:
        matches = list(mdi.glob(f"src/*/{name}/materialiconsround/24px.svg"))
        if not matches:
            print(f"material icon '{name}' not found", file=sys.stderr)
            return 1
        render(matches[0], REPO_ROOT / material["out"] / f"icon_{name}.png", 96, 2 / 24)

    game = manifest["game-icons"]
    gi = clone(GAME_ICONS_REPO, work / "game-icons")
    authors = author_names((gi / "license.txt").read_text())
    out_dir = REPO_ROOT / game["out"]
    rows = []
    for name, source in sorted(game["icons"].items()):
        render(gi / source, out_dir / f"icon_{name}.png", 128, 0.04)
        folder = source.split("/")[0]
        display, cc0 = authors.get(re.sub(r"[^a-z0-9]", "", folder.lower()), (folder, False))
        slug = Path(source).stem
        rows.append(f"| `icon_{name}.png` | [{slug}](https://game-icons.net/1x1/{folder}/{slug}.html) | {display} | {'CC0' if cc0 else 'CC BY 3.0'} |")

    (out_dir / "CREDITS.md").write_text(
        "# Game icons\n\n"
        "Icons from [game-icons.net](https://game-icons.net) (source: https://github.com/game-icons/icons),\n"
        "licensed under [Creative Commons BY 3.0](https://creativecommons.org/licenses/by/3.0/) unless marked CC0.\n"
        "Recoloured to white on a transparent background so UI Images can tint them.\n"
        "Generated by `Tools/UIIcons/fetch_icons.py` from `Tools/UIIcons/icons.json`.\n\n"
        "| File | Icon | Author | License |\n|---|---|---|---|\n" + "\n".join(rows) + "\n")
    print(f"{len(material['icons'])} material icons, {len(rows)} game icons written")
    return 0


if __name__ == "__main__":
    sys.exit(main())
