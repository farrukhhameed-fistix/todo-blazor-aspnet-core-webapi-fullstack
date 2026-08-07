using Fistix.TaskManager.ViewModel.Commands.Todos;
using Fistix.TaskManager.ViewModel.Validators.Todos;
using Xunit;

namespace Fistix.TaskManager.AiLayer.Tests;

public class AiQueryCommandValidatorTests
{
    private readonly AiQueryCommandValidator _validator = new();

    [Fact]
    public void AcceptsNonEmptyQuestion()
    {
        var result = _validator.Validate(new AiQueryCommand { Question = "What am I working on?" });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void RejectsEmptyQuestion()
    {
        var result = _validator.Validate(new AiQueryCommand { Question = "" });
        Assert.False(result.IsValid);
    }
}
