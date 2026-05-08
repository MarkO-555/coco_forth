\ trig.fs — Sine/cosine lookup table for angle-based operations
\
\ Provides: init-sin, sin, cos, angle-dx, angle-dy
\ Requires: kernel primitives C@, *, +, -, /MOD, NEGATE, RSHIFT, CMOVE,
\           sin-data, DUP, DROP, SWAP, OVER, @, !, <, >, 0=,
\           IF, ELSE, THEN
\
\ 91-entry sine table covering 0-90 degrees.  Values are 7-bit
\ fixed-point: 0 = 0.000, 127 = 1.000 (actually 0.992).
\ Cosine and full 0-360 range derived by quadrant mirroring.
\
\ Usage:
\   init-sin                   \ build the lookup table once
\   45 sin                     \ ( -- 90 )  ~0.707 * 127
\   30 cos                     \ ( -- 110 ) ~0.866 * 127
\   45 64 angle-dx             \ ( -- 45 )  x displacement for 64-pixel line
\   45 64 angle-dy             \ ( -- -45 ) y displacement (negative = up)

\ ── Sine table (91 bytes at $86CC) ──────────────────────────────────────
\ sin(0)=0 through sin(90)=127, in 1-degree steps.
\ Values = round(sin(deg) * 127).

\ Sine table location is set by the kernel build (trig-base from fc.py:
\ $86CC in all-RAM mode, $7800 in ROM mode).  Used directly below since
\ fc.py's CONSTANT requires a literal value.

\ Sin table is now stored as raw FCB bytes in the kernel; init-sin
\ just CMOVEs them into place at trig-base.  Saves ~25,000 cycles of
\ boot-time work and ~500 bytes of app binary vs the per-byte tb form.

: init-sin  ( -- )
  sin-data trig-base 91 CMOVE ;

\ ── Signed divide by 128 ────────────────────────────────────────────────
\ RSHIFT is logical (unsigned) on the 6809, so we handle sign manually.

: _s/128  ( n -- n/128 )
  DUP 0 < IF
    NEGATE 7 RSHIFT NEGATE
  ELSE
    7 RSHIFT
  THEN ;

\ ── sin ( angle -- value ) ───────────────────────────────────────────────
\ Return sine of angle (0-360) as signed fixed-point (-127..+127).

VARIABLE _sa-tmp

: sin  ( angle -- value )
  \ Normalize to 0-359 (handles any positive angle)
  360 /MOD DROP
  _sa-tmp !

  _sa-tmp @ 180 < IF
    \ Quadrant 1 or 2: sin is positive
    _sa-tmp @ 90 > IF
      180 _sa-tmp @ - trig-base + C@
    ELSE
      _sa-tmp @ trig-base + C@
    THEN
  ELSE
    \ Quadrant 3 or 4: sin is negative
    _sa-tmp @ 270 > IF
      360 _sa-tmp @ - trig-base + C@ NEGATE
    ELSE
      _sa-tmp @ 180 - trig-base + C@ NEGATE
    THEN
  THEN ;

\ ── cos ( angle -- value ) ───────────────────────────────────────────────

: cos  ( angle -- value )  90 + sin ;

\ ── angle-dx ( angle length -- dx ) ─────────────────────────────────────
\ X displacement: dx = length * cos(angle) / 128
\ Angle 0 = right, 90 = up, 180 = left, 270 = down.

VARIABLE _ad-len

: angle-dx  ( angle length -- dx )
  _ad-len !
  cos _ad-len @ * _s/128 ;

\ ── angle-dy ( angle length -- dy ) ─────────────────────────────────────
\ Y displacement: dy = -length * sin(angle) / 128
\ Negative because screen Y increases downward but angle 90 = up.

: angle-dy  ( angle length -- dy )
  _ad-len !
  sin NEGATE _ad-len @ * _s/128 ;
