# CoCo Forth 1-Bit Sound

A short interactive demo that exercises the CoCo's *second* audio path:
the **single-bit sound output** at `$FF22` bit 1, mixed into the same
audio bus as the 6-bit DAC but driven by a single GPIO bit. See
`../sound/` for the DAC counterpart.

## What It Demonstrates

- **`snd-tone1` (CODE word)** — square tone produced by read-modify-write
  toggling of `$FF22` bit 1 (`LDB $FF22 / EORB #$02 / STB $FF22`). RMW
  is mandatory: `$FF22` also carries VDG mode bits 7–3 and RS-232 IN
  bit 0, so a constant store would scramble the screen mode. Stack:
  `( pitch duration -- )`, same convention as `snd-tone` — pitch
  numbers from the DAC demo transfer directly.
- **`snd-click1` (CODE word)** — single toggle; the primitive
  percussive element.
- **`snd-noise1`** — RNG-gated toggle: each iteration consults `rng`
  and toggles the bit only when the random byte's high bit is set, so
  the toggle pattern is irregular (noise rather than tone).
- **PIA1 PB DDR setup** — by default PIA1 PB bit 1 is configured as an
  *input* (BASIC sets up the VDG control bits 7–3 but leaves bit 1
  alone), so toggling the data register does nothing audible until the
  DDR bit is set. `snd-init` now flips `$FF23` bit 2 to enter DDR
  mode, ORs `$02` into `$FF22`, then returns `$FF23` to data mode +
  sound enable (`$3C`).

## Controls

| Key | Effect              | Word(s) used                    |
|-----|---------------------|---------------------------------|
| 1   | Low square          | `snd-tone1(600, 100)`           |
| 2   | Mid square          | `snd-tone1(300, 300)`           |
| 3   | High square         | `snd-tone1(100, 600)`           |
| 4   | Very high square    | `snd-tone1(50, 1200)`           |
| 5   | Ascending sweep     | loop of `snd-tone1(220-I, 1)`   |
| 6   | Click train         | 40× `snd-click1` + `snd-pause`  |
| 7   | Noise burst         | `snd-noise1(1, 400)`            |
| 8   | A/B: DAC vs 1-bit   | `snd-tone(300,200)` then `snd-tone1(300,200)` |
| BREAK | Return to BASIC   | `exit-basic`                    |

## Build

```sh
cd kernel && make           # build kernel if not already built
cd src/sound1bit && make run
```

Also works under the all-RAM kernel:

```sh
make KERNEL_VARIANT=allram run
```

## Hardware Notes

The 1-bit output sits on the same audio bus as the 6-bit DAC but is
mixed in *independently* of the audio MUX (`$FF01`/`$FF03`). The
sound-enable line (`$FF23` bit 3, CB2) is shared, so any path that
enables sound for the DAC also enables 1-bit output. The two can be
written interleaved within a single demo (key 8 demonstrates this),
though they can't sound *simultaneously* without timing tricks since
the MCU is single-threaded.

A single toggle = a click; toggling at audio rates = a square tone.
The duty cycle is always 50% — there's no amplitude control, so the
1-bit path can't produce sawtooth, triangle, or sine waveforms. For
those, use the DAC primitives (`snd-saw`, `snd-tri`, `snd-sin` in
`lib/sound.fs`).
