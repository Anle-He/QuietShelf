# 一页 Yiye

一页 Yiye 是一个本地优先、低打扰的 Windows 书影音记录应用。

它以作品为中心：一本书或一部影视只在作品库中出现一次，其下可以保留多次阅读或观看、每日进度、完成总结与评分。

## 主要功能

- 书籍与影视作品库，支持标题、副标题、作者搜索和类别筛选
- 多次阅读或观看，以及按分钟或集数记录的每日进度
- 每次完成后的四维评分，以及基于完整评分汇总的作品综合分
- 多张本地封面，用照片堆呈现同一作品的不同版本
- 记录、封面和迁移备份全部保存在本地

一页不包含账号、社交、推荐、广告或在线元数据抓取。当前版本为 `0.1.2` 开发预发布版。

## 安装与数据

需要 Windows x64 和 [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)。从 [Releases](https://github.com/Anle-He/Yiye/releases) 下载 `QuietShelf-Setup-<版本>.exe`，后续安装包使用 `Yiye-Setup-<版本>.exe` 命名。安装后可从开始菜单搜索 `Yiye` 启动，并在 Windows“已安装的应用”中卸载。

默认数据目录：

```text
%LOCALAPPDATA%\QuietShelf
```

其中 `records.db` 保存记录，`covers` 保存封面；数据库升级前生成的备份也位于此处。需要更换位置时，可以设置环境变量 `QUIETSHELF_DATA_DIR`。

## 从源码构建

需要 Windows 和 .NET 10 SDK：

```powershell
dotnet restore .\QuietShelf.slnx --locked-mode
dotnet build .\QuietShelf.slnx --no-restore -c Release
```

生成安装包还需要 Inno Setup 6：

```powershell
.\scripts\build-installer.ps1
```

产品边界与技术设计见 [`docs`](docs/PRODUCT.md)，发布流程见 [`docs/RELEASE.md`](docs/RELEASE.md)。

## License

[MIT](LICENSE)
