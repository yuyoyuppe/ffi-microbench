using System.Runtime.InteropServices;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using CsBindgen;
using Stock = uniffi.benchffi.BenchffiMethods;

namespace FfiBench.Benchmarks;

/// <summary>
/// Case 9: single nested/optional record (AppWindowRequest + PwaInfo shape) —
/// branch-heavy lift/lower vs a small JSON document.
/// </summary>
public unsafe class Case09_NestedRecord
{
    private WindowRequestDto _dto = null!;
    private uniffi.benchffi.WindowRequest _request = null!;

    [GlobalSetup]
    public void Setup()
    {
        _dto = Payloads.RequestDto();
        _request = Payloads.UniffiRequest();
    }

    [Benchmark(Baseline = true)]
    public ulong CsbindgenJsonTake()
    {
        var jsonUtf8 = JsonSerializer.SerializeToUtf8Bytes(_dto, Payloads.JsonOpts);
        fixed (byte* p = jsonUtf8)
        {
            return CsbNative.csb_take_request_json_utf8(p, jsonUtf8.Length);
        }
    }

    [Benchmark]
    public ulong UniffiTake() => Stock.UTakeRequest(_request);

    [Benchmark]
    public int CsbindgenJsonGive()
    {
        var ptr = CsbNative.csb_give_request_json_utf8();
        var dto = JsonSerializer.Deserialize<WindowRequestDto>(
            Marshal.PtrToStringUTF8((IntPtr)ptr)!, Payloads.JsonOpts)!;
        CsbNative.csb_dealloc_string(ptr);
        return dto.WindowIds.Length;
    }

    [Benchmark]
    public int UniffiGive() => Stock.UGiveRequest().WindowIds.Length;
}
