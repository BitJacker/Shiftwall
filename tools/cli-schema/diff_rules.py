#!/usr/bin/env python3
"""
Compares two CLI schemas on the paths Shiftwall rebuilds and lists every difference that has no rule yet in
data/rules/<from>-<to>.json. Run it after rebuilding a schema; the test suite fails while differences are uncovered.

  diff_rules.py --from 5.2 --to 8.0 [--add-removals]

--add-removals appends a "remove-no-equivalent" rule (sources: both CLI references) for each parameter that is
documented in the old reference and absent from the new one. Renames and value changes must be written by hand
with their own sources.
"""
import argparse
import json
import os

ROOT = os.path.join(os.path.dirname(__file__), "..", "..")


def walk(a, b, path, sub=()):
    """Yields (path, sub, param, kind, detail) for differences between node a (old) and b (new)."""
    for p, d in a["params"].items():
        if p not in b["params"]:
            yield (path, list(sub), p, "param", None)
            continue
        n = b["params"][p]
        if d.get("t") == "option" and n.get("t") == "option" and d.get("o") and n.get("o"):
            for v in d["o"]:
                if v not in n["o"] and " " not in v:
                    yield (path, list(sub), p, "value", v)
    for s, node in a["sub"].items():
        if s not in b["sub"]:
            yield (path, list(sub), s, "table", None)
        else:
            yield from walk(node, b["sub"][s], path, sub + (s,))


def covered(rules, path, sub, param, kind, value):
    for r in rules:
        if r["path"] != path or r.get("sub", []) != sub:
            continue
        if kind == "table" and r.get("table") == param:
            return True
        if kind == "param" and r.get("param") == param and r["kind"] != "value-map":
            return True
        if kind == "value" and r.get("param") == param and r["kind"] in ("value-map", "remove-no-equivalent", "remove-with-equivalent", "not-a-difference") and \
                (r["kind"] != "value-map" or value in r.get("map", {})) and (r["kind"] == "value-map" or not r.get("values") or value in r["values"]):
            return True
    return False


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--from", dest="src", required=True)
    ap.add_argument("--to", dest="dst", required=True)
    ap.add_argument("--add-removals", action="store_true")
    a = ap.parse_args()
    old = json.load(open(os.path.join(ROOT, "data", "schemas", a.src + ".json")))
    new = json.load(open(os.path.join(ROOT, "data", "schemas", a.dst + ".json")))
    rules_path = os.path.join(ROOT, "data", "rules", f"{a.src}-{a.dst}.json")
    data = json.load(open(rules_path))
    rules = data["rules"]
    missing = []
    for path in data["paths"]:
        if path not in old["paths"] or path not in new["paths"]:
            missing.append((path, [], None, "path", None))
            continue
        for d in walk(old["paths"][path], new["paths"][path], path):
            if not covered(rules, *d):
                missing.append(d)
    for m in missing:
        print("uncovered:", m)
    if a.add_removals:
        n = sum(1 for r in rules if r["id"].startswith("R"))
        for path, sub, param, kind, value in missing:
            if kind not in ("param", "table"):
                continue
            n += 1
            r = {"id": f"R{n:03d}", "kind": "remove-no-equivalent", "path": path}
            if sub:
                r["sub"] = sub
            r["table" if kind == "table" else "param"] = param
            r["sources"] = ["cli-" + a.src, "cli-" + a.dst]
            r["note"] = "Documented in the old CLI Reference, absent from the new one."
            rules.append(r)
        with open(rules_path, "w", encoding="utf-8", newline="\n") as f:
            json.dump(data, f, indent=1, ensure_ascii=False)
            f.write("\n")
    print(f"{len(missing)} uncovered")


if __name__ == "__main__":
    main()
