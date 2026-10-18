using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace MapleDay.Core;

public sealed record SyncDocument(int Version, Dictionary<string, JsonElement> Records)
{
    public static SyncDocument Empty => new(1, new(StringComparer.Ordinal));
}
public sealed record SyncConflict(string Key, JsonElement? Local, JsonElement? Remote);
public sealed record SyncMergeResult(SyncDocument Document, IReadOnlyList<SyncConflict> Conflicts);
public static class RecordSync
{
    public static bool Same(JsonElement? a, JsonElement? b) => a.HasValue == b.HasValue
        && (!a.HasValue || JsonNode.DeepEquals(JsonNode.Parse(a.Value.GetRawText()), JsonNode.Parse(b!.Value.GetRawText())));
    private static JsonElement? Get(SyncDocument document, string key) => document.Records.TryGetValue(key, out var value) ? value : null;
    public static SyncMergeResult Merge(SyncDocument baseline, SyncDocument local, SyncDocument remote, IReadOnlySet<string>? preferLocal = null)
    {
        if (baseline.Version != 1 || local.Version != 1 || remote.Version != 1) throw new ArgumentException("지원하지 않는 동기화 버전입니다. 앱을 업데이트해주세요.");
        var result = SyncDocument.Empty; var conflicts = new List<SyncConflict>();
        foreach (var key in baseline.Records.Keys.Concat(local.Records.Keys).Concat(remote.Records.Keys).Distinct(StringComparer.Ordinal))
        {
            var b = Get(baseline, key); var l = Get(local, key); var r = Get(remote, key);
            JsonElement? chosen;
            if (Same(l, r) || Same(b, r)) chosen = l;
            else if (Same(b, l)) chosen = r;
            else { conflicts.Add(new(key, l, r)); chosen = preferLocal is not null && preferLocal.Contains(key) ? l : r; }
            if (chosen is { } value) result.Records[key] = value.Clone();
        }
        return new(result, conflicts);
    }
    public static byte[] Encrypt(SyncDocument document, string keyHex, string vault)
    {
        var nonce = RandomNumberGenerator.GetBytes(12); var tag = new byte[16];
        var raw = JsonSerializer.SerializeToUtf8Bytes(document);
        if (raw.Length > 3_900_000) throw new ArgumentException("동기화할 기록이 너무 큽니다.");
        var cipher = new byte[raw.Length];
        using var aes = new AesGcm(Convert.FromHexString(keyHex), 16);
        aes.Encrypt(nonce, raw, cipher, tag, Encoding.UTF8.GetBytes("MapleDay-records-v1:" + vault));
        return nonce.Concat(tag).Concat(cipher).ToArray();
    }
    public static SyncDocument Decrypt(byte[] payload, string keyHex, string vault)
    {
        if (payload.Length is < 29 or > 4_194_304) throw new ArgumentException("암호화 기록 크기를 확인해주세요.");
        var raw = new byte[payload.Length-28];
        using var aes = new AesGcm(Convert.FromHexString(keyHex), 16);
        aes.Decrypt(payload.AsSpan(0, 12), payload.AsSpan(28), payload.AsSpan(12, 16), raw, Encoding.UTF8.GetBytes("MapleDay-records-v1:" + vault));
        var result = JsonSerializer.Deserialize<SyncDocument>(raw) ?? throw new ArgumentException("기록을 읽지 못했어요.");
        if (result.Version != 1 || result.Records is null || result.Records.Count > 100000) throw new ArgumentException("앱을 업데이트하거나 기록을 확인해주세요.");
        return result;
    }
}
