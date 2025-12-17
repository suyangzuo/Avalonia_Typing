using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia_Typing.Views.Dialogs;

namespace Avalonia_Typing.Views;

public partial class MainWindow : Window
{
    // 保存选中的二级菜单项和三级菜单项的标识
    private string? _rememberedSubMenuKey;
    private string? _rememberedThirdMenuFileName;

    public MainWindow()
    {
        InitializeComponent();
        LoadThirdLevelMenus();
        AttachMenuClickHandlers();
        ApplyRememberedSelection();
        AttachDialogMenuHandlers();
    }

    private void LoadThirdLevelMenus()
    {
        try
        {
            // 读取 JSON 文件
            var uri = new Uri("avares://Avalonia_Typing/Assets/Texts/file-list.json");
            using var stream = AssetLoader.Open(uri);
            using var reader = new StreamReader(stream);
            var jsonContent = reader.ReadToEnd();
            
            var jsonDoc = JsonDocument.Parse(jsonContent);
            var root = jsonDoc.RootElement;

            // 二级菜单项与 JSON key 的映射关系
            var menuKeyMap = new Dictionary<string, string>
            {
                { "C", "C" },
                { "Computer", "Computer" },
                { "Electron", "Electron" },
                { "Java", "Java" },
                { "JavaScript", "JavaScript" },
                { "Life", "Life" },
                { "Linux", "Linux" },
                { "MySQL", "MySQL" },
                { "Node.js", "Node.js" },
                { "Python", "Python" },
                { "Vue", "Vue" }
            };

            // 找到"文本选择"菜单项
            if (MainMenu == null) return;

            MenuItem? textSelectionMenu = null;
            foreach (var item in MainMenu.Items.OfType<MenuItem>())
            {
                if (item.Header?.ToString() == "文本选择")
                {
                    textSelectionMenu = item;
                    break;
                }
            }

            if (textSelectionMenu == null) return;

            // 为每个二级菜单项添加三级菜单
            foreach (var subMenuItem in textSelectionMenu.Items.OfType<MenuItem>())
            {
                // 从二级菜单项的 TextBlock 中获取文本
                string? menuKey = null;
                if (subMenuItem.Header is StackPanel panel)
                {
                    foreach (var child in panel.Children)
                    {
                        if (child is TextBlock textBlock)
                        {
                            menuKey = textBlock.Text;
                            break;
                        }
                    }
                }

                if (menuKey == null || !menuKeyMap.ContainsKey(menuKey)) continue;

                var jsonKey = menuKeyMap[menuKey];
                if (!root.TryGetProperty(jsonKey, out var fileArray)) continue;

                // 解析每个文件并添加三级菜单项
                foreach (var fileElement in fileArray.EnumerateArray())
                {
                    if (!fileElement.TryGetProperty("文件名", out var fileNameElement) ||
                        !fileElement.TryGetProperty("字符数", out var charCountElement))
                        continue;

                    var fileName = fileNameElement.GetString();
                    var charCount = charCountElement.GetInt32();

                    if (string.IsNullOrEmpty(fileName)) continue;

                    // 解析文件名：提取序号和文件名（去掉序号和_，去掉.txt）
                    var match = Regex.Match(fileName, @"^(\d+)_(.+)\.txt$");
                    if (match.Success)
                    {
                        var number = match.Groups[1].Value; // 序号
                        var name = match.Groups[2].Value; // 文件名（已去掉序号和_，已去掉.txt）

                        // 创建三级菜单项：序号 + 文件名 + 字符数
                        var thirdLevelMenuItem = new MenuItem();
                        // 为三级菜单项添加类，方便样式控制
                        thirdLevelMenuItem.Classes.Add("third-menu");

                        // 创建包含三个部分的 StackPanel，每个部分都有独立的类
                        var headerPanel = new StackPanel
                        {
                            Orientation = Avalonia.Layout.Orientation.Horizontal
                        };

                        // 序号部分
                        var numberBlock = new TextBlock
                        {
                            Text = number
                        };
                        numberBlock.Classes.Add("menu-number");
                        headerPanel.Children.Add(numberBlock);

                        // 文件名部分
                        var nameBlock = new TextBlock
                        {
                            Text = name
                        };
                        nameBlock.Classes.Add("menu-name");
                        headerPanel.Children.Add(nameBlock);

                        // 字符数部分
                        var charCountBlock = new TextBlock
                        {
                            Text = charCount.ToString()
                        };
                        charCountBlock.Classes.Add("menu-charcount");
                        headerPanel.Children.Add(charCountBlock);

                        thirdLevelMenuItem.Header = headerPanel;
                        // 保存文件名作为标识，用于记住选中状态
                        thirdLevelMenuItem.Tag = fileName;

                        subMenuItem.Items.Add(thirdLevelMenuItem);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // 处理异常，可以记录日志
            System.Diagnostics.Debug.WriteLine($"加载三级菜单失败: {ex.Message}");
        }
    }

    private void AttachMenuClickHandlers()
    {
        try
        {
            if (MainMenu == null) return;

            MenuItem? textSelectionMenu = MainMenu.Items.OfType<MenuItem>().FirstOrDefault(item => item.Header?.ToString() == "文本选择");
            if (textSelectionMenu == null) return;

            foreach (var subMenuItem in textSelectionMenu.Items.OfType<MenuItem>())
            {
                // 为二级菜单项添加标识
                string? menuKey = null;
                if (subMenuItem.Header is StackPanel panel)
                {
                    menuKey = panel.Children.OfType<TextBlock>().FirstOrDefault()?.Text;
                }
                if (menuKey != null)
                {
                    subMenuItem.Tag = menuKey;
                }

                // 为三级菜单项添加点击事件
                foreach (var thirdMenuItem in subMenuItem.Items.OfType<MenuItem>())
                {
                    thirdMenuItem.Click += (sender, _) =>
                    {
                        if (sender is MenuItem item && item.Tag is string fileName)
                        {
                            // 记住选中的菜单项
                            _rememberedSubMenuKey = menuKey;
                            _rememberedThirdMenuFileName = fileName;
                            
                            // 应用记住的样式
                            ApplyRememberedSelection();
                        }
                    };
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"附加菜单点击事件失败: {ex.Message}");
        }
    }

    private void ApplyRememberedSelection()
    {
        try
        {
            if (MainMenu == null) return;

            MenuItem? textSelectionMenu = MainMenu.Items.OfType<MenuItem>().FirstOrDefault(item => item.Header?.ToString() == "文本选择");
            if (textSelectionMenu == null) return;

            // 清除所有记住状态的类
            foreach (var subMenuItem in textSelectionMenu.Items.OfType<MenuItem>())
            {
                subMenuItem.Classes.Remove("remembered");
                foreach (var thirdMenuItem in subMenuItem.Items.OfType<MenuItem>())
                {
                    thirdMenuItem.Classes.Remove("remembered");
                }
            }

            // 如果有记住的选中项，应用样式
            if (!string.IsNullOrEmpty(_rememberedSubMenuKey) && !string.IsNullOrEmpty(_rememberedThirdMenuFileName))
            {
                foreach (var subMenuItem in textSelectionMenu.Items.OfType<MenuItem>())
                {
                    if (subMenuItem.Tag is string menuKey && menuKey == _rememberedSubMenuKey)
                    {
                        subMenuItem.Classes.Add("remembered");
                        
                        // 找到对应的三级菜单项
                        foreach (var thirdMenuItem in subMenuItem.Items.OfType<MenuItem>())
                        {
                            if (thirdMenuItem.Tag is string fileName && fileName == _rememberedThirdMenuFileName)
                            {
                                thirdMenuItem.Classes.Add("remembered");
                                break;
                            }
                        }
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"应用记住的选中状态失败: {ex.Message}");
        }
    }

    private void AttachDialogMenuHandlers()
    {
        if (MainMenu == null) return;

        // 目标菜单及其图标和按钮配置
        var dialogItems = new Dictionary<string, (string Emoji, bool HasCancel)>
        {
            { "姓名", ("👤", true) },
            { "计时", ("⏱️", true) },
            { "统计", ("📊", false) },
            { "使用说明", ("❓", false) },
            { "关于", ("ℹ️", false) },
        };

        // 遍历所有二级菜单项，匹配文字部分
        foreach (var menuItem in EnumerateMenuItems(MainMenu.Items))
        {
            var text = GetSecondLevelText(menuItem);
            if (text != null && dialogItems.TryGetValue(text, out var info))
            {
                menuItem.Click += async (_, _) => await ShowDialogForMenu(text, info.Emoji, info.HasCancel);
            }
        }
    }

    private static string? GetSecondLevelText(MenuItem item)
    {
        if (item.Header is StackPanel sp)
        {
            // 期望：第一个 TextBlock 是 Emoji，第二个是文本
            var textBlocks = sp.Children.OfType<TextBlock>().ToList();
            if (textBlocks.Count >= 2)
            {
                return textBlocks[1].Text;
            }
        }
        else if (item.Header is string s)
        {
            return s;
        }
        return null;
    }

    private async Task ShowDialogForMenu(string titleText, string emoji, bool hasCancel)
    {
        var dialog = new Window
        {
            Title = $"{emoji} {titleText}",
        };
        dialog.Classes.Add("dialog-window");

        Control content = titleText switch
        {
            "姓名" => new NameDialogView(),
            "计时" => new TimerDialogView(),
            "统计" => new StatsDialogView(),
            "使用说明" => new HelpDialogView(),
            "关于" => new AboutDialogView(),
            _ => new TextBlock { Text = $"这里是“{titleText}”对话框内容。", TextWrapping = TextWrapping.Wrap }
        };

        var buttonPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8
        };

        var okButton = new Button
        {
            Content = "确定"
        };
        okButton.Classes.Add("dialog-button");
        okButton.Click += (_, _) => dialog.Close(true);
        buttonPanel.Children.Add(okButton);

        if (hasCancel)
        {
            var cancelButton = new Button
            {
                Content = "取消"
            };
            cancelButton.Classes.Add("dialog-button");
            cancelButton.Click += (_, _) => dialog.Close(false);
            buttonPanel.Children.Add(cancelButton);
        }

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                content,
                buttonPanel
            }
        };

        await dialog.ShowDialog(this);
    }

    private static IEnumerable<MenuItem> EnumerateMenuItems(IEnumerable items)
    {
        foreach (var obj in items)
        {
            if (obj is MenuItem mi)
            {
                yield return mi;
                foreach (var child in EnumerateMenuItems(mi.Items))
                    yield return child;
            }
        }
    }
}