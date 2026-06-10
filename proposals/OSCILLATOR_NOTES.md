# Oscillator & Wavetable Roadmap Notes

*June 2026 — Paul Cunningham + Claude. Post-1.2 / forward-looking.*

Notes distilled from a review of the **Faust oscillators library**
([faustlibraries.grame.fr/libs/oscillators](https://faustlibraries.grame.fr/libs/oscillators/)),
filtered through our actual constraints: a 6809, a 6-bit DAC, an effective
sample rate near **15.7 kHz** (HSYNC), integer-only math, and a hard split
between *one-time table generation* and *cheap per-sample playback*.

These are **notes for future work**, not a committed plan. They sit on top of
the 1.2 async-sound engine (`lib/async-sound.fs`, `gen-*` generators).

## 1. Split wavetable generation from the playback engine

Faust models exactly this separation: its "oscillators" are signal generators,
decoupled from routing/playback. Our `gen-*` words are the same — pure table
math with no DAC, no HSYNC, no voice state. They don't belong inside the async
engine.

**Proposed structure:**

- **`lib/wavetable.fs`** (or `lib/oscillators.fs`) — `gen-sine`, `gen-square`,
  `gen-saw`, `gen-tri`, `/wave`, and the new generators below. Pure table
  fills, no dependencies. Reusable by `async-sound.fs`, the synchronous
  `sound.fs`, and any future engine; testable in isolation.
- **`lib/async-sound.fs`** — the playback engine only (`snd-poll`, `snd-fill`,
  `snd-note`, `snd-frame`, `snd-slide!`, noise) consuming a table address via
  `snd-wave-base` / `snd-waveform`.

## 2. The lens: generate-time vs play-time cost

Faust runs everything per-sample in real time; we can't (`snd-poll` must stay
~100 cy). Every idea below sorts into one bucket:

- **Generate-time** — one-time, can be expensive → bake into the table. This is
  where band-limiting and arbitrary waveform shapes live.
- **Play-time** — must stay cheap → only phase tricks survive: warp the lookup
  index, reset the phase, or pick a different table.

## 3. Feature notes (by value / effort)

### A. Variable-duty pulse / PWM — high value, trivial
*Faust: `lf_pulsetrain`, `pulsetrain(freq,duty)`, `squareN`.*
We only have a fixed 50% square. `gen-pulse ( duty addr -- )` is a threshold on
the index — unlocks the full thin/nasal → hollow pulse palette. **PWM**
(sweeping duty over time) is a signature chiptune timbre. Cheapest big win.

### B. Band-limited tables (additive) + octave mip-maps — the quality win
*Faust: `sawN`/DPW, `saw2ptr`/PTR, `polyblep_*` — most of the page is
anti-aliasing.*
At ~15.7 kHz, a naive saw/square at 1–2 kHz folds super-Nyquist harmonics back
as audible "grunge." We can't afford PolyBLEP/DPW *per sample*, **but** we can
build the table by summing sine harmonics only up to Nyquist (truncated Fourier
series) at **generate-time** — free at playback. For a wide pitch range,
generate a **few tables (≈ one per octave)** with fewer harmonics up high and
select by pitch — classic mip-mapped wavetable synthesis. Most impactful
addition for clean bright tones. (Our sine is already band-limited: 1 harmonic.)

### C. Phase distortion / Casio-CZ brightness — high SFX payoff, cheap play-time
*Faust: the `CZsaw/square/pulse/...` family with an `index` parameter.*
Phase distortion warps the linear phase→index mapping to sweep brightness with
**no filter**. In our accumulator that's an index warp before the table lookup,
driven by a brightness/`index` param — a filter-sweep *timbre* for a few cycles
inside `snd-poll`. Ideal for lasers / power-ups / alerts.

### D. Hard sync — low effort, distinctive
*Faust: `hs_phasor`, `hs_oscsin` (phase reset on a trigger).*
Reset `snd-phase` to 0 on an event while the table plays at another rate → the
classic "sync sweep." Just expose `snd-sync` / `snd-phase!`.

### E. Resonator / coupled-form sine (no table) — aligns with the memory goal
*Faust: `oscrs`, `oscb`, `oscs`, `oscrq`.*
These generate a **true** sine at ~2 multiplies/sample with **zero table**.
Having just moved tables out of the binary to save space, a table-free sine is
the logical extreme — trades 256 bytes for a couple of mults in the emitter,
and is a truer sine than the current parabolic `gen-sine`. Worth prototyping as
a sine-specific option.

### F. Phasor + phase-offset exposure — minor, enabling
*Faust: `phasor`, `oscp`, `lf_sawpos_phase`.*
Faust separates the ramp (`phasor`) from the oscillators on top. We already have
a phase accumulator; exposing phase set/offset enables multi-voice phase
relationships and underpins the sync / phase-distortion effects above.

### Lower priority for a game-SFX engine
Quadrature / state-variable sines (`quadosc`, `oscs`), DSF additive (`dsf` —
band-limited generation covers the same ground at gen-time), impulse trains
(`lf_imptrain`), waveform morphing (`twin_osc`).

## 4. Suggested sequencing

1. **Split** `lib/wavetable.fs` out of `async-sound.fs` (mechanical; enables the rest).
2. **A — pulse / PWM** (cheap, immediate timbral range).
3. **B — band-limited tables + octave mip-maps** (the anti-aliasing quality win).
4. **C — phase distortion** and **D — hard sync** (cheap playback-side SFX).
5. **E — resonator sine** (memory-vs-CPU experiment).

— *Companion to `SOUND_ENGINE_PROPOSAL.md`; built on the 1.2 async-sound
engine.*
