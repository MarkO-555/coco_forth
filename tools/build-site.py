#!/usr/bin/env python3
"""build-site.py -- CoCo Renovation static-site builder.

Bespoke documentation publishing pipeline (epic #539). Renders markdown
source into the committed ``site/`` directory at the repo root. Every layer
-- template, navigation, styling, link-rewriting -- is our own code; there
is no third-party static-site framework, only a markdown parser (mistune).

The build is driven by a declarative manifest
(``tools/site-template/manifest.json``): each entry is one page under a
section, and the section titles become the sidebar-nav groups. Adding a doc
is a one-line manifest edit -- never hand-edit generated HTML.

Issue map:
  #541  pipeline skeleton + make site + pin mistune          (done)
  #542  shared style tokens + docs.css                        (done)
  #543  manifest-driven render with template + sidebar nav    (this)
  #544  copy bespoke HTML (tutorials, reference) into site/
  #545  manifest formalisation + internal-link validation
  #546  source-code link strategy (link out to GitHub)
  #547  landing page + PUBLISHING.md

Usage:
    python3 tools/build-site.py [--manifest PATH] [--output-dir site]
"""

from __future__ import annotations

import argparse
import json
import re
import shutil
import subprocess
import sys
from pathlib import Path

try:
    import mistune
except ImportError:  # pragma: no cover - guidance only
    sys.exit(
        "error: mistune is not installed.\n"
        "       python3 -m pip install -r tools/requirements.txt"
    )

# --------------------------------------------------------------------------
# Paths
# --------------------------------------------------------------------------

REPO_ROOT = Path(__file__).resolve().parent.parent
SITE_DIR = REPO_ROOT / "site"

# Hand-authored site source (template, manifest, assets). Kept OUT of site/ so
# a clean rebuild of the generated output can never delete it; the build copies
# what it needs into site/.
TEMPLATE_DIR = REPO_ROOT / "tools" / "site-template"
ASSETS_SRC = TEMPLATE_DIR / "assets"
MANIFEST = TEMPLATE_DIR / "manifest.json"

SITE_NAME = "CoCo Renovation"
SITE_TAGLINE = "Bare Naked Forth"

# Fallback GitHub base if the origin remote can't be read (see github_base()).
DEFAULT_GITHUB = "https://github.com/ugufru/coco"
# Ref the source links point at. main is the published branch; a moving ref is
# the convention for "view source" doc links.
GITHUB_BRANCH = "main"

# --------------------------------------------------------------------------
# Template
# --------------------------------------------------------------------------
#
# Placeholders:
#   {asset_prefix} relative path from the page back to site/ ("" for a root
#                  page, "../" for a nested one) so links resolve at any depth
#   {title}        page title (from the source H1)
#   {nav}          sidebar navigation HTML
#   {body}         rendered article HTML

PAGE_TEMPLATE = """<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <link rel="stylesheet" href="{asset_prefix}assets/tokens.css">
  <link rel="stylesheet" href="{asset_prefix}assets/docs.css">
  <title>{title} — CoCo Renovation</title>
</head>
<body>
  <header class="doc-header">
    <span class="doc-brand">CoCo Renovation</span>
    <span class="doc-brand-sub">Bare Naked Forth</span>
  </header>
  <div class="doc-layout">
    <nav class="doc-sidebar">
{nav}
    </nav>
    <article class="doc-article">
{body}
    </article>
  </div>
  <footer class="doc-footer">
    Generated from source by <code>build-site.py</code>. Do not hand-edit.
  </footer>
</body>
</html>
"""


# --------------------------------------------------------------------------
# Rendering
# --------------------------------------------------------------------------

def make_renderer() -> "mistune.Markdown":
    """Build the markdown->HTML converter used site-wide.

    Plugins cover what the in-tree markdown actually uses: GitHub tables,
    strikethrough, task lists, footnotes, and bare-URL autolinking. Keep this
    the single place plugins are configured so every page renders identically.
    """
    return mistune.create_markdown(
        escape=False,
        plugins=["table", "strikethrough", "task_lists", "footnotes", "url"],
    )


def derive_title(md_text: str, fallback: str) -> str:
    """Title = the first ``# H1`` in the document, else the fallback."""
    for line in md_text.splitlines():
        stripped = line.strip()
        if stripped.startswith("# "):
            return stripped[2:].strip()
    return fallback


def output_name(source: Path) -> str:
    """Map a source path to its site-relative output filename."""
    return source.stem.lower().replace("_", "-") + ".html"


# --------------------------------------------------------------------------
# Manifest
# --------------------------------------------------------------------------

def load_manifest(manifest_path: Path) -> tuple[list[dict], list[str]]:
    """Load the manifest into (pages, exclude).

    Each page carries: source (Path), output (str), nav (str), section (str),
    kind (str). Missing ``output`` is derived from the source; missing ``kind``
    defaults to ``generate``.

    ``exclude`` is the manifest's top-level list of repo-relative source docs
    deliberately kept off the site (CLAUDE.md, speaking notes, ...). It lets
    the unlisted-source check (#545) tell "not on the site on purpose" from
    "silently forgotten".
    """
    if not manifest_path.is_file():
        sys.exit(f"error: manifest not found: {manifest_path}")
    try:
        data = json.loads(manifest_path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        sys.exit(f"error: manifest is not valid JSON: {exc}")

    pages: list[dict] = []
    seen_output: dict[str, Path] = {}
    for section in data.get("sections", []):
        section_title = section.get("title", "")
        for entry in section.get("pages", []):
            if "source" not in entry or "nav" not in entry:
                sys.exit(f"error: manifest page missing source/nav: {entry!r}")
            source = REPO_ROOT / entry["source"]
            kind = entry.get("kind", "generate")
            if kind not in ("generate", "copy"):
                sys.exit(f"error: unknown kind {kind!r} for {entry['source']}")
            output = entry.get("output") or default_output(source, kind)
            if output in seen_output:
                sys.exit(
                    f"error: two manifest pages target the same output "
                    f"{output!r}: {seen_output[output]} and {source}"
                )
            seen_output[output] = source
            pages.append({
                "source": source,
                "output": output,
                "nav": entry["nav"],
                "section": section_title,
                "kind": kind,
            })
    if not pages:
        sys.exit("error: manifest lists no pages")
    exclude = list(data.get("exclude", []))
    return pages, exclude


def default_output(source: Path, kind: str) -> str:
    """Derive the site-relative output/nav-target for a manifest entry.

    generate -> a flat ``basename.html`` at the site root.
    copy     -> the source's own repo-relative path, so the copied tree keeps
                the exact layout that makes its internal links resolve. A
                directory source has no single page, so its output (the landing
                page to link in nav) must be given explicitly.
    """
    if kind == "copy":
        if source.is_dir():
            sys.exit(
                f"error: copy entry for a directory needs an explicit "
                f"'output' (its landing page): {source}"
            )
        return str(source.relative_to(REPO_ROOT))
    return output_name(source)


def build_nav(pages: list[dict], current_output: str, asset_prefix: str) -> str:
    """Render the sidebar nav, grouping by section and marking the current page."""
    lines: list[str] = []
    current_section = None
    for page in pages:
        if page["section"] != current_section:
            current_section = page["section"]
            lines.append(f'      <h2>{current_section}</h2>')
        cls = ' class="current"' if page["output"] == current_output else ""
        href = asset_prefix + page["output"]
        lines.append(f'      <a href="{href}"{cls}>{page["nav"]}</a>')
    return "\n".join(lines)


# --------------------------------------------------------------------------
# Build
# --------------------------------------------------------------------------

def copy_assets(site_dir: Path) -> set[Path]:
    """Copy the hand-authored assets (CSS) into ``site_dir/assets``.

    Assets live under tools/site-template so a clean rebuild of site/ can
    never delete them. Copying (rather than symlinking) keeps the committed
    site/ self-contained and servable straight from git. Returns the set of
    files written (resolved), for orphan tracking.
    """
    if not ASSETS_SRC.is_dir():
        sys.exit(f"error: assets source not found: {ASSETS_SRC}")
    dest = site_dir / "assets"
    dest.mkdir(parents=True, exist_ok=True)
    produced: set[Path] = set()
    for asset in sorted(ASSETS_SRC.iterdir()):
        if asset.is_file():
            out = dest / asset.name
            shutil.copy2(asset, out)
            produced.add(out.resolve())
    return produced


def files_under(path: Path) -> set[Path]:
    """Resolved set of every file at/under ``path`` (single file or tree)."""
    if path.is_dir():
        return {p.resolve() for p in path.rglob("*") if p.is_file()}
    return {path.resolve()}


def copy_bespoke(page: dict, site_dir: Path) -> Path:
    """Copy a hand-authored HTML file or tree into ``site_dir`` verbatim.

    The copy preserves the source's repo-relative path (reference.html ->
    site/reference.html, tutorials/ -> site/tutorials/). Preserving that
    layout is what lets the book's own relative links (../reference.html,
    ../style.css, sibling chapters) resolve unchanged -- no rewriting needed.

    Links that point OUTSIDE the copied set (e.g. ../../src/... demo sources)
    are left intact here and rewritten to GitHub in #546.
    """
    source = page["source"]
    if not source.exists():
        sys.exit(f"error: copy source not found: {source}")

    rel = source.relative_to(REPO_ROOT)
    dest = site_dir / rel
    if source.is_dir():
        shutil.copytree(source, dest, dirs_exist_ok=True)
    else:
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, dest)
    return dest


def render_page(page: dict, pages: list[dict], site_dir: Path,
                renderer: "mistune.Markdown") -> Path:
    """Render one 'generate' page into ``site_dir``; return the output path."""
    source = page["source"]
    if not source.is_file():
        sys.exit(f"error: source not found: {source}")

    md_text = source.read_text(encoding="utf-8")
    body = renderer(md_text)
    title = derive_title(md_text, source.stem)

    # All generated pages are at site root today, so assets and nav links
    # resolve with an empty prefix. Nested outputs (#544) will compute depth.
    asset_prefix = ""
    nav = build_nav(pages, page["output"], asset_prefix)

    out_path = site_dir / page["output"]
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(
        PAGE_TEMPLATE.format(
            asset_prefix=asset_prefix, title=title, nav=nav, body=body,
        ),
        encoding="utf-8",
    )
    return out_path


# --------------------------------------------------------------------------
# Validation (#545)
# --------------------------------------------------------------------------

LINK_RE = re.compile(r'(?:href|src)\s*=\s*"([^"]*)"', re.IGNORECASE)

# Schemes / forms that point off-site; never validated as local files.
_EXTERNAL_PREFIXES = ("http://", "https://", "//", "mailto:", "tel:",
                      "data:", "javascript:")


def _is_external(link: str) -> bool:
    return link.startswith(_EXTERNAL_PREFIXES)


def github_base() -> str:
    """Best-effort ``https://github.com/<owner>/<repo>`` from the origin remote.

    Falls back to DEFAULT_GITHUB when git or the remote is unavailable (e.g. a
    detached CI checkout), so the build never fails just to link out to source.
    """
    try:
        url = subprocess.check_output(
            ["git", "-C", str(REPO_ROOT), "config", "--get", "remote.origin.url"],
            text=True, stderr=subprocess.DEVNULL,
        ).strip()
    except (subprocess.CalledProcessError, FileNotFoundError, OSError):
        url = ""
    if url.startswith("git@"):                       # git@github.com:owner/repo.git
        host_path = url.split("@", 1)[1]
        host, _, path = host_path.partition(":")
        return f"https://{host}/{path.removesuffix('.git')}"
    if url.startswith(("https://", "http://")):      # https://github.com/owner/repo.git
        return url.removesuffix(".git")
    return DEFAULT_GITHUB


def rewrite_repo_links(site_dir: Path, base: str, branch: str) -> int:
    """Point on-site links that target repo files (not on the site) at GitHub.

    Same classification as validate_links: a link that doesn't resolve on the
    site but does resolve in the repo is a live source reference. We rewrite it
    to a GitHub URL -- ``blob`` for files, ``tree`` for directories -- keeping
    any #fragment. Genuinely broken links (nowhere in repo) are left untouched
    for validation to report. Returns the number of links rewritten.
    """
    count = 0
    for html in sorted(site_dir.rglob("*.html")):
        page_dir = html.parent.relative_to(site_dir)
        text = html.read_text(encoding="utf-8", errors="replace")

        def repl(match: "re.Match") -> str:
            nonlocal count
            attr, link = match.group(0), match.group(1).strip()
            if not link or link.startswith("#") or _is_external(link):
                return attr
            target = link.split("#", 1)[0].split("?", 1)[0]
            frag = link[len(target):]                # keeps '#anchor' / '?query'
            if not target or _exists((html.parent / target).resolve()):
                return attr                          # empty or resolves on-site
            repo_path = (REPO_ROOT / page_dir / target).resolve()
            if not repo_path.exists() or not repo_path.is_relative_to(REPO_ROOT):
                return attr                          # dead or escapes repo -> leave
            rel = repo_path.relative_to(REPO_ROOT)
            kind = "tree" if repo_path.is_dir() else "blob"
            url = f"{base}/{kind}/{branch}/{rel}{frag}"
            count += 1
            return attr.replace(f'"{link}"', f'"{url}"')

        new_text = LINK_RE.sub(repl, text)
        if new_text != text:
            html.write_text(new_text, encoding="utf-8")
    return count


def _exists(target: Path) -> bool:
    """A link target resolves if it's a file, or a dir with an index.html."""
    if target.is_dir():
        return (target / "index.html").exists()
    return target.exists()


def validate_links(site_dir: Path) -> tuple[list, list]:
    """Classify every internal link into (broken, pending).

    Scans all HTML in ``site_dir`` -- generated and copied alike -- so the whole
    shipped doc set is checked. A link that doesn't resolve on the site is:

      * pending  -- the same path resolves to a real file in the *repo*, so it's
                    a live reference to a source that isn't (yet) on the site.
                    #546 rewrites these to point at GitHub.
      * broken   -- resolves nowhere, on the site or in the repo: a real defect.

    Because the site mirrors the repo layout (generated root pages come from
    root docs; copied trees keep their path), resolving the link against
    REPO_ROOT/<page-dir> is the correct repo-equivalent test.
    """
    broken: list[tuple[str, str]] = []
    pending: list[tuple[str, str]] = []
    for html in sorted(site_dir.rglob("*.html")):
        page_dir = html.parent.relative_to(site_dir)
        text = html.read_text(encoding="utf-8", errors="replace")
        for raw in LINK_RE.findall(text):
            link = raw.strip()
            if not link or link.startswith("#") or _is_external(link):
                continue
            target = link.split("#", 1)[0].split("?", 1)[0]
            if not target:
                continue
            if _exists((html.parent / target).resolve()):
                continue
            page_rel = str(html.relative_to(site_dir))
            # Repo-existence is plain: a live source reference may point at a
            # directory (src/hello/) that has no index.html. #546 rewrites the
            # whole reference to GitHub regardless of file-vs-dir.
            if (REPO_ROOT / page_dir / target).resolve().exists():
                pending.append((page_rel, link))
            else:
                broken.append((page_rel, link))
    return broken, pending


def prune_orphans(site_dir: Path, produced: set[Path]) -> list[str]:
    """Delete files in ``site_dir`` the build did not produce; return their names.

    Keeps site/ an exact mirror of the manifest so a deleted or renamed source
    can't leave a stale page behind. Everything here is regenerable from source,
    so removal is safe. Empty directories left over are cleaned up too.
    """
    removed: list[str] = []
    for path in sorted(site_dir.rglob("*")):
        if path.is_file() and path.resolve() not in produced:
            removed.append(str(path.relative_to(site_dir)))
            path.unlink()
    for d in sorted((p for p in site_dir.rglob("*") if p.is_dir()), reverse=True):
        try:
            d.rmdir()  # only succeeds if empty
        except OSError:
            pass
    return removed


def find_unlisted(pages: list[dict], exclude: list[str]) -> list[str]:
    """Root-level *.md not in the manifest and not deliberately excluded.

    Catches docs that silently never made it onto the site.
    """
    listed = {p["source"].resolve() for p in pages}
    excluded = {(REPO_ROOT / e).resolve() for e in exclude}
    unlisted = []
    for md in sorted(REPO_ROOT.glob("*.md")):
        if md.resolve() in listed or md.resolve() in excluded:
            continue
        unlisted.append(md.name)
    return unlisted


# --------------------------------------------------------------------------
# Build
# --------------------------------------------------------------------------

def build_site(manifest_path: Path, site_dir: Path, strict: bool = False,
               base: str | None = None, branch: str = GITHUB_BRANCH) -> int:
    pages, exclude = load_manifest(manifest_path)
    if base is None:
        base = github_base()
    site_dir.mkdir(parents=True, exist_ok=True)

    produced: set[Path] = set()
    produced |= copy_assets(site_dir)

    renderer = make_renderer()
    generated = copied = 0
    for page in pages:
        if page["kind"] == "generate":
            out_path = render_page(page, pages, site_dir, renderer)
            produced.add(out_path.resolve())
            generated += 1
        else:  # 'copy' -- kind already validated in load_manifest
            dest = copy_bespoke(page, site_dir)
            produced |= files_under(dest)
            copied += 1
        rel = out_path.relative_to(REPO_ROOT) if page["kind"] == "generate" \
            else dest.relative_to(REPO_ROOT)
        print(f"  {page['kind']:<8} {page['source'].name:<26} -> {rel}")

    print(f"built {generated} generated + {copied} copied into "
          f"{site_dir.relative_to(REPO_ROOT)}/")

    # ---- Rewrite source links to GitHub (#546) ---------------------------
    rewritten = rewrite_repo_links(site_dir, base, branch)
    if rewritten:
        print(f"rewrote {rewritten} source link(s) -> {base}/blob|tree/{branch}/")

    # ---- Validation ------------------------------------------------------
    problems = 0

    removed = prune_orphans(site_dir, produced)
    if removed:
        print(f"\npruned {len(removed)} stale file(s):")
        for name in removed:
            print(f"  - {name}")

    broken, pending = validate_links(site_dir)
    if broken:
        problems += len(broken)
        print(f"\nBROKEN internal links ({len(broken)}):")
        for page_rel, link in broken:
            print(f"  {page_rel}: {link}")
    if pending:
        distinct = sorted({link for _, link in pending})
        print(f"\nnote: {len(pending)} link(s) point at repo files not on the "
              f"site; #546 rewrites these to GitHub (known-pending):")
        for link in distinct:
            print(f"  - {link}")

    unlisted = find_unlisted(pages, exclude)
    if unlisted:
        problems += len(unlisted)
        print(f"\nunlisted source docs ({len(unlisted)}) "
              f"-- add to a manifest section or the 'exclude' list:")
        for name in unlisted:
            print(f"  - {name}")

    if problems == 0:
        print("\nvalidation: clean")
    else:
        print(f"\nvalidation: {problems} problem(s)")
        if strict:
            return 1
    return 0


# --------------------------------------------------------------------------
# CLI
# --------------------------------------------------------------------------

def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument(
        "--manifest", type=Path, default=MANIFEST,
        help="manifest JSON (default: tools/site-template/manifest.json)",
    )
    parser.add_argument(
        "--output-dir", type=Path, default=SITE_DIR,
        help="output directory (default: site/ at repo root)",
    )
    parser.add_argument(
        "--strict", action="store_true",
        help="exit non-zero if validation finds real problems (for CI)",
    )
    parser.add_argument(
        "--github-base", default=None,
        help="GitHub base URL for source links (default: derived from origin)",
    )
    parser.add_argument(
        "--github-branch", default=GITHUB_BRANCH,
        help=f"branch/ref for source links (default: {GITHUB_BRANCH})",
    )
    args = parser.parse_args(argv)
    return build_site(
        args.manifest, args.output_dir, strict=args.strict,
        base=args.github_base, branch=args.github_branch,
    )


if __name__ == "__main__":
    raise SystemExit(main())
