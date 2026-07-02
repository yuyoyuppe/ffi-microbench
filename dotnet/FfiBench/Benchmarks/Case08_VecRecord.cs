using System.Runtime.InteropServices;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using CsBindgen;
using Stock = uniffi.benchffi.BenchffiMethods;

namespace FfiBench.Benchmarks;

/// <summary>
/// Case 8: Vec&lt;Record&gt; of 1000 records with ~6 mixed fields (AppWindowRequest-like).
/// Incumbent crosses as JSON (System.Text.Json ↔ serde_json); uniffi lowers/lifts
/// each record field-by-field through RustBuffer.
/// </summary>
public unsafe class Case08_VecRecord
{
    private const int Count = 1000;

    private List<RecordDto> _dtos = null!;
    private uniffi.benchffi.BenchRecord[] _records = null!;

    [GlobalSetup]
    public void Setup()
    {
        _dtos = Payloads.RecordDtos(Count);
        _records = Payloads.UniffiRecords(Count);
    }

    [Benchmark(Baseline = true)]
    public ulong CsbindgenJsonTake()
    {
        var jsonUtf8 = JsonSerializer.SerializeToUtf8Bytes(_dtos, Payloads.JsonOpts);
        fixed (byte* p = jsonUtf8)
        {
            return CsbNative.csb_take_records_json_utf8(p, jsonUtf8.Length);
        }
    }

    [Benchmark]
    public ulong UniffiTake() => Stock.UTakeRecords(_records);

    [Benchmark]
    public int CsbindgenJsonGive()
    {
        var ptr = CsbNative.csb_give_records_json_utf8(Count);
        var list = JsonSerializer.Deserialize<List<RecordDto>>(
            Marshal.PtrToStringUTF8((IntPtr)ptr)!, Payloads.JsonOpts)!;
        CsbNative.csb_dealloc_string(ptr);
        return list.Count;
    }

    [Benchmark]
    public int UniffiGive() => Stock.UGiveRecords(Count).Length;
}
