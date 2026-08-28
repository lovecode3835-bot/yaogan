using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace FightstickLab
{
    public partial class CommandEditorWindow : Window
    {
        private sealed class SegmentEdit
        {
            public List<InputToken> Tokens { get; } = new List<InputToken>();
            public string WindowText { get; set; } = "1200";
            public string GapText { get; set; } = "420";
            public StackPanel? Preview { get; set; }
            public TextBox? CommandBox { get; set; }
        }

        private readonly List<SegmentEdit> _segments = new List<SegmentEdit>();
        private readonly HashSet<InputToken> _forbidden = new HashSet<InputToken>();
        private readonly bool _isNew;
        private int _activeSegment;
        private bool _suppress;

        public CommandDefinition? Result { get; private set; }
        public bool DeleteRequested { get; private set; }

        public CommandEditorWindow(CommandDefinition source, bool isNew)
        {
            InitializeComponent();
            _isNew = isNew;
            CharacterBox.Text = source.Character;
            NameBox.Text = source.Name;
            foreach (var seg in source.EffectiveSegments)
            {
                var edit = new SegmentEdit { WindowText = seg.WindowMs.ToString(), GapText = seg.MaxGapMs.ToString() };
                foreach (var token in seg.Sequence) edit.Tokens.Add(token);
                _segments.Add(edit);
            }
            if (_segments.Count == 0) _segments.Add(new SegmentEdit());
            foreach (var token in source.Forbidden) _forbidden.Add(token);
            BuildForbiddenPalette(source.Forbidden);
            RebuildSegmentsPanel();
            DeleteButton.Visibility = isNew ? Visibility.Collapsed : Visibility.Visible;
            Result = new CommandDefinition { Id = source.Id };
        }

        private void BuildForbiddenPalette(IReadOnlyCollection<InputToken> forbidden)
        {
            var toggle = (Style)FindResource("ForbiddenToggle");
            foreach (var token in Enum.GetValues(typeof(InputToken)).Cast<InputToken>().Where(t => t != InputToken.Neutral))
            {
                var button = new ToggleButton
                {
                    Content = TokenInfo.Glyph(token),
                    Tag = token,
                    IsChecked = forbidden.Contains(token),
                    Style = toggle,
                    ToolTip = TokenInfo.Name(token),
                    Cursor = Cursors.Hand
                };
                button.Checked += ToggleForbidden_Changed;
                button.Unchecked += ToggleForbidden_Changed;
                ForbiddenPalette.Children.Add(button);
            }
        }

        private void ToggleForbidden_Changed(object sender, RoutedEventArgs e)
        {
            if (((ToggleButton)sender).Tag is InputToken token)
            {
                if (((ToggleButton)sender).IsChecked == true) _forbidden.Add(token);
                else _forbidden.Remove(token);
            }
        }

        // ---------- 段落面板 ----------
        private void RebuildSegmentsPanel()
        {
            SegmentsPanel.Children.Clear();
            for (var i = 0; i < _segments.Count; i++) SegmentsPanel.Children.Add(BuildSegmentBorder(_segments[i], i));
        }

        private UIElement BuildSegmentBorder(SegmentEdit edit, int index)
        {
            var active = index == _activeSegment;
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x11, 0x14, 0x15)),
                BorderBrush = new SolidColorBrush(active ? Color.FromRgb(0xEF, 0x4E, 0x3E) : Color.FromRgb(0x44, 0x4A, 0x4B)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var stack = new StackPanel();

            var header = new DockPanel();
            var segButton = new Button { Content = $"段 {index + 1}", Style = (Style)FindResource("TextButton"), Cursor = Cursors.Hand, Foreground = active ? new SolidColorBrush(Color.FromRgb(0xEF, 0x4E, 0x3E)) : new SolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xEF)) };
            segButton.Click += (s, e) => { _activeSegment = index; RebuildSegmentsPanel(); };
            DockPanel.SetDock(segButton, Dock.Left);
            header.Children.Add(segButton);
            if (index > 0)
            {
                var remove = new Button { Content = "移除段", Style = (Style)FindResource("TextButton"), Cursor = Cursors.Hand, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x78, 0x6C)) };
                remove.Click += (s, e) => { _segments.RemoveAt(index); if (_activeSegment >= _segments.Count) _activeSegment = _segments.Count - 1; RebuildSegmentsPanel(); };
                DockPanel.SetDock(remove, Dock.Right);
                header.Children.Add(remove);
            }
            header.Children.Add(new TextBlock { Text = active ? "（当前段）" : string.Empty, Foreground = new SolidColorBrush(Color.FromRgb(0x93, 0x9B, 0x97)), FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
            stack.Children.Add(header);

            // 指令输入框（直接用 numpad 记法）
            var cmdLabel = new TextBlock { Text = "指令（numpad，如 236A / 623C / →↓↘A）", Foreground = new SolidColorBrush(Color.FromRgb(0x93, 0x9B, 0x97)), FontSize = 10, Margin = new Thickness(0, 8, 0, 4) };
            stack.Children.Add(cmdLabel);
            var cmdBox = new TextBox
            {
                Text = SerializeCommand(edit.Tokens),
                Height = 32,
                Padding = new Thickness(8, 5, 8, 5),
                Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x11, 0x12)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xF2, 0xEF)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x30, 0x35, 0x36)),
                BorderThickness = new Thickness(1),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 14
            };
            edit.CommandBox = cmdBox;
            cmdBox.TextChanged += (s, e) =>
            {
                if (_suppress) return;
                var parsed = ParseCommand(cmdBox.Text);
                edit.Tokens.Clear();
                edit.Tokens.AddRange(parsed);
                RefillPreview(edit);
            };
            stack.Children.Add(cmdBox);

            // 已加步骤预览（可点删除）
            var preview = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            edit.Preview = preview;
            stack.Children.Add(preview);
            RefillPreview(edit);

            var timing = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            timing.Children.Add(new TextBlock { Text = "窗口 ", Foreground = new SolidColorBrush(Color.FromRgb(0x93, 0x9B, 0x97)), FontSize = 10, VerticalAlignment = VerticalAlignment.Center });
            timing.Children.Add(MakeTextBox(edit, true));
            timing.Children.Add(new TextBlock { Text = " ms    间隔 ", Foreground = new SolidColorBrush(Color.FromRgb(0x93, 0x9B, 0x97)), FontSize = 10, VerticalAlignment = VerticalAlignment.Center });
            timing.Children.Add(MakeTextBox(edit, false));
            timing.Children.Add(new TextBlock { Text = " ms", Foreground = new SolidColorBrush(Color.FromRgb(0x93, 0x9B, 0x97)), FontSize = 10, VerticalAlignment = VerticalAlignment.Center });
            stack.Children.Add(timing);

            border.Child = stack;
            return border;
        }

        private void RefillPreview(SegmentEdit edit)
        {
            if (edit.Preview == null) return;
            edit.Preview.Children.Clear();
            for (var i = 0; i < edit.Tokens.Count; i++)
            {
                var idx = i;
                var chip = new Button
                {
                    Content = TokenInfo.Glyph(edit.Tokens[i]),
                    Width = 34, Height = 30, Margin = new Thickness(0, 0, 6, 0),
                    Background = new SolidColorBrush(Color.FromRgb(0x29, 0x2D, 0x2E)),
                    Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x48, 0x4E, 0x4F)),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    ToolTip = "点我移除该步"
                };
                chip.Click += (s, e) => { edit.Tokens.RemoveAt(idx); _suppress = true; if (edit.CommandBox != null) edit.CommandBox.Text = SerializeCommand(edit.Tokens); _suppress = false; RefillPreview(edit); };
                edit.Preview.Children.Add(chip);
            }
            if (edit.Tokens.Count == 0)
            {
                edit.Preview.Children.Add(new TextBlock { Text = "（指令框直接输入，或点下方键位）", Foreground = new SolidColorBrush(Color.FromRgb(0x93, 0x9B, 0x97)), FontSize = 10, VerticalAlignment = VerticalAlignment.Center });
            }
        }

        private TextBox MakeTextBox(SegmentEdit edit, bool isWindow)
        {
            var box = new TextBox
            {
                Text = isWindow ? edit.WindowText : edit.GapText,
                Width = 52, Height = 30, Padding = new Thickness(6, 4, 6, 4),
                Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x11, 0x12)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x30, 0x35, 0x36)),
                BorderThickness = new Thickness(1)
            };
            box.TextChanged += (s, e) => { if (isWindow) edit.WindowText = box.Text; else edit.GapText = box.Text; };
            return box;
        }

        // ---------- 指令记法 解析/生成 ----------
        private static string SerializeCommand(IReadOnlyList<InputToken> tokens)
        {
            var sb = new StringBuilder();
            foreach (var token in tokens) sb.Append(ToChar(token));
            return sb.ToString();
        }

        private static char ToChar(InputToken t)
        {
            switch (t)
            {
                case InputToken.Down: return '2';
                case InputToken.DownRight: return '3';
                case InputToken.Right: return '6';
                case InputToken.Up: return '8';
                case InputToken.UpRight: return '9';
                case InputToken.Left: return '4';
                case InputToken.DownLeft: return '1';
                case InputToken.UpLeft: return '7';
                case InputToken.LightPunch: return 'A';
                case InputToken.LightKick: return 'B';
                case InputToken.HeavyPunch: return 'C';
                case InputToken.HeavyKick: return 'D';
                default: return ' ';
            }
        }

        private static List<InputToken> ParseCommand(string text)
        {
            var list = new List<InputToken>();
            foreach (var c in text)
            {
                switch (char.ToUpperInvariant(c))
                {
                    case '1': list.Add(InputToken.DownLeft); break;
                    case '2': list.Add(InputToken.Down); break;
                    case '3': list.Add(InputToken.DownRight); break;
                    case '4': list.Add(InputToken.Left); break;
                    case '6': list.Add(InputToken.Right); break;
                    case '7': list.Add(InputToken.UpLeft); break;
                    case '8': list.Add(InputToken.Up); break;
                    case '9': list.Add(InputToken.UpRight); break;
                    case 'A': list.Add(InputToken.LightPunch); break;
                    case 'B': list.Add(InputToken.LightKick); break;
                    case 'C': list.Add(InputToken.HeavyPunch); break;
                    case 'D': list.Add(InputToken.HeavyKick); break;
                    default: break;
                }
            }
            return list;
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void AddSegment_Click(object sender, RoutedEventArgs e)
        {
            if (_segments.Count >= 12) return;
            _segments.Add(new SegmentEdit());
            _activeSegment = _segments.Count - 1;
            RebuildSegmentsPanel();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(CharacterBox.Text) || string.IsNullOrWhiteSpace(NameBox.Text))
            {
                MessageBox.Show(this, "请填写角色和招式名称。", "无法保存", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!_segments.Any(sg => sg.Tokens.Count > 0))
            {
                MessageBox.Show(this, "请至少添加一个指令步骤。", "无法保存", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var segments = new List<CommandSegment>();
            foreach (var sg in _segments)
            {
                if (sg.Tokens.Count == 0) continue;
                if (!int.TryParse(sg.WindowText, out var window) || window < 200 || window > 10000 ||
                    !int.TryParse(sg.GapText, out var gap) || gap < 50 || gap > 2000)
                {
                    MessageBox.Show(this, "每段窗口应为 200–10000ms，单步间隔应为 50–2000ms。", "时间设置无效", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                segments.Add(new CommandSegment { Sequence = sg.Tokens.ToList(), WindowMs = window, MaxGapMs = gap });
            }

            var first = segments[0];
            Result = new CommandDefinition
            {
                Id = Result?.Id ?? Guid.NewGuid(),
                Character = CharacterBox.Text.Trim(),
                Name = NameBox.Text.Trim(),
                WindowMs = first.WindowMs,
                MaxGapMs = first.MaxGapMs,
                Sequence = first.Sequence,
                Segments = segments,
                Forbidden = _forbidden.ToList()
            };
            DialogResult = true;
        }

        private void Delete_Click(object sender, RoutedEventArgs e) { DeleteRequested = true; DialogResult = true; }
        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
