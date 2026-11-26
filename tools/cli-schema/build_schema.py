#!/usr/bin/env python3
"""
Builds data/schemas/<version>.json from the official FortiOS CLI Reference.

Only structured data is kept: configuration paths, sub-tables, parameter names, types, sizes, defaults and the
names of the allowed options. No description text is copied. Every path records the URL it was read from.

  build_schema.py html  --version 8.0 --doc 8.0.0 --out data/schemas/8.0.json --cache <dir>
  build_schema.py pdf   --version 5.2 --pdf <CLI reference PDF> --url <its URL> --out data/schemas/5.2.json

The tool runs on a developer machine; it is not part of the application.
"""
import argparse
import datetime
import html
import json
import re
import subprocess
import sys

from fortidocs import fetch, parse_html, squash

TODAY = datetime.date.today().isoformat()


# ---------------------------------------------------------------- HTML CLI reference (6.2 and later)

def parse_pre(pre_text):
    """Parses the syntax block of a page into a nested node tree (structure and parameter names)."""
    lines = [l.rstrip() for l in pre_text.split("\n") if l.strip()]
    root = None
    stack = []  # (indent, node)
    for line in lines:
        indent = len(line) - len(line.lstrip())
        s = line.strip()
        if s.startswith("Description:"):
            continue
        while stack and indent <= stack[-1][0] and not s.startswith(("edit", "next", "end")):
            stack.pop()
        if s.startswith("config "):
            node = {"edit": False, "params": {}, "sub": {}}
            name = s[len("config "):].strip()
            if root is None:
                root, node["name"] = node, name
            else:
                stack[-1][1]["sub"][name] = node
            stack.append((indent, node))
        elif s.startswith("edit "):
            if stack:
                stack[-1][1]["edit"] = True
                stack[-1][1]["key"] = s[5:].strip()
        elif s.startswith("set "):
            m = re.match(r"set (\S+)\s*(.*)", s)
            if m and stack:
                stack[-1][1]["params"][m.group(1)] = pre_param(m.group(2))
        elif s in ("next", "end"):
            if s == "end" and stack:
                stack.pop()
    return root


def pre_param(spec):
    p = {}
    spec = spec.strip()
    if spec.startswith("["):
        p["t"] = "option"
        p["o"] = [o for o in spec.strip("[]").split("|") if o != "..."]
        if "..." in spec:
            p["partial"] = True
    elif spec.startswith("{option1}"):
        p["t"] = "option"
        p["multi"] = True
    elif spec.startswith("<"):
        p["t"] = "string"
        p["multi"] = True
    else:
        m = re.match(r"\{([^}]+)\}", spec)
        p["t"] = m.group(1) if m else "string"
    return p


def size_of(text):
    out = {}
    m = re.search(r"Maximum length:\s*(\d+)", text)
    if m:
        out["len"] = int(m.group(1))
    m = re.search(r"Minimum value:\s*(-?\d+)", text)
    if m:
        out["min"] = int(m.group(1))
    m = re.search(r"Maximum value:\s*(-?\d+)", text)
    if m:
        out["max"] = int(m.group(1))
    return out


def table_rows(table):
    body = table.kids("tbody") or [table]
    rows = []
    for b in body:
        rows.extend(b.kids("tr"))
    return rows


def option_names(cell):
    opts = []
    for inner in cell.find_all("table"):
        for itr in table_rows(inner):
            ic = [c for c in itr.children if hasattr(c, "tag") and c.tag in ("td", "th")]
            if ic:
                o = squash(ic[0].text())
                if o and o != "Option":
                    opts.append(o)
    return opts


def parse_param_table(table):
    """Returns {param: details} from a Parameter | Description | Type | Size | Default table."""
    params = {}
    last = None
    for tr in table_rows(table):
        cells = [c for c in tr.children if hasattr(c, "tag") and c.tag in ("td", "th")]
        if len(cells) < 5:
            # the allowed options sit in a nested table, in a row of their own after the parameter
            if last is not None and last["t"] == "option" and "o" not in last:
                opts = [o for c in cells for o in option_names(c)]
                if opts:
                    last["o"] = opts
            continue
        name = squash(cells[0].text())
        if not name or name == "Parameter":
            continue
        some_models = name.endswith("*")
        name = name.rstrip("* ").strip()
        typ = squash(cells[2].text())
        d = {"t": typ}
        d.update(size_of(squash(cells[3].text())))
        default = squash(cells[4].text())
        if default and default != "-":
            d["d"] = default
        if typ == "option":
            opts = option_names(cells[1])
            if opts:
                d["o"] = opts
        if some_models:
            d["some_models"] = True  # "This parameter may not exist in some models."
        params[name] = d
        last = d
    return params


def walk(node):
    yield node
    for s in node["sub"].values():
        yield from walk(s)


def parse_page(body, url):
    text = body.decode("utf-8", "replace")
    m = re.search(r'<div class="prose[^"]*">(.*?)<div class="reader__footer', text, re.S)
    if not m:
        return None
    prose = m.group(1)
    pre = re.search(r"<pre>(.*?)</pre>", prose, re.S)
    if not pre:
        return None
    root = parse_pre(html.unescape(re.sub(r"<[^>]+>", "", pre.group(1))))
    if root is None:
        return None
    name_subnodes(root)
    # h2 sections in document order, each followed by its parameter table.
    dom = parse_html(prose[pre.end():])
    sections = []
    current = None
    for el in dom.children:
        if not hasattr(el, "tag"):
            continue
        if el.tag in ("h2", "h3", "h4", "h5"):
            current = squash(el.text())
            sections.append((current, []))
        elif el.tag == "table" and sections:
            sections[-1][1].append(el)
    order = list(walk(root))
    i = 0
    for title, tables in sections:
        name = title[len("config "):] if title.startswith("config ") else title
        # match the next node in pre-order with this name
        j = i
        while j < len(order) and (order[j].get("name") or order[j].get("_n")) != name:
            j += 1
        if j == len(order):
            continue
        node = order[j]
        i = j + 1
        for t in tables:
            for p, d in parse_param_table(t).items():
                base = node["params"].get(p, {})
                if base.get("multi"):
                    d["multi"] = True
                node["params"][p] = d
    for n in order:
        n.pop("_n", None)
        for p in n["params"].values():
            p.pop("partial", None)
    root["url"] = url
    return root


def name_subnodes(node):
    for k, s in node["sub"].items():
        s["_n"] = k
        name_subnodes(s)


def build_html(args):
    index_url = f"https://docs.fortinet.com/document/fortigate/{args.doc}/cli-reference"
    index = fetch(index_url, args.cache).decode("utf-8", "replace")
    links = sorted(set(re.findall(r'href="(/document/fortigate/' + re.escape(args.doc) + r'/cli-reference/\d+/config-[a-z0-9-]+)"', index)))
    paths = {}
    for n, link in enumerate(links, 1):
        url = "https://docs.fortinet.com" + link
        body = fetch(url, args.cache)
        node = parse_page(body, url)
        if node is None:
            print(f"skip {url}", file=sys.stderr)
            continue
        name = node.pop("name")
        if name in paths:
            continue
        paths[name] = node
        if n % 50 == 0:
            print(f"{n}/{len(links)}", file=sys.stderr)
    return {
        "version": args.version,
        "source": {"title": f"FortiOS {args.doc} CLI Reference", "url": index_url, "verified_on": TODAY},
        "paths": dict(sorted(paths.items())),
    }


# ---------------------------------------------------------------- PDF CLI reference (5.2)

def build_pdf(args):
    text = subprocess.run(["pdftotext", "-layout", args.pdf, "-"], check=True, capture_output=True, text=True).stdout
    lines = strip_page_breaks(text.split("\n"))
    paths = {}
    i = 0
    n = len(lines)
    while i < n:
        line = lines[i]
        s = line.strip()
        m = re.match(r"^config ([a-z0-9][a-z0-9_.+-]*(?: [a-z0-9][a-z0-9_.+-]*)*)((?:, [a-z0-9_.+-]+)*)$", s)
        if m and is_syntax_start(lines, i):
            node, i = parse_pdf_block(lines, i)
            if node is not None:
                name = node.pop("name").split(",")[0].strip()
                names = [name] + [name.rsplit(" ", 1)[0] + " " + x.strip() for x in m.group(2).split(",") if x.strip()]
                for nm in names:
                    old = paths.get(nm)
                    if old is None or count(node) > count(old):
                        paths[nm] = json.loads(json.dumps(node))
            continue
        i += 1
    return {
        "version": args.version,
        "source": {"title": args.title, "url": args.url, "verified_on": TODAY},
        "paths": dict(sorted(paths.items())),
    }


PAGE_FOOTER = re.compile(r"Fortinet Technologies Inc\.\s+Page \d+")


def strip_page_breaks(lines):
    """Removes the page footer and the running header that follows it, so that syntax blocks are contiguous."""
    out = []
    skip_header = False
    for line in lines:
        if PAGE_FOOTER.search(line):
            skip_header = True
            continue
        if skip_header:
            if line.strip():
                skip_header = False
                if "\f" in line or re.match(r"^\f?\S.*\s{10,}\S+$", line):
                    continue
            else:
                continue
        out.append(line.replace("\f", ""))
    return out


def count(node):
    return len(node["params"]) + sum(count(s) for s in node["sub"].values())


def is_syntax_start(lines, i):
    # A syntax block starts with "config x y" and, within a few lines, has "edit", "set" or "config".
    for k in range(i + 1, min(i + 4, len(lines))):
        s = lines[k].strip()
        if s.startswith(("edit ", "set ", "config ", "unset ")) or s == "end":
            return True
        if s:
            return False
    return False


OPT_RE = re.compile(r"\{([^{}]*)\}")


def pdf_param(spec):
    spec = spec.strip()
    p = {}
    m = OPT_RE.match(spec)
    if m and "|" in m.group(1):
        p["t"] = "option"
        p["o"] = [o.strip() for o in m.group(1).split("|") if o.strip() and not o.strip().startswith("<")]
    elif m:
        p["t"] = "option"
        p["o"] = [m.group(1).strip()]
    elif spec.startswith("<"):
        t = re.match(r"<([^>]*)>", spec)
        tag = t.group(1) if t else "string"
        p["t"] = ("integer" if tag.endswith("_int") else "ipv4" if "ipv4" in tag else "string")
        if re.search(r"_str>\s*\[|\.\.\.|\[<", spec) or tag.endswith("s_str") or "_list" in tag:
            p["multi"] = True
    else:
        p["t"] = "string"
    return p


def spec_open(spec):
    return spec.count("{") > spec.count("}") or spec.count("[") > spec.count("]") or spec.rstrip().endswith(("|", "-"))


def parse_pdf_block(lines, i):
    first = lines[i]
    base = len(first) - len(first.lstrip())
    root = {"edit": False, "params": {}, "sub": {}, "name": first.strip()[len("config "):]}
    stack = [root]
    indents = [base]
    last = None  # (node, name, spec) of the last "set" line, for wrapped lines
    i += 1
    blank = 0
    while i < len(lines):
        line = lines[i]
        s = line.strip()
        if not s:
            blank += 1
            if blank > 6:
                break
            i += 1
            continue
        blank = 0
        indent = len(line) - len(line.lstrip())
        if indent < base - 8:
            break
        s = re.sub(r"\s+\*$", "", s)  # "*": IPv4 policies only
        word = s.split(" ", 1)[0]
        if word == "set":
            m = re.match(r"set (\{[^}]*\}|\S+)\s*(.*)", s)
            if m:
                for pname in [x.strip() for x in m.group(1).strip("{}").split("|") if x.strip()]:
                    last = (stack[-1], pname, m.group(2))
                    stack[-1]["params"].setdefault(pname, pdf_param(m.group(2)))
        elif word == "edit":
            stack[-1]["edit"] = True
            stack[-1]["key"] = s[5:].strip()
            last = None
        elif word == "config":
            # the 5.2 reference sometimes omits the "end" of a sub-table: a sibling starts at the same indentation
            while len(stack) > 1 and indent <= indents[-1]:
                stack.pop()
                indents.pop()
            sub = {"edit": False, "params": {}, "sub": {}}
            stack[-1]["sub"][s[7:].strip()] = sub
            stack.append(sub)
            indents.append(indent)
            last = None
        elif s == "end":
            stack.pop()
            if len(indents) > 1:
                indents.pop()
            last = None
            if not stack:
                return root, i + 1
        elif word in ("next", "unset", "get", "show"):
            last = None
        elif last is not None and (spec_open(last[2]) or s[0] in "|[<"):
            node, name, spec = last
            spec = spec + ("" if spec.endswith("-") else " ") + s
            last = (node, name, spec)
            node["params"][name] = pdf_param(spec)
        else:
            break
        i += 1
    return (root if root["params"] or root["sub"] else None), i


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("mode", choices=["html", "pdf"])
    ap.add_argument("--version", required=True)
    ap.add_argument("--doc")
    ap.add_argument("--pdf")
    ap.add_argument("--url")
    ap.add_argument("--title")
    ap.add_argument("--cache", default=".cache/cli-schema")
    ap.add_argument("--out", required=True)
    args = ap.parse_args()
    schema = build_html(args) if args.mode == "html" else build_pdf(args)
    with open(args.out, "w", encoding="utf-8", newline="\n") as f:
        json.dump(schema, f, indent=1, sort_keys=False, ensure_ascii=False)
        f.write("\n")
    print(f"{len(schema['paths'])} paths -> {args.out}", file=sys.stderr)


if __name__ == "__main__":
    main()
