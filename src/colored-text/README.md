# colored-text — RG6 colored-text palette swatch

Diagnostic demo for the copy-mask colored-text feature (issues #534 / #535).
RG6 artifact text is recolored by ANDing each font byte with `KVAR-RGCOLMASK`
during the glyph copy — no separate colored fonts, zero extra RAM.

It prints the same line three times, once per mask, so the artifact colors
can be eyeballed and the mask→color mapping pinned down.

## Confirmed mapping

Under the stock CSS that `rg-init` sets (`$F8` via `set-pia`):

| Mask  | Binary     | Color | Notes                          |
|-------|------------|-------|--------------------------------|
| `$FF` | `11111111` | white | identity default, no recolor   |
| `$55` | `01010101` | blue  |                                |
| `$AA` | `10101010` | red   |                                |

The `$FF` row doubles as a regression check — it must remain white, identical
to pre-feature output. Verified in XRoar (`coco2bus`, NTSC composite).

## Usage in your own code

```forth
$55 KVAR-RGCOLMASK C!     \ blue
S" HELLO" 2 5 rg-type
$FF KVAR-RGCOLMASK C!     \ reset to white
```

The mask is honored by both the kernel `rg-char` primitive and the Forth
`rg-type` in `lib/rg-text.fs`.

## Build & run

```sh
make            # build colored-text.bin
make run        # launch in XRoar
```

Press any key to return to BASIC (BREAK also exits).
