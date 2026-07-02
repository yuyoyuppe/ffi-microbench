using System.Runtime.InteropServices;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using CsBindgen;
using SpanApi = uniffi.benchffi_span.BenchffiMethods;
using Stock = uniffi.benchffi.BenchffiMethods;

namespace FfiBench.Benchmarks;

/// <summary>
/// Case 7: Vec&lt;String&gt; (hotkey/snippet registration, file lists), ~16 B elements.
/// Incumbents: pointer-array of pinned UTF-16 strings (hotkeys today) and a single
/// JSON payload (clipboard/snippets today). uniffi: per-element length-prefixed
/// RustBuffer encode. Serialization is measured where it is part of the crossing.
/// </summary>
public unsafe class Case07_VecString
{
    [Params(10, 1000, 100000)]
    public int Count;

    private string[] _list = Array.Empty<string>();

    [GlobalSetup]
    public void Setup() => _list = Payloads.StringList(Count);

    [Benchmark(Baseline = true)]
    public ulong CsbindgenPtrArray()
    {
        var handles = new GCHandle[_list.Length];
        var ptrs = new ushort*[_list.Length];
        var lens = new int[_list.Length];
        for (int i = 0; i < _list.Length; i++)
        {
            handles[i] = GCHandle.Alloc(_list[i], GCHandleType.Pinned);
            ptrs[i] = (ushort*)handles[i].AddrOfPinnedObject();
            lens[i] = _list[i].Length;
        }
        ulong result;
        fixed (ushort** pp = ptrs)
        fixed (int* pl = lens)
        {
            result = CsbNative.csb_take_string_array_utf16(pp, pl, _list.Length);
        }
        for (int i = 0; i < handles.Length; i++)
        {
            handles[i].Free();
        }
        return result;
    }

    [Benchmark]
    public ulong CsbindgenJson()
    {
        var jsonUtf8 = JsonSerializer.SerializeToUtf8Bytes(_list);
        fixed (byte* p = jsonUtf8)
        {
            return CsbNative.csb_take_string_list_json_utf8(p, jsonUtf8.Length);
        }
    }

    [Benchmark]
    public ulong UniffiSequence() => Stock.UTakeStringList(_list);

    [Benchmark]
    public ulong UniffiJsonString() => Stock.UTakeStringListJson(JsonSerializer.Serialize(_list));

    [Benchmark]
    public ulong UniffiSpanJsonUtf8() =>
        SpanApi.UTakeStringListJsonSpan(JsonSerializer.SerializeToUtf8Bytes(_list));

    // Out direction: uniffi sequence vs incumbent JSON round-trip.
    [Benchmark]
    public int UniffiSequenceOut() => Stock.UGiveStringList((uint)Count).Length;

    [Benchmark]
    public int CsbindgenJsonOut()
    {
        var ptr = CsbNative.csb_give_string_list_json_utf8((uint)Count);
        var list = JsonSerializer.Deserialize<List<string>>(Marshal.PtrToStringUTF8((IntPtr)ptr)!)!;
        CsbNative.csb_dealloc_string(ptr);
        return list.Count;
    }
}
