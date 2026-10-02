using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fieldmate.AI;
using Fieldmate.Knowledge;
using Fieldmate.Providers;
using UnityEditor;
using UnityEngine;

namespace Fieldmate.Editor;

/// <summary>
/// Fieldmate → Run AI Eval (design.md §5.4, #17): runs the fixture's 25 utterances against the real model through the
/// configured proxy (editor keys.json), with the same harness the CI contract tests use, and writes docs/eval/report.md
/// plus a dated copy (docs/eval/ is gitignored). Calls the paid API: run it by hand before a release.
/// Batch mode: <c>-executeMethod Fieldmate.Editor.AIEvalRunner.RunFromCommandLine</c> exits 0 when ≥ 80 % pass.
/// </summary>
public static class AIEvalRunner
{
    public const string FixturePath = "Assets/Fieldmate.Tests/AIEval/utterances.json";
    private const string ManualPath = "Assets/_Project/Manual/manual.json";
    private const double PassBar = 0.8;

    [MenuItem("Fieldmate/Run AI Eval")]
    public static async void RunFromMenu()
    {
        if (!EditorUtility.DisplayDialog("Run AI eval",
                "Sends the 25 eval utterances to the real model through the proxy (a few cents). Continue?", "Run", "Cancel"))
        {
            return;
        }

        try
        {
            var report = await RunAsync(progress: (i, n, id) =>
                EditorUtility.DisplayCancelableProgressBar("AI eval", $"{i + 1}/{n}: {id}", (float)i / n));
            EditorUtility.RevealInFinder(report);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Eval] {e.Message}");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    public static async void RunFromCommandLine()
    {
        var code = 1;
        try
        {
            var path = await RunAsync((i, n, id) => { Debug.Log($"[Eval] {i + 1}/{n} {id}"); return false; });
            var summary = File.ReadLines(path).First();
            Debug.Log($"[Eval] {summary}");
            var parts = summary.Split(' ')[2].Split('/');
            code = double.Parse(parts[0]) / double.Parse(parts[1]) >= PassBar ? 0 : 2;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Eval] {e.Message}");
        }

        EditorApplication.Exit(code);
    }

    /// <summary>Runs every case; <paramref name="progress"/> returns true to cancel. Returns the report path.</summary>
    public static async Task<string> RunAsync(Func<int, int, string, bool> progress)
    {
        var providers = AssistantProviders.Load();
        if (!providers.IsEnabled)
        {
            throw new InvalidOperationException($"No assistant provider: {providers.DisabledReason}");
        }

        var manual = MachineManual.Parse(File.ReadAllText(ManualPath));
        var cases = EvalCase.ParseFixture(File.ReadAllText(FixturePath));
        var harness = new EvalHarness(manual, _ => providers.Chat);
        var results = new List<EvalResult>();
        using var cancel = new CancellationTokenSource();
        for (var i = 0; i < cases.Count; i++)
        {
            if (progress(i, cases.Count, cases[i].Id))
            {
                cancel.Cancel();
                break;
            }

            var result = await harness.RunAsync(cases[i], cancel.Token);
            results.Add(result);
            Debug.Log($"[Eval] {(result.Passed ? "pass" : "FAIL")} {cases[i].Id} {result.Seconds:0.0}s {string.Join("; ", result.Failures)}");
        }

        var markdown = EvalReport.Markdown(results, providers.Chat.Name, DateTime.UtcNow);
        var dir = Path.Combine(Directory.GetCurrentDirectory(), "docs", "eval");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "report.md");
        File.WriteAllText(path, markdown);
        File.WriteAllText(Path.Combine(dir, $"report-{DateTime.UtcNow:yyyyMMdd-HHmm}.md"), markdown);
        Debug.Log($"[Eval] {results.Count(r => r.Passed)}/{results.Count} passed → {path}");
        return path;
    }
}
