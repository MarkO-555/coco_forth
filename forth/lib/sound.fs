\ sound.fs — DAC sound library for the CoCo
\
\ Provides: snd-init, snd-tone, snd-noise, snd-beep, snd-zap, snd-boom,
\           snd-chirp, snd-dock, snd-hit
\
\ Requires: kernel primitives rng (for snd-noise)
\
\ Plays square-wave tones and white-noise bursts through the 6-bit DAC
\ at $FF20.  PIA initialization values come from Dungeons of Daggorath's
\ ONCE.ASM (1982) — storing literal control-register values rather than
\ masking individual bits makes the result deterministic regardless of
\ prior PIA state, so this works in both ROM-mode and all-RAM kernels.

\ ── Internal: PIA setup ─────────────────────────────────────────────────
\ snd-init configures the audio path once.  snd-tone and snd-noise call
\ it on every invocation; the cost is 5 byte-stores so it's cheap.
CODE snd-init
        LDB     #$34
        STB     $FF01           ; PIA0 CR-A: SEL1=0, data reg, no IRQ
        STB     $FF03           ; PIA0 CR-B: SEL2=0, data reg, no IRQ
                                ; → audio mux = 00 = 6-bit DAC
        STB     $FF21           ; PIA1 CR-A: data reg, no IRQ
        LDB     #$3C
        STB     $FF23           ; PIA1 CR-B: Six Bit Sounds ON
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

\ ── snd-noise ( duration -- ) ──────────────────────────────────────────
\ White-noise burst by writing random DAC values.  duration counts
\ samples; each sample = one rng + one $FF20 write + a small fixed delay
\ to keep the rate audible (otherwise it's above the audio range).
: snd-noise  ( duration -- )
  snd-init
  0 DO
    rng kvar-seed @ $FC AND $FF20 C!
    \ short fixed delay so we don't outrun the audio sample rate
    32 0 DO LOOP
  LOOP ;

\ ── Convenience effects ────────────────────────────────────────────────
\ Tuned for the 16-bit pitch/duration interface.  Adjust freely.
: snd-beep   ( -- )  300  60 snd-tone ;
: snd-zap    ( -- )   80 100 snd-tone  200 100 snd-tone  500 100 snd-tone ;
: snd-boom   ( -- )  500 snd-noise ;
: snd-chirp  ( -- )  500 60 snd-tone  300 60 snd-tone  100 60 snd-tone ;
: snd-dock   ( -- )  600  40 snd-tone  300  40 snd-tone ;
: snd-hit    ( -- )  150  30 snd-tone ;
