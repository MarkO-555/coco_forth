\ sound1bit.fs — Interactive 1-bit sound demo
\
\ Exercises the CoCo's *second* audio path: a single bit at $FF22 bit 1,
\ independent of the 6-bit DAC.  Press 1..8 for different effects, BREAK
\ to return to BASIC.

INCLUDE ../../lib/screen.fs
INCLUDE ../../lib/bye.fs
INCLUDE ../../lib/sound.fs

\ Ascending pitch sweep using the 1-bit output.  Pitch parameter is
\ inter-toggle delay; lower delay = higher audible frequency.
: snd-sweep1  ( -- )
  200 20 DO  220 I -  1 snd-tone1  3 +LOOP ;

\ Click train: a burst of single 1-bit toggles spaced by snd-pause.
\ Sounds like a buzzy click roll — pure transient, no sustained tone.
: snd-clicks  ( -- )
  40 0 DO  snd-click1  120 snd-pause  LOOP ;

\ A/B comparison: same pitch played first on the 6-bit DAC, then on
\ the 1-bit output, back-to-back.  Listen for the difference in timbre
\ — DAC is a clean 2-level square, 1-bit is also square but routed
\ through a different mixer path, so the two can sound subtly distinct
\ (and on some hardware revisions, quite different in level).
: snd-ab  ( -- )
  300 200 snd-tone        \ DAC
  300 snd-pause
  300 200 snd-tone1 ;     \ 1-bit

: banner  ( -- )
  ." COCO 1-BIT SOUND DEMO" CR CR
  ." 1 2 3 4  SQUARE TONES" CR
  ." 5        SWEEP" CR
  ." 6        CLICK TRAIN" CR
  ." 7        NOISE" CR
  ." 8        A/B: DAC vs 1-BIT" CR CR
  ." BREAK    QUIT" CR ;

: dispatch  ( ascii -- )
  DUP CHAR 1 = IF DROP 600  100 snd-tone1
  ELSE DUP CHAR 2 = IF DROP 300  300 snd-tone1
  ELSE DUP CHAR 3 = IF DROP 100  600 snd-tone1
  ELSE DUP CHAR 4 = IF DROP  50 1200 snd-tone1
  ELSE DUP CHAR 5 = IF DROP snd-sweep1
  ELSE DUP CHAR 6 = IF DROP snd-clicks
  ELSE DUP CHAR 7 = IF DROP 1 400 snd-noise1
  ELSE DUP CHAR 8 = IF DROP snd-ab
  ELSE DUP 3 = IF exit-basic
  ELSE DROP
  THEN THEN THEN THEN THEN THEN THEN THEN THEN ;

: main  ( -- )
  snd-init                          \ configure the audio path once
  cls-black
  banner
  BEGIN  KEY dispatch  0 UNTIL ;

main
