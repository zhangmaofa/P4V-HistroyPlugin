using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using P4HistroyPlugin.Core;

namespace P4HistroyPlugin.Editor
{
    internal sealed class P4WorkspaceSettingsDialog : Window
    {
        private readonly TextBox _root;
        private readonly TextBox _port;
        private readonly TextBox _user;
        private readonly TextBox _client;
        private readonly string _filePath;

        public P4WorkspaceSettingsDialog(string filePath, P4WorkspaceContext? workspace, string settingsPath)
        {
            _filePath = Path.GetFullPath(filePath);
            Title = "设置 P4 工作区";
            Width = 620;
            Height = 390;
            MinWidth = 520;
            MinHeight = 360;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var panel = new Grid { Margin = new Thickness(16) };
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var index = 0; index < 7; index++)
            {
                panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var description = new TextBlock
            {
                Text = "该设置按本地 Root 匹配文件，并优先于环境变量。不会保存密码；P4 登录票据仍由 P4 管理。",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14),
            };
            Grid.SetRow(description, 0);
            Grid.SetColumnSpan(description, 2);
            panel.Children.Add(description);

            _root = AddField(panel, 1, "本地 Root", workspace?.Root ?? Path.GetDirectoryName(filePath) ?? string.Empty);
            _port = AddField(panel, 2, "P4PORT", workspace?.ConnectionAddress ?? string.Empty);
            _user = AddField(panel, 3, "P4USER", workspace?.User ?? string.Empty);
            _client = AddField(panel, 4, "P4CLIENT", workspace?.Client ?? string.Empty);

            var pathLabel = new TextBlock
            {
                Text = "共享配置文件：" + settingsPath,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7,
                Margin = new Thickness(0, 12, 0, 0),
            };
            Grid.SetRow(pathLabel, 5);
            Grid.SetColumnSpan(pathLabel, 2);
            panel.Children.Add(pathLabel);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 18, 0, 0),
            };
            var save = new Button
            {
                Content = "保存",
                IsDefault = true,
                MinWidth = 88,
                Margin = new Thickness(0, 0, 8, 0),
            };
            save.Click += OnSave;
            var cancel = new Button
            {
                Content = "取消",
                IsCancel = true,
                MinWidth = 88,
            };
            buttons.Children.Add(save);
            buttons.Children.Add(cancel);
            Grid.SetRow(buttons, 8);
            Grid.SetColumnSpan(buttons, 2);
            panel.Children.Add(buttons);

            Content = panel;
        }

        public string RootValue => _root.Text.Trim();

        public string PortValue => _port.Text.Trim();

        public string UserValue => _user.Text.Trim();

        public string ClientValue => _client.Text.Trim();

        private static TextBox AddField(Grid panel, int row, string label, string value)
        {
            var textLabel = new TextBlock
            {
                Text = label + "：",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 12, 4),
            };
            Grid.SetRow(textLabel, row);
            panel.Children.Add(textLabel);

            var textBox = new TextBox
            {
                Text = value,
                MinWidth = 380,
                Margin = new Thickness(0, 4, 0, 4),
                Padding = new Thickness(5, 3, 5, 3),
            };
            Grid.SetRow(textBox, row);
            Grid.SetColumn(textBox, 1);
            panel.Children.Add(textBox);
            return textBox;
        }

        private void OnSave(object sender, RoutedEventArgs args)
        {
            if (string.IsNullOrWhiteSpace(RootValue)
                || string.IsNullOrWhiteSpace(PortValue)
                || string.IsNullOrWhiteSpace(UserValue)
                || string.IsNullOrWhiteSpace(ClientValue))
            {
                MessageBox.Show(
                    this,
                    "本地 Root、P4PORT、P4USER 和 P4CLIENT 都必须填写。",
                    "P4 Histroy Plugin",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!Directory.Exists(RootValue))
            {
                MessageBox.Show(
                    this,
                    "本地 Root 目录不存在，请检查路径。",
                    "P4 Histroy Plugin",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var fullRoot = Path.GetFullPath(RootValue)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(_filePath, fullRoot, StringComparison.OrdinalIgnoreCase)
                && !_filePath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    this,
                    "当前文件不在填写的本地 Root 目录下。",
                    "P4 Histroy Plugin",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
        }
    }
}
