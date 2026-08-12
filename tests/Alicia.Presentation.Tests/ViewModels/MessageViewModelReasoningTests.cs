using Alicia.Domain.Conversations;
using Alicia.Presentation.ViewModels;

namespace Alicia.Presentation.Tests.ViewModels;

public sealed class MessageViewModelReasoningTests
{
    [Fact]
    public void StreamingAssistantStartsWaitingAndSegmentsReasoningOnBlankLines()
    {
        MessageViewModel viewModel = MessageViewModel.CreateStreamingAssistant();

        Assert.True(viewModel.IsWaitingForFirstDelta);
        Assert.True(viewModel.IsReasoningExpanded);
        Assert.False(viewModel.HasReasoning);
        Assert.False(viewModel.HasContent);
        Assert.Equal("Alicia réfléchit.", viewModel.ThinkingIndicatorText);

        viewModel.AppendReasoningDelta("First step\n\nSecond step");

        Assert.False(viewModel.IsWaitingForFirstDelta);
        Assert.True(viewModel.HasReasoning);
        Assert.False(viewModel.HasContent);
        Assert.Collection(
            viewModel.ReasoningSteps,
            step => Assert.Equal("First step", step.Content),
            step => Assert.Equal("Second step", step.Content));
    }

    [Fact]
    public void StreamingAssistantAcceptsReasoningThenVisibleContent()
    {
        MessageViewModel viewModel = MessageViewModel.CreateStreamingAssistant();

        viewModel.AppendReasoningDelta("Plan");
        viewModel.AppendContentDelta("Final answer");

        Assert.Equal("Final answer", viewModel.Content);
        Assert.True(viewModel.HasContent);
        Assert.Collection(
            viewModel.CaptureReasoningSteps(),
            step => Assert.Equal("Plan", step));
    }

    [Fact]
    public void PersistedAssistantCanReceiveCollapsedEphemeralReasoningSnapshot()
    {
        ChatMessage message = new(
            MessageId.New(),
            MessageRole.Assistant,
            "Visible answer",
            new DateTimeOffset(2026, 8, 13, 0, 45, 0, TimeSpan.Zero));
        MessageViewModel viewModel = new(message);

        viewModel.SetReasoningSnapshot(["Step one", "Step two"]);

        Assert.True(viewModel.HasReasoning);
        Assert.False(viewModel.IsReasoningExpanded);
        Assert.Collection(
            viewModel.ReasoningSteps,
            step => Assert.Equal("Step one", step.Content),
            step => Assert.Equal("Step two", step.Content));
    }

    [Fact]
    public void ThinkingIndicatorCanOnlyChangeWhileWaitingForFirstDelta()
    {
        MessageViewModel viewModel = MessageViewModel.CreateStreamingAssistant();

        viewModel.SetThinkingIndicatorText("Alicia réfléchit...");
        Assert.Equal("Alicia réfléchit...", viewModel.ThinkingIndicatorText);

        viewModel.AppendContentDelta("Answer");
        viewModel.SetThinkingIndicatorText("Alicia réfléchit.");

        Assert.Equal("Alicia réfléchit...", viewModel.ThinkingIndicatorText);
    }
}
