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
bytes(72)
]DATA
VARIABLE nq-head
VARIABLE nq-tail
VARIABLE nq-slot         \ scratch slot address (avoids return-stack juggling)

: nq-reset  ( -- )  0 nq-head !  0 nq-tail ! ;
: seq0      ( -- )  snd-stop  nq-reset  0 snd-ringmod! ;   \ start fresh (ring mod off)

: nq-add  ( wave freq amp frames slide env -- )
  nq  nq-tail @ 12 *  +  nq-slot !
  nq-slot @ 10 + !       \ env
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
  nq  nq-head @ 12 *  +  nq-slot !
  nq-slot @ @  DUP 4 < IF snd-shape ELSE snd-waveform THEN  \ <4 = algorithmic mode
  nq-slot @ 2 + @  nq-slot @ 4 + @  nq-slot @ 6 + @  snd-note
  nq-slot @ 8 + @ snd-slide!
  nq-slot @ 10 + @ snd-env!
  nq-head @ 1 + nq-head ! ;

\ ── Effects ───────────────────────────────────────────────────────────────
\ single-note effect: ( wave freq amp frames slide env -- )
: 1tone  ( wave freq amp frames slide env -- )  seq0 nq-add ;

\ Algorithmic-waveform modes (snd-shape) -- computed inline, no table RAM.
1 CONSTANT SAW   2 CONSTANT SQR   3 CONSTANT TRI

\ ── Wavetable bank ───────────────────────────────────────────────────────
\ Only the sine needs a real table now; saw/square/triangle are computed
\ inline by the engine.  Built once into free hi RAM at startup (a 64K app
\ would use $9200) -- 256 bytes instead of the 1KB four-table bank.
$7000 CONSTANT wt-base
VARIABLE wt-sine

: make-waves  ( -- )  wt-base DUP gen-sine-hq wt-sine ! ;

\ ( wave freq amp frames slide env )  env = per-frame attenuation rise (fade-out)
: sq1   ( -- )  SQR 880 0 14  0   0 1tone ;
: sq2   ( -- )  SQR 440 0 14  0   0 1tone ;
: sq3   ( -- )  SQR 220 0 14  0   0 1tone ;
: sq4   ( -- )  SQR 110 0 14  0   0 1tone ;
: swp   ( -- )  wt-sine @ 1500 0 12 -600 0 1tone ;     \ descending sweep (sine)
: saw1  ( -- )  SAW 220 0 16  0   0 1tone ;
: saw2  ( -- )  SAW 440 0 16  0   0 1tone ;
: tri8  ( -- )  TRI 330 0 16  0   0 1tone ;
: sin9  ( -- )  wt-sine @ 440 0 18  0  16 1tone ;      \ sine that fades out (envelope)
: beep  ( -- )  SQR 700 0  5  0   0 1tone ;            \ short blip
: zap   ( -- )  SAW 2800 0  6 -1700 42 1tone ;         \ maser: falls AND fades (envelope)
: hit   ( -- )  SQR 400 0  5 -300 50 1tone ;           \ short, quick fade
: rise  ( -- )  wt-sine @ 300 0 14  500 0 1tone ;

\ ── Ring modulation (square modulator) examples ──────────────────────────
\ Carrier = the oscillator (pitch/slide/amp-env all apply); modulator = a
\ fixed square set by snd-ringmod! (period samples, ~7867/period Hz). 1tone's
\ seq0 clears ring mod first, so we set it after.
: ring  ( -- )  wt-sine @ 440 0 30   0 0 1tone   16 snd-ringmod! ;  \ steady metallic tone
: gong  ( -- )  wt-sine @ 330 0 40   0 6 1tone   24 snd-ringmod! ;  \ ring mod + amp decay = bell
: warp  ( -- )  wt-sine @ 1200 0 24 -140 0 1tone 10 snd-ringmod! ;  \ slide sweeps the sidebands

\ ── Classic game SFX (envelope + queue sequences + bidirectional slide) ──
: kick  ( -- )  wt-sine @ 180 0 7 -90 45 1tone ;   \ drum: pitch drop + fast amp decay
: coin  ( -- )  seq0                               \ two-tone pickup, second fades
  SQR 988  0 4  0 0  nq-add
  SQR 1319 0 12 0 12 nq-add ;
: pwr   ( -- )  seq0                               \ rising major arpeggio (powerup)
  SQR 523  0 3 0 0  nq-add
  SQR 659  0 3 0 0  nq-add
  SQR 784  0 3 0 0  nq-add
  SQR 1047 0 9 0 12 nq-add ;
: siren ( -- )  seq0                               \ wail up then down (two slides)
  wt-sine @ 400 0 12  140 0 nq-add
  wt-sine @ 800 0 12 -140 0 nq-add ;

\ Two-tone dock chime.
: dock  ( -- )
  seq0
  SQR 600 0 10 0 0 nq-add
  SQR 300 0 12 0 0 nq-add ;

\ Three sine blips with silent rests between (rest = freq 0, amp 255 = silent).
: chirp  ( -- )
  seq0
  wt-sine @ 1400   0 2 0 0 nq-add
  wt-sine @    0 255 2 0 0 nq-add
  wt-sine @ 1400   0 2 0 0 nq-add
  wt-sine @    0 255 2 0 0 nq-add
  wt-sine @ 1400   0 2 0 0 nq-add ;

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
  ." C CHIRP  D DOCK  H HIT" CR
  ." R RING  G GONG  W WARP" CR
  ." K KICK M COIN P PWR S SIRN" CR CR
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
  DUP CHAR R = IF DROP ring  ELSE
  DUP CHAR G = IF DROP gong  ELSE
  DUP CHAR W = IF DROP warp  ELSE
  DUP CHAR K = IF DROP kick  ELSE
  DUP CHAR M = IF DROP coin  ELSE
  DUP CHAR P = IF DROP pwr   ELSE
  DUP CHAR S = IF DROP siren ELSE
  DUP 3     = IF DROP 0 running ! ELSE
  DROP
  THEN THEN THEN THEN THEN THEN THEN THEN THEN THEN THEN THEN
  THEN THEN THEN THEN THEN THEN THEN THEN THEN THEN THEN THEN ;

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
      noise-len @ 0 > IF                       \ smooth amplitude decay (boom)
        noise-len @ noise-fr @ -  DUP 2* 2* +  255 MIN  snd-amp !
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
