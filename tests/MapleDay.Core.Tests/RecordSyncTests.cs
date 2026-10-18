using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MapleDay.Core;
namespace MapleDay.Core.Tests;

public class RecordSyncTests
{
    private static SyncDocument Doc(params (string Key, int Value)[] rows) => new(1, rows.ToDictionary(row => row.Key, row => JsonSerializer.SerializeToElement(row.Value)));
    [Fact] public void IndependentChangesMergeAndDeletedRecordsStayDeletedAcrossRestarts()
    {
        var baseline = Doc(("a", 1), ("b", 2));
        var result = RecordSync.Merge(baseline, Doc(("a", 3)), Doc(("a", 1), ("b", 2), ("c", 4)));
        Assert.Empty(result.Conflicts); Assert.False(result.Document.Records.ContainsKey("b"));
        Assert.Equal(3, result.Document.Records["a"].GetInt32()); Assert.Equal(4, result.Document.Records["c"].GetInt32());
        var persisted = JsonSerializer.Deserialize<SyncDocument>(JsonSerializer.Serialize(result.Document))!;
        Assert.Empty(RecordSync.Merge(persisted, persisted, result.Document).Conflicts);
        Assert.False(RecordSync.Merge(persisted, persisted, result.Document).Document.Records.ContainsKey("b"));
    }
    [Fact] public void EditAgainstDeletionRequiresAnExplicitChoice()
    {
        var basis = Doc(("a", 1)); var local = Doc(("a", 2)); var remote = Doc();
        var conflict = Assert.Single(RecordSync.Merge(basis, local, remote).Conflicts);
        Assert.NotNull(conflict.Local); Assert.Null(conflict.Remote);
        Assert.Equal(2, RecordSync.Merge(basis, local, remote, new HashSet<string> { "a" }).Document.Records["a"].GetInt32());
        Assert.Empty(RecordSync.Merge(basis, local, remote, new HashSet<string>()).Document.Records);
    }
    [Fact] public void EncryptionAuthenticatesKeyVaultAndContentWithoutPlaintext()
    {
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)); var doc = Doc(("private-record", 85));
        var encrypted = RecordSync.Encrypt(doc, key, "vault-a");
        Assert.DoesNotContain("private-record", Encoding.UTF8.GetString(encrypted));
        Assert.Equal(85, RecordSync.Decrypt(encrypted, key, "vault-a").Records["private-record"].GetInt32());
        Assert.NotEqual(encrypted, RecordSync.Encrypt(doc, key, "vault-a"));
        Assert.ThrowsAny<CryptographicException>(() => RecordSync.Decrypt(encrypted, key, "vault-b"));
        Assert.ThrowsAny<CryptographicException>(() => RecordSync.Decrypt(encrypted, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), "vault-a"));
        encrypted[^1] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => RecordSync.Decrypt(encrypted, key, "vault-a"));
    }
    [Fact] public void EquivalentObjectsDoNotCreateFalseConflicts()
    {
        var a = new SyncDocument(1, new() { ["a"] = JsonSerializer.SerializeToElement(new { Meso = 5, Fragments = 2 }) });
        var b = new SyncDocument(1, new() { ["a"] = JsonSerializer.SerializeToElement(new { Fragments = 2, Meso = 5 }) });
        Assert.Empty(RecordSync.Merge(SyncDocument.Empty, a, b).Conflicts);
    }
}
