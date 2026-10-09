using System.Security.Cryptography;

namespace MapleDay.Web;

public sealed record WebPassword(string Salt, string Hash)
{
    public bool Valid
    {
        get { try { return Convert.FromBase64String(Salt).Length == 16 && Convert.FromBase64String(Hash).Length == 32; } catch (FormatException) { return false; } }
    }
    public static WebPassword Create(string password)
    {
        if (password.Length is < 8 or > 256) throw new ArgumentException("접속 비밀번호는 8~256자로 입력해주세요.");
        var salt = RandomNumberGenerator.GetBytes(16);
        return new(Convert.ToBase64String(salt), Convert.ToBase64String(Derive(password, salt)));
    }
    public bool Verify(string password)
    {
        if (password.Length is < 8 or > 256) return false;
        try
        {
            var salt = Convert.FromBase64String(Salt); var hash = Convert.FromBase64String(Hash);
            return salt.Length == 16 && hash.Length == 32 && CryptographicOperations.FixedTimeEquals(hash, Derive(password, salt));
        }
        catch (FormatException) { return false; }
    }
    private static byte[] Derive(string password, byte[] salt) => Rfc2898DeriveBytes.Pbkdf2(password, salt, 210_000, HashAlgorithmName.SHA256, 32);
}
