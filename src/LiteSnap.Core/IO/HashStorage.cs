using System.Security.Cryptography;
using Microsoft.CopyOnWrite;

namespace LiteSnap.Core.IO;

public class HashStorage
{
    private readonly string _folder;
    private readonly int _prefixLength;
    private readonly ICopyOnWriteFilesystem _cow = CopyOnWriteFilesystemFactory.GetInstance();

    public HashStorage(string folder, int prefixLength = 2)
    {
        _folder = folder;
        _prefixLength = prefixLength;
    }

    public Stream? Get(string hash)
    {
        var destPath = GetDestPath(hash);
        if (!File.Exists(destPath))
            destPath = GetMisplacedFilePath(hash);
        return File.Exists(destPath) ? File.OpenRead(destPath) : null;
    }

    public bool Exists(string hash)
    {
        return File.Exists(GetDestPath(hash)) || File.Exists(GetMisplacedFilePath(hash));
    }

    public async Task PutAsync(Stream input, string hash)
    {
        var destPath = GetDestPath(hash);
        var dir = Path.GetDirectoryName(destPath)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        await using var fs = File.Create(destPath);
        input.Position = 0;
        await input.CopyToAsync(fs);
    }

    public async Task PutAsync(string sourcePath, string hash)
    {
        var destPath = GetDestPath(hash);
        var dir = Path.GetDirectoryName(destPath)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        if (File.Exists(destPath)) return;

        var canClone = _cow.CopyOnWriteLinkSupportedBetweenPaths(sourcePath, destPath);
        if (canClone)
        {
            try
            {
                _cow.CloneFile(sourcePath, destPath);
                return;
            }
            catch (MaxCloneFileLinksExceededException) { /* Continue for normal copy */ }
        }

        await using var fs = File.OpenRead(sourcePath);
        await PutAsync(fs, hash);
    }

    public Task DeleteAsync(string hash)
    {
        var path = GetDestPath(hash);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    public string GetDestPath(string hash) =>
        Path.Combine(_folder, GetPrefixFolder(hash), hash);

    public string GetMisplacedFilePath(string hash) =>
        Path.Combine(_folder, hash);

    private string GetPrefixFolder(string hash)
    {
        if (hash.Length < _prefixLength)
            throw new ArgumentException($"Hash length ({hash.Length}) is shorter than required prefix length ({_prefixLength})", nameof(hash));
        return hash[.._prefixLength];
    }

    public static string ComputeHash(Stream input)
    {
        using var sha512 = SHA512.Create();
        input.Position = 0;
        return Convert.ToHexStringLower(sha512.ComputeHash(input));
    }

    public static async Task<string> ComputeHashAsync(Stream input)
    {
        using var sha512 = SHA512.Create();
        input.Position = 0;
        return Convert.ToHexStringLower(await sha512.ComputeHashAsync(input));
    }
}
