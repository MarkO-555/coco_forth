# CoCo Renovation — Roadmap

*Reset 2026-06-16.* The previous roadmap had grown stale: its Phase 1 demo
audit (issues 421-444) is **done**, kernel **1.2 shipped** (the async sound
engine), and the bulk of what was filed as "open" turned out to be an
accumulated *wishlist* — ideas logged early and never pruned, which made the
backlog look far heavier and more committed than it was. This version keeps
only genuinely-live work, honestly prioritized, and parks the rest in an
explicit **Icebox** (see bottom) rather than leaving it masquerading as planned
work.

**Where things stand:** a cross-developed Forth (kernel + `fc.py`) with ROM and
all-RAM build profiles; a complete 13-chapter tutorial; ~15 demos; trig + sound
libraries; an interactive ITC-threading tutorial; FujiNet/clock live on real
hardware. Released as **v1.2**.

For per-issue detail, see `issues.jsonl` (statuses: `open`, `in-progress`,
`done`, `icebox`, `closed`).

---

## Open strategic question (deliberately undecided)

Before committing to a multi-month direction, one fork needs an owner's call —
it is **not** resolved here:

- **On-device, self-hosting Forth** — the `COCO_RENOVATION.md` vision:
  *program the machine on the machine.* Today everything is cross-compiled from
  a laptop via `fc.py`; the CoCo runs pre-compiled threads but cannot compile on
  itself. The elegant path is not a separate 6809 assembler — Forth is natively
  self-hosting, so this means an on-device interpreter/compiler. **Unstarted; no
  epic filed yet.**
- **Cross-developed, published, on real hardware** — accept the current model;
  make it excellent, browser-explorable, and running on iron, without an
  on-device compiler.

The tracks below advance the project under *either* answer. Track 3 in
particular is where the fork bites — pick the north star before committing to
on-device-compiler epics.

---

## Track 1 — Publish & polish (highest leverage)

Goal: make what already exists excellent and reachable. This is the best
return on effort right now — the project is good but hard to *experience*.

- **409 (high)** — embed runnable **XRoar (WASM)** in the HTML docs (run demos
  in-browser while reading)
- **410 (high)** — host the docs as a static website
- **467 (high)** — audit + bulletproof the shipped demos (the per-demo
  standardization sweep is done; this is the final polish pass)
- **407** — tutorial flow audit (re-read all 13 chapters against the current kernel)
- **408** — demo appendix (a featured page per demo)
- **413** — "Working with Claude" guide (the modern, AI-assisted workflow — a
  genuinely novel angle worth telling well)
- **412** — document the DSK build & distribution workflow
- **411 (low)** — bespoke tutorial artwork (owner: Paul)

---

## Track 2 — Real hardware

Goal: escape the emulator — the cartridge thesis from `COCO_RENOVATION.md`.
Important but not urgent; no committed timeline.

- **405** — ROM cartridge image
- **404** — serial / SD loader (bit-banged RS-232)

*(RP2350 co-processor parked — see Icebox; revisit after 404/405.)*

---

## Track 3 — Interactive / on-device tooling

Small, useful introspection words that pay off regardless of the fork — and
that become the seed of the on-device experience if that north star is chosen.

- **27** — `WORDS` (word lister)
- **26** — `.S` stack viewer
- **25** — 6809 disassembler / `SEE`
- **22 (low)** — memory monitor / hex editor
- **28 (low)** — breakpoint trap
- **490 (low)** — `fc.py` vocabularies / word lists

---

## Track 4 — Sound & libraries (opportunistic, post-1.2)

Resume when motivated; not on the critical path.

- **522 / 523** — `fc.py` shared-asm / macro support, then DRY the async-sound emit core
- **525** — DP-locate voice state (CPU win)
- **530** — sine / wavetable ring modulator (the full 2-op path; wants 522/523 first)

---

## Icebox (parked 2026-06-16 — reopen on demand)

Not rejected, just not committed: ideas with no concrete consumer or trigger.
Kept here so the open list reflects *real* commitments. Pull any back the moment
there's a reason to build it.

- **Demos (22):** Snake (13), Drawing (14), Life (16), Starfield (17),
  Clock/stopwatch (18, superseded), Maze (19), Text adventure (20),
  Sprite animation (21), Reaction game (23), Serial terminal (24),
  Joy to the World (38), Rocket (39), Hi-res font (40), Back to Bach (41),
  Electronic piano (42), Ready Aim Fire (44), Typing test (45),
  Music composer (46), Bouncing ball (47), Dice/Craps (48),
  Speed reading (49), Math quiz (50)
- **Libraries (4):** circle (33), flood fill (34), turtle/DRAW (35), PLAY music (37)
- **Tools (2):** memory compare (29), execution tracer (30)
- **Hardware spike:** RP2350 co-processor (406)
- **Research (3):** international CoCo community (459), DriveWire/toolchain
  authors (460), pre-XRoar emulator lineage (461)

---

## Cross-cutting commitments (unchanged)

- Every change gets a tracking issue in `issues.jsonl` BEFORE work starts.
- Every code change ships with verification (XRoar capture or hardware run).
- Demos work without optional peripherals (FujiNet, joystick) — graceful
  fallback or a clear message.
- `reference.html` and generated docs stay in sync with code, not as a follow-up.

---

## Suggested next move

Track 1 is the obvious place to start regardless of the fork: **409 + 410**
(browser-runnable docs) turn the whole project from "clone a repo and install a
toolchain" into "click a link and play." Highest leverage, no dependency on the
strategic decision. Resolve the on-device-Forth fork before opening Track 3
epics.
