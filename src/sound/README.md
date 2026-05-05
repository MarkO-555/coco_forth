# CoCo Forth Sound

A minimal interactive sound demo for the TRS-80 Color Computer, built on the CoCo Forth kernel. Plays square-wave tones through the 6-bit DAC at `$FF20` — the same audio path BASIC's `SOUND` command uses, programmed directly from a Forth `CODE` word.

## What It Demonstrates

- **`snd-tone` (CODE word)** — initializes PIA0/PIA1 for DAC output and toggles `$FF20` between `$FC` and `$00` with a programmable delay. Stack effect: `( pitch duration -- )` where `pitch` is a 16-bit half-cycle delay (lower = higher frequency) and `duration` is a 16-bit cycle count.
- **PIA setup** — the canonical Daggorath recipe: `$FF01 = $FF03 = $34` (mux source-select 00 → 6-bit DAC), `$FF21 = $34`, `$FF23 = $3C` (Six Bit Sounds ON). Storing literal values rather than masking individual bits makes the result deterministic regardless of prior state.
- **`KEY` dispatch** — read a keystroke and choose one of five tone profiles via nested `IF`/`ELSE`/`THEN`.
- **`exit-basic`** — clean return to BASIC's `OK` prompt on BREAK.

## Both Kernel Modes Work

Because `snd-tone` does its own explicit PIA setup, the demo runs identically under the ROM-mode kernel (`make run`) and the all-RAM kernel (`make KERNEL_VARIANT=allram run`). The PIA registers at `$FF00-$FF3F` live in the I/O range, which is unaffected by SAM TY (the bit that pages BASIC's ROMs in and out). An earlier investigation (see `SOUND.md`) concluded that all-RAM mode "killed" DAC audio; that was actually because the prior code relied on BASIC's PIA initialization, which got wiped along with the ROMs. Doing our own setup makes the mode irrelevant.

## Controls

| Key | Action |
|-----|--------|
| 1 | Low tone (pitch 600, ~1/3 sec) |
| 2 | Mid tone (pitch 300, ~1/3 sec) |
| 3 | High tone (pitch 100, ~2/3 sec) |
| 4 | Very high tone (pitch 50, ~1.3 sec) |
| 5 | Descending sweep (7 stepped bursts) |
| BREAK | Return to BASIC |

## Build

```sh
# Build the kernel (if not already built)
cd forth/kernel && make

# Compile and run the sound demo
cd src/sound && make run
```

## Background

`SOUND.md` in this directory captures an earlier (March 2026) investigation into why DAC audio failed under the all-RAM kernel that was the default at the time. Most of those failures stemmed from `STA $FFDF` paging out the BASIC ROMs and breaking the audio path. With ROM mode now the default kernel build, the situation simplified dramatically — `snd-tone` works because BASIC's hardware initialization remains in effect.
