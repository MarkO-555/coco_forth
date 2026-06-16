# The Color Computer 3 — An Introduction (the GIME era)

The CoCo 1 and CoCo 2 are, to software, the same machine: a Motorola 6809E
wrapped in three support chips — the MC6883 SAM (timing and address
multiplexing), the MC6847 VDG (video), and a pair of MC6821 PIAs (I/O). The
CoCo 3 (1986) keeps the 6809E, keeps the 6-bit sound DAC, keeps the same passive
keyboard matrix — and then changes almost everything else by collapsing the SAM
and VDG into one custom chip, the **GIME**.

The single most useful way to understand the CoCo 3 is this: **the GIME rarely
*replaces* hardware — it inserts a layer of indirection and programmability under
what was already there.** Color stops being baked into pixel bits and goes
through a *palette*. Memory stops being a flat 64K and goes through an *MMU*.
Timing stops being a pair of fixed sync pulses and goes through a *programmable
timer*. The CPU, the DAC, the keyboard are untouched; what's new is configurability.

That was a deliberate budget decision. In Mark Siegel's own words on the design
of the machine:

> "I had been given a choice... I could put in either a sound chip or a UART.
> Not enough money for both. So I opted for neither. I put in programmable timers
> and an interrupt controller."
> — Mark Siegel, "father of the CoCo 3" ([tandy-trs80.com](https://tandy-trs80.com/introducing-the-color-computer-3/))

Tandy spent the silicon budget on *programmability*, not peripherals. That one
sentence explains the whole 1/2 → 3 evolution.

## The lineage at a glance

| Subsystem | CoCo 1 (1980) | CoCo 2 (1983) | CoCo 3 (1986) |
|-----------|---------------|---------------|---------------|
| **CPU** | MC6809E @ 0.895 MHz | MC6809E @ 0.895 MHz | MC6809E @ 0.895 MHz, **stable 1.79 MHz** double-speed |
| **Video/timing chip** | MC6883 SAM + MC6847 VDG | SAM + VDG (late: MC6847T1) | **GIME** (ACVC, part TCC1014) — SAM+VDG in one |
| **RAM** | 4K–64K | 16K–64K | **128K std, 512K official**, 1–2 MB unofficial |
| **Colors** | 8–9 fixed | 8–9 fixed | **64-color palette, 16 simultaneous** |
| **Max graphics** | 256×192×2 (RG6) | 256×192×2 | **640×192×2 / 320×192×16** |
| **Text** | VDG 32-col alphanumeric, no attributes | same (+ lowercase on T1) | **HW 40/80-col with per-char attributes** |
| **Memory model** | flat 64K, SAM page select | flat 64K | **MMU: 8×8K segments, 2 task sets** |
| **Sound output** | 6-bit DAC + analog mux | 6-bit DAC + analog mux | **6-bit DAC + analog mux (identical)** |
| **Sound timing** | HSYNC 15.7 kHz / VSYNC 60 Hz (fixed) | same | **+ programmable GIME timer (FIRQ)** |
| **Keyboard** | PIA0 passive matrix scan | same | **same matrix** + CTRL/ALT/F1/F2 + GIME key IRQ |
| **Video out** | composite / RF | composite / RF | **+ analog RGB (CM-8 monitor)** |

The CoCo 1 and CoCo 2 are functionally interchangeable to a programmer — the
CoCo 2 is a cost-reduction in a smaller case, and the only software-visible
wrinkle is the late **MC6847T1** VDG variant that added true lowercase. The real
fork is **2 → 3**.

## The story

The CoCo 3 grew out of a cancelled project. Tandy had planned a "Deluxe Color
Computer," but — per Siegel — "Motorola ran short of video/memory controllers,"
and the Deluxe died. The CoCo 3 was the leaner machine that rose from it,
championed inside Tandy by **Mark Siegel**, widely called the "father of the
CoCo 3."

The defining constraint was money. The budget allowed a sound chip *or* a UART,
not both — so Siegel chose *neither* and spent the transistors on programmable
timers and an interrupt controller (see the quote above). This is why the CoCo 3
makes sound through the same primitive DAC as the CoCo 1, yet can run music
players the older machines never could: it has a *clock* the others lack.

Microsoft held the BASIC. Rather than license new work, Siegel handed **Microware**
a disassembly of the existing Microsoft ROM plus a list of new commands to add.
Microware implemented them by copying the Microsoft ROM into RAM at boot and
*patching* it — the result shipped as **Super Extended Color BASIC**.

A prototype reached Microware in **August 1985**, before the GIME was even
fabricated: it used banks of programmable PAL chips and discrete logic to stand
in for the unbuilt ASIC, and shipped with **512K RAM standard** — later cut to a
128K base model to hit a price point. The CoCo 3 was announced **July 30, 1986**
at the Waldorf-Astoria in New York. The CoCo line was finally cancelled in
**October 1990**.

## The GIME chip

The GIME is a single custom VLSI gate array that replaces *both* the SAM and the
VDG. Tandy's official name was the **Advanced Color Video Chip (ACVC)**, part
number **TCC1014**; the community name **GIME** stands for *Graphics Interrupt
Memory Enhancer*. Die imaging during later reverse-engineering revealed the
designers' names on the silicon: **J.L. Bruister** (VLSI Technology) and
**J.M. Prickett** (Tandy).

Two production revisions exist, nicknamed after their development codenames:

| Revision | Year | Notes |
|----------|------|-------|
| **"Tequila"** | 1986 | Original; several documented bugs (see *Myths & gossip*) |
| **"Tortilla"** | 1987 | Bug-fix revision; partially corrected timing/sync issues |

All GIME control lives in one register block at **`$FF90`–`$FFBF`**:

| Range | Purpose |
|-------|---------|
| `$FF90`–`$FF9F` | Initialization, interrupt enable, timer, video mode/resolution, scroll/offset |
| `$FFA0`–`$FFAF` | MMU segment registers (two task sets) |
| `$FFB0`–`$FFBF` | 16 palette registers |

This is an intro-level summary. For the full bit-field breakdown of every
register, see **Sock's GIME register reference**
([6809.org.uk](https://www.6809.org.uk/twilight/sock/gime.html)) and the Lomont
hardware reference (linked below); a dedicated in-tree `gime.md` is a natural
follow-on document.

## Subsystem by subsystem — how development changes

Each subsystem below is framed as **the CoCo 1/2 way → the CoCo 3 way → what it
means for how you write code.** Items are marked *fundamental* (a different
mental model) or *additive* (old approach still works, new capability layered on).

### Memory — *fundamental*

- **CoCo 1/2:** A flat 64K address space. The SAM picks a coarse display page;
  there is no general remapping. Your memory map is static — the kernel's
  ROM-mode vs all-RAM split and the `$FFDF` SAM TY toggle are exactly this
  SAM-era world.
- **CoCo 3:** The GIME **MMU** (enabled by INIT0 `$FF90` bit 6) maps the 6809's
  64K logical space as **eight 8K segments**, each pointed by a register in
  `$FFA0`–`$FFAF` holding a 6-bit physical block number. There are **two task
  register sets** (`$FFA0`–`$FFA7` and `$FFA8`–`$FFAF`), selected by the TR bit
  in INIT1 `$FF91` — flip one bit and the whole address space changes.
- **What it means:** memory goes from "everything you have is the 64K you can
  see" to "page what you need into the window." This is what makes overlays,
  RAM disks, and OS-9 Level II multitasking practical — and it makes "all-RAM"
  trivial where on the CoCo 1/2 it is a careful dance.

### Video modes — *fundamental*

- **CoCo 1/2:** You choose one entry off a *fixed menu* of modes (SG4–SG24,
  CG1–6, RG1–6) by agreeing SAM mode bits (`$FFC0`–`$FFC5`) with VDG mode bits
  (PIA1 `$FF22`). Display memory lives in the 64K at a SAM-selected offset.
- **CoCo 3:** Resolution and color depth are configured *orthogonally* through
  GIME video registers — `$FF98` (mode) and `$FF99` (resolution: lines/field,
  horizontal resolution, and 2/4/16 colors). The screen can be read from
  **anywhere in the full memory space** via the vertical-offset registers
  `$FF9D`/`$FF9E`, and the GIME supports **hardware scroll / virtual screens**
  (`$FF9F` horizontal offset, `$FF9C` vertical smooth scroll).
- **What it means:** you stop *picking a mode* and start *programming a video
  controller* — choosing width, depth, and the memory address it scans, then
  scrolling it in hardware.

### Color — *fundamental*

- **CoCo 1/2:** A pixel's bits *are* its color, drawn from a fixed palette with
  the CSS bit selecting one of two color sets. No indirection.
- **CoCo 3:** A pixel's bits select one of **16 palette registers**
  (`$FFB0`–`$FFBF`); the 6-bit value *in that register* defines the actual color,
  chosen from **64 possible**. Color is now indirect — change a palette register
  and every pixel using it changes at once.

> **Gotcha — the same palette value looks different on RGB vs composite.** Those
> 6 bits mean different things depending on the output. On **RGB** they are
> `RRGGBB` (two intensity bits per gun). On **composite** they encode intensity
> + hue (bits 5–4 = intensity level, bits 3–0 = hue) — a chroma scheme, not RGB.
> The same screen renders as different colors on a CM-8 monitor versus a TV,
> which is why Super Extended BASIC has both `PALETTE RGB` and `PALETTE CMP`, and
> why CoCo 3 software often ships separate palette tables per output.

### Text — *fundamental*

- **CoCo 1/2:** "Text" is really the VDG's alphanumeric mode: 32 columns,
  uppercase-biased, color baked in, **no attributes**.
- **CoCo 3:** True **hardware text** at 40/80 columns (also 32/64) with a
  per-character **attribute byte**: bit 7 flash/blink, bit 6 underline,
  bits 5–3 foreground palette, bits 2–0 background palette.
- **What it means:** a real 80-column editor or shell becomes possible *in
  hardware*, with colored/underlined/blinking text, instead of being painted by
  hand into a graphics buffer.

### Keyboard — *additive*

- **CoCo 1/2:** A passive matrix wired to PIA0. There is no keyboard controller —
  you drive a column-select byte and read which rows come back low. (This is
  exactly what the kernel's `KEY_TABLE` scan does.)
- **CoCo 3:** The **same matrix**, scanned the same way — existing scanning code
  runs unmodified. Added: the **CTRL, ALT, F1, F2** keys occupying
  previously-empty matrix cells, plus an optional GIME **keyboard interrupt** so
  you can be event-driven instead of polling.

### Sound — output *identical*, timing *fundamental*

- **CoCo 1/2:** Sound comes from a **6-bit DAC** (the high 6 bits of PIA1
  `$FF20`) feeding an **analog multiplexer** shared with the joystick and
  cassette, plus a single-bit sound path at `$FF22` bit 1. Every sample is
  CPU-driven. The only steady interrupt clocks are **HSYNC** (`$FF01`, ~15.7 kHz)
  and **VSYNC** (`$FF03`, ~60 Hz) — both fixed.
- **CoCo 3:** The audio output path is **unchanged** — same DAC, same mux, no
  sound chip (recall Siegel's budget decision). What's new is a **programmable
  12-bit GIME timer** (`$FF94`/`$FF95`) that fires **FIRQ** (enabled via `$FF93`
  bit 5) at a rate you choose, clock-selectable via INIT1 `$FF91` (TINS bit:
  a ~279 ns fast tick or the HSYNC-rate tick).
- **What it means:** the CoCo 1/2 can only update the DAC at a fixed sync rate;
  the CoCo 3 can drive an interrupt-driven mixer at any sample rate. This is the
  capability the project's `proposals/SOUND_ENGINE_PROPOSAL.md` explicitly notes
  it lacks on stock CoCo 1/2 hardware — "every CoCo 3 music player and sample
  tracker found in the wild" relies on this timer.

### CPU speed — *additive*

- **CoCo 1/2:** A SAM poke (`POKE 65495,0`) can double the clock to ~1.79 MHz,
  but it is marginal and corrupts timing-sensitive I/O.
- **CoCo 3:** A **stable** 1.79 MHz double-speed mode (`$FFD9` fast / `$FFD8`
  slow). The CPU is still the 6809E; the **Hitachi 6309** is a pin-compatible
  aftermarket swap (extra registers/opcodes, NitrOS-9 native mode) — but the
  stock chip is soldered, so it is a desolder-and-socket job.

### Video output — *additive*

- **CoCo 1/2:** Composite and RF only.
- **CoCo 3:** Adds **analog RGB** output for the Tandy CM-8 monitor alongside
  composite/RF.

## Myths & gossip

> **The hidden 256-color mode — legend.** A surviving Tandy R&D document
> ("Color Computer Custom Video Proposed Feature List") listed "512 possible
> (256 displayed) colors," and insiders described a fragile activation involving
> the timer and an address near `$FE00`. But designer **John Prickett says the
> GIME is 6-bit / 64-color only** ("I very much doubt there is a hidden 256-color
> mode"), Steve Bjork says it never shipped, and Mark Siegel could not recall
> details. Treat as **legend, not confirmed in production silicon.**

> **"CoCo 3 composite is worse than the CoCo 2" — contested.** Real composite
> complaints are documented: the GIME's HSYNC pulse is too wide (worse on the
> 1986 part), pushing video off-center and breaking some modern video-encoder
> ICs and RGB→S-video adapters; composite also needs its encoder clock synced to
> the GIME or you get dot-crawl, and users report blurry text. But **no
> authoritative source states a clean regression versus the CoCo 2.** Present as
> community observation, not a spec fact.

> **GIME timer bugs.** Neither GIME can run a timer count of 1: store 1 and the
> 1986 part behaves as 3, the 1987 part as 2 (roughly +2 / +1 on every value, a
> clock-propagation quirk). Setting the timer to zero immediately re-asserts the
> interrupt on real hardware — the root of the "Arkanoid sound bug"
> investigation. And the `$FF99` setting intended for **210 scanlines** produces
> "infinite lines" and corrupts the display — never fixed in either revision.

> **The reverse-engineering saga.** Tandy never published the GIME's internals
> (only the Service Manual register list), so "no one truly knows how it works"
> and emulators/FPGAs are clean-room approximations — which is why GIME-bashing
> demos can look different across emulators. **Roger Taylor's "Project 256"**
> (launched Nov 2021) had both the 1986 and 1987 dies decapped and imaged; that
> imaging is what revealed the Bruister/Prickett names.

> **The rushed ROM.** Super Extended Color BASIC is literally Microsoft's
> Extended Color BASIC ROM copied into RAM at boot and patched by Microware. The
> patched code shipped with several bugs and incomplete support for the new
> hardware, and the new hi-res commands (`HSCREEN`, `HCOLOR`, `HSET`, `HLINE`,
> `HCIRCLE`, `HGET`/`HPUT`, `PALETTE`, `ATTR`, `WIDTH 40/80`) are slow and
> impractical to use from BASIC.

## What this means for CoCo Renovation

Because the GIME impersonates the SAM and VDG so faithfully, **the project's
current kernel would boot on a real CoCo 3 unmodified, in compatibility mode** —
it simply wouldn't use anything new. Everything interesting about the machine
sits on the *other* side of the GIME wall, and you only cross it deliberately:

- **MMU paging** turns "all-RAM mode" into a non-event and opens 512K — a very
  different memory discipline than the current ROM-mode / all-RAM split.
- **Hardware 80-column text with attributes** is the natural substrate for a real
  on-device editor and shell (the project's stated long-term goal).
- **The programmable timer** gives a real scheduler tick and the interrupt-driven
  sample audio the sound proposal currently has to design around.

This is direction-setting, not a commitment. The project's "constraints are
features" philosophy (`COCO_RENOVATION.md`) is a deliberate CoCo 1/2 stance — the
64K ceiling and fixed timing *are* the discipline. Adopting the GIME would trade
some of that scarcity for capability, and that is a choice to make later, with
eyes open, not one this document makes.

## References

### Technical (registers, modes, timing)
- **Lomont, *Color Computer 1/2/3 Hardware Programming*** — best single-document
  hardware reference; corroborates the register map, the 6-bit DAC, the CoCo 1/2
  HSYNC/VSYNC-only interrupts, the GIME timer, and the artifact/colorset
  behavior. [lomont.org](https://www.lomont.org/software/misc/coco/Lomont_CoCoHardware.pdf)
- **Sock's GIME register reference** — authoritative per-register, per-bit detail
  (`$FF90`–`$FFBF`, MMU ranges, palette layout, timer reload quirk).
  [6809.org.uk](https://www.6809.org.uk/twilight/sock/gime.html)
- **Tandy CoCo 3 Service Manual** — canonical register authority.
  [archive.org](https://archive.org/details/TandyServiceManualColorComputer3)
- **CoCo 1/2 hardware** — `coco-guides/coco_technical_reference.pdf` (SAM/VDG/PIA;
  no GIME content) and `coco-guides/vdg-modes.md` (the VDG counterpart to this doc).

### History
- **Pitre & Loguidice, *CoCo: The Colorful History of Tandy's Underdog
  Computer*** — GIME designer attribution, design history, the exec who killed
  the line. (See also `legends/references.md`.)
- **Mark Siegel interview, "Introducing the Color Computer 3"** — the
  sound-chip-vs-timer quote and the Deluxe CoCo origin.
  [tandy-trs80.com](https://tandy-trs80.com/introducing-the-color-computer-3/)
- **Sub-Etha Software, "The Color Computer 3 Prototype"** — the Aug-1985
  prototype, Steve Bjork's timer advocacy, Microware's firmware role.
  [subethasoftware.com](https://subethasoftware.com/2024/03/11/the-color-computer-3-prototype/)

### Lore & reverse-engineering
- **Nickolas Marentes, "256 Mode"** — the hidden-256-color investigation and the
  210-line bug, with Prickett/Bjork/Siegel quotes.
  [nickmarentes.com](https://nickmarentes.com/ProjectArchive/256mode.html)
- **Vintage is the New Old, "Reverse engineering Tandy's CoCo 3 GIME chip"** —
  Project 256, die imaging.
  [vintageisthenewold.com](https://www.vintageisthenewold.com/reverse-engineering-tandys-coco-3-gime-chip)
- **tlindner, "CoCo 3 GIME Timer Tests"** — the timer-zero re-assert and Arkanoid
  bug. [tlindner.macmess.org](https://tlindner.macmess.org/?p=762)
- **CoCopedia — GIME / Color Computer 3** — Tequila/Tortilla revisions, Super
  ECB ROM-copy-and-patch. [cocopedia.com](https://www.cocopedia.com/wiki/index.php/GIME)

For the people and companies behind the CoCo 3 (Siegel, Prickett, Bruister,
Bjork, Microware, Cloud-9, Disto, the NitrOS-9 team), see `legends/whos-who.md`
and `legends/companies.md`. Source citations live in `legends/references.md`.
