# CoCo Renovation

*A manifesto for a machine that outlived its limits.*

---

## The Idea

Start with the difference between *emulation* and *renovation*. Emulation runs the old thing in a new context. Renovation keeps the machine authentic and rebuilds the layer that always held it back. Almost nobody bothers with renovation, even when the hardware invites it.

The TRS-80 Color Computer had 64K of RAM, a Motorola 6809 processor, and a ROM cartridge slot, wrapped in a development experience defined by cassette tapes, a 32-column editor, and an assembler (EDTASM+) that could not handle a project of any real size. The machine deserved better than the software it shipped with. That gap is the whole opportunity.

The question that started this project: what about the original CoCo could be improved with modern software practices and hardware? Not answered with nostalgia, but with tools and boards you can actually use today.

---

## Why the 6809 Is Worth Renovating

The 6809 is not nostalgia. It is an objectively elegant processor:

- Two 16-bit index registers (X and Y).
- Two stack pointers (S and U), giving you a call stack and a data stack as first-class concepts.
- PC-relative addressing throughout, so position-independent code is natural rather than a hack.
- A clean, orthogonal instruction set, clearly influenced by the PDP-11.
- Rich addressing modes without the irregularities of the Z80 or 6502.

Writing 6809 assembly is genuinely pleasant. The silicon was never the problem.

---

## The Limits Were Real, and They Were of Their Time

It is tempting to call the CoCo's software ecosystem a bad decision. It wasn't. The cassette interface, the 32-column screen, the assembler that buckled on real projects: each was a reasonable answer to what memory, storage, and silicon cost in 1982. The limitations were real, and they were driven by the hardware economics of the era, not by the machine's potential.

That is exactly why they lift today. The constraints that shaped the 1982 toolchain, the price of RAM, the slowness of tape, the size of a ROM, are gone. What remains is an elegant processor and an architecture that never got the software it deserved. Renovation is simply giving it that software now, on terms the 1980s could not afford.

---

## What Renovation Looks Like

The original plan imagined a whole toolchain living on the CoCo itself: a shell, an editor, an assembler, a linker in ROM. Reality took a better road, and it starts with a decision about where the work happens.

Development belongs on a modern machine. A Mac or a Linux box is perfectly suited to cross-compiling 6809 code with mature open-source tools, and it commits none of the CoCo's memory or cycles to the act of writing software. The CoCo is the target. Your desktop is the workshop.

**Bare Naked Forth** fills that workshop: a from-scratch, indirect-threaded Forth kernel for the 6809, paired with a cross-compiler, `fc.py`, that turns Forth source into a complete DECB binary you load straight into a real machine. This is not hypothetical. A compact ITC kernel with about eighty primitives, a library of reusable words with hand-written 6809 in the hot paths, a thirteen-chapter tutorial, a shelf of demos, and **Space Warp**, a full real-time starship-combat game, all already run on real hardware.

---

## The Workflow

Write Forth on your desktop, compile it with `fc.py` into a DECB binary, test it in an emulator, then deploy the same binary to real hardware over CoCoSDC, FujiNet, or DriveWire.

The constraints do not disappear because the toolchain is modern. You still feel the 64K. You still feel the register pressure. Cycles are still currency on a 0.89 MHz machine, and the gap between a threaded word and a hand-written `CODE` word is something you measure, not guess. Renovation removes the accidental friction of 1982 and keeps the essential constraints that make the machine worth programming. And because development lives off the machine, every byte and every cycle stays free for the software itself: streamlined, optimized games and applications.

---

## The Ecosystem We Build On

The CoCo community never stopped building. We stand on it rather than reinventing it:

| Tool | What it does |
|---|---|
| lwasm | Excellent modern 6809 cross-assembler |
| XRoar / MAME | Accurate emulators with debugging support |
| CoCoSDC | SD-card storage interface for real CoCo hardware |
| CoCoFujiNet | WiFi peripheral with virtual disks and network services for real CoCos |
| DriveWire 4 | A modern PC acting as a disk server over serial |
| NitrOS-9 | Modernized OS-9, a real multitasking OS for the CoCo |
| CMOC | A working C compiler targeting the 6809 |
| toolshed | Cross-development utilities |

Bare Naked Forth is our own addition to that shelf: a modern take on one of computing's oldest languages. It pares Forth down to a bare, cross-compiled ITC kernel, and lets you drop into hand-written 6809 wherever it counts, sitting closer to the metal than a traditional Forth while staying small and legible enough to read end to end.

---

## The Hardware Catches Up

The original vision asked for one more thing: a modern cartridge that plugs into the pak slot, connects to the bus, and provides storage and peripherals without modifying the machine. At the time that was a wish. It is now being built, by others, on the RP2350.

The RP2350 is a fast, inexpensive microcontroller with enough I/O and speed to sit on the CoCo's expansion bus directly, through level-shifters, and answer the 6809 in real time.

- Henry Strickland's [**copico-centipede**](https://github.com/strickyak/copico-centipede), built on his RP2350 **Centipede** boards, exposes 46 usable GPIO lines covering most of the CoCo's expansion-port bus. It uses the RP2350 to talk to the 6809 directly, and presents itself as virtual peripherals addressable from 6809 code.
- The latest, still-unreleased RP2350 **CoCoFujiNet** will have this ability built in.

None of these do on-device compilation, and they do not need to. They deliver the other half of the renovation: a direct, modern connection to the slot, with storage and virtual peripherals, on authentic hardware.

---

## The CoCo Is an Idea Now

Here is the part that changes everything. Spend enough time with XRoar and the other emulators and a quiet realization sets in: the Color Computer is no longer a fixed hardware specification. It is an abstract concept. A precise, well-understood, faithfully reproduced concept, but a concept all the same.

That is not a loss. It is the answer to two problems at once. Real CoCos are finite and aging, and the factory will never make another. And the physical constraints of 1982 were bound to that specific silicon and its price. When the machine becomes an idea, both problems dissolve. An emulator hands a new generation an authentic CoCo without a single scarce chip. A modern board on the bus extends what that machine can do without betraying what it is. The definition of a CoCo widens to include everything faithful to its architecture, and the 1980s ceiling stops being a ceiling.

This is how the Color Computer survives its own hardware. Not frozen in a display case, but running: emulated, reimplemented, extended, and programmed with tools it never had. The renovation is not only of the software or the peripherals. It is of the idea of the machine itself, handed forward.

---

## Why *Wouldn't* You?

The hardware works. The processor is elegant. The limits were real once, and the reasons for them are gone. This was never about making the CoCo into something it isn't. It is about making it into what it always *could* have been, and what it can still become. You write 6809. You live in 64K. You run on real iron, on a faithful emulator, or on a board that speaks to the bus in real time. The Color Computer's best years are not necessarily behind it. That is the whole point of renovating instead of remembering.
