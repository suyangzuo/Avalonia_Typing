using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
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
    private const string DefaultName = "江湖人士";
    private readonly string _stateJsonPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Avalonia_Typing",
        "window.state.json");
    private string _currentName = DefaultName;
    private bool _isCountdownEnabled = false; // 倒计时功能是否启用
    private int _timerHours = 0; // 计时器小时数
    private int _timerMinutes = 0; // 计时器分钟数
    private int _timerSeconds = 0; // 计时器秒数

    public MainWindow()
    {
        InitializeComponent();
        LoadNameFromJson();
        LoadTimerSettingsFromJson();
        UpdateMainViewName();
        LoadThirdLevelMenus();
        AttachMenuClickHandlers();
        ApplyRememberedSelection();
        AttachDialogMenuHandlers();
        
        // 添加文本输入事件处理
        this.TextInput += MainWindow_TextInput;
        
        // 添加键盘事件处理（用于退格键）
        this.KeyDown += MainWindow_KeyDown;
    }
    
    private void MainWindow_TextInput(object? sender, Avalonia.Input.TextInputEventArgs e)
    {
        // 处理输入的文本（英文形式，只处理单个字符）
        if (!string.IsNullOrEmpty(e.Text) && e.Text.Length == 1)
        {
            MainContent?.HandleInput(e.Text[0]);
        }
    }

    private void MainWindow_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        // 处理退格键
        if (e.Key == Avalonia.Input.Key.Back)
        {
            MainContent?.HandleBackspace();
            e.Handled = true; // 标记为已处理，避免其他默认行为
        }
    }

    private void UpdateMainViewName()
    {
        if (MainContent != null)
        {
            MainContent.UpdateName(_currentName);
        }
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
                var currentMenuKey = menuKey; // 保存当前 menuKey 的副本
                foreach (var thirdMenuItem in subMenuItem.Items.OfType<MenuItem>())
                {
                    thirdMenuItem.Click += (sender, _) =>
                    {
                        if (sender is MenuItem item && item.Tag is string fileName && !string.IsNullOrEmpty(currentMenuKey))
                        {
                            // 记住选中的菜单项
                            _rememberedSubMenuKey = currentMenuKey;
                            _rememberedThirdMenuFileName = fileName;
                            
                            // 应用记住的样式
                            ApplyRememberedSelection();
                            
                            // 加载对应的文章文件
                            LoadArticleFile(currentMenuKey, fileName);
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

    /// <summary>
    /// 加载文章文件
    /// </summary>
    private void LoadArticleFile(string menuKey, string fileName)
    {
        try
        {
            // 构建文件路径：avares://Avalonia_Typing/Assets/Texts/{menuKey}/{fileName}
            var filePath = $"avares://Avalonia_Typing/Assets/Texts/{menuKey}/{fileName}";
            var uri = new Uri(filePath);
            
            using var stream = AssetLoader.Open(uri);
            using var reader = new StreamReader(stream);
            var content = reader.ReadToEnd();
            
            // 加载文章到 MainView
            MainContent?.LoadText(content);
            
            // 设置焦点以便接收键盘输入
            MainContent?.Focus();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载文章文件失败: {ex.Message}");
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
        TextBox? nameInput = null;

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

        if (titleText == "姓名" && content is NameDialogView nameView)
        {
            nameInput = nameView.FindControl<TextBox>("NameInput");
            if (nameInput != null)
            {
                nameInput.Text = _currentName;
            }
        }
        else if (titleText == "计时" && content is TimerDialogView timerView)
        {
            // 加载当前的时、分、秒和倒计时状态
            var hoursInput = timerView.FindControl<TextBox>("HoursInput");
            var minutesInput = timerView.FindControl<TextBox>("MinutesInput");
            var secondsInput = timerView.FindControl<TextBox>("SecondsInput");
            var countdownCheckBox = timerView.FindControl<CheckBox>("CountdownCheckBox");

            if (hoursInput != null)
            {
                hoursInput.Text = _timerHours.ToString();
            }
            if (minutesInput != null)
            {
                minutesInput.Text = _timerMinutes.ToString();
            }
            if (secondsInput != null)
            {
                secondsInput.Text = _timerSeconds.ToString();
            }
            if (countdownCheckBox != null)
            {
                countdownCheckBox.IsChecked = _isCountdownEnabled;
            }
        }

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
        okButton.Click += (_, _) =>
        {
            if (titleText == "姓名" && nameInput != null)
            {
                var newName = (nameInput.Text ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(newName))
                {
                    _currentName = newName;
                    SaveNameToJson(newName);
                    UpdateMainViewName();
                }
            }
            else if (titleText == "计时" && content is TimerDialogView timerView)
            {
                // 保存时、分、秒和倒计时状态
                _timerHours = timerView.Hours;
                _timerMinutes = timerView.Minutes;
                _timerSeconds = timerView.Seconds;
                _isCountdownEnabled = timerView.IsCountdown;
                SaveTimerSettingsToJson(_timerHours, _timerMinutes, _timerSeconds, _isCountdownEnabled);
            }
            dialog.Close(true);
        };
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

    private void LoadNameFromJson()
    {
        _currentName = DefaultName;
        try
        {
            if (!File.Exists(_stateJsonPath))
            {
                return;
            }

            var jsonContent = File.ReadAllText(_stateJsonPath);
            using var jsonDoc = JsonDocument.Parse(jsonContent);
            var root = jsonDoc.RootElement;

            // 优先使用 Name 字段，如果没有则使用 TesterName 字段（兼容旧数据）
            if (root.TryGetProperty("Name", out var nameElement))
            {
                var nameValue = nameElement.GetString();
                if (!string.IsNullOrWhiteSpace(nameValue))
                {
                    _currentName = nameValue.Trim();
                }
            }
            else if (root.TryGetProperty("TesterName", out var testerNameElement))
            {
                var nameValue = testerNameElement.GetString();
                if (!string.IsNullOrWhiteSpace(nameValue))
                {
                    _currentName = nameValue.Trim();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"读取姓名失败，使用默认值: {ex.Message}");
            _currentName = DefaultName;
        }
    }

    private void SaveNameToJson(string name)
    {
        try
        {
            // 确保目录存在
            var directory = Path.GetDirectoryName(_stateJsonPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 读取现有 JSON 文件（如果存在）
            Dictionary<string, JsonElement> stateData = new();
            if (File.Exists(_stateJsonPath))
            {
                try
                {
                    var jsonContent = File.ReadAllText(_stateJsonPath);
                    using var jsonDoc = JsonDocument.Parse(jsonContent);
                    var root = jsonDoc.RootElement;
                    
                    // 复制所有现有字段，但排除 TesterName 字段
                    foreach (var property in root.EnumerateObject())
                    {
                        // 跳过 TesterName 字段（与 Name 字段含义相同）
                        if (property.Name != "TesterName")
                        {
                            stateData[property.Name] = property.Value.Clone();
                        }
                    }
                }
                catch
                {
                    // 如果读取失败，使用空字典
                }
            }

            // 构建新的 JSON 对象
            var jsonObject = new Dictionary<string, object?>();
            foreach (var kvp in stateData)
            {
                var element = kvp.Value;
                if (element.ValueKind == JsonValueKind.String)
                {
                    jsonObject[kvp.Key] = element.GetString();
                }
                else if (element.ValueKind == JsonValueKind.Number)
                {
                    if (element.TryGetInt32(out var intValue))
                    {
                        jsonObject[kvp.Key] = intValue;
                    }
                    else
                    {
                        jsonObject[kvp.Key] = element.GetDouble();
                    }
                }
                else if (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False)
                {
                    jsonObject[kvp.Key] = element.GetBoolean();
                }
                else if (element.ValueKind == JsonValueKind.Null)
                {
                    jsonObject[kvp.Key] = null;
                }
            }

            // 更新或添加 Name 字段（直接设置字符串值，不使用 JsonElement）
            jsonObject["Name"] = name;

            // 保存回文件，使用不转义非 ASCII 字符的编码器
            var options = new JsonSerializerOptions 
            { 
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var jsonString = JsonSerializer.Serialize(jsonObject, options);
            File.WriteAllText(_stateJsonPath, jsonString, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"保存姓名失败: {ex.Message}");
        }
    }

    private void LoadTimerSettingsFromJson()
    {
        _timerHours = 0;
        _timerMinutes = 0;
        _timerSeconds = 0;
        _isCountdownEnabled = false;
        try
        {
            if (!File.Exists(_stateJsonPath))
            {
                return;
            }

            var jsonContent = File.ReadAllText(_stateJsonPath);
            using var jsonDoc = JsonDocument.Parse(jsonContent);
            var root = jsonDoc.RootElement;

            // 加载时、分、秒
            if (root.TryGetProperty("TimerHours", out var hoursElement) && hoursElement.ValueKind == JsonValueKind.Number)
            {
                if (hoursElement.TryGetInt32(out var hours))
                {
                    _timerHours = hours;
                }
            }
            if (root.TryGetProperty("TimerMinutes", out var minutesElement) && minutesElement.ValueKind == JsonValueKind.Number)
            {
                if (minutesElement.TryGetInt32(out var minutes))
                {
                    _timerMinutes = minutes;
                }
            }
            if (root.TryGetProperty("TimerSeconds", out var secondsElement) && secondsElement.ValueKind == JsonValueKind.Number)
            {
                if (secondsElement.TryGetInt32(out var seconds))
                {
                    _timerSeconds = seconds;
                }
            }

            // 加载倒计时状态
            if (root.TryGetProperty("IsCountdownEnabled", out var countdownElement))
            {
                if (countdownElement.ValueKind == JsonValueKind.True || countdownElement.ValueKind == JsonValueKind.False)
                {
                    _isCountdownEnabled = countdownElement.GetBoolean();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"读取计时设置失败，使用默认值: {ex.Message}");
            _timerHours = 0;
            _timerMinutes = 0;
            _timerSeconds = 0;
            _isCountdownEnabled = false;
        }
    }

    private void SaveTimerSettingsToJson(int hours, int minutes, int seconds, bool isCountdownEnabled)
    {
        try
        {
            // 确保目录存在
            var directory = Path.GetDirectoryName(_stateJsonPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 读取现有 JSON 文件（如果存在）
            Dictionary<string, JsonElement> stateData = new();
            if (File.Exists(_stateJsonPath))
            {
                try
                {
                    var jsonContent = File.ReadAllText(_stateJsonPath);
                    using var jsonDoc = JsonDocument.Parse(jsonContent);
                    var root = jsonDoc.RootElement;
                    
                    // 复制所有现有字段
                    foreach (var property in root.EnumerateObject())
                    {
                        stateData[property.Name] = property.Value.Clone();
                    }
                }
                catch
                {
                    // 如果读取失败，使用空字典
                }
            }

            // 构建新的 JSON 对象
            var jsonObject = new Dictionary<string, object?>();
            foreach (var kvp in stateData)
            {
                var element = kvp.Value;
                if (element.ValueKind == JsonValueKind.String)
                {
                    jsonObject[kvp.Key] = element.GetString();
                }
                else if (element.ValueKind == JsonValueKind.Number)
                {
                    if (element.TryGetInt32(out var intValue))
                    {
                        jsonObject[kvp.Key] = intValue;
                    }
                    else
                    {
                        jsonObject[kvp.Key] = element.GetDouble();
                    }
                }
                else if (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False)
                {
                    jsonObject[kvp.Key] = element.GetBoolean();
                }
                else if (element.ValueKind == JsonValueKind.Null)
                {
                    jsonObject[kvp.Key] = null;
                }
            }

            // 更新或添加时、分、秒和倒计时状态字段
            jsonObject["TimerHours"] = hours;
            jsonObject["TimerMinutes"] = minutes;
            jsonObject["TimerSeconds"] = seconds;
            jsonObject["IsCountdownEnabled"] = isCountdownEnabled;

            // 保存回文件，使用不转义非 ASCII 字符的编码器
            var options = new JsonSerializerOptions 
            { 
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var jsonString = JsonSerializer.Serialize(jsonObject, options);
            File.WriteAllText(_stateJsonPath, jsonString, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"保存计时设置失败: {ex.Message}");
        }
    }
}