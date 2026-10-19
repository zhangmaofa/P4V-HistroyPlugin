# P4 Histroy Plugin

P4 Histroy Plugin 是一个面向 Visual Studio 2022 的轻量 VSIX。在代码编辑器左侧逐行显示：

```text
提交人 · CL 变更号 · 日期
```

它直接调用机器上的 `p4.exe`，复用现有登录票据，不保存密码。双击任意已提交行会直接调用 `p4v.exe -p4vc`，并明确传入当前文件实际生效的 server、user 和 client，在 P4V 中打开对应 changelist。

插件主体使用 C#（.NET Framework 4.7.2）和 WPF，实现为 Visual Studio 的 MEF 编辑器边栏；运行时不依赖 Unity 或任何具体业务程序集。

## 跨项目行为

- 不绑定 solution、depot、stream 或固定 `P4CLIENT`。
- 优先使用当前文件目录中的 `P4CONFIG` / 当前 P4 环境。
- 如果默认 client 不映射当前文件，会查询当前用户的 workspace，按本机路径选出最精确的 client，并用 `p4 where` 二次验证。
- P4V 跳转使用当前文件目录下 `p4 set -q P4PORT` 的实际连接地址，不会误用 `p4 info` 返回的服务器内部名称，也不再经过会闪出 CMD 的 `p4vc.bat`。
- workspace 解析按目录缓存；同一个 Visual Studio 实例可以打开来自不同 client 的文件。
- P4 查询失败时显示紧凑的 `P4 !` 提示；悬停可看错误，右键可重试，不会再静默隐藏。

## 构建

要求 Visual Studio 2022 或 Build Tools、.NET Framework 4.7.2 targeting pack，以及可访问 NuGet.org。

```powershell
& 'E:\VisualStudio2022\MSBuild\Current\Bin\MSBuild.exe' .\P4LineLens.sln /restore /t:Build /p:Configuration=Release
```

生成的安装包位于（构建时也会复制一份到 `dist`）：

```text
dist\P4LineLens.vsix
```

双击 `.vsix` 安装。Visual Studio 重启后，打开任意已纳入 P4 的文本文件即可看到边栏。

## 当前交互

- 点击边栏右上角的 `‹`/`›`：动态折叠或展开行级 P4 信息；折叠后保留一条窄边，随时可以恢复。
- 双击某行：在 P4V 中打开该 changelist。
- 右键边栏：折叠/展开，或刷新当前文件的 annotate 数据。
- 本地新增或修改、尚未提交的行显示为 `本地修改`，不会错误套用其他行的提交人。
- 边栏始终随编辑器容器整高拉伸；行坐标和行高会按 Visual Studio 缩放比例换算，在高倍缩放下仍与代码逐行对齐。
- 边栏正文采用紧凑的“提交人 · 变更号”显示，日期和 workspace 保留在悬停详情中，避免挤占过多代码宽度。

## 性能与限制

- `p4 annotate` 在后台进程执行，不阻塞 UI 线程。
- 只为屏幕上可见的行创建 WPF 元素。
- 初次打开大文件取决于 P4 Server 的 annotate 速度；结果会在编辑器视图中缓存。
- 当前版本面向 VS 2022（17.x）和 Windows。
