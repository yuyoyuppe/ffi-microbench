using BenchmarkDotNet.Attributes;
using CsBindgen;
using Stock = uniffi.benchffi.BenchffiMethods;

namespace FfiBench.Benchmarks;

/// <summary>Case 1: per-call floor — RustCallStatus, catch_unwind, checksum-free nop.</summary>
public class Case01_NullCall
{
    [Benchmark(Baseline = true)]
    public void Csbindgen() => CsbNative.csb_nop();

    [Benchmark]
    public void Uniffi() => Stock.UNop();
}
