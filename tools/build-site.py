#!/usr/bin/env python3
"""build-site.py -- CoCo Renovation static-site builder.

Foundation skeleton for the bespoke documentation publishing pipeline
(issue #541, part of epic #539). Renders markdown source into the
committed ``site/`` directory at the repo root. Every layer -- template,
navigation, styling, link-rewriting -- is our own code; there is no
third-party static-site framework, only a markdown parser (mistune).

Scope of this skeleton: render a *single* markdown file end-to-end, so
there is a working pipeline to hang the rest of the epic on. Later issues
extend it:

  #542  shared style tokens + docs.css for generated pages
  #543  manifest-driven rendering with the shared template + nav
  #544  copy bespoke HTML (tutorials, reference) into site/ with rewriting
  #545  site manifest + internal-link validation
  #546  source-code link strategy (link out to GitHub)
  #547  landing page + PUBLISHING.md

Usage:
    python3 tools/build-site.py [SOURCE.md] [--output-dir site]

With no SOURCE, renders the default proof-of-pipeline page. The manifest
that replaces this hardcoded default arrives in #545.
"""

from __future__ import annotations

import argparse
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

# Hand-authored site source (template + assets). Kept OUT of site/ so a clean
# rebuild of the generated output can never delete it; the build copies what
# it needs into site/.
TEMPLATE_DIR = REPO_ROOT / "tools" / "site-template"
ASSETS_SRC = TEMPLATE_DIR / "assets"

# Default page rendered when no source is given -- a proof that the pipeline
# works end-to-end. Replaced by the declarative manifest in #545.
DEFAULT_SOURCE = REPO_ROOT / "COCO_RENOVATION.md"

# --------------------------------------------------------------------------
# Template
# --------------------------------------------------------------------------
#
# Shared page shell. Styling lives in the linked stylesheets (#542): tokens.css
# holds palette/type, docs.css the article + sidebar-nav layout. The sidebar
# markup itself is populated by the manifest-driven build in #543; until then
# the layout renders as a single centred article.
#
# {asset_prefix} is the relative path from the page back to site/ (e.g. "" for
# a root page, "../" for a nested one) so asset links resolve at any depth.

PAGE_TEMPLATE = """<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <link rel="stylesheet" href="{asset_prefix}assets/tokens.css">
  <link rel="stylesheet" href="{asset_prefix}assets/docs.css">
  <title>{title}</title>
</head>
<body>
  <div class="doc-layout">
    <article class="doc-article">
{body}
    </article>
  </div>
</body>
</html>
"""


# --------------------------------------------------------------------------
# Rendering
# --------------------------------------------------------------------------

def make_renderer() -> "mistune.Markdown":
    """Build the markdown->HTML converter used site-wide.

    Plugins are chosen to cover what the in-tree markdown actually uses:
    GitHub-style tables, strikethrough, task lists, footnotes, and bare-URL
    autolinking. Keep this the single place plugins are configured so every
    page renders identically.
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


def build_page(source: Path, site_dir: Path, renderer: "mistune.Markdown") -> Path:
    """Render one markdown file into ``site_dir``; return the output path."""
    if not source.is_file():
        sys.exit(f"error: source not found: {source}")

    md_text = source.read_text(encoding="utf-8")
    body = renderer(md_text)
    title = derive_title(md_text, source.stem)

    site_dir.mkdir(parents=True, exist_ok=True)
    out_path = site_dir / output_name(source)
    # Root-level page, so assets resolve at "assets/...". Nested pages (#544)
    # will pass a deeper prefix.
    out_path.write_text(
        PAGE_TEMPLATE.format(title=title, body=body, asset_prefix=""),
        encoding="utf-8",
    )
    return out_path


# --------------------------------------------------------------------------
# CLI
# --------------------------------------------------------------------------

def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument(
        "source",
        nargs="?",
        type=Path,
        default=DEFAULT_SOURCE,
        help="markdown file to render (default: the proof-of-pipeline page)",
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=SITE_DIR,
        help="output directory (default: site/ at repo root)",
    )
    args = parser.parse_args(argv)

    copy_assets(args.output_dir)

    renderer = make_renderer()
    out_path = build_page(args.source, args.output_dir, renderer)

    rel = out_path.relative_to(REPO_ROOT) if out_path.is_relative_to(REPO_ROOT) else out_path
    print(f"rendered {args.source.name} -> {rel}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
