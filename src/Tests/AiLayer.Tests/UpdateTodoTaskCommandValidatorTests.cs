using Fistix.TaskManager.ViewModel.Commands.Todos;
using Fistix.TaskManager.ViewModel.Validators.Todos;
using FluentValidation.TestHelper;

namespace Fistix.TaskManager.AiLayer.Tests;

public class UpdateTodoTaskCommandValidatorTests
{
    private readonly UpdateTodoTaskCommandValidator _validator = new();

    [Fact]
    public void Should_fail_when_description_is_empty()
    {
        var command = new UpdateTodoTaskCommand
        {
            ExternalId = Guid.NewGuid(),
            Title = "Valid title",
            Description = "",
            DueDate = DateTime.UtcNow.AddDays(1)
        };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Description);
    }

    [Fact]
    public void Should_fail_when_description_exceeds_max_length()
    {
        var command = new UpdateTodoTaskCommand
        {
            ExternalId = Guid.NewGuid(),
            Title = "Valid title",
            Description = new string('d', TodoFieldLimits.DescriptionMaxLength + 1),
            DueDate = DateTime.UtcNow.AddDays(1)
        };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Description);
    }

    [Fact]
    public void Should_fail_when_priority_is_invalid()
    {
        var command = new UpdateTodoTaskCommand
        {
            ExternalId = Guid.NewGuid(),
            Title = "Valid title",
            Description = "Valid description",
            DueDate = DateTime.UtcNow.AddDays(1),
            Priority = "Urgent"
        };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Priority);
    }

    [Fact]
    public void Should_pass_when_priority_is_valid()
    {
        var command = new UpdateTodoTaskCommand
        {
            ExternalId = Guid.NewGuid(),
            Title = "Valid title",
            Description = "Valid description",
            DueDate = DateTime.UtcNow.AddDays(1),
            Priority = "High"
        };

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveValidationErrorFor(x => x.Priority);
    }

    [Fact]
    public void Should_allow_past_due_date_on_update()
    {
        var command = new UpdateTodoTaskCommand
        {
            ExternalId = Guid.NewGuid(),
            Title = "Valid title",
            Description = "Valid description",
            DueDate = DateTime.UtcNow.AddDays(-3),
            Priority = "Medium"
        };

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveValidationErrorFor(x => x.DueDate);
    }
}
