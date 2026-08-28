using System;
using System.Collections.Generic;
using System.Text.Json;
using FightstickLab;

internal static class Program
{
    private static int Main()
    {
        try
        {
            MatchesQuarterCircleBack();
            MirrorsForFacingLeft();
            RejectsSlowInput();
            MatchesFullAoiHanaSequence();
            ExportsReadableChronologicalJson();
            UsesStandardNumpadNotation();
            Console.WriteLine("6 项输入、显示与导出自检全部通过。");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static void MatchesQuarterCircleBack()
    {
        var command = Command("波动", 800, 300, InputToken.Down, InputToken.DownLeft, InputToken.Left, InputToken.LightPunch);
        Assert(InputMatcher.TryMatch(Records(100, command.Sequence.ToArray()), command, false) != null, "背向四分之一圈应匹配。");
    }

    private static void MirrorsForFacingLeft()
    {
        var command = Command("鬼烧", 900, 300, InputToken.Right, InputToken.Down, InputToken.DownRight, InputToken.HeavyPunch);
        var actual = new[] { InputToken.Left, InputToken.Down, InputToken.DownLeft, InputToken.HeavyPunch };
        Assert(InputMatcher.TryMatch(Records(100, actual), command, true) != null, "面向左时应镜像水平方向。");
    }

    private static void RejectsSlowInput()
    {
        var command = Command("超时测试", 900, 180, InputToken.Down, InputToken.DownRight, InputToken.Right, InputToken.LightPunch);
        Assert(InputMatcher.TryMatch(Records(250, command.Sequence.ToArray()), command, false) == null, "超出单步间隔的输入不应匹配。");
    }

    private static void MatchesFullAoiHanaSequence()
    {
        var sequence = new List<InputToken>();
        for (var i = 0; i < 3; i++) sequence.AddRange(new[] { InputToken.Down, InputToken.DownLeft, InputToken.Left, InputToken.LightPunch });
        var command = Command("葵花三段", 2400, 460, sequence.ToArray());
        Assert(InputMatcher.TryMatch(Records(120, sequence.ToArray()), command, false)?.Records.Count == 12, "葵花三段应标记完整 12 步输入。");
    }

    private static void ExportsReadableChronologicalJson()
    {
        var start = new DateTime(2026, 8, 24, 10, 20, 30, 120, DateTimeKind.Local);
        var newestFirst = new List<InputRecord>
        {
            new InputRecord { Id = 2, Token = InputToken.LightPunch, Time = start.AddMilliseconds(80), DeltaMs = 80 },
            new InputRecord { Id = 1, Token = InputToken.Down, Time = start, DeltaMs = 0 }
        };
        var json = HistoryExporter.Serialize(newestFirst);
        using var document = JsonDocument.Parse(json);
        var items = document.RootElement;
        Assert(items.GetArrayLength() == 2, "导出 JSON 应包含全部最近输入。");
        Assert(items[0].GetProperty("输入键位").GetString() == "↓", "导出 JSON 应按时间从旧到新排序。");
        Assert(items[1].GetProperty("间隔").GetInt32() == 80, "间隔应以整数毫秒导出。");
        Assert(json.Contains("\"时间\"") && !json.Contains("\\u65f6"), "中文字段名应保持可读。");

        var overLimit = new List<InputRecord>();
        for (var i = 0; i < 1005; i++)
        {
            overLimit.Add(new InputRecord { Id = i + 1, Token = InputToken.Right, Time = start.AddMilliseconds(i), DeltaMs = i });
        }
        using var limitedDocument = JsonDocument.Parse(HistoryExporter.Serialize(overLimit));
        var limitedItems = limitedDocument.RootElement;
        Assert(limitedItems.GetArrayLength() == 1000, "导出应限制为最近 1000 条输入。");
        Assert(limitedItems[0].GetProperty("间隔").GetInt32() == 5, "超过上限时应淘汰最早记录。");
        Assert(limitedItems[999].GetProperty("间隔").GetInt32() == 1004, "导出应保留最新记录。");
    }

    private static void UsesStandardNumpadNotation()
    {
        Assert(TokenInfo.Notation(InputToken.DownLeft) == "1", "左下应使用数字 1。");
        Assert(TokenInfo.Notation(InputToken.Down) == "2", "下应使用数字 2。");
        Assert(TokenInfo.Notation(InputToken.DownRight) == "3", "右下应使用数字 3。");
        Assert(TokenInfo.Notation(InputToken.Left) == "4" && TokenInfo.Notation(InputToken.Neutral) == "5" && TokenInfo.Notation(InputToken.Right) == "6", "中排方向数字应为 456。");
        Assert(TokenInfo.Notation(InputToken.UpLeft) == "7" && TokenInfo.Notation(InputToken.Up) == "8" && TokenInfo.Notation(InputToken.UpRight) == "9", "上排方向数字应为 789。");
    }

    private static CommandDefinition Command(string name, int window, int gap, params InputToken[] sequence) =>
        new CommandDefinition { Name = name, WindowMs = window, MaxGapMs = gap, Sequence = new List<InputToken>(sequence) };

    private static List<InputRecord> Records(int intervalMs, params InputToken[] tokens)
    {
        var start = DateTime.Now;
        var result = new List<InputRecord>();
        for (var i = 0; i < tokens.Length; i++) result.Add(new InputRecord { Id = i + 1, Token = tokens[i], Time = start.AddMilliseconds(i * intervalMs) });
        return result;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"自检失败：{message}");
    }
}
