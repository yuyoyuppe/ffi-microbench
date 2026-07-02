using BenchmarkDotNet.Attributes;
using CsBindgen;
using Stock = uniffi.benchffi.BenchffiMethods;

namespace FfiBench.Benchmarks;

/// <summary>
/// Case 11: Rust→C# callback with scalar arg (BindingTriggerCallback). Rust fires
/// the registered callback in a tight loop; per-invocation cost reported via
/// OperationsPerInvoke. Targeted setups keep each child process flavor-pure.
/// </summary>
public class Case11_CallbackScalar
{
    private const int FireCount = 10_000;

    [GlobalSetup(Target = nameof(Csbindgen))]
    public void SetupCsb() => CsbCallbacks.RegisterScalar();

    [GlobalSetup(Target = nameof(Uniffi))]
    public void SetupUniffi() => Stock.URegisterScalarCallback(new UniffiScalarCallback());

    [Benchmark(Baseline = true, OperationsPerInvoke = FireCount)]
    public void Csbindgen() => CsbNative.csb_fire_scalar(FireCount);

    [Benchmark(OperationsPerInvoke = FireCount)]
    public void Uniffi() => Stock.UFireScalar(FireCount);
}

/// <summary>Single-shot latency: one FFI entry + one callback invocation
/// (the cold BindingTriggerCallback activation path).</summary>
public class Case11_CallbackScalarSingleShot
{
    [GlobalSetup(Target = nameof(Csbindgen))]
    public void SetupCsb() => CsbCallbacks.RegisterScalar();

    [GlobalSetup(Target = nameof(Uniffi))]
    public void SetupUniffi() => Stock.URegisterScalarCallback(new UniffiScalarCallback());

    [Benchmark(Baseline = true)]
    public void Csbindgen() => CsbNative.csb_fire_scalar(1);

    [Benchmark]
    public void Uniffi() => Stock.UFireScalar(1);
}
