#!/usr/bin/env python3
"""
gen-lib-readme.py — regenerate lib/README.md from lib/*.fs sources.

Walks every lib/*.fs file, extracts:
  - Leading \\-comment block (description, "Provides:", "Requires:")
  - Word definitions: : ... ;, CODE, KCODE, VARIABLE, CONSTANT
  - Stack-effect comments (from the same line for colon defs, or a
    preceding "\\ name ( ... -- ... )" comment for CODE/KCODE words)

Filters out underscore-prefixed names (the "_foo" private convention).
Writes a single combined Markdown file with a TOC and one section per
library file.
"""

import re
import sys
from pathlib import Path

LIB_DIR = Path(__file__).parent.parent / "lib"
OUT_PATH = LIB_DIR / "README.md"

DEFN_RE = re.compile(r"^(:|CODE|KCODE|VARIABLE)\s+(\S+)(.*)?$")
# Forth's CONSTANT is postfix: `<value> CONSTANT <name>`. Match anywhere on a line.
CONST_RE = re.compile(r"^\s*\S+\s+CONSTANT\s+(\S+)\s*(.*)?$")
STACK_RE = re.compile(r"\(\s*([^()]*?)--\s*([^()]*?)\s*\)")

KIND_LABEL = {
    ":":        "colon",
    "CODE":     "CODE",
    "KCODE":    "KCODE",
    "VARIABLE": "var",
    "CONSTANT": "const",
}


def parse_header(lines):
    """Return (short_description, provides, requires).

    short_description is the first non-`Provides:`/`Requires:` comment line,
    with any leading "filename.fs — " stripped (the heading shows the
    filename already, so the prefix is redundant in the description).

    Multi-line `Provides:` / `Requires:` blocks are joined by spaces.
    The header ends at the first blank or non-`\\` line.
    """
    short = ""
    provides, requires = [], []
    current = None  # most recent labelled block, for continuation joining
    started = False
    for line in lines:
        s = line.rstrip()
        if not s:
            if started:
                break
            continue
        if not s.startswith("\\"):
            break
        started = True
        # Drop leading "\\" and at most one space.
        body = s[1:]
        if body.startswith(" "):
            body = body[1:]
        if not body.strip():
            current = None
            continue
        is_continuation = body.startswith((" ", "\t"))
        content = body.strip()
        low = content.lower()
        if low.startswith("provides:"):
            provides.append(content[9:].strip())
            current = "provides"
        elif low.startswith("requires:"):
            requires.append(content[9:].strip())
            current = "requires"
        elif is_continuation and current == "provides" and provides:
            provides[-1] += " " + content
        elif is_continuation and current == "requires" and requires:
            requires[-1] += " " + content
        elif not short:
            # Treat the first plain line as the short description.
            short = content
            current = None
        # else: drop later description lines — they're file-internal docs.
    # Strip "filename.fs — " or "filename.fs - " prefix if the short line
    # opens with the filename plus an em-dash separator.
    short = re.sub(r"^[A-Za-z0-9._/-]+\s*[—-]\s*", "", short)
    return short, provides, requires


def format_stack(before, after):
    """Render a stack effect compactly. ('', '') -> '( -- )'."""
    b, a = before.strip(), after.strip()
    if not b and not a:
        return "( -- )"
    if not b:
        return f"( -- {a} )"
    if not a:
        return f"( {b} -- )"
    return f"( {b} -- {a} )"


def find_code_stack(lines, idx, name):
    """Find a stack effect for the CODE word at `lines[idx]`.

    Looks in two places:
      - up to 8 preceding lines, for a Forth comment `\\ ... NAME ( a -- b ) ...`
        (works around dividers like `\\ ── NAME ( a -- b ) ───`).
      - up to 4 following lines, for a `;;; ( a -- b )` bare-stack comment
        inside the CODE body (a common kernel-style convention).

    Forth names contain hyphens, slashes, `?`, `!` — Python's `\\b` doesn't
    cope with those at the boundaries, so we use explicit name-char lookarounds.
    """
    namechar = r"[A-Za-z0-9_\-/\?\!]"
    name_pat = re.compile(
        r"(?<!" + namechar + r")" + re.escape(name) +
        r"(?!" + namechar + r")\s*\(\s*([^()]*?)--\s*([^()]*?)\s*\)")
    bare_pat = re.compile(r"^\s*;{2,}\s*\(\s*([^()]*?)--\s*([^()]*?)\s*\)")

    for line in lines[max(0, idx - 8):idx]:
        if not line.lstrip().startswith("\\"):
            continue
        m = name_pat.search(line)
        if m:
            return format_stack(m.group(1), m.group(2))

    for line in lines[idx + 1:idx + 5]:
        m = bare_pat.match(line)
        if m:
            return format_stack(m.group(1), m.group(2))

    return ""


def parse_file(path):
    text = path.read_text()
    lines = text.split("\n")
    short, provides, requires = parse_header(lines)

    words = []
    for i, line in enumerate(lines):
        # Try the standard prefix-keyword form first.
        m = DEFN_RE.match(line)
        if m:
            kind, name, rest = m.group(1), m.group(2), m.group(3) or ""
            if name.startswith("_"):
                continue
            stack = ""
            if kind in (":", "CODE", "KCODE"):
                # Try same-line stack effect first (works for `: name ( a -- b )`
                # and `CODE name  \\ ( a -- b )` on the same line).
                sm = STACK_RE.search(rest)
                if sm:
                    stack = format_stack(sm.group(1), sm.group(2))
            if not stack and kind in (":", "CODE", "KCODE"):
                # Fall back to preceding `\\` block or following `;;;` comment.
                stack = find_code_stack(lines, i, name)
            words.append((name, stack, KIND_LABEL[kind]))
            continue

        # Forth's `<value> CONSTANT <name>` postfix form.
        m = CONST_RE.match(line)
        if m:
            name = m.group(1)
            if name.startswith("_"):
                continue
            words.append((name, "", KIND_LABEL["CONSTANT"]))

    return short, provides, requires, words


def slug(filename):
    """Approximate the GFM auto-anchor for a heading containing this filename."""
    s = filename.lower()
    s = re.sub(r"[^a-z0-9-]", "", s)
    return s


def render():
    files = sorted(LIB_DIR.glob("*.fs"))
    out = []
    out.append("# `lib/` — Shared Forth Libraries")
    out.append("")
    out.append(
        "> *Auto-generated by `tools/gen-lib-readme.py`. "
        "Do not hand-edit — update each file's `.fs` header and "
        "re-run the generator (`make lib-readme` or "
        "`python3 tools/gen-lib-readme.py`).*")
    out.append("")
    out.append(
        "Each table lists the public words from one file. Words whose "
        "names begin with `_` are treated as private and omitted.")
    out.append("")
    out.append("---")
    out.append("")
    out.append("## Files")
    out.append("")
    for f in files:
        out.append(f"- [`{f.name}`](#{slug(f.name)})")
    out.append("")
    out.append("---")
    out.append("")

    for f in files:
        short, provides, requires, words = parse_file(f)
        out.append(f"## `{f.name}`")
        out.append("")
        if short:
            out.append(short)
            out.append("")
        if provides:
            out.append(f"**Provides:** {', '.join(provides)}")
            out.append("")
        if requires:
            out.append(f"**Requires:** {', '.join(requires)}")
            out.append("")
        if words:
            out.append("| Word | Stack | Kind |")
            out.append("|------|-------|------|")
            for name, stack, kind in words:
                stack_md = f"`{stack}`" if stack else ""
                out.append(f"| `{name}` | {stack_md} | {kind} |")
            out.append("")
        else:
            out.append("*No public words.*")
            out.append("")
        out.append("---")
        out.append("")

    return "\n".join(out)


def main():
    if not LIB_DIR.is_dir():
        sys.exit(f"lib/ not found at {LIB_DIR}")
    text = render()
    OUT_PATH.write_text(text)
    print(f"Wrote {OUT_PATH.relative_to(Path.cwd())} ({len(text)} bytes)")


if __name__ == "__main__":
    main()
