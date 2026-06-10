\ async-sound.fs — non-blocking (cooperative) DAC sound for the CoCo
\
\ Provides: snd-async-init, snd-note, snd-stop, snd-frame, snd-pitch!,
\           snd-amp!, snd-playing?, snd-poll, snd-fill, freq>inc, snd-wave
\
\ Requires: kernel primitives only (* /mod 2* @ ! c! ...). No kvar, no trig
\           table, no kernel patch — snd-poll reads the HSYNC flag at $FF01
\           and writes the 6-bit DAC at $FF20 directly.
\
\ ── What this is ────────────────────────────────────────────────────────
\ The synchronous lib/sound.fs blocks the caller for the whole duration of
\ a note. This library is the opposite: it never blocks. A voice is a
\ phase-accumulator wavetable oscillator whose state lives in VARIABLEs;
\ the game advances it one sample at a time by calling snd-poll (or a burst
\ of samples with snd-fill) from inside its own main loop. The note keeps
\ playing across frames with no busy-wait.
\
\ ── Why cooperative, not interrupt-driven ───────────────────────────────
\ The only audio-rate interrupt on a stock CoCo 1/2 is HSYNC, and HSYNC is
\ wired to the *IRQ* line, not FIRQ (coco_technical_reference.txt:4775,
\ vdg-modes.md:180-181). An IRQ auto-stacks the full 12-byte machine state,
\ so taking one every scan line costs ~44 cy even on a line you skip —
\ ~11,500 cy/frame of pure entry/exit tax. Polling the HSYNC *flag* dodges
\ that entirely: the PIA latches the flag on each scan line (set in bit 7 of
\ $FF01, cleared by reading $FF00), exactly the mechanism the kernel's
\ WAIT-PAST-ROW already uses. See proposals/SOUND_ENGINE_PROPOSAL.md and
\ proposals/hsync_polling_budget_chart.html.
\
\ ── Driving model ───────────────────────────────────────────────────────
\   per event   : snd-note ( freq amp frames -- )   start a voice
\   per VSYNC   : snd-frame ( -- )                  age the note, auto-stop
\   sub-frame   : snd-poll  ( -- )                  emit one sample if a new
\                                                   HSYNC row has elapsed
\               : snd-fill  ( n -- )                emit n HSYNC-locked
\                                                   samples back-to-back
\ Sprinkle snd-poll at your main loop's phase boundaries so no stretch of
\ work exceeds the sample spacing you want (16 samples/frame = one every
\ ~1,375 cy at R0-high), and/or drop snd-fill into a VSYNC-wait spin for a
\ dense, perfectly-even burst.
\
\ ── Pitch ───────────────────────────────────────────────────────────────
\ freq>inc converts Hz to the per-sample phase increment assuming the full
\ HSYNC sample rate (15,734 Hz, i.e. snd-fill emission). When you instead
\ drive the voice with sparser snd-poll calls the effective rate — and so
\ the audible pitch — scales down proportionally; calibrate per use site.

\ ── Wavetables: generated at runtime, not baked into the binary ──────────
\ A wavetable is 256 signed bytes (-124..+124); snd-poll/snd-fill recenter to
\ the DAC midpoint ($80) and mask to 6 bits after amplitude, so the table is
\ amplitude-agnostic and silence sits at mid-rail (no click).
\
\ Rather than ship four static DATA tables (~1KB of program space), the
\ generators below fill a table at ANY caller-given address. A 64K app builds
\ them in free hi RAM (e.g. $9200 in all-RAM mode); a memory-rich app can hand
\ them a DATA buffer. Run a generator once at startup, then point snd-waveform
\ at the address. The generators are defined after the arithmetic helpers
\ (they use /wave, below).
256 CONSTANT /wave       \ bytes per wavetable

\ ── Voice state (one voice; v2 clones this block and sums in snd-poll) ────
VARIABLE snd-phase       \ 16-bit phase accumulator; high byte = table index
VARIABLE snd-inc         \ phase increment per emitted sample (sets pitch)
VARIABLE snd-amp         \ amplitude as an arithmetic right-shift (0 = full)
VARIABLE snd-frames      \ remaining frames; 0 = idle (voice silent)
VARIABLE snd-wave-base   \ cached address of snd-wave for the CODE emitters
VARIABLE snd-slide       \ signed per-frame phase-increment delta (pitch slide; 0 = steady)
VARIABLE snd-seed        \ 16-bit LFSR state for snd-noise-fill (nonzero)
VARIABLE snd-noise-div   \ HSYNC lines each noise sample is held (>=1; higher = lower pitch)

\ ── Internal: PIA audio path → 6-bit DAC ─────────────────────────────────
\ Same canonical control-register values as lib/sound.fs snd-init, kept
\ private (leading underscore) so this file never collides with that one.
\ _snd-pia — internal: route the PIA audio path to the 6-bit DAC (one-time).
CODE _snd-pia  \ ( -- )
        LDB     #$34
        STB     $FF01           ; PIA0 CR-A: audio mux bit = 0
        STB     $FF03           ; PIA0 CR-B: audio mux bit = 0  -> 6-bit DAC
        STB     $FF21           ; PIA1 CR-A: data reg, no IRQ
        LDB     #$30
        STB     $FF23           ; PIA1 CR-B: DDR access
        LDB     $FF22
        ORB     #$02            ; PB1 = output
        STB     $FF22
        LDB     #$3C
        STB     $FF23           ; data mode + Six-Bit-Sound enable (CB2)
        ;NEXT
;CODE

\ ── snd-poll ( -- )  non-blocking single-sample emitter ──────────────────
\ If a new HSYNC scan line has elapsed since the last consumed flag, advance
\ the phase and write one wavetable sample to the DAC; otherwise return at
\ once. Touches only scratch registers (A/B/D/Y) — X(IP)/U(DSP)/S(RSP) are
\ preserved, so it is safe to call anywhere in a Forth thread.
\ Table index (0..255) is loaded through D with a cleared high byte because
\ 6809 accumulator-offset indexing (B,Y) treats the accumulator as SIGNED.
\ snd-poll — emit one wavetable sample to the DAC if a new HSYNC row elapsed; non-blocking.
CODE snd-poll  \ ( -- )
        LDA     $FF01           ; HSYNC flag in bit 7
        BPL     @done           ; clear -> no new row, fast return
        LDA     $FF00           ; reading $FF00 clears the HSYNC flag
        LDD     FVAR_snd_frames    ; voice idle? (LDD sets Z on the 16-bit value)
        BEQ     @done
        LDD     FVAR_snd_phase
        ADDD    FVAR_snd_inc
        STD     FVAR_snd_phase     ; phase += inc  (A = new high byte = index)
        TFR     A,B             ; B = table index
        CLRA                    ; D = 0..255 (unsigned offset)
        LDY     FVAR_snd_wave_base
        LDA     D,Y             ; A = signed wave sample
        LDB     FVAR_snd_amp+1     ; amplitude = right-shift count (low byte)
        BEQ     @amp0
@ash    ASRA                    ; arithmetic shift preserves sign
        DECB
        BNE     @ash
@amp0   ADDA    #$80            ; recenter to DAC midpoint
        ANDA    #$FC            ; mask to the 6-bit DAC (bits 7-2)
        STA     $FF20
@done
        ;NEXT
;CODE

\ ── snd-fill ( n -- )  blocking, n HSYNC-locked samples ──────────────────
\ Emit exactly n evenly-spaced samples, one per HSYNC scan line, blocking
\ until done. Ideal inside a VSYNC-wait / wait-past-row spin where the CPU
\ would otherwise idle — those rows become a clean, jitter-free burst.
\ snd-fill — emit n evenly HSYNC-locked samples back-to-back (blocking); for the VSYNC-wait spin.
CODE snd-fill  \ ( n -- )
        PSHS    X               ; save IP (we use X for the wave base)
        LDY     ,U              ; Y = n
        LEAU    2,U             ; pop n
        CMPY    #0
        BEQ     @fdone          ; n = 0
        LDD     FVAR_snd_frames
        BEQ     @fdone          ; voice idle
        LDX     FVAR_snd_wave_base
        LDA     $FF00           ; clear any stale HSYNC flag
@floop  LDA     $FF01
        BPL     @floop          ; wait for the next HSYNC edge
        LDA     $FF00           ; clear it
        LDD     FVAR_snd_phase
        ADDD    FVAR_snd_inc
        STD     FVAR_snd_phase
        TFR     A,B             ; B = table index
        CLRA                    ; D = 0..255
        LDA     D,X             ; A = signed wave sample
        LDB     FVAR_snd_amp+1
        BEQ     @famp0
@fash   ASRA
        DECB
        BNE     @fash
@famp0  ADDA    #$80
        ANDA    #$FC
        STA     $FF20
        LEAY    -1,Y
        BNE     @floop
@fdone  PULS    X
        ;NEXT
;CODE

\ ── snd-noise-fill ( n -- )  blocking, n noise samples ────────────────────
\ Like snd-fill, but each sample is a fresh pseudo-random byte from a 16-bit
\ galois LFSR (tap $B400) instead of the wavetable. Two knobs shape it:
\   snd-noise-div  — hold each sample this many HSYNC lines (1 = bright hiss
\                    at 15.7kHz; higher = lower-pitched rumble)
\   snd-amp        — amplitude right-shift around the DAC midpoint (0 = full,
\                    higher = quieter); ramp it up across calls for a decay
\ Total duration = n * snd-noise-div scan lines. Independent of voice state.
\ snd-noise-fill — emit n noise samples (pitch via snd-noise-div, level via snd-amp).
CODE snd-noise-fill  \ ( n -- )
        PSHS    X               ; save IP (X reused as the per-sample hold counter)
        LDY     ,U              ; Y = sample count
        LEAU    2,U
        CMPY    #0
        BEQ     @ndone
        LDA     $FF00           ; clear any stale HSYNC flag
@nsamp  LDD     FVAR_snd_seed   ; 16-bit galois LFSR step
        LSRA
        RORB
        BCC     @nofb
        EORA    #$B4            ; tap $B400 (high byte)
@nofb   STD     FVAR_snd_seed
        SUBA    #$80            ; high byte -> signed deviation about midpoint
        LDB     FVAR_snd_amp+1  ; amplitude right-shift (decay)
        BEQ     @namp0
@nash   ASRA
        DECB
        BNE     @nash
@namp0  ADDA    #$80            ; recenter
        ANDA    #$FC            ; A = held DAC value for this sample
        LDX     FVAR_snd_noise_div  ; hold it for div HSYNC lines (>=1)
@nhold  LDB     $FF01
        BPL     @nhold          ; wait for the next HSYNC edge
        LDB     $FF00           ; clear it
        STA     $FF20           ; output the held sample
        LEAX    -1,X
        BNE     @nhold
        LEAY    -1,Y
        BNE     @nsamp
@ndone
        PULS    X
        ;NEXT
;CODE

\ ── Pitch helper ─────────────────────────────────────────────────────────
\ inc = freq * 65536 / 15734  ≈  freq * 4.166  =  freq*4 + freq/6.
\ Kept as integer ops with no 16-bit overflow for freq up to a few kHz.
\ freq>inc — convert a frequency in Hz to the per-sample phase increment (full snd-fill rate).
: freq>inc  ( freq -- inc )
  DUP 6 /MOD SWAP DROP    \ ( freq freq/6 )
  SWAP 2* 2* + ;          \ freq/6 + freq*4

\ ── Wavetable generators ( addr -- ) ─────────────────────────────────────
\ Each fills a 256-byte signed table at addr. Run once into free RAM, then
\ `addr snd-waveform`. One-time cost, so written in plain Forth.

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

\ ── Public API ───────────────────────────────────────────────────────────

\ One-time setup: configure the DAC path and seed noise. Does NOT set a
\ waveform — generate one (gen-sine etc.) into free RAM and snd-waveform it
\ before playing a voice.
\ snd-async-init — one-time setup: init the DAC path and noise (no default waveform).
: snd-async-init  ( -- )
  _snd-pia
  $ACE1 snd-seed !          \ nonzero LFSR seed for noise
  1 snd-noise-div !         \ default noise pitch = bright (1 line/sample)
  0 snd-frames !
  0 snd-amp !
  $80 $FF20 c! ;            \ DAC to midpoint (silence)

\ Point the oscillator at a 256-byte signed wavetable (from a gen-* generator,
\ or any 256-byte table). Takes effect on the next emitted sample.
\ snd-waveform — point the oscillator at a 256-byte signed wavetable.
: snd-waveform  ( addr -- )  snd-wave-base ! ;

\ Hold silence for `frames` frames while keeping the voice "playing" (so the
\ main loop keeps emitting + aging). The async analog of the old snd-pause:
\ inc 0 freezes the phase and amp 8 collapses the sample to the DAC midpoint.
\ snd-rest — hold silence for N frames (a rest between notes).
: snd-rest  ( frames -- )
  snd-frames !
  0 snd-inc !
  0 snd-slide !
  8 snd-amp ! ;

\ Start a voice. Returns immediately; the sound is produced by subsequent
\ snd-poll / snd-fill calls and aged by snd-frame.
\   freq   pitch in Hz (assuming full snd-fill sample rate)
\   amp    amplitude as a right-shift: 0 = full, 1 = half, ...
\   frames how many frames (VSYNC ticks) the note lasts
\ snd-note — start a voice (freq Hz, amp right-shift, frames duration); returns immediately.
: snd-note  ( freq amp frames -- )
  snd-frames !
  snd-amp !
  freq>inc snd-inc !
  0 snd-phase !
  0 snd-slide ! ;         \ steady pitch unless snd-slide! is set after

\ Silence the voice now.
\ snd-stop — silence the voice now and hold the DAC at midpoint.
: snd-stop  ( -- )
  0 snd-frames !
  $80 $FF20 c! ;

\ Per-VSYNC housekeeping: age the note by one frame, auto-stop at zero.
\ snd-frame — per-VSYNC housekeeping: age the note one frame, auto-stop at zero.
: snd-frame  ( -- )
  snd-frames @ 0= IF EXIT THEN
  snd-frames @ 1 - snd-frames !
  snd-inc @ snd-slide @ + snd-inc !    \ apply per-frame pitch slide
  snd-frames @ 0= IF snd-stop THEN ;

\ Live control (no restart): retune / change volume / set a pitch slide.
\ snd-pitch! — retune the running voice without restarting it.
: snd-pitch!  ( freq -- )  freq>inc snd-inc ! ;
\ snd-amp! — change the running voice amplitude (right-shift) without restarting.
: snd-amp!    ( amp -- )   snd-amp ! ;
\ snd-slide! — set a signed per-frame pitch slide (negative = falling, e.g. a zap).
: snd-slide!  ( delta -- )  snd-slide ! ;

\ Is a voice currently playing?  ( -- flag )
\ snd-playing? — true if a voice is currently active.
: snd-playing?  ( -- f )  snd-frames @ 0= 0= ;
