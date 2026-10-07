using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MapleDay.Core;

public sealed record DiagnosticReport(string Id, string Category, string ExceptionType, int Hresult,
    string[] Frames, string Version, string OsVersion, bool Fatal, long Occurred)
{
    public static DiagnosticReport Create(Exception error, string category, Version version, bool fatal)
    {
        if (error is AggregateException aggregate && aggregate.Flatten().InnerExceptions.FirstOrDefault() is { } inner) error = inner;
        // Never use Message, ToString, StackTrace strings, Data, URLs or paths.
        var frames = new StackTrace(error, false).GetFrames() ?? [];
        var symbols = frames.Select(frame => frame.GetMethod())
            .Where(method => method?.DeclaringType?.FullName?.StartsWith("MapleDay.", StringComparison.Ordinal) == true)
            .Select(method => Symbol(method!.DeclaringType!.FullName + "." + method.Name)).Distinct().Take(12).ToArray();
        var os = Environment.OSVersion.Version;
        return new(Guid.NewGuid().ToString("N"), category, Symbol(error.GetType().FullName ?? "System.Exception"),
            error.HResult, symbols, FourParts(version), FourParts(os), fatal, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }
    private static string FourParts(Version version) => new Version(Math.Clamp(version.Major, 0, 65535),
        Math.Clamp(version.Minor, 0, 65535), Math.Clamp(version.Build, 0, 65535), Math.Clamp(version.Revision, 0, 65535)).ToString(4);
    private static string Symbol(string symbol) => Regex.Replace(symbol, "[^A-Za-z0-9_.+<>`\\[\\]-]", "_")[..Math.Min(symbol.Length, 180)];
    public string Fingerprint => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new { Category, ExceptionType, Hresult, Frames }))));
}

public static class UsageIdentity
{
    public static string NicknameHash(string nickname) => Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes("MapleDay-usage-nickname-v1:" + nickname.Trim().Normalize(NormalizationForm.FormC))));
    // The highest-level character is independent from the user's chosen support representative.
    public static string? HighestNickname(IEnumerable<(string Name, int Level)> characters) => characters
        .Where(character => !string.IsNullOrWhiteSpace(character.Name) && character.Level > 0)
        .OrderByDescending(character => character.Level).ThenBy(character => character.Name, StringComparer.Ordinal)
        .Select(character => character.Name).FirstOrDefault();
}
