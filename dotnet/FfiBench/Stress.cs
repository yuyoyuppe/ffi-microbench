using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using CsBindgen;
using SpanApi = uniffi.benchffi_span.BenchffiMethods;
using Stock = uniffi.benchffi.BenchffiMethods;

namespace FfiBench;

/// <summary>
/// Multi-threaded stability/leak harness. Hammers every FFI surface with a mixed
/// workload (including non-ASCII strings — smoke uses ASCII only), churns object
/// lifetimes, fires callbacks concurrently, and exercises the exception path;
/// verifies checksums on every call and fails on managed/native memory drift.
/// Like smoke, the stock and span flavors must run in separate processes
/// (--stress / --stress-span).
/// </summary>
internal static unsafe class Stress
{
    private const int ManagedDriftLimitMb = 16;
    private const int PrivateDriftLimitMb = 48;

    // checksum_str(0, s) folds to the UTF-8 byte length; lists fold acc*31 + len.
    private static readonly string[] Payload = BuildPayloads();
    private static readonly byte[][] PayloadUtf8;
    private static readonly ulong[] PayloadChecksum;

    private static long _scalarCallbacks;
    private static long _stringCallbackChars;
    private static long _totalOps;
    private static readonly List<string> Failures = new();
    private static readonly object FailLock = new();

    static Stress()
    {
        PayloadUtf8 = Payload.Select(Encoding.UTF8.GetBytes).ToArray();
        PayloadChecksum = Payload.Select(s => (ulong)Encoding.UTF8.GetByteCount(s)).ToArray();
    }

    private static string[] BuildPayloads()
    {
        const string mixed = "héllo wörld 🚀 日本語テキスト clipboard payload — ØÆ𝄞\n";
        var sb = new StringBuilder();
        var result = new List<string> { "x", "ascii-only-payload", mixed };
        while (sb.Length < 64 * 1024)
        {
            sb.Append(mixed);
        }
        result.Add(sb.ToString(0, 1000));
        result.Add(sb.ToString());
        return result.ToArray();
    }

    private static void Fail(string what)
    {
        lock (FailLock)
        {
            Failures.Add(what);
        }
    }

    private static void Check(bool cond, string what)
    {
        if (!cond)
        {
            Fail(what);
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void StressOnTrigger(uint id) => Interlocked.Increment(ref _scalarCallbacks);

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void StressOnLog(ushort* ptr, int len)
    {
        var s = new string(new ReadOnlySpan<char>((char*)ptr, len));
        Interlocked.Add(ref _stringCallbackChars, s.Length);
    }

    private sealed class StressScalarCb : uniffi.benchffi.ScalarCallback
    {
        public void OnTrigger(uint id) => Interlocked.Increment(ref _scalarCallbacks);
    }

    private sealed class StressStringCb : uniffi.benchffi.StringCallback
    {
        public void OnLog(string msg) => Interlocked.Add(ref _stringCallbackChars, msg.Length);
    }

    private sealed class StressSpanScalarCb : uniffi.benchffi_span.ScalarCallback
    {
        public void OnTrigger(uint id) => Interlocked.Increment(ref _scalarCallbacks);
    }

    private sealed class StressSpanStringCb : uniffi.benchffi_span.StringCallback
    {
        public void OnLog(string msg) => Interlocked.Add(ref _stringCallbackChars, msg.Length);
    }

    public static bool RunStock(int seconds) =>
        Run("stock+csbindgen", seconds, StockIteration, () =>
        {
            CsbNative.csb_register_scalar_callback(&StressOnTrigger);
            CsbNative.csb_register_string_callback(&StressOnLog);
            Stock.URegisterScalarCallback(new StressScalarCb());
            Stock.URegisterStringCallback(new StressStringCb());
        });

    public static bool RunSpan(int seconds) =>
        Run("span", seconds, SpanIteration, () =>
        {
            SpanApi.URegisterScalarCallback(new StressSpanScalarCb());
            SpanApi.URegisterStringCallback(new StressSpanStringCb());
        });

    private static void StockIteration(int op, int tid)
    {
        int p = op % Payload.Length;
        switch (op % 11)
        {
            case 0: // string in, both surfaces, non-ASCII included
                Check(Stock.UTakeString(Payload[p]) == PayloadChecksum[p], "UTakeString checksum");
                fixed (char* ptr = Payload[p])
                {
                    Check(CsbNative.csb_take_string_utf16((ushort*)ptr, Payload[p].Length) == PayloadChecksum[p],
                        "csb_take_string_utf16 checksum");
                }
                break;
            case 1: // string out + content equality
                var s = Stock.UGiveString(512);
                var cptr = CsbNative.csb_give_string_utf8(512);
                var cs = Marshal.PtrToStringUTF8((IntPtr)cptr);
                CsbNative.csb_dealloc_string(cptr);
                Check(s == cs && s.Length == 512, "give_string equality");
                break;
            case 2: // bytes roundtrip
                var data = Payloads.Bytes(2048);
                Check(Stock.UTakeBytes(data) == (2048uL ^ data[^1]), "UTakeBytes checksum");
                Check(Stock.UGiveBytes(2048).AsSpan().SequenceEqual(data), "UGiveBytes content");
                break;
            case 3: // object lifecycle churn + method
                using (var counter = new uniffi.benchffi.UCounter())
                {
                    Check(counter.Add(41) + counter.Add(1) >= 42, "UCounter.Add");
                }
                break;
            case 4: // sequences
                var list = Payloads.StringList(64);
                ulong expected = 0;
                foreach (var item in list)
                {
                    expected = unchecked(expected * 31 + (ulong)item.Length);
                }
                Check(Stock.UTakeStringList(list) == expected, "UTakeStringList checksum");
                Check(Stock.UGiveStringList(64).Length == 64, "UGiveStringList length");
                break;
            case 5: // records + nested request
                Check(Stock.UTakeRequest(Payloads.UniffiRequest()) != 0, "UTakeRequest");
                Check(Stock.UGiveRecords(16).Length == 16, "UGiveRecords");
                break;
            case 6: // map roundtrip
                Check(Stock.UGiveMap(32).Count == 32, "UGiveMap");
                break;
            case 7: // exception path
                try
                {
                    Stock.UTryOp(true);
                    Fail("UTryOp did not throw");
                }
                catch (uniffi.benchffi.BenchException.Denied)
                {
                }
                Stock.UTryOp(false);
                break;
            case 8: // callbacks under concurrency (registered once in setup)
                Stock.UFireScalar(16);
                Stock.UFireString(4, 200);
                CsbNative.csb_fire_scalar(16);
                CsbNative.csb_fire_string(4, 200);
                break;
            case 9: // JSON incumbent path
                var json = JsonSerializer.SerializeToUtf8Bytes(Payloads.StringList(32));
                fixed (byte* ptr = json)
                {
                    Check(CsbNative.csb_take_string_list_json_utf8(ptr, json.Length) != 0,
                        "csb_take_string_list_json checksum");
                }
                break;
            case 10: // throwing string function, ok + err
                Check(Stock.UTakeStringChecked(Payload[p], false) == PayloadChecksum[p],
                    "UTakeStringChecked checksum");
                try
                {
                    Stock.UTakeStringChecked(Payload[p], true);
                    Fail("UTakeStringChecked did not throw");
                }
                catch (uniffi.benchffi.BenchException.Denied)
                {
                }
                break;
        }
    }

    private static void SpanIteration(int op, int tid)
    {
        int p = op % Payload.Length;
        switch (op % 6)
        {
            case 0: // delegated + direct span string, non-ASCII included
                Check(SpanApi.UTakeString(Payload[p]) == PayloadChecksum[p], "span UTakeString checksum");
                Check(SpanApi.UTakeStringSpan(PayloadUtf8[p]) == PayloadChecksum[p],
                    "UTakeStringSpan checksum");
                break;
            case 1: // span bytes
                var data = Payloads.Bytes(4096);
                Check(SpanApi.UTakeBytesSpan(data) == (4096uL ^ data[^1]), "UTakeBytesSpan checksum");
                break;
            case 2: // span JSON
                var json = JsonSerializer.SerializeToUtf8Bytes(Payloads.StringList(32));
                Check(SpanApi.UTakeStringListJsonSpan(json) != 0, "UTakeStringListJsonSpan");
                break;
            case 3: // throwing span function, ok + err
                Check(SpanApi.UTakeStringCheckedSpan(PayloadUtf8[p], false) == PayloadChecksum[p],
                    "UTakeStringCheckedSpan checksum");
                try
                {
                    SpanApi.UTakeStringCheckedSpan(PayloadUtf8[p], true);
                    Fail("UTakeStringCheckedSpan did not throw");
                }
                catch (uniffi.benchffi_span.BenchException.Denied)
                {
                }
                break;
            case 4: // callbacks through span-flavor bindings
                SpanApi.UFireScalar(16);
                SpanApi.UFireString(4, 200);
                break;
            case 5: // stack-allocated span input (pin path with non-array memory)
                Span<byte> local = stackalloc byte[64];
                PayloadUtf8[1].AsSpan(0, Math.Min(64, PayloadUtf8[1].Length)).CopyTo(local);
                var slice = local.Slice(0, Math.Min(64, PayloadUtf8[1].Length));
                Check(SpanApi.UTakeStringSpan(slice) == (ulong)slice.Length, "stackalloc span checksum");
                break;
        }
    }

    private static bool Run(string phase, int seconds, Action<int, int> iteration, Action setup)
    {
        Console.WriteLine($"stress({phase}): {seconds}s main phase, {Environment.ProcessorCount / 2} threads");
        setup();

        // Warmup to stabilize JIT/pools before the leak baseline.
        RunThreads(TimeSpan.FromSeconds(2), iteration);
        var (managedBefore, privateBefore) = MemorySnapshot();

        RunThreads(TimeSpan.FromSeconds(seconds), iteration);
        var (managedAfter, privateAfter) = MemorySnapshot();

        var managedDriftMb = (managedAfter - managedBefore) / (1024.0 * 1024.0);
        var privateDriftMb = (privateAfter - privateBefore) / (1024.0 * 1024.0);

        Console.WriteLine($"  ops             : {Interlocked.Read(ref _totalOps):N0}");
        Console.WriteLine($"  scalar callbacks: {Interlocked.Read(ref _scalarCallbacks):N0}");
        Console.WriteLine($"  string cb chars : {Interlocked.Read(ref _stringCallbackChars):N0}");
        Console.WriteLine($"  managed drift   : {managedDriftMb:F2} MB (limit {ManagedDriftLimitMb})");
        Console.WriteLine($"  private drift   : {privateDriftMb:F2} MB (limit {PrivateDriftLimitMb})");

        Check(managedDriftMb < ManagedDriftLimitMb, $"managed memory drift {managedDriftMb:F2} MB");
        Check(privateDriftMb < PrivateDriftLimitMb, $"private memory drift {privateDriftMb:F2} MB");
        Check(Interlocked.Read(ref _scalarCallbacks) > 0, "scalar callbacks fired");
        Check(Interlocked.Read(ref _stringCallbackChars) > 0, "string callbacks fired");

        Console.WriteLine(Failures.Count == 0
            ? $"stress({phase}): PASS"
            : $"stress({phase}): {Failures.Count} FAILURES: {string.Join("; ", Failures.Distinct().Take(10))}");
        return Failures.Count == 0;
    }

    private static void RunThreads(TimeSpan duration, Action<int, int> iteration)
    {
        int threadCount = Math.Max(4, Environment.ProcessorCount / 2);
        var deadline = Stopwatch.StartNew();
        var threads = new Thread[threadCount];
        for (int t = 0; t < threadCount; t++)
        {
            int tid = t;
            threads[t] = new Thread(() =>
            {
                int op = tid * 7919; // decorrelate op mix across threads
                while (deadline.Elapsed < duration && Failures.Count == 0)
                {
                    iteration(op++, tid);
                    Interlocked.Increment(ref _totalOps);
                }
            })
            { IsBackground = true };
            threads[t].Start();
        }
        foreach (var thread in threads)
        {
            thread.Join();
        }
    }

    private static (long managed, long privateBytes) MemorySnapshot()
    {
        for (int i = 0; i < 3; i++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
        var managed = GC.GetTotalMemory(forceFullCollection: true);
        using var proc = Process.GetCurrentProcess();
        proc.Refresh();
        return (managed, proc.PrivateMemorySize64);
    }
}
