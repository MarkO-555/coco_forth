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

| Key | Effect            | Word(s) used                       |
|-----|-------------------|------------------------------------|
| 1   | Low square        | `snd-tone(600, 100)`               |
| 2   | Mid square        | `snd-tone(300, 300)`               |
| 3   | High square       | `snd-tone(100, 600)`               |
| 4   | Very high square  | `snd-tone(50, 1200)`               |
| 5   | Smooth ascending sweep | loop of `snd-tone(220-I, 1)` |
| 6   | Slow sawtooth     | `snd-saw(4, 30, 800)`              |
| 7   | Fast sawtooth     | `snd-saw(16, 30, 800)`             |
| 8   | Triangle          | `snd-tri` (= 2× `snd-saw`)         |
| 9   | Sine              | `snd-sin(2, 120)`                  |
| 0   | High-pitch hiss   | `snd-noise(1, 400)`                |
| B   | Beep              | `snd-beep`                         |
| Z   | Laser zap         | `snd-zap` (descending sweep)       |
| X   | Boom              | `snd-boom` (= long `snd-noise`)    |
| C   | Chirp             | `snd-chirp` (3 fast blips)         |
| D   | Dock              | `snd-dock` (2-tone confirmation)   |
| H   | Hit               | `snd-hit` (impact tick)            |
| BREAK | Return to BASIC | `exit-basic`                       |

## Cost Per Effect

The CoCo runs at ~0.89 MHz, so 1 ms ≈ 890 cycles.

### Library primitives (in `forth/lib/sound.fs`)

| Word      | Bytes | Fixed cycles | Per-iteration             |
|-----------|------:|-------------:|---------------------------|
| snd-init  | 23    | 39           | —                         |
| snd-tone  | 60    | 114          | ~14·pitch + 46 per cycle  |
| snd-saw   | 59    | 124          | ~7·pitch + 43 per sample  |
| snd-sin   | 107   | 208          | ~7·pitch + 126 per sample (44/half-wave) |
| snd-pause | 14    | 212          | + 6 cy × count            |
| snd-noise | 56    | 777          | + per-sample (rng + delay) |

### Convenience effects (Forth-side wrappers)

Cycle counts include the wrapper's overhead **and** the playback time at the wrapper's hard-coded parameters.

| Word       | Bytes | Total cycles | ms   |
|------------|------:|-------------:|-----:|
| snd-beep   | 14    | 128,000      | 143  |
| snd-hit    | 14    | 65,000       | 73   |
| snd-dock   | 24    | 254,000      | 286  |
| snd-tri    | 26    | 41,000       | 46   |
| snd-zap    | 30    | 43,000       | 48   |
| snd-sweep  | 36    | 109,000      | 122  |
| snd-chirp  | 46    | 31,000       | 35   |
| snd-boom   | 14    | ~291,000     | ~327 |

### Demo-key totals (the inline calls in `dispatch`)

| Key | Cycles    | ms    |
|----:|----------:|------:|
| 1   | 844,000   | 950   |
| 2   | 1,274,000 | 1,430 |
| 3   | 868,000   | 975   |
| 4   | 895,000   | 1,005 |
| 6,7 | 202,000   | 228   |
| 9   | 739,000   | 830   |
| 0   | ~97,000   | ~109  |

Library footprint total: ~440 bytes (CODE words 249 + Forth wrappers ~190).

## Build

```sh
# Build the kernel (if not already built)
cd forth/kernel && make

# Compile and run the sound demo
cd src/sound && make run
```

## Background

`SOUND.md` in this directory captures an earlier (March 2026) investigation into why DAC audio failed under the all-RAM kernel that was the default at the time. Most of those failures stemmed from `STA $FFDF` paging out the BASIC ROMs and breaking the audio path. With ROM mode now the default kernel build, the situation simplified dramatically — `snd-tone` works because BASIC's hardware initialization remains in effect.
