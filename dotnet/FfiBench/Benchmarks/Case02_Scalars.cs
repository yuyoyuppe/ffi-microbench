using BenchmarkDotNet.Attributes;
using CsBindgen;
using Stock = uniffi.benchffi.BenchffiMethods;

namespace FfiBench.Benchmarks;

/// <summary>Case 2: scalar args/return stay register-passed on both surfaces.</summary>
public class Case02_Scalars
{
    private ulong _a = 123456789;
    private ulong _b = 987654321;

    [Benchmark(Baseline = true)]
    public ulong Csbindgen() => CsbNative.csb_add(_a, _b);

    [Benchmark]
    public ulong Uniffi() => Stock.UAdd(_a, _b);
}
