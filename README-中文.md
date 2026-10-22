# OpenAA Desktop

Windows 11 桌面动态壁纸软件：以蓝天、大海、白云和远山为桌面背景，右上角显示白色 `OpenAA.com` 品牌文字。**直接在桌面上用鼠标单击文字，即可循环切换字体**，无需打开浏览器或任何设置界面。

## 安装

1. 从 [Releases](../../releases) 下载 `OpenAA-Desktop-Setup.exe`。
2. 双击运行安装程序（当前用户安装，**不需要管理员权限**）。
3. 安装完成后从开始菜单启动 `OpenAA Desktop`，桌面壁纸即生效。
4. 安装包**未做代码签名**：首次运行时 Windows SmartScreen 可能弹出警告，点"更多信息"→"仍要运行"即可。可用同目录的 `SHA256SUMS.txt` 校验文件完整性。

## 使用

- **切换字体**：在桌面上单击右上角的 `OpenAA.com` 文字，依次切换 10 种字体（Segoe UI Light、Arial、Calibri Light、Century Gothic、Georgia、Trebuchet MS、Verdana、Bahnschrift Light、Candara Light、Segoe UI），循环往复。系统里没有的字体会自动跳过。
- **悬停提示**：鼠标移到文字上时，文字会轻微变亮。
- **记住选择**：切换后的字体会保存到 `%AppData%\OpenAADesktop\settings.json`，下次启动自动恢复。配置文件损坏或缺失时自动回退默认字体，不会崩溃。
- **桌面不受影响**：桌面图标、任务栏、其他程序都和原来一样。壁纸层位于图标下方并对鼠标透明，除文字区域外所有点击都原样交给 Windows。
- **托盘菜单**（右下角系统托盘图标）：切换字体、恢复默认字体、选择自定义壁纸、重新加载默认壁纸、开机启动开关、打开设置文件夹、设置窗口、版本、退出。
- **开机启动**：默认**关闭**，只在你从托盘或设置里主动开启后才写入注册表启动项。
- **退出**：托盘菜单 → 退出。退出后桌面恢复为你原来的 Windows 壁纸（本软件不会修改系统壁纸设置）。
- **卸载**：Windows 设置 → 应用 → OpenAA Desktop → 卸载。卸载会移除程序文件和开机启动项，**不会**删除你的任何文件；你自己的设置文件位于 `%AppData%\OpenAADesktop`，如不需要可手动删除。

## 自定义壁纸

托盘菜单 → 选择自定义壁纸…，支持 JPG / PNG / BMP。选好的路径记在本地配置里，不会写进安装目录，升级不会覆盖。点"重新加载默认壁纸"即可恢复内置的蓝天大海壁纸。

## 构建（开发者）

源码是 C# / .NET 8 / WPF，目标平台 Windows 11 x64。

```powershell
# 1. 还原二进制资源（仓库里以 base64 文本分片保存）
$assetDir = "OpenAADesktop/Assets"
$b64 = (Get-ChildItem "$assetDir/wallpaper.b64.part*" | Sort-Object Name | ForEach-Object { Get-Content $_.FullName -Raw }) -join ""
[IO.File]::WriteAllBytes("$assetDir/wallpaper.jpg", [Convert]::FromBase64String($b64))
[IO.File]::WriteAllBytes("$assetDir/app.ico", [Convert]::FromBase64String((Get-Content "$assetDir/app.ico.b64" -Raw)))

# 2. 发布（自包含、单文件）
dotnet publish OpenAADesktop/OpenAADesktop.csproj -c Release -o publish

# 3. 打安装包（需要 Inno Setup 6）
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\OpenAA-Desktop.iss
```

GitHub Actions（`.github/workflows/build.yml`）在 `windows-latest` 上自动完成以上全部步骤，并为 tag 构建生成 Release。

## 测试报告（v1.0.0）

已通过：
- [x] GitHub Actions（Windows）Release 编译与发布成功
- [x] Inno Setup 安装包生成成功
- [x] 安装包与程序的 SHA-256 校验值已生成

> 验证记录：2026-10-21，GitHub Actions 运行 #39701977019（windows-latest，.NET 8，Inno Setup 6.7.1）全部步骤成功；产物 `OpenAA-Desktop-v1.0.0`（含 OpenAA-Desktop-Setup.exe、OpenAA-Desktop.exe、SHA256SUMS.txt）。

**以下项目需要在真实 Windows 11 机器上人工验证**（构建环境无法代替真机桌面测试，欢迎反馈结果）：
- [ ] 安装包在干净 Windows 11 上的安装 / 升级 / 卸载
- [ ] 壁纸显示效果（1920×1080 / 2560×1440 / 3840×2160；100% / 125% / 150% / 200% 缩放）
- [ ] 点击文字切换字体、连续点击 100 次、重启后恢复字体
- [ ] 桌面图标可正常点击、任务栏与 Alt+Tab 不受影响
- [ ] Explorer 重启、锁屏解锁、分辨率/缩放变更后壁纸自动恢复
- [ ] 开机启动开 / 关
- [ ] 双显示器（v1.0 仅保证主显示器）

## 已知限制

- v1.0 只保证**主显示器**显示效果。
- 安装包未签名，SmartScreen 会有警告（见上文）。
- Explorer 重启瞬间壁纸可能短暂消失，程序会在数秒内（看门狗 30 秒内）自动重新挂载。
