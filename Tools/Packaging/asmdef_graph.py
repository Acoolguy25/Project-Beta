#!/usr/bin/env python3
"""Dump the asmdef dependency graph of Project-Beta and check package layering rules.

Usage:
  python3 Tools/Packaging/asmdef_graph.py            # print graph (project asmdefs only)
  python3 Tools/Packaging/asmdef_graph.py --check    # exit 1 on a layering violation or a cycle
  python3 Tools/Packaging/asmdef_graph.py --dot      # Graphviz output

Layering comes from Tools/Packaging/packages.json (dependencies may only point to the same or a
lower layer):
  universes  ->  com.ryan.commands (5)  ->  com.ryan.hud (4)  ->  com.ryan.gameframework (3)
  ->  com.ryan.networking (2)  ->  com.ryan.ui (1)  ->  com.ryan.core (0)
A universe may never reference another universe, no package may reference a universe, and
nothing may live in Assets/RyanAssets any more.
"""
import json, re, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCAN = [ROOT / "Assets" / "RyanAssets", ROOT / "Assets" / "Universes", ROOT / "Packages"]

# Higher number = higher layer. Same-layer references are allowed.
_PACKAGES = json.loads((Path(__file__).parent / "packages.json").read_text())["packages"]
LAYERS = {name: spec["layer"] for name, spec in _PACKAGES.items()}
LAYERS["com.ryan.codegenerator"] = 0
LAYERS["universe"] = max(LAYERS.values()) + 1


def strip_json_comments(text: str) -> str:
    return re.sub(r"^\s*//.*$", "", text, flags=re.M)


def owner(path: Path) -> str:
    rel = path.relative_to(ROOT).parts
    if rel[0] == "Packages" and len(rel) > 1 and rel[1].startswith("com.ryan."):
        return rel[1]
    if rel[0] == "Assets" and rel[1] == "Universes":
        # Assets/Universes/UniverseData/<name>/... is a universe; the rest (the universe catalog
        # and the game browser) is game-level code that universes may use.
        if len(rel) > 3 and rel[2] == "UniverseData":
            return "universe:" + rel[3]
        return "game"
    if rel[0] == "Assets" and rel[1] == "RyanAssets":
        return "Assets/RyanAssets"
    return "/".join(rel[:2])


def load():
    nodes = {}
    guid_to_name = {}
    for base in SCAN:
        if not base.exists():
            continue
        for p in base.rglob("*.asmdef"):
            data = json.loads(strip_json_comments(p.read_text(encoding="utf-8-sig")))
            meta = p.with_suffix(".asmdef.meta")
            if meta.exists():
                m = re.search(r"guid:\s*([0-9a-f]{32})", meta.read_text())
                if m:
                    guid_to_name[m.group(1)] = data["name"]
            nodes[data["name"]] = {"path": p, "owner": owner(p), "refs": data.get("references", [])}
    for n in nodes.values():
        n["refs"] = [guid_to_name.get(r[5:], r) if r.startswith("GUID:") else r for r in n["refs"]]
    return nodes


def layer_of(o: str):
    if o.startswith("universe:") or o == "game" or o.startswith("Assets/"):
        return LAYERS["universe"]
    return LAYERS.get(o)


def check(nodes):
    errors = []
    for name, n in nodes.items():
        if n["owner"] == "Assets/RyanAssets":
            errors.append(f"not packaged: {name} is still under Assets/RyanAssets")
    for name, n in nodes.items():
        for r in n["refs"]:
            if r not in nodes:
                continue  # third-party / Unity assembly
            src, dst = n["owner"], nodes[r]["owner"]
            if src.startswith("universe:") and dst.startswith("universe:") and src != dst:
                errors.append(f"universe-to-universe: {name} ({src}) -> {r} ({dst})")
            ls, ld = layer_of(src), layer_of(dst)
            if ls is not None and ld is not None and ld > ls:
                errors.append(f"upward reference: {name} ({src}) -> {r} ({dst})")
    # cycles
    state = {}
    def visit(v, stack):
        state[v] = 1
        for r in nodes[v]["refs"]:
            if r in nodes:
                if state.get(r) == 1:
                    errors.append("cycle: " + " -> ".join(stack[stack.index(r):] + [r]))
                elif not state.get(r):
                    visit(r, stack + [r])
        state[v] = 2
    for v in nodes:
        if not state.get(v):
            visit(v, [v])
    return errors


def main():
    nodes = load()
    if "--dot" in sys.argv:
        print("digraph asmdefs {")
        for name, n in sorted(nodes.items()):
            for r in n["refs"]:
                if r in nodes:
                    print(f'  "{name}" -> "{r}";')
        print("}")
        return 0
    if "--check" in sys.argv:
        errs = check(nodes)
        for e in errs:
            print("ERROR", e)
        print(f"{len(nodes)} project asmdefs, {len(errs)} violation(s)")
        return 1 if errs else 0
    for name, n in sorted(nodes.items(), key=lambda kv: (kv[1]["owner"], kv[0])):
        internal = [r for r in n["refs"] if r in nodes]
        print(f"[{n['owner']}] {name}  ({n['path'].relative_to(ROOT)})")
        for r in internal:
            print(f"    -> {r} [{nodes[r]['owner']}]")
    return 0


if __name__ == "__main__":
    sys.exit(main())
