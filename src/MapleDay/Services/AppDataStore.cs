namespace MapleDay.Services;

public sealed class AppDataStore(string? root = null)
{
    private readonly string _root = Path.GetFullPath(root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MapleDay"));
    public static bool IsDeleteConfirmation(string text) => string.Equals(text, "삭제", StringComparison.Ordinal);

    public Task<long> SizeAsync() => Task.Run(() => Directory.Exists(_root) ? Size(_root) : 0);

    public Task DeleteAsync(string confirmation)
    {
        if (!IsDeleteConfirmation(confirmation)) throw new ArgumentException("삭제 문구가 일치하지 않습니다.", nameof(confirmation));
        if (_root == Path.GetPathRoot(_root)) throw new InvalidOperationException("데이터 폴더를 확인할 수 없습니다.");
        return Task.Run(() => { if (Directory.Exists(_root)) DeleteDirectory(_root); });
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):0.##} GB",
        >= 1024L * 1024 => $"{bytes / (1024d * 1024):0.##} MB",
        >= 1024 => $"{bytes / 1024d:0.##} KB", _ => $"{bytes} B"
    };

    private long Size(string directory)
    {
        Validate(directory);
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return 0;
        long size = 0;
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            Validate(path);
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
            if ((attributes & FileAttributes.Directory) != 0) size += Size(path);
            else size += new FileInfo(path).Length;
        }
        return size;
    }

    private void DeleteDirectory(string directory)
    {
        Validate(directory);
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("연결된 폴더는 삭제하지 않습니다.");
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            Validate(path);
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("연결된 파일은 삭제하지 않습니다.");
            if ((attributes & FileAttributes.Directory) != 0) DeleteDirectory(path);
            else File.Delete(path);
        }
        Directory.Delete(directory, recursive: false);
    }

    private void Validate(string path)
    {
        var full = Path.GetFullPath(path);
        if (!string.Equals(full, _root, StringComparison.OrdinalIgnoreCase)
            && !full.StartsWith(_root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("앱 데이터 폴더 밖의 경로입니다.");
    }
}
