# CoCo Forth Async Sound

An interactive showcase of the **non-blocking** sound engine for the TRS-80
Color Computer. Unlike `src/sound` (whose effects stop the program while they
play), every effect here is produced cooperatively a sample at a time, so the
marker keeps animating and the keyboard stays live while sound plays.

Built on `lib/async-sound.fs` (the engine) + `lib/wavetable.fs` (the
generators). See `reference.html` (Asynchronous Sound / Waveform Tables) and
`proposals/SOUND_ENGINE_PROPOSAL.md` for the full design.

## What it demonstrates

- **Cooperative HSYNC polling** - the engine writes the 6-bit DAC (`$FF20`) by
  polling the HSYNC flag (`$FF01`) rather than taking an interrupt; no kernel
  changes, no busy-wait that blocks the game.
- **A phase-accumulator wavetable voice** - sine from a runtime-generated table
  (`gen-sine-hq`, built from the kernel sin table), plus table-free
  **algorithmic** saw / square / triangle (`snd-shape`).
- **Per-frame modulation** applied in `snd-frame`, the way a game would drive it:
  - **pitch slide** (`snd-slide!`) - laser/zap sweeps,
  - **amplitude envelope** (`snd-env!`) - smooth fades/decays,
  - **square-wave ring modulation** (`snd-ringmod!`) - metallic/bell timbres.
- **White noise** with adjustable pitch (`snd-noise-div`) and a smooth decay.
- **A small note queue** so multi-step effects (chirp, dock, coin, powerup,
  siren) sequence without blocking.

Run with `make run`. Works identically under the all-RAM kernel
(`make KERNEL_VARIANT=allram run`) - the audio path is in the I/O range,
unaffected by ROM/RAM paging.

## Controls

| Key | Effect   | Shows off |
|-----|----------|-----------|
| 1-4 | Square tones (880/440/220/110 Hz) | algorithmic square waveform |
| 5   | Sweep            | descending pitch slide (sine) |
| 6 7 | Sawtooth (220/440 Hz) | algorithmic saw waveform |
| 8   | Triangle (330 Hz) | algorithmic triangle waveform |
| 9   | Sine             | true sine table + amplitude envelope (fades out) |
| 0   | Noise            | bright white-noise hiss (LFSR) |
| B   | Beep             | short square blip |
| Z   | Zap / maser      | saw, fast downward slide |
| X   | Boom             | low noise rumble with smooth decay |
| C   | Chirp            | three sine blips with silent rests (queue) |
| D   | Dock             | two-tone chime (queue) |
| H   | Hit              | short downward tick |
| R   | Ring             | steady square ring modulation (metallic) |
| G   | Gong             | ring modulation + amplitude decay = bell |
| W   | Warp             | ring modulation + pitch slide (sidebands sweep) |
| K   | Kick             | drum: pitch drop + fast amplitude decay |
| M   | Coin             | two-tone pickup, second note fades (queue) |
| P   | Powerup          | rising major arpeggio (queue) |
| S   | Siren            | wail up then down (bidirectional slide, queue) |
| BREAK | Quit           | clean return to BASIC `OK` |

## Footprint

The engine + generators are ~1.1 KB of code plus one 256-byte wavetable in RAM
(the demo generates its sine into `$7000`; a 64K app would use `$9200`). The
algorithmic waveforms (saw/square/triangle) need no table at all. See the
`Footprint:` notes in the library headers / `reference.html`.
