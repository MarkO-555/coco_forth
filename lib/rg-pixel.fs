\ rg-pixel.fs — RG6 artifact-color pixel primitives
\
\ Provides: rv (VRAM base variable), rg-init, rg-init-at,
\           rg-pcls, rg-pset (kernel), rg-pget, rg-addr,
\           rg-hline, rg-line (kernel)
\ Requires: kernel primitives, vdg.fs (set-sam-v, set-sam-f, set-pia)
\
\ rg-pset and rg-line are kernel CODE words.
\ rg-pget, rg-addr, rg-hline are Forth words defined here.

\ rv — VRAM base address for the active RG6 framebuffer; set by rg-init-at.
VARIABLE rv                       \ VRAM base address

\ ── Lookup tables ─────────────────────────────────────────────────────────
$8728 CONSTANT _COLTAB
$872C CONSTANT _SHFTAB
$8730 CONSTANT _MSKTAB

: _init-tables  ( -- )
  0 _COLTAB     C!  1 _COLTAB 1 + C!  2 _COLTAB 2 + C!  3 _COLTAB 3 + C!
  6 _SHFTAB     C!  4 _SHFTAB 1 + C!  2 _SHFTAB 2 + C!  0 _SHFTAB 3 + C!
  $3F _MSKTAB     C!  $CF _MSKTAB 1 + C!  $F3 _MSKTAB 2 + C!  $FC _MSKTAB 3 + C! ;

\ rg-init-at — Point the VDG at the given $0200-aligned VRAM base, set up shift/mask tables, and clear 6K of pixel memory. (rg-init wraps this with the kernel's default vram-base.)
: rg-init-at  ( base -- )
  _init-tables
  DUP rv !  KVAR-RGVRAM !
  6 set-sam-v
  rv @ 9 RSHIFT set-sam-f
  $F8 set-pia
  rv @ 6144 0 FILL ;

: rg-init  ( -- )  vram-base rg-init-at ;

: rg-pcls  ( -- )  rv @ 6144 0 FILL ;

VARIABLE _pa
VARIABLE _ps

\ rg-addr — Compute the byte address and intra-byte shift for pixel (x,y) in the active framebuffer; stores results in _pa (addr) and _ps (shift index).
: rg-addr  ( x y -- )
  32 * rv @ + SWAP
  DUP 3 AND _ps !
  2 RSHIFT + _pa ! ;

\ rg-pset ( x y color -- )  — kernel primitive

: rg-pget  ( x y -- raw )
  rg-addr
  _pa @ C@
  _ps @ _SHFTAB + C@ RSHIFT
  3 AND ;

VARIABLE _hl-c  VARIABLE _hl-y

: rg-hline  ( x1 x2 y color -- )
  _hl-c ! _hl-y !
  1 + SWAP
  DO  I _hl-y @ _hl-c @ rg-pset  LOOP ;

\ rg-line ( x1 y1 x2 y2 color -- )  — kernel primitive
