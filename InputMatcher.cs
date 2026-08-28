using System;
using System.Collections.Generic;
using System.Linq;

namespace FightstickLab
{
    public sealed class MatchResult
    {
        public IReadOnlyList<InputRecord> Records { get; set; } = Array.Empty<InputRecord>();
        public int DurationMs { get; set; }
        public int SegmentsMatched { get; set; }
        public int SegmentBreaksAt { get; set; } = -1;
    }

    public static class InputMatcher
    {
        // 把多段连段展平成单个 token 列表（按朝向做镜像）
        public static List<InputToken> Flatten(IReadOnlyList<CommandSegment> segments, bool facingLeft)
        {
            var list = new List<InputToken>();
            foreach (var segment in segments)
                foreach (var token in segment.Sequence)
                    list.Add(facingLeft ? TokenInfo.Mirror(token) : token);
            return list;
        }

        public static MatchResult? TryMatch(IReadOnlyList<InputRecord> chronologicalRecords, CommandDefinition command, bool facingLeft)
        {
            var segments = command.EffectiveSegments;
            if (segments.Count == 0) return null;

            var expected = Flatten(segments, facingLeft);
            var usable = chronologicalRecords.Where(record => record.Token != InputToken.Neutral).ToList();
            if (expected.Count == 0 || usable.Count < expected.Count) return null;

            var candidate = usable.Skip(usable.Count - expected.Count).ToList();
            var offset = usable.Count - expected.Count;
            for (var i = 0; i < expected.Count; i++)
            {
                if (candidate[i].Token != expected[i]) return null;
            }

            // 每段各自的窗口/间隔约束
            var tokenIndex = 0;
            var segmentIndex = 0;
            foreach (var segment in segments)
            {
                var count = segment.Sequence.Count;
                var segFirst = candidate[offset + tokenIndex];
                var segLast = candidate[offset + tokenIndex + count - 1];
                if ((segLast.Time - segFirst.Time).TotalMilliseconds > segment.WindowMs) return null;
                for (var j = tokenIndex + 1; j < tokenIndex + count; j++)
                {
                    if ((candidate[offset + j].Time - candidate[offset + j - 1].Time).TotalMilliseconds > segment.MaxGapMs) return null;
                }
                tokenIndex += count;
                segmentIndex++;
            }

            var duration = (int)(candidate[candidate.Count - 1].Time - candidate[0].Time).TotalMilliseconds;
            return new MatchResult
            {
                Records = candidate,
                DurationMs = duration,
                SegmentsMatched = segments.Count
            };
        }
    }
}
