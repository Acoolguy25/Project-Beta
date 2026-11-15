#!/usr/bin/env python3
"""Move Assets/RyanAssets/<path> into Packages/<package>/Runtime/<path> with `git mv`.

Moves each file together with its .meta (so GUIDs, and every prefab/scene reference, survive),
writes package.json/README/CHANGELOG for each package, adds the packages to manifest.json, and
rewrites hard-coded "Assets/RyanAssets/..." strings in scripts and docs. Folders created along the
way get a .meta with a fresh GUID. Run from the repo root:  python3 Tools/Packaging/move_to_packages.py
"""
import json, re, subprocess, sys, uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / "Assets" / "RyanAssets"
CFG = json.loads((Path(__file__).parent / "packages.json").read_text())["packages"]
VERSION = "0.1.0"


def git(*args):
    subprocess.run(["git", *args], cwd=ROOT, check=True)


def folder_meta(path: Path):
    meta = path.with_name(path.name + ".meta")
    if not meta.exists():
        meta.write_text(f"fileFormatVersion: 2\nguid: {uuid.uuid4().hex}\nfolderAsset: yes\nDefaultImporter:\n"
                        "  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n")


def ensure_dir(path: Path, stop: Path):
    """mkdir -p with a .meta for every new folder below `stop` (the package root)."""
    missing = []
    p = path
    while p != stop and not p.exists():
        missing.append(p)
        p = p.parent
    for d in reversed(missing):
        d.mkdir()
        folder_meta(d)


def mapping():
    out = []
    for pkg, spec in CFG.items():
        for rel in spec["paths"]:
            out.append((rel, pkg))
    out.sort(key=lambda x: -len(x[0]))  # longest prefix first
    return out


def new_path(rel: str) -> str | None:
    for prefix, pkg in mapping():
        if rel == prefix or rel.startswith(prefix + "/"):
            return f"Packages/{pkg}/Runtime/{rel}"
    return None


def move():
    moved = 0
    for prefix, pkg in sorted(mapping(), key=lambda x: len(x[0])):
        src = SRC / prefix
        if not src.exists():
            print(f"skip (absent): {prefix}")
            continue
        pkg_root = ROOT / "Packages" / pkg
        dst = pkg_root / "Runtime" / prefix
        pkg_root.mkdir(exist_ok=True)
        ensure_dir(dst.parent, pkg_root)
        git("mv", str(src), str(dst))
        meta = src.with_name(src.name + ".meta")
        if meta.exists():
            git("mv", str(meta), str(dst.with_name(dst.name + ".meta")))
        moved += 1
    return moved


def leftovers():
    rest = [p for p in SRC.rglob("*") if p.is_file() and not p.name.endswith(".meta")]
    return rest


def cleanup_empty():
    if not SRC.exists():
        SRC.mkdir()  # git mv removed it; recreate so leftover .meta files can be handled below
    for d in sorted((p for p in SRC.rglob("*") if p.is_dir()), key=lambda p: -len(p.parts)):
        if d.exists() and not any(d.iterdir()):
            d.rmdir()
    # metas of folders that git mv emptied and removed
    for m in list(SRC.rglob("*.meta")):
        if not m.with_suffix("").exists():
            git("rm", "-q", str(m))
    if SRC.exists() and not any(p for p in SRC.iterdir() if not p.name.endswith(".meta")):
        for m in SRC.glob("*.meta"):
            git("rm", "-q", str(m))
        if not any(SRC.iterdir()):
            SRC.rmdir()
    if not SRC.exists() and (SRC.parent / "RyanAssets.meta").exists():
        git("rm", "-q", str(SRC.parent / "RyanAssets.meta"))


def write_package_files():
    for pkg, spec in CFG.items():
        root = ROOT / "Packages" / pkg
        if not root.exists():
            continue
        manifest = {
            "name": pkg,
            "version": VERSION,
            "displayName": spec["displayName"],
            "description": spec["description"],
            "unity": "6000.0",
            "author": {"name": "Ryan"},
            "dependencies": {d: VERSION for d in spec.get("dependencies", [])},
        }
        (root / "package.json").write_text(json.dumps(manifest, indent=2) + "\n")
        layer_deps = ", ".join(spec.get("dependencies", [])) or "nothing else in this repo"
        (root / "README.md").write_text(
            f"# {spec['displayName']} (`{pkg}`)\n\n{spec['description']}\n\n"
            f"Layer {spec['layer']}: may reference {layer_deps}. Never a universe, and never a package in a "
            "higher layer (checked by `Tools/Packaging/asmdef_graph.py --check`).\n\n"
            "Files under `Runtime/` keep the path they had under `Assets/RyanAssets/`. Embedded in "
            "Project-Beta; another Unity project can use it through a git URL:\n\n"
            f"```json\n\"{pkg}\": \"https://github.com/Acoolguy25/Project-Beta.git?path=/Packages/{pkg}#<tag>\"\n```\n")
        (root / "CHANGELOG.md").write_text(
            f"# Changelog\n\n## [{VERSION}] - 2026-11-15\n\n- Extracted from `Assets/RyanAssets` into an embedded package. No code changes.\n")
        for f in ("package.json", "README.md", "CHANGELOG.md"):
            p = root / f
            meta = p.with_name(p.name + ".meta")
            if not meta.exists():
                meta.write_text(f"fileFormatVersion: 2\nguid: {uuid.uuid4().hex}\n"
                                + ("PackageManifestImporter:\n" if f == "package.json" else "TextScriptImporter:\n")
                                + "  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n")


def add_to_manifest():
    """Add file: entries right after com.ryan.codegenerator, keeping the rest of the order."""
    path = ROOT / "Packages" / "manifest.json"
    data = json.loads(path.read_text())
    deps = data["dependencies"]
    new = {pkg: f"file:{pkg}" for pkg in sorted(CFG) if (ROOT / "Packages" / pkg).exists() and pkg not in deps}
    if not new:
        return
    out = {}
    for k, v in deps.items():
        out[k] = v
        if k == "com.ryan.codegenerator":
            out.update(new)
    for k, v in new.items():
        out.setdefault(k, v)
    data["dependencies"] = out
    path.write_text(json.dumps(data, indent=2) + "\n")


KEEP_OLD_PATHS = {"AGENTS.md"}

PATH_RE = re.compile(r"Assets/RyanAssets/([A-Za-z0-9_\-./ ]*?)(?=[\"'`\s)]|$)")


def rewrite_strings():
    files = subprocess.run(["git", "grep", "-l", "Assets/RyanAssets", "--", "*.cs", "*.md", "*.py"],
                           cwd=ROOT, capture_output=True, text=True).stdout.split()
    changed = []
    for f in files:
        # The packaging tools and AGENTS.md quote old paths on purpose (AGENTS.md has an
        # "old path is now new path" example), so a re-run must not rewrite them.
        if f.startswith("Tools/Packaging/") or f in KEEP_OLD_PATHS:
            continue
        p = ROOT / f
        text = p.read_text(encoding="utf-8")

        def repl(m):
            rel = m.group(1).rstrip("/")
            target = new_path(rel) if rel else None
            if target is None:
                # a parent folder of several packages (e.g. "Assets/RyanAssets/Tools" stays valid as
                # the gameframework Tools folder only if it maps); leave unknowns untouched
                return m.group(0)
            return target + (m.group(1)[len(rel):])

        new = PATH_RE.sub(repl, text)
        if new != text:
            p.write_text(new, encoding="utf-8")
            changed.append(f)
    return changed


def main():
    n = move()
    rest = leftovers()
    if rest:
        print("Files not covered by packages.json, left in Assets/RyanAssets:")
        for r in rest:
            print("  ", r.relative_to(ROOT))
    cleanup_empty()
    write_package_files()
    add_to_manifest()
    changed = rewrite_strings()
    print(f"moved {n} paths; rewrote paths in: {', '.join(changed) or 'nothing'}")
    return 1 if rest else 0


if __name__ == "__main__":
    sys.exit(main())
