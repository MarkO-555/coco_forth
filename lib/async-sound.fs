\ async-sound.fs — non-blocking (cooperative) DAC sound for the CoCo
\
\ Provides: snd-async-init, snd-note, snd-stop, snd-frame, snd-pitch!,
\           snd-amp!, snd-slide!, snd-env!, snd-ringmod!, snd-playing?,
\           snd-waveform, snd-shape, snd-rest, snd-poll, snd-fill,
\           snd-noise-fill, freq>inc
\
\ Requires: kernel primitives only (* /mod 2* @ ! c! ...). Waveform tables
\           come from lib/wavetable.fs (gen-sine etc.) — include it too,
\           generate a table, and snd-waveform it before playing.
\           No kvar, no trig table, no kernel patch — snd-poll reads the
\           HSYNC flag at $FF01 and writes the 6-bit DAC at $FF20 directly.
\
\ Footprint: ~765 bytes code (Forth words + 4 CODE emitters + 10 voice vars).
\            With lib/wavetable.fs the full async engine is ~1125 bytes code,
\            plus 256 bytes per runtime wavetable (or 0 with algorithmic modes).
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

\ ── Wavetables ──────────────────────────────────────────────────────────
\ A wavetable is 256 signed bytes (-124..+124); snd-poll/snd-fill recenter to
\ the DAC midpoint ($80) and mask to 6 bits after amplitude, so the table is
\ amplitude-agnostic and silence sits at mid-rail (no click).  The generators
\ (gen-sine/square/saw/tri, /wave) live in lib/wavetable.fs — generate a table
\ into free RAM and point snd-waveform at it before playing a voice.

\ ── Voice state (one voice; v2 clones this block and sums in snd-poll) ────
VARIABLE snd-phase       \ 16-bit phase accumulator; high byte = table index
VARIABLE snd-inc         \ phase increment per emitted sample (sets pitch)
VARIABLE snd-amp         \ attenuation 0..255 (0 = full volume, 255 = silent)
VARIABLE snd-frames      \ remaining frames; 0 = idle (voice silent)
VARIABLE snd-wave-base   \ cached address of the wavetable for the CODE emitters
VARIABLE snd-wave-mode   \ 0 = wavetable; 1 = saw, 2 = square, 3 = triangle (no table)
VARIABLE snd-slide       \ signed per-frame phase-increment delta (pitch slide; 0 = steady)
VARIABLE snd-env         \ signed per-frame attenuation delta (amp envelope; + fades out)
VARIABLE snd-rm-period   \ ring-mod square half-period in samples (0 = off)
VARIABLE snd-rm-count    \ ring-mod down-counter (per emitted sample)
VARIABLE snd-rm-sign     \ ring-mod current polarity ($00 = pass, $FF = invert)
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
        LDB     FVAR_snd_wave_mode+1
        BNE     @palg           ; non-zero -> algorithmic shape (no table)
        SUBA    #$80            ; table mode: A = signed offset (phasehi - 128)
        LDY     FVAR_snd_wave_base   ; base = table midpoint (table + 128)
        LDA     A,Y             ; A = signed wave sample (A is a signed offset)
@pgot   LDB     FVAR_snd_rm_period+1   ; ring mod (square): 0 = off
        BEQ     @rmoff
        DEC     FVAR_snd_rm_count+1    ; advance the half-cycle counter
        BNE     @rmtst
        STB     FVAR_snd_rm_count+1    ; count hit 0 -> reload (B = period)
        COM     FVAR_snd_rm_sign+1     ; and flip polarity ($00<->$FF)
@rmtst  TST     FVAR_snd_rm_sign+1
        BEQ     @rmoff
        NEGA                           ; modulator low -> invert the carrier
@rmoff  LDB     FVAR_snd_amp+1     ; B = attenuation (0 = full)
        BEQ     @amp0           ; full volume -> skip the multiply (fast path)
        COMB                    ; B = 255 - att = gain
        TSTA                    ; signed-magnitude multiply: |sample| * gain >> 8
        BMI     @aneg
        MUL                     ; D = sample * gain ; A = product high byte
        BRA     @amp0
@aneg   NEGA
        MUL
        NEGA                    ; re-apply the sign
@amp0   ADDA    #$80            ; recenter to DAC midpoint
        ANDA    #$FC            ; mask to the 6-bit DAC (bits 7-2)
        STA     $FF20
@done
        ;NEXT
        ; ── algorithmic shapes: derive signed sample in A from phase hi ──
@palg   DECB                    ; B held mode 1..3
        BEQ     @psaw           ; 1 = saw
        DECB
        BEQ     @psqr           ; 2 = square
        TSTA                    ; 3 = triangle: fold the phase hi
        BPL     @ptri
        COMA                    ; A = 255 - phasehi (mirror the down-ramp)
@ptri   ASLA                    ; *2
        SUBA    #$80            ; -> signed deviation
        BRA     @pgot
@psaw   SUBA    #$80            ; saw: deviation = phasehi - 128
        BRA     @pgot
@psqr   TSTA                    ; square: sign of phase hi
        BMI     @psqn
        LDA     #124
        BRA     @pgot
@psqn   LDA     #-124
        BRA     @pgot
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
        LDB     FVAR_snd_wave_mode+1
        BNE     @falg           ; non-zero -> algorithmic shape
        SUBA    #$80            ; table mode: A = signed offset (phasehi - 128)
        LDA     A,X             ; A = signed wave sample (X = table midpoint)
@fgot   LDB     FVAR_snd_rm_period+1   ; ring mod (square): 0 = off
        BEQ     @frmoff
        DEC     FVAR_snd_rm_count+1
        BNE     @frmtst
        STB     FVAR_snd_rm_count+1    ; reload (B = period)
        COM     FVAR_snd_rm_sign+1     ; flip polarity
@frmtst TST     FVAR_snd_rm_sign+1
        BEQ     @frmoff
        NEGA                           ; modulator low -> invert
@frmoff LDB     FVAR_snd_amp+1     ; B = attenuation (0 = full)
        BEQ     @famp0
        COMB                    ; gain = 255 - att
        TSTA
        BMI     @faneg
        MUL
        BRA     @famp0
@faneg  NEGA
        MUL
        NEGA
@famp0  ADDA    #$80
        ANDA    #$FC
        STA     $FF20
        LEAY    -1,Y
        BNE     @floop
@fdone  PULS    X
        ;NEXT
        ; ── algorithmic shapes: derive signed sample in A from phase hi ──
@falg   DECB                    ; B held mode 1..3
        BEQ     @fsaw           ; 1 = saw
        DECB
        BEQ     @fsqr           ; 2 = square
        TSTA                    ; 3 = triangle
        BPL     @ftri
        COMA
@ftri   ASLA
        SUBA    #$80
        BRA     @fgot
@fsaw   SUBA    #$80
        BRA     @fgot
@fsqr   TSTA
        BMI     @fsqn
        LDA     #124
        BRA     @fgot
@fsqn   LDA     #-124
        BRA     @fgot
;CODE

\ ── snd-noise-fill ( n -- )  blocking, n noise samples ────────────────────
\ Like snd-fill, but each sample is a fresh pseudo-random byte from a 16-bit
\ galois LFSR (tap $B400) instead of the wavetable. Two knobs shape it:
\   snd-noise-div  — hold each sample this many HSYNC lines (1 = bright hiss
\                    at 15.7kHz; higher = lower-pitched rumble)
\   snd-amp        — attenuation 0..255 (0 = full, 255 = silent); ramp it up
\                    across calls for a smooth decay
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
        LDB     FVAR_snd_amp+1  ; B = attenuation (0 = full)
        BEQ     @namp0
        COMB                    ; gain = 255 - att
        TSTA
        BMI     @naneg
        MUL
        BRA     @namp0
@naneg  NEGA
        MUL
        NEGA
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

\ ── Public API ───────────────────────────────────────────────────────────

\ One-time setup: configure the DAC path and seed noise. Does NOT set a
\ waveform — generate one (gen-sine etc.) into free RAM and snd-waveform it
\ before playing a voice.
\ snd-async-init — one-time setup: init the DAC path and noise (no default waveform).
: snd-async-init  ( -- )
  _snd-pia
  $ACE1 snd-seed !          \ nonzero LFSR seed for noise
  1 snd-noise-div !         \ default noise pitch = bright (1 line/sample)
  0 snd-wave-mode !         \ default to wavetable mode
  0 snd-frames !
  0 snd-amp !               \ full volume (0 attenuation)
  0 snd-env !
  0 snd-rm-period !         \ ring mod off
  $80 $FF20 c! ;            \ DAC to midpoint (silence)

\ Point the oscillator at a 256-byte signed wavetable (from a gen-* generator,
\ or any 256-byte table). Takes effect on the next emitted sample.
\ snd-waveform — point the oscillator at a 256-byte signed wavetable (mode 0).
\ Stores the table midpoint (addr+128) so the emitters can index with a signed
\ offset (phasehi-128), which is cheaper than a 16-bit unsigned offset.
: snd-waveform  ( addr -- )  128 + snd-wave-base !  0 snd-wave-mode ! ;

\ Select a table-free algorithmic waveform: 1 = saw, 2 = square, 3 = triangle.
\ Computed inline from the phase, so no wavetable RAM is needed for these.
\ snd-shape — select an algorithmic (table-free) waveform: 1 saw, 2 square, 3 triangle.
: snd-shape  ( mode -- )  snd-wave-mode ! ;

\ Hold silence for `frames` frames while keeping the voice "playing" (so the
\ main loop keeps emitting + aging). The async analog of the old snd-pause:
\ inc 0 freezes the phase and full attenuation (255) mutes the output.
\ snd-rest — hold silence for N frames (a rest between notes).
: snd-rest  ( frames -- )
  snd-frames !
  0 snd-inc !
  0 snd-slide !
  0 snd-env !
  255 snd-amp ! ;

\ Start a voice. Returns immediately; the sound is produced by subsequent
\ snd-poll / snd-fill calls and aged by snd-frame.
\   freq   pitch in Hz (assuming full snd-fill sample rate)
\   amp    attenuation: 0 = full volume, 255 = silent
\   frames how many frames (VSYNC ticks) the note lasts
\ snd-note — start a voice (freq Hz, amp attenuation, frames duration); returns immediately.
: snd-note  ( freq amp frames -- )
  snd-frames !
  snd-amp !
  freq>inc snd-inc !
  0 snd-phase !
  0 snd-slide !           \ steady pitch unless snd-slide! is set after
  0 snd-env ! ;           \ steady level unless snd-env! is set after

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
  snd-inc @ snd-slide @ + snd-inc !            \ pitch slide
  snd-amp @ snd-env @ + 0MAX 255 MIN snd-amp ! \ amplitude envelope (clamped 0..255)
  snd-frames @ 0= IF snd-stop THEN ;

\ Live control (no restart): retune / change volume / set pitch or amp ramps.
\ snd-pitch! — retune the running voice without restarting it.
: snd-pitch!  ( freq -- )  freq>inc snd-inc ! ;
\ snd-amp! — change the running voice attenuation (0 = full, 255 = silent).
: snd-amp!    ( att -- )   snd-amp ! ;
\ snd-slide! — set a signed per-frame pitch slide (negative = falling, e.g. a zap).
: snd-slide!  ( delta -- )  snd-slide ! ;
\ snd-env! — set a signed per-frame amplitude envelope: +delta fades out, -delta swells.
: snd-env!    ( delta -- )  snd-env ! ;
\ Enable square-wave ring modulation: period = samples per half cycle (modulator
\ freq ~ 7867/period Hz); 0 turns it off. The square just flips the carrier's
\ sign, so it costs no multiply. A persistent timbre setting (like snd-shape):
\ snd-note does not clear it; carrier slide and amp env both apply on top.
\ snd-ringmod! — enable square ring mod (period samples, ~7867/period Hz; 0 = off).
: snd-ringmod!  ( period -- )  DUP snd-rm-period !  snd-rm-count !  0 snd-rm-sign ! ;

\ Is a voice currently playing?  ( -- flag )
\ snd-playing? — true if a voice is currently active.
: snd-playing?  ( -- f )  snd-frames @ 0= 0= ;
