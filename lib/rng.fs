\ rng.fs — compatibility shim
\
\ rng and rnd are now kernel primitives, and the seed variable lives
\ in kernel space as VAR_SEED (exposed by fc.py as kvar-seed).  This
\ file re-exports the seed address under its legacy name so older
\ demos compile without modification.

: seed  kvar-seed ;
