# Results — 2026-07-02 (x64, fork @ b89b258)

Environment: AMD Ryzen 9 7950X, Windows 11 26200, .NET 8.0.28 (net8.0, LibraryImport paths
active), rustc 1.88.0, BenchmarkDotNet 0.15.2 (default job, MemoryDiagnoser).
uniffi-bindgen-cs fork: `span_feature @ b89b258` (uniffi-rs 0.31). arm64: not yet measured.

Second snapshot, after three fork optimization commits (previous snapshot: `15daf0b`,
see git history of this file):

- `2cc7bfd` lift strings/bytes without intermediate copies on .NET 8+
- `953582a` extend the span fast path to byte-array arguments
- `b89b258` emit sync calls without per-call closure allocations

Acceptance bar: best applicable uniffi variant ≤ **1.1×** csbindgen ns/op, OR absolute cost
on the order of **< 50 ns/op**. Callbacks additionally: zero steady-state managed
allocations (scalar), allocation ≈ the string itself (string).

## Verdict per case (mean ns/op, csbindgen → best uniffi variant)

| # | Case | csbindgen | uniffi (best) | Ratio | Verdict |
|---|------|-----------|---------------|-------|---------|
| 1 | nop | 2.7 | 5.5 | 2.0× | **PASS** (Δ2.7 ns, 0 B/call) |
| 2 | add(u64,u64) | 4.2 | 5.4 | 1.30× | **PASS** (Δ1.2 ns, 0 B/call) |
| 3 | object method | 6.5 | 10.4 | 1.59× | **PASS** (Δ3.8 ns, 0 B/call) |
| 4 | string in 16 B–1 MiB | 76–1,118,694 | 15–118,051 (span†); stock also wins every size | 0.05–0.62× | **PASS** (uniffi wins outright) |
| 5 | string out 16 B–1 MiB | 56–454,838 | 60–348,951 | 0.70–1.08× | **PASS** (wins at ≥1 KiB; 16 B within 1.1×) |
| 6 | bytes in 1 KiB / 1 MiB / 16 MiB | 36 / 155,460 / 2,158,381 | 40 / 154,414 / 2,990,834 (span) | 1.11× / 0.99× / 1.39× | **PASS** at ≤1 MiB (Δ4 ns at 1 KiB); 16 MiB 1.39× this run (0.97–1.16× in short runs — page-fault sensitive, watch) |
| 6 | bytes out 1 KiB / 1 MiB / 16 MiB | 85 / 207,941 / 4,396,485 | 901 / 1,006,455 / 23,493,699 | 10.6× / 4.8× / 5.3× | **FAIL** — uniffi-rs-core limitation, see below |
| 7 | Vec\<String\> in (vs JSON incumbent) | 773 / 61,056 / 7,252,761 | 653 / 49,496 / 6,338,246 (span JSON) | 0.84–0.87× | **PASS** |
| 7 | Vec\<String\> out (vs JSON incumbent) | 634 / 39,087 / 8,956,715 | 871 / 55,881 / 10,406,890 | 1.37× / 1.43× / 1.16× | **FAIL** (improved from 1.6–1.8×; uniffi allocates ~½ of the JSON path) |
| 8 | Vec\<Record\> 1k in / out | 488,900 / 494,100 | 194,600 / 195,100 | 0.40× | **PASS** (uniffi 2.5× faster) |
| 9 | nested record in / out | 1,254 / 1,370 | 530 / 656 | 0.42 / 0.48× | **PASS** |
| 10 | map in 100 / 10k | 16,141 / 1,851,348 | 14,283 / 1,546,044 | 0.88 / 0.84× | **PASS** |
| 10 | map out 100 / 10k | 9,784 / 1,289,863 | 13,005 / 1,492,468 | 1.33× / 1.16× | **FAIL** (improved from 1.65×) |
| 11 | scalar callback steady-state / single-shot | 2.9 / 6.4 | 12.8 / 24.4 | 4.5× / 3.8× | **PASS** (<50 ns, **0 B** in both — single-shot was 176 B) |
| 12 | string callback ~200 B loop / single-shot | 235 / 242 | 69 / 96 | 0.29 / 0.40× | **PASS** (uniffi 2.5–3.4× faster; alloc = 424 B = the string itself ✓) |
| 13 | error Ok path | 4.5 | 7.0 | 1.53× | **PASS** (Δ2.4 ns, ≈ case 2 ✓) |
| 13 | error Err path | 4.5 (bool) / 48 (bool+msg) | 3,510 (exception) | ~770× | documented — keep bool results for routinely-failing APIs |
| 14 | 4-thread degradation | none | none (all 4T ≤ 1T per-op; span string 4T = 3.2 ns) | — | **PASS** |

† `UniffiSpanUtf8` (~6 ns flat) measures the borrowed zero-copy view; `UniffiSpanFromString`
(GetBytes + span) is the apples-to-apples variant from a C# `string`. Note stock uniffi now
beats the UTF-16 incumbent at every size anyway: csbindgen's `from_utf16_lossy` transcode in
Rust costs more than uniffi's vectorized C#-side UTF-8 encode + span-based lower.

## Remaining gaps (all outside or beyond the current bindgen fork)

1. **Bytes/sequence returns (cases 6-out, 7-out, 10-out)** — Rust-side uniffi-core lowering
   serializes into a freshly allocated length-prefixed RustBuffer; the C# lift is now a
   single span copy, so the remaining cost is uniffi-rs core (borrowing returns or an
   out-pointer ABI would be a uniffi-rs PR, not a bindgen change). Absolute costs: 1 ms per
   1 MiB image get, 56 µs per 1000-string list — acceptable for clipboard/hotkey use, not
   per-keystroke paths.
2. **Sequence-out residual 1.16–1.43×** — per-element length reads still go through the
   UnmanagedMemoryStream virtuals; a span-based sequence reader could close it if it ever
   matters (uniffi already allocates half of what the JSON incumbent does here).
3. **Err-path exceptions (~3.5 µs)** — inherent to exception-based error mapping; APIs that
   fail routinely should keep bool-style results.
4. **16 MiB bytes-in variance** — 0.97–1.39× across runs for both surfaces (fresh-page
   faults dominate); re-measure on quiet hardware before reading anything into it.

Raw reports: `results\results\*-report-github.md` (regenerate with `.\run_benchmarks.ps1`).
