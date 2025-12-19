using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Avalonia_Typing.Views;

public partial class MainView : UserControl
{
    /// <summary>
    /// 字符输入状态
    /// </summary>
    private enum CharState
    {
        NotTyped, // 未输入（灰色）
        Correct, // 输入正确（绿色）
        Incorrect // 输入错误（红色）
    }

    private const double TextFontSize = 24;
    private const double LineSpacing = 50;
    private const int VisibleLines = 5;
    private readonly List<TextBlock> _characterBlocks = new();
    private readonly List<Border> _characterBorders = new(); // 记录每个字符的内层 Border（用于设置背景色）
    private readonly List<Border> _outerBorders = new(); // 记录每个字符的外层 Border（用于设置边距）
    private readonly List<CharState> _characterStates = new(); // 记录每个字符的输入状态
    private int _currentCharIndex = -1;
    private Timer? _resizeDebounceTimer;
    private const int ResizeDebounceDelay = 100; // 延迟100毫秒
    private bool _lineSpacingApplied;
    private bool _isScrolling; // 标志：是否正在滚动，防止连续滚动
    private bool _testStarted; // 标志：是否已开始测试
    private DateTime _testStartTime; // 测试开始时间
    private Timer? _statsUpdateTimer; // 统计信息更新定时器
    private const int StatsUpdateInterval = 100; // 统计信息更新间隔（毫秒）

    public MainView()
    {
        InitializeComponent();

        // 统一设置上区域所有 TextBlock 的字体
        var fontFamily =
            new FontFamily("Google Sans Code, Ubuntu Mono, Consolas, HarmonyOS Sans SC, Noto Sans CJK SC, 微软雅黑");
        if (TopInfoGrid != null)
        {
            var textBlocks = TopInfoGrid.GetVisualDescendants().OfType<TextBlock>();
            foreach (var textBlock in textBlocks)
            {
                textBlock.FontFamily = fontFamily;
            }
        }

        // 设置 ScrollViewer 的高度：5行，每行高度 = 字体高度 + 底部边距
        // 每行实际占用 = TextFontSize + LineSpacing = 24 + 50 = 74
        // 5行总高度 = 5 * 74 = 370
        if (TextScrollViewer != null)
        {
            var height = VisibleLines * (TextFontSize + LineSpacing);
            TextScrollViewer.Height = height;
        }

        // 监听窗口尺寸变化，使用防抖机制延迟更新布局
        this.AttachedToVisualTree += MainView_AttachedToVisualTree;
    }

    private void MainView_AttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        // 获取父窗口
        var window = this.GetVisualAncestors().OfType<Window>().FirstOrDefault();
        if (window != null)
        {
            // 监听窗口尺寸变化（包括放大和缩小）
            // 监听多个属性以确保在放大和缩小时都能触发
            window.PropertyChanged += (_, args) =>
            {
                if (args.Property == Control.WidthProperty ||
                    args.Property == Control.HeightProperty ||
                    args.Property == Control.BoundsProperty)
                {
                    // 使用防抖机制，延迟更新布局
                    DebounceResize();
                }
            };
        }
    }

    /// <summary>
    /// 防抖更新布局
    /// </summary>
    private void DebounceResize()
    {
        // 取消之前的定时器
        _resizeDebounceTimer?.Dispose();

        // 创建新的定时器，延迟更新
        _resizeDebounceTimer =
            new Timer(_ => { Avalonia.Threading.Dispatcher.UIThread.Post(() => { UpdateWrapPanelWidth(); }); }, null,
                ResizeDebounceDelay, Timeout.Infinite);
    }

    /// <summary>
    /// 更新 WrapPanel 的宽度
    /// </summary>
    private void UpdateWrapPanelWidth()
    {
        if (TextScrollViewer == null || TextContentPanel == null) return;

        var availableWidth = TextScrollViewer.Bounds.Width;
        if (availableWidth > 0)
        {
            // 使用实际可用宽度，但不超过 MaxWidth
            var maxWidth = TextScrollViewer.MaxWidth;
            if (double.IsNaN(maxWidth) || maxWidth <= 0)
            {
                maxWidth = 1200;
            }

            TextContentPanel.MaxWidth = Math.Min(availableWidth, maxWidth);
        }
    }

    /// <summary>
    /// 格式化百分比值，精确到小数点后2位，末尾0省略
    /// </summary>
    private string FormatPercentage(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return "0";
        }

        // 格式化为2位小数
        var formatted = value.ToString("F2");

        // 移除末尾的0
        if (formatted.Contains('.'))
        {
            formatted = formatted.TrimEnd('0');
            formatted = formatted.TrimEnd('.');
        }

        return formatted;
    }

    /// <summary>
    /// 更新已输入字符数显示
    /// </summary>
    public void UpdateTypedChars(int typed, int total)
    {
        TypedCharsText.Text = typed.ToString();
        TotalCharsText.Text = total.ToString();
    }

    /// <summary>
    /// 更新完成率显示
    /// </summary>
    public void UpdateCompletionRate(double rate)
    {
        CompletionRateText.Text = FormatPercentage(rate);
    }

    /// <summary>
    /// 更新正确率显示
    /// </summary>
    public void UpdateAccuracyRate(double rate)
    {
        AccuracyRateText.Text = FormatPercentage(rate);
    }

    /// <summary>
    /// 更新速度显示
    /// </summary>
    public void UpdateSpeed(int speed)
    {
        SpeedText.Text = speed.ToString();
    }

    /// <summary>
    /// 更新用时显示
    /// </summary>
    public void UpdateElapsedTime(int hours, int minutes, int seconds)
    {
        HoursText.Text = hours.ToString("D2");
        MinutesText.Text = minutes.ToString("D2");
        SecondsText.Text = seconds.ToString("D2");
    }

    /// <summary>
    /// 更新姓名显示
    /// </summary>
    public void UpdateName(string name)
    {
        NameText.Text = name;
    }

    /// <summary>
    /// 开始测试
    /// </summary>
    private void StartTest()
    {
        if (_testStarted) return;

        _testStarted = true;
        _testStartTime = DateTime.Now; // 记录测试开始时间

        // 启动统计信息更新定时器
        StartStatsUpdateTimer();
    }

    /// <summary>
    /// 启动统计信息更新定时器
    /// </summary>
    private void StartStatsUpdateTimer()
    {
        StopStatsUpdateTimer(); // 先停止之前的定时器

        _statsUpdateTimer = new Timer(_ =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                UpdateStatistics();
            });
        }, null, 0, StatsUpdateInterval);
    }

    /// <summary>
    /// 停止统计信息更新定时器
    /// </summary>
    private void StopStatsUpdateTimer()
    {
        _statsUpdateTimer?.Dispose();
        _statsUpdateTimer = null;
    }

    /// <summary>
    /// 更新统计信息
    /// </summary>
    private void UpdateStatistics()
    {
        if (!_testStarted || _characterBlocks.Count == 0) return;

        // 计算已输入字符数（已输入状态的字符数）
        int typedChars = 0;
        int correctChars = 0;
        for (int i = 0; i < _characterStates.Count; i++)
        {
            if (_characterStates[i] != CharState.NotTyped)
            {
                typedChars++;
                if (_characterStates[i] == CharState.Correct)
                {
                    correctChars++;
                }
            }
        }

        int totalChars = _characterBlocks.Count;

        // 更新已输入字符数
        UpdateTypedChars(typedChars, totalChars);

        // 计算完成率
        double completionRate = totalChars > 0 ? (typedChars * 100.0 / totalChars) : 0;
        UpdateCompletionRate(completionRate);

        // 计算正确率
        double accuracyRate = typedChars > 0 ? (correctChars * 100.0 / typedChars) : 0;
        UpdateAccuracyRate(accuracyRate);

        // 计算用时（精确到毫秒）
        var elapsed = DateTime.Now - _testStartTime;
        int totalSeconds = (int)elapsed.TotalSeconds;
        int hours = totalSeconds / 3600;
        int minutes = (totalSeconds % 3600) / 60;
        int seconds = totalSeconds % 60;
        UpdateElapsedTime(hours, minutes, seconds);

        // 计算速度（字符/分钟）
        int speed = 0;
        if (elapsed.TotalMinutes > 0)
        {
            speed = (int)(typedChars / elapsed.TotalMinutes);
        }
        UpdateSpeed(speed);
    }

    /// <summary>
    /// 加载文章文本（英文形式）
    /// </summary>
    public void LoadText(string text)
    {
        if (TextContentPanel == null || TextScrollViewer == null) return;

        // 清空现有内容
        TextContentPanel.Children.Clear();
        _characterBlocks.Clear();
        _characterBorders.Clear();
        _outerBorders.Clear();
        _characterStates.Clear();
        _currentCharIndex = -1;
        _lineSpacingApplied = false; // 重置行距应用标志
        _testStarted = false; // 重置测试开始标志
        StopStatsUpdateTimer(); // 停止统计信息更新定时器

        // 初始设置 WrapPanel 的宽度
        UpdateWrapPanelWidth();

        // 将换行符替换为空格（统一处理各种换行符：\n, \r, \r\n）
        text = text.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
        // 修剪文本起始和结束的空白字符
        text = text.Trim();

        // 创建字体
        var fontFamily =
            new FontFamily("Google Sans Code, Ubuntu Mono, Consolas, HarmonyOS Sans SC, Noto Sans CJK SC, 微软雅黑");

        // 为每个字符创建双层 Border 结构：
        // - 外层 Border：负责边距（行距），无背景色
        // - 内层 Border：负责背景色，无边距，不拉伸（只占用文本高度）
        // - TextBlock：只显示文本
        foreach (var ch in text)
        {
            var textBlock = new TextBlock
            {
                Text = ch.ToString(),
                FontSize = TextFontSize,
                FontFamily = fontFamily,
                Foreground = new SolidColorBrush(Colors.Gray) // 默认灰色
            };

            // 内层 Border：设置背景色，不拉伸，只占用文本实际高度
            var innerBorder = new Border
            {
                Child = textBlock,
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, // 顶部对齐，不拉伸
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                ClipToBounds = true // 限制背景色不超出字符范围
            };

            // 外层 Border：设置边距（行距），无背景色
            var outerBorder = new Border
            {
                Child = innerBorder,
                Padding = new Thickness(0),
                Margin = new Thickness(0), // 初始无边距，行距稍后应用
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
            };

            _characterBlocks.Add(textBlock);
            _characterBorders.Add(innerBorder); // 存储内层 Border（用于设置背景色）
            _outerBorders.Add(outerBorder); // 存储外层 Border（用于设置边距）
            _characterStates.Add(CharState.NotTyped); // 初始状态为未输入
            TextContentPanel.Children.Add(outerBorder); // 添加外层 Border
        }

        // 等待布局完成后，只给每行的最后一个字符设置底部边距
        ApplyLineSpacingAfterLayout();

        // 设置当前字符为第一个
        if (_characterBlocks.Count > 0)
        {
            SetCurrentCharIndex(0);
        }
    }

    /// <summary>
    /// 在布局完成后应用行距（只给每行的最后一个字符设置底部边距）
    /// </summary>
    private void ApplyLineSpacingAfterLayout()
    {
        if (TextContentPanel == null || _outerBorders.Count == 0 || _lineSpacingApplied) return;

        void ApplyLineSpacing()
        {
            if (_lineSpacingApplied || TextContentPanel == null || _outerBorders.Count == 0) return;

            // 检查布局是否完成（至少有一个外层 Border 有有效的高度）
            bool layoutComplete = false;
            foreach (var border in _outerBorders)
            {
                if (border.Bounds.Height > 0)
                {
                    layoutComplete = true;
                    break;
                }
            }

            if (!layoutComplete) return;

            // 标记为已应用，避免重复执行
            _lineSpacingApplied = true;

            // 使用 Dispatcher 延迟执行，避免在布局更新过程中修改布局
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (TextContentPanel == null || _outerBorders.Count == 0) return;

                // 先清除所有外层 Border 的底部边距
                foreach (var border in _outerBorders)
                {
                    border.Margin = new Thickness(0);
                }

                // 通过比较相邻外层 Border 的Y坐标来判断行尾（最可靠的方法）
                // 同时处理空格字符：如果行尾是空格，则使用该行最后一个非空格字符
                var lineEnds = new HashSet<Border>();
                var lineStartIndex = 0;

                for (int i = 0; i < _outerBorders.Count; i++)
                {
                    var currentOuterBorder = _outerBorders[i];
                    var currentY = Math.Round(currentOuterBorder.Bounds.Top, 1);

                    bool isLineEnd = false;

                    // 如果是最后一个字符，一定是行尾
                    if (i == _outerBorders.Count - 1)
                    {
                        isLineEnd = true;
                    }
                    else
                    {
                        var nextOuterBorder = _outerBorders[i + 1];
                        var nextY = Math.Round(nextOuterBorder.Bounds.Top, 1);

                        // 如果下一个字符换行了（Y坐标不同，允许1像素误差），当前字符是行尾
                        if (Math.Abs(nextY - currentY) > 1)
                        {
                            isLineEnd = true;
                        }
                    }

                    if (isLineEnd)
                    {
                        // 找到这一行的最后一个非空格字符
                        Border? lastNonSpaceOuterBorder = null;
                        for (int j = i; j >= lineStartIndex; j--)
                        {
                            var charText = _characterBlocks[j].Text;
                            if (!string.IsNullOrWhiteSpace(charText))
                            {
                                lastNonSpaceOuterBorder = _outerBorders[j];
                                break;
                            }
                        }

                        // 如果找到了非空格字符，使用它；否则使用当前字符（可能是整行都是空格）
                        if (lastNonSpaceOuterBorder != null)
                        {
                            lineEnds.Add(lastNonSpaceOuterBorder);
                        }
                        else
                        {
                            lineEnds.Add(currentOuterBorder);
                        }

                        lineStartIndex = i + 1; // 下一行的起始索引
                    }
                }

                // 给每行的最后一个字符的外层 Border 设置底部边距
                foreach (var lastOuterBorderInLine in lineEnds)
                {
                    lastOuterBorderInLine.Margin = new Thickness(0, 0, 0, LineSpacing);
                }
            }, Avalonia.Threading.DispatcherPriority.Background);
        }

        // 等待布局更新（只执行一次）
        TextContentPanel.LayoutUpdated += (_, _) =>
        {
            if (!_lineSpacingApplied)
            {
                ApplyLineSpacing();
            }
        };
    }

    /// <summary>
    /// 设置当前字符索引
    /// </summary>
    private void SetCurrentCharIndex(int index)
    {
        if (index < 0 || index >= _characterBlocks.Count) return;

        // 恢复之前当前字符的颜色和背景（根据状态）
        if (_currentCharIndex >= 0 && _currentCharIndex < _characterBlocks.Count)
        {
            var previousBlock = _characterBlocks[_currentCharIndex];
            var previousBorder = _characterBorders[_currentCharIndex];
            var previousState = _characterStates[_currentCharIndex];
            var previousIsSpace = !string.IsNullOrEmpty(previousBlock.Text) && char.IsWhiteSpace(previousBlock.Text[0]);

            // 恢复前景色
            previousBlock.Foreground = GetColorForState(previousState);

            // 恢复背景色：如果是空格且状态是 Incorrect，保持红色背景；否则清除背景
            // 背景色设置在内层 Border 上，不会延伸到边距区域
            if (previousIsSpace && previousState == CharState.Incorrect)
            {
                previousBorder.Background = new SolidColorBrush(Colors.IndianRed);
            }
            else
            {
                previousBorder.Background = null;
            }
        }

        _currentCharIndex = index;

        // 设置当前字符为白色，并设置背景色
        UpdateCurrentCharAppearance();

        // 滚动到当前字符
        ScrollToCurrentChar();
    }

    /// <summary>
    /// 更新当前字符的外观（前景色和背景色）
    /// </summary>
    private void UpdateCurrentCharAppearance()
    {
        if (_currentCharIndex < 0 || _currentCharIndex >= _characterBlocks.Count) return;

        var currentChar = _characterBlocks[_currentCharIndex];
        var currentBorder = _characterBorders[_currentCharIndex];

        // 设置前景色为白色（当前字符）
        currentChar.Foreground = new SolidColorBrush(Colors.White);

        // 当前字符始终使用深灰色背景来突出显示
        // 背景色设置在内层 Border 上，不会延伸到边距区域
        // 空格打错的红色背景只在输入错误时立即显示，移动到下一个字符后会恢复为正常状态
        // 如果退格回到空格，空格作为当前字符应该显示深灰色背景，而不是红色背景
        currentBorder.Background = new SolidColorBrush(Color.FromRgb(60, 100, 160)); // 深灰色背景
    }

    /// <summary>
    /// 根据状态获取颜色
    /// </summary>
    private SolidColorBrush GetColorForState(CharState state)
    {
        return state switch
        {
            CharState.NotTyped => new SolidColorBrush(Colors.Gray),
            CharState.Correct => new SolidColorBrush(Colors.MediumSeaGreen),
            CharState.Incorrect => new SolidColorBrush(Colors.IndianRed),
            _ => new SolidColorBrush(Colors.Gray)
        };
    }

    /// <summary>
    /// 处理输入字符
    /// </summary>
    public void HandleInput(char inputChar)
    {
        if (_currentCharIndex < 0 || _currentCharIndex >= _characterBlocks.Count) return;

        // 检测是否是第一个字符输入（开始测试）
        if (!_testStarted && _currentCharIndex == 0)
        {
            StartTest();
        }

        var currentChar = _characterBlocks[_currentCharIndex];
        if (string.IsNullOrEmpty(currentChar.Text)) return;

        var expectedChar = currentChar.Text[0];

        var isSpace = char.IsWhiteSpace(expectedChar);
        var currentBorder = _characterBorders[_currentCharIndex];

        if (inputChar == expectedChar)
        {
            // 输入正确，显示绿色
            currentChar.Foreground = new SolidColorBrush(Colors.MediumSeaGreen);
            currentBorder.Background = null; // 清除背景色（设置在内层 Border 上）
            _characterStates[_currentCharIndex] = CharState.Correct;
        }
        else
        {
            // 输入错误
            _characterStates[_currentCharIndex] = CharState.Incorrect;

            if (isSpace)
            {
                // 如果是空格打错，使用红色背景，白色前景
                // 背景色设置在内层 Border 上，不会延伸到边距区域
                currentChar.Foreground = new SolidColorBrush(Colors.White);
                currentBorder.Background = new SolidColorBrush(Colors.IndianRed);
            }
            else
            {
                // 非空格打错，显示红色前景，无背景
                currentChar.Foreground = new SolidColorBrush(Colors.IndianRed);
                currentBorder.Background = null;
            }
        }

        // 移动到下一个字符
        if (_currentCharIndex < _characterBlocks.Count - 1)
        {
            SetCurrentCharIndex(_currentCharIndex + 1);

            // 检测是否输入完当前视口内最后一行的最后一个字符
            // 只有当当前字符在视口底部，并且是这一行的最后一个字符时，才向下滚动4行
            if (_currentCharIndex >= 0 && _currentCharIndex < _outerBorders.Count && TextScrollViewer != null && !_isScrolling)
            {
                var currentOuterBorder = _outerBorders[_currentCharIndex];
                var scrollViewerBounds = TextScrollViewer.Bounds;
                var transform = currentOuterBorder.TransformToVisual(TextScrollViewer);
                
                if (transform.HasValue)
                {
                    var matrix = transform.Value;
                    var point = matrix.Transform(new Point(0, 0));
                    var y = point.Y;
                    var bounds = currentOuterBorder.Bounds;
                    
                    // 检测条件：
                    // 1. 当前字符在视口底部（视口底部90%位置以下）
                    // 2. 当前字符是它所在行的最后一个字符
                    var isAtBottom = y + bounds.Height >= scrollViewerBounds.Height * 0.9;
                    var isLastInLine = IsLastCharInLine(_currentCharIndex);
                    
                    if (isAtBottom && isLastInLine)
                    {
                        SmoothScrollLines(4); // 向下滚动4行
                    }
                }
            }
        }
    }

    /// <summary>
    /// 处理退格键
    /// </summary>
    public void HandleBackspace()
    {
        if (_currentCharIndex <= 0) return; // 已经在第一个字符，无法再退

        // 将当前字符和之后已输入的字符都重置为灰色
        for (int i = _currentCharIndex; i < _characterBlocks.Count; i++)
        {
            if (_characterStates[i] != CharState.NotTyped)
            {
                _characterBlocks[i].Foreground = new SolidColorBrush(Colors.Gray);
                _characterBorders[i].Background = null; // 清除背景色（设置在内层 Border 上）
                _characterStates[i] = CharState.NotTyped;
            }
        }

        // 退回到上一个字符
        SetCurrentCharIndex(_currentCharIndex - 1);

        // 检测是否退到当前视口第一行第一个字符
        if (_currentCharIndex >= 0 && _currentCharIndex < _outerBorders.Count && TextScrollViewer != null && !_isScrolling)
        {
            var currentBorder = _outerBorders[_currentCharIndex];
            var scrollViewerBounds = TextScrollViewer.Bounds;
            var transform = currentBorder.TransformToVisual(TextScrollViewer);

            if (transform.HasValue)
            {
                var matrix = transform.Value;
                var point = matrix.Transform(new Point(0, 0));
                var y = point.Y;

                // 检测条件：
                // 1. 当前字符在视口顶部附近（视口顶部20%位置）
                // 2. 当前字符是它所在行的第一个字符
                var isAtTop = y <= scrollViewerBounds.Height * 0.2;
                var isFirstInLine = IsFirstCharInLine(_currentCharIndex);
                
                if (isAtTop && isFirstInLine)
                {
                    SmoothScrollLines(-1); // 向上滚动1行
                }
            }
        }
    }

    /// <summary>
    /// 检测指定字符是否是它所在行的最后一个字符
    /// </summary>
    private bool IsLastCharInLine(int charIndex)
    {
        if (charIndex < 0 || charIndex >= _outerBorders.Count) return false;
        if (charIndex == _outerBorders.Count - 1) return true; // 最后一个字符

        var currentBorder = _outerBorders[charIndex];
        var nextBorder = _outerBorders[charIndex + 1];
        
        // 通过比较Y坐标判断是否在同一行
        var currentY = Math.Round(currentBorder.Bounds.Top, 1);
        var nextY = Math.Round(nextBorder.Bounds.Top, 1);
        
        // 如果下一个字符换行了，当前字符是行尾
        return Math.Abs(nextY - currentY) > 1;
    }

    /// <summary>
    /// 检测指定字符是否是它所在行的第一个字符
    /// </summary>
    private bool IsFirstCharInLine(int charIndex)
    {
        if (charIndex < 0 || charIndex >= _outerBorders.Count) return false;
        if (charIndex == 0) return true; // 第一个字符

        var currentBorder = _outerBorders[charIndex];
        var previousBorder = _outerBorders[charIndex - 1];
        
        // 通过比较Y坐标判断是否在同一行
        var currentY = Math.Round(currentBorder.Bounds.Top, 1);
        var previousY = Math.Round(previousBorder.Bounds.Top, 1);
        
        // 如果上一个字符换行了，当前字符是行首
        return Math.Abs(previousY - currentY) > 1;
    }

    /// <summary>
    /// 精确测量实际行高（通过测量相邻行的实际距离）
    /// </summary>
    private double MeasureActualLineHeight()
    {
        if (_outerBorders.Count < 2) return TextFontSize + LineSpacing; // 如果字符太少，使用理论值

        // 找到两行相邻的字符，计算它们之间的实际距离
        for (int i = 0; i < _outerBorders.Count - 1; i++)
        {
            var currentBorder = _outerBorders[i];
            var nextBorder = _outerBorders[i + 1];
            
            var currentY = Math.Round(currentBorder.Bounds.Top, 1);
            var nextY = Math.Round(nextBorder.Bounds.Top, 1);
            
            // 如果下一个字符换行了（Y坐标不同），计算行高
            if (Math.Abs(nextY - currentY) > 1)
            {
                // 找到当前行的第一个字符
                var lineStartIndex = i;
                for (int j = i; j >= 0; j--)
                {
                    var border = _outerBorders[j];
                    var y = Math.Round(border.Bounds.Top, 1);
                    if (Math.Abs(y - currentY) > 1)
                    {
                        lineStartIndex = j + 1;
                        break;
                    }
                    if (j == 0) lineStartIndex = 0;
                }
                
                // 找到下一行的第一个字符
                var nextLineStartIndex = i + 1;
                
                // 计算两行第一个字符之间的实际距离
                var lineStartBorder = _outerBorders[lineStartIndex];
                var nextLineStartBorder = _outerBorders[nextLineStartIndex];
                
                var lineStartY = lineStartBorder.Bounds.Top;
                var nextLineStartY = nextLineStartBorder.Bounds.Top;
                
                var actualLineHeight = nextLineStartY - lineStartY;
                
                // 如果测量值合理（在理论值的80%-120%范围内），使用它
                var theoreticalHeight = TextFontSize + LineSpacing;
                if (actualLineHeight >= theoreticalHeight * 0.8 && actualLineHeight <= theoreticalHeight * 1.2)
                {
                    return actualLineHeight;
                }
            }
        }
        
        // 如果无法测量，使用理论值
        return TextFontSize + LineSpacing;
    }

    /// <summary>
    /// 平滑滚动指定行数
    /// </summary>
    private async void SmoothScrollLines(int lines)
    {
        if (TextScrollViewer == null || _isScrolling) return; // 如果正在滚动，不执行新的滚动

        _isScrolling = true; // 设置滚动标志

        try
        {
            // 使用精确测量的行高
            var lineHeight = MeasureActualLineHeight();
            var targetOffset = TextScrollViewer.Offset.Y + (lineHeight * lines);
            var startOffset = TextScrollViewer.Offset.Y;
            var distance = targetOffset - startOffset;
            var duration = TimeSpan.FromMilliseconds(300); // 300毫秒的动画时长
            var steps = 60; // 增加步骤数，让动画更流畅，减少残影
            var stepDelay = duration.TotalMilliseconds / steps;

            for (int i = 0; i <= steps; i++)
            {
                var progress = (double)i / steps;
                // 使用缓动函数（ease-in-out）
                var easedProgress = progress < 0.5
                    ? 2 * progress * progress
                    : 1 - Math.Pow(-2 * progress + 2, 2) / 2;
                
                var currentOffset = startOffset + distance * easedProgress;
                
                // 在UI线程上更新滚动位置
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (TextScrollViewer != null)
                    {
                        TextScrollViewer.Offset = new Vector(0, Math.Max(0, currentOffset));
                    }
                }, Avalonia.Threading.DispatcherPriority.Render); // 使用 Render 优先级确保及时渲染

                await Task.Delay((int)stepDelay);
            }
        }
        finally
        {
            _isScrolling = false; // 清除滚动标志
        }
    }

    /// <summary>
    /// 滚动到当前字符
    /// </summary>
    private void ScrollToCurrentChar()
    {
        if (_currentCharIndex < 0 || _currentCharIndex >= _outerBorders.Count) return;
        if (TextScrollViewer == null) return;
        if (_isScrolling) return; // 如果正在平滑滚动，不执行自动滚动

        var currentBorder = _outerBorders[_currentCharIndex];
        var savedIndex = _currentCharIndex; // 保存当前索引，避免在布局更新时索引改变

        // 等待布局完成后再滚动
        void OnLayoutUpdated(object? sender, EventArgs e)
        {
            try
            {
                if (TextScrollViewer == null) return;
                if (_isScrolling) return; // 如果正在平滑滚动，不执行自动滚动
                if (savedIndex != _currentCharIndex) 
                {
                    // 如果索引已改变，取消事件订阅并返回
                    currentBorder.LayoutUpdated -= OnLayoutUpdated;
                    return;
                }

                var bounds = currentBorder.Bounds;
                var scrollViewerBounds = TextScrollViewer.Bounds;

                // 如果边界无效，不滚动
                if (bounds.Height <= 0 || scrollViewerBounds.Height <= 0) return;

                // 计算当前字符在 ScrollViewer 中的位置
                var transform = currentBorder.TransformToVisual(TextScrollViewer);
                if (transform.HasValue)
                {
                    var matrix = transform.Value;
                    var point = matrix.Transform(new Point(0, 0));
                    var y = point.Y;

                    // 只有当当前字符完全不可见时才滚动（允许一些边距，避免误判）
                    // 如果字符在视口上方（y < -20）或下方（y + bounds.Height > scrollViewerBounds.Height + 20），才滚动
                    if (y < -20 || y + bounds.Height > scrollViewerBounds.Height + 20)
                    {
                        TextScrollViewer.Offset = new Vector(0, Math.Max(0, y - scrollViewerBounds.Height / 2));
                    }
                }
            }
            catch
            {
                // 忽略滚动错误
            }
            finally
            {
                currentBorder.LayoutUpdated -= OnLayoutUpdated;
            }
        }

        currentBorder.LayoutUpdated += OnLayoutUpdated;
    }
}