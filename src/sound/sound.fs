\ sound.fs — Interactive DAC sound demo
\
\ Press 0..9 to play different tones and effects.  BREAK returns to
\ BASIC.  Works in both ROM-mode and all-RAM kernel builds — see
\ SOUND.md for the historical context.

INCLUDE ../../forth/lib/screen.fs
INCLUDE ../../forth/lib/bye.fs
INCLUDE ../../forth/lib/datawrite.fs
INCLUDE ../../forth/lib/trig.fs
INCLUDE ../../forth/lib/sound.fs

\ Smooth ascending sweep: low frequency → high frequency.
\ Pitch parameter (= inter-toggle delay) sweeps from 200 down to 20
\ over many small steps; lower pitch = higher audio frequency.
: snd-sweep  ( -- )
  200 20 DO  220 I -  1 snd-tone  3 +LOOP ;

: banner  ( -- )
  ." COCO SOUND DEMO" CR CR
  ." 1 2 3 4  SQUARE WAVES" CR
  ." 5        SWEEP" CR
  ." 6 7      SAWTOOTH" CR
  ." 8        TRIANGLE" CR
  ." 9        SINE" CR
  ." 0        NOISE" CR CR
  ." B  BEEP    Z  ZAP" CR
  ." X  BOOM    C  CHIRP" CR
  ." D  DOCK    H  HIT" CR CR
  ." BREAK    QUIT" CR ;

\ Dispatch on the keystroke.  Nested IF/ELSE because fc.py
\ miscompiles EXIT inside IF.
\
\ 1..4 : square waves (snd-tone) at increasing pitch
\ 5    : descending sweep
\ 6,7  : sawtooth (low, high)
\ 8    : triangle
\ 9    : sine
\ 0    : white-noise burst
: dispatch  ( ascii -- )
  DUP CHAR 1 = IF DROP 600  100 snd-tone
  ELSE DUP CHAR 2 = IF DROP 300  300 snd-tone
  ELSE DUP CHAR 3 = IF DROP 100  600 snd-tone
  ELSE DUP CHAR 4 = IF DROP  50 1200 snd-tone
  ELSE DUP CHAR 5 = IF DROP snd-sweep
  ELSE DUP CHAR 6 = IF DROP   4 30 800 snd-saw
  ELSE DUP CHAR 7 = IF DROP  16 30 800 snd-saw
  ELSE DUP CHAR 8 = IF DROP    30  80 snd-tri
  ELSE DUP CHAR 9 = IF DROP     2 120 snd-sin
  ELSE DUP CHAR 0 = IF DROP 1 400 snd-noise     \ very fast rate → high hiss
  ELSE DUP CHAR B = IF DROP snd-beep
  ELSE DUP CHAR Z = IF DROP snd-zap
  ELSE DUP CHAR X = IF DROP snd-boom
  ELSE DUP CHAR C = IF DROP snd-chirp
  ELSE DUP CHAR D = IF DROP snd-dock
  ELSE DUP CHAR H = IF DROP snd-hit
  ELSE DUP 3 = IF exit-basic
  ELSE DROP
  THEN THEN THEN THEN THEN THEN THEN THEN THEN THEN
  THEN THEN THEN THEN THEN THEN THEN ;

: main  ( -- )
  init-sin                          \ snd-sin needs the table populated
  cls-black
  banner
  BEGIN  KEY dispatch  0 UNTIL ;

main
