\ sound-async.fs — non-blocking sound-FX demo (lib/async-sound.fs)
\
\ Models the tonal effects from the synchronous src/sound demo (zap, beep,
\ hit) using the async engine — but here they DON'T block: a keypress starts
\ an effect, and the marker keeps animating + the keyboard stays live while
\ the effect plays out across frames. The maser/zap is the headline: a tone
\ that starts high and slides down fast, built from snd-note + snd-slide!.

INCLUDE ../../lib/screen.fs
INCLUDE ../../lib/async-sound.fs
INCLUDE ../../lib/bye.fs

VARIABLE mcol            \ marker column 0..31
VARIABLE running         \ main-loop flag

: banner  ( -- )
  ." ASYNC SOUND FX DEMO" CR CR
  ." Z  MASER (ZAP)" CR
  ." B  BEEP" CR
  ." H  HIT" CR
  ." R  RISE" CR CR
  ." MARKER KEEPS MOVING AND" CR
  ." KEYS STAY LIVE WHILE FX PLAY." CR CR
  ." BREAK    QUIT" CR ;

\ Move a single '*' marker along row 14: erase here, advance, redraw.
: marker  ( -- )
  mcol @ 14 AT  32 EMIT
  mcol @ 1 + 31 AND mcol !
  mcol @ 14 AT  CHAR * EMIT ;

\ ── Async effects: each one starts a voice and returns immediately ────────
\ maser/zap: ~1800 Hz dropping to ~290 Hz over 9 frames (snd-frame self-slides)
: maser  ( -- )  1800 0 9  snd-note  -700 snd-slide! ;
\ beep: steady 700 Hz for 16 frames
: beep   ( -- )   700 0 16 snd-note ;
\ hit: quick downward tick, 400 -> ~210 Hz over 5 frames
: hit    ( -- )   400 0 5  snd-note  -300 snd-slide! ;
\ rise: ascending 300 Hz -> ~2000 Hz over 14 frames
: rise   ( -- )   300 0 14 snd-note   500 snd-slide! ;

: dispatch  ( ascii -- )
  DUP CHAR Z = IF DROP maser     ELSE
  DUP CHAR B = IF DROP beep      ELSE
  DUP CHAR H = IF DROP hit       ELSE
  DUP CHAR R = IF DROP rise      ELSE
  DUP 3     = IF DROP 0 running ! ELSE
  DROP
  THEN THEN THEN THEN THEN ;

: main  ( -- )
  cls-black
  banner
  snd-async-init
  0 mcol !  1 running !
  BEGIN
    marker
    key? dispatch                     \ non-blocking: start an effect on a key
    snd-playing? IF 240 snd-fill       \ while a note plays, emit a tone chunk
                 ELSE vsync THEN        \ idle: pace the marker at 60 Hz
    snd-frame                          \ age the note + apply its pitch slide
    running @ 0=
  UNTIL
  snd-stop
  exit-basic ;

main
