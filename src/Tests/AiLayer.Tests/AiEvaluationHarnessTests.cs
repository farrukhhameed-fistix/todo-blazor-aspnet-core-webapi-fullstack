using Fistix.TaskManager.AiLayer.Evaluation;
using Fistix.TaskManager.AiLayer.Shared;
using System.Text.Json;

namespace Fistix.TaskManager.AiLayer.Tests;

public class ClassificationAccuracyHarnessTests
{
    private static string FindSampleCsv()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "samples", "ai-eval-todos.csv");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate samples/ai-eval-todos.csv from test base directory.");
    }

    [Fact]
    public void LoadCsv_ReadsExpectedPriorityColumn()
    {
        var rows = ClassificationAccuracyHarness.LoadCsv(FindSampleCsv());

        Assert.NotEmpty(rows);
        Assert.Contains(rows, r => !string.IsNullOrWhiteSpace(r.ExpectedPriority));
    }

    [Fact]
    public void Score_ComputesAccuracyAndConfusion()
    {
        var rows = new[]
        {
            new ClassifyEvalRow { Title = "a", ExpectedPriority = "HIGH" },
            new ClassifyEvalRow { Title = "b", ExpectedPriority = "LOW" }
        };

        var report = ClassificationAccuracyHarness.Score(
            rows,
            row => (row.Title == "a" ? "HIGH" : "MEDIUM", 0.9f));

        Assert.Equal(2, report.TotalLabeled);
        Assert.Equal(1, report.Correct);
        Assert.Equal(0.5, report.Accuracy);
        Assert.Equal(AiPromptVersions.Classify, report.PromptVersion);
    }

    [Fact]
    public void GuardrailPredict_ForcesHighForBlockerKeywords()
    {
        var row = new ClassifyEvalRow
        {
            Title = "Prod auth outage – users cannot login",
            Description = "Blocking entire team",
            ExpectedPriority = "HIGH",
            Category = "Auth"
        };

        var (priority, confidence) = ClassificationAccuracyHarness.PredictWithGuardrailsOnly(row);

        Assert.Equal("HIGH", priority);
        Assert.True(confidence >= 0.85f);
    }

    [Fact]
    public void SafetyCases_FromCsv_PassGuardrailExpectations()
    {
        var rows = ClassificationAccuracyHarness.LoadCsv(FindSampleCsv())
            .Where(ClassificationAccuracyHarness.IsSafetyCase)
            .Where(r => !string.IsNullOrWhiteSpace(r.ExpectedPriority))
            .ToList();

        Assert.NotEmpty(rows);

        var report = ClassificationAccuracyHarness.Score(
            rows,
            ClassificationAccuracyHarness.PredictWithGuardrailsOnly);

        // Guardrails alone won't match every safety label (e.g. docs XSS title),
        // but blocker keyword rows must match HIGH.
        var blockers = report.Cases.Where(c =>
            c.Row.Title.Contains("cannot login", StringComparison.OrdinalIgnoreCase) ||
            c.Row.Description.Contains("cannot login", StringComparison.OrdinalIgnoreCase));
        Assert.All(blockers, c => Assert.Equal("HIGH", c.PredictedPriority));
    }
}

public class RagTriadEvaluatorTests
{
    private static string FindSampleRag()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "samples", "ai-eval-rag.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate samples/ai-eval-rag.json");
    }

    [Fact]
    public void LoadFixtures_ReadsCases()
    {
        var cases = RagTriadEvaluator.LoadFixtures(FindSampleRag());
        Assert.Contains(cases, c => c.ExpectInsufficient);
    }

    [Fact]
    public void ContextRelevance_RecallAtK()
    {
        var score = RagTriadEvaluator.ScoreContextRelevance(
            ["Stripe webhook"],
            ["Stripe webhook timeouts under load", "Other"]);

        Assert.Equal(1.0, score);
    }

    [Fact]
    public void InsufficientCase_PassesWhenEmptySources()
    {
        var evalCase = new RagEvalCase { Id = "empty", ExpectInsufficient = true };
        var scores = RagTriadEvaluator.ScoreCase(
            evalCase,
            retrievedTitles: [],
            answer: LlmOutputValidator.InsufficientContextMessage,
            sourceIds: []);

        Assert.True(scores.FaithfulnessPass);
        Assert.Equal(1.0, scores.ContextRelevance);
    }
}

public class ToolProposalEvalHarnessTests
{
    private static string FindSampleTools()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "samples", "ai-eval-tool-proposals.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate samples/ai-eval-tool-proposals.json");
    }

    [Fact]
    public void Score_RequiresSchemaPass()
    {
        var fixtures = ToolProposalEvalHarness.LoadFixtures(FindSampleTools());
        var create = fixtures.First(f => f.Id == "create-high-priority");

        var result = ToolProposalEvalHarness.Score(
            create,
            [
                ("create_todo", new Dictionary<string, JsonElement>
                {
                    ["title"] = JsonSerializer.SerializeToElement("Fix login bug"),
                    ["priority"] = JsonSerializer.SerializeToElement("High")
                })
            ]);

        Assert.True(result.ToolNamesMatch);
        Assert.True(result.SchemaPassRateOk);
    }

    [Fact]
    public void Score_FailsWhenArgsInvalid()
    {
        var fixtures = ToolProposalEvalHarness.LoadFixtures(FindSampleTools());
        var create = fixtures.First(f => f.Id == "create-high-priority");

        var result = ToolProposalEvalHarness.Score(
            create,
            [
                ("create_todo", new Dictionary<string, JsonElement>())
            ]);

        Assert.False(result.SchemaPassRateOk);
    }
}

public class LlmJudgeServiceTests
{
    [Fact]
    public void Parse_ReadsRubricScores()
    {
        var result = LlmJudgeService.Parse(
            """{"faithfulness":4,"answer_relevance":5,"rationale":"grounded"}""");

        Assert.Equal(4, result.FaithfulnessScore);
        Assert.Equal(5, result.AnswerRelevanceScore);
        Assert.Equal("grounded", result.Rationale);
    }
}
