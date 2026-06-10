\ sound-async.fs — full non-blocking sound-FX menu (lib/async-sound.fs)
\
\ Mirrors the src/sound (synchronous) demo's menu — square/saw/triangle/sine
\ tones, sweep, noise, beep, zap, boom, chirp, dock, hit — but every effect
\ is NON-BLOCKING: a keypress queues an effect, the main loop plays it out
\ across frames, and the marker keeps animating + the keyboard stays live the
\ whole time. Multi-step effects (chirp, dock) use a small note queue; rests
\ (silence between notes) are queued as silent notes via snd-rest semantics.

INCLUDE ../../lib/screen.fs
INCLUDE ../../lib/wavetable.fs
INCLUDE ../../lib/async-sound.fs
INCLUDE ../../lib/bye.fs

VARIABLE mcol            \ marker column 0..31
VARIABLE running         \ main-loop flag
VARIABLE noise-fr        \ remaining noise chunks (boom/noise), 0 = none
VARIABLE noise-len       \ initial chunk count (for amplitude decay); 0 = no decay
VARIABLE noise-n         \ noise samples emitted per chunk

\ ── Note queue: up to 6 steps, each 5 cells (wave freq amp frames slide) ──
DATA[PY nq
bytes(60)
]DATA
VARIABLE nq-head
VARIABLE nq-tail
VARIABLE nq-slot         \ scratch slot address (avoids return-stack juggling)

: nq-reset  ( -- )  0 nq-head !  0 nq-tail ! ;
: seq0      ( -- )  snd-stop  nq-reset ;     \ interrupt current sound, start fresh

: nq-add  ( wave freq amp frames slide -- )
  nq  nq-tail @ 10 *  +  nq-slot !
  nq-slot @ 8 + !        \ slide
  nq-slot @ 6 + !        \ frames
  nq-slot @ 4 + !        \ amp
  nq-slot @ 2 + !        \ freq
  nq-slot @     !        \ wave
  nq-tail @ 1 + nq-tail ! ;

\ When the voice goes idle, start the next queued note (if any).
: nq-play  ( -- )
  snd-playing? IF EXIT THEN
  nq-head @ nq-tail @ < 0= IF EXIT THEN
  nq  nq-head @ 10 *  +  nq-slot !
  nq-slot @     @ snd-waveform
  nq-slot @ 2 + @  nq-slot @ 4 + @  nq-slot @ 6 + @  snd-note
  nq-slot @ 8 + @ snd-slide!
  nq-head @ 1 + nq-head ! ;

\ ── Effects ───────────────────────────────────────────────────────────────
\ single-note effect: ( wave freq amp frames slide -- )
: 1tone  ( wave freq amp frames slide -- )  seq0 nq-add ;

\ ── Wavetable bank, generated at runtime into free hi RAM ────────────────
\ Built once by make-waves; the tables are NOT in the program binary. A 64K
\ app would point wt-base at $9200 (free all-RAM heap); here we use a fixed
\ free address in the ROM-mode heap to mirror that.
$7000 CONSTANT wt-base
VARIABLE wt-sine    VARIABLE wt-square
VARIABLE wt-saw     VARIABLE wt-tri

: make-waves  ( -- )
  wt-base             DUP gen-sine   wt-sine !
  wt-base /wave +     DUP gen-square wt-square !
  wt-base /wave 2* +  DUP gen-saw    wt-saw !
  wt-base /wave 3 * + DUP gen-tri    wt-tri ! ;

: sq1   ( -- )  wt-square @ 880 0 14  0 1tone ;
: sq2   ( -- )  wt-square @ 440 0 14  0 1tone ;
: sq3   ( -- )  wt-square @ 220 0 14  0 1tone ;
: sq4   ( -- )  wt-square @ 110 0 14  0 1tone ;
: swp   ( -- )  wt-sine @  1500 0 12 -600 1tone ;    \ descending sweep
: saw1  ( -- )  wt-saw @    220 0 16  0 1tone ;
: saw2  ( -- )  wt-saw @    440 0 16  0 1tone ;
: tri8  ( -- )  wt-tri @    330 0 16  0 1tone ;
: sin9  ( -- )  wt-sine @   440 0 18  0 1tone ;
: beep  ( -- )  wt-square @ 700 0  5  0 1tone ;      \ short blip
: zap   ( -- )  wt-saw @   2800 0  6 -1700 1tone ;   \ maser: high saw, fast fall
: hit   ( -- )  wt-square @ 400 0  5 -300 1tone ;
: rise  ( -- )  wt-sine @   300 0 14  500 1tone ;

\ Two-tone dock chime.
: dock  ( -- )
  seq0
  wt-square @ 600 0 10 0 nq-add
  wt-square @ 300 0 12 0 nq-add ;

\ Three sine blips with silent rests between (rest = freq 0, amp 8).
: chirp  ( -- )
  seq0
  wt-sine @ 1400 0 2 0 nq-add
  wt-sine @    0 8 2 0 nq-add
  wt-sine @ 1400 0 2 0 nq-add
  wt-sine @    0 8 2 0 nq-add
  wt-sine @ 1400 0 2 0 nq-add ;

\ Noise / boom: chunked noise so the marker keeps moving.
\ noise = bright hiss, steady level.  boom = low rumble that decays away.
: noise  ( -- )  seq0   1 snd-noise-div !  0 snd-amp !
                 0 noise-len !  240 noise-n !  20 noise-fr ! ;
: boom   ( -- )  seq0  12 snd-noise-div !  0 snd-amp !
                 56 noise-len !   20 noise-n !  56 noise-fr ! ;

: banner  ( -- )
  ." ASYNC SOUND FX (NON-BLOCKING)" CR CR
  ." 1-4 SQUARE   5 SWEEP" CR
  ." 6 7 SAW  8 TRI  9 SINE" CR
  ." 0 NOISE" CR
  ." B BEEP  Z ZAP  X BOOM" CR
  ." C CHIRP  D DOCK  H HIT" CR CR
  ." BREAK   QUIT" CR ;

: marker  ( -- )
  mcol @ 14 AT  32 EMIT
  mcol @ 1 + 31 AND mcol !
  mcol @ 14 AT  CHAR * EMIT ;

: dispatch  ( ascii -- )
  DUP CHAR 1 = IF DROP sq1   ELSE
  DUP CHAR 2 = IF DROP sq2   ELSE
  DUP CHAR 3 = IF DROP sq3   ELSE
  DUP CHAR 4 = IF DROP sq4   ELSE
  DUP CHAR 5 = IF DROP swp   ELSE
  DUP CHAR 6 = IF DROP saw1  ELSE
  DUP CHAR 7 = IF DROP saw2  ELSE
  DUP CHAR 8 = IF DROP tri8  ELSE
  DUP CHAR 9 = IF DROP sin9  ELSE
  DUP CHAR 0 = IF DROP noise ELSE
  DUP CHAR B = IF DROP beep  ELSE
  DUP CHAR Z = IF DROP zap   ELSE
  DUP CHAR X = IF DROP boom  ELSE
  DUP CHAR C = IF DROP chirp ELSE
  DUP CHAR D = IF DROP dock  ELSE
  DUP CHAR H = IF DROP hit   ELSE
  DUP 3     = IF DROP 0 running ! ELSE
  DROP
  THEN THEN THEN THEN THEN THEN THEN THEN THEN
  THEN THEN THEN THEN THEN THEN THEN THEN ;

: main  ( -- )
  cls-black
  banner
  snd-async-init
  make-waves                  \ generate the wavetables into hi RAM
  wt-sine @ snd-waveform      \ default waveform
  nq-reset  0 noise-fr !  0 mcol !  1 running !
  BEGIN
    marker
    key? dispatch
    noise-fr @ 0 > IF
      noise-len @ 0 > IF                       \ amplitude decay (boom)
        noise-len @ noise-fr @ -  3 rshift  snd-amp !
      THEN
      noise-n @ snd-noise-fill
      noise-fr @ 1 - noise-fr !
    ELSE
      nq-play
      snd-playing? IF 240 snd-fill ELSE vsync THEN
      snd-frame
    THEN
    running @ 0=
  UNTIL
  snd-stop
  exit-basic ;

main
