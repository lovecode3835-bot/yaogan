using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace FightstickLab
{
    public enum InputToken
    {
        Neutral, Up, UpRight, Right, DownRight, Down, DownLeft, Left, UpLeft,
        LightPunch, HeavyPunch, LightKick, HeavyKick
    }

    public enum InputAction
    {
        Up, Down, Left, Right, LightPunch, HeavyPunch, LightKick, HeavyKick
    }

    public sealed class CommandSegment
    {
        public List<InputToken> Sequence { get; set; } = new List<InputToken>();
        public int WindowMs { get; set; } = 1200;
        public int MaxGapMs { get; set; } = 420;
    }

    public sealed class CommandDefinition
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Character { get; set; } = "自定义角色";
        public string Name { get; set; } = "新招式";
        public int WindowMs { get; set; } = 1200;
        public int MaxGapMs { get; set; } = 420;
        public List<InputToken> Sequence { get; set; } = new List<InputToken>();
        // 多段连段：一段即一个 CommandSegment（单招 = 一段）
        public List<CommandSegment> Segments { get; set; } = new List<CommandSegment>();
        // 训练时的禁用输入（如升龙禁"上(8)"），命中即报红
        public List<InputToken> Forbidden { get; set; } = new List<InputToken>();

        [JsonIgnore]
        public bool IsCombo => Segments.Count > 1;

        [JsonIgnore]
        public IReadOnlyList<CommandSegment> EffectiveSegments => Segments.Count > 0
            ? Segments
            : new List<CommandSegment> { new CommandSegment { Sequence = Sequence, WindowMs = WindowMs, MaxGapMs = MaxGapMs } };

        public override string ToString() => $"{Character} · {Name}";
    }

    public sealed class InputRecord : INotifyPropertyChanged
    {
        private bool _completed;
        private bool _forbidden;
        private string _moveName = string.Empty;

        public long Id { get; set; }
        public InputToken Token { get; set; }
        public DateTime Time { get; set; }
        public int DeltaMs { get; set; }
        public string Glyph => TokenInfo.Glyph(Token);
        public string DisplayName => TokenInfo.Name(Token);
        public string TimeText => Time.ToString("HH:mm:ss.fff");
        public string DeltaText => DeltaMs <= 0 ? "起始" : $"+{DeltaMs}ms";

        public bool Completed
        {
            get => _completed;
            set { _completed = value; OnPropertyChanged(); }
        }

        public bool Forbidden
        {
            get => _forbidden;
            set { _forbidden = value; OnPropertyChanged(); }
        }

        public string MoveName
        {
            get => _moveName;
            set { _moveName = value; OnPropertyChanged(); OnPropertyChanged(nameof(MoveText)); }
        }

        public string MoveText => string.IsNullOrEmpty(MoveName) ? string.Empty : $"完成 · {MoveName}";
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class TokenView
    {
        public InputToken Token { get; set; }
        public string Glyph => TokenInfo.Glyph(Token);
        public string Name => TokenInfo.Name(Token);
        public bool IsAction => Token >= InputToken.LightPunch;
    }

    public sealed class TimingCellView
    {
        public string Glyph { get; set; } = string.Empty;
        public string GapText { get; set; } = string.Empty;
        public string Timing { get; set; } = "Ok";   // Ok / Slow / Over
        public string Color { get; set; } = "#3A3F40";
    }

    // SF6 式输入时间轴：每帧一格，上=方向、下=按键（按帧对齐，看拳脚与方向的协调）
    public sealed class FrameCellView
    {
        public string Top { get; set; } = string.Empty;   // 方向（箭头）
        public string Bottom { get; set; } = string.Empty; // 按键（A/B/C/D）
        public string GapText { get; set; } = string.Empty; // 该帧间隔 ms
        public string Color { get; set; } = "#3A3F40";
        public bool Changed { get; set; }
    }

    public sealed class AssistTokenView
    {
        public InputToken Token { get; set; }
        public string Glyph => TokenInfo.Glyph(Token);
        public string Name => TokenInfo.Name(Token);
        public string DeltaText { get; set; } = string.Empty;
        public string Status { get; set; } = "Idle";
    }

    public sealed class AttemptResultView
    {
        public string Text { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public string Kind { get; set; } = "Neutral";
    }

    public static class TokenInfo
    {
        public static string Notation(InputToken token)
        {
            switch (token)
            {
                case InputToken.UpLeft: return "7";
                case InputToken.Up: return "8";
                case InputToken.UpRight: return "9";
                case InputToken.Left: return "4";
                case InputToken.Neutral: return "5";
                case InputToken.Right: return "6";
                case InputToken.DownLeft: return "1";
                case InputToken.Down: return "2";
                case InputToken.DownRight: return "3";
                default: return Glyph(token);
            }
        }

        public static string Glyph(InputToken token)
        {
            switch (token)
            {
                case InputToken.Up: return "↑";
                case InputToken.UpRight: return "↗";
                case InputToken.Right: return "→";
                case InputToken.DownRight: return "↘";
                case InputToken.Down: return "↓";
                case InputToken.DownLeft: return "↙";
                case InputToken.Left: return "←";
                case InputToken.UpLeft: return "↖";
                case InputToken.LightPunch: return "A";
                case InputToken.LightKick: return "B";
                case InputToken.HeavyPunch: return "C";
                case InputToken.HeavyKick: return "D";
                default: return "●";
            }
        }

        public static string Name(InputToken token)
        {
            switch (token)
            {
                case InputToken.Up: return "上";
                case InputToken.UpRight: return "右上";
                case InputToken.Right: return "右";
                case InputToken.DownRight: return "右下";
                case InputToken.Down: return "下";
                case InputToken.DownLeft: return "左下";
                case InputToken.Left: return "左";
                case InputToken.UpLeft: return "左上";
                case InputToken.LightPunch: return "轻拳";
                case InputToken.HeavyPunch: return "重拳";
                case InputToken.LightKick: return "轻脚";
                case InputToken.HeavyKick: return "重脚";
                default: return "回中";
            }
        }

        public static InputToken Mirror(InputToken token)
        {
            switch (token)
            {
                case InputToken.Left: return InputToken.Right;
                case InputToken.Right: return InputToken.Left;
                case InputToken.UpLeft: return InputToken.UpRight;
                case InputToken.UpRight: return InputToken.UpLeft;
                case InputToken.DownLeft: return InputToken.DownRight;
                case InputToken.DownRight: return InputToken.DownLeft;
                default: return token;
            }
        }
    }
}
