# LiteSnap

简易文件夹快照工具，诞生于 [PCL CE](https://github.com/PCL-Community) 的存档备份需求。

## 由来

玩 Minecraft 的玩家都懂 —— 进新版本前备份存档，模组搞炸了回滚，这是刻在骨子里的习惯。LiteSnap 最初就是为 PCL CE 的 .minecraft/saves 文件夹快照备份而写，后来抽离为通用工具，可以对任意文件夹做文件级快照管理。

## 原理

```
{target_folder}/
└── .litesnap/
    ├── index.db            # LiteDB 索引：版本列表 + 每个版本的文件清单
    └── objects/            # 内容寻址存储（SHA-512 去重）
        └── {hash[:2]}/     # 前缀子目录
            └── {hash}      # 原始文件内容
```

- **创建快照**：扫描目标文件夹，按 SHA-512 哈希去重存储文件，版本清单写入 LiteDB
- **回滚快照**：根据版本清单从 `objects/` 还原文件到目标目录
- **导出**：支持导出为 ZIP 或普通文件夹，方便迁移

文件重复越多，节约空间越明显（同一个文件在不同版本间只存一份物理副本）。

## 功能

| 功能 | 说明 |
|---|---|
| 创建快照 | 扫描当前文件夹，生成文件级快照 |
| 回滚快照 | 将指定版本还原到目标目录 |
| 导出 ZIP | 导出为 ZIP 压缩包 |
| 导出文件夹 | 导出为普通文件夹 |
| 删除快照 | 删除指定版本记录 |

## 使用

```
# 命令行 - 直接运行
dotnet run --project src/LiteSnap.App

# 发布为单文件
dotnet publish src/LiteSnap.App -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:PublishTrimmed=true -p:TrimMode=link

# 产物在 bin/Release/net10.0/win-x64/publish/LiteSnap.App.exe
```

启动后点击「打开」选择包含 `.litesnap` 数据的文件夹，左侧列出所有版本，右侧查看文件详情，顶部工具栏执行操作。

## 技术栈

| 层 | 技术 |
|---|---|
| 运行时 | .NET 10, C# |
| 桌面 UI | Avalonia 12, Fluent Theme, CommunityToolkit.Mvvm |
| 存储引擎 | LiteDB 5 (嵌入式 NoSQL) |
| 内容寻址 | SHA-512, 文件级去重 |
| 平台 | Windows / Linux / macOS |

## 构建

```bash
dotnet build
```

无需额外依赖，还原即用。

## 数据结构

详见 [docs/data-structure.md](docs/data-structure.md)。

## License

MIT
