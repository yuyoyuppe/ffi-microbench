using BenchmarkDotNet.Attributes;
using CsBindgen;
using SpanApi = uniffi.benchffi_span.BenchffiMethods;
using Stock = uniffi.benchffi.BenchffiMethods;

namespace FfiBench.Benchmarks;

/// <summary>
/// Case 14: cases 2 and 4 (1 KiB) from 4 CLR threads simultaneously — handle-map /
/// GC contention. _1T variants use the identical loop shape on one thread so the
/// cross-thread degradation ratio isolates contention from loop overhead.
/// </summary>
public unsafe class Case14_Concurrency
{
    private const int Threads = 4;
    private const int ScalarIters = 100_000;
    private const int StringIters = 5_000;

    private string _payload = "";
    private byte[] _payloadUtf8 = Array.Empty<byte>();
    private Action[] _csbAdd = null!;
    private Action[] _uniAdd = null!;
    private Action[] _csbString = null!;
    private Action[] _uniString = null!;
    private Action[] _spanString = null!;

    [GlobalSetup]
    public void Setup()
    {
        _payload = Payloads.AsciiString(1024);
        _payloadUtf8 = Payloads.AsciiStringUtf8(1024);
        _csbAdd = MakeActions(CsbAddLoop);
        _uniAdd = MakeActions(UniAddLoop);
        _csbString = MakeActions(CsbStringLoop);
        _uniString = MakeActions(UniStringLoop);
        _spanString = MakeActions(SpanStringLoop);
    }

    private static Action[] MakeActions(Action body)
    {
        var actions = new Action[Threads];
        Array.Fill(actions, body);
        return actions;
    }

    private void CsbAddLoop()
    {
        for (int i = 0; i < ScalarIters; i++)
        {
            CsbNative.csb_add((ulong)i, 1);
        }
    }

    private void UniAddLoop()
    {
        for (int i = 0; i < ScalarIters; i++)
        {
            Stock.UAdd((ulong)i, 1);
        }
    }

    private void CsbStringLoop()
    {
        for (int i = 0; i < StringIters; i++)
        {
            fixed (char* p = _payload)
            {
                CsbNative.csb_take_string_utf16((ushort*)p, _payload.Length);
            }
        }
    }

    private void UniStringLoop()
    {
        for (int i = 0; i < StringIters; i++)
        {
            Stock.UTakeString(_payload);
        }
    }

    private void SpanStringLoop()
    {
        for (int i = 0; i < StringIters; i++)
        {
            SpanApi.UTakeStringSpan(_payloadUtf8);
        }
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = ScalarIters)]
    public void CsbindgenAdd1T() => CsbAddLoop();

    [Benchmark(OperationsPerInvoke = Threads * ScalarIters)]
    public void CsbindgenAdd4T() => Parallel.Invoke(_csbAdd);

    [Benchmark(OperationsPerInvoke = ScalarIters)]
    public void UniffiAdd1T() => UniAddLoop();

    [Benchmark(OperationsPerInvoke = Threads * ScalarIters)]
    public void UniffiAdd4T() => Parallel.Invoke(_uniAdd);

    [Benchmark(OperationsPerInvoke = StringIters)]
    public void CsbindgenString1K1T() => CsbStringLoop();

    [Benchmark(OperationsPerInvoke = Threads * StringIters)]
    public void CsbindgenString1K4T() => Parallel.Invoke(_csbString);

    [Benchmark(OperationsPerInvoke = StringIters)]
    public void UniffiString1K1T() => UniStringLoop();

    [Benchmark(OperationsPerInvoke = Threads * StringIters)]
    public void UniffiString1K4T() => Parallel.Invoke(_uniString);

    [Benchmark(OperationsPerInvoke = StringIters)]
    public void UniffiSpanString1K1T() => SpanStringLoop();

    [Benchmark(OperationsPerInvoke = Threads * StringIters)]
    public void UniffiSpanString1K4T() => Parallel.Invoke(_spanString);
}
