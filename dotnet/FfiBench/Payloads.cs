using System.Text;
using System.Text.Json;

namespace FfiBench;

/// <summary>
/// Test data mirroring rust/src/types.rs exactly (ASCII-only so UTF-16 length ==
/// UTF-8 length and cross-surface checksums agree). Built once per process.
/// </summary>
internal static class Payloads
{
    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private static readonly Dictionary<int, string> StringCache = new();
    private static readonly Dictionary<int, byte[]> Utf8Cache = new();
    private static readonly Dictionary<int, byte[]> BytesCache = new();
    private static readonly Dictionary<int, string[]> ListCache = new();

    public static string AsciiString(int n)
    {
        if (StringCache.TryGetValue(n, out var cached))
        {
            return cached;
        }
        const string pattern = "ffi-microbench-payload-0123456789-";
        var sb = new StringBuilder(n + pattern.Length);
        while (sb.Length < n)
        {
            sb.Append(pattern);
        }
        var s = sb.ToString(0, n);
        StringCache[n] = s;
        return s;
    }

    public static byte[] AsciiStringUtf8(int n)
    {
        if (!Utf8Cache.TryGetValue(n, out var cached))
        {
            Utf8Cache[n] = cached = Encoding.UTF8.GetBytes(AsciiString(n));
        }
        return cached;
    }

    public static byte[] Bytes(int n)
    {
        if (!BytesCache.TryGetValue(n, out var cached))
        {
            cached = new byte[n];
            for (int i = 0; i < n; i++)
            {
                cached[i] = (byte)(i % 251);
            }
            BytesCache[n] = cached;
        }
        return cached;
    }

    public static string[] StringList(int count)
    {
        if (!ListCache.TryGetValue(count, out var cached))
        {
            cached = new string[count];
            for (int i = 0; i < count; i++)
            {
                cached[i] = $"hotkey-item-{i:D5}";
            }
            ListCache[count] = cached;
        }
        return cached;
    }

    public static List<RecordDto> RecordDtos(int count)
    {
        var list = new List<RecordDto>(count);
        for (uint i = 0; i < count; i++)
        {
            list.Add(new RecordDto
            {
                Id = i,
                Title = $"Window Title {i}",
                AppPath = $"C:\\Program Files\\App{i % 17}\\app.exe",
                Enabled = i % 2 == 0,
                Score = unchecked(i * 2654435761u),
                Tag = i % 3 == 0 ? $"tag-{i % 7}" : null,
            });
        }
        return list;
    }

    public static uniffi.benchffi.BenchRecord[] UniffiRecords(int count)
    {
        var arr = new uniffi.benchffi.BenchRecord[count];
        for (uint i = 0; i < count; i++)
        {
            arr[i] = new uniffi.benchffi.BenchRecord(
                i,
                $"Window Title {i}",
                $"C:\\Program Files\\App{i % 17}\\app.exe",
                i % 2 == 0,
                unchecked(i * 2654435761u),
                i % 3 == 0 ? $"tag-{i % 7}" : null);
        }
        return arr;
    }

    public static WindowRequestDto RequestDto() => new()
    {
        Title = "Example Settings — Extensions",
        ClassName = null,
        ExePath = "C:\\Users\\user\\AppData\\Local\\Programs\\ExampleApp\\ExampleApp.exe",
        WindowIds = new uint[] { 0x1a2b3c, 0x2b3c4d, 0x3c4d5e, 0x4d5e6f },
        Pwa = new PwaInfoDto
        {
            AppId = "com.example.pwa.settings",
            StartUrl = "https://app.example.com/settings?tab=extensions",
            BrowserPath = null,
        },
        TimeoutMs = 1500,
        Focus = true,
    };

    public static uniffi.benchffi.WindowRequest UniffiRequest() => new(
        "Example Settings — Extensions",
        null,
        "C:\\Users\\user\\AppData\\Local\\Programs\\ExampleApp\\ExampleApp.exe",
        new uint[] { 0x1a2b3c, 0x2b3c4d, 0x3c4d5e, 0x4d5e6f },
        new uniffi.benchffi.PwaInfo(
            "com.example.pwa.settings",
            "https://app.example.com/settings?tab=extensions",
            null),
        1500,
        true);

    public static Dictionary<string, string> Map(int count)
    {
        var map = new Dictionary<string, string>(count);
        for (int i = 0; i < count; i++)
        {
            map[$"setting-key-{i:D5}"] = $"setting-value-{i:D5}";
        }
        return map;
    }

    /// <summary>Mirror of types.rs checksum_str (payloads are ASCII so Length == UTF-8 len).</summary>
    public static ulong Checksum(ulong acc, string s) =>
        unchecked(acc * 31 + (ulong)s.Length);
}

internal sealed class RecordDto
{
    public uint Id { get; set; }
    public string Title { get; set; } = "";
    public string AppPath { get; set; } = "";
    public bool Enabled { get; set; }
    public uint Score { get; set; }
    public string? Tag { get; set; }
}

internal sealed class PwaInfoDto
{
    public string AppId { get; set; } = "";
    public string? StartUrl { get; set; }
    public string? BrowserPath { get; set; }
}

internal sealed class WindowRequestDto
{
    public string? Title { get; set; }
    public string? ClassName { get; set; }
    public string? ExePath { get; set; }
    public uint[] WindowIds { get; set; } = Array.Empty<uint>();
    public PwaInfoDto? Pwa { get; set; }
    public uint TimeoutMs { get; set; }
    public bool Focus { get; set; }
}
