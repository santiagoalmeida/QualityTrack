using System.Text;

namespace QualityTrack.Application.Findings;

public enum AiAssistant
{
    Claude = 0,
    Copilot = 1,
    Codex = 2
}

/// <summary>
/// Writes the instruction a developer pastes into an AI coding assistant to fix findings. The task is the
/// same for every assistant; what changes is how each one is told where to look and how to finish.
/// </summary>
public static class FixPromptBuilder
{
    /// <summary>Locations listed in a prompt that covers a whole rule; beyond this the prompt would not fit a chat box.</summary>
    public const int MaxLocations = 40;

    private const int MaxExplanationLength = 1200;

    /// <summary>Prompt to fix every listed finding of a rule.</summary>
    public static string ForRule(AiAssistant assistant, RuleFindings rule) => Build(assistant, rule, rule.Findings, rule.Total);

    /// <summary>Prompt to fix a single finding, with its code.</summary>
    public static string ForFinding(AiAssistant assistant, RuleFindings rule, FindingDetail finding) => Build(assistant, rule, [finding], 1);

    private static string Build(AiAssistant assistant, RuleFindings rule, IReadOnlyList<FindingDetail> findings, int total)
    {
        var listed = findings.Take(MaxLocations).ToList();
        var files = listed.Select(f => f.FilePath).Distinct().ToList();
        var text = new StringBuilder();

        if (assistant == AiAssistant.Copilot)
        {
            // Copilot Chat attaches files through #file: references.
            text.AppendLine(string.Join(' ', files.Select(f => $"#file:{f}")));
            text.AppendLine();
        }

        text.AppendLine(assistant == AiAssistant.Codex
            ? "Trabaja en este repositorio y aplica los cambios directamente."
            : "Necesito que corrijas incidencias de análisis estático en este repositorio.");
        text.AppendLine();
        text.AppendLine(total == 1
            ? $"Corrige esta incidencia de la regla «{rule.RuleName}» ({rule.RuleKey}) en el proyecto {rule.Context.ProjectName}."
            : $"Corrige las {total} incidencias de la regla «{rule.RuleName}» ({rule.RuleKey}) en el proyecto {rule.Context.ProjectName}.");

        AppendSection(text, "Por qué es un problema", rule.Description);
        AppendSection(text, "Cómo se corrige", rule.HowToFix);

        text.AppendLine();
        text.AppendLine(listed.Count == 1 ? "Ubicación:" : "Ubicaciones:");
        foreach (var finding in listed)
            text.AppendLine($"- {Location(finding)} — {finding.Message}");
        if (total > listed.Count)
            text.AppendLine($"- …y {total - listed.Count} más de la misma regla: búscalas en el proyecto y corrígelas igual.");

        if (listed is [{ SnippetLines.Count: > 0 } only])
        {
            text.AppendLine();
            text.AppendLine("Código actual (la línea marcada con > es la que hay que corregir):");
            text.AppendLine("```");
            for (var i = 0; i < only.SnippetLines.Count; i++)
            {
                var number = only.SnippetStartLine!.Value + i;
                var marked = only.Line is { } line && number >= line && number <= (only.EndLine ?? line);
                text.AppendLine($"{(marked ? ">" : " ")} {number,4} | {only.SnippetLines[i]}");
            }

            text.AppendLine("```");
        }

        text.AppendLine();
        text.AppendLine("Condiciones:");
        text.AppendLine("- Cambia solo lo necesario para resolver la incidencia; no alteres el comportamiento.");
        text.AppendLine("- Si alguna es un falso positivo, no la toques y explica por qué.");
        text.AppendLine(assistant switch
        {
            AiAssistant.Claude => "- Lee cada archivo antes de editarlo, corre los tests al terminar y resume qué cambiaste en cada archivo.",
            AiAssistant.Copilot => "- Propón los cambios archivo por archivo e indica qué tests debo correr para comprobarlos.",
            _ => "- Corre los tests al terminar y muestra el diff final."
        });

        return text.ToString().TrimEnd();
    }

    private static void AppendSection(StringBuilder text, string title, string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return;

        var trimmed = body.Trim();
        text.AppendLine();
        text.AppendLine($"{title}:");
        text.AppendLine(trimmed.Length > MaxExplanationLength ? trimmed[..MaxExplanationLength].TrimEnd() + "…" : trimmed);
    }

    private static string Location(FindingDetail finding) =>
        finding.Line is { } line ? $"{finding.FilePath}:{line}" : finding.FilePath;
}
