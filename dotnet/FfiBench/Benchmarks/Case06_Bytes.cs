using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using CsBindgen;
using SpanApi = uniffi.benchffi_span.BenchffiMethods;
using Stock = uniffi.benchffi.BenchffiMethods;

namespace FfiBench.Benchmarks;

/// <summary>
/// Case 6: byte buffers (clipboard images). Should be ~memcpy bound; verifies
/// uniffi doesn't add extra copies. Both give-paths materialize a managed byte[].
/// </summary>
public unsafe class Case06_Bytes
{
    [Params(1024, 1048576, 16777216)]
    public int Size;

    private byte[] _payload = Array.Empty<byte>();

    [GlobalSetup]
    public void Setup() => _payload = Payloads.Bytes(Size);

    [Benchmark(Baseline = true)]
    public ulong CsbindgenTake()
    {
        fixed (byte* p = _payload)
        {
            return CsbNative.csb_take_bytes(p, _payload.Length);
        }
    }

    [Benchmark]
    public ulong UniffiTake() => Stock.UTakeBytes(_payload);

    [Benchmark]
    public ulong UniffiSpanTake() => SpanApi.UTakeBytesSpan(_payload);

    [Benchmark]
    public int CsbindgenGive()
    {
        int len;
        var ptr = CsbNative.csb_give_bytes((uint)Size, &len);
        var managed = new byte[len];
        Marshal.Copy((IntPtr)ptr, managed, 0, len);
        CsbNative.csb_dealloc_bytes(ptr, len);
        return managed.Length;
    }

    [Benchmark]
    public int UniffiGive() => Stock.UGiveBytes((uint)Size).Length;
}
