\ sound.fs — Interactive DAC tone demo
\
\ Press 1..5 to play different tones.  BREAK returns to BASIC.
\ Works in both ROM-mode and all-RAM kernel builds — see SOUND.md
\ for the historical context.

INCLUDE ../../forth/lib/screen.fs
INCLUDE ../../forth/lib/bye.fs
INCLUDE ../../forth/lib/sound.fs

\ Descending sweep: short burst at each pitch from high → low.
: snd-sweep  ( -- )
  600 30 snd-tone
  450 30 snd-tone
  300 30 snd-tone
  200 30 snd-tone
  120 30 snd-tone
   80 30 snd-tone
   50 30 snd-tone ;

: banner  ( -- )
  CHAR S EMIT CHAR O EMIT CHAR U EMIT CHAR N EMIT CHAR D EMIT CR
  CHAR 1 EMIT CHAR - EMIT CHAR 5 EMIT 32 EMIT
  CHAR T EMIT CHAR O EMIT CHAR N EMIT CHAR E EMIT CHAR S EMIT CR
  CHAR B EMIT CHAR R EMIT CHAR E EMIT CHAR A EMIT CHAR K EMIT 32 EMIT
  CHAR Q EMIT CHAR U EMIT CHAR I EMIT CHAR T EMIT CR ;

\ Dispatch on the keystroke: 1..5 → distinct tones, BREAK ($03) → exit.
\ Nested IF/ELSE because fc.py miscompiles EXIT inside IF.
: dispatch  ( ascii -- )
  DUP CHAR 1 = IF DROP 600 300 snd-tone
  ELSE DUP CHAR 2 = IF DROP 300 300 snd-tone
  ELSE DUP CHAR 3 = IF DROP 100 600 snd-tone
  ELSE DUP CHAR 4 = IF DROP  50 1200 snd-tone
  ELSE DUP CHAR 5 = IF DROP snd-sweep
  ELSE DUP 3 = IF exit-basic
  ELSE DROP THEN THEN THEN THEN THEN THEN ;

: main  ( -- )
  banner
  BEGIN  KEY dispatch  0 UNTIL ;

main
