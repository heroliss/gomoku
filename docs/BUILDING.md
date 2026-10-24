# 构建与发布

## .NET 10

安装 Windows x64 的 **SDK**，不是仅供运行的 Desktop Runtime：

- [官方下载页](https://dotnet.microsoft.com/zh-cn/download/dotnet/10.0)
- [微软 Windows 安装说明](https://learn.microsoft.com/zh-cn/dotnet/core/install/windows)

安装后重新打开 PowerShell 7，执行 `dotnet --list-sdks`，应有 `10.x`。仓库不绑定单个补丁 SDK；请使用仍受支持的 .NET 10 SDK 和安全更新。V3 发布目标固定 `net10.0-windows`、`win-x64`。

## Photon 兼容依赖

原工程使用 Photon .NET **4.0.5.0** 的两个未修改程序集：

```text
Photon3DotNet.dll
PhotonLoadbalancingApi.dll
```

通过 [Photon 官方 SDK 下载页](https://www.photonengine.com/sdks) 和自己的 Photon 账户取得合法 SDK。当前最新版本的类型和 API 已改变，**不能下载最新包后改名替换**。若官方页面不再提供兼容旧版，需要向官方查询历史 SDK，或使用自己已合法持有的原工程依赖；本仓库不提供第三方网盘或 DLL 镜像。完整游戏发布包含这些依赖，不能将它们单独再分发成 SDK。

`licenses/PHOTON-NOTICE.md` 列出已验证程序集版本与 SHA-256，bootstrap 会核对这两项，不执行未知 DLL。

```powershell
.\scripts\bootstrap.ps1 -PhotonSdkDirectory 'C:\SDKs\Photon405'
```

该命令检查 .NET，再把两份兼容程序集复制到被忽略的 `vendor/Photon/`；不下载 DLL、不安装全局组件、不改 PATH。只查看官方页面可以使用 `-OpenDownloadPages`。如果本机拥有只读 `legacy/v2.3/五子棋/Lib`，构建工程也允许以其为本地兜底，legacy 全目录不得提交。

无需复制也可显式传 MSBuild 属性：

```powershell
dotnet build src/Gomoku.App/Gomoku.App.csproj -c Release -p:PhotonSdkDirectory='C:\SDKs\Photon405'
dotnet run --project tests/Gomoku.Networking.Tests -c Release -p:PhotonSdkDirectory='C:\SDKs\Photon405'
.\scripts\publish.ps1 -PhotonSdkDirectory 'C:\SDKs\Photon405'
```

独立复用 Core 和 Storage 不需要 Photon SDK 或 Windows UI。

## Windows 完整包

```powershell
.\scripts\publish.ps1 -Version 3.0.0
```

默认顺序：验证 SDK → 运行三个控制台测试项目 → `dotnet publish` 自包含 Windows x64 → 检查 EXE/运行时/Photon 文件 → 复制 README 和第三方声明 → 生成逐文件校验清单 → 生成 ZIP 与 SHA-256。

- 解压构建：`artifacts/publish/<构建标识>/liss-gomoku-v3.0.0-win-x64/`
- 下载文件：`artifacts/releases/liss-gomoku-v3.0.0-win-x64.zip`
- 校验文件：同名 `.zip.sha256`
- 测试与包记录：`artifacts/verification/<构建标识>/`

每次构建使用独立目录，不触碰正在运行的开发版本。ZIP 写入临时文件后替换同版本的发布 ZIP。旧构建目录保留，便于人工回溯；脚本不递归删除目录。仅调试打包时可用 `-SkipTests`，包清单会记下跳过状态，不能据此标记验收通过。

此版本不使用裁剪或单文件捆绑，以保持旧 Photon SDK 的反射/序列化兼容性。只支持 Windows x64，未签名；完整 ZIP 带运行时，下载与解压体积会比框架依赖版本大。

## 发布前核对

1. 在全新数据目录启动实际 EXE，测试新局、提示/取消、悔棋/回看、保存/读取，以及思考中重开/关闭。
2. 如发布联机功能，用两份实际客户端确认连接、建房、加入、双向落子、请求/响应和离房；控制台协议测试不能代替实网。
3. 将完整 ZIP 与 SHA-256 作为同一版本上传；不要上传 `legacy/`、`vendor/`、错误日志、自动存档、测试用户数据或私有配置。
4. 完整游戏保留 `licenses/PHOTON-NOTICE.md`、`Photon-NET-OSS-current.pdf`，以及 .NET Runtime/WinForms 的三份许可/第三方声明。公开源码不附单独 SDK DLL，也没有额外添加仓库 LICENSE。ZIP 内根目录稳定为 `liss-gomoku-v3.0.0-win-x64/`，不包含构建随机标识。
