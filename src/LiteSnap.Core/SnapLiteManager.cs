using System.Globalization;
using System.IO.Compression;
using LiteDB;
using LiteSnap.Core.IO;
using LiteSnap.Core.Models;

namespace LiteSnap.Core;

public class SnapLiteManager : IDisposable
{
    private const string ConfigFolderName = ".litesnap";
    private const string DatabaseName = "index.db";
    private const string DatabaseIndexTableName = "index";
    private const string ObjectsFolderName = "objects";

    private readonly string _rootPath;
    private readonly LiteDatabase _database;
    private readonly HashStorage _storage;

    public string RootPath => _rootPath;

    public SnapLiteManager(string rootPath)
    {
        _rootPath = rootPath;
        var configDir = Path.Combine(_rootPath, ConfigFolderName);
        var dbFile = Path.Combine(configDir, DatabaseName);
        var objFolder = Path.Combine(configDir, ObjectsFolderName);

        if (!Directory.Exists(configDir))
            throw new DirectoryNotFoundException($"未找到 SnapLite 配置目录: {configDir}");
        if (!File.Exists(dbFile))
            throw new FileNotFoundException($"未找到 SnapLite 数据库文件: {dbFile}");
        if (!Directory.Exists(objFolder))
            Directory.CreateDirectory(objFolder);

        _storage = new HashStorage(objFolder);
        _database = new LiteDatabase($"Filename={dbFile}");
    }

    public static bool IsSnapLiteFolder(string path) =>
        Directory.Exists(Path.Combine(path, ConfigFolderName)) &&
        File.Exists(Path.Combine(path, ConfigFolderName, DatabaseName));

    public static void Initialize(string rootPath)
    {
        var configDir = Path.Combine(rootPath, ConfigFolderName);
        var dbFile = Path.Combine(configDir, DatabaseName);
        var objFolder = Path.Combine(configDir, ObjectsFolderName);

        Directory.CreateDirectory(configDir);
        Directory.CreateDirectory(objFolder);

        using var db = new LiteDatabase($"Filename={dbFile}");
        db.GetCollection<VersionData>(DatabaseIndexTableName);
    }

    // ── Read ──

    public List<VersionData> GetVersions()
    {
        var nodeList = _database.GetCollection<VersionData>(DatabaseIndexTableName);
        return nodeList.Query().OrderByDescending(x => x.Created).ToList();
    }

    public VersionData? GetVersion(string nodeId)
    {
        var nodeList = _database.GetCollection<VersionData>(DatabaseIndexTableName);
        return nodeList.FindOne(x => x.NodeId == nodeId);
    }

    public List<FileVersionObjects>? GetNodeObjects(string nodeId)
    {
        var objectList = _database.GetCollection<FileVersionObjects>(GetNodeTableName(nodeId));
        return objectList?.Query().ToList();
    }

    public Stream? GetObjectContent(string objectHash)
    {
        return _storage.Get(objectHash);
    }

    // ── Create ──

    public async Task<string> CreateVersion(string? name = null, string? desc = null)
    {
        var nodeId = Guid.NewGuid().ToString("N");
        var allFiles = await ScanAllFilesAsync();

        var nodeObjects = allFiles
            .Distinct(FileVersionObjectsComparer.Instance)
            .Where(x => x.ObjectType == ObjectType.Directory ||
                        (x.ObjectType == ObjectType.File && !_storage.Exists(x.Hash)))
            .ToList();

        var nodeTable = _database.GetCollection<FileVersionObjects>(GetNodeTableName(nodeId));
        nodeTable.InsertBulk(allFiles);

        var fileTasks = nodeObjects
            .Where(x => x.ObjectType == ObjectType.File)
            .Select(x => StoreFileAsync(x));
        await Task.WhenAll(fileTasks);

        var nodeList = _database.GetCollection<VersionData>(DatabaseIndexTableName);
        nodeList.Insert(new VersionData
        {
            NodeId = nodeId,
            Created = DateTime.Now,
            Name = name ?? DateTime.Now.ToString("yyyy/MM/dd-HH:mm:ss", CultureInfo.InvariantCulture),
            Desc = desc ?? "由 LiteSnap 创建的备份",
            Version = 1,
        });

        return nodeId;
    }

    private async Task StoreFileAsync(FileVersionObjects obj)
    {
        var filePath = Path.Combine(_rootPath, obj.Path);
        await _storage.PutAsync(filePath, obj.Hash);
    }

    private async Task<FileVersionObjects[]> ScanAllFilesAsync()
    {
        List<FileVersionObjects> result = [];
        Queue<string> queue = new();
        queue.Enqueue(_rootPath);
        var excludePath = Path.Combine(_rootPath, ConfigFolderName);

        while (queue.Count > 0)
        {
            var curDir = new DirectoryInfo(queue.Dequeue());
            var files = curDir.EnumerateFiles().ToArray();
            var dirs = curDir.EnumerateDirectories().ToArray();

            if (files.Length == 0 && dirs.Length == 0)
            {
                result.Add(new FileVersionObjects
                {
                    Path = curDir.FullName.Replace(_rootPath, "").TrimStart(Path.DirectorySeparatorChar),
                    Hash = "",
                    Length = 0,
                    CreationTime = curDir.CreationTime,
                    LastWriteTime = curDir.LastWriteTime,
                    ObjectType = ObjectType.Directory,
                });
                continue;
            }

            foreach (var file in files)
            {
                await using var fs = file.Open(FileMode.Open, FileAccess.Read, FileShare.Read);
                result.Add(new FileVersionObjects
                {
                    Path = file.FullName.Replace(_rootPath, "").TrimStart(Path.DirectorySeparatorChar),
                    Hash = await HashStorage.ComputeHashAsync(fs),
                    Length = fs.Length,
                    CreationTime = file.CreationTime,
                    LastWriteTime = file.LastWriteTime,
                    ObjectType = ObjectType.File,
                });
            }

            foreach (var dir in dirs)
                if (!string.Equals(dir.FullName, excludePath, StringComparison.OrdinalIgnoreCase))
                    queue.Enqueue(dir.FullName);
        }

        return [.. result];
    }

    // ── Apply ──

    public async Task ApplyVersion(string nodeId)
    {
        var applyObjects = GetNodeObjects(nodeId)
            ?? throw new InvalidOperationException("无法获取记录");
        var currentObjects = await ScanAllFilesAsync();
        var curDict = currentObjects.ToDictionary(x => x.Path);

        List<FileVersionObjects> toDelete = [];
        List<FileVersionObjects> toAdd = [];
        List<FileVersionObjects> toUpdate = [];

        foreach (var applyObject in applyObjects)
        {
            if (curDict.TryGetValue(applyObject.Path, out var existing))
            {
                var sameContent = existing.ObjectType == applyObject.ObjectType
                                  && existing.Length == applyObject.Length
                                  && existing.Hash == applyObject.Hash;
                var sameMetadata = existing.CreationTime == applyObject.CreationTime
                                   && existing.LastWriteTime == applyObject.LastWriteTime;
                if (!sameContent && !sameMetadata) toAdd.Add(applyObject);
                if (sameContent && !sameMetadata) toUpdate.Add(applyObject);
            }
            else
            {
                toAdd.Add(applyObject);
            }
        }

        toDelete.AddRange(from c in currentObjects
                          where applyObjects.All(x => x.Path != c.Path)
                          select c);

        foreach (var del in toDelete.OrderByDescending(x => x.Path.Count(c => c == Path.DirectorySeparatorChar))
                     .ThenBy(x => (int)x.ObjectType))
        {
            var fullPath = Path.Combine(_rootPath, del.Path);
            if (del.ObjectType == ObjectType.File && File.Exists(fullPath))
                File.Delete(fullPath);
            else if (del.ObjectType == ObjectType.Directory && Directory.Exists(fullPath))
                Directory.Delete(fullPath, true);
        }

        foreach (var add in toAdd.OrderBy(x => (int)x.ObjectType))
        {
            var fullPath = Path.Combine(_rootPath, add.Path);
            if (add.ObjectType == ObjectType.Directory)
            {
                Directory.CreateDirectory(fullPath);
            }
            else
            {
                var dir = Path.GetDirectoryName(fullPath)!;
                Directory.CreateDirectory(dir);
                using var source = GetObjectContent(add.Hash)
                    ?? throw new InvalidOperationException($"无法找到存储的文件: {add.Hash}");
                using var dest = File.Create(fullPath);
                await source.CopyToAsync(dest);
            }
            File.SetCreationTime(fullPath, add.CreationTime);
            File.SetLastWriteTime(fullPath, add.LastWriteTime);
        }

        foreach (var upd in toUpdate)
        {
            var fullPath = Path.Combine(_rootPath, upd.Path);
            if (File.Exists(fullPath) || Directory.Exists(fullPath))
            {
                File.SetCreationTime(fullPath, upd.CreationTime);
                File.SetLastWriteTime(fullPath, upd.LastWriteTime);
            }
        }
    }

    // ── Delete ──

    public void DeleteVersion(string nodeId)
    {
        var nodeList = _database.GetCollection<VersionData>(DatabaseIndexTableName);
        nodeList.DeleteMany(x => x.NodeId == nodeId);
        _database.DropCollection(GetNodeTableName(nodeId));
    }

    // ── Export ──

    public void ExportToZip(string nodeId, string saveFilePath)
    {
        var fileObjects = GetNodeObjects(nodeId)
            ?? throw new InvalidOperationException("获取记录失败");
        if (File.Exists(saveFilePath))
            File.Delete(saveFilePath);

        using var fs = new FileStream(saveFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        using var targetZip = new ZipArchive(fs, ZipArchiveMode.Create);

        foreach (var fileObject in fileObjects.OrderByDescending(x => (int)x.ObjectType))
        {
            switch (fileObject.ObjectType)
            {
                case ObjectType.File:
                {
                    var entry = targetZip.CreateEntry(fileObject.Path);
                    entry.LastWriteTime = fileObject.LastWriteTime;
                    using var writer = entry.Open();
                    using var reader = GetObjectContent(fileObject.Hash)
                        ?? throw new InvalidOperationException($"无法找到存储的文件: {fileObject.Hash}");
                    reader.CopyTo(writer);
                    break;
                }
                case ObjectType.Directory:
                {
                    var entry = targetZip.CreateEntry($"{fileObject.Path}/");
                    entry.LastWriteTime = fileObject.LastWriteTime;
                    break;
                }
            }
        }
    }

    public void ExportToFolder(string nodeId, string destFolderPath)
    {
        var fileObjects = GetNodeObjects(nodeId)
            ?? throw new InvalidOperationException("获取记录失败");

        foreach (var fileObject in fileObjects.OrderByDescending(x => (int)x.ObjectType))
        {
            var fullPath = Path.Combine(destFolderPath, fileObject.Path);
            switch (fileObject.ObjectType)
            {
                case ObjectType.Directory:
                    Directory.CreateDirectory(fullPath);
                    break;
                case ObjectType.File:
                {
                    var dir = Path.GetDirectoryName(fullPath)!;
                    Directory.CreateDirectory(dir);
                    using var source = GetObjectContent(fileObject.Hash)
                        ?? throw new InvalidOperationException($"无法找到存储的文件: {fileObject.Hash}");
                    using var dest = File.Create(fullPath);
                    source.CopyTo(dest);
                    File.SetCreationTime(fullPath, fileObject.CreationTime);
                    File.SetLastWriteTime(fullPath, fileObject.LastWriteTime);
                    break;
                }
            }
        }
    }

    // ── Internal ──

    private static string GetNodeTableName(string nodeId) => $"node_{nodeId}";

    public void Dispose()
    {
        _database.Dispose();
    }
}
