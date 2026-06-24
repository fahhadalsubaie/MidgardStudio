# Vendored GRF libraries

These DLLs are from **[Tokeiburu's GRF Editor](https://github.com/Tokeiburu/GRFEditor)** and are vendored here so Midgard Studio builds and runs from a fresh clone with no extra setup.

| File | Purpose |
| --- | --- |
| `GRF.dll` | GRF archive reading + RO file-format parsers (images, sprites, gat/gnd/rsw/rsm) |
| `Utilities.dll` | Encoding / helper utilities used by `GRF.dll` |
| `Encryption.dll` | GRF table decryption support |
| `ErrorManager.dll` | Error plumbing used by the GRF library |

Midgard Studio uses these **read-only** (it never writes to a `.grf`). GRF Editor is open source and free to use.
