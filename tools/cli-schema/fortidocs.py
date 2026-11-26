"""Small helpers shared by the schema builder: a polite fetcher restricted to official Fortinet hosts and a tiny DOM."""
import hashlib
import os
import time
import urllib.parse
import urllib.request
from html.parser import HTMLParser

ALLOWED_HOSTS = {"docs.fortinet.com", "fortinetweb.s3.amazonaws.com"}
USER_AGENT = "Mozilla/5.0 (compatible; Shiftwall-cli-schema)"
_last = [0.0]


def fetch(url, cache_dir, delay=0.7):
    """Returns the body of an official Fortinet page, from the cache when present."""
    host = urllib.parse.urlparse(url).hostname
    if not url.startswith("https://") or host not in ALLOWED_HOSTS:
        raise ValueError("Refusing to fetch a non-official URL: " + url)
    os.makedirs(cache_dir, exist_ok=True)
    path = os.path.join(cache_dir, hashlib.sha256(url.encode()).hexdigest()[:32])
    if os.path.exists(path):
        with open(path, "rb") as f:
            return f.read()
    wait = _last[0] + delay - time.time()
    if wait > 0:
        time.sleep(wait)
    for attempt in range(3):
        try:
            _last[0] = time.time()
            req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
            with urllib.request.urlopen(req, timeout=60) as r:
                body = r.read()
            break
        except OSError:
            if attempt == 2:
                raise
            time.sleep(3 * (attempt + 1))
    with open(path, "wb") as f:
        f.write(body)
    return body


class Node:
    __slots__ = ("tag", "attrs", "children", "parent")

    def __init__(self, tag, attrs, parent):
        self.tag, self.attrs, self.children, self.parent = tag, dict(attrs), [], parent

    def text(self):
        out = []
        for c in self.children:
            out.append(c if isinstance(c, str) else (" " + c.text() + " " if c.tag in ("p", "br", "li", "div") else c.text()))
        return "".join(out)

    def find_all(self, tag, recursive=True):
        for c in self.children:
            if isinstance(c, Node):
                if c.tag == tag:
                    yield c
                    if not recursive:
                        continue
                if recursive:
                    yield from c.find_all(tag)

    def kids(self, tag):
        return [c for c in self.children if isinstance(c, Node) and c.tag == tag]


VOID = {"br", "img", "hr", "meta", "link", "input", "col", "area", "base", "wbr", "source"}


class _Builder(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.root = Node("root", {}, None)
        self.cur = self.root

    def handle_starttag(self, tag, attrs):
        n = Node(tag, attrs, self.cur)
        self.cur.children.append(n)
        if tag not in VOID:
            self.cur = n

    def handle_endtag(self, tag):
        n = self.cur
        while n is not None and n.tag != tag:
            n = n.parent
        if n is not None and n.parent is not None:
            self.cur = n.parent

    def handle_data(self, data):
        self.cur.children.append(data)


def parse_html(text):
    b = _Builder()
    b.feed(text)
    return b.root


def squash(s):
    return " ".join(s.split())
