# Trans Tool · ASCII 转换

适用于 Windows 11 和 Windows 10 22H2 的本地 ASCII 双向转换工具。转换无需联网。

## 安装

在 [GitHub Releases](https://github.com/tangyuanx/trans_tool/releases/latest) 下载 `TransTool-Windows-Setup.exe`，运行安装向导，再从开始菜单打开 **Trans Tool**。安装在当前用户目录下，可以在向导里选择其他路径。首版安装包未签名。

## 转换

| 操作 | 输入 | 结果 |
| --- | --- | --- |
| 字符 → ASCII，十进制 | `ABC` | `65 66 67` |
| 字符 → ASCII，十六进制 | `ABC` | `41 42 43` |
| 字符 → ASCII，二进制 | `AB` | `01000001 01000010` |
| ASCII → 字符，十进制 | `65, 0x42, 0b1000011, 13, 10` | `ABC\r\n` |

- 选择方向和进制，输入或粘贴后立即转换。ASCII 码支持用空格、逗号、分号或换行分隔。单项可用 `0x` 或 `0b` 指定进制。
- 反向转换的结果使用转义文本，支持 `\0`、`\t`、`\n`、`\r`、`\\` 和 `\x00` 到 `\x7F`。点 **用结果反转** 会自动为字符输入开启 **识别转义**。未开启时反斜杠按普通字符转换。
- ASCII 只包含 0–127。遇到中文、emoji 或超出范围的码值时，会标明出错位置。

## 更新

点右上角 **检查更新**。找到新版本后，先点 **下载更新**，等待进度完成和 SHA-256 校验通过，再点 **安装并重启**。安装会覆盖原目录，更新前如有输入会提示本次内容将清空。退出应用不会触发安装。

## 构建和发布

在装有 .NET Framework 4.8+ 与 Inno Setup 6 的 Windows 上执行：

```powershell
./scripts/build.ps1 -RunTests
```

安装包输出到 `artifacts/TransTool-Windows-Setup.exe`。Windows 工作流会验证双向转换、更新下载校验、首次安装、原地升级和重启。发布新版本时更新 `VERSION` 和 `RELEASE_NOTES.md`，推送 `main`。验证通过后会发布对应的稳定版 Release。
