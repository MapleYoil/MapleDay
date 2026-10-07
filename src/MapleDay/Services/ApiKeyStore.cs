using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;

namespace MapleDay.Services;

/// <summary>Encrypted with Windows data protection for the current user.</summary>
public sealed class ApiKeyStore(string? path = null)
{
    private readonly string _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MapleDay", "api-key.dat");

    public async Task<string?> LoadAsync()
    {
        if (!File.Exists(_path)) return null;
        var encrypted = CryptographicBuffer.CreateFromByteArray(await File.ReadAllBytesAsync(_path));
        var decrypted = await new DataProtectionProvider().UnprotectAsync(encrypted);
        return CryptographicBuffer.ConvertBinaryToString(BinaryStringEncoding.Utf8, decrypted);
    }

    public async Task SaveAsync(string key, CancellationToken token)
    {
        var plaintext = CryptographicBuffer.ConvertStringToBinary(key, BinaryStringEncoding.Utf8);
        var encrypted = await new DataProtectionProvider("LOCAL=user").ProtectAsync(plaintext);
        CryptographicBuffer.CopyToByteArray(encrypted, out var bytes);
        token.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes, token);
            token.ThrowIfCancellationRequested();
            File.Move(temporaryPath, _path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public void Delete()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
