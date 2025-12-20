using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using Avalonia_Typing.Views;

namespace Avalonia_Typing.Views.Dialogs;

public partial class StatsDialogView : UserControl
{
    private const string PropertyNameColor = "#AAA"; // 属性名称颜色（灰色）
    private const string ColonColor = "#888"; // 冒号颜色（浅灰色）
    private const string ValueColor = "#4A90E2"; // 属性值颜色（蓝色）
    private const string DecimalPointColor = "#888888"; // 小数点颜色（灰色）
    private const string PercentColor = "#888888"; // 百分号颜色（灰色）
    private const string TimeColonColor = "#888888"; // 时间冒号颜色（灰色）
    private const string SlashColor = "#888888"; // 斜杠颜色（灰色）
    private const string HyphenColor = "#888888"; // 连字符颜色（灰色）
    private const string DateUnitColor = "#789"; // 日期单位颜色（年、月、日）（紫色）
    private const string TextUnitColor = "#789"; // 文本单位颜色（字符、分钟）（绿色）

    public StatsDialogView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 设置统计数据
    /// </summary>
    public void SetStatistics(string name, TestStatistics stats, bool isTestEnded)
    {
        if (StatsPanel == null) return;

        // 清空现有内容（保留标题）
        StatsPanel.Children.Clear();
        StatsPanel.Children.Add(new TextBlock { Text = "统计", Classes = { "dialog-title" } });

        // 属性名称列表（用于计算最大宽度）
        var propertyNames = new[]
        {
            "姓名",
            "测试文章",
            "测试起始时间",
            "测试结束时间",
            "测试用时",
            "完成率",
            "完成详情",
            "正确率",
            "退格次数",
            "速度"
        };

        // 计算最大属性名称宽度
        var maxPropertyNameWidth = CalculateMaxPropertyNameWidth(propertyNames);

        // 添加统计行
        // 姓名始终显示
        AddStatRow("姓名", name, maxPropertyNameWidth);
        
        // 如果测试未结束，其他属性显示"无"
        if (!isTestEnded)
        {
            AddStatRow("测试文章", "无", maxPropertyNameWidth);
            AddStatRow("测试起始时间", "无", maxPropertyNameWidth);
            AddStatRow("测试结束时间", "无", maxPropertyNameWidth);
            AddStatRow("测试用时", "无", maxPropertyNameWidth);
            AddStatRow("完成率", "无", maxPropertyNameWidth);
            AddStatRow("完成详情", "无", maxPropertyNameWidth);
            AddStatRow("正确率", "无", maxPropertyNameWidth);
            AddStatRow("退格次数", "无", maxPropertyNameWidth);
            AddStatRow("速度", "无", maxPropertyNameWidth);
        }
        else
        {
            // 测试已结束，显示实际数据
            AddStatRow("测试文章", $"{stats.ArticleFolder} - {stats.ArticleName}", maxPropertyNameWidth);
            AddStatRow("测试起始时间", FormatDateTime(stats.StartTime), maxPropertyNameWidth, hasTimeColon: true);
            AddStatRow("测试结束时间", FormatDateTime(stats.EndTime), maxPropertyNameWidth, hasTimeColon: true);
            AddStatRow("测试用时", FormatTimeSpan(stats.ElapsedTime), maxPropertyNameWidth, hasTimeColon: true);

            double completionRate = stats.TotalChars > 0 ? (stats.TypedChars * 100.0 / stats.TotalChars) : 0;
            AddStatRow("完成率", $"{completionRate:F2}%", maxPropertyNameWidth, hasDecimal: true, hasPercent: true);

            AddStatRow("完成详情", $"{stats.TypedChars}/{stats.TotalChars}", maxPropertyNameWidth, hasSlash: true);

            double accuracyRate = stats.TypedChars > 0 ? (stats.CorrectChars * 100.0 / stats.TypedChars) : 0;
            AddStatRow("正确率", $"{accuracyRate:F2}%", maxPropertyNameWidth, hasDecimal: true, hasPercent: true);

            AddStatRow("退格次数", stats.BackspaceCount.ToString(), maxPropertyNameWidth);

            int speed = stats.ElapsedTime.TotalMinutes > 0 ? (int)(stats.TypedChars / stats.ElapsedTime.TotalMinutes) : 0;
            AddStatRow("速度", $"{speed} 字符/分钟", maxPropertyNameWidth, hasSlash: true);
        }
    }

    /// <summary>
    /// 计算最大属性名称宽度
    /// </summary>
    private double CalculateMaxPropertyNameWidth(string[] propertyNames)
    {
        // 使用测量方式计算实际文本宽度
        var measureTextBlock = new TextBlock
        {
            FontFamily = new FontFamily("HarmonyOS Sans SC, Noto Sans CJK SC, 微软雅黑"),
            FontSize = 16,
            Text = "测试起始时间"
        };
        measureTextBlock.Measure(Avalonia.Size.Infinity);
        return measureTextBlock.DesiredSize.Width;
    }

    /// <summary>
    /// 添加统计行
    /// </summary>
    private void AddStatRow(string propertyName, string value, double propertyNameWidth, bool hasDecimal = false, bool hasPercent = false, bool hasTimeColon = false, bool hasSlash = false)
    {
        if (StatsPanel == null) return;

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Avalonia.Thickness(0, 4, 0, 4),
        };

        // 属性名称（右对齐，固定宽度）
        var propertyNameBlock = new TextBlock
        {
            Text = propertyName,
            FontFamily = new FontFamily("HarmonyOS Sans SC, Noto Sans CJK SC, 微软雅黑"),
            Foreground = new SolidColorBrush(Color.Parse(PropertyNameColor)),
            Width = propertyNameWidth,
            TextAlignment = TextAlignment.Right,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Avalonia.Thickness(0, 0, 2, 0)
        };
        panel.Children.Add(propertyNameBlock);

        // 冒号
        var colonBlock = new TextBlock
        {
            Text = "：",
            FontFamily = new FontFamily("HarmonyOS Sans SC, Noto Sans CJK SC, 微软雅黑"),
            Foreground = new SolidColorBrush(Color.Parse(ColonColor)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Avalonia.Thickness(0, 0, 2, 0)
        };
        panel.Children.Add(colonBlock);

        // 属性值（使用多个 TextBlock 来设置不同颜色和边距）
        var valueContainer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        FormatValueWithMargin(valueContainer, value, hasDecimal, hasPercent, hasTimeColon, hasSlash);
        panel.Children.Add(valueContainer);

        StatsPanel.Children.Add(panel);
    }

    /// <summary>
    /// 格式化属性值，使用 TextBlock 和 Margin 来设置不同颜色和边距
    /// </summary>
    private void FormatValueWithMargin(StackPanel container, string value, bool hasDecimal, bool hasPercent, bool hasTimeColon, bool hasSlash)
    {
        var fontFamily = new FontFamily("Google Sans Code, Ubuntu Mono, Consolas, HarmonyOS Sans CJK SC, Noto Sans CJK SC, 微软雅黑");
        
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            string color = ValueColor;
            bool isSlash = hasSlash && (c == '/' || c == '\\');
            bool isPercent = hasPercent && c == '%';
            bool isHyphen = c == '-';
            bool isDateUnit = c == '年' || c == '月' || c == '日';
            bool isTextUnit = false;

            // 检查是否是文本单位（"字符"或"分钟"）
            // 如果当前字符是"字"，检查下一个字符是否是"符"
            if (c == '字' && i < value.Length - 1 && value[i + 1] == '符')
            {
                isTextUnit = true;
            }
            // 如果当前字符是"符"，检查上一个字符是否是"字"
            else if (c == '符' && i > 0 && value[i - 1] == '字')
            {
                isTextUnit = true;
            }
            // 如果当前字符是"分"，检查下一个字符是否是"钟"（但要排除"分钟"中的"分"）
            else if (c == '分' && i < value.Length - 1 && value[i + 1] == '钟')
            {
                isTextUnit = true;
            }
            // 如果当前字符是"钟"，检查上一个字符是否是"分"
            else if (c == '钟' && i > 0 && value[i - 1] == '分')
            {
                isTextUnit = true;
            }

            // 检查是否需要特殊颜色
            if (hasDecimal && c == '.')
            {
                color = DecimalPointColor;
            }
            else if (isPercent)
            {
                color = PercentColor;
            }
            else if (hasTimeColon && c == ':')
            {
                color = TimeColonColor;
            }
            else if (isSlash)
            {
                color = SlashColor;
            }
            else if (isHyphen)
            {
                color = HyphenColor;
            }
            else if (isDateUnit)
            {
                color = DateUnitColor;
            }
            else if (isTextUnit)
            {
                color = TextUnitColor;
            }

            // 创建 TextBlock
            var textBlock = new TextBlock
            {
                Text = c.ToString(),
                FontFamily = fontFamily,
                Foreground = new SolidColorBrush(Color.Parse(color)),
                VerticalAlignment = VerticalAlignment.Center
            };

            // 设置边距
            if (isSlash)
            {
                // 斜杠左右各增加2的边距
                textBlock.Margin = new Avalonia.Thickness(2, 0, 2, 0);
            }
            else if (isPercent)
            {
                // 百分号左边增加2的边距
                textBlock.Margin = new Avalonia.Thickness(2, 0, 0, 0);
            }
            else if (isDateUnit)
            {
                // 日期单位（年、月、日）左右各增加2的边距
                textBlock.Margin = new Avalonia.Thickness(2, 0, 2, 0);
            }
            else
            {
                textBlock.Margin = new Avalonia.Thickness(0);
            }

            container.Children.Add(textBlock);
        }
    }

    /// <summary>
    /// 格式化日期时间
    /// </summary>
    private string FormatDateTime(DateTime dateTime)
    {
        return dateTime.ToString("yyyy年MM月dd日 HH:mm:ss");
    }

    /// <summary>
    /// 格式化时间跨度
    /// </summary>
    private string FormatTimeSpan(TimeSpan timeSpan)
    {
        int hours = (int)timeSpan.TotalHours;
        int minutes = timeSpan.Minutes;
        int seconds = timeSpan.Seconds;
        return $"{hours:D2}:{minutes:D2}:{seconds:D2}";
    }
}
