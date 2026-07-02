using BenchmarkDotNet.Attributes;
using CsBindgen;
using SpanApi = uniffi.benchffi_span.BenchffiMethods;
using Stock = uniffi.benchffi.BenchffiMethods;

namespace FfiBench.Benchmarks;

/// <summary>
/// Case 4: string C#→Rust. Incumbent passes UTF-16 ptr+len (Rust decodes via
/// from_utf16_lossy); stock uniffi re-encodes UTF-16→UTF-8 into a RustBuffer;
/// the fork's span path passes UTF-8 ptr+len directly (borrowed, zero-copy).
/// UniffiSpanUtf8 is the best case where the caller already holds UTF-8 bytes
/// (e.g. output of JsonSerializer.SerializeToUtf8Bytes).
/// </summary>
public unsafe class Case04_StringIn
{
    [Params(16, 1024, 65536, 1048576)]
    public int Size;

    private string _payload = "";
    private byte[] _payloadUtf8 = Array.Empty<byte>();

    [GlobalSetup]
    public void Setup()
    {
        _payload = Payloads.AsciiString(Size);
        _payloadUtf8 = Payloads.AsciiStringUtf8(Size);
    }

    [Benchmark(Baseline = true)]
    public ulong Csbindgen()
    {
        fixed (char* p = _payload)
        {
            return CsbNative.csb_take_string_utf16((ushort*)p, _payload.Length);
        }
    }

    [Benchmark]
    public ulong Uniffi() => Stock.UTakeString(_payload);

    [Benchmark]
    public ulong UniffiSpanFromString() => SpanApi.UTakeString(_payload);

    [Benchmark]
    public ulong UniffiSpanUtf8() => SpanApi.UTakeStringSpan(_payloadUtf8);
}
