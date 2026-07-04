# Publishing the Documentation Site

This project builds a self-contained static website into `site/` from a single
command:

```sh
python3 -m pip install -r tools/requirements.txt   # once: installs mistune
make site                                          # build site/ end-to-end
```

Everything under `site/` is **generated** — never hand-edit it. Edit the
source, run `make site`, commit the result. The whole pipeline is one bespoke
script, `tools/build-site.py`; there is no static-site framework, only a
markdown parser (mistune).

## The one rule for each kind of source

Every page on the site comes from one of two source kinds, declared in the
manifest:

| Kind       | Source                     | Rule |
|------------|----------------------------|------|
| `generate` | a markdown file            | render markdown → HTML, wrap in the shared template (header, sidebar nav, footer) |
| `copy`     | a hand-authored HTML file or tree | copy **verbatim** into `site/`, preserving its repo-relative path |

Plus one special page:

- **Landing** — `site/index.html` is generated from a cover template with a
  card grid built from the manifest sections. Its version number is read from
  `kernel/kernel.asm` (`KERN_VERSION`) so it can't drift.

Why copy bespoke HTML instead of converting it? The tutorial book and
`reference.html` are hand-crafted with their own layout and styling. Preserving
their repo-relative path (`tutorials/` → `site/tutorials/`, `reference.html` →
`site/reference.html`) means their own relative links — `../reference.html`,
`../style.css`, sibling chapters, images — resolve unchanged. No rewriting, and
the book renders identically to the source.

## The manifest

`tools/site-template/manifest.json` is the single source of truth for what's on
the site. **Adding a doc is a one-line edit.**

```json
{
  "exclude": [ "CLAUDE.md", "README.md", "presentation-notes.md" ],
  "sections": [
    {
      "title": "The Book",
      "pages": [
        { "source": "tutorials", "nav": "Tutorial", "output": "tutorials/index.html", "kind": "copy" },
        { "source": "reference.html", "nav": "Language Reference", "kind": "copy" }
      ]
    },
    {
      "title": "About",
      "pages": [
        { "source": "COCO_RENOVATION.md", "nav": "The Vision" }
      ]
    }
  ]
}
```

Per-page fields:

- `source` (required) — repo-relative path.
- `nav` (required) — short label for the sidebar and the landing cards.
- `output` (optional) — site-relative output path. Derived if omitted:
  `generate` → `basename.html` at the site root; `copy` → the source's own
  repo-relative path. A `copy` **directory** must give `output` explicitly (its
  landing page), since a tree has no single file.
- `kind` (optional) — `generate` (default) or `copy`.

The page `<title>` comes from the source's first `# H1`, not the manifest.

Section titles become the sidebar-nav groups **and** the landing-page card
groups, so both stay in sync with the manifest automatically.

`exclude` lists repo-root `*.md` deliberately kept off the site, so the
unlisted-source check can tell "omitted on purpose" from "forgotten."

## Styling — which CSS applies where

All stylesheets pull palette and type from one token file so the site reads as
one publication.

| Context | Stylesheets | Notes |
|---------|-------------|-------|
| Tokens (everywhere) | `assets/tokens.css` | `--green*`, surfaces, fonts. Single source of truth, mirrors the book. |
| Generated article pages | `tokens.css` + `assets/docs.css` | Article body + sticky sidebar-nav layout. |
| Landing page | `tokens.css` + `assets/landing.css` | Cover, hero, card grid, gallery. |
| The book (copied) | its own `tutorials/style.css` | Untouched — renders identically to source. |

Stylesheet **source** lives under `tools/site-template/assets/` and is copied
into `site/assets/` by the build. Editing the copies in `site/assets/` directly
would be overwritten on the next build — always edit the source.

## Links

The build handles links in three ways, checked on every `make site`:

1. **Internal links** that resolve within `site/` are left as-is.
2. **Source references** — links that resolve to a real repo file that isn't on
   the site (e.g. a demo's `../../src/hello/`, `kernel/README.md`) are rewritten
   to GitHub URLs (`blob` for files, `tree` for directories, fragments kept).
   The GitHub base is derived from the `origin` remote; override with
   `--github-base` / `--github-branch`.
3. **Genuinely broken links** (resolve nowhere) are reported by validation.

Validation also **prunes orphans** (files in `site/` the build didn't produce,
so `site/` mirrors the manifest exactly) and **flags unlisted** root `*.md`.

Run `python3 tools/build-site.py --strict` to exit non-zero on real problems
(broken links, unlisted docs) — suitable for CI. Rewritten source links and
clean builds pass.

## Adding a new page

1. Put the source in the repo (a `.md`, or hand-authored `.html`).
2. Add one line to the right section in `manifest.json`.
3. `make site`.
4. Confirm `validation: clean`, then commit `site/` along with the source.

## Files at a glance

| Path | Role |
|------|------|
| `tools/build-site.py` | The whole builder: render, copy, landing, link rewrite, validation. |
| `tools/site-template/manifest.json` | What's on the site (sections, pages, exclude). |
| `tools/site-template/assets/` | Stylesheet source (`tokens.css`, `docs.css`, `landing.css`). |
| `tools/requirements.txt` | Pins `mistune`. |
| `site/` | Generated output — committed, servable straight from git. |
