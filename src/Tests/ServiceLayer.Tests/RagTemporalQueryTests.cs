using Fistix.TaskManager.Core.DomainModel.Aggregates;
using Fistix.TaskManager.ServiceLayer.Todos;

namespace Fistix.TaskManager.ServiceLayer.Tests;

public class RagTemporalQueryTests
{
    private static readonly DateTime Today = new(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("what I have for next week", RagTemporalKind.NextWeek)]
    [InlineData("tasks due next week", RagTemporalKind.NextWeek)]
    [InlineData("what am I working on this week?", RagTemporalKind.ThisWeek)]
    [InlineData("due this week", RagTemporalKind.ThisWeek)]
    [InlineData("what did I have last week", RagTemporalKind.LastWeek)]
    [InlineData("tasks previous week", RagTemporalKind.LastWeek)]
    [InlineData("what I was working in last month", RagTemporalKind.LastMonth)]
    [InlineData("due last month", RagTemporalKind.LastMonth)]
    [InlineData("tasks this month", RagTemporalKind.ThisMonth)]
    [InlineData("what's due next month", RagTemporalKind.NextMonth)]
    [InlineData("due tomorrow", RagTemporalKind.Tomorrow)]
    [InlineData("what was due yesterday", RagTemporalKind.Yesterday)]
    [InlineData("show overdue tasks", RagTemporalKind.Overdue)]
    [InlineData("what is due today?", RagTemporalKind.Today)]
    [InlineData("what is blocking payments?", RagTemporalKind.None)]
    public void Detect_RecognizesTemporalPhrases(string question, RagTemporalKind expected)
    {
        var window = RagTemporalQuery.Detect(question, Today);
        Assert.Equal(expected, window.Kind);
    }

    [Fact]
    public void NextWeek_WhenTodayIsThuAug6_IsMonAug10ToSunAug16()
    {
        // Today = Thu 2026-08-06 → this week Mon 8/3–Sun 8/9 → next Mon 8/10–Sun 8/16
        var window = RagTemporalQuery.Detect("what do I have next week", Today);

        Assert.Equal(new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc), window.StartDate);
        Assert.Equal(new DateTime(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc), window.EndDateExclusive);
    }

    [Fact]
    public void LastWeek_WhenTodayIsThuAug6_IsMonJul27ToSunAug2()
    {
        var window = RagTemporalQuery.Detect("last week", Today);

        Assert.Equal(new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc), window.StartDate);
        Assert.Equal(new DateTime(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc), window.EndDateExclusive);
    }

    [Fact]
    public void LastMonth_WhenTodayIsAug6_IsJulyCalendarMonth()
    {
        var window = RagTemporalQuery.Detect("what I was working in last month", Today);

        Assert.Equal(RagTemporalKind.LastMonth, window.Kind);
        Assert.Equal(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), window.StartDate);
        Assert.Equal(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), window.EndDateExclusive);
    }

    [Fact]
    public void ThisMonth_IsAug2026_WhenTodayIsAug6()
    {
        var window = RagTemporalQuery.Detect("this month", Today);

        Assert.Equal(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), window.StartDate);
        Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), window.EndDateExclusive);
    }

    [Fact]
    public void NextMonth_IsSep2026_WhenTodayIsAug6()
    {
        var window = RagTemporalQuery.Detect("next month", Today);

        Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), window.StartDate);
        Assert.Equal(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), window.EndDateExclusive);
    }

    [Fact]
    public void Tomorrow_And_Yesterday_AreSingleDays()
    {
        var tomorrow = RagTemporalQuery.Detect("due tomorrow", Today);
        var yesterday = RagTemporalQuery.Detect("yesterday", Today);

        Assert.Equal(Today.AddDays(1), tomorrow.StartDate);
        Assert.Equal(Today.AddDays(2), tomorrow.EndDateExclusive);
        Assert.Equal(Today.AddDays(-1), yesterday.StartDate);
        Assert.Equal(Today, yesterday.EndDateExclusive);
    }

    [Fact]
    public void ThisWeekWindow_WhenTodayIsThuAug6_IsMonAug3ToSunAug9()
    {
        var window = RagTemporalQuery.ThisWeekWindow(Today);

        Assert.Equal(RagTemporalKind.ThisWeek, window.Kind);
        Assert.Equal(new DateTime(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc), window.StartDate);
        Assert.Equal(new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc), window.EndDateExclusive);
    }

    [Fact]
    public void Matches_LastWeek_IncludesJul27_ExcludesJul26AndAug3()
    {
        var window = RagTemporalQuery.Detect("last week", Today);
        var before = MakeTodo("Sat", new DateTime(2026, 7, 26, 0, 0, 0, DateTimeKind.Utc), "Pending");
        var mon = MakeTodo("Mon", new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc), "Pending");
        var sun = MakeTodo("Sun", new DateTime(2026, 8, 2, 0, 0, 0, DateTimeKind.Utc), "Pending");
        var thisMon = MakeTodo("ThisMon", new DateTime(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc), "Pending");

        Assert.False(RagTemporalQuery.Matches(before, window));
        Assert.True(RagTemporalQuery.Matches(mon, window));
        Assert.True(RagTemporalQuery.Matches(sun, window));
        Assert.False(RagTemporalQuery.Matches(thisMon, window));
    }

    [Fact]
    public void Matches_ExcludesJulyTaskFromNextWeek()
    {
        var window = RagTemporalQuery.Detect("what I have for next week", Today);
        var julyTask = MakeTodo(
            "CEO wants dark mode by Friday",
            new DateTime(2026, 7, 25, 0, 0, 0, DateTimeKind.Utc),
            "Pending");
        // Aug 16 = Sunday of next ISO week (Mon 8/10–Sun 8/16)
        var inWindow = MakeTodo("Ship sprint plan", new DateTime(2026, 8, 16, 0, 0, 0, DateTimeKind.Utc), "Pending", "High");

        Assert.False(RagTemporalQuery.Matches(julyTask, window));
        Assert.True(RagTemporalQuery.Matches(inWindow, window));
    }

    [Fact]
    public void Matches_LastMonth_IncludesJuly_ExcludesJuneAndAugust()
    {
        var window = RagTemporalQuery.Detect("last month", Today);
        var june = MakeTodo("June", new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), "Pending");
        var july = MakeTodo("July", new DateTime(2026, 7, 30, 0, 0, 0, DateTimeKind.Utc), "Pending");
        var aug1 = MakeTodo("Aug1", new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), "Pending");

        Assert.False(RagTemporalQuery.Matches(june, window));
        Assert.True(RagTemporalQuery.Matches(july, window));
        Assert.False(RagTemporalQuery.Matches(aug1, window));
    }

    [Fact]
    public void BuildDeterministicAnswer_ListsAllMatchingTasks()
    {
        var window = RagTemporalQuery.Detect("tasks due next week", Today);
        var taskA = MakeTodo("Task A", new DateTime(2026, 8, 11, 0, 0, 0, DateTimeKind.Utc), "Pending", "High");
        var taskB = MakeTodo("Task B", new DateTime(2026, 8, 14, 0, 0, 0, DateTimeKind.Utc), "Pending", "Low");
        var tasks = new List<TodoTask> { taskA, taskB };

        var answer = RagTemporalQuery.BuildDeterministicAnswer(tasks, window);

        Assert.Contains("Task A", answer, StringComparison.Ordinal);
        Assert.Contains("Task B", answer, StringComparison.Ordinal);
        Assert.Contains(taskA.ExternalId.ToString(), answer, StringComparison.Ordinal);
        Assert.Contains(taskB.ExternalId.ToString(), answer, StringComparison.Ordinal);
        Assert.Contains("(2)", answer, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildDeterministicAnswer_EmptyWindowMessage()
    {
        var window = RagTemporalQuery.Detect("next week", Today);
        var answer = RagTemporalQuery.BuildDeterministicAnswer([], window);

        Assert.Contains("No tasks due", answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("next week", answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Overdue_DateMatchIsIndependentOfStatus_CompletedFlaggedSeparately()
    {
        var window = RagTemporalQuery.Detect("overdue", Today);
        var overduePending = MakeTodo("Late", Today.AddDays(-3), "Pending");
        var overdueCompleted = MakeTodo("Done late", Today.AddDays(-3), "Completed");

        Assert.True(RagTemporalQuery.Matches(overduePending, window));
        Assert.True(RagTemporalQuery.Matches(overdueCompleted, window));
        Assert.True(RagTemporalQuery.ShouldExcludeCompleted("overdue"));
        Assert.True(RagTemporalQuery.IsCompleted(overdueCompleted));
        Assert.False(RagTemporalQuery.IsCompleted(overduePending));
    }

    [Theory]
    [InlineData("what I have for next week", true)]
    [InlineData("what I was working in last month", true)]
    [InlineData("tasks due last month", true)]
    [InlineData("show overdue tasks", true)]
    [InlineData("was there any critical or high priority task for me in last month?", false)]
    [InlineData("high priority tasks last week", false)]
    [InlineData("summarize last month", false)]
    [InlineData("which tasks last month were blocking?", false)]
    [InlineData("how many tasks last week?", false)]
    public void IsPlainListQuestion_DistinguishesListFromAnalytical(string question, bool expectedPlainList)
    {
        Assert.Equal(expectedPlainList, RagTemporalQuery.IsPlainListQuestion(question));
    }

    private static TodoTask MakeTodo(
        string title,
        DateTime dueDate,
        string status,
        string priority = "Medium")
    {
        var todo = new TodoTask
        {
            Title = title,
            DueDate = dueDate,
            Status = status,
            Priority = priority
        };
        todo.GenerateNewExternalId();
        return todo;
    }
}
