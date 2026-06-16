# Display Modes — CoCo 1/2 (VDG) vs CoCo 3 (GIME)

A side-by-side reference for every text and graphics display mode across the two
hardware generations. This is the comparison companion to two deeper docs:
[`vdg-modes.md`](vdg-modes.md) carries the full CoCo 1/2 VDG detail (byte
layouts, color sets, mode-switching code), and [`coco3-intro.md`](coco3-intro.md)
carries the CoCo 3 / GIME architectural context. This file is the lookup table
that puts them next to each other.

## The fundamental difference in how a mode is chosen

On the **CoCo 1/2** a display mode is a single entry off a *fixed menu*. You pick
one of a handful of named modes (SG4–SG24, CG1–6, RG1–6) by agreeing **SAM mode
bits** (`$FFC0`–`$FFC5`) with **VDG mode bits** in PIA1 (`$FF22` bits 7–3). The
resolution, color count, and byte layout all come bundled together — you cannot
mix and match.

On the **CoCo 3** you *configure a video controller*. Resolution, color depth,
character-row height, and refresh are set independently through two GIME
registers — **`$FF98`** (video mode) and **`$FF99`** (video resolution) — and the
screen reads from anywhere in 512K via the offset registers (`$FF9D`/`$FF9E`).
The "modes" below are just useful *combinations* of those bits, not a fixed menu.

> **Compatibility note.** The GIME also emulates the SAM+VDG, so every CoCo 1/2
> mode in this document still works on a CoCo 3 unchanged — the project's kernel
> uses exactly those. The GIME-native modes are the *additional* capability.

---

## Text modes

| Generation | Mode | Columns × Rows | Cell | Colors | Per-cell bytes | VRAM | Attributes |
|------------|------|----------------|------|--------|----------------|------|------------|
| **CoCo 1/2** | VDG Alphanumeric | 32 × 16 | 8×12 | 2 (CSS: green or orange) | 1 | 512 | none |
| **CoCo 3** | GIME 32-col | 32 × 16/24 | 8×8 | 16-palette (w/ attrs) | 2 (char+attr) | 1,024–1,536 | optional |
| **CoCo 3** | GIME 40-col | 40 × 24 | 8×8 | 16-palette (w/ attrs) | 2 | 1,920 | optional |
| **CoCo 3** | GIME 64-col | 64 × 24 | 8×8 | 16-palette (w/ attrs) | 2 | 3,072 | optional |
| **CoCo 3** | GIME 80-col | 80 × 24 | 8×8 | 16-palette (w/ attrs) | 2 | 3,840 | optional |

The CoCo 1/2 has exactly **one** text mode: the VDG's 32×16 alphanumeric grid,
uppercase-biased, color baked in by the CSS bit, **no per-character attributes**.
(The VDG can mix text and semigraphics per cell via bit 7 — see `vdg-modes.md` —
but it is still 32 columns.)

The CoCo 3 adds true hardware text at 32/40/64/80 columns. Row count is
scan-lines ÷ cell-height: a 192-line field with 8-line cells gives **24 rows**,
so the standard hi-res text screens are **40×24** and **80×24**. Each cell is
**two bytes** — a character code followed by an attribute byte — so an 80×24
screen is 80 × 24 × 2 = 3,840 bytes.

### GIME text-mode register settings

Text mode is selected by **BP = 0** in `$FF98`; column count comes from the HRES
field of `$FF99` (in text mode only bits 4 and 2 are decoded — bit 3 is
don't-care):

| Columns | HRES (`$FF99` bits 4-2) |
|---------|--------------------------|
| 32 | `0x0` |
| 40 | `0x1` |
| 64 | `1x0` |
| 80 | `1x1` |

Attribute consumption is gated by **CRES bit 0 of `$FF99`**: `1` = attribute
bytes are read (the odd bytes), `0` = they are ignored and characters use the
default palette.

### The GIME attribute byte

When attributes are enabled, each character's second byte is:

```
Bit 7  Flash      1 = character blinks
Bit 6  Underline  1 = underline
Bits 5-3  Foreground color  → palette registers 8-15  (3-bit value + 8)
Bits 2-0  Background color  → palette registers 0-7
```

> **Load-bearing detail:** the foreground field indexes the **upper** 8 palette
> registers (`$FFB8`–`$FFBF`) — the 3-bit value plus 8 — while the background
> field indexes the **lower** 8 (`$FFB0`–`$FFB7`). Foreground and background draw
> from different halves of the palette.

The CoCo 1/2 has **no equivalent** — there is no attribute byte, no blink, no
underline, and no programmable color in text mode.

---

## Graphics modes

### CoCo 1/2 — VDG (the fixed menu)

The VDG tops out at **256×192, 2-color** (RG6 / PMODE 4) and **128×192, 4-color**
(CG6). On NTSC, the 2-color high-res modes produce ~4 *artifact* colors — this is
the project's default RG6 graphics surface. Full byte layouts and color sets are
in [`vdg-modes.md`](vdg-modes.md); summarized here for comparison:

| Mode | Resolution | Colors | VRAM | Bytes/row |
|------|-----------|--------|------|-----------|
| CG1 | 64×64 | 4 | 1,024 | 16 |
| RG1 | 128×64 | 2 | 1,024 | 16 |
| CG2 | 128×64 | 4 | 1,536 | 32 |
| RG2 | 128×96 | 2 | 1,536 | 16 |
| CG3 | 128×96 | 4 | 3,072 | 32 |
| RG3 | 128×192 | 2 | 3,072 | 16 |
| CG6 | 128×192 | 4 | 6,144 | 32 |
| **RG6** | **256×192** | **2 (4 artifact)** | **6,144** | **32** |

Plus the semigraphics modes (SG4 64×32 through SG24 64×192, 8 colors, in 512 B–6 K).

### CoCo 3 — GIME

The GIME builds a mode from independent fields. Color depth sets pixels-per-byte
(2-color = 8 px/byte, 4-color = 4 px/byte, 16-color = 2 px/byte); horizontal
pixels = bytes/row × pixels-per-byte; total VRAM = bytes/row × scan-lines.

| Resolution × colors | Bytes/row | VRAM (192-line) | HSCREEN |
|---------------------|-----------|-----------------|---------|
| 640×192×2 | 80 | 15,360 | **3** |
| 640×192×4 | 160 | 30,720 | **4** |
| 512×192×2 | 64 | 12,288 | — |
| 512×192×4 | 128 | 24,576 | — |
| 320×192×4 | 40 | 7,680 | **1** |
| 320×192×16 | 80 | 15,360 | **2** |
| 256×192×2 | 32 | 6,144 | — |
| 256×192×4 | 64 | 12,288 | — |
| 256×192×16 | 128 | 24,576 | — |
| 160×192×16 | 40 | 7,680 | — |
| 128×192×16 | 32 | 6,144 | — |

200-line and 225-line variants exist (set the LPF field, below): e.g. 320×200×16
= 16,000 bytes. The Super Extended BASIC `HSCREEN` command maps to the modes
marked above (HSCREEN 1–4); there is no graphics HSCREEN 0 (it returns to text).

> **The 16-color and 80-column-graphics modes have no VDG equivalent at all.** The
> VDG has no 16-simultaneous-color mode and no programmable palette — its colors
> are fixed by the VDG color sets and the CSS pin. The GIME draws its 16 on-screen
> colors from 16 palette registers, each chosen from 64. This is the starkest
> capability gap between the generations.

---

## The controlling registers, side by side

| | CoCo 1/2 (VDG) | CoCo 3 (GIME) |
|--|----------------|----------------|
| **Mode select** | SAM V0–V2 (`$FFC0`–`$FFC5`) + PIA1 `$FF22` bits 7–3 (A\*/G, GM2-0, CSS) | `$FF98` (BP, H50, LPR) + `$FF99` (LPF, HRES, CRES) |
| **Color** | fixed sets; CSS bit picks one of two | 16 palette registers `$FFB0`–`$FFBF`, 64-color |
| **Screen address** | SAM offset F0–F6 (`$FFC6`–`$FFD3`), = offset × 512, within 64K | `$FF9D`/`$FF9E` vertical offset, ×8, anywhere in 512K |
| **Horizontal scroll** | none | `$FF9F` (HVEN virtual screen + 2-byte-step pan) |

### `$FF98` — Video Mode Register

```
Bit 7  BP    1 = graphics modes, 0 = text modes
Bit 5  BPI   1 = composite color phase invert
Bit 4  MOCH  1 = monochrome on composite out
Bit 3  H50   1 = 50 Hz video, 0 = 60 Hz
Bits 2-0  LPR  lines per character row:
              00x=1  010=2  011=8  100=9  101=10  110=11  111=*infinite
```

### `$FF99` — Video Resolution Register

```
Bits 6-5  LPF   lines per field (on-screen scan lines):
                00 = 192   01 = 200   10 = *undefined   11 = 225
Bits 4-2  HRES  graphics: bytes/row    text: chars/row
                000=16   001=20   010=32   011=40        (text: 0x0=32, 0x1=40,
                100=64   101=80   110=128  111=160         1x0=64, 1x1=80)
Bits 1-0  CRES  graphics: 00=2 colors  01=4 colors  10=16 colors  11=undefined
                text:     bit 0 = attribute enable
```

> **There is no 210-line setting.** The LPF `10` value is undefined and produces
> "infinite lines" — this is the documented 210-line display bug noted in
> `coco3-intro.md`, not a usable mode.

### Video memory placement (`$FF9D`/`$FF9E`/`$FF9F`)

The vertical offset registers form a 16-bit value (`$FF9D` = MSB, `$FF9E` = LSB)
that is the video-start address ÷ 8 — so the screen can begin at any 8-byte
boundary across the **full 512K** physical space. `$FF9F` bit 7 (HVEN) enables a
256-byte virtual row pitch for hardware horizontal scrolling, with bits 6–0
giving a pan offset in 2-byte steps. The CoCo 1/2's SAM offset, by contrast, only
positions the screen within the 64K space in 512-byte steps.

---

## Source notes

- **CoCo 1/2 (VDG):** values from [`vdg-modes.md`](vdg-modes.md), derived from
  `coco_technical_reference.pdf` and the MC6847 datasheet.
- **CoCo 3 (GIME) bit fields** (`$FF98`/`$FF99`/`$FF9D`–`$FF9F`, attribute byte):
  **Sock's GIME register reference** ([6809.org.uk](https://www.6809.org.uk/twilight/sock/gime.html))
  and the Lomont hardware reference (attribute byte). See `legends/references.md`.
- **The per-mode `$FF98`/`$FF99` *combinations* and the HSCREEN mapping** are
  *derived* from Sock's bit-field definitions and cross-checked against community
  resolution/byte-count figures (coco3.com, Sub-Etha Software, Robert Gault's
  CC3 notes). No single authoritative source publishes a byte-for-byte hex mode
  table; for byte-exact verification consult the Tandy *CoCo 3 Service Manual* or
  *Super Extended BASIC Unravelled*. The resolution / color / VRAM figures
  themselves are confirmed across sources.
