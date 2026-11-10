using System.Collections.Generic;
using System.Text;

/// <summary>
/// CSV 글을 줄(레코드)과 칸으로 나눈다. 문제 CSV와 진행자 대사 CSV가 같이 쓴다.
/// 따옴표 안의 쉼표·줄바꿈, 따옴표 안의 "" (따옴표 한 개), CRLF, 파일 맨 앞 BOM을 처리한다.
/// </summary>
public static class CsvReader
{
    public struct Record
    {
        /// <summary>이 줄이 시작하는 파일 줄 번호 (1부터).</summary>
        public int Line;
        public List<string> Fields;
    }

    public static List<Record> ReadRecords(string text)
    {
        var records = new List<Record>();
        if (string.IsNullOrEmpty(text))
            return records;

        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var line = 1;
        var recordLine = 1;

        var start = text[0] == '﻿' ? 1 : 0;
        for (var i = start; i < text.Length; i++)
        {
            var ch = text[i];

            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    if (ch == '\n')
                        line++;
                    field.Append(ch);
                }

                continue;
            }

            switch (ch)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    records.Add(new Record { Line = recordLine, Fields = fields });
                    fields = new List<string>();
                    line++;
                    recordLine = line;
                    break;
                default:
                    field.Append(ch);
                    break;
            }
        }

        // 마지막 줄 끝에 줄바꿈이 없을 때
        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            records.Add(new Record { Line = recordLine, Fields = fields });
        }

        return records;
    }

    /// <summary>첫 칸이 #으로 시작하는 설명 줄인지. 게임은 이 줄을 읽지 않는다.</summary>
    public static bool IsComment(List<string> fields) => fields.Count > 0 && fields[0].TrimStart().StartsWith("#");

    /// <summary>모든 칸이 비어 있는 줄인지.</summary>
    public static bool IsEmpty(List<string> fields)
    {
        foreach (var field in fields)
        {
            if (!string.IsNullOrWhiteSpace(field))
                return false;
        }

        return true;
    }
}
