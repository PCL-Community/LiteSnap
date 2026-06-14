# LiteSnap 数据结构

## 目录布局

```
{rootPath}/
└── .litesnap/
    ├── index.db                # LiteDB 数据库文件
    └── objects/                # 内容可寻址存储
        └── {hash[..2]}/        # 前缀子目录（SHA-512 前 2 位十六进制字符）
            └── {hash}          # 原始文件内容
```

`index.db` 使用 LiteDB 嵌入式数据库，包含两类表：

---

## LiteDB 表

### 1. 索引表 — `index`

**集合名**: `index`  
**记录类型**: `VersionData`

| 字段 | 类型 | 说明 |
|---|---|---|
| `NodeId` | `string` | GUID 字符串，版本唯一标识 |
| `Created` | `DateTime` | 创建时间 |
| `Name` | `string` | 版本名称（默认格式: `yyyy/MM/dd-HH:mm:ss`） |
| `Desc` | `string` | 版本描述 |
| `Version` | `long` | 版本号（当前恒为 1） |

### 2. 版本内容表 — `node_{nodeId}`

**集合名**: `node_{NodeId}`（如 `node_a1b2c3d4e5f6...`）  
**记录类型**: `FileVersionObjects`

| 字段 | 类型 | 说明 |
|---|---|---|
| `Path` | `string` | 相对于根目录的路径（如 `saves/new-world/level.dat`） |
| `Hash` | `string` | SHA-512 十六进制小写（目录为空字符串） |
| `Length` | `long` | 文件字节数（目录为 0） |
| `CreationTime` | `DateTime` | 文件创建时间 |
| `LastWriteTime` | `DateTime` | 文件最后修改时间 |
| `ObjectType` | `int` | 0 = `File`, 1 = `Directory` |

### 查询示例

```csharp
// 列出所有版本
manager.GetVersions();

// 获取某个版本的文件列表
manager.GetNodeObjects(nodeId);

// 读取某个文件内容
manager.GetObjectContent(hash);
```

---

## 对象存储（objects/）

文件内容通过 SHA-512 哈希存储，路径规则：

```
objects/{hash[0..2]}/{hash}
```

例如哈希为 `"ab12ef..."` 的文件存储在 `objects/ab/ab12ef...`。

由于历史原因，部分文件可能直接存在于 `objects/{hash}`（无前缀子目录）。`HashStorage` 在读取时会自动处理这种情况：

```csharp
// 优先查找 objects/{prefix}/{hash}，若不存在则查找 objects/{hash}
storage.Get(hash);
```

---

## SnapLiteManager API

### 读取

| 方法 | 返回 | 说明 |
|---|---|---|
| `GetVersions()` | `List<VersionData>` | 按创建时间降序返回所有版本 |
| `GetVersion(nodeId)` | `VersionData?` | 按 ID 查询单个版本 |
| `GetNodeObjects(nodeId)` | `List<FileVersionObjects>?` | 获取版本内的文件列表 |
| `GetObjectContent(hash)` | `Stream?` | 读取哈希对应的文件内容 |
| `IsSnapLiteFolder(path)` | `bool` | 静态方法，检查路径是否为有效 SnapLite 目录 |

### 写入

| 方法 | 说明 |
|---|---|
| `CreateVersion(name?, desc?)` | 扫描根目录并创建快照，返回 `nodeId` |
| `ApplyVersion(nodeId)` | 将版本恢复到根目录（增/删/改文件） |
| `DeleteVersion(nodeId)` | 删除版本记录及其文件列表 |
| `Initialize(rootPath)` | 静态方法，在目标目录初始化 `.litesnap` 结构 |

### 导出

| 方法 | 说明 |
|---|---|
| `ExportToZip(nodeId, savePath)` | 导出为 ZIP 文件 |
| `ExportToFolder(nodeId, destPath)` | 导出到指定文件夹 |
