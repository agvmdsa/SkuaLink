using AqwCompanion.App.Characters;
using AqwCompanion.App.Queue;
using AqwCompanion.Phone.Characters;
using AqwCompanion.Phone.Queue;
using AqwCompanion.Skua;
using AqwCompanion.Tests.TestDoubles;
using static AqwCompanion.Tests.Queue.Kit;

namespace AqwCompanion.Tests.Queue;

public class QueueViewModelTests
{
    private sealed class Rig
    {
        public FakeQueues Queues { get; } = new();
        public FakeSkua Skua { get; } = new();
        public CharacterBook Book { get; } = new(new MemoryCharacterBookStore(), () => DateTimeOffset.UtcNow);
        public CharacterSelection Selection { get; }
        public FakeQueueEditorDialog Dialog { get; } = new();
        public List<Action> Posted { get; } = [];

        public Rig()
        {
            Selection = new CharacterSelection(Book, Skua);
        }

        public QueueViewModel Build(bool inline = true) =>
            new(Queues, Selection, Dialog, inline ? a => a() : Posted.Add);

        public void Know(string name) =>
            Book.OnStatus(new SkuaStatus(true, name, null, "map", null, false, null, null, null, "idle", null, false));
    }

    private static QueueView View(
        string character, QueueState state, int cursor = 0, QueuePhase? phase = null, string? reason = null, int? have = null,
        bool canEdit = true, params QueueStep[] steps) =>
        new(character, steps.Length == 0 ? [Step("1")] : steps, cursor, state, phase, reason, have, canEdit);

    [Fact]
    public void Without_a_character_nothing_can_be_done_and_the_empty_text_asks_for_one()
    {
        var rig = new Rig();
        var vm = rig.Build();

        Assert.False(vm.HasCharacter);
        Assert.False(vm.CanStart || vm.CanPause || vm.CanContinue || vm.CanSkip || vm.CanStop || vm.CanEdit);
        Assert.Equal(QueueViewModel.NoCharacterText, vm.EmptyStateText);
        Assert.False(vm.HasSteps);
        vm.EditCommand.Execute(null);
        Assert.Empty(rig.Dialog.Shown);
    }

    [Fact]
    public void The_selected_characters_queue_is_listed_with_its_current_step_and_progress()
    {
        var rig = new Rig();
        rig.Know(Hero);
        var steps = new[]
        {
            Step("1", item: "Blood Orb", quantity: 100, rep: new ReputationGoal("Ebilcorp", 10), minutes: 30),
            Step("2", B, "Butler", "Gold", 5),
        };
        rig.Queues.Items.Add(View(Hero, QueueState.Running, 0, QueuePhase.Running, null, 37, true, steps));
        var vm = rig.Build();

        Assert.True(vm.HasCharacter);
        Assert.Equal(Hero, vm.Character);
        Assert.Equal("1. Dailies: Blood Orb x100 / Ebilcorp rank 10 / limit 30 min", vm.Steps[0].Text);
        Assert.Equal("2. Butler: Gold x5", vm.Steps[1].Text);
        Assert.True(vm.Steps[0].IsCurrent);
        Assert.Equal(">", vm.Steps[0].Marker);
        Assert.False(vm.Steps[1].IsCurrent);
        Assert.Equal(string.Empty, vm.Steps[1].Marker);
        Assert.Equal("37 of 100 Blood Orb", vm.ProgressText);
        Assert.Equal("Running: watching the script", vm.StateText);
        Assert.True(vm.HasSteps);
        Assert.Equal(string.Empty, vm.EmptyStateText);
    }

    [Fact]
    public void A_character_without_steps_shows_the_empty_steps_text()
    {
        var rig = new Rig();
        rig.Know(Hero);
        var vm = rig.Build();

        Assert.Equal(QueueViewModel.EmptyText, vm.EmptyStateText);
        Assert.Equal(QueueViewModel.StoppedText, vm.StateText);
        Assert.Equal(string.Empty, vm.ProgressText);
        Assert.False(vm.CanStart);
        Assert.True(vm.CanEdit);
    }

    [Theory]
    [InlineData(QueueState.Stopped, null, true, "Start,Edit")]
    [InlineData(QueueState.Stopped, null, false, "Edit")]
    [InlineData(QueueState.Running, QueuePhase.Running, true, "Pause,Skip,Stop,Edit")]
    [InlineData(QueueState.Running, QueuePhase.Starting, true, "Pause,Skip,Stop")]
    [InlineData(QueueState.Running, QueuePhase.Stopping, true, "Pause,Skip,Stop")]
    [InlineData(QueueState.Running, QueuePhase.Settling, true, "Pause,Skip,Stop")]
    [InlineData(QueueState.Paused, null, true, "Continue,Skip,Stop,Edit")]
    [InlineData(QueueState.Finished, null, true, "Start,Edit")]
    public void The_buttons_follow_the_state(QueueState state, QueuePhase? phase, bool withSteps, string expected)
    {
        var rig = new Rig();
        rig.Know(Hero);
        var canEdit = phase is null or QueuePhase.Running;
        rig.Queues.Items.Add(withSteps
            ? View(Hero, state, 0, phase, null, null, canEdit)
            : new QueueView(Hero, [], 0, state, phase, null, null, canEdit));
        var vm = rig.Build();

        var on = new List<string>();
        if (vm.CanStart) { on.Add("Start"); }
        if (vm.CanPause) { on.Add("Pause"); }
        if (vm.CanContinue) { on.Add("Continue"); }
        if (vm.CanSkip) { on.Add("Skip"); }
        if (vm.CanStop) { on.Add("Stop"); }
        if (vm.CanEdit) { on.Add("Edit"); }

        Assert.Equal(expected, string.Join(",", on));
        Assert.Equal(vm.CanStart, vm.StartCommand.CanExecute(null));
        Assert.Equal(vm.CanEdit, vm.EditCommand.CanExecute(null));
    }

    [Fact]
    public void A_paused_queue_shows_its_reason_and_a_finished_one_says_so()
    {
        var rig = new Rig();
        rig.Know(Hero);
        rig.Queues.Items.Add(View(Hero, QueueState.Paused, reason: "Script not found."));
        var vm = rig.Build();

        Assert.Equal("Paused", vm.StateText);
        Assert.Equal("Script not found.", vm.ReasonText);
        Assert.True(vm.HasReason);

        rig.Queues.Items[0] = View(Hero, QueueState.Finished, 1);
        rig.Queues.Raise();

        Assert.Equal("Finished", vm.StateText);
        Assert.False(vm.HasReason);
        Assert.False(vm.Steps[0].IsCurrent);
    }

    [Theory]
    [InlineData(QueuePhase.Starting, "Running: starting the script")]
    [InlineData(QueuePhase.Stopping, "Running: stopping the script")]
    [InlineData(QueuePhase.Settling, "Running: waiting for Skua to settle")]
    public void The_phase_is_in_words(QueuePhase phase, string expected)
    {
        var rig = new Rig();
        rig.Know(Hero);
        rig.Queues.Items.Add(View(Hero, QueueState.Running, 0, phase));

        Assert.Equal(expected, rig.Build().StateText);
    }

    [Fact]
    public void Each_command_goes_to_the_queues_for_the_character_as_the_book_spells_it()
    {
        var rig = new Rig();
        rig.Know("Fictional Hero");
        rig.Queues.Items.Add(View("fictional hero", QueueState.Stopped));
        var vm = rig.Build();

        vm.StartCommand.Execute(null);
        vm.PauseCommand.Execute(null);
        vm.ContinueCommand.Execute(null);
        vm.SkipCommand.Execute(null);
        vm.StopCommand.Execute(null);

        Assert.Equal(
            [QueueCommand.Start, QueueCommand.Pause, QueueCommand.Continue, QueueCommand.Skip, QueueCommand.Stop],
            rig.Queues.Commands.Select(c => c.Command));
        Assert.All(rig.Queues.Commands, c => Assert.Equal("Fictional Hero", c.Character));
    }

    [Fact]
    public void A_refusal_is_shown_and_cleared_by_the_next_success()
    {
        var rig = new Rig();
        rig.Know(Hero);
        rig.Queues.Items.Add(View(Hero, QueueState.Stopped));
        var vm = rig.Build();

        rig.Queues.CommandResult = QueueResult.Refused("The queue is paused. Press Continue.");
        vm.StartCommand.Execute(null);
        Assert.Equal("The queue is paused. Press Continue.", vm.ErrorText);
        Assert.True(vm.HasError);

        rig.Queues.CommandResult = QueueResult.Done;
        vm.StartCommand.Execute(null);
        Assert.Equal(string.Empty, vm.ErrorText);
        Assert.False(vm.HasError);
    }

    [Fact]
    public void Edit_opens_the_editor_for_the_character_only_when_editing_is_allowed()
    {
        var rig = new Rig();
        rig.Know(Hero);
        rig.Queues.Items.Add(View(Hero, QueueState.Running, 0, QueuePhase.Starting, canEdit: false));
        var vm = rig.Build();

        vm.EditCommand.Execute(null);
        Assert.Empty(rig.Dialog.Shown);

        rig.Queues.Items[0] = View(Hero, QueueState.Stopped);
        rig.Queues.Raise();
        vm.EditCommand.Execute(null);
        Assert.Equal(Hero, Assert.Single(rig.Dialog.Shown));
    }

    [Fact]
    public void A_change_of_the_queues_the_selection_or_the_book_refreshes_through_the_ui_thread()
    {
        var rig = new Rig();
        rig.Know(Hero);
        var vm = rig.Build(inline: false);
        var seen = new List<string?>();
        vm.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        rig.Queues.Items.Add(View(Hero, QueueState.Paused, reason: "why"));
        rig.Queues.Raise();
        rig.Queues.Raise();

        Assert.Empty(seen);
        Assert.Single(rig.Posted);
        rig.Posted[0]();
        Assert.Equal("Paused", vm.StateText);
        Assert.NotEmpty(seen);

        rig.Posted.Clear();
        rig.Know("Other Hero");
        rig.Selection.Choose("Other Hero");
        Assert.NotEmpty(rig.Posted);
        rig.Posted[0]();
        Assert.Equal("Other Hero", vm.Character);
    }

    [Fact]
    public void The_queue_of_another_character_does_not_leak_in_when_the_selection_changes()
    {
        var rig = new Rig();
        rig.Know(Hero);
        rig.Know("Other Hero");
        rig.Queues.Items.Add(View(Hero, QueueState.Paused, reason: "mine"));
        rig.Selection.Choose(Hero);
        var vm = rig.Build();
        Assert.Equal("Paused", vm.StateText);

        rig.Selection.Choose("Other Hero");

        Assert.Equal("Other Hero", vm.Character);
        Assert.Equal(QueueViewModel.StoppedText, vm.StateText);
        Assert.False(vm.HasReason);
        Assert.False(vm.HasSteps);
    }

    [Fact]
    public void Hostile_text_is_shown_as_the_engine_gave_it_and_never_throws()
    {
        var rig = new Rig();
        rig.Know(Hero);
        var name = new string('x', 200);
        rig.Queues.Items.Add(View(Hero, QueueState.Running, 0, QueuePhase.Running, "bad\u0001reason", 3, true, Step("1", name: name, item: "Item\u0007 " + name)));
        var vm = rig.Build();

        Assert.Contains(name, vm.Steps[0].Text);
        Assert.Contains("3 of 10", vm.ProgressText);
        Assert.Equal("bad\u0001reason", vm.ReasonText);
    }

    [Fact]
    public void After_dispose_a_change_of_the_queues_no_longer_refreshes()
    {
        var rig = new Rig();
        rig.Know(Hero);
        var vm = rig.Build();
        vm.Dispose();

        rig.Queues.Items.Add(View(Hero, QueueState.Paused, reason: "why"));
        rig.Queues.Raise();

        Assert.Equal(QueueViewModel.StoppedText, vm.StateText);
    }
}
