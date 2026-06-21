\ colored-text.fs — RG6 colored-text palette swatch test  (issue #536)
\
\ Diagnostic for the copy-mask colored-text feature (#535).  RG6 text is
\ recolored by ANDing each font byte with KVAR-RGCOLMASK during the glyph
\ copy: $FF=white (identity), $55 and $AA mask each artifact pair to a
\ single bit (blue/red, exact mapping CSS/column-parity dependent).
\
\ Prints the SAME line three times, one per mask, so the two non-white
\ rows can be eyeballed in XRoar to pin down which mask is blue vs red.
\ The $FF row doubles as a regression check (must still be white).
\
\ Press any key (BREAK returns to BASIC).

INCLUDE ../../lib/vdg.fs
INCLUDE ../../lib/bye.fs
INCLUDE ../../lib/rg-pixel.fs
INCLUDE ../../lib/font-art.fs
INCLUDE ../../lib/rg-text.fs

\ setup — RG6 mode + font, point the text renderer at the cleared framebuffer.
: setup  ( -- )
  rg-init                 \ SAM RG6 mode, CSS, clear 6K VRAM, rv := base
  init-font               \ copy artifact-safe font to font-base
  rv @ cv !               \ rg-type VRAM base = active framebuffer
  32 cb ! ;               \ 32 bytes per RG6 row

\ swatch — set the recolor mask, then type a counted string at (cx,cy).
: swatch  ( c-addr len mask cx cy -- )
  >R >R                   \ stash cx cy        ( c-addr len mask  R: cy cx )
  KVAR-RGCOLMASK C!       \ apply recolor mask ( c-addr len        R: cy cx )
  R> R>                   \ restore cx cy      ( c-addr len cx cy )
  rg-type ;

: main  ( -- )
  setup
  S" COLORED RG6 TEXT - MASK SWATCH"  $FF  1  1  swatch
  S" FF WHITE 0123456789 ABCDEF"      $FF  2  5  swatch
  S" 55 BLUE  0123456789 ABCDEF"      $55  2  8  swatch
  S" AA RED   0123456789 ABCDEF"      $AA  2 11  swatch
  $FF KVAR-RGCOLMASK C!              \ leave the mask at the identity default
  BEGIN KEY? UNTIL  KEY  $03 = IF exit-basic THEN
  exit-basic ;

main
