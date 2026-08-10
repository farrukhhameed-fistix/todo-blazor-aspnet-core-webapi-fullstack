using Fistix.TaskManager.Core.DomainModel.Aggregates;
using Fistix.TaskManager.ServiceLayer.Todos;
using Fistix.TaskManager.ViewModel.Commands.Todos;

namespace Fistix.TaskManager.ServiceLayer.Tests;

public class RagQueryIntentTests
{
    [Fact]
    public void Parse_HighPriorityAuth0LastMonth_StripsFilters_KeepsTopic()
    {
        var intent = RagQueryIntent.Parse("high priority Auth0 last month?");

        Assert.Equal("High", intent.PriorityFilter);
        Assert.True(intent.ExcludeCompleted);
        Assert.False(intent.IsAdviceQuestion);
        Assert.Contains("Auth0", intent.SearchQuery, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("last month", intent.SearchQuery, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("high priority", intent.SearchQuery, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_StripeWebhookTimeouts_KeepsTopic()
    {
        var intent = RagQueryIntent.Parse("Which tasks are about Stripe webhook timeouts?");

        Assert.Null(intent.PriorityFilter);
        Assert.Contains("Stripe", intent.SearchQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("webhook", intent.SearchQuery, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Which tasks", intent.SearchQuery, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_JwtLoginOutage_KeepsTopic()
    {
        var intent = RagQueryIntent.Parse("Tasks about JWT / login outage");

        Assert.Contains("JWT", intent.SearchQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("login", intent.SearchQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("outage", intent.SearchQuery, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_AdviceAuthPayments_MarksAdvice_AndStripsLeadIn()
    {
        var intent = RagQueryIntent.Parse("What should I work on for Auth and Payments next?");

        Assert.True(intent.IsAdviceQuestion);
        Assert.Contains("Auth", intent.SearchQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Payments", intent.SearchQuery, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("What should", intent.SearchQuery, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_CompletedExplicit_DoesNotExcludeCompleted()
    {
        var intent = RagQueryIntent.Parse("show completed Auth0 tasks last month");

        Assert.False(intent.ExcludeCompleted);
        Assert.Equal("Auth0", intent.SearchQuery.Trim(), ignoreCase: true);
    }

    [Theory]
    [InlineData("critical payments work", "High")]
    [InlineData("medium priority refunds", "Medium")]
    [InlineData("low priority docs", "Low")]
    public void Parse_DetectsPriority(string question, string expected)
    {
        var intent = RagQueryIntent.Parse(question);
        Assert.Equal(expected, intent.PriorityFilter);
    }

    [Fact]
    public void MatchesTodo_AppliesPriorityAndCompleted()
    {
        var intent = RagQueryIntent.Parse("high priority Auth0");
        Assert.True(intent.MatchesSource("High", "Pending"));
        Assert.False(intent.MatchesSource("Medium", "Pending"));
        Assert.False(intent.MatchesSource("High", "Completed"));
    }

    [Fact]
    public void SelectCitedSources_PrefersAnswerGuids()
    {
        var a = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var b = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var retrieval = new List<AiQuerySourceDto>
        {
            new() { ExternalId = a, Title = "A" },
            new() { ExternalId = b, Title = "B" }
        };

        var selected = AiQueryCommandHandler.SelectCitedSources(
            retrieval,
            $"Only [{a}] matters.");

        Assert.Single(selected);
        Assert.Equal(a, selected[0].ExternalId);
    }

    [Fact]
    public void TopicSearchQueries_AdviceSplitsAuthAndPayments()
    {
        var intent = RagQueryIntent.Parse("What should I work on for Auth and Payments next?");
        var topics = intent.TopicSearchQueries();

        Assert.Equal(2, topics.Count);
        Assert.Contains(topics, t => t.Contains("Auth", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(topics, t => t.Contains("Payments", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void IsMetaPlanningTask_DetectsSprintCapacityCollector()
    {
        Assert.True(RagQueryIntent.IsMetaPlanningTask(
            "Sprint capacity for Q3 planning",
            "Collect High/Medium incomplete Auth and Payments work for optimizer demo."));
        Assert.False(RagQueryIntent.IsMetaPlanningTask(
            "Stripe webhook timeouts under load",
            "Payment confirmation webhooks timing out."));
    }

    [Fact]
    public void ShouldUseGlobalAdviceFallback_OpenEndedWhatNext()
    {
        var open = RagQueryIntent.Parse("What should I work next?");
        Assert.True(open.IsAdviceQuestion);
        Assert.True(open.ShouldUseGlobalAdviceFallback());
        Assert.True(RagQueryIntent.IsWeakAdviceTopic("work"));

        var scoped = RagQueryIntent.Parse("What should I work on for Auth and Payments next?");
        Assert.True(scoped.IsAdviceQuestion);
        Assert.False(scoped.ShouldUseGlobalAdviceFallback());
    }

    [Fact]
    public void MergeTopicsFairly_KeepsQuotaFromEachDomain()
    {
        TodoTask Make(string title, string priority, DateTime due)
        {
            var t = new TodoTask
            {
                Title = title,
                Priority = priority,
                Status = "Pending",
                DueDate = due
            };
            t.GenerateNewExternalId();
            return t;
        }

        var auth = new List<TodoTask>
        {
            Make("Auth0 login loop", "High", new DateTime(2026, 7, 19, 0, 0, 0, DateTimeKind.Utc)),
            Make("Login failures Auth0", "High", new DateTime(2026, 7, 19, 0, 0, 0, DateTimeKind.Utc)),
            Make("Prod auth outage", "High", new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc)),
            Make("Token refresh", "High", new DateTime(2026, 7, 26, 0, 0, 0, DateTimeKind.Utc)),
        };
        var payments = new List<TodoTask>
        {
            Make("Stripe webhook timeouts under load", "High", new DateTime(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc)),
            Make("Fix payment webhook timeout", "High", new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc)),
        };

        // Without fair merge, taking 4 globally would be Auth-only (earlier dues).
        var merged = RagQueryIntent.MergeTopicsFairly(new IReadOnlyList<TodoTask>[] { auth, payments }, totalLimit: 4);

        Assert.Equal(4, merged.Count);
        Assert.Contains(merged, t => t.Title!.Contains("Auth0", StringComparison.OrdinalIgnoreCase)
                                     || t.Title.Contains("auth", StringComparison.OrdinalIgnoreCase)
                                     || t.Title.Contains("Login", StringComparison.OrdinalIgnoreCase)
                                     || t.Title.Contains("Token", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(merged, t => t.Title!.Contains("Stripe", StringComparison.OrdinalIgnoreCase)
                                     || t.Title.Contains("payment", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildDeterministicAdviceAnswer_KeepsOverdueHighInFocus()
    {
        var overdue = new TodoTask
        {
            Title = "Login failures after Auth0 tenant change",
            Priority = "High",
            Status = "Pending",
            DueDate = new DateTime(2026, 7, 19, 0, 0, 0, DateTimeKind.Utc)
        };
        overdue.GenerateNewExternalId();

        var payment = new TodoTask
        {
            Title = "Fix payment webhook timeout",
            Priority = "High",
            Status = "Pending",
            DueDate = new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc)
        };
        payment.GenerateNewExternalId();

        var today = new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc);
        var ordered = RagQueryIntent.OrderForRag(new[] { payment, overdue });
        var answer = RagQueryIntent.BuildDeterministicAdviceAnswer(ordered, today);

        Assert.Contains("overdue", answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Login failures", answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no longer be in focus", answer, StringComparison.OrdinalIgnoreCase);
        var lines = answer.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Contains(lines, l => l.StartsWith("1. Login failures", StringComparison.Ordinal));
    }
}
