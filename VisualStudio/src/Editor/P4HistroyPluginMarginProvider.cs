using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using P4HistroyPlugin.Core;

namespace P4HistroyPlugin.Editor
{
    [Export(typeof(IWpfTextViewMarginProvider))]
    [Name(P4HistroyPluginMargin.MarginName)]
    [MarginContainer(PredefinedMarginNames.Left)]
    [Order(After = PredefinedMarginNames.LineNumber)]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class P4HistroyPluginMarginProvider : IWpfTextViewMarginProvider
    {
        private readonly ITextDocumentFactoryService _documentFactory;
        private readonly P4AnnotationService _annotationService = new P4AnnotationService();

        [ImportingConstructor]
        public P4HistroyPluginMarginProvider(ITextDocumentFactoryService documentFactory)
        {
            _documentFactory = documentFactory;
        }

        public IWpfTextViewMargin? CreateMargin(IWpfTextViewHost textViewHost, IWpfTextViewMargin containerMargin)
        {
            var documentBuffer = textViewHost.TextView.TextDataModel.DocumentBuffer;
            return _documentFactory.TryGetTextDocument(documentBuffer, out var document)
                ? new P4HistroyPluginMargin(textViewHost.TextView, document.FilePath, _annotationService)
                : null;
        }
    }

    internal sealed class P4HistroyPluginMargin : IWpfTextViewMargin
    {
        public const string MarginName = "P4HistroyPluginMargin";

        private const double ExpandedWidth = 184;
        private const double LoadingWidth = 84;
        private const double ErrorWidth = 48;
        private const double CollapsedWidth = 24;
        private const double BaseFontSize = 10.5;
        private const double MinimumFontSize = 9;
        private const double MaximumFontSize = 16;

        private readonly IWpfTextView _view;
        private readonly string _filePath;
        private readonly P4AnnotationService _annotationService;
        private readonly Border _root;
        private readonly Grid _layoutRoot;
        private readonly Canvas _canvas;
        private readonly Button _toggleButton;
        private readonly DispatcherTimer _editTimer;
        private CancellationTokenSource? _loadCancellation;
        private IReadOnlyList<AnnotationLine> _baseline = Array.Empty<AnnotationLine>();
        private AnnotationLine[] _mapped = Array.Empty<AnnotationLine>();
        private P4WorkspaceContext? _workspace;
        private double _expandedWidth = LoadingWidth;
        private object? _expandedToolTip;
        private bool _isCollapsed;
        private bool _disposed;

        public P4HistroyPluginMargin(IWpfTextView view, string filePath, P4AnnotationService annotationService)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            _annotationService = annotationService ?? throw new ArgumentNullException(nameof(annotationService));

            _canvas = new Canvas
            {
                ClipToBounds = true,
                Background = _view.Background,
                VerticalAlignment = VerticalAlignment.Stretch,
            };

            _toggleButton = new Button
            {
                Width = 20,
                Height = 18,
                Margin = new Thickness(0, 1, 2, 0),
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = SystemColors.GrayTextBrush,
                Cursor = Cursors.Hand,
                Focusable = false,
                FontSize = 14,
                Opacity = 0.35,
                Content = "‹",
                ToolTip = "折叠 P4 Histroy Plugin",
            };
            _toggleButton.Click += OnToggleButtonClick;
            Panel.SetZIndex(_toggleButton, 1);

            _layoutRoot = new Grid
            {
                ClipToBounds = true,
                Background = _view.Background,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            _layoutRoot.Children.Add(_canvas);
            _layoutRoot.Children.Add(_toggleButton);

            _root = new Border
            {
                Width = LoadingWidth,
                ClipToBounds = true,
                Background = _view.Background,
                VerticalAlignment = VerticalAlignment.Stretch,
                BorderBrush = new SolidColorBrush(Color.FromArgb(55, 128, 128, 128)),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Child = _layoutRoot,
                ContextMenu = CreateMarginContextMenu(),
            };
            _root.MouseEnter += OnRootMouseEnter;
            _root.MouseLeave += OnRootMouseLeave;

            _editTimer = new DispatcherTimer(DispatcherPriority.Background, _view.VisualElement.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(180),
            };
            _editTimer.Tick += OnEditTimerTick;

            _view.LayoutChanged += OnLayoutChanged;
            _view.TextBuffer.Changed += OnBufferChanged;
            _view.Closed += OnViewClosed;

            SyncTheme();
            ShowStatus("P4 加载…");
            _ = RefreshAsync(invalidateWorkspace: false);
        }

        public FrameworkElement VisualElement => _root;

        public double MarginSize => _root.ActualWidth;

        public bool Enabled => !_disposed && _root.Width > 0;

        public ITextViewMargin? GetTextViewMargin(string marginName)
        {
            return string.Equals(marginName, MarginName, StringComparison.OrdinalIgnoreCase) ? this : null;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _loadCancellation?.Cancel();
            _loadCancellation?.Dispose();
            _loadCancellation = null;
            _editTimer.Stop();
            _editTimer.Tick -= OnEditTimerTick;
            _view.LayoutChanged -= OnLayoutChanged;
            _view.TextBuffer.Changed -= OnBufferChanged;
            _view.Closed -= OnViewClosed;
            _root.MouseEnter -= OnRootMouseEnter;
            _root.MouseLeave -= OnRootMouseLeave;
            _toggleButton.Click -= OnToggleButtonClick;
            _canvas.Children.Clear();
        }

        private async Task RefreshAsync(bool invalidateWorkspace)
        {
            _loadCancellation?.Cancel();
            _loadCancellation?.Dispose();
            _loadCancellation = new CancellationTokenSource();
            var cancellationToken = _loadCancellation.Token;

            if (invalidateWorkspace)
            {
                _annotationService.Invalidate(_filePath);
            }

            SetExpandedPresentation(LoadingWidth, "P4 Histroy Plugin 正在查询…");
            ShowStatus("P4 加载…");

            try
            {
                var result = await _annotationService.LoadAsync(_filePath, cancellationToken);
                if (_disposed || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (!result.Succeeded)
                {
                    _baseline = Array.Empty<AnnotationLine>();
                    _mapped = Array.Empty<AnnotationLine>();
                    _workspace = null;
                    SetExpandedPresentation(ErrorWidth, $"P4 Histroy Plugin：{result.Error}\n右键可重新查询");
                    ShowStatus("P4 !");
                    return;
                }

                _baseline = result.Annotations;
                _workspace = result.Workspace;
                SetExpandedPresentation(
                    ExpandedWidth,
                    $"P4 server: {_workspace?.ConnectionAddress}\nP4 client: {_workspace?.Client}\n右键刷新");
                RemapAndRender();
            }
            catch (OperationCanceledException)
            {
                // A newer refresh or view close owns the UI now.
            }
        }

        private void RemapAndRender()
        {
            if (_disposed || _baseline.Count == 0)
            {
                return;
            }

            var snapshot = _view.TextSnapshot;
            var lines = new string[snapshot.LineCount];
            for (var i = 0; i < lines.Length; i++)
            {
                lines[i] = snapshot.GetLineFromLineNumber(i).GetText();
            }

            _mapped = LineAnnotationMapper.Map(_baseline, lines);
            RenderVisibleLines();
        }

        private void RenderVisibleLines()
        {
            if (_disposed || _isCollapsed || _mapped.Length == 0 || _view.TextViewLines == null)
            {
                return;
            }

            SyncTheme();
            _canvas.Children.Clear();
            var zoomScale = GetZoomScale();
            foreach (var viewLine in _view.TextViewLines)
            {
                if (!viewLine.IsFirstTextViewLineForSnapshotLine)
                {
                    continue;
                }

                var lineNumber = viewLine.Start.GetContainingLine().LineNumber;
                if (lineNumber < 0 || lineNumber >= _mapped.Length)
                {
                    continue;
                }

                var annotation = _mapped[lineNumber];
                var label = CreateAnnotationLabel(annotation, viewLine.Height, zoomScale);
                Canvas.SetLeft(label, 5);
                Canvas.SetTop(label, (viewLine.TextTop - _view.ViewportTop) * zoomScale);
                _canvas.Children.Add(label);
            }
        }

        private TextBlock CreateAnnotationLabel(AnnotationLine annotation, double lineHeight, double zoomScale)
        {
            var label = new TextBlock
            {
                Width = ExpandedWidth - 8,
                Height = Math.Max(1, lineHeight * zoomScale),
                Padding = new Thickness(0, 1, 2, 0),
                FontFamily = new FontFamily("Consolas"),
                FontSize = GetAnnotationFontSize(zoomScale),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = annotation.IsLocal
                    ? new SolidColorBrush(Color.FromRgb(214, 132, 46))
                    : SystemColors.GrayTextBrush,
                Opacity = annotation.IsLocal ? 0.95 : 0.82,
            };

            if (annotation.IsLocal)
            {
                label.Text = "本地修改";
                label.ToolTip = "这一行相对 workspace 的 #have 版本有本地改动，尚无已提交归属。";
                return label;
            }

            label.Text = $"{annotation.User} · {annotation.Change}";
            label.ToolTip = BuildToolTip(annotation);
            label.Cursor = Cursors.Hand;
            label.MouseLeftButtonDown += (_, args) =>
            {
                if (args.ClickCount == 2 && _workspace != null)
                {
                    _annotationService.OpenChange(_workspace, annotation.Change);
                    args.Handled = true;
                }
            };
            label.ContextMenu = CreateLineContextMenu(annotation);
            return label;
        }

        private ContextMenu CreateMarginContextMenu()
        {
            var menu = new ContextMenu();
            var toggle = new MenuItem();
            toggle.Click += (_, __) => ToggleCollapsed();
            menu.Opened += (_, __) => toggle.Header = _isCollapsed
                ? "展开 P4 行归属"
                : "折叠 P4 行归属";
            menu.Items.Add(toggle);

            var refresh = new MenuItem { Header = "刷新 P4 行归属" };
            refresh.Click += (_, __) => _ = RefreshAsync(invalidateWorkspace: true);
            menu.Items.Add(refresh);
            return menu;
        }

        private ContextMenu CreateLineContextMenu(AnnotationLine annotation)
        {
            var menu = CreateMarginContextMenu();
            menu.Items.Insert(0, new Separator());

            var copy = new MenuItem { Header = $"复制 CL {annotation.Change}" };
            copy.Click += (_, __) =>
            {
                try
                {
                    Clipboard.SetText(annotation.Change.ToString(CultureInfo.InvariantCulture));
                }
                catch
                {
                    // Clipboard can be temporarily locked by another process.
                }
            };
            menu.Items.Insert(0, copy);

            var open = new MenuItem { Header = $"在 P4V 中打开 CL {annotation.Change}" };
            open.Click += (_, __) =>
            {
                if (_workspace != null)
                {
                    _annotationService.OpenChange(_workspace, annotation.Change);
                }
            };
            menu.Items.Insert(0, open);
            return menu;
        }

        private string BuildToolTip(AnnotationLine annotation)
        {
            var time = annotation.ModifiedAt?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "未知";
            return $"提交人：{annotation.User}\nChangelist：{annotation.Change}\n时间：{time}\n提交 workspace：{annotation.Client}\n当前 server：{_workspace?.ConnectionAddress}\n当前 client：{_workspace?.Client}\n\n双击在 P4V 中打开";
        }

        private void ShowStatus(string text)
        {
            _canvas.Children.Clear();
            var label = new TextBlock
            {
                Text = text,
                Margin = new Thickness(5, 3, 2, 0),
                Foreground = SystemColors.GrayTextBrush,
                FontSize = GetAnnotationFontSize(GetZoomScale()),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            _canvas.Children.Add(label);
        }

        private void SetExpandedPresentation(double width, object? toolTip)
        {
            _expandedWidth = width;
            _expandedToolTip = toolTip;
            ApplyCollapsedState();
        }

        private void ToggleCollapsed()
        {
            if (_disposed)
            {
                return;
            }

            _isCollapsed = !_isCollapsed;
            ApplyCollapsedState();

            if (!_isCollapsed && _mapped.Length > 0)
            {
                RenderVisibleLines();
            }
        }

        private void ApplyCollapsedState()
        {
            _root.Width = _isCollapsed ? CollapsedWidth : _expandedWidth;
            _root.ToolTip = _isCollapsed ? "展开 P4 Histroy Plugin" : _expandedToolTip;
            _canvas.Visibility = _isCollapsed ? Visibility.Collapsed : Visibility.Visible;
            _toggleButton.Content = _isCollapsed ? "›" : "‹";
            _toggleButton.ToolTip = _isCollapsed ? "展开 P4 Histroy Plugin" : "折叠 P4 Histroy Plugin";
            _toggleButton.Opacity = _isCollapsed ? 0.85 : 0.35;
            SyncTheme();
        }

        private void SyncTheme()
        {
            var background = _view.Background;
            _root.Background = background;
            _layoutRoot.Background = background;
            _canvas.Background = background;
        }

        private double GetZoomScale()
        {
            var scale = _view.ZoomLevel / 100.0;
            return double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0 ? 1 : scale;
        }

        private static double GetAnnotationFontSize(double zoomScale)
        {
            return Math.Max(MinimumFontSize, Math.Min(MaximumFontSize, BaseFontSize * zoomScale));
        }

        private void OnToggleButtonClick(object sender, RoutedEventArgs args)
        {
            ToggleCollapsed();
            args.Handled = true;
        }

        private void OnRootMouseEnter(object sender, MouseEventArgs args)
        {
            _toggleButton.Opacity = 0.9;
        }

        private void OnRootMouseLeave(object sender, MouseEventArgs args)
        {
            _toggleButton.Opacity = _isCollapsed ? 0.85 : 0.35;
        }

        private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs args)
        {
            SyncTheme();
            RenderVisibleLines();
        }

        private void OnBufferChanged(object sender, TextContentChangedEventArgs args)
        {
            if (_baseline.Count == 0)
            {
                return;
            }

            _editTimer.Stop();
            _editTimer.Start();
        }

        private void OnEditTimerTick(object sender, EventArgs args)
        {
            _editTimer.Stop();
            RemapAndRender();
        }

        private void OnViewClosed(object sender, EventArgs args)
        {
            Dispose();
        }
    }
}
