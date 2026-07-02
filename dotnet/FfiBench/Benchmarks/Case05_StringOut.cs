using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using CsBindgen;
using Stock = uniffi.benchffi.BenchffiMethods;

namespace FfiBench.Benchmarks;

/// <summary>
/// Case 5: string Rust→C#. Incumbent returns an owned null-terminated UTF-8
/// CString consumed via Marshal.PtrToStringUTF8 + explicit free; uniffi returns a
/// RustBuffer lifted to string. Both end in a managed string allocation.
/// </summary>
public unsafe class Case05_StringOut
{
    [Params(16, 1024, 65536, 1048576)]
    public int Size;

    [Benchmark(Baseline = true)]
    public int Csbindgen()
    {
        var ptr = CsbNative.csb_give_string_utf8((uint)Size);
        var s = Marshal.PtrToStringUTF8((IntPtr)ptr)!;
        CsbNative.csb_dealloc_string(ptr);
        return s.Length;
    }

    [Benchmark]
    public int Uniffi() => Stock.UGiveString((uint)Size).Length;
}
