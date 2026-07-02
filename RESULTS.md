# Results — 2026-07-02 (x64)

Environment: AMD Ryzen 9 7950X, Windows 11 26200, .NET 8.0.28 (net8.0, LibraryImport paths
active), rustc 1.88.0, BenchmarkDotNet 0.15.2 (default job, MemoryDiagnoser).
uniffi-bindgen-cs fork: `span_feature @ 15daf0b` (uniffi-rs 0.31). arm64: not yet measured.

Acceptance bar (tightened 2026-07-02): best applicable uniffi variant ≤ **1.1×** csbindgen
ns/op, OR absolute cost on the order of **< 50 ns/op**. Callbacks additionally: zero
steady-state managed allocations (scalar), allocation ≈ the string itself (string).

## Verdict per case (mean ns/op, csbindgen → best uniffi variant)

| # | Case | csbindgen | uniffi (best) | Ratio | Verdict |
|---|------|-----------|---------------|-------|---------|
| 1 | nop | 2.7 | 9.0 | 3.3× | **PASS** (Δ6 ns, <50 ns) ⚠ 88 B/call |
| 2 | add(u64,u64) | 4.1 | 10.4 | 2.5× | **PASS** (<50 ns) ⚠ ~96 B/call |
| 3 | object method | 6.6 | 28.3 | 4.3× | **PASS** (<50 ns) ⚠ 248 B/call |
| 4 | string in 16 B | 76.9 | 15.4 span† / 79.5 stock | 0.20× | **PASS** (uniffi wins) |
| 4 | string in 1 KiB–1 MiB | 941–1,049,632 | 60–90,308 span† | 0.06–0.09× | **PASS** (uniffi wins big) |
| 5 | string out 16 B | 54.7 | 109.4 | 2.0× | **FAIL** (109 ns) |
| 5 | string out 1–64 KiB | 224 / 37,529 | 215 / 36,951 | ~1.0× | **PASS** |
| 5 | string out 1 MiB | 360,970 | 715,683 | 1.98× | **FAIL** |
| 6 | bytes in 1 KiB / 1 MiB / 16 MiB | 36 / 156,300 / 1,916,868 | 720 / 981,380 / 14,755,464 | 20× / 6.3× / 7.7× | **FAIL** |
| 6 | bytes out 1 KiB / 1 MiB / 16 MiB | 83 / 218,326 / 2,969,504 | 924 / 1,008,385 / 17,279,050 | 11× / 4.6× / 5.8× | **FAIL** |
| 7 | Vec\<String\> in (vs JSON incumbent) | 773 / 59,835 / 7,506,170 | 697–698 / 49,243 / 6,193,081 (span JSON) | 0.82–0.90× | **PASS** |
| 7 | Vec\<String\> out (vs JSON incumbent) | 609 / 38,816 / 8,811,481 | 992 / 64,436 / 15,593,883 | 1.6–1.8× | **FAIL** |
| 8 | Vec\<Record\> 1k in / out | 496,400 / 517,800 | 178,600 / 222,700 | 0.36 / 0.43× | **PASS** (uniffi 2.3–2.8× faster) |
| 9 | nested record in / out | 1,251 / 1,334 | 545 / 744 | 0.44 / 0.56× | **PASS** |
| 10 | map in 100 / 10k | 16,014 / 1,802,960 | 14,357 / 1,525,421 | 0.85–0.90× | **PASS** |
| 10 | map out 100 / 10k | 9,574 / 1,277,258 | 15,784 / 2,138,714 | 1.65–1.67× | **FAIL** |
| 11 | scalar callback (steady-state) | 2.8 | 14.4 | 5.1× | **PASS** (<50 ns, **0 B/op** ✓) |
| 11 | scalar callback single-shot | 6.3 | 32.5 | 5.2× | **PASS** (<50 ns) ⚠ 176 B on the C#-initiated fire entry, not the callback |
| 12 | string callback ~200 B | 209.7 | 117.1 | 0.56× | **PASS** (uniffi wins) ⚠ 952 B vs 424 B alloc |
| 13 | error Ok path | 4.5 | 15.2 | 3.4× | **PASS** (<50 ns) |
| 13 | error Err path | 4.5 (bool) / 48.3 (bool+msg) | 3,664 (exception) | 811× | documented — keep bool results for routinely-failing APIs |
| 14 | 4-thread degradation | none (scales) | none (scales; span string 4T = 2.1 ns/op) | — | **PASS** |

† `UniffiSpanFromString` (UTF8.GetBytes + span call) is the apples-to-apples variant when the
caller holds a C# `string`. The pure `UniffiSpanUtf8` numbers (~6 ns flat at every size) measure
the borrowed zero-copy view without materializing the payload on the Rust side — representative
only when Rust can consume borrowed UTF-8 directly (e.g. JSON parse, case 7). The csbindgen
string-in baseline includes its mandatory Rust-side `from_utf16_lossy` transcode — that cost is
inherent to the incumbent's UTF-16 convention, and is why stock uniffi already wins at ≥1 KiB.

## Where the failures come from (fork work items, by the reference app impact)

1. **Lift copies through an intermediate managed `byte[]`** — `FfiConverterString.Lift` /
   `FfiConverterByteArray` / sequence & map `Read` all do `AsStream().ReadBytes(len)` then
   decode. On net8 the decode can run directly over the native RustBuffer memory
   (`Encoding.UTF8.GetString(ReadOnlySpan<byte>)`, `new byte[]`+single copy, pooled buffers).
   Expected to fix: case 5 (16 B and 1 MiB), case 6 out, case 7 out, case 10 out, and most of
   case 12's excess allocation.
2. **No fast path for byte buffers in** — case 6 take (clipboard images) is 6–20× slower; extend
   the `high_performance_strings` approach to `Vec<u8>` arguments (`ReadOnlySpan<byte>` +
   `*_raw` ptr+len borrow, copy-to-own inside Rust only when needed).
3. **Per-call closure allocations** — every generated call allocates 88–248 B
   (`RustCall`/`CallWithPointer` lambdas + `UniffiRustCallStatus` boxing through delegates).
   Emit inline try/finally + direct status check instead of delegate-based helpers. Fixes the
   ⚠ rows (GC pressure on hot paths), and should shave the call floor toward the csbindgen
   numbers.
4. **Callback dispatch cost** (cases 11–12): 14.4 ns vs 2.8 ns steady-state is vtable +
   handle-map + status plumbing; acceptable in absolute terms, and the string callback already
   beats the incumbent (no Rust-side UTF-16 encode). Revisit only after items 1–3.

Raw reports: `results\results\*-report-github.md` (regenerate with `.\run_benchmarks.ps1`).
