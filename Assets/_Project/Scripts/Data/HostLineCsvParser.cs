using System;
using System.Collections.Generic;

/// <summary>
/// 진행자 대사 CSV 글을 <see cref="HostLine"/> 목록으로 바꾼다. 양식은 docs/04_roles_and_schedule.md 2절 (event,id,text,emotion).
/// 잘못된 줄은 건너뛰고 경고 목록에 이유를 남긴다.
/// </summary>
public static class HostLineCsvParser
{
    static readonly string[] k_Header = { "event", "id", "text", "emotion" };

    /// <param name="text">CSV 파일 내용 (UTF-8)</param>
    /// <param name="warnings">경고를 받을 목록. 필요 없으면 null</param>
    public static List<HostLine> Parse(string text, List<string> warnings = null)
    {
        var lines = new List<HostLine>();
        var records = CsvReader.ReadRecords(text);
        if (records.Count == 0 || !IsHeader(records[0].Fields))
        {
            warnings?.Add($"첫 줄이 제목 줄이 아니다. 필요한 제목 줄: {string.Join(",", k_Header)}");
            return lines;
        }

        var ids = new HashSet<string>();
        for (var i = 1; i < records.Count; i++)
        {
            var record = records[i];
            if (CsvReader.IsEmpty(record.Fields))
                continue;

            if (record.Fields.Count != k_Header.Length)
            {
                warnings?.Add($"{record.Line}번째 줄 건너뜀: 칸이 {record.Fields.Count}개다 ({k_Header.Length}개여야 한다)");
                continue;
            }

            var line = new HostLine
            {
                Event = record.Fields[0].Trim(),
                Id = record.Fields[1].Trim(),
                Text = record.Fields[2].Trim(),
                Emotion = record.Fields[3].Trim(),
            };

            if (line.Event.Length == 0 || line.Id.Length == 0 || line.Text.Length == 0)
            {
                warnings?.Add($"{record.Line}번째 줄 건너뜀: event, id, text 중 빈 칸이 있다");
                continue;
            }

            if (!ids.Add(line.Id))
            {
                warnings?.Add($"{record.Line}번째 줄 건너뜀: id '{line.Id}'가 이미 있다");
                continue;
            }

            lines.Add(line);
        }

        return lines;
    }

    static bool IsHeader(List<string> fields)
    {
        if (fields.Count != k_Header.Length)
            return false;

        for (var i = 0; i < k_Header.Length; i++)
        {
            if (!string.Equals(fields[i].Trim(), k_Header[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }
}
