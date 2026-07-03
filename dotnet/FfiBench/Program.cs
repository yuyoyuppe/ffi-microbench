using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Running;

#if !NET8_0_OR_GREATER
#error FfiBench must target net8.0+ so the generated bindings' NET8_0_OR_GREATER fast paths (LibraryImport P/Invoke, BigEndianStream span reads) are compiled in.
#endif

namespace FfiBench;

public static class Program
{
    public static int Main(string[] args)
    {
        // Two smoke/stress phases in separate processes — see notes in Smoke.cs/Stress.cs.
        if (args.Contains("--smoke"))
        {
            return Smoke.RunStockAndCsbindgen() ? 0 : 1;
        }
        if (args.Contains("--smoke-span"))
        {
            return Smoke.RunSpanFlavor() ? 0 : 1;
        }
        if (args.Contains("--stress"))
        {
            return Stress.RunStock(StressSeconds(args)) ? 0 : 1;
        }
        if (args.Contains("--stress-span"))
        {
            return Stress.RunSpan(StressSeconds(args)) ? 0 : 1;
        }

        var config = ManualConfig.Create(DefaultConfig.Instance)
            .AddDiagnoser(MemoryDiagnoser.Default)
            .AddColumn(StatisticColumn.Median, StatisticColumn.P90, StatisticColumn.P95)
            .AddExporter(JsonExporter.Full);

        var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
        return summaries.Any(s => s.HasCriticalValidationErrors) ? 1 : 0;
    }

    private static int StressSeconds(string[] args)
    {
        var idx = Array.FindIndex(args, a => a is "--stress" or "--stress-span");
        return idx >= 0 && idx + 1 < args.Length && int.TryParse(args[idx + 1], out var s) ? s : 20;
    }
}
