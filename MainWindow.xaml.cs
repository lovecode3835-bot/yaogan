using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace FightstickLab
{
    internal enum DisplayMode
    {
        Full,
        Compact,
        Mini
    }

    internal sealed class AttemptAnalysis
    {
        public List<InputRecord> Matched { get; } = new List<InputRecord>();
        public List<InputRecord> Timeline { get; set; } = new List<InputRecord>();
        public InputRecord? WrongRecord { get; set; }
        public InputToken ExpectedToken { get; set; } = InputToken.Neutral;
        public int MissIndex { get; set; } = -1;
        public bool Slow { get; set; }
    }

    public partial class MainWindow : Window
    {
        private readonly KeyboardHook _keyboardHook = new KeyboardHook();
        private readonly GamepadMonitor _gamepadMonitor = new GamepadMonitor();
        private readonly HashSet<int> _pressedKeys = new HashSet<int>();
        private readonly HashSet<InputAction> _keyboardDirections = new HashSet<InputAction>();
        private readonly HashSet<InputAction> _keyboardButtons = new HashSet<InputAction>();
        private readonly List<InputRecord> _inputBuffer = new List<InputRecord>();
        private readonly List<int> _completionTimes = new List<int>();
        private readonly Dictionary<InputAction, Button> _bindingButtons = new Dictionary<InputAction, Button>();

        private AppSettings _settings = new AppSettings();
        private GamepadSnapshot _gamepad = new GamepadSnapshot();
        private InputToken _currentDirection = InputToken.Neutral;
        private InputAction? _capturingBinding;
        private CommandDefinition? _currentCommand;
        private long _nextRecordId;
        private long _lastMatchedRecordId;
        private long _lastFailureRecordId;
        private int _streak;
        private int _sessionSuccess;
        private int _sessionFails;
        private readonly Dictionary<string, int> _failByStep = new Dictionary<string, int>();
        private DateTime _lastSuccess = DateTime.MinValue;
        private bool _facingLeft;
        private DisplayMode _displayMode = DisplayMode.Full;
        private bool _loaded;
        private Rect _fullBounds;

        public ObservableCollection<InputRecord> History { get; } = new ObservableCollection<InputRecord>();
        public ObservableCollection<AssistTokenView> AssistProgress { get; } = new ObservableCollection<AssistTokenView>();
        public ObservableCollection<AssistTokenView> AttemptTimeline { get; } = new ObservableCollection<AssistTokenView>();
        public ObservableCollection<TimingCellView> Timing { get; } = new ObservableCollection<TimingCellView>();
        public ObservableCollection<AttemptResultView> RecentResults { get; } = new ObservableCollection<AttemptResultView>();

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            Loaded += Window_Loaded;
            ContentRendered += Window_ContentRendered;
            Closed += Window_Closed;
        }

        private void Window_ContentRendered(object? sender, EventArgs e)
        {
            var arguments = Environment.GetCommandLineArgs();
            var snapshotIndex = Array.IndexOf(arguments, "--snapshot");
            if (snapshotIndex < 0) return;

            ContentRendered -= Window_ContentRendered;
            if (Array.IndexOf(arguments, "--mini") >= 0) SetDisplayMode(DisplayMode.Mini);
            else if (Array.IndexOf(arguments, "--compact") >= 0) SetDisplayMode(DisplayMode.Compact);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            timer.Tick += (_, __) =>
            {
                timer.Stop();
                var width = Math.Max(1, (int)Math.Ceiling(MainSurface.ActualWidth));
                var height = Math.Max(1, (int)Math.Ceiling(MainSurface.ActualHeight));
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(MainSurface);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                var output = snapshotIndex + 1 < arguments.Length
                    ? arguments[snapshotIndex + 1]
                    : Path.Combine(AppContext.BaseDirectory, "preview.png");
                using (var stream = File.Create(output)) encoder.Save(stream);
                Close();
            };
            timer.Start();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _settings = SettingsStore.Load();
            if (_settings.Bindings.Count == 0) _settings.Bindings = AppSettings.DefaultBindings();
            if (_settings.Commands.Count == 0) _settings.Commands = AppSettings.DefaultCommands();

            DeadzoneSlider.Value = _settings.GamepadDeadzone;
            _gamepadMonitor.Deadzone = _settings.GamepadDeadzone;
            DirectionNumberCheckBox.IsChecked = _settings.ShowDirectionNumbers;
            ApplyDirectionNumberVisibility();
            BuildBindingRows();
            RefreshCommandList();

            _keyboardHook.KeyChanged += KeyboardHook_KeyChanged;
            try { _keyboardHook.Start(); }
            catch (Exception error)
            {
                InputStatusText.Text = error.Message;
                InputStatusText.Foreground = new SolidColorBrush(Color.FromRgb(255, 120, 108));
            }

            _gamepadMonitor.StateChanged += GamepadMonitor_StateChanged;
            _gamepadMonitor.Start();
            _loaded = true;
        }

        private void Window_Closed(object? sender, EventArgs e)
        {
            _keyboardHook.Dispose();
            _gamepadMonitor.Dispose();
            SettingsStore.Save(_settings);
        }

        private void KeyboardHook_KeyChanged(int virtualKey, bool isDown)
        {
            Dispatcher.BeginInvoke(new Action(() => HandleKeyboardChange(virtualKey, isDown)));
        }

        private void HandleKeyboardChange(int virtualKey, bool isDown)
        {
            if (_capturingBinding.HasValue && isDown)
            {
                var action = _capturingBinding.Value;
                _settings.Bindings[action] = virtualKey;
                _capturingBinding = null;
                _pressedKeys.Add(virtualKey);
                BuildBindingRows();
                SettingsStore.Save(_settings);
                InputStatusText.Text = $"已绑定 {ActionName(action)} → {KeyName(virtualKey)}";
                return;
            }

            if (OwnedWindows.Cast<Window>().Any(window => window.IsVisible)) return;

            if (isDown)
            {
                if (!_pressedKeys.Add(virtualKey)) return;
            }
            else
            {
                if (!_pressedKeys.Remove(virtualKey)) return;
            }

            var actions = _settings.Bindings.Where(pair => pair.Value == virtualKey).Select(pair => pair.Key).ToList();
            foreach (var action in actions) ApplyKeyboardAction(action, isDown);
            if (actions.Count > 0) DeviceStatusText.Text = "键盘";
        }

        private void ApplyKeyboardAction(InputAction action, bool isDown)
        {
            if (action <= InputAction.Right)
            {
                if (isDown) _keyboardDirections.Add(action); else _keyboardDirections.Remove(action);
                UpdateDirection();
                return;
            }

            if (isDown)
            {
                if (_keyboardButtons.Add(action))
                {
                    SetActionVisual(action, true);
                    AddInput(ActionToken(action));
                }
            }
            else
            {
                _keyboardButtons.Remove(action);
                SetActionVisual(action, GamepadActionPressed(action));
            }
        }

        private void GamepadMonitor_StateChanged(GamepadSnapshot snapshot)
        {
            Dispatcher.BeginInvoke(new Action(() => ApplyGamepadState(snapshot)));
        }

        private void ApplyGamepadState(GamepadSnapshot next)
        {
            if (next.Connected)
            {
                var isDirect = next.Source == "DirectInput";
                GamepadStatusText.Text = isDirect
                    ? "手柄 · DirectInput"
                    : next.Slot >= 0 ? $"手柄 · 槽{next.Slot}" : "手柄 · 已连接";
                GamepadStatusText.Foreground = (Brush)FindResource("SuccessBrush");
                GamepadStatusText.ToolTip = isDirect
                    ? "DirectInput / HID 摇杆（按钮映射：第1~4个按键 = A B C D）"
                    : $"XInput 手柄已连接 (槽{next.Slot})";
                DeviceStatusText.Text = isDirect ? "DirectInput 手柄" : "XInput 手柄";
            }
            else
            {
                GamepadStatusText.Text = "未连接";
                GamepadStatusText.Foreground = (Brush)FindResource("MutedBrush");
                GamepadStatusText.ToolTip = string.IsNullOrEmpty(next.Note)
                    ? "未检测到 XInput 手柄。若为 DirectInput 街机摇杆/杂牌手柄，本程序暂不支持，需要 XInput 兼容模式。"
                    : next.Note;
            }

            ApplyGamepadButton(InputAction.LightPunch, next.LightPunch, _gamepad.LightPunch);
            ApplyGamepadButton(InputAction.HeavyPunch, next.HeavyPunch, _gamepad.HeavyPunch);
            ApplyGamepadButton(InputAction.LightKick, next.LightKick, _gamepad.LightKick);
            ApplyGamepadButton(InputAction.HeavyKick, next.HeavyKick, _gamepad.HeavyKick);
            _gamepad = next;
            UpdateDirection();
        }

        private void ApplyGamepadButton(InputAction action, bool pressed, bool wasPressed)
        {
            if (pressed && !wasPressed) AddInput(ActionToken(action));
            SetActionVisual(action, pressed || _keyboardButtons.Contains(action));
        }

        private void UpdateDirection()
        {
            var up = _keyboardDirections.Contains(InputAction.Up) || _gamepad.Up;
            var down = _keyboardDirections.Contains(InputAction.Down) || _gamepad.Down;
            var left = _keyboardDirections.Contains(InputAction.Left) || _gamepad.Left;
            var right = _keyboardDirections.Contains(InputAction.Right) || _gamepad.Right;
            var x = (right ? 1 : 0) - (left ? 1 : 0);
            var y = (down ? 1 : 0) - (up ? 1 : 0);
            var next = DirectionFromVector(x, y);
            if (next == _currentDirection) return;

            _currentDirection = next;
            var distance = 84.0;
            Glide(StickTransform, x * distance, y * distance);
            Glide(StickDotTransform, x * distance, y * distance);
            Glide(StickShadowTransform, x * distance * 0.72, y * distance * 0.72);
            Glide(CompactStickTransform, x * 44, y * 44);
            Glide(CompactDotTransform, x * 44, y * 44);
            Glide(MiniStickTransform, x * 26, y * 26);
            Glide(MiniDotTransform, x * 26, y * 26);
            HighlightDirection(next);
            var directionGlyph = _settings.ShowDirectionNumbers ? TokenInfo.Notation(next) : TokenInfo.Glyph(next);
            CurrentDirectionGlyph.Text = directionGlyph;
            CurrentDirectionName.Text = TokenInfo.Name(next);
            MiniDirectionGlyph.Text = directionGlyph;
            MiniDirectionName.Text = TokenInfo.Name(next);
            AddInput(next);
        }

        private static void Glide(TranslateTransform transform, double x, double y)
        {
            var duration = TimeSpan.FromMilliseconds(120);
            var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, duration) { EasingFunction = easing });
            transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, duration) { EasingFunction = easing });
        }

        private static readonly Brush DirNumberDefault = new SolidColorBrush(Color.FromRgb(214, 221, 216));
        private static readonly Brush DirNumberActive = new SolidColorBrush(Color.FromRgb(239, 78, 62));

        private void HighlightDirection(InputToken dir)
        {
            TextBlock? active;
            switch (dir)
            {
                case InputToken.Up: active = Dir8; break;
                case InputToken.UpRight: active = Dir9; break;
                case InputToken.Right: active = Dir6; break;
                case InputToken.DownRight: active = Dir3; break;
                case InputToken.Down: active = Dir2; break;
                case InputToken.DownLeft: active = Dir1; break;
                case InputToken.Left: active = Dir4; break;
                case InputToken.UpLeft: active = Dir7; break;
                default: active = null; break;
            }
            foreach (var t in new[] { Dir1, Dir2, Dir3, Dir4, Dir5, Dir6, Dir7, Dir8, Dir9 })
                t.Foreground = DirNumberDefault;
            if (active != null) active.Foreground = DirNumberActive;
        }

        private void AddInput(InputToken token)
        {
            var now = DateTime.Now;
            var previous = _inputBuffer.LastOrDefault();
            var record = new InputRecord
            {
                Id = ++_nextRecordId,
                Token = token,
                Time = now,
                DeltaMs = previous == null ? 0 : (int)(now - previous.Time).TotalMilliseconds
            };

            record.Forbidden = token != InputToken.Neutral && _currentCommand != null && _currentCommand.Forbidden.Contains(token);

            _inputBuffer.Add(record);
            if (_inputBuffer.Count > HistoryExporter.MaxExportRecords) _inputBuffer.RemoveAt(0);
            History.Insert(0, record);
            while (History.Count > 80) History.RemoveAt(History.Count - 1);
            EmptyHistory.Visibility = Visibility.Collapsed;
            UpdateHistoryCount();
            UpdateHistoryActions();

            if (token != InputToken.Neutral) CheckCommandMatch();
            UpdateAssistPanel();
        }

        private void CheckCommandMatch()
        {
            if (_currentCommand == null) return;
            var result = InputMatcher.TryMatch(_inputBuffer, _currentCommand, _facingLeft);
            if (result == null || result.Records.Last().Id == _lastMatchedRecordId) return;

            _lastMatchedRecordId = result.Records.Last().Id;
            foreach (var record in result.Records)
            {
                record.Completed = true;
                record.MoveName = string.Empty;
            }
            result.Records.Last().MoveName = $"{_currentCommand.Name} · {result.DurationMs}ms";

            _streak = DateTime.Now - _lastSuccess <= TimeSpan.FromSeconds(5) ? _streak + 1 : 1;
            _lastSuccess = DateTime.Now;
            _sessionSuccess++;
            _completionTimes.Add(result.DurationMs);
            AddResultChip($"{result.DurationMs}ms", _currentCommand.Name, "Success");
            StreakText.Text = $"{_streak} 连续";
            MiniStreakText.Text = _streak.ToString();
            SuccessCountText.Text = _completionTimes.Count.ToString();
            AverageTimeText.Text = $"{(int)_completionTimes.Average()}ms";
            BestTimeText.Text = $"{_completionTimes.Min()}ms";
        }

        private void UpdateAssistPanel()
        {
            AssistProgress.Clear();

            if (_currentCommand == null || _currentCommand.Sequence.Count == 0)
            {
                AssistDiagnosisText.Text = "先选择或新建一个练习招式。";
                return;
            }

            var expected = InputMatcher.Flatten(_currentCommand.EffectiveSegments, _facingLeft);
            var analysis = AnalyzeCurrentAttempt(expected);
            var recentSuccess = DateTime.Now - _lastSuccess <= TimeSpan.FromSeconds(1.8) && _completionTimes.Count > 0;

            for (var i = 0; i < expected.Count; i++)
            {
                var status = recentSuccess ? "Done" : i < analysis.Matched.Count ? "Done" : i == analysis.MissIndex ? "Miss" : i == analysis.Matched.Count ? "Next" : "Idle";
                AssistProgress.Add(new AssistTokenView { Token = expected[i], Status = status });
            }

            var notation = _inputBuffer
                .Where(record => record.Token != InputToken.Neutral)
                .TakeLast(14)
                .Select(record => TokenInfo.Notation(record.Token));
            NotationText.Text = string.Join("  ", notation);

            AssistDiagnosisText.Foreground = (Brush)FindResource("MutedBrush");
            AssistDiagnosisText.Text = BuildDiagnosis(expected, analysis);

            if (analysis.WrongRecord != null && analysis.Matched.Count > 0 && analysis.WrongRecord.Id != _lastFailureRecordId)
            {
                _lastFailureRecordId = analysis.WrongRecord.Id;
                AddResultChip("失败", analysis.Slow ? "间隔过慢" : $"错按 {TokenInfo.Glyph(analysis.WrongRecord.Token)}", "Fail");
                _sessionFails++;
                var label = SegmentLabel(analysis.MissIndex);
                _failByStep[label] = _failByStep.GetValueOrDefault(label) + 1;
            }

            BuildTiming(expected);
            BuildPrecision(expected);
            BuildStats();

            UpdateRecentSummary();
        }

        private void BuildTiming(IReadOnlyList<InputToken> expected)
        {
            Timing.Clear();
            var gapLimit = _currentCommand != null ? _currentCommand.MaxGapMs : 0;
            var recent = _inputBuffer.Where(record => record.Token != InputToken.Neutral).TakeLast(10).ToList();
            foreach (var record in recent)
            {
                var gap = record.DeltaMs;
                string color = "#3A3F40";
                if (gapLimit > 0)
                {
                    if (gap <= gapLimit) color = "#2F6A48";
                    else if (gap <= gapLimit * 1.25) color = "#B98A2E";
                    else color = "#8A2E2A";
                }
                Timing.Add(new TimingCellView
                {
                    Glyph = TokenInfo.Notation(record.Token),
                    GapText = gap <= 0 ? "·" : gap.ToString(),
                    Color = color
                });
            }
        }

        private void BuildPrecision(IReadOnlyList<InputToken> expected)
        {
            if (expected.Count == 0) { PrecisionText.Text = string.Empty; return; }
            var actual = _inputBuffer.Where(record => record.Token != InputToken.Neutral).TakeLast(expected.Count).ToList();
            if (actual.Count == 0) { PrecisionText.Text = string.Empty; return; }

            var targetStr = string.Concat(expected.Select(TokenInfo.Notation));
            var actualStr = string.Concat(actual.Select(record => TokenInfo.Notation(record.Token)));

            if (actualStr == targetStr) { PrecisionText.Text = "指令干净 ✓"; return; }

            var mismatch = -1;
            for (var i = 0; i < Math.Min(expected.Count, actual.Count); i++)
            {
                if (actual[i].Token != expected[i]) { mismatch = i; break; }
            }
            var extras = actual.Count > expected.Count
                ? string.Concat(actual.Skip(expected.Count).Select(record => TokenInfo.Notation(record.Token)))
                : string.Empty;

            var flags = string.Empty;
            if (mismatch >= 0) flags += $"　@{mismatch + 1} 错";
            if (!string.IsNullOrEmpty(extras)) flags += $"　混入 {extras}";
            if (actual.Count < expected.Count) flags += "　缺尾";
            PrecisionText.Text = $"目标 {targetStr} → 实际 {actualStr}" + flags;
        }

        private void BuildStats()
        {
            var total = _sessionSuccess + _sessionFails;
            if (total == 0) { StatsText.Text = string.Empty; return; }
            var rate = (int)(_sessionSuccess * 100.0 / total);
            var text = $"成功 {_sessionSuccess} · 失败 {_sessionFails} · 成功率 {rate}%";
            if (_failByStep.Count > 0)
            {
                var weakest = _failByStep.OrderByDescending(pair => pair.Value).First();
                text += $"　｜　最弱 {weakest.Key} ×{weakest.Value}";
            }
            StatsText.Text = text;
        }

        private void UpdateRecentSummary()
        {
            if (RecentResults.Count == 0)
            {
                RecentSummaryText.Text = string.Empty;
                return;
            }
            var total = RecentResults.Count;
            var fail = RecentResults.Count(result => result.Kind == "Fail");
            var ok = total - fail;
            var avg = _completionTimes.Count > 0 ? $" · 平均{(int)_completionTimes.Average()}ms" : string.Empty;
            RecentSummaryText.Text = $"最近 {total} 次 · {ok}成 {fail}败{avg}";
        }

        private AttemptAnalysis AnalyzeCurrentAttempt(IReadOnlyList<InputToken> expected)
        {
            var analysis = new AttemptAnalysis();
            var usable = _inputBuffer
                .Where(record => record.Token != InputToken.Neutral && record.Id > _lastMatchedRecordId)
                .ToList();

            foreach (var record in usable)
            {
                if (analysis.WrongRecord != null)
                {
                    if (record.Token == expected[0])
                    {
                        analysis.Matched.Clear();
                        analysis.Matched.Add(record);
                        analysis.Timeline.Clear();
                        analysis.Timeline.Add(record);
                        analysis.WrongRecord = null;
                        analysis.MissIndex = -1;
                    }
                    else
                    {
                        analysis.WrongRecord = record;
                        analysis.ExpectedToken = expected[0];
                        analysis.MissIndex = 0;
                        analysis.Slow = false;
                        analysis.Timeline.Add(record);
                    }
                    continue;
                }

                if (analysis.Matched.Count == 0)
                {
                    if (record.Token == expected[0])
                    {
                        analysis.Matched.Add(record);
                        analysis.Timeline.Clear();
                        analysis.Timeline.Add(record);
                        analysis.WrongRecord = null;
                        analysis.MissIndex = -1;
                    }
                    else
                    {
                        analysis.WrongRecord = record;
                        analysis.ExpectedToken = expected[0];
                        analysis.MissIndex = 0;
                        analysis.Timeline.Clear();
                        analysis.Timeline.Add(record);
                    }
                    continue;
                }

                if (analysis.Matched.Count >= expected.Count) break;

                var nextIndex = analysis.Matched.Count;
                var gap = (record.Time - analysis.Matched.Last().Time).TotalMilliseconds;
                var total = (record.Time - analysis.Matched[0].Time).TotalMilliseconds;
                var expectedToken = expected[nextIndex];
                if (record.Token == expectedToken && gap <= _currentCommand!.MaxGapMs && total <= _currentCommand.WindowMs)
                {
                    analysis.Matched.Add(record);
                    analysis.Timeline.Add(record);
                    analysis.WrongRecord = null;
                    analysis.MissIndex = -1;
                    continue;
                }

                if (record.Token == expected[0])
                {
                    analysis.Matched.Clear();
                    analysis.Matched.Add(record);
                    analysis.Timeline.Clear();
                    analysis.Timeline.Add(record);
                    analysis.WrongRecord = null;
                    analysis.MissIndex = -1;
                    continue;
                }

                analysis.WrongRecord = record;
                analysis.ExpectedToken = expectedToken;
                analysis.MissIndex = nextIndex;
                analysis.Slow = record.Token == expectedToken && (gap > _currentCommand!.MaxGapMs || total > _currentCommand.WindowMs);
                analysis.Timeline.Add(record);
            }

            if (analysis.Timeline.Count > 10) analysis.Timeline = analysis.Timeline.Skip(analysis.Timeline.Count - 10).ToList();
            return analysis;
        }

        private string BuildDiagnosis(IReadOnlyList<InputToken> expected, AttemptAnalysis analysis)
        {
            if (DateTime.Now - _lastSuccess <= TimeSpan.FromSeconds(1.8) && _currentCommand != null)
            {
                return $"完成 {_currentCommand.Name}。继续保持同样节奏。";
            }

            if (analysis.WrongRecord != null)
            {
                var label = SegmentLabel(analysis.MissIndex);
                if (analysis.Slow)
                {
                    return $"断在{label}：{TokenInfo.Glyph(analysis.ExpectedToken)} 过慢。当前限制 {_currentCommand!.MaxGapMs}ms。";
                }
                return $"断在{label}：需要 {TokenInfo.Glyph(analysis.ExpectedToken)}，实际 {TokenInfo.Glyph(analysis.WrongRecord.Token)}。";
            }

            if (analysis.Matched.Count == 0)
            {
                return $"等待{SegmentLabel(0)}：{TokenInfo.Glyph(expected[0])}。";
            }

            if (analysis.Matched.Count >= expected.Count)
            {
                return "输入已到最后一步，等待完成判定。";
            }

            var next = expected[analysis.Matched.Count];
            var elapsed = (int)(analysis.Matched.Last().Time - analysis.Matched.First().Time).TotalMilliseconds;
            return $"已命中 {analysis.Matched.Count} 步。下一步{SegmentLabel(analysis.Matched.Count)}：{TokenInfo.Glyph(next)}。本次已用 {elapsed}ms。";
        }

        private string SegmentLabel(int tokenIndex)
        {
            if (_currentCommand == null || !_currentCommand.IsCombo) return $"第 {tokenIndex + 1} 步";
            var running = 0;
            for (var seg = 0; seg < _currentCommand.EffectiveSegments.Count; seg++)
            {
                var count = _currentCommand.EffectiveSegments[seg].Sequence.Count;
                if (tokenIndex < running + count)
                {
                    return $"第 {seg + 1} 段第 {tokenIndex - running + 1} 步";
                }
                running += count;
            }
            return $"第 {tokenIndex + 1} 步";
        }

        private void AddResultChip(string text, string detail, string kind)
        {
            RecentResults.Insert(0, new AttemptResultView { Text = text, Detail = detail, Kind = kind });
            while (RecentResults.Count > 10) RecentResults.RemoveAt(RecentResults.Count - 1);
        }

        private void RefreshCommandList(Guid? selectId = null)
        {
            CommandComboBox.ItemsSource = null;
            CommandComboBox.ItemsSource = _settings.Commands;
            var targetId = selectId ?? _settings.SelectedCommandId;
            var selected = _settings.Commands.FirstOrDefault(command => command.Id == targetId) ?? _settings.Commands.First();
            CommandComboBox.SelectedItem = selected;
        }

        private void Command_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(CommandComboBox.SelectedItem is CommandDefinition command)) return;
            _currentCommand = command;
            _settings.SelectedCommandId = command.Id;
            CharacterText.Text = command.Character;
            CommandNameText.Text = command.Name;
            FighterBadgeText.Text = command.Character.Length > 0 ? command.Character.Substring(command.Character.Length - 1) : "斗";
            if (command.IsCombo)
            {
                WindowRuleText.Text = string.Join("/", command.EffectiveSegments.Select(segment => segment.WindowMs.ToString()));
                GapRuleText.Text = string.Join("/", command.EffectiveSegments.Select(segment => segment.MaxGapMs.ToString()));
            }
            else
            {
                WindowRuleText.Text = $"{command.WindowMs}ms";
                GapRuleText.Text = $"{command.MaxGapMs}ms";
            }
            CompactCommandText.Text = command.Name;
            MiniCommandText.Text = command.Name;
            _lastMatchedRecordId = _inputBuffer.LastOrDefault()?.Id ?? 0;
            ResetStats();
            UpdateAssistPanel();
            SettingsStore.Save(_settings);
        }

        private void NewCommand_Click(object sender, RoutedEventArgs e)
        {
            var command = new CommandDefinition { Character = "自定义角色", Name = "新招式" };
            OpenEditor(command, true);
        }

        private void EditCommand_Click(object sender, RoutedEventArgs e)
        {
            if (_currentCommand != null) OpenEditor(_currentCommand, false);
        }

        private void OpenEditor(CommandDefinition command, bool isNew)
        {
            var editor = new CommandEditorWindow(command, isNew) { Owner = this };
            if (editor.ShowDialog() != true) return;

            if (editor.DeleteRequested)
            {
                if (_settings.Commands.Count == 1)
                {
                    MessageBox.Show(this, "至少保留一个训练招式。", "无法删除", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                _settings.Commands.RemoveAll(item => item.Id == command.Id);
                RefreshCommandList();
            }
            else if (editor.Result != null)
            {
                if (isNew) _settings.Commands.Add(editor.Result);
                else
                {
                    var index = _settings.Commands.FindIndex(item => item.Id == command.Id);
                    if (index >= 0) _settings.Commands[index] = editor.Result;
                }
                RefreshCommandList(editor.Result.Id);
            }
            SettingsStore.Save(_settings);
        }

        private void BuildBindingRows()
        {
            if (BindingsPanel == null) return;
            BindingsPanel.Children.Clear();
            _bindingButtons.Clear();
            foreach (var action in Enum.GetValues(typeof(InputAction)).Cast<InputAction>())
            {
                var row = new Border
                {
                    Height = 42,
                    Background = (Brush)FindResource("PanelAltBrush"),
                    BorderBrush = (Brush)FindResource("LineBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Margin = new Thickness(0, 0, 0, 7)
                };
                var grid = new Grid { Margin = new Thickness(11, 0, 6, 0) };
                grid.Children.Add(new TextBlock { Text = ActionName(action), VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("MutedBrush"), FontSize = 11 });
                var button = new Button
                {
                    Content = _capturingBinding == action ? "按键…" : KeyName(_settings.Bindings[action]),
                    Tag = action,
                    Style = (Style)FindResource("TextButton"),
                    Height = 28,
                    MinWidth = 48,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = _capturingBinding == action ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("TextBrush")
                };
                button.Click += CaptureBinding_Click;
                grid.Children.Add(button);
                row.Child = grid;
                BindingsPanel.Children.Add(row);
                _bindingButtons[action] = button;
            }
        }

        private void CaptureBinding_Click(object sender, RoutedEventArgs e)
        {
            _capturingBinding = (InputAction)((Button)sender).Tag;
            BuildBindingRows();
            InputStatusText.Text = $"请按下“{ActionName(_capturingBinding.Value)}”的新键位";
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            SettingsPanel.Visibility = SettingsPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ResetSettings_Click(object sender, RoutedEventArgs e)
        {
            _settings.Bindings = AppSettings.DefaultBindings();
            _settings.GamepadDeadzone = 0.42;
            _settings.ShowDirectionNumbers = true;
            DeadzoneSlider.Value = 0.42;
            DirectionNumberCheckBox.IsChecked = true;
            ApplyDirectionNumberVisibility();
            BuildBindingRows();
            SettingsStore.Save(_settings);
        }

        private void Deadzone_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (DeadzoneText == null) return;
            DeadzoneText.Text = e.NewValue.ToString("0.00");
            _gamepadMonitor.Deadzone = e.NewValue;
            if (_loaded)
            {
                _settings.GamepadDeadzone = e.NewValue;
                SettingsStore.Save(_settings);
            }
        }

        private void DirectionNumbers_Changed(object sender, RoutedEventArgs e)
        {
            if (DirectionNumberLayer == null || CompactDirectionNumberLayer == null || MiniDirectionNumberLayer == null) return;
            _settings.ShowDirectionNumbers = DirectionNumberCheckBox.IsChecked == true;
            ApplyDirectionNumberVisibility();
            if (_loaded) SettingsStore.Save(_settings);
        }

        private void ApplyDirectionNumberVisibility()
        {
            var visibility = _settings.ShowDirectionNumbers ? Visibility.Visible : Visibility.Collapsed;
            DirectionNumberLayer.Visibility = visibility;
            CompactDirectionNumberLayer.Visibility = visibility;
            MiniDirectionNumberLayer.Visibility = visibility;
            var directionGlyph = _settings.ShowDirectionNumbers ? TokenInfo.Notation(_currentDirection) : TokenInfo.Glyph(_currentDirection);
            CurrentDirectionGlyph.Text = directionGlyph;
            MiniDirectionGlyph.Text = directionGlyph;
        }

        private void SetActionVisual(InputAction action, bool active)
        {
            var idle = (Brush)FindResource("PanelAltBrush");
            var brush = active ? new SolidColorBrush(Color.FromRgb(58, 63, 64)) : idle;
            switch (action)
            {
                case InputAction.LightPunch: LightPunchButton.Background = brush; CompactLP.Background = brush; break;
                case InputAction.HeavyPunch: HeavyPunchButton.Background = brush; CompactHP.Background = brush; break;
                case InputAction.LightKick: LightKickButton.Background = brush; CompactLK.Background = brush; break;
                case InputAction.HeavyKick: HeavyKickButton.Background = brush; CompactHK.Background = brush; break;
            }
        }

        private bool GamepadActionPressed(InputAction action)
        {
            switch (action)
            {
                case InputAction.LightPunch: return _gamepad.LightPunch;
                case InputAction.HeavyPunch: return _gamepad.HeavyPunch;
                case InputAction.LightKick: return _gamepad.LightKick;
                case InputAction.HeavyKick: return _gamepad.HeavyKick;
                default: return false;
            }
        }

        private static InputToken ActionToken(InputAction action)
        {
            switch (action)
            {
                case InputAction.LightPunch: return InputToken.LightPunch;
                case InputAction.HeavyPunch: return InputToken.HeavyPunch;
                case InputAction.LightKick: return InputToken.LightKick;
                default: return InputToken.HeavyKick;
            }
        }

        private static InputToken DirectionFromVector(int x, int y)
        {
            if (x == 0 && y < 0) return InputToken.Up;
            if (x > 0 && y < 0) return InputToken.UpRight;
            if (x > 0 && y == 0) return InputToken.Right;
            if (x > 0 && y > 0) return InputToken.DownRight;
            if (x == 0 && y > 0) return InputToken.Down;
            if (x < 0 && y > 0) return InputToken.DownLeft;
            if (x < 0 && y == 0) return InputToken.Left;
            if (x < 0 && y < 0) return InputToken.UpLeft;
            return InputToken.Neutral;
        }

        private static string ActionName(InputAction action)
        {
            switch (action)
            {
                case InputAction.Up: return "上";
                case InputAction.Down: return "下";
                case InputAction.Left: return "左";
                case InputAction.Right: return "右";
                case InputAction.LightPunch: return "轻拳 A";
                case InputAction.LightKick: return "轻脚 B";
                case InputAction.HeavyPunch: return "重拳 C";
                default: return "重脚 D";
            }
        }

        private static string KeyName(int virtualKey)
        {
            var key = KeyInterop.KeyFromVirtualKey(virtualKey);
            var name = key.ToString();
            if (name.StartsWith("D") && name.Length == 2) return name.Substring(1);
            return name.Replace("Oem", string.Empty);
        }

        private void FaceRight_Click(object sender, RoutedEventArgs e)
        {
            _facingLeft = false;
            FaceRightButton.Background = new SolidColorBrush(Color.FromRgb(58, 63, 64));
            FaceLeftButton.Background = (Brush)FindResource("PanelAltBrush");
            UpdateAssistPanel();
        }

        private void FaceLeft_Click(object sender, RoutedEventArgs e)
        {
            _facingLeft = true;
            FaceLeftButton.Background = new SolidColorBrush(Color.FromRgb(58, 63, 64));
            FaceRightButton.Background = (Brush)FindResource("PanelAltBrush");
            UpdateAssistPanel();
        }

        private void ResetStats()
        {
            _completionTimes.Clear();
            RecentResults.Clear();
            _lastFailureRecordId = 0;
            _streak = 0;
            _lastSuccess = DateTime.MinValue;
            SuccessCountText.Text = "0";
            AverageTimeText.Text = "—";
            BestTimeText.Text = "—";
            StreakText.Text = "0 连续";
            MiniStreakText.Text = "0";
            UpdateRecentSummary();
        }

        private void ClearHistory_Click(object sender, RoutedEventArgs e)
        {
            History.Clear();
            _inputBuffer.Clear();
            _lastMatchedRecordId = 0;
            UpdateHistoryCount();
            EmptyHistory.Visibility = Visibility.Visible;
            UpdateHistoryActions();
            ResetStats();
            UpdateAssistPanel();
        }

        private void CopyHistory_Click(object sender, RoutedEventArgs e)
        {
            if (History.Count == 0) return;
            try
            {
                Clipboard.SetText(HistoryExporter.Serialize(_inputBuffer));
                InputStatusText.Text = $"已复制 {_inputBuffer.Count} 条输入 JSON";
            }
            catch (Exception error)
            {
                MessageBox.Show(this, $"无法写入剪贴板：{error.Message}", "复制失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ExportHistory_Click(object sender, RoutedEventArgs e)
        {
            if (History.Count == 0) return;
            var dialog = new SaveFileDialog
            {
                Title = "保存最近输入 JSON",
                Filter = "JSON 数据 (*.json)|*.json",
                DefaultExt = ".json",
                AddExtension = true,
                FileName = $"fight-inputs-{DateTime.Now:yyyyMMdd-HHmmss}.json"
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                File.WriteAllText(dialog.FileName, HistoryExporter.Serialize(_inputBuffer));
                InputStatusText.Text = $"已保存 {_inputBuffer.Count} 条输入 JSON";
            }
            catch (Exception error)
            {
                MessageBox.Show(this, $"无法保存文件：{error.Message}", "保存失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void UpdateHistoryActions()
        {
            var hasHistory = _inputBuffer.Count > 0;
            CopyHistoryButton.IsEnabled = hasHistory;
            ExportHistoryButton.IsEnabled = hasHistory;
            ClearHistoryButton.IsEnabled = hasHistory;
        }

        private void UpdateHistoryCount()
        {
            HistoryCountText.Text = $"{_inputBuffer.Count} / {HistoryExporter.MaxExportRecords}";
        }

        private void Topmost_Changed(object sender, RoutedEventArgs e)
        {
            if (_loaded) Topmost = TopmostCheckBox.IsChecked == true;
        }

        private void Compact_Click(object sender, RoutedEventArgs e)
        {
            SetDisplayMode(_displayMode == DisplayMode.Compact ? DisplayMode.Full : DisplayMode.Compact);
        }

        private void Mini_Click(object sender, RoutedEventArgs e)
        {
            SetDisplayMode(_displayMode == DisplayMode.Mini ? DisplayMode.Full : DisplayMode.Mini);
        }

        private void SetDisplayMode(DisplayMode mode)
        {
            if (mode != DisplayMode.Full && _displayMode == DisplayMode.Full)
            {
                _fullBounds = new Rect(Left, Top, Width, Height);
            }

            SettingsPanel.Visibility = Visibility.Collapsed;

            FullView.Visibility = mode == DisplayMode.Full ? Visibility.Visible : Visibility.Collapsed;
            CompactView.Visibility = mode == DisplayMode.Compact ? Visibility.Visible : Visibility.Collapsed;
            MiniView.Visibility = mode == DisplayMode.Mini ? Visibility.Visible : Visibility.Collapsed;

            if (mode == DisplayMode.Full)
            {
                ResizeMode = ResizeMode.NoResize;
                BrandSubtitle.Visibility = Visibility.Visible;
                HeaderStatusPanel.Visibility = Visibility.Visible;
                TopmostCheckBox.Visibility = Visibility.Visible;
                SettingsHeaderButton.Visibility = Visibility.Visible;
                MinimizeHeaderButton.Visibility = Visibility.Visible;
                CompactHeaderButton.Background = (Brush)FindResource("PanelAltBrush");
                MiniHeaderButton.Background = (Brush)FindResource("PanelAltBrush");
                MinWidth = 900;
                MinHeight = 620;
                Width = Math.Max(_fullBounds.Width, 900);
                Height = Math.Max(_fullBounds.Height, 620);
                if (_fullBounds.Width > 0)
                {
                    Left = _fullBounds.Left;
                    Top = _fullBounds.Top;
                }
            }
            else
            {
                ResizeMode = ResizeMode.NoResize;
                BrandSubtitle.Visibility = Visibility.Collapsed;
                HeaderStatusPanel.Visibility = Visibility.Collapsed;
                TopmostCheckBox.Visibility = Visibility.Collapsed;
                SettingsHeaderButton.Visibility = Visibility.Collapsed;
                MinimizeHeaderButton.Visibility = Visibility.Collapsed;
                Topmost = true;
                TopmostCheckBox.IsChecked = true;
                CompactHeaderButton.Background = mode == DisplayMode.Compact
                    ? new SolidColorBrush(Color.FromRgb(58, 63, 64))
                    : (Brush)FindResource("PanelAltBrush");
                MiniHeaderButton.Background = mode == DisplayMode.Mini
                    ? new SolidColorBrush(Color.FromRgb(58, 63, 64))
                    : (Brush)FindResource("PanelAltBrush");

                if (mode == DisplayMode.Compact)
                {
                    MinWidth = 340;
                    MinHeight = 500;
                    Width = 360;
                    Height = 620;
                }
                else
                {
                    MinWidth = 390;
                    MinHeight = 210;
                    Width = 430;
                    Height = 236;
                }
            }

            _displayMode = mode;
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && _displayMode == DisplayMode.Full)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                return;
            }
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
