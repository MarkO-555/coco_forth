# Feedback from a Bare Naked Forth user

Notes from working on **Space Warp** — a real-time space combat game
built on Bare Naked Forth, the ITC kernel + cross-compiler in this
repo. Roughly 5,200 lines of Forth across one application, plus
heavy use of `CODE` blocks and `KCODE` promotion. These notes are
meant as honest user feedback, not a wish list.

## What works well

### Tiered code: kernel / KCODE / CODE / Forth

The four-layer structure (kernel primitives → KCODE in kernel space →
CODE in app space → high-level Forth) is the single best feature for
real work. When the app overflows, there's a clear playbook:

1. Identify the inner loop or hot pattern.
2. Promote it to a CODE word in the app, written in 6809 asm.
3. If app space is tight but kernel space has slack, promote that
   CODE to KCODE — bytes move to `$E000+` and free up the app budget.
4. Convert duplicated 3- to 5-word patterns into colon helpers.

That sequence has shipped in spacewarp many times, and it's a
genuinely powerful set of levers. It also makes the right thing
discoverable: I usually know which tier a piece of code wants to
live in within a few seconds of looking at it.

### Inline assembly via `CODE ... ;CODE`

The convention (`U` = DSP, `X` = IP, push results via `STD ,--U`,
end with `LDY ,X++ / JMP [,Y]`) is consistent and unobtrusive. After
a couple of CODE words you stop noticing the boilerplate. Mixing
high-level Forth and 6809 in the same source file feels seamless,
which is rare — most low-level languages have a more abrupt
boundary at the asm/native frontier.

### `fc.py` in the source tree

The cross-compiler being plain Python in `forth/tools/fc.py` is
huge. When the build behaves unexpectedly, reading the compiler is
trivial — and a couple of times during the spacewarp work, the
right answer was to add a flag (`--stage-base`) rather than
contort the source. Most toolchains make that prohibitively hard.

### `--stage-base` (recently added)

Pinning the staged-kernel address is exactly the right escape hatch
for binary-layout-sensitive applications. Spacewarp uses it to keep
the KCODE record at a LOADM-safe address regardless of kernel size
churn. Decoupling app layout from kernel size churn is a meaningful
robustness win.

### Factoring discipline

The 2-byte CFA cost per call makes deep factoring effectively free.
Spacewarp has hundreds of words averaging 2-4 lines each. In a C
codebase that style would be noisy (each call is `JSR addr` plus
stack housekeeping). Here it's the natural shape of the code, and
it pays back when reading: every word has a stack signature and
does one thing.

## Real friction points

### `I` doesn't survive a colon-defined helper

`CODE_I` reads from `,S` flat — no walk past intervening return
addresses. So `: jhp@  JOV-DMG I + C@ ;` returns the helper's
return address rather than the loop index of an outer DO/LOOP.
This is the friction point I noticed most. The `JOV-DMG I + C@`
pattern appears 13 times in spacewarp; factoring it would have
saved ~65 bytes, but the kernel's `I` semantics make that
impossible.

A standard ANS Forth's `I` walks the return stack to skip
intervening colon-call frames. That makes loop-body helpers
trivially factorable, which is a common idiom. I think this is
worth fixing — the cycle cost is small (one or two `LEAS`) and
the loss of factoring power is real.

### `EXIT` inside `IF/THEN` miscompiles

Per the CLAUDE.md gotcha. `IF EXIT THEN` works (early-return
guard), but `IF ... EXIT ... THEN` with code after the EXIT
inside the same conditional is dangerous. I carry this around
as a defensive habit, but it's the kind of thing that bites
casual users who try a natural pattern and silently get wrong
behaviour. Worth either fixing or making it a hard compile-time
error so the failure mode is loud.

### No vocabularies / word lists

Single global namespace. Naming gets cramped fast. During a
recent spacewarp optimization I wanted to define helpers
called `c@1`, `c!1`, `w@+`, etc. Couldn't simply name the
zero-pushing helper `0` because the parser interprets `0` as a
literal first. With `WORDLIST`s this would be straightforward:
shadow short common names inside an internal vocabulary,
expose only what the application needs at the top level.

This isn't a one-line fix — it's a real architectural feature —
but it's the missing piece I felt most often.

### No `IMMEDIATE` / compile-time words

Because fc.py is a cross-compiler, all the metaprogramming
machinery lives in Python rather than Forth. Most Forth
traditions let you define new control structures, parsers,
table generators, etc. as compile-time words inside the source.
Spacewarp can't, so it has hand-rolled tables (sprite data,
font glyphs, sin/cos, jovian genome decoding) where a richer
Forth would have a definer.

I'd settle for a way to define data words via a Python callout —
something like `: GENRE-TABLE [PYTHON] generate_genre_table() ;`
that splices computed bytes into the dictionary at compile time.
Even that lightweight bridge would clean up 100+ lines of
spacewarp boilerplate.

### No structured data

Only `CONSTANT`, `VARIABLE`, and raw byte/word arrays via
address arithmetic. Every "struct" is `BASE +` `C@`. The
spacewarp code has lots of `JOV-DMG i + C@` and
`SHIP-POS 1 + C@` style patterns. A simple `RECORD/FIELD`
definer (or even a Forth-83 `+FIELD`) would eliminate maybe
50% of the address-arithmetic noise. This isn't a heavy lift
and would dramatically improve source readability.

### Small-integer literals are expensive

Every literal compiles to 4 bytes (`LIT` CFA + 16-bit value).
In spacewarp, `0` appears 201 times, `1` 138 times, `2` 87
times — collectively burning ~1,700 bytes on LIT encoding for
values where a 2-byte dedicated CFA would suffice. Adding
`CFA_LIT0`/`CFA_LIT1`/`CFA_LIT2` primitives (~30 bytes of
kernel code) would shed ~850 bytes from spacewarp's app and
benefit every other coco demo. (Filed as a separate proposal
already — this is just to note it as a recurring friction point.)

### No live REPL

The cross-compiler model means there's no "type a word, watch
it run" feedback loop. Build, load, test, repeat. For game
work this is fine because the iteration cycle is short — but
the Forth tradition of incremental exploration at the prompt
doesn't exist here, and I do miss it occasionally. (Real
hardware I/O constraints likely make a true REPL impractical;
this isn't a complaint, just an acknowledgement of the
trade-off.)

## What I'd prioritize if extending the system

Roughly in order of bang-for-buck:

1. **`CFA_LIT0/1/2/3/-1` primitives.** ~30 bytes of kernel for
   ~850 bytes of app savings on a real codebase. Easy win.
2. **`RECORD/FIELD` (or `+FIELD`) definers.** Major source
   readability win, modest implementation cost.
3. **Fix `I` to walk the return stack.** Restores the
   loop-body-helper factoring idiom that's standard in other
   Forths.
4. **Fix or hard-error on `EXIT` inside `IF/THEN`.** Removes a
   silent footgun.
5. **Vocabularies / word lists.** Real architectural work, but
   the missing piece I noticed most often.
6. **Compile-time Python callouts** (or a `[PYTHON]` immediate
   form). Minimal-effort substitute for full `IMMEDIATE`,
   restores generative metaprogramming.

## Closing

This is a pleasant Forth to write in. The kernel/KCODE/CODE
discipline is well thought out, `fc.py` is hackable, the inline
asm story is clean. The friction points are real but bounded —
each one is addressable with localized work, and none of them
are showstoppers. After ~5K lines of application code, my
honest assessment is that I'd rather be writing this Forth
than writing C-with-asm against the same hardware, and the
specific things I miss are the specific things this project
could ship in its next iteration.

— Author of *Bare Naked Space Warp*, April 2026
