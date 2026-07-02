using System.Runtime.InteropServices;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using CsBindgen;
using Stock = uniffi.benchffi.BenchffiMethods;

namespace FfiBench.Benchmarks;

/// <summary>Case 10: HashMap&lt;String, String&gt; vs JSON object incumbent.</summary>
public unsafe class Case10_HashMap
{
    [Params(100, 10000)]
    public int Count;

    private Dictionary<string, string> _map = null!;

    [GlobalSetup]
    public void Setup() => _map = Payloads.Map(Count);

    [Benchmark(Baseline = true)]
    public ulong CsbindgenJsonTake()
    {
        var jsonUtf8 = JsonSerializer.SerializeToUtf8Bytes(_map);
        fixed (byte* p = jsonUtf8)
        {
            return CsbNative.csb_take_map_json_utf8(p, jsonUtf8.Length);
        }
    }

    [Benchmark]
    public ulong UniffiTake() => Stock.UTakeMap(_map);

    [Benchmark]
    public int CsbindgenJsonGive()
    {
        var ptr = CsbNative.csb_give_map_json_utf8((uint)Count);
        var map = JsonSerializer.Deserialize<Dictionary<string, string>>(
            Marshal.PtrToStringUTF8((IntPtr)ptr)!)!;
        CsbNative.csb_dealloc_string(ptr);
        return map.Count;
    }

    [Benchmark]
    public int UniffiGive() => Stock.UGiveMap((uint)Count).Count;
}
