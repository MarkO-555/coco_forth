# Bare Naked Forth — Presentation Speaking Notes

*~10-minute talk · 12 slides · `presentation.html`*

Notes track the slide order. Each section is what to say while that
slide is on screen. Timing in parentheses is a target, not a budget.

---

## Slide 1 — Title (15 sec)

Land on the title and let the image breathe for a beat. One sentence:

> "Bare Naked Forth — a cross-compiled, token-threaded Forth for the
> TRS-80 Color Computer. No OS, no disk, no clothes."

Press right-arrow.

---

## Slide 2 — The Question (1 min)

Open with the central provocation:

> "What if Tandy had made better software decisions in 1982?"

That question launched this project. The Color Computer had a genuinely
elegant processor — the Motorola 6809. The hardware was never the
problem. The cassette interface, the 32-column editor, the assembler
that couldn't handle a real project — those were. And they were all
software. We can't go back and change 1982, but we *can* build the
software layer the machine deserved.

---

## Slide 3 — The Hardware (1 min)

The 6809 is the hero of the talk. Quick tour:

- Two 16-bit index registers (X and Y).
- Two stack pointers — S and U. *(Foreshadow: this matters in two slides.)*
- PC-relative addressing — position-independent code is natural.
- Clean, orthogonal ISA, influenced by the PDP-11.
- Writing 6809 assembly is genuinely *pleasant* — a rare thing for an
  8-bit machine.

The callout is the punchline: the 6809 was running multitasking
operating systems by the early 80s. The hardware had headroom. **The
tooling didn't keep up.**

---

## Slide 4 — Renovation, Not Emulation (1 min)

Most people doing vintage computing do one of two things:

- **Emulation** — run the old thing on new hardware.
- **Preservation** — archive what existed.

This project is neither. **Renovation** keeps the hardware authentic —
real 6809, real 64K, real cartridge slot — and replaces only the
software layer. The delivery mechanism is a ROM cartridge: pull it
out, you have a stock CoCo. Plug it in, you have a modern development
environment.

That non-destructive property is a design constraint, not an
afterthought. Anything that hard-bricks a CoCo isn't a renovation,
it's a transplant.

---

## Slide 5 — Why Forth? (1 min)

Two stacks. The 6809 has two hardware stack pointers (S and U). Forth
is defined by two stacks (data and return). This is **not a
coincidence** — the 6809 was practically designed to run Forth.

Concrete payoff: the entire kernel fits in roughly 4 KB of 6809
assembly. Other CoCo Forths typically need an OS, a disk, or both.
This one needs neither.

*(Slide says "~1KB" — that was the v1 number. The current kernel is
~4KB after the math, RG6 graphics, beam-tracing, sound, and
random-number primitives landed. Decide on the day whether to update
the slide or just gloss over it as "small.")*

---

## Slide 6 — The Architecture (1 min)

The whole inner interpreter is two instructions:

```
LDY ,X++    ; fetch next CFA, advance IP
JMP [,Y]    ; jump through CFA to machine code
```

That's Indirect Threaded Code. Three things make it work:

- The kernel — ~4KB of 6809 assembly with **81 primitives**.
- The CFA dispatch table — maps tokens to machine code.
- The compiled thread — a sequence of 2-byte CFA addresses.

Register convention: X is the instruction pointer, U is the data stack
pointer, S is the return stack pointer. Y is scratch.

*(Slide currently says 25 primitives — same caveat as kernel size.)*

---

## Slide 7 — The Workflow (45 sec)

Walk left-to-right across the diagram:

> Forth source → fc.py cross-compiler → DECB binary → CoCo 6809.

The asymmetry is the point. **On your Mac**, you write Forth and run
`make`. **On the CoCo**, the kernel executes the compiled thread; it
never sees the source. This is what makes the workflow practical for
modern development — full editor, version control, fast iteration —
without giving up authenticity on the target.

---

## Slide 8 — What We Built (1 min)

The proof of concept, plus everything that's grown around it:

- **kernel.asm** — 6809 ITC executor, 81 primitives.
- **fc.py** — Forth cross-compiler in Python: colon definitions,
  variables, IF/ELSE/THEN, DO/LOOP, BEGIN/AGAIN/UNTIL, struct
  definers (`+FIELD`), and compile-time data via `DATA[PY]`.
- **The demos** — clock, kaleidoscope, tetris, rain, calculator,
  sound demo, and others. The clock alone reads time over FujiNet
  and renders an analog face with a smooth-sweeping second hand.
- **Hello, World** — the original validation. Real output on
  (emulated) real hardware. That moment was the proof the
  architecture holds.

The terminal box on the slide shows the full workflow in three lines.

*(Slide stat reads "25 primitives, ~1K bytes" — see slide 5/6
caveats.)*

---

## Slide 9 — The Tutorial (1 min)

Second major deliverable: **Getting Started with Bare Naked Forth** —
a 13-chapter beginner's book, styled as a period-appropriate manual.
Cover, illustrations, chapter programs.

The chapters take someone with no Forth experience from "what is a
stack" to a complete interactive game. Each chapter has a working
example program and DIY exercises. Highlight a few:

- **Ch 1 Meet Your Stack** — the foundation.
- **Ch 6 Count and Loop** — DO, LOOP, I.
- **Ch 10 The Calculator** — a real RPN calculator.
- **Ch 12 The Guessing Game** — a complete game.
- **Ch 13 Getting It onto Your CoCo** — real hardware deployment.

If you can show the guessing game running live in XRoar at this
point, do it. Live demo lands harder than any description.

---

## Slide 10 — vs. Every Other CoCo Forth (45 sec)

This is where the project's *position* gets sharp. Walk the table
top-to-bottom:

- **NitrOS-9 Forth** — needs OS-9, needs a disk, hours to set up,
  large kernel.
- **Typical CoCo Forth** — usually needs both, minutes-to-hours setup,
  medium kernel.
- **Bare Naked Forth** — no OS, no disk, seconds to "Hello, World",
  ~4KB kernel.

The callout is the elevator pitch:

> "Write Forth on your Mac. Run it on the CoCo. That's the whole
> workflow."

---

## Slide 11 — Where This Goes (1 min)

The roadmap, near-term to longer-term:

- **Serial loader** — bit-banged via PIA. Send bytecode over RS-232
  from a modern machine.
- **ROM cartridge image** — kernel burned to flash, plug-and-play.
- **SD card** — store and load programs via CoCoSDC.
- **RP2350 co-processor** — one core handling the 6809 bus, the
  other managing storage and services. The 6809 gets capabilities
  that didn't exist in 1987 without knowing anything changed. *(A
  prototype board called "Centipede" — designed by Henry Strickland
  — implements the hardware side and is en route.)*
- **CoCo 1 / 2 / 3** — same bytecode runs everywhere. Hardware
  differences are the kernel's problem, not the application's.

The bigger picture: the CoCo community has done extraordinary
preservation work. What doesn't exist is anything genuinely new. This
is the U-turn — same hardware, different direction.

---

## Slide 12 — The Point (45 sec)

Land on the closing thought:

> "Constraints are features."

You feel the 64K. You feel the register pressure. The feedback is
immediate and embodied. The 6809 was an elegant processor let down by
its ecosystem. **Bare Naked Forth** is the ecosystem it deserved.

End on `github.com/ugufru/coco`. Pause. Take questions.

---

## Numbers footnote (for the speaker)

The deck was authored when the kernel was smaller. Current discrete
numbers, in case anyone asks:

- **81 primitives** (84 CFA entries: 81 code + 3 inline data words —
  `font-data`, `sprite-data`, `sin-data`).
- **~4 KB kernel** (3977 bytes ROM mode, 4029 bytes all-RAM mode).
- **About a dozen demos** in `src/`.
- **216 issues** tracked in `issues.jsonl`, ~165 done.

If the slides still say "25" / "~1K", that's a v1 number — note for
yourself but don't correct on the fly unless someone asks.

---

## Things to consider adding

*Original suggestions preserved from the prior notes — unfulfilled
ideas the deck author flagged for a future revision.*

- **A demo moment** — if you can show XRoar running the guessing
  game (or tetris, or the clock) live, even briefly, that lands
  better than any description.
- **A slide of the kernel architecture** — the ITC threading diagram
  (X=IP, U=DSP, S=RSP) is clarifying for a technical audience. Slide
  6 currently shows the registers as text; an actual diagram would
  carry more.
- **The tutorial cover page** — it's a strong visual artifact showing
  what the project *looks* like, not just what it does.
- **The 6809 two-stack insight** — worth slowing down on; it's the
  "aha" moment that explains why Forth and this hardware are a
  natural pair. (Slide 5 is the place.)

*Newer suggestions:*

- **Recent dev-experience wins** — a ~5,200-line space-combat game
  (Bare Naked Space Warp) was built on this Forth, and most of its
  author's feedback proposals have already shipped (#477/#478,
  #479, #487, #488, #489). Worth a sentence at slide 11 — real game
  work is happening here, not just tutorial demos.
