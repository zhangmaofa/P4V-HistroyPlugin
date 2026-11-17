# P4 Histroy Plugin for Visual Studio Code

在 Visual Studio Code 中显示当前文件每一行对应的 Perforce 提交人和 changelist。本扩展不会保存密码，也不会写死 server、stream 或 workspace。

## 安装

从 `Installer/P4HistroyPlugin-VSCode.vsix` 安装：

```powershell
code --install-extension .\Installer\P4HistroyPlugin-VSCode.vsix
```

也可以在 VS Code 的扩展视图中选择“从 VSIX 安装…”。

## 使用

- 打开已纳入 P4 的本地文本文件后，行首会显示 `提交人 · CL`。
- 悬停行级记录可查看日期、提交工作区、服务器地址和当前工作区，并可打开 P4V changelist。
- 尚未提交的新增或修改行显示“本地修改”。
- 编辑器右上角提供刷新和动态显示/隐藏按钮。
- 命令面板中可使用 `P4 Histroy Plugin: 刷新当前文件`、`显示/隐藏行级记录`。
- 在编辑器中右键选择“设置当前文件的 P4 工作区”，可手动填写项目根目录、服务器地址、用户名和工作区；无需重启。
- 选择“清除当前文件的 P4 工作区设置”可删除保存的映射并恢复自动识别。
- 在行号上右键可打开该行的 changelist。

VS Code 不允许扩展创建与 Visual Studio 完全相同的自定义宽边栏，因此行级文字通过编辑器 Decoration 显示在代码行之前。

## P4 workspace 识别

扩展以当前文件所在目录为工作目录：

1. 读取当前 P4 环境和默认 client。
2. 使用 `p4 where` 验证默认 client 是否映射当前文件。
3. 不匹配时查询当前用户的 clients，按 Host 和项目根目录选择最精确的候选。
4. 再次使用 `p4 where` 验证后，查询 `文件#have` 的 annotate 数据。

第一次自动识别成功后，扩展会把项目根目录、服务器地址、用户名和工作区保存到 `%LOCALAPPDATA%\P4HistroyPlugin\workspaces.json`。Visual Studio 与 VS Code 共用该文件；后续即使环境变量丢失，也会显式传入已保存的连接参数。该文件不包含密码，P4 登录票据仍由 P4 自身管理。

手动设置优先于自动识别，直到用户主动清除。多个 Root 同时匹配时，使用路径最长、最精确的映射。

同一个 VS Code 窗口可以打开来自不同 P4 workspace 的文件。多服务器环境建议为每个项目配置 `P4CONFIG`。

## 构建

```powershell
npm install
npm test
npm run package
```

安装包生成到 `dist/P4HistroyPlugin-VSCode.vsix`。发布时复制到 `Installer/`。
