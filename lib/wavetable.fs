\ wavetable.fs — waveform-table generators for the CoCo sound engine
\
\ Provides: /wave, gen-sine, gen-sine-hq, gen-square, gen-saw, gen-tri
\
\ Requires: kernel primitives only (DO/LOOP, *, /MOD, 2*, C!, ...).
\
\ A wavetable is 256 signed bytes (-124..+124 deviation about the DAC
\ midpoint).  Generate one into any free RAM at startup, then point the sound
\ engine at it (snd-waveform).  Generating at runtime keeps ~1KB of tables out
\ of the program binary — useful for 64K apps short on space: build them in
\ free hi RAM (e.g. $9200 in all-RAM mode) instead of consuming program space.
\
\ Independent of lib/async-sound.fs — tables here, playback there.

256 CONSTANT /wave       \ bytes per wavetable

\ ── Generators ( addr -- ) ───────────────────────────────────────────────
\ Each fills a /wave-byte signed table at addr.  One-time cost at startup, so
\ written in plain Forth.

\ gen-square — square wave: +124 first half, -124 second.
: gen-square  ( addr -- )
  /wave 0 DO
    I 128 < IF 124 ELSE -124 THEN
    OVER I + C!
  LOOP DROP ;

\ gen-saw — rising sawtooth ramp (deviation -128..+127).
: gen-saw  ( addr -- )
  /wave 0 DO
    I 128 -
    OVER I + C!
  LOOP DROP ;

\ gen-tri — triangle: ramp up then down.
: gen-tri  ( addr -- )
  /wave 0 DO
    I 128 < IF I 2* 128 - ELSE 382 I 2* - THEN
    OVER I + C!
  LOOP DROP ;

\ gen-sine — two-lobe parabolic sine approximation (no sin dependency).
\ Positive lobe peaks +124 at index 64; negative lobe -124 at index 192.
: gen-sine  ( addr -- )
  /wave 0 DO
    I 128 < IF  I  64 - DUP * 33 /MOD SWAP DROP  124 SWAP -
            ELSE I 192 - DUP * 33 /MOD SWAP DROP  124 -
            THEN
    OVER I + C!
  LOOP DROP ;

\ gen-sine-hq — a TRUE sine built from the kernel's 91-byte quarter-wave
\ (sin-data, sin 0..90 deg scaled 0..127) by quadrant symmetry. Truer than
\ gen-sine's parabola and reuses the kernel's sine bytes; requires the kernel
\ sin-data primitive. Index I -> quadrant (bits 6,7) + position (bits 0..5);
\ odd quadrants mirror the ramp, the upper half negates.
VARIABLE _hq-dst
VARIABLE _hq-src
: gen-sine-hq  ( addr -- )
  _hq-dst !  sin-data _hq-src !
  /wave 0 DO
    I 63 AND                      \ position within quadrant (0..63)
    I 64 AND IF 63 SWAP - THEN    \ odd quadrant: mirror the ramp
    90 * 6 RSHIFT                 \ -> quarter-table index (0..88)
    _hq-src @ + C@                \ quarter sine value (0..~126)
    I 128 AND IF NEGATE THEN      \ lower half: negate
    _hq-dst @ I + C!              \ store signed deviation
  LOOP ;
