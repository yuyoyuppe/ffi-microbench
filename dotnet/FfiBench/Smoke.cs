using System.Runtime.InteropServices;
using System.Text.Json;
using CsBindgen;
using Stock = uniffi.benchffi.BenchffiMethods;
using SpanApi = uniffi.benchffi_span.BenchffiMethods;

namespace FfiBench;

/// <summary>
/// Runs every operation once on every surface and cross-checks results, so a broken
/// binding (wrong _raw symbol, marshalling bug, JSON shape mismatch) fails loudly
/// before any timing numbers are produced.
/// </summary>
internal static unsafe class Smoke
{
    private static readonly List<string> Failures = new();

    private static void Check(bool cond, string name)
    {
        Console.WriteLine($"  [{(cond ? "PASS" : "FAIL")}] {name}");
        if (!cond)
        {
            Failures.Add(name);
        }
    }

    private static bool Summarize(string phase)
    {
        Console.WriteLine(Failures.Count == 0
            ? $"smoke({phase}): ALL PASS"
            : $"smoke({phase}): {Failures.Count} FAILURES: {string.Join(", ", Failures)}");
        return Failures.Count == 0;
    }

    // The two phases must run in separate processes: each flavor's _UniFFILib
    // static ctor registers the callback vtables for the same Rust-side slots, and
    // freeing a callback lowered under the previous flavor's vtable corrupts the
    // current flavor's handle map. BDN benchmarks are immune (one process per
    // benchmark, flavor-pure by construction); the entry script runs both phases.
    public static bool RunStockAndCsbindgen()
    {
        Console.WriteLine("smoke: csbindgen + stock uniffi bindings");
        // Case 1/2: nop + scalars
        CsbNative.csb_nop();
        Stock.UNop();
        Check(CsbNative.csb_add(2, 3) == 5, "csb_add");
        Check(Stock.UAdd(2, 3) == 5, "uniffi UAdd");

        // Case 3: object method / context pointer
        var ctx = CsbNative.csb_ctx_create();
        Check(CsbNative.csb_ctx_add(ctx, 7) == 7, "csb_ctx_add");
        CsbNative.csb_ctx_destroy(ctx);
        using (var counter = new uniffi.benchffi.UCounter())
        {
            Check(counter.Add(7) == 7, "uniffi UCounter.Add");
        }

        // Case 4: string in — all four variants must agree
        foreach (var n in new[] { 16, 1024, 65536 })
        {
            var s = Payloads.AsciiString(n);
            ulong expected = Payloads.Checksum(0, s);
            ulong csb;
            fixed (char* p = s)
            {
                csb = CsbNative.csb_take_string_utf16((ushort*)p, s.Length);
            }
            Check(csb == expected, $"csb_take_string_utf16({n})");
            Check(Stock.UTakeString(s) == expected, $"uniffi UTakeString({n})");
        }

        // Case 5: string out
        {
            var expected = Payloads.AsciiString(1024);
            var ptr = CsbNative.csb_give_string_utf8(1024);
            var fromCsb = Marshal.PtrToStringUTF8((IntPtr)ptr);
            CsbNative.csb_dealloc_string(ptr);
            Check(fromCsb == expected, "csb_give_string_utf8");
            Check(Stock.UGiveString(1024) == expected, "uniffi UGiveString");
        }

        // Case 6: bytes
        {
            var data = Payloads.Bytes(4096);
            ulong expected = 4096uL ^ data[^1];
            ulong csb;
            fixed (byte* p = data)
            {
                csb = CsbNative.csb_take_bytes(p, data.Length);
            }
            Check(csb == expected, "csb_take_bytes");
            Check(Stock.UTakeBytes(data) == expected, "uniffi UTakeBytes");

            int outLen;
            var bptr = CsbNative.csb_give_bytes(4096, &outLen);
            var fromCsb = new byte[outLen];
            Marshal.Copy((IntPtr)bptr, fromCsb, 0, outLen);
            CsbNative.csb_dealloc_bytes(bptr, outLen);
            Check(fromCsb.AsSpan().SequenceEqual(data), "csb_give_bytes");
            Check(Stock.UGiveBytes(4096).AsSpan().SequenceEqual(data), "uniffi UGiveBytes");
        }

        // Case 7: Vec<String> — pointer-array, JSON incumbents, uniffi sequence, span JSON
        {
            var list = Payloads.StringList(1000);
            ulong expected = 0;
            foreach (var s in list)
            {
                expected = Payloads.Checksum(expected, s);
            }

            var handles = new GCHandle[list.Length];
            var ptrs = new ushort*[list.Length];
            var lens = new int[list.Length];
            for (int i = 0; i < list.Length; i++)
            {
                handles[i] = GCHandle.Alloc(list[i], GCHandleType.Pinned);
                ptrs[i] = (ushort*)handles[i].AddrOfPinnedObject();
                lens[i] = list[i].Length;
            }
            ulong csbArr;
            fixed (ushort** pp = ptrs)
            fixed (int* pl = lens)
            {
                csbArr = CsbNative.csb_take_string_array_utf16(pp, pl, list.Length);
            }
            foreach (var h in handles)
            {
                h.Free();
            }
            Check(csbArr == expected, "csb_take_string_array_utf16");

            var jsonUtf8 = JsonSerializer.SerializeToUtf8Bytes(list);
            fixed (byte* p = jsonUtf8)
            {
                Check(CsbNative.csb_take_string_list_json_utf8(p, jsonUtf8.Length) == expected,
                    "csb_take_string_list_json_utf8");
            }
            Check(Stock.UTakeStringList(list) == expected, "uniffi UTakeStringList");
            Check(Stock.UTakeStringListJson(JsonSerializer.Serialize(list)) == expected,
                "uniffi UTakeStringListJson");

            var fromUniffi = Stock.UGiveStringList(1000);
            Check(fromUniffi.SequenceEqual(list), "uniffi UGiveStringList");
            var jptr = CsbNative.csb_give_string_list_json_utf8(1000);
            var fromCsb = JsonSerializer.Deserialize<List<string>>(Marshal.PtrToStringUTF8((IntPtr)jptr)!);
            CsbNative.csb_dealloc_string(jptr);
            Check(fromCsb!.SequenceEqual(list), "csb_give_string_list_json_utf8");
        }

        // Case 8: Vec<Record>
        {
            var dtos = Payloads.RecordDtos(1000);
            var records = Payloads.UniffiRecords(1000);
            ulong expected = 0;
            foreach (var r in dtos)
            {
                expected = unchecked(Payloads.Checksum(expected, r.Title) + r.Id);
            }
            var jsonUtf8 = JsonSerializer.SerializeToUtf8Bytes(dtos, Payloads.JsonOpts);
            fixed (byte* p = jsonUtf8)
            {
                Check(CsbNative.csb_take_records_json_utf8(p, jsonUtf8.Length) == expected,
                    "csb_take_records_json_utf8");
            }
            Check(Stock.UTakeRecords(records) == expected, "uniffi UTakeRecords");

            var fromUniffi = Stock.UGiveRecords(1000);
            Check(fromUniffi.Length == 1000 && fromUniffi[999].Title == "Window Title 999",
                "uniffi UGiveRecords");
            var jptr = CsbNative.csb_give_records_json_utf8(1000);
            var fromCsb = JsonSerializer.Deserialize<List<RecordDto>>(
                Marshal.PtrToStringUTF8((IntPtr)jptr)!, Payloads.JsonOpts);
            CsbNative.csb_dealloc_string(jptr);
            Check(fromCsb!.Count == 1000 && fromCsb[999].Title == "Window Title 999",
                "csb_give_records_json_utf8");
        }

        // Case 9: nested/optional record
        {
            var dto = Payloads.RequestDto();
            var jsonUtf8 = JsonSerializer.SerializeToUtf8Bytes(dto, Payloads.JsonOpts);
            ulong csb;
            fixed (byte* p = jsonUtf8)
            {
                csb = CsbNative.csb_take_request_json_utf8(p, jsonUtf8.Length);
            }
            ulong uni = Stock.UTakeRequest(Payloads.UniffiRequest());
            Check(csb == uni && csb != 0, "take_request csb == uniffi");

            var fromUniffi = Stock.UGiveRequest();
            var jptr = CsbNative.csb_give_request_json_utf8();
            var fromCsb = JsonSerializer.Deserialize<WindowRequestDto>(
                Marshal.PtrToStringUTF8((IntPtr)jptr)!, Payloads.JsonOpts);
            CsbNative.csb_dealloc_string(jptr);
            Check(fromUniffi.Title == fromCsb!.Title && fromUniffi.Pwa?.AppId == fromCsb.Pwa?.AppId,
                "give_request csb == uniffi");
        }

        // Case 10: HashMap
        {
            var map = Payloads.Map(100);
            var jsonUtf8 = JsonSerializer.SerializeToUtf8Bytes(map);
            ulong csb;
            fixed (byte* p = jsonUtf8)
            {
                csb = CsbNative.csb_take_map_json_utf8(p, jsonUtf8.Length);
            }
            Check(csb == Stock.UTakeMap(map) && csb != 0, "take_map csb == uniffi");
            Check(Stock.UGiveMap(100).Count == 100, "uniffi UGiveMap");
            var jptr = CsbNative.csb_give_map_json_utf8(100);
            var fromCsb = JsonSerializer.Deserialize<Dictionary<string, string>>(
                Marshal.PtrToStringUTF8((IntPtr)jptr)!);
            CsbNative.csb_dealloc_string(jptr);
            Check(fromCsb!.Count == 100, "csb_give_map_json_utf8");
        }

        // Cases 11/12: callbacks
        {
            CsbCallbacks.RegisterScalar();
            CsbCallbacks.RegisterString();
            var before = CallbackCounters.ScalarCalls;
            CsbNative.csb_fire_scalar(5);
            Check(CallbackCounters.ScalarCalls == before + 5, "csb_fire_scalar");
            var charsBefore = CallbackCounters.StringChars;
            CsbNative.csb_fire_string(3, 200);
            Check(CallbackCounters.StringChars == charsBefore + 600, "csb_fire_string");

            Stock.URegisterScalarCallback(new UniffiScalarCallback());
            Stock.URegisterStringCallback(new UniffiStringCallback());
            before = CallbackCounters.ScalarCalls;
            Stock.UFireScalar(5);
            Check(CallbackCounters.ScalarCalls == before + 5, "uniffi UFireScalar");
            charsBefore = CallbackCounters.StringChars;
            Stock.UFireString(3, 200);
            Check(CallbackCounters.StringChars == charsBefore + 600, "uniffi UFireString");
        }

        // Case 13: error path
        {
            Check(CsbNative.csb_try_op(false), "csb_try_op ok");
            Check(!CsbNative.csb_try_op(true), "csb_try_op err");
            byte* err = null;
            Check(!CsbNative.csb_try_op_with_msg(true, &err) && err != null, "csb_try_op_with_msg err");
            var msg = Marshal.PtrToStringUTF8((IntPtr)err);
            CsbNative.csb_dealloc_string(err);
            Check(msg == "access denied", "csb error message");

            var threw = false;
            Stock.UTryOp(false);
            try
            {
                Stock.UTryOp(true);
            }
            catch (uniffi.benchffi.BenchException.Denied)
            {
                threw = true;
            }
            Check(threw, "uniffi UTryOp throws Denied");

            var s = Payloads.AsciiString(64);
            Check(Stock.UTakeStringChecked(s, false) == Payloads.Checksum(0, s),
                "uniffi UTakeStringChecked ok");
            var threwChecked = false;
            try
            {
                Stock.UTakeStringChecked(s, true);
            }
            catch (uniffi.benchffi.BenchException.Denied)
            {
                threwChecked = true;
            }
            Check(threwChecked, "uniffi UTakeStringChecked throws Denied");
        }

        return Summarize("stock");
    }

    /// <summary>Span-flavor checks — run in a dedicated process (see above).</summary>
    public static bool RunSpanFlavor()
    {
        Console.WriteLine("smoke: span-flavor uniffi bindings (high_performance_strings)");
        SpanApi.UNop();

        foreach (var n in new[] { 16, 1024, 65536 })
        {
            var s = Payloads.AsciiString(n);
            var utf8 = Payloads.AsciiStringUtf8(n);
            ulong expected = Payloads.Checksum(0, s);
            Check(SpanApi.UTakeString(s) == expected, $"span-delegated UTakeString({n})");
            Check(SpanApi.UTakeStringSpan(utf8) == expected, $"UTakeStringSpan({n})");
        }

        {
            var list = Payloads.StringList(1000);
            ulong expected = 0;
            foreach (var s in list)
            {
                expected = Payloads.Checksum(expected, s);
            }
            var jsonUtf8 = JsonSerializer.SerializeToUtf8Bytes(list);
            Check(SpanApi.UTakeStringListJsonSpan(jsonUtf8) == expected,
                "UTakeStringListJsonSpan");
        }

        // Throwing string function: exercises the *_raw error path (status populated
        // by uniffi::rust_call in api_raw.rs, lifted by CheckCallStatus in C#).
        {
            var s = Payloads.AsciiString(64);
            var utf8 = Payloads.AsciiStringUtf8(64);
            ulong expected = Payloads.Checksum(0, s);
            Check(SpanApi.UTakeStringChecked(s, false) == expected,
                "span-delegated UTakeStringChecked ok");
            Check(SpanApi.UTakeStringCheckedSpan(utf8, false) == expected,
                "UTakeStringCheckedSpan ok");
            var threw = false;
            try
            {
                SpanApi.UTakeStringCheckedSpan(utf8, true);
            }
            catch (uniffi.benchffi_span.BenchException.Denied)
            {
                threw = true;
            }
            Check(threw, "UTakeStringCheckedSpan throws Denied");
        }

        // Callbacks through the span-flavor bindings (vtable now owned by this flavor).
        {
            SpanApi.URegisterScalarCallback(new UniffiSpanScalarCallback());
            SpanApi.URegisterStringCallback(new UniffiSpanStringCallback());
            var before = CallbackCounters.ScalarCalls;
            SpanApi.UFireScalar(5);
            Check(CallbackCounters.ScalarCalls == before + 5, "span uniffi UFireScalar");
            var charsBefore = CallbackCounters.StringChars;
            SpanApi.UFireString(3, 200);
            Check(CallbackCounters.StringChars == charsBefore + 600, "span uniffi UFireString");
        }

        return Summarize("span");
    }
}
