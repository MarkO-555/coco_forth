# CoCo 3 Support Plan — Proposal

*May 2026 — Paul Cunningham + Claude*

## Why this exists

The kernel and libraries target the CoCo 1/2 (SAM + VDG + PIA). They contain
**zero** GIME access — there is not a single reference to `$FF90`–`$FFBF`
anywhere in `kernel/kernel.asm` or `lib/*.fs`. A real CoCo 3 runs our binaries
today *only* because the GIME impersonates the SAM and VDG; nothing we ship uses
the CoCo 3 as a CoCo 3.

This document turns "support the CoCo 3" into a concrete, prioritized work list.
It is grounded in an inventory of what the kernel and libs actually touch, so
every gap below is measured against real code (with `file:line` evidence), not
against the feature list in the abstract. For the hardware background and the
1/2 → 3 evolution, see [`../coco-guides/coco3-intro.md`](../coco-guides/coco3-intro.md).

## The governing principle: one binary, two machines

The project's identity is the CoCo 1/2 and its "constraints are features"
discipline (`COCO_RENOVATION.md`). Adopting the GIME must **not** fork that or
strand the existing audience. So the design rule for everything here:

> **Every CoCo 3 feature is gated on a runtime GIME-detection probe. On a CoCo 2
> the probe fails and the code takes the existing CoCo 1/2 path. One binary runs
> correctly on both machines.**

This makes a CoCo 3 detection word the *keystone* — nothing else should be
written until it exists, because every other feature depends on it to stay
backward-compatible.

## How the gaps sort

The features split into three tiers by **how much existing code resists them**,
which is the real predictor of effort — not how "big" the feature sounds.

- **Tier 1 — Pure additions.** No existing code assumes the feature's absence.
  Write a new library word; the GIME sits idle until called. Cheap.
- **Tier 2 — Blocked by baked-in assumptions.** A CoCo 1/2 fact is welded into
  kernel CODE words (32-column text, RG6 geometry, flat 64K). Supporting the
  CoCo 3 feature means *refactoring*, not just adding. Expensive.
- **Tier 3 — Timing model change.** The kernel is poll-driven by design and
  masks all interrupts. The GIME timer asks for a capability the kernel has
  never had. Philosophical, not just mechanical.

---

## Tier 1 — Pure additions (cheap, high compatibility value)

### 1.1 GIME / CoCo 3 detection — *the keystone*
**Gap:** nothing probes the machine; the kernel assumes CoCo 1/2 unconditionally.
**Work:** a word that detects a CoCo 3 (e.g. via a GIME-only register behavior or
the `$FFFE` reset-vector / init-register signature) and returns a flag. Every
feature below branches on it.
**Effort:** small. **Blocks:** everything else. Do first.

### 1.2 64-color palette (`$FFB0`–`$FFBF`)
**Gap:** color is hard-masked to 2-bit artifact values — `rg-pset` does
`ANDA #$03` (kernel ~1818), `spr-draw` does `ANDB #$03` (~2019). No palette word
exists.
**Work:** a `palette.fs` with words to load the 16 palette registers; constants
for the 64 colors.
**Effort:** small and self-contained (writing 16 registers). The *use* of the
palette by graphics modes is Tier 2; loading it is Tier 1.

### 1.3 RGB vs composite palette tables
**Gap:** N/A — no palette support at all today.
**Work:** because the same 6-bit value renders differently on RGB (`RRGGBB`)
vs composite (intensity+hue), ship **two** color tables and a selector
(mirroring Super Extended BASIC's `PALETTE RGB` / `PALETTE CMP`). This is a data
+ selection concern layered on 1.2.
**Effort:** small; mostly a `DATA[PY ...]` table pair.

### 1.4 Stable double-speed (`$FFD8` slow / `$FFD9` fast)
**Gap:** the kernel never touches CPU speed; runs at 0.895 MHz.
**Work:** two trivial words (`fast` / `slow`).
**Effort:** tiny — **but not free.** See Tier 3.2: it silently breaks every
cycle-timed delay in `lib/sound.fs`. Ship it *with* a fix or a guard, not alone.

---

## Tier 2 — Blocked by baked-in assumptions (expensive, high unlock)

### 2.1 80/40-column text — *the deepest gap*
**Gap:** **32 columns is a literal welded into the kernel's hottest primitives.**
- `kernel.asm:91` `SCREEN EQU $0400`, `:92` `NSCR EQU 512` (32×16)
- `EMIT` (~514–528) and `CR` (~560–570): `ADDD #32`, `ANDB #$E0` (clear low 5
  bits to align to a 32-col boundary)
- `AT` (~1092–1103): `LDA #32; MUL`
- `TYPE`: `CMPY #NSCR`
- libs echo it: `screen.fs` clears exactly 512 bytes; `rg-text`/`sg6-text` step
  `+32` per row.

**Work:** parameterize the row stride and screen size out of these CODE words
(driven by a width variable set after GIME detection), and drive the GIME into
hardware 40/80-column mode. This touches the kernel's core text path — the single
most invasive item in the plan, and the one that actually unlocks the on-device
editor/shell the project is ultimately about.
**Effort:** large. **Unlock:** the editor/shell vision.

### 2.2 Hardware text attributes (blink / underline / fg / bg per char)
**Gap:** VDG text is one byte per cell — `EMIT` encodes `ANDB #$3F / ORB #$40`,
no attribute byte.
**Work:** an attribute mode where each cell carries a second byte (bit 7 flash,
bit 6 underline, bits 5–3 fg palette, bits 2–0 bg palette). Structural change to
the text primitive; naturally paired with 2.1.
**Effort:** medium, coupled to 2.1.

### 2.3 MMU paging (`$FFA0`–`$FFAF`, 512K)
**Gap:** the memory model is flat 64K with static EQU constants (`APP_BASE`,
`FONT_BASE`, `TRIG_BASE`). The only remapping the kernel knows is the **SAM**
`$FFDF` TY toggle (kernel ~190) for all-RAM mode; `fujinet.fs` already juggles
that same bit to reach cart ROM. The GIME MMU is a *different mechanism*.
**Work:** an MMU abstraction (8×8K segments, two task sets) so "all-RAM" becomes
trivial and 512K becomes addressable via paging. The kernel's fixed memory map
has to learn to think in pages.
**Effort:** large; a new memory model. Highest *architectural* leverage (RAM
disk, overlays, multitasking).

### 2.4 New graphics modes (320×192×16, 640×192×2, …)
**Gap:** every graphics CODE word assumes RG6 256×192, 2-bit, 32 bytes/row:
`rg-pset`, `rg-line`, `spr-draw` (`#32` stride ~2003, `#$03` color ~2019),
`beam-trace`, `rg-char` (`VAR_RGBPR` default 32 ~2478). These are CoCo-1/2 VDG
primitives by construction.
**Work:** a parallel GIME graphics layer keyed off the video mode/resolution
registers (`$FF98`/`$FF99`) and the video-offset registers (`$FF9D`/`$FF9E`,
screen anywhere in 512K), reusing the palette from 1.2.
**Effort:** large; net-new graphics layer alongside the existing RG6 one.

---

## Tier 3 — The timing model (subtle, but the project already wants it)

### 3.1 Programmable GIME timer (`$FF94`/`$FF95`, FIRQ)
**Gap:** the kernel **permanently masks IRQ/FIRQ** (`ORCC #$50`, ~1699) and
installs **no interrupt handlers at all**. Every timed thing is *polled*:
`VSYNC`/`HSYNC` busy-waits (~1743–1776), key auto-repeat counts VSYNC frames,
sound is cycle-counted busy-wait. The whole kernel is poll-driven by design.
**Work:** install a real FIRQ handler driven by the GIME's programmable 12-bit
timer — a capability the kernel has never had. This is exactly what
`SOUND_ENGINE_PROPOSAL.md` wants but cannot get on CoCo 1/2 (where it falls back
to HSYNC at a fixed 15.7 kHz). On a CoCo 3 the timer gives an interrupt-driven
mixer at an arbitrary sample rate.
**Effort:** medium-large; introduces interrupt-driven execution to a polling
kernel. Highest payoff for sound and a real scheduler tick.
**Caveat to document:** the timer reload quirk (a count of 1 behaves as 3 on the
'86 GIME, 2 on the '87; roughly +2/+1 on every value) — see `coco3-intro.md`.

### 3.2 Double-speed breaks cycle-timed code
**Gap:** `snd-tone` and friends in `lib/sound.fs` are hard-coded delay loops that
assume 0.895 MHz. Enabling 1.79 MHz (item 1.4) silently doubles every pitch and
halves every delay.
**Work:** make the sound delays speed-aware (read the speed flag), or gate
double-speed off during sound, or re-derive the constants. Bundle with 1.4 — they
must not ship apart.
**Effort:** small, but a hard dependency on 1.4.

---

## What is NOT a gap (deliberately out of scope)

- **Keyboard** — the matrix scan (`KEY_TABLE`, PIA0 `$FF00`–`$FF03`) runs
  unchanged on a CoCo 3. The CTRL/ALT/F1/F2 keys already sit in the table as
  `$00` modifiers; reading them and the optional GIME key IRQ are minor, later
  additions. Not on the critical path.
- **Sound output** — the 6-bit DAC (`$FF20`) is identical hardware; `lib/sound.fs`
  already works on a CoCo 3. The *opportunity* is the timing source (3.1), not
  the output path.

---

## Suggested sequencing

The cheap-vs-deep choice is real, so two tracks rather than one strict order:

**Track A — Compatibility wins (make existing demos better on a CoCo 3):**
1. **1.1 GIME detection** (keystone — nothing ships without it)
2. **1.2 palette** + **1.3 RGB/composite tables** — existing RG6 demos can light
   up real colors instead of artifacts, gated so the CoCo 2 is untouched
3. **1.4 double-speed** + **3.2 sound fix** (shipped together)

**Track B — The unlock (what the project is ultimately for):**
4. **2.1 80-column text** (+ **2.2 attributes**) — substrate for the editor/shell
5. **3.1 GIME timer FIRQ** — real sound + a scheduler tick
6. **2.3 MMU** and **2.4 GIME graphics** — the big architectural layers, last

Track A is days-scale and immediately visible. Track B is the months-scale
commitment that decides whether CoCo Renovation stays a CoCo 1/2 project that
*runs* on a 3, or becomes a GIME-native environment. **This proposal does not
make that call** — it scopes the work so the call can be made with eyes open.

## Relationship to the roadmap

This is a candidate future phase for `ROADMAP.md` (it slots near Phase 6 hardware
work, but is independent of the cartridge effort). Per the project's cross-cutting
commitments, each item above becomes its own tracking issue in `issues.jsonl`
*before* work starts, ships with XRoar/hardware verification, and updates
`reference.html` alongside the code. Note that XRoar's `coco3` machine target
makes all of this testable in the emulator before real hardware.
