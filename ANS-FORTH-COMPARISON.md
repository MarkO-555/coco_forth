# ANS-Forth (Sean Conner, 2025) vs Bare Naked Forth

A side-by-side comparison of two ITC Forth implementations for the 6809.

ANS-Forth source: `~/github/ANS-Forth` (`forth.asm`, GPL3+).

## Identity & Scope

|                | ANS-Forth                                            | Bare Naked Forth                                                       |
|----------------|------------------------------------------------------|-----------------------------------------------------------------------|
| Form           | Single 11.5K-line `forth.asm`, GPL3+                 | Kernel + Python cross-compiler + .fs library                          |
| Target         | Generic 6809 (host wires 3 vectors: getchar/putchar/bye) | TRS-80 CoCo specifically                                          |
| Spec           | ANS Forth 2012 (formally documented per spec)        | Bespoke dialect, no compliance claim                                  |
| Assembler      | `a09` (Sean's own)                                   | `lwasm`                                                               |
| Distribution   | One binary, runs from ROM                            | `make` (ROM mode) / `make allram` → DECB binary, eventual cart ROM    |

## Architecture (both ITC)

|                | ANS-Forth                                  | Bare Naked Forth               |
|----------------|--------------------------------------------|-------------------------------|
| IP register    | **Y**                                      | **X**                         |
| Data stack     | **U**                                      | **U**                         |
| Return stack   | **S**                                      | **S**                         |
| xt / scratch   | X (xt), D                                  | Y (scratch), D                |
| NEXT           | Inlined per primitive (`ldx ,y++ ; jmp [,x]`) | Shared NEXT routine        |
| DOCOL          | `pshs y ; leay 2,x ; ldx ,y++ ; jmp [,x]`  | Standard DOCOL pushing IP     |
| Dictionary on target | Yes — full headers (link / 16-bit length+flags / name / xt / body) | **None** — names stripped by cross-compiler |

## Word counts / Wordsets

|                | ANS-Forth                                                  | Bare Naked Forth                                          |
|----------------|------------------------------------------------------------|----------------------------------------------------------|
| Built-in words | 272 (in target dictionary)                                 | 81 primitives + 3 inline data words; rest in `lib/*.fs`  |
| Name flags     | IMMEDIATE, HIDDEN, NOINTERP, DOUBLE-TO, LOCAL-TO           | (Cross-compile time only)                                |
| ANS sets       | CORE/-EXT, DOUBLE/-EXT, EXCEPTION/-EXT, LOCAL/-EXT, TOOLS (most -EXT), SEARCH/-EXT, STRING/-EXT | None formally; CORE-ish primitives only |
| Notable absent | BLOCK, FACILITY, FILE, FLOATING, MEMORY, XCHAR             | EXCEPTION, LOCALS, SEARCH-ORDER, DOES>, pictured numeric I/O, BEGIN/WHILE/REPEAT |

## Interactive Surface

|                       | ANS-Forth                                                                         | Bare Naked Forth                                          |
|-----------------------|-----------------------------------------------------------------------------------|----------------------------------------------------------|
| On-device interpreter | **Yes** — full text reader, parser, `: … ;`, `CREATE … DOES>`, `EVALUATE`, `SEE` | **No** — write `.fs` on host, cross-compile, transfer    |
| Number prefixes       | `#dec`, `$hex`, `%bin`, `'c'`                                                     | Compile-time: `$hex`, `%bin`, `#literal` (fc.py)         |
| Compile-time data     | n/a                                                                               | `DATA[PY name <python> ]DATA` (Python embedded in .fs)   |
| Inline assembly       | Hand-coded primitives only                                                        | `CODE` / `KCODE` blocks inside .fs                       |
| Struct definers       | n/a                                                                               | `+FIELD`, `CFIELD:`, `FIELD:`                            |

## Platform Integration

|                          | ANS-Forth                          | Bare Naked Forth                                                                 |
|--------------------------|------------------------------------|---------------------------------------------------------------------------------|
| Hardware-specific words  | None — pure CPU + I/O vectors      | VDG/SG, keyboard matrix, IRQ/VSYNC, SAM register, FujiNet, sound DAC, sprite/beam libs |
| Memory profiles          | One; placeable in ROM              | Two: ROM mode ($2000) and all-RAM ($1000-staged → $E000 final)                  |
| Tutorial / demos         | None                               | 13-chapter tutorial, ~10 demos (Tetris, Kaleidoscope, Rain, etc.)               |
| Build framework          | `GNUmakefile` (one rule)           | `make`, `make allram`, `make dsk` (decb), XRoar auto-launch, `fc.py --cycles`   |

## Engineering Process

|                        | ANS-Forth                                                          | Bare Naked Forth                                                |
|------------------------|--------------------------------------------------------------------|----------------------------------------------------------------|
| Built-in tests         | `.test`/`.endtst` blocks throughout source, asserts vs. registers/memory | None in-asm; relies on demo-running + manual XRoar verification |
| Standards documentation | Full Implementation-Defined / Ambiguous-Conditions tables in README | `reference.html`, tutorial HTML, `kernel/README.md`             |
| Issue tracking         | Implicit                                                           | `issues.jsonl` workflow                                        |

## One-line Take

**ANS-Forth** is a *standards-compliant, self-hosted, hardware-agnostic* 6809
Forth — drop-in three vectors and you have an interactive ANS 2012 system. Big,
complete, conservative.

**Bare Naked Forth** (the Forth that powers CoCo Renovation) is a
*cross-compiled, hardware-rich, custom* Forth —
smaller kernel, no on-device interpreter, but the toolchain (Python escape
hatches, KCODE blocks, struct definers, `DATA[PY]`, cycle measurement) and the
CoCo-native libraries (VDG, sprites, sound, FujiNet) are the value
proposition. It's a development environment, not a Forth system.

## Things Worth Borrowing from ANS-Forth

If we ever go on-device:

- **Inline-NEXT pattern** — `ldx ,y++ ; jmp [,x]` at the tail of each primitive
  saves the shared-NEXT JMP cost (4–5 cycles per dispatch) at a cost of ~3
  bytes per primitive. On a kernel with ~80 primitives, the trade is real and
  measurable.
- **Dictionary header layout** — link / 16-bit length+flags / name / xt /
  body. The flag bits (IMMEDIATE / HIDDEN / NOINTERP / DOUBLE-TO / LOCAL-TO)
  are a clean, compact design.
- **`.test`/`.endtst` in-source assertion pattern** — embed unit tests next to
  the primitive being tested, with asserts against registers/memory after
  invocation. Drops into kernel.asm cleanly if a09's test directives are
  ported (or replaced with lwasm equivalents).
- **SEE / EVALUATE design** — for an eventual on-device REPL milestone.
