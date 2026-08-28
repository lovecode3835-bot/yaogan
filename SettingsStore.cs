using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace FightstickLab
{
    public sealed class AppSettings
    {
        public Dictionary<InputAction, int> Bindings { get; set; } = DefaultBindings();
        public List<CommandDefinition> Commands { get; set; } = DefaultCommands();
        public Guid? SelectedCommandId { get; set; }
        public double GamepadDeadzone { get; set; } = 0.42;
        public bool ShowDirectionNumbers { get; set; } = true;

        public static Dictionary<InputAction, int> DefaultBindings() => new Dictionary<InputAction, int>
        {
            [InputAction.Up] = 0x57,
            [InputAction.Down] = 0x53,
            [InputAction.Left] = 0x41,
            [InputAction.Right] = 0x44,
            [InputAction.LightPunch] = 0x4A,
            [InputAction.HeavyPunch] = 0x4B,
            [InputAction.LightKick] = 0x55,
            [InputAction.HeavyKick] = 0x49
        };

        public static List<CommandDefinition> DefaultCommands() => new List<CommandDefinition>
        {
            new CommandDefinition
            {
                Character = "八神庵", Name = "葵花三段", WindowMs = 2400, MaxGapMs = 460,
                Sequence = new List<InputToken>
                {
                    InputToken.Down, InputToken.DownLeft, InputToken.Left, InputToken.LightPunch,
                    InputToken.Down, InputToken.DownLeft, InputToken.Left, InputToken.LightPunch,
                    InputToken.Down, InputToken.DownLeft, InputToken.Left, InputToken.LightPunch
                }
            },
            new CommandDefinition
            {
                Character = "八神庵", Name = "百式·鬼烧", WindowMs = 900, MaxGapMs = 320,
                Sequence = new List<InputToken> { InputToken.Right, InputToken.Down, InputToken.DownRight, InputToken.HeavyPunch }
            },
            new CommandDefinition
            {
                Character = "八神庵", Name = "琴月阴", WindowMs = 1100, MaxGapMs = 340,
                Sequence = new List<InputToken> { InputToken.Right, InputToken.DownRight, InputToken.Down, InputToken.DownLeft, InputToken.Left, InputToken.HeavyKick }
            },
            new CommandDefinition
            {
                Character = "八神庵", Name = "禁千二百十一式·八稚女", WindowMs = 1500, MaxGapMs = 360,
                Sequence = new List<InputToken>
                {
                    InputToken.Down, InputToken.DownRight, InputToken.Right,
                    InputToken.Down, InputToken.DownLeft, InputToken.Left, InputToken.LightPunch
                }
            }
        };
    }

    public static class SettingsStore
    {
        private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FightstickLab");
        private static readonly string FilePath = Path.Combine(Folder, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new AppSettings();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), options) ?? new AppSettings();
            }
            catch { return new AppSettings(); }
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, options));
            }
            catch
            {
                // 设置保存失败（如目录只读、被沙箱/杀软拦截）不致命，忽略即可
            }
        }
    }
}
