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
import shutil
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

def load_manifest(manifest_path: Path) -> list[dict]:
    """Load the manifest and flatten it into an ordered list of page dicts.

    Each returned page carries: source (Path), output (str), nav (str),
    section (str), kind (str). Missing ``output`` is derived from the source;
    missing ``kind`` defaults to ``generate``.
    """
    if not manifest_path.is_file():
        sys.exit(f"error: manifest not found: {manifest_path}")
    try:
        data = json.loads(manifest_path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        sys.exit(f"error: manifest is not valid JSON: {exc}")

    pages: list[dict] = []
    for section in data.get("sections", []):
        section_title = section.get("title", "")
        for entry in section.get("pages", []):
            if "source" not in entry or "nav" not in entry:
                sys.exit(f"error: manifest page missing source/nav: {entry!r}")
            source = REPO_ROOT / entry["source"]
            pages.append({
                "source": source,
                "output": entry.get("output") or output_name(source),
                "nav": entry["nav"],
                "section": section_title,
                "kind": entry.get("kind", "generate"),
            })
    if not pages:
        sys.exit("error: manifest lists no pages")
    return pages


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

def copy_assets(site_dir: Path) -> None:
    """Copy the hand-authored assets (CSS) into ``site_dir/assets``.

    Assets live under tools/site-template so a clean rebuild of site/ can
    never delete them. Copying (rather than symlinking) keeps the committed
    site/ self-contained and servable straight from git.
    """
    if not ASSETS_SRC.is_dir():
        sys.exit(f"error: assets source not found: {ASSETS_SRC}")
    dest = site_dir / "assets"
    dest.mkdir(parents=True, exist_ok=True)
    for asset in sorted(ASSETS_SRC.iterdir()):
        if asset.is_file():
            shutil.copy2(asset, dest / asset.name)


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


def build_site(manifest_path: Path, site_dir: Path) -> int:
    pages = load_manifest(manifest_path)
    site_dir.mkdir(parents=True, exist_ok=True)
    copy_assets(site_dir)

    renderer = make_renderer()
    generated = 0
    for page in pages:
        if page["kind"] != "generate":
            # 'copy' pages are handled in #544; skip for now but keep them in
            # the nav so the structure is already correct.
            continue
        out_path = render_page(page, pages, site_dir, renderer)
        rel = out_path.relative_to(REPO_ROOT)
        print(f"  {page['source'].name:<28} -> {rel}")
        generated += 1

    print(f"built {generated} page(s) into {site_dir.relative_to(REPO_ROOT)}/")
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
    args = parser.parse_args(argv)
    return build_site(args.manifest, args.output_dir)


if __name__ == "__main__":
    raise SystemExit(main())
