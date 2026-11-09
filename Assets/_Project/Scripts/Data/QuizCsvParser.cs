using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// 문제 CSV 글을 <see cref="QuizQuestion"/> 목록으로 바꾼다. 양식은 docs/04_roles_and_schedule.md 2절.
/// 잘못된 줄은 건너뛰고 경고 목록에 이유를 남긴다. 글자 수가 길면 경고만 남기고 불러온다.
/// </summary>
public static class QuizCsvParser
{
    /// <summary>VR에서 읽기 좋은 문제 길이 (넘으면 경고만).</summary>
    public const int RecommendedQuestionLength = 30;
    /// <summary>VR에서 읽기 좋은 선택지 길이 (넘으면 경고만).</summary>
    public const int RecommendedChoiceLength = 10;

    static readonly string[] k_Header =
        { "id", "difficulty", "category", "question", "choice1", "choice2", "choice3", "choice4", "answer" };

    /// <param name="text">CSV 파일 내용 (UTF-8)</param>
    /// <param name="warnings">경고를 받을 목록. 필요 없으면 null</param>
    public static List<QuizQuestion> Parse(string text, List<string> warnings = null)
    {
        var questions = new List<QuizQuestion>();
        if (string.IsNullOrEmpty(text))
        {
            warnings?.Add("CSV 내용이 비어 있다");
            return questions;
        }

        var records = ReadRecords(text);
        if (records.Count == 0 || !IsHeader(records[0].Fields))
        {
            warnings?.Add($"첫 줄이 제목 줄이 아니다. 필요한 제목 줄: {string.Join(",", k_Header)}");
            return questions;
        }

        var ids = new HashSet<string>();
        for (var i = 1; i < records.Count; i++)
        {
            var record = records[i];
            if (IsEmpty(record.Fields))
                continue;

            if (TryCreateQuestion(record.Fields, out var question, out var error))
            {
                if (!ids.Add(question.Id))
                {
                    warnings?.Add($"{record.Line}번째 줄 건너뜀: id '{question.Id}'가 이미 있다");
                    continue;
                }

                AddLengthWarnings(question, record.Line, warnings);
                questions.Add(question);
            }
            else
            {
                warnings?.Add($"{record.Line}번째 줄 건너뜀: {error}");
            }
        }

        return questions;
    }

    static bool TryCreateQuestion(List<string> fields, out QuizQuestion question, out string error)
    {
        question = null;
        if (fields.Count != k_Header.Length)
        {
            error = $"칸이 {fields.Count}개다 ({k_Header.Length}개여야 한다)";
            return false;
        }

        var id = fields[0].Trim();
        if (id.Length == 0)
        {
            error = "id가 비어 있다";
            return false;
        }

        if (!int.TryParse(fields[1].Trim(), out var difficulty) || difficulty < 1 || difficulty > 3)
        {
            error = $"difficulty '{fields[1]}'는 1~3이어야 한다";
            return false;
        }

        var questionText = fields[3].Trim();
        if (questionText.Length == 0)
        {
            error = "question이 비어 있다";
            return false;
        }

        var choices = new string[4];
        for (var c = 0; c < choices.Length; c++)
        {
            choices[c] = fields[4 + c].Trim();
            if (choices[c].Length == 0)
            {
                error = $"choice{c + 1}이 비어 있다";
                return false;
            }
        }

        if (!int.TryParse(fields[8].Trim(), out var answer) || answer < 1 || answer > 4)
        {
            error = $"answer '{fields[8]}'는 1~4여야 한다";
            return false;
        }

        question = new QuizQuestion
        {
            Id = id,
            Difficulty = difficulty,
            Category = fields[2].Trim(),
            Question = questionText,
            Choices = choices,
            AnswerIndex = answer - 1,
        };
        error = null;
        return true;
    }

    static void AddLengthWarnings(QuizQuestion question, int line, List<string> warnings)
    {
        if (warnings == null)
            return;

        if (question.Question.Length > RecommendedQuestionLength)
            warnings.Add($"{line}번째 줄 ({question.Id}): 문제가 {question.Question.Length}자다 (권장 {RecommendedQuestionLength}자 이내)");

        for (var c = 0; c < question.Choices.Length; c++)
        {
            var length = question.Choices[c].Length;
            if (length > RecommendedChoiceLength)
                warnings.Add($"{line}번째 줄 ({question.Id}): choice{c + 1}이 {length}자다 (권장 {RecommendedChoiceLength}자 이내)");
        }
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

    static bool IsEmpty(List<string> fields)
    {
        foreach (var field in fields)
        {
            if (!string.IsNullOrWhiteSpace(field))
                return false;
        }

        return true;
    }

    struct Record
    {
        public int Line;            // 이 줄이 시작하는 파일 줄 번호 (1부터)
        public List<string> Fields;
    }

    // 따옴표 안의 쉼표·줄바꿈, 따옴표 안의 "" (따옴표 한 개), CRLF, 파일 맨 앞 BOM을 처리한다
    static List<Record> ReadRecords(string text)
    {
        var records = new List<Record>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var line = 1;
        var recordLine = 1;

        var start = text.Length > 0 && text[0] == '﻿' ? 1 : 0;
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
}
