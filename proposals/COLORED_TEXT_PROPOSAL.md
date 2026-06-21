# Colored RG6 Text via Copy-Mask — Proposal

*June 2026 — Paul Cunningham + Claude*
*Originated from the Space Warp title-screen color discussion. Tracking issue
#534.*

## Why this exists

RG6 text today is white-only. `rg-char` block-copies an 8-byte glyph from the
font into VRAM, and the artifact-safe font (`lib/font-art.fs`) pre-bakes every
lit pixel as the `11` (white) artifact pair. There is no color parameter, so
every string the kernel draws comes out white, even the status words "RED" /
"GREEN" / "YELLOW".

The pixel/sprite layer already speaks the full RG6 palette (black/blue/red/
white via 2-bit pairs), so colored *graphics* are easy. Colored *text* is the
gap. This proposal closes it for **near-zero cost and zero extra RAM** by
coloring the glyph as it is copied, rather than shipping or generating separate
colored fonts.

## The artifact-color model (recap)

Each VRAM byte holds four horizontal pixel pairs. A pair's value selects the
artifact color:

| Pair | Value | Color |
|------|-------|-------|
| `00` | 0 | black |
| `01` | 1 | blue |
| `10` | 2 | red |
| `11` | 3 | white |

(Blue vs red on the `01` / `10` pairs ultimately depends on column parity and
the CSS bit; the assignment above matches the convention `plot-dots` and the
sprite code already use. Final color is eyeball-confirmed in XRoar.)

The font encodes lit pixels as `11` and background as `00`, with the 4th pair
always `00` (inter-character gap).

## The idea — one AND during the copy

To recolor a white glyph, mask each `11` pair down to a single bit. Because the
background pairs are `00`, they AND to `00` regardless, so only the lit pixels
change and the black stays black:

| Mask | Binary | Effect on a `11` pair | Resulting text |
|------|--------|-----------------------|----------------|
| `$FF` | `11111111` | `11` (unchanged) | **white** (default) |
| `$55` | `01010101` | `01` | **blue** |
| `$AA` | `10101010` | `10` | **red** |

`$FF` is the identity mask, so existing callers and every other coco app are
unaffected — this is a strictly additive change with no regression surface.

## Where it goes — the rg-char copy loop

The kernel primitive's inner loop is a straight byte copy
(`kernel/kernel.asm:2169`):

```asm
@copy   LDA     ,Y+          ; load font byte
        STA     ,X           ; store to VRAM
        LDA     VAR_RGBPR
        LEAX    A,X          ; next VRAM row
        DECB
        BNE     @copy
```

The change is a single masking instruction between the load and the store:

```asm
@copy   LDA     ,Y+
        ANDA    VAR_RGCOLMASK   ; <-- recolor: $FF=white, $55=blue, $AA=red
        STA     ,X
        LDA     VAR_RGBPR
        LEAX    A,X
        DECB
        BNE     @copy
```

`VAR_RGCOLMASK` is a new one-byte kernel variable defaulting to `$FF`, exposed
to apps as `KVAR-RGCOLMASK` exactly like the existing `KVAR-RGFONT`,
`KVAR-RGVRAM`, `KVAR-RGCHARMIN`, etc. (the rg-char config vars at
`kernel.asm:2473+`). An app draws colored text by setting the mask, calling
`rg-type`, and resetting to `$FF`:

```forth
$55 KVAR-RGCOLMASK C!    \ blue
S" SPACE WARP" rg-type
$FF KVAR-RGCOLMASK C!    \ back to white
```

## Cost

| Resource | Cost |
|----------|------|
| CPU | one `ANDA VAR_RGCOLMASK` per glyph row = 8 per character (~40 cy/char). Negligible. |
| RAM | **zero.** One kernel variable byte. No colored font copies (contrast the font-swap approach, which needs 472 bytes per extra color). |
| Kernel size | ~3 bytes (the `ANDA`) + one variable + one `kvar` export. |
| Regression | none — default `$FF` reproduces current behavior byte-for-byte. |

This is the key advantage over the two alternatives considered: pre-baking
colored fonts (472 bytes RAM each) or runtime-generating them from the white
font (472 bytes RAM each + a generator pass). Coloring in the copy spends a
handful of cycles per character and no memory.

## Palette ceiling

The change buys white, blue, and red (plus black background). That is the whole
RG6 artifact palette; NTSC offers nothing more. "Colored text" means picking one
of those three for a given string, not arbitrary RGB. Per-character or
per-pixel multicolor within one glyph is out of scope (would need a wider font
format and a different renderer).

## Alternatives considered

- **Pre-baked colored fonts.** Ship `font-art-red` / `font-art-blue` tables and
  point `KVAR-RGFONT` at them. Clean, but 472 bytes RAM per color and no
  per-call flexibility. Rejected on memory.
- **Runtime-generated colored fonts.** Generate the colored tables at init by
  masking the white font into free RAM, then swap `KVAR-RGFONT`. Same 472
  bytes/color RAM cost as above, plus generator code. The copy-mask is the same
  AND applied lazily at draw time instead of eagerly at init, so it dominates.
- **App-side colored-copy `CODE` word (fallback).** An app that does not want a
  kernel change can duplicate the rg-char copy loop in its own `CODE` word with
  `ANDA #imm` baked in. Costs app code, forgoes the shared-kernel benefit, but
  needs no coco change. Worth documenting for apps that must stay on a stock
  kernel.

## Implementation plan

1. **Kernel.** Add `VAR_RGCOLMASK` (default `$FF`) near the other rg-char
   config vars; insert `ANDA VAR_RGCOLMASK` in `CODE_RG_CHAR`'s `@copy` loop;
   export `KVAR-RGCOLMASK`. Document the `$FF`-is-identity contract beside it.
2. **Demo.** Extend an existing RG6 text demo to print one line each in white,
   blue, and red, confirming the artifact colors on hardware/XRoar and pinning
   down the `$55`/`$AA` → blue/red mapping under the demo's CSS setting.
3. **Verify the mapping.** `$55` vs `$AA` to blue vs red depends on column
   parity and CSS; capture the demo in XRoar and record which mask yields which
   color so apps get a documented constant, not a guess.
4. **Consumers (optional).** Space Warp would use this for a colored title and
   possibly the COND/SOS status words (the place where colored "RED" actually
   reads as red). Tracked separately in the space-warp repo if adopted.

## Open questions

- **CSS interaction.** The mask picks the pair bit; CSS picks which of blue/red
  that bit renders as. Should the demo expose CSS so an app can choose its
  blue/red polarity, or is the stock CSS the only target? Pin in step 3.
- **Background half-pairs.** Glyphs whose lit run starts on an odd pixel column
  could fringe at the glyph edge under a non-white mask. The font's `00`
  inter-character gap pair should absorb this, but worth a close look in the
  demo at small sizes.
- **Combining with `KVAR-RGFONT`.** The mask and a custom font compose freely
  (mask applies to whatever font bytes are copied), so a future bold/alt font
  inherits coloring for free. No action needed, just noting the orthogonality.

— *Filed alongside `lib/font-art.fs` (artifact-safe font) and the rg-char
config vars in `kernel/kernel.asm`. Companion to
`proposals/SOUND_ENGINE_PROPOSAL.md`.*
