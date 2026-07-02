using BenchmarkDotNet.Attributes;
using CsBindgen;

namespace FfiBench.Benchmarks;

/// <summary>
/// Case 3: instance method — csbindgen context pointer (RwLock acquire + Arc clone,
/// NativeContext pattern) vs uniffi Object (Arc handle + CallWithPointer).
/// </summary>
public unsafe class Case03_ObjectMethod
{
    private CsbContext* _ctx;
    private uniffi.benchffi.UCounter _counter = null!;

    [GlobalSetup]
    public void Setup()
    {
        _ctx = CsbNative.csb_ctx_create();
        _counter = new uniffi.benchffi.UCounter();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        CsbNative.csb_ctx_destroy(_ctx);
        _counter.Dispose();
    }

    [Benchmark(Baseline = true)]
    public ulong Csbindgen() => CsbNative.csb_ctx_add(_ctx, 1);

    [Benchmark]
    public ulong Uniffi() => _counter.Add(1);
}
