using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
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
    private EventHandler? _layoutUpdatedHandler; // 保存 LayoutUpdated 事件处理器，以便可以取消订阅
    private bool _isScrolling; // 标志：是否正在滚动，防止连续滚动
    private bool _testStarted; // 标志：是否已开始测试
    private DateTime _testStartTime; // 测试开始时间
    private Timer? _statsUpdateTimer; // 统计信息更新定时器
    private const int StatsUpdateInterval = 100; // 统计信息更新间隔（毫秒）
    private int _backspaceCount = 0; // 退格次数统计
    private bool _isCountdownEnabled = false; // 倒计时功能是否启用
    private int _countdownHours = 0; // 倒计时小时数
    private int _countdownMinutes = 0; // 倒计时分钟数
    private int _countdownSeconds = 0; // 倒计时秒数
    private int _remainingSeconds = 0; // 剩余秒数
    private Timer? _countdownTimer; // 倒计时定时器
    private bool _testEnded = false; // 测试是否已结束
    private DateTime _testEndTime; // 测试结束时间
    private string _currentArticleFolder = ""; // 当前文章文件夹名称
    private string _currentArticleName = ""; // 当前文章名称（不含 .txt）
    public event Action? TestEnded; // 测试结束事件
    public event Action<string, string>? ArticleReloadRequested; // 文章重新加载请求事件

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
        // 初始化完成率背景和显示为0（在控件加载后执行）
        InitializeCompletionRateBackground();
        UpdateCompletionRateDisplay(0);

        // 禁止 TextScrollViewer 的鼠标滚轮滚动
        if (TextScrollViewer != null)
        {
            TextScrollViewer.AddHandler(PointerWheelChangedEvent, (sender, args) =>
            {
                // 阻止滚轮事件，防止用户通过鼠标滚轮滚动 TextScrollViewer
                args.Handled = true;
            }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        }

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
    /// 初始化完成率背景为完全透明
    /// </summary>
    private void InitializeCompletionRateBackground()
    {
        if (CompletionRateBorder == null) return;

        var gradientBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative)
        };

        // 完成率为0，全部透明
        gradientBrush.GradientStops.Add(new GradientStop { Offset = 0, Color = Colors.Transparent });
        gradientBrush.GradientStops.Add(new GradientStop { Offset = 1, Color = Colors.Transparent });

        CompletionRateBorder.Background = gradientBrush;
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
            
            // 窗口尺寸变化后，文本会重新换行，需要重新应用行距
            // 重置行距应用标志，以便重新应用行距
            if (_outerBorders.Count > 0)
            {
                _lineSpacingApplied = false;
                // 延迟重新应用行距，等待布局完成
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    ApplyLineSpacingAfterLayout();
                    // 重新滚动到当前字符，确保当前字符在缩放后仍然可见
                    if (_currentCharIndex >= 0)
                    {
                        ScrollToCurrentChar();
                    }
                }, Avalonia.Threading.DispatcherPriority.Background);
            }
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
    /// 在英文中文间添加空格（与 generate-file-list.js 保持一致）
    /// </summary>
    private string AddSpaceBetweenEnglishAndChinese(string text)
    {
        // 中文字符后跟英文字母：在中文字符和英文字母之间添加空格
        text = Regex.Replace(text, @"([\u4e00-\u9fa5])([a-zA-Z])", "$1 $2");
        // 英文字母后跟中文字符：在英文字母和中文字符之间添加空格
        text = Regex.Replace(text, @"([a-zA-Z])([\u4e00-\u9fa5])", "$1 $2");
        return text;
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
    /// 更新退格次数显示
    /// </summary>
    public void UpdateBackspaceCount(int count)
    {
        if (BackspaceCountText != null)
        {
            BackspaceCountText.Text = count.ToString();
        }
    }

    /// <summary>
    /// 更新完成率显示（新的底部显示区域）
    /// </summary>
    public void UpdateCompletionRateDisplay(double rate)
    {
        if (CompletionRateValueText == null || CompletionRateBorder == null) return;

        // 格式化完成率文本
        var formattedRate = FormatPercentage(rate);

        // 更新数字部分，需要将数字和小数点分开显示
        // 先清除 Text 属性，因为 Text 和 Inlines 是互斥的
        CompletionRateValueText.Text = null;
        var valueInlines = CompletionRateValueText.Inlines;
        valueInlines?.Clear();

        // 解析数字部分，为数字和小数点设置不同颜色
        foreach (var ch in formattedRate)
        {
            if (ch == '.')
            {
                // 小数点用深灰色
                valueInlines?.Add(new Run(ch.ToString())
                {
                    Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88))
                });
            }
            else if (char.IsDigit(ch))
            {
                // 数字用蓝色
                valueInlines?.Add(new Run(ch.ToString())
                {
                    Foreground = new SolidColorBrush(Color.FromRgb(0x87, 0xCE, 0xFA))
                });
            }
        }

        // 设置渐变背景：完成率部分用颜色，剩余部分透明
        var completionPercent = Math.Max(0, Math.Min(100, rate)) / 100.0;

        // 创建或更新渐变背景
        var gradientBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative)
        };

        var colorBlue = new Color(0x70, 0x4A, 0x90, 0xE2);

        if (completionPercent <= 0)
        {
            // 完成率为0，全部透明
            gradientBrush.GradientStops.Add(new GradientStop { Offset = 0, Color = Colors.Transparent });
            gradientBrush.GradientStops.Add(new GradientStop { Offset = 1, Color = Colors.Transparent });
        }
        else if (completionPercent >= 1)
        {
            // 完成率为100%，全部有颜色
            gradientBrush.GradientStops.Add(new GradientStop { Offset = 0, Color = colorBlue });
            gradientBrush.GradientStops.Add(new GradientStop { Offset = 1, Color = colorBlue });
        }
        else
        {
            // 正常情况：从0到completionPercent使用颜色，从completionPercent到1使用透明
            gradientBrush.GradientStops.Add(new GradientStop { Offset = 0, Color = colorBlue });
            gradientBrush.GradientStops.Add(new GradientStop { Offset = completionPercent, Color = colorBlue });
            gradientBrush.GradientStops.Add(new GradientStop { Offset = completionPercent, Color = Colors.Transparent });
            gradientBrush.GradientStops.Add(new GradientStop { Offset = 1, Color = Colors.Transparent });
        }

        CompletionRateBorder.Background = gradientBrush;
    }

    /// <summary>
    /// 更新正确率显示
    /// </summary>
    public void UpdateAccuracyRate(double rate)
    {
        if (AccuracyRateText == null) return;

        // 格式化正确率文本
        var formattedRate = FormatPercentage(rate);

        // 更新数字部分，需要将数字和小数点分开显示
        // 先清除 Text 属性，因为 Text 和 Inlines 是互斥的
        AccuracyRateText.Text = null;
        var valueInlines = AccuracyRateText.Inlines;
        valueInlines?.Clear();

        // 解析数字部分，为数字和小数点设置不同颜色
        foreach (var ch in formattedRate)
        {
            if (ch == '.')
            {
                // 小数点用深灰色
                valueInlines?.Add(new Run(ch.ToString())
                {
                    Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88))
                });
            }
            else if (char.IsDigit(ch))
            {
                // 数字用蓝色
                valueInlines?.Add(new Run(ch.ToString())
                {
                    Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xE2))
                });
            }
        }
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
    /// 设置倒计时参数
    /// </summary>
    public void SetCountdown(bool enabled, int hours, int minutes, int seconds)
    {
        _isCountdownEnabled = enabled;
        _countdownHours = hours;
        _countdownMinutes = minutes;
        _countdownSeconds = seconds;
        _remainingSeconds = hours * 3600 + minutes * 60 + seconds;
        
        // 根据倒计时状态更新标签文本
        if (ElapsedTimeLabel != null)
        {
            ElapsedTimeLabel.Text = enabled ? "倒计时" : "用时";
        }
        
        // 如果倒计时已启用，立即更新显示
        if (enabled)
        {
            UpdateCountdownDisplay();
        }
        else
        {
            // 如果取消倒计时，重置显示为 00:00:00
            UpdateElapsedTime(0, 0, 0);
        }
    }

    /// <summary>
    /// 手动结束测试（用于点击结束按钮）
    /// </summary>
    public void EndTestManually()
    {
        EndTest("手动结束");
    }

    /// <summary>
    /// 开始测试
    /// </summary>
    private void StartTest()
    {
        if (_testStarted) return;

        _testStarted = true;
        _testEnded = false;
        _testStartTime = DateTime.Now; // 记录测试开始时间

        // 启动统计信息更新定时器
        StartStatsUpdateTimer();

        // 如果倒计时已启用，启动倒计时
        if (_isCountdownEnabled && _remainingSeconds > 0)
        {
            StartCountdown();
        }

        // 更新播放/暂停按钮状态
        UpdatePlayPauseButton();
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
    /// 启动倒计时
    /// </summary>
    private void StartCountdown()
    {
        StopCountdown(); // 先停止之前的倒计时

        // 更新标签文本为"倒计时"
        if (ElapsedTimeLabel != null)
        {
            ElapsedTimeLabel.Text = "倒计时";
        }

        _countdownTimer = new Timer(_ =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_remainingSeconds > 0)
                {
                    _remainingSeconds--;
                }
                
                // 更新显示（包括当 _remainingSeconds 为 0 时）
                UpdateCountdownDisplay();
                
                // 如果倒计时到0，延迟一点时间确保显示更新，然后结束测试
                if (_remainingSeconds == 0)
                {
                    // 延迟一点时间确保显示更新，然后结束测试
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        EndTest("倒计时结束");
                    }, Avalonia.Threading.DispatcherPriority.Background);
                }
            });
        }, null, 0, 1000); // 每秒更新一次
    }

    /// <summary>
    /// 停止倒计时
    /// </summary>
    private void StopCountdown()
    {
        _countdownTimer?.Dispose();
        _countdownTimer = null;
    }

    /// <summary>
    /// 更新倒计时显示
    /// </summary>
    private void UpdateCountdownDisplay()
    {
        if (!_isCountdownEnabled) return;

        // 确保使用 _remainingSeconds 的值，而不是重新计算
        int hours = _remainingSeconds / 3600;
        int minutes = (_remainingSeconds % 3600) / 60;
        int seconds = _remainingSeconds % 60;
        UpdateElapsedTime(hours, minutes, seconds);
    }

    /// <summary>
    /// 结束测试
    /// </summary>
    private void EndTest(string reason = "")
    {
        if (_testEnded) return;

        _testEnded = true;
        _testStarted = false;
        _testEndTime = DateTime.Now; // 记录测试结束时间
        
        // 在结束测试前，最后更新一次统计信息，确保完成率等数据正确
        UpdateStatistics();
        
        StopStatsUpdateTimer();
        StopCountdown();

        // 恢复标签文本为"用时"（如果不是倒计时模式）
        if (ElapsedTimeLabel != null && !_isCountdownEnabled)
        {
            ElapsedTimeLabel.Text = "用时";
        }

        // 触发测试结束事件
        TestEnded?.Invoke();

        // 更新播放/暂停按钮状态
        UpdatePlayPauseButton();
    }

    /// <summary>
    /// 更新统计信息
    /// </summary>
    private void UpdateStatistics()
    {
        // 允许在测试结束后也更新统计信息（用于确保完成率等数据正确）
        if ((!_testStarted && !_testEnded) || _characterBlocks.Count == 0) return;

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

        // 更新退格次数
        UpdateBackspaceCount(_backspaceCount);

        // 计算完成率
        double completionRate = totalChars > 0 ? (typedChars * 100.0 / totalChars) : 0;
        UpdateCompletionRateDisplay(completionRate);

        // 计算正确率
        double accuracyRate = typedChars > 0 ? (correctChars * 100.0 / typedChars) : 0;
        UpdateAccuracyRate(accuracyRate);

        // 计算用时（精确到毫秒）
        // 如果倒计时启用，显示倒计时剩余时间；否则显示实际用时
        var elapsed = DateTime.Now - _testStartTime;
        int hours, minutes, seconds;
        if (_isCountdownEnabled)
        {
            // 倒计时模式：显示剩余时间（包括0）
            hours = _remainingSeconds / 3600;
            minutes = (_remainingSeconds % 3600) / 60;
            seconds = _remainingSeconds % 60;
        }
        else
        {
            // 正常模式：显示实际用时
            int totalSeconds = (int)elapsed.TotalSeconds;
            hours = totalSeconds / 3600;
            minutes = (totalSeconds % 3600) / 60;
            seconds = totalSeconds % 60;
        }
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
        _testEnded = false; // 重置测试结束标志
        _backspaceCount = 0; // 重置退格次数
        StopStatsUpdateTimer(); // 停止统计信息更新定时器
        StopCountdown(); // 停止倒计时
        
        // 重置倒计时到初始值（如果倒计时已启用）
        if (_isCountdownEnabled)
        {
            // 使用保存的倒计时值重置（_countdownHours、_countdownMinutes、_countdownSeconds 应该已经被 SetCountdown 设置）
            // 重新计算初始秒数，确保使用最新的设置值
            var initialSeconds = _countdownHours * 3600 + _countdownMinutes * 60 + _countdownSeconds;
            // 总是使用计算出的初始秒数重置（即使为0也要重置，因为用户可能设置了0秒倒计时）
            _remainingSeconds = initialSeconds;
            // 直接更新显示，不通过 UpdateCountdownDisplay（避免条件检查）
            int hours = _remainingSeconds / 3600;
            int minutes = (_remainingSeconds % 3600) / 60;
            int seconds = _remainingSeconds % 60;
            UpdateElapsedTime(hours, minutes, seconds);
            // 保持标签文本为"倒计时"
            if (ElapsedTimeLabel != null)
            {
                ElapsedTimeLabel.Text = "倒计时";
            }
        }
        else
        {
            // 恢复标签文本为"用时"，并重置显示为 00:00:00
            if (ElapsedTimeLabel != null)
            {
                ElapsedTimeLabel.Text = "用时";
            }
            UpdateElapsedTime(0, 0, 0);
        }

        // 初始设置 WrapPanel 的宽度
        UpdateWrapPanelWidth();

        // 更新播放/暂停按钮状态
        UpdatePlayPauseButton();

        // 将换行符替换为空格（统一处理各种换行符：\r\n, \n, \r，与 generate-file-list.js 保持一致）
        text = text.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
        // 修剪文本起始和结束的空白字符
        text = text.Trim();
        // 在英文中文间添加空格（与 generate-file-list.js 保持一致）
        text = AddSpaceBetweenEnglishAndChinese(text);
        
        // 计算总字符数（在处理完文本后）
        int totalChars = text.Length;
        
        // 重置所有统计信息显示
        UpdateTypedChars(0, totalChars); // 重置已输入字符数和总字符数
        UpdateBackspaceCount(0); // 重置退格次数显示
        UpdateAccuracyRate(0); // 重置正确率显示
        UpdateSpeed(0); // 重置速度显示
        UpdateElapsedTime(0, 0, 0); // 重置用时显示
        InitializeCompletionRateBackground(); // 重置完成率背景为完全透明
        UpdateCompletionRateDisplay(0); // 重置完成率显示

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

        // 等待布局更新（可以多次执行，用于窗口尺寸变化后重新应用行距）
        // 先取消之前的事件订阅（如果存在）
        if (_layoutUpdatedHandler != null && TextContentPanel != null)
        {
            TextContentPanel.LayoutUpdated -= _layoutUpdatedHandler;
        }
        
        // 创建新的事件处理器
        _layoutUpdatedHandler = (_, _) =>
        {
            if (!_lineSpacingApplied)
            {
                ApplyLineSpacing();
            }
        };
        
        // 订阅事件
        if (TextContentPanel != null)
        {
            TextContentPanel.LayoutUpdated += _layoutUpdatedHandler;
        }
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

        // 在移动到下一个字符之前，检查刚才输入的字符是否是行尾
        // 只有当输入了最后一个字符（即输入完当前行的最后一个字符）时才滚动
        var justTypedCharIndex = _currentCharIndex; // 保存刚才输入的字符索引
        if (justTypedCharIndex >= 0 && justTypedCharIndex < _outerBorders.Count && TextScrollViewer != null && !_isScrolling)
        {
            var justTypedOuterBorder = _outerBorders[justTypedCharIndex];
            var scrollViewerBounds = TextScrollViewer.Bounds;
            var transform = justTypedOuterBorder.TransformToVisual(TextScrollViewer);

            if (transform.HasValue)
            {
                var matrix = transform.Value;
                var point = matrix.Transform(new Point(0, 0));
                var y = point.Y;
                var bounds = justTypedOuterBorder.Bounds;

                // 检测条件：
                // 1. 刚才输入的字符在视口底部（视口底部90%位置以下）
                // 2. 刚才输入的字符是它所在行的最后一个字符
                var isAtBottom = y + bounds.Height >= scrollViewerBounds.Height * 0.9;
                var isLastInLine = IsLastCharInLine(justTypedCharIndex);

                if (isAtBottom && isLastInLine)
                {
                    SmoothScrollLines(4); // 向下滚动4行
                }
            }
        }

        // 移动到下一个字符
        if (_currentCharIndex < _characterBlocks.Count - 1)
        {
            SetCurrentCharIndex(_currentCharIndex + 1);
        }
        else
        {
            // 所有文本输入完毕，结束测试
            EndTest("所有文本输入完毕");
        }
    }

    /// <summary>
    /// 处理退格键
    /// </summary>
    public void HandleBackspace()
    {
        if (_currentCharIndex <= 0) return; // 已经在第一个字符，无法再退

        // 增加退格次数统计
        _backspaceCount++;

        // 退格时，应该从上一个已输入的字符开始重置（_currentCharIndex - 1）
        // 因为 _currentCharIndex 指向的是下一个要输入的字符，而退格是要撤销最后一个已输入的字符
        var startIndex = _currentCharIndex - 1;
        
        // 将上一个字符和之后已输入的字符都重置为灰色
        for (int i = startIndex; i < _characterBlocks.Count; i++)
        {
            if (_characterStates[i] != CharState.NotTyped)
            {
                _characterBlocks[i].Foreground = new SolidColorBrush(Colors.Gray);
                _characterBorders[i].Background = null; // 清除背景色（设置在内层 Border 上）
                _characterStates[i] = CharState.NotTyped;
            }
        }

        // 退回到上一个字符
        SetCurrentCharIndex(startIndex);

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

            // 使用高精度计时器，基于实际经过的时间而不是固定步骤数
            var stopwatch = Stopwatch.StartNew();
            var startTime = stopwatch.ElapsedMilliseconds;

            // 使用较小的更新间隔以确保流畅（约16ms，对应60Hz，但实际基于时间计算）
            var updateInterval = TimeSpan.FromMilliseconds(16); // 约60fps的更新频率

            while (stopwatch.ElapsedMilliseconds < duration.TotalMilliseconds)
            {
                // 计算实际经过的时间进度（0.0 到 1.0）
                var elapsed = stopwatch.ElapsedMilliseconds;
                var progress = Math.Min(1.0, elapsed / duration.TotalMilliseconds);

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

                // 等待更新间隔，但确保不超过剩余时间
                var remainingTime = duration.TotalMilliseconds - elapsed;
                var delayTime = Math.Min(updateInterval.TotalMilliseconds, remainingTime);
                if (delayTime > 0)
                {
                    await Task.Delay((int)delayTime);
                }
            }

            // 确保最终位置准确
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (TextScrollViewer != null)
                {
                    TextScrollViewer.Offset = new Vector(0, Math.Max(0, targetOffset));
                }
            }, Avalonia.Threading.DispatcherPriority.Render);
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

    /// <summary>
    /// 设置当前文章信息
    /// </summary>
    public void SetArticleInfo(string folderName, string articleName)
    {
        _currentArticleFolder = folderName;
        _currentArticleName = articleName;
    }

    /// <summary>
    /// 检查测试是否已结束
    /// </summary>
    public bool IsTestEnded()
    {
        return _testEnded;
    }

    /// <summary>
    /// 检查是否有文章加载
    /// </summary>
    public bool HasArticleLoaded()
    {
        return _characterBlocks.Count > 0 && !string.IsNullOrEmpty(_currentArticleFolder);
    }

    /// <summary>
    /// 播放/暂停按钮点击事件
    /// </summary>
    private void PlayPauseButton_Click(object? sender, RoutedEventArgs e)
    {
        // 阻止按钮获得焦点
        e.Handled = true;
        
        // 如果没有文章加载，什么也不做
        if (!HasArticleLoaded()) return;

        if (_testStarted)
        {
            // 测试进行中，点击暂停（结束测试）
            EndTest("手动暂停");
        }
        else
        {
            // 测试未开始，点击播放（重新开始测试）
            // 重新开始测试意味着重新载入文章，重置各项测试数据
            ReloadCurrentArticle();
        }
    }

    /// <summary>
    /// 重新加载当前文章（用于重新开始测试）
    /// </summary>
    public void ReloadCurrentArticle()
    {
        // 如果没有文章加载，什么也不做
        if (!HasArticleLoaded()) return;

        // 触发重新加载事件，让 MainWindow 重新加载当前文章
        ArticleReloadRequested?.Invoke(_currentArticleFolder, _currentArticleName);
    }

    /// <summary>
    /// 更新播放/暂停按钮状态
    /// </summary>
    private void UpdatePlayPauseButton()
    {
        if (PlayPauseButtonIcon == null) return;

        if (_testStarted)
        {
            // 测试进行中，显示暂停图标
            PlayPauseButtonIcon.Text = "⏸";
        }
        else
        {
            // 测试未开始，显示播放图标
            PlayPauseButtonIcon.Text = "▶";
        }
    }

    /// <summary>
    /// 获取测试统计数据
    /// </summary>
    public TestStatistics GetTestStatistics()
    {
        // 计算已输入字符数和正确字符数
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
        var elapsed = _testEndTime - _testStartTime;

        return new TestStatistics
        {
            ArticleFolder = _currentArticleFolder,
            ArticleName = _currentArticleName,
            StartTime = _testStartTime,
            EndTime = _testEndTime,
            ElapsedTime = elapsed,
            TypedChars = typedChars,
            TotalChars = totalChars,
            CorrectChars = correctChars,
            BackspaceCount = _backspaceCount
        };
    }
}

/// <summary>
/// 测试统计数据
/// </summary>
public class TestStatistics
{
    public string ArticleFolder { get; set; } = "";
    public string ArticleName { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan ElapsedTime { get; set; }
    public int TypedChars { get; set; }
    public int TotalChars { get; set; }
    public int CorrectChars { get; set; }
    public int BackspaceCount { get; set; }
}