using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FightstickLab
{
    public sealed class HistoryExportItem
    {
        [JsonPropertyName("时间")]
        public string Time { get; set; } = string.Empty;

        [JsonPropertyName("间隔")]
        public int Interval { get; set; }

        [JsonPropertyName("输入键位")]
        public string InputKey { get; set; } = string.Empty;
    }

    public static class HistoryExporter
    {
        public const int MaxExportRecords = 1000;

        public static string Serialize(IEnumerable<InputRecord> records)
        {
            var items = records
                .OrderByDescending(record => record.Time)
                .ThenByDescending(record => record.Id)
                .Take(MaxExportRecords)
                .OrderBy(record => record.Time)
                .ThenBy(record => record.Id)
                .Select(record => new HistoryExportItem
                {
                    Time = record.Time.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz"),
                    Interval = record.DeltaMs,
                    InputKey = TokenInfo.Glyph(record.Token)
                });

            return JsonSerializer.Serialize(items, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }
    }
}
