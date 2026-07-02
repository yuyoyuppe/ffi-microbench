using BenchmarkDotNet.Attributes;
using CsBindgen;
using Stock = uniffi.benchffi.BenchffiMethods;

namespace FfiBench.Benchmarks;

/// <summary>
/// Case 12: Rust→C# callback with ~200 B string arg (LogCallback /
/// RecordingEventCallback). csbindgen: fresh UTF-16 encode in Rust, C# materializes
/// via new string(span). uniffi: String clone into RustBuffer + vtable dispatch +
/// UTF-8 decode. This is where vtable dispatch and serialization stack.
/// </summary>
public class Case12_CallbackString
{
    private const int FireCount = 10_000;
    private const uint MsgLen = 200;

    [GlobalSetup(Target = nameof(Csbindgen))]
    public void SetupCsb() => CsbCallbacks.RegisterString();

    [GlobalSetup(Target = nameof(Uniffi))]
    public void SetupUniffi() => Stock.URegisterStringCallback(new UniffiStringCallback());

    [Benchmark(Baseline = true, OperationsPerInvoke = FireCount)]
    public void Csbindgen() => CsbNative.csb_fire_string(FireCount, MsgLen);

    [Benchmark(OperationsPerInvoke = FireCount)]
    public void Uniffi() => Stock.UFireString(FireCount, MsgLen);
}

/// <summary>Single-shot latency for the string callback.</summary>
public class Case12_CallbackStringSingleShot
{
    private const uint MsgLen = 200;

    [GlobalSetup(Target = nameof(Csbindgen))]
    public void SetupCsb() => CsbCallbacks.RegisterString();

    [GlobalSetup(Target = nameof(Uniffi))]
    public void SetupUniffi() => Stock.URegisterStringCallback(new UniffiStringCallback());

    [Benchmark(Baseline = true)]
    public void Csbindgen() => CsbNative.csb_fire_string(1, MsgLen);

    [Benchmark]
    public void Uniffi() => Stock.UFireString(1, MsgLen);
}
