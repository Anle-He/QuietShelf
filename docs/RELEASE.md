# 发布流程

正式发布只从版本标签对应的提交构建。项目版本统一定义在根目录的 `Directory.Build.props`，应用项目、安装脚本和安装程序不得各自维护默认版本。

## 发布前

在原始电脑上完成以下检查：

1. 更新 `VersionPrefix`、`AssemblyVersion` 和 `FileVersion`；需要预发布标识时再在 `Directory.Build.props` 中定义 `VersionSuffix`，并提交依赖锁文件。
2. 从候选提交执行 Release 构建、自动测试和手动界面检查。
3. 验证安装、启动、升级、卸载和现有数据库迁移。
4. 确认工作区干净，候选提交已经合并到 `main`。

## 构建发布产物

正式打标签前，可以从 GitHub Actions 手动运行 `Release artifacts` 工作流。它会从所选分支生成候选 ZIP 和安装包，用于验证完整发布路径，但不会创建标签或 Release。

在原始电脑上创建与项目版本完全一致的标签，例如：

```powershell
git tag -a v0.1.2 -m "QuietShelf 0.1.2"
git push origin v0.1.2
```

标签会触发 `Release artifacts` 工作流。工作流固定 .NET SDK 10.0.400 和 Inno Setup 6.7.3，校验安装程序下载、还原锁定依赖、检查 framework-dependent 发布目录的必要文件，并重新执行构建和自动测试，然后生成：

- `QuietShelf-<版本>-win-x64.zip`
- `QuietShelf-Setup-<版本>.exe`

工作流只上传待审查的 Actions artifact，不会自动创建 GitHub Release。原始电脑下载并核对产物及安装行为后，再手动创建 Release 并上传这两个文件。

如果标签与 `Directory.Build.props` 中的版本不一致，工作流会直接失败。不要通过修改工作流产物或重新使用旧标签绕过该检查。
