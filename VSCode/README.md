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
- 悬停行级记录可查看日期、提交 workspace、当前 server 和 client，并可打开 P4V changelist。
- 尚未提交的新增或修改行显示“本地修改”。
- 编辑器右上角提供刷新和动态显示/隐藏按钮。
- 命令面板中可使用 `P4 Histroy Plugin: 刷新当前文件`、`显示/隐藏行级记录`。
- 在行号上右键可打开该行的 changelist。

VS Code 不允许扩展创建与 Visual Studio 完全相同的自定义宽边栏，因此行级文字通过编辑器 Decoration 显示在代码行之前。

## P4 workspace 识别

扩展以当前文件所在目录为工作目录：

1. 读取当前 P4 环境和默认 client。
2. 使用 `p4 where` 验证默认 client 是否映射当前文件。
3. 不匹配时查询当前用户的 clients，按 Host 和本地 Root 选择最精确的候选。
4. 再次使用 `p4 where` 验证后，查询 `文件#have` 的 annotate 数据。

同一个 VS Code 窗口可以打开来自不同 P4 workspace 的文件。多服务器环境建议为每个项目配置 `P4CONFIG`。

## 构建

```powershell
npm install
npm test
npm run package
```

安装包生成到 `dist/P4HistroyPlugin-VSCode.vsix`。发布时复制到 `Installer/`。
