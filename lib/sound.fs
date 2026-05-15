\ sound.fs — DAC sound library for the CoCo
\
\ Provides: snd-init, snd-tone, snd-saw, snd-tri, snd-sin, snd-noise,
\           snd-pause, snd-beep, snd-zap, snd-boom, snd-chirp, snd-dock,
\           snd-hit
\
\ Requires: kernel primitives rng + kvar-seed (for snd-noise),
\           lib/trig.fs init-sin populating trig-base (for snd-sin)
\
\ Plays square-wave tones and white-noise bursts through the 6-bit DAC
\ at $FF20.  PIA initialization values come from Dungeons of Daggorath's
\ ONCE.ASM (1982) — storing literal control-register values rather than
\ masking individual bits makes the result deterministic regardless of
\ prior PIA state, so this works in both ROM-mode and all-RAM kernels.

\ ── Internal: PIA setup ─────────────────────────────────────────────────
\ snd-init configures the audio path once.  snd-tone and snd-noise call
\ it on every invocation; the cost is 5 byte-stores so it's cheap.
CODE snd-init  \ ( -- )
        LDB     #$34
        STB     $FF01           ; PIA0 CR-A: SEL1=0, data reg, no IRQ
        STB     $FF03           ; PIA0 CR-B: SEL2=0, data reg, no IRQ
                                ; → audio mux = 00 = 6-bit DAC
        STB     $FF21           ; PIA1 CR-A: data reg, no IRQ
        ; Make PIA1 PB1 (single-bit sound output) an output.
        ; Drop CR-B bit 2 to switch $FF22 to its DDR, set bit 1, restore.
        LDB     #$30
        STB     $FF23           ; PIA1 CR-B: DDR access mode
        LDB     $FF22
        ORB     #$02            ; bit 1 = output
        STB     $FF22           ; write DDR
        LDB     #$3C
        STB     $FF23           ; PIA1 CR-B: data mode + Six Bit Sound enable (CB2)
        ;NEXT
;CODE

\ ── snd-tone ( pitch duration -- ) ─────────────────────────────────────
\ Square-wave tone via DAC toggling.
\   pitch    16-bit half-cycle delay (lower = higher frequency)
\   duration 16-bit toggle-cycle count (one cycle = high+low pair)
CODE snd-tone
        LDD     2,U             ; D = pitch (NOS)
        PSHS    D               ; stash on return stack
        LDY     ,U              ; Y = duration counter (TOS)
        LEAU    4,U             ; pop both args
        LDB     #$34
        STB     $FF01
        STB     $FF03
        STB     $FF21
        LDB     #$3C
        STB     $FF23
SNT_LP  LDB     #$FC
        STB     $FF20           ; DAC high
        LDD     ,S
SNT_D1  SUBD    #1
        BNE     SNT_D1
        CLR     $FF20           ; DAC low
        LDD     ,S
SNT_D2  SUBD    #1
        BNE     SNT_D2
        LEAY    -1,Y
        BNE     SNT_LP
        LEAS    2,S
        ;NEXT
;CODE

VARIABLE _noise-delay   \ shared by snd-noise and snd-noise1

\ ── 1-bit sound primitives ────────────────────────────────────────────
\ The CoCo has a *second* audio path independent of the 6-bit DAC: a
\ single bit at PIA1 PB bit 1 ($FF22 bit 1), mixed into the same audio
\ bus.  Toggling it produces a square click; toggling at audio rates
\ produces a square tone.  Cost per toggle is one EOR + STB — cheaper
\ than the DAC's two writes per half-cycle.
\
\ $FF22 also carries VDG mode bits (7-3) and RS-232 IN (bit 0), so all
\ writes here are read-modify-write (LDB/EORB #$02/STB) to preserve
\ those bits.  A constant store would scramble the screen mode.

\ snd-tone1 — 1-bit square tone via $FF22 bit 1; pitch/duration like snd-tone.
CODE snd-tone1  \ ( pitch duration -- )
        LDD     2,U             ; D = pitch
        PSHS    D
        LDY     ,U              ; Y = duration
        LEAU    4,U
        LDB     #$34
        STB     $FF01
        STB     $FF03
        STB     $FF21
        LDB     #$30
        STB     $FF23           ; DDR mode
        LDB     $FF22
        ORB     #$02
        STB     $FF22           ; PB1 = output
        LDB     #$3C
        STB     $FF23           ; data mode + sound enable
SN1_LP  LDB     $FF22
        EORB    #$02
        STB     $FF22           ; toggle 1-bit output
        LDD     ,S
SN1_D1  SUBD    #1
        BNE     SN1_D1
        LDB     $FF22
        EORB    #$02
        STB     $FF22           ; toggle back
        LDD     ,S
SN1_D2  SUBD    #1
        BNE     SN1_D2
        LEAY    -1,Y
        BNE     SN1_LP
        LEAS    2,S
        ;NEXT
;CODE

\ snd-click1 — Single 1-bit toggle; the primitive percussive element.
CODE snd-click1  \ ( -- )
        LDB     #$34
        STB     $FF01
        STB     $FF03
        STB     $FF21
        LDB     #$30
        STB     $FF23           ; DDR mode
        LDB     $FF22
        ORB     #$02
        STB     $FF22           ; PB1 = output
        LDB     #$3C
        STB     $FF23           ; data mode + sound enable
        LDB     $FF22
        EORB    #$02
        STB     $FF22
        ;NEXT
;CODE

\ _snd-toggle1 — Internal: single toggle of $FF22 bit 1, no PIA init.
\ Used by snd-noise1 inside a tight Forth loop where snd-init has
\ already run.
CODE _snd-toggle1  \ ( -- )
        LDB     $FF22
        EORB    #$02
        STB     $FF22
        ;NEXT
;CODE

\ snd-noise1 — 1-bit white-noise burst; delay/duration like snd-noise.
\ Each iteration consults the RNG and toggles the bit when the random
\ byte's high bit is set, so the toggle pattern is irregular — gives
\ noise rather than a tone.  delay sets inter-sample spacing.
: snd-noise1  ( delay duration -- )
  snd-init
  SWAP _noise-delay !
  0 DO
    rng kvar-seed @ $80 AND IF _snd-toggle1 THEN
    _noise-delay @ 0 DO LOOP
  LOOP ;

\ ── snd-saw ( step pitch duration -- ) ────────────────────────────────
\ Sawtooth-style waveform.  Each sample, the DAC value is incremented
\ (or decremented) by `step` modulo 256 — the wraparound is the saw's
\ vertical edge.  step=4 gives a 64-sample period (lowest pitch),
\ larger steps give shorter periods (higher pitch).  Negative steps
\ (e.g. $FC) ramp downward.  pitch and duration as in snd-tone.
CODE snd-saw
        LDA     5,U             ; A = step (low byte of NNS)
        STA     ,-S             ; push step (1 byte) onto return stack
        LDD     2,U             ; D = pitch (NOS)
        PSHS    D
        LDY     ,U              ; Y = duration (TOS)
        LEAU    6,U             ; pop 3 args
        LDB     #$34
        STB     $FF01
        STB     $FF03
        STB     $FF21
        LDB     #$3C
        STB     $FF23
        CLRB                    ; B = current sample
SAW_LP  STB     $FF20
        ADDB    2,S             ; B += step (step is at 2,S past pitch)
        PSHS    B               ; save sample so LDD ,S can use D
        LDD     1,S             ; D = pitch (offset shifted by the PSHS B)
SAW_DL  SUBD    #1
        BNE     SAW_DL
        PULS    B               ; restore sample
        LEAY    -1,Y
        BNE     SAW_LP
        LEAS    3,S             ; drop pitch (2) + step (1)
        ;NEXT
;CODE

\ Triangle: ramp up then ramp down, each for `duration` samples.  Total
\ period = 2 × duration × per-sample-time.
: snd-tri  ( pitch duration -- )
  2DUP   4 ROT ROT snd-saw       \ ramp up   ($00 → $FC)
        -4 ROT ROT snd-saw ;     \ ramp down ($FC → $00)

\ ── snd-sin ( pitch duration -- ) ─────────────────────────────────────
\ Walks the kernel's 91-byte sin table at trig-base, treating it as a
\ half-cycle: forward 0..90 then backward 89..1.  Output is centred at
\ $80 (DAC midpoint) with the table value scaled to roughly half of
\ the 6-bit range.  Caller must have run init-sin first.
CODE snd-sin
        PSHS    X               ; save IP — we use X to walk the table
        LDD     2,U             ; pitch
        PSHS    D
        LDY     ,U              ; duration (full half-waves to play)
        LEAU    4,U
        LDB     #$34
        STB     $FF01
        STB     $FF03
        STB     $FF21
        LDB     #$3C
        STB     $FF23
SIN_HALF
        ;; forward: indices 0, 4, 8, ..., 88 (23 samples).  Stride of
        ;; 4 keeps the per-half-wave sample count down so the audible
        ;; frequency lands in the hundreds of Hz instead of single
        ;; digits.
        LDX     #TRIG_BASE
        LDB     #23
SIN_FW  LDA     ,X
        LEAX    4,X
        ;; output = $80 + (sin/2), masked to 6 bits
        LSRA
        ADDA    #$80
        ANDA    #$FC
        STA     $FF20
        DECB
        BEQ     SIN_BACK_INIT
        PSHS    B               ; save remaining count (A is overwritten next iter)
        LDD     1,S             ; pitch (offset 1 past B push)
SIN_D1  SUBD    #1
        BNE     SIN_D1
        PULS    B
        BRA     SIN_FW
SIN_BACK_INIT
        ;; backward: indices 84, 80, ..., 4 (21 samples).  X currently
        ;; points 4 past index 88 (= TRIG_BASE+92); back up to index 84.
        LEAX    -8,X            ; X = TRIG_BASE+84
        LDB     #21
SIN_BK  LDA     ,X
        LEAX    -4,X
        LSRA
        ADDA    #$80
        ANDA    #$FC
        STA     $FF20
        DECB
        BEQ     SIN_HALF_DONE
        PSHS    B
        LDD     1,S
SIN_D2  SUBD    #1
        BNE     SIN_D2
        PULS    B
        BRA     SIN_BK
SIN_HALF_DONE
        LEAY    -1,Y
        BNE     SIN_HALF
        LEAS    2,S             ; drop pitch
        PULS    X               ; restore IP
        ;NEXT
;CODE

\ ── snd-noise ( delay duration -- ) ────────────────────────────────────
\ White-noise burst by writing random DAC values.
\   delay    inter-sample wait (lower = higher pitched hiss)
\   duration sample count
: snd-noise  ( delay duration -- )
  snd-init
  SWAP _noise-delay !
  0 DO
    rng kvar-seed @ $FC AND $FF20 C!
    _noise-delay @ 0 DO LOOP
  LOOP ;

\ ── Convenience effects ────────────────────────────────────────────────
\ Tuned for the 16-bit pitch/duration interface.  Adjust freely.
: snd-beep   ( -- )  300  30 snd-tone ;
\ Smooth descending laser zap.  Steps the pitch from 10 to ~120 in
\ small increments, playing a very short burst at each level.  Linear
\ pitch increment gives an exponential frequency drop (musical).
\ snd-zap — Linear pitch sweep 10..120 with short bursts; sounds like a laser zap.
: snd-zap    ( -- )
  120 10 DO  I 1 snd-tone  3 +LOOP ;
: snd-boom   ( -- )  60 500 snd-noise ;     \ slow rate → low rumble
\ Brief silent pause — DAC stays at 0 between snd-tone calls because
\ snd-tone's last write is CLR $FF20.  Caller passes an iteration
\ count; ~6 cycles per iter at 0.89 MHz = ~6.7us each.
\ snd-pause — Brief silent gap; busy-loops `count` iterations (~6 cy each at 0.89 MHz ≈ 6.7µs) with the DAC held at 0 by the previous snd-tone call.
: snd-pause  ( count -- )  0 DO LOOP ;

\ Three identical high blips in quick succession — like a bird chirp.
: snd-chirp  ( -- )
   60 7 snd-tone  900 snd-pause
   60 7 snd-tone  900 snd-pause
   60 7 snd-tone ;
: snd-dock   ( -- )  600  20 snd-tone  300  20 snd-tone ;
: snd-hit    ( -- )  150  30 snd-tone ;
