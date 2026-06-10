# Oscillator & Wavetable Roadmap Notes

*June 2026 - Paul Cunningham + Claude. Post-1.2 / forward-looking.*

Notes distilled from a review of the Faust oscillators library
([faustlibraries.grame.fr/libs/oscillators](https://faustlibraries.grame.fr/libs/oscillators/)),
the Korg Electribe / TR-808 drum-synthesis lineage, and classic 8-bit SFX
tricks - filtered through our actual constraints: a 6809, a 6-bit DAC, an
effective sample rate near 15.7 kHz (HSYNC), integer-only math, and a hard
split between one-time table generation and cheap per-sample playback.

These are notes for future work, not a committed plan. They build on the 1.2
async-sound engine (lib/async-sound.fs, the gen-* generators).

## Design north star

> State-of-the-art for a 6809 - frugal on both RAM and cycles, whether
> synthesizing in real time or baking a wavetable ahead of time.

Concretely:

- Stay in the phase-accumulator + table paradigm. It is the cheapest way to get
  an arbitrary periodic waveform: one ADDD to advance, one indexed LDA to read.
  Everything below is a small twist on it.
- Algorithmic where it is cheaper than a table; a table where it is cheaper than
  math. A square/saw/PD index-warp or an LFSR needs no table. A rich
  band-limited timbre is cheaper to read than to recompute per sample.
- Spend the cycles on what defines the character. On a 6809 that is the MUL
  instruction - ring/AM modulation the 6502 and Z80 cannot afford. Lean into it.
- Modulate slowly for free. Anything that can run at frame rate (60 Hz) instead
  of sample rate (LFOs, envelopes, wave-sequencing, ratio sweeps) is essentially
  free - it lives in snd-frame, not snd-poll.

### RAM / CPU budget to design against

| Resource | Rough unit | Note |
|---|---|---|
| snd-poll emit | ~100 cy measured | per produced sample; ~16-32/frame for bleep audio |
| extra osc (2nd phase acc + table read) | ~40 cy | the cost of going 1-op to 2-op |
| ring mod (MUL + sign + scale) | ~25 cy | 6809-only; impossible cheaply elsewhere |
| phase-mod combine (scale + add to index) | ~15-25 cy | cheaper than ring mod |
| self-feedback (add last output to phase) | ~5 cy | tone-to-noise knob |
| LFO / envelope step | ~30-50 cy per frame | negligible amortized |
| one wavetable | 256 bytes | share across operators where possible |
| full 2-op + LFO voice state | ~16 bytes | DP-able for cheap access |
| Karplus-Strong delay line | ~32-64 bytes | the one RAM-hungry idea |

*(Cycle figures are estimates pending fc.py --cycles once coded - measure, do
not trust.)* The headline: a 2-op voice sharing one 256-byte table lands around
~160 cy/emit and ~280 bytes total - lean enough for a game.

## 1. Split wavetable generation from the playback engine

Faust models exactly this separation: its oscillators are signal generators,
decoupled from routing/playback. Our gen-* words are the same - pure table math,
no DAC, no HSYNC, no voice state. They do not belong inside the engine.

- lib/wavetable.fs (or lib/oscillators.fs) - gen-sine/square/saw/tri, /wave, and
  the new generators below. Pure, dependency-free, testable alone; reusable by
  async-sound.fs, the synchronous sound.fs, and any future engine.
- lib/async-sound.fs - playback only, consuming a table via snd-wave-base.

## 2. The lens: generate-time vs play-time

Every idea sorts into one bucket:

- Generate-time - one-time, can be expensive, bake into the table. Home of
  band-limiting and arbitrary waveform shapes.
- Play-time - must stay cheap, only phase tricks: warp the lookup index, reset
  the phase, multiply by a second oscillator, or pick a different table.

## 3. Proposed compact voice model (2-op + LFO)

A minimal FM/ring voice - the Electribe/OPL idea, sized for a 6809. The goal is
maximum timbral range per byte and per cycle.

```
Per-voice play-time state  (~16 bytes, DP-located):
  car-phase   2   carrier phase accumulator
  car-inc     2   carrier increment (pitch)
  mod-phase   2   modulator phase accumulator
  mod-inc     2   modulator increment (= ratio * car-inc)
  mod-depth   1   how hard OSC2 bends OSC1
  mod-mode    1   0=off 1=ring 2=phase-mod 3=AM 4=sync
  feedback    1   self-FM amount (0=off)
  last-out    1   previous output (for feedback)
  amp         1   amplitude shift
  frames      2   remaining duration

Shared:
  one 256-byte sine table  (carrier AND modulator read it)
  LFO + envelope state      (~6 bytes, stepped in snd-frame)
```

Both operators reading one shared sine table is the key frugality move: classic
FM/ring uses sine operators, so a single 256-byte table feeds the whole voice.
Drop even that 256 bytes with the resonator sine (Single-oscillator tricks,
item E) at some CPU cost.

## 4. Two-operator modulation - the Electribe / 808 model

Two oscillators, OSC2 bending OSC1. This is the Korg Electribe "modulation"
section (ER-1 depth/speed; EA-1 Ring/Sync/OscMod) and the TR-808 lineage - the
cowbell (two squares) and the cymbal/hat (six ring-modded squares).

- Ring mod - out = carrier * modulator. Metallic, bell-like, clangy; the
  drum-machine timbre. 6809 MUL makes it cheap (see 6809-specific techniques).
- Phase mod / "FM" - add the modulator's scaled output to the carrier's table
  index before lookup. Bells, basses, growls, clangs. Two knobs own the space:
  - Ratio = mod inc / carrier inc. Integer ratio gives harmonic/tonal;
    non-integer (1.41, 1.68 ...) gives inharmonic, metallic, drums. This is the
    bass-vs-cowbell control.
  - Depth = bend amount (an LFO/envelope target - sweep it for wah or attack).
- AM - carrier * (1 + mod); audio-rate tremolo gives sidebands (gentler ring mod).
- Sync - reset carrier phase each modulator cycle (the classic sync sweep).

One mod-mode byte plus ratio and depth spans an enormous range from a ~16-byte
voice.

## 5. LFO modulation matrix

A per-frame (60 Hz, stepped in snd-frame) low-rate oscillator routed to a
target. Free, because it is frame-rate not sample-rate.

| Shape | Target | Result |
|---|---|---|
| triangle / sine | pitch (car-inc) | vibrato |
| triangle / sine | amplitude | tremolo |
| saw / triangle | pulse duty | PWM sweep |
| any | phase-distortion index | auto-wah |
| any | 2-op mod-depth or ratio | evolving FM/ring |
| sample-and-hold (LFSR) | pitch | R2D2 / computer burble (canonical SFX) |

Sample-and-hold = sample our existing snd-seed LFSR each LFO step; stepped
random pitch is instantly recognizable and almost free. (snd-slide! is already a
degenerate one-shot ramp LFO - generalize it to a looping shape + depth.)

## 6. Single-oscillator table tricks

- A. Variable-duty pulse / PWM (high value, trivial) - gen-pulse ( duty addr -- )
  is a threshold on the index; unlocks the thin/nasal to hollow palette. Sweeping
  duty with the LFO is a signature chiptune sound.
- B. Band-limited tables + octave mip-maps (the quality win) - at 15.7 kHz a
  naive saw/square folds super-Nyquist harmonics back as aliasing grunge. Build
  the table by summing sine harmonics only up to Nyquist (truncated Fourier) at
  generate-time - free at playback. Generate a few tables (about one per octave)
  and pick by pitch (mip-mapped wavetables). Faust's sawN/DPW, saw2ptr/PTR,
  polyblep_* do this per-sample; we do it offline.
- C. Phase distortion / Casio-CZ brightness (high SFX payoff, cheap) - warp the
  linear phase-to-index mapping to sweep brightness with no filter; a
  filter-sweep timbre for a few cycles in snd-poll. (Faust CZ* family.)
- D. Hard sync (low effort) - reset snd-phase on a trigger, gives a sync sweep.
- E. Resonator / coupled-form sine (no table) - generates a true sine at ~2
  multiplies/sample with zero table; trades 256 bytes for CPU, and is truer than
  the parabolic gen-sine. (Faust oscrs/oscb/oscs.)
- F. Phasor / phase-offset exposure - expose phase set/offset (snd-phase!);
  underpins sync, PD, and multi-voice phase relationships.

## 7. 6809-specific techniques

The differentiator. Most 8-bit chiptune ran on the 6502 or Z80, which have no
multiply. The 6809 does.

- A. MUL-based ring mod and AM - MUL is 8x8 to 16 in D, ~11 cy. Ring mod and
  audio-rate AM are practical for us and were essentially impossible for them.
  This is a real, defensible "our machine is different" feature - the Electribe
  metallic-drum trick in real time on hardware from 1982. Handle signs around the
  unsigned MUL (bias or sign-correct; our tables are signed deviation).
- B. Dual index registers (X and Y) plus accumulator-offset addressing (LDA B,X)
  make 2-op lookup tidy: carrier table in X, modulator in Y, two reads with no
  pointer reloads.
- C. Self-feedback FM - store the last output, add it back into the oscillator's
  own phase. One operator plus a feedback knob sweeps pure tone to buzzy to noise
  (the DX7 feedback op); a free harshness control, ~5 cy.

## 8. Drum & SFX recipe cookbook

Most are buildable now or with the 2-op layer. ([now] = doable on the 1.2 engine
today.)

| Voice | Recipe |
|---|---|
| Kick [now] | pitch-swept sine/tri (~150 to 50 Hz in ~50 ms via snd-slide!) + fast amp decay |
| Tom [now] | same, tuned higher |
| Snare | LFSR noise + a ~180/330 Hz tone, layered, fast decay |
| Hat / cymbal | bright LFSR noise, very fast decay; metallic = ring-modded squares (808 six-osc, approximated with 2-3) |
| Cowbell (808) | two squares ~540 and ~800 Hz summed (2-op), short decay |
| Clap [now] | 3-4 rapid noise bursts (note queue) + a longer tail |
| Laser / zap [now] | saw + steep negative snd-slide! |
| Powerup [now] | ascending arpeggio via the queue |
| Coin [now] | two-tone via the queue |

## 9. Wildcards (clever / novel)

- Periodic "short-mode" LFSR noise - flip the LFSR to a short period and the hiss
  becomes a pitched, buzzy tone (the NES noise channel's famous short mode). One
  tap-mask change; authentic chiptune color, near-free.
- Bit-crush + rate-crush - we already rate-reduce noise (snd-noise-div); apply
  the same sample hold to tones and mask to 4/3 bits ($F0 / $E0) for deliberate
  lo-fi grit.
- Wavefolding - reflect the sample past a threshold (s > t becomes 2t - s); cheap
  West-coast harmonics, great when animated by the LFO.
- Wave-sequencing - swap snd-wave-base between tables each frame (PPG / Prophet-VS
  evolving timbres); nearly free.
- Karplus-Strong - a noise burst into a short feedback delay (ring buffer +
  average-adjacent lowpass) gives genuinely plucked strings and drum membranes
  from noise. The "wow" wildcard; needs an N-byte buffer + a little per-sample
  work, pitch = sample-rate / buffer length.
- Supersaw / detuned stack - a few slightly-detuned phase accumulators summed;
  thick leads (costs one osc + one add each).

## 10. Suggested sequencing

1. Split lib/wavetable.fs out of async-sound.fs (mechanical; enables the rest).
2. Pulse / PWM (Single-oscillator tricks A) - cheap, immediate timbral range.
3. 2-op modulation (Electribe model) on the shared-sine voice model: ring mod
   first (the 6809 bragging right), then phase-mod with ratio/depth.
4. LFO matrix, including sample-and-hold - vibrato/tremolo/PWM and the burble
   SFX, all frame-rate cheap.
5. Band-limited tables + mip-maps (Single-oscillator tricks B) - the
   anti-aliasing quality pass.
6. Phase distortion, hard sync, short-mode noise.
7. Karplus-Strong as the flagship physical-percussion experiment.

Together, the voice model + 2-op + LFO are the design's heart: a ~16-byte,
~160 cy/emit, one-shared-table 2-op voice with a free frame-rate LFO - a tiny
FM/ring drum-synth voice that leans on the one thing the 6809 has and its 8-bit
peers do not.

- *Companion to SOUND_ENGINE_PROPOSAL.md; built on the 1.2 async-sound engine.*
