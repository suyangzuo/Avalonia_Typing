using Avalonia.Controls;
using Avalonia.Input;

namespace Avalonia_Typing.Views.Dialogs;

public partial class TimerDialogView : UserControl
{
    public TimerDialogView()
    {
        InitializeComponent();
        
        // 设置复选框不接收 Tab 焦点
        CountdownCheckBox.IsTabStop = false;
        
        // 为时、分、秒输入框添加输入验证
        HoursInput.KeyDown += OnTimeInputKeyDown;
        MinutesInput.KeyDown += OnTimeInputKeyDown;
        SecondsInput.KeyDown += OnTimeInputKeyDown;
        
        // 为时、分、秒输入框添加文本变化事件，实现自动换算
        HoursInput.TextChanged += OnHoursTextChanged;
        MinutesInput.TextChanged += OnMinutesTextChanged;
        SecondsInput.TextChanged += OnSecondsTextChanged;
    }

    private void OnTimeInputKeyDown(object? sender, KeyEventArgs e)
    {
        // 处理 Tab 键，在时、分、秒之间循环切换
        if (e.Key == Key.Tab)
        {
            e.Handled = true;
            
            if (sender is TextBox currentTextBox)
            {
                TextBox? nextTextBox = null;
                bool isShiftPressed = (e.KeyModifiers & KeyModifiers.Shift) == KeyModifiers.Shift;
                
                // 根据当前输入框和是否按 Shift 确定下一个输入框
                if (currentTextBox == HoursInput)
                {
                    // 从时切换到分（正向）或秒（反向）
                    nextTextBox = isShiftPressed ? SecondsInput : MinutesInput;
                }
                else if (currentTextBox == MinutesInput)
                {
                    // 从分切换到秒（正向）或时（反向）
                    nextTextBox = isShiftPressed ? HoursInput : SecondsInput;
                }
                else if (currentTextBox == SecondsInput)
                {
                    // 从秒切换到时（正向）或分（反向）
                    nextTextBox = isShiftPressed ? MinutesInput : HoursInput;
                }
                
                // 切换到下一个输入框
                if (nextTextBox != null)
                {
                    nextTextBox.Focus();
                    // 选中所有文本，方便直接输入
                    nextTextBox.SelectAll();
                }
            }
            return;
        }
        
        // 只允许数字、退格、删除、方向键等控制键
        if (e.Key >= Key.D0 && e.Key <= Key.D9)
        {
            // 数字键，允许
            return;
        }
        if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9)
        {
            // 小键盘数字键，允许
            return;
        }
        if (e.Key == Key.Back || e.Key == Key.Delete || 
            e.Key == Key.Left || e.Key == Key.Right || 
            e.Key == Key.Home || e.Key == Key.End)
        {
            // 控制键，允许
            return;
        }
        
        // 其他键都阻止
        e.Handled = true;
    }

    private void OnHoursTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (HoursInput.Text == null || !int.TryParse(HoursInput.Text, out var hours) || hours < 0)
        {
            return;
        }

        // 小时不需要换算（可以很大）
        // 但我们可以限制一个合理的最大值，比如 99
        if (hours > 99)
        {
            HoursInput.Text = "99";
        }
    }

    private void OnMinutesTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (MinutesInput.Text == null || string.IsNullOrWhiteSpace(MinutesInput.Text))
        {
            return;
        }

        if (!int.TryParse(MinutesInput.Text, out var minutes) || minutes < 0)
        {
            return;
        }

        // 如果分钟数 >= 60，自动换算为小时和分钟
        if (minutes >= 60)
        {
            var hours = minutes / 60;
            var remainingMinutes = minutes % 60;
            
            // 更新小时（如果超过99，则限制为99）
            if (hours > 99)
            {
                hours = 99;
                remainingMinutes = 59;
            }
            
            HoursInput.Text = hours.ToString();
            MinutesInput.Text = remainingMinutes.ToString();
        }
    }

    private void OnSecondsTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (SecondsInput.Text == null || string.IsNullOrWhiteSpace(SecondsInput.Text))
        {
            return;
        }

        if (!int.TryParse(SecondsInput.Text, out var seconds) || seconds < 0)
        {
            return;
        }

        // 如果秒数 >= 60，自动换算为分钟和秒
        if (seconds >= 60)
        {
            var minutes = seconds / 60;
            var remainingSeconds = seconds % 60;
            
            // 获取当前分钟数
            var currentMinutes = 0;
            if (MinutesInput.Text != null && int.TryParse(MinutesInput.Text, out var m))
            {
                currentMinutes = m;
            }
            
            // 更新分钟（如果超过60，会触发分钟换算）
            var totalMinutes = currentMinutes + minutes;
            if (totalMinutes >= 60)
            {
                var hours = totalMinutes / 60;
                var remainingMinutes = totalMinutes % 60;
                
                if (hours > 99)
                {
                    hours = 99;
                    remainingMinutes = 59;
                    remainingSeconds = 59;
                }
                
                HoursInput.Text = hours.ToString();
                MinutesInput.Text = remainingMinutes.ToString();
            }
            else
            {
                MinutesInput.Text = totalMinutes.ToString();
            }
            
            SecondsInput.Text = remainingSeconds.ToString();
        }
    }

    // 获取时、分、秒的值
    public int Hours => int.TryParse(HoursInput.Text, out var h) ? h : 0;
    public int Minutes => int.TryParse(MinutesInput.Text, out var m) ? m : 0;
    public int Seconds => int.TryParse(SecondsInput.Text, out var s) ? s : 0;
    
    // 获取倒计时状态
    public bool IsCountdown => CountdownCheckBox.IsChecked == true;
}

