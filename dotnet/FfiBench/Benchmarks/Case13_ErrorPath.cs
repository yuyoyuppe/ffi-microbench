using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using CsBindgen;
using Stock = uniffi.benchffi.BenchffiMethods;

namespace FfiBench.Benchmarks;

/// <summary>
/// Case 13: error path. Incumbent returns bool (optionally + out CString message);
/// uniffi turns Err into a thrown C# exception. Matters for routinely-failing calls
/// (e.g. focus stealing denied). Ok path should match case 2's floor.
/// </summary>
public unsafe class Case13_ErrorPath
{
    [Benchmark(Baseline = true)]
    public bool CsbindgenOk() => CsbNative.csb_try_op(false);

    [Benchmark]
    public void UniffiOk() => Stock.UTryOp(false);

    [Benchmark]
    public bool CsbindgenErr() => CsbNative.csb_try_op(true);

    [Benchmark]
    public int CsbindgenErrWithMsg()
    {
        byte* err = null;
        if (!CsbNative.csb_try_op_with_msg(true, &err))
        {
            var msg = Marshal.PtrToStringUTF8((IntPtr)err)!;
            CsbNative.csb_dealloc_string(err);
            return msg.Length;
        }
        return 0;
    }

    [Benchmark]
    public bool UniffiErr()
    {
        try
        {
            Stock.UTryOp(true);
            return false;
        }
        catch (uniffi.benchffi.BenchException)
        {
            return true;
        }
    }
}
