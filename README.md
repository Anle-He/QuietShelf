# QuietShelf

QuietShelf 是一个本地优先、低打扰的 Windows 书影音记录应用。

它以作品为单位管理书籍和影视；同一作品可以有多次阅读或观看体验。进行过程中可以按分钟或集数留下每日进度，完成后再记录起止日期、总结和四维评分。完整评分会汇总为作品综合分。

## 当前功能

- 双栏作品库、标题搜索和书籍/影视筛选
- 多次阅读或观看，以及进行中的每日记录
- 按时长或集数记录；影视可保存总集数并显示累计进度
- 四维评分、单次评分和作品综合评分
- 分层编辑与删除
- SQLite 本地存储，无账号、社交、推荐、广告或在线元数据抓取

项目目前处于 `0.1.0-alpha` 阶段，数据结构和交互仍可能调整。

## 运行与数据

发布版本是免安装的 Windows 单文件程序。个人记录默认保存在：

```text
%LOCALAPPDATA%\QuietShelf\records.db
```

数据库、编译产物和本机配置均被 Git 忽略，不会进入源码仓库。

## 从源码构建

需要 Windows 与 .NET 10 SDK：

```powershell
dotnet restore .\QuietShelf.slnx
dotnet build .\QuietShelf.slnx -c Release
```

生成免安装的单文件程序：

```powershell
dotnet publish .\src\QuietShelf.App\QuietShelf.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o .\artifacts\win-x64
```

## 产品边界与设计

产品、交互、设计与工程决策位于 [`docs`](docs/PRODUCT.md)。界面和数据结构参考了 Openreads、LibrisLog、Yamtrack 与 WPF UI 等开源项目，但 QuietShelf 保持个人日志工具的最小边界。

## License

[MIT](LICENSE)

