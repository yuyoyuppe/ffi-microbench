using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace FfiBench;

internal static class CallbackCounters
{
    public static long ScalarCalls;
    public static long StringChars;
}

/// <summary>
/// csbindgen-style callbacks: static [UnmanagedCallersOnly] methods taken as raw
/// Cdecl function pointers (no delegate object, no GCHandle), exactly like
/// app\Native\NativeContext.cs. String arg arrives as UTF-16 ptr+len and is
/// materialized with new string(span), matching the incumbent.
/// </summary>
internal static unsafe class CsbCallbacks
{
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static void OnTrigger(uint id) => CallbackCounters.ScalarCalls++;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static void OnLog(ushort* ptr, int len)
    {
        var s = new string(new ReadOnlySpan<char>((char*)ptr, len));
        CallbackCounters.StringChars += s.Length;
    }

    public static void RegisterScalar() =>
        CsBindgen.CsbNative.csb_register_scalar_callback(&OnTrigger);

    public static void RegisterString() =>
        CsBindgen.CsbNative.csb_register_string_callback(&OnLog);
}

/// <summary>
/// uniffi callback-interface implementations. Callback codegen is identical in both
/// flavors, but each flavor's _UniFFILib static ctor registers its own vtable for
/// the same Rust-side slot — last one touched wins. Benchmark processes therefore
/// stay flavor-pure (BDN runs each benchmark in its own process; targeted
/// GlobalSetups never touch the other flavor), and the smoke test phases stock
/// strictly before span.
/// </summary>
internal sealed class UniffiScalarCallback : uniffi.benchffi.ScalarCallback
{
    public void OnTrigger(uint id) => CallbackCounters.ScalarCalls++;
}

internal sealed class UniffiStringCallback : uniffi.benchffi.StringCallback
{
    public void OnLog(string msg) => CallbackCounters.StringChars += msg.Length;
}

internal sealed class UniffiSpanScalarCallback : uniffi.benchffi_span.ScalarCallback
{
    public void OnTrigger(uint id) => CallbackCounters.ScalarCalls++;
}

internal sealed class UniffiSpanStringCallback : uniffi.benchffi_span.StringCallback
{
    public void OnLog(string msg) => CallbackCounters.StringChars += msg.Length;
}
