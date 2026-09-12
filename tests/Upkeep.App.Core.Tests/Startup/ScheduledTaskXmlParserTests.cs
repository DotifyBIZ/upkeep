using Upkeep.App.Core.Startup;

namespace Upkeep.App.Core.Tests.Startup;

public class ScheduledTaskXmlParserTests
{
    private const string TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    private static string Document(params string[] tasks) =>
        $"<Tasks>{string.Join(string.Empty, tasks)}</Tasks>";

    private static string Task(
        string uri,
        string trigger = "<LogonTrigger><Enabled>true</Enabled></LogonTrigger>",
        string? enabled = null,
        string author = "Google LLC",
        string command = @"C:\Program Files\Google\Update.exe") =>
        $"""
        <Task xmlns="{TaskNamespace}">
          <RegistrationInfo><Author>{author}</Author><URI>{uri}</URI></RegistrationInfo>
          <Triggers>{trigger}</Triggers>
          <Settings>{(enabled is null ? string.Empty : $"<Enabled>{enabled}</Enabled>")}</Settings>
          <Actions><Exec><Command>{command}</Command></Exec></Actions>
        </Task>
        """;

    [Fact]
    public void Parse_LogonTask_IsListedWithItsDetails()
    {
        var entries = ScheduledTaskXmlParser.Parse(Document(Task(@"\GoogleUpdateTaskMachineCore")));

        var entry = Assert.Single(entries);
        Assert.Equal(@"\GoogleUpdateTaskMachineCore", entry.Path);
        Assert.Equal("Google LLC", entry.Author);
        Assert.EndsWith("Update.exe", entry.Command, StringComparison.Ordinal);
        Assert.True(entry.RunsAtLogon);
        Assert.True(entry.IsEnabled);
    }

    [Fact]
    public void Parse_TaskWithoutALogonTrigger_IsNotAStartupItem()
    {
        string xml = Document(Task(@"\NightlyBackup", trigger: "<CalendarTrigger><Enabled>true</Enabled></CalendarTrigger>"));

        Assert.Empty(ScheduledTaskXmlParser.Parse(xml));
    }

    [Fact]
    public void Parse_WindowsOwnTasks_AreNeverListed()
    {
        // Disabling a Windows maintenance task from a cleanup tool is a support call waiting to
        // happen.
        string xml = Document(Task(@"\Microsoft\Windows\Defrag\ScheduledDefrag"));

        Assert.Empty(ScheduledTaskXmlParser.Parse(xml));
    }

    [Fact]
    public void Parse_DisabledTask_IsListedAsDisabled()
    {
        var entries = ScheduledTaskXmlParser.Parse(Document(Task(@"\VendorUpdater", enabled: "false")));

        Assert.False(Assert.Single(entries).IsEnabled);
    }

    [Fact]
    public void Parse_TaskWithNoEnabledElement_CountsAsEnabled()
    {
        // The schema's default, and what Task Scheduler shows.
        var entries = ScheduledTaskXmlParser.Parse(Document(Task(@"\VendorUpdater")));

        Assert.True(Assert.Single(entries).IsEnabled);
    }

    [Fact]
    public void Parse_SeveralTasks_ReturnsEachOne()
    {
        string xml = Document(
            Task(@"\FirstUpdater"),
            Task(@"\SecondUpdater"),
            Task(@"\Microsoft\Windows\Something"),
            Task(@"\Nightly", trigger: "<CalendarTrigger />"));

        var entries = ScheduledTaskXmlParser.Parse(xml);

        Assert.Equal([@"\FirstUpdater", @"\SecondUpdater"], entries.Select(entry => entry.Path));
    }

    [Fact]
    public void Parse_TaskWithNoUri_IsSkipped()
    {
        // Without a path there is nothing to toggle later, so the row would be a dead end.
        string xml = $"""<Tasks><Task xmlns="{TaskNamespace}"><Triggers><LogonTrigger /></Triggers></Task></Tasks>""";

        Assert.Empty(ScheduledTaskXmlParser.Parse(xml));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("this is not xml at all <<<")]
    public void Parse_UnusableOutput_IsEmptyRatherThanAnException(string? xml)
    {
        // This is the output of an external tool; a startup page that fails to load because one
        // task is odd is worse than one that lists the rest.
        Assert.Empty(ScheduledTaskXmlParser.Parse(xml));
    }

    [Fact]
    public void IsWindowsOwned_RecognizesTheMicrosoftFolder()
    {
        Assert.True(ScheduledTaskXmlParser.IsWindowsOwned(@"\Microsoft\Windows\Defrag\ScheduledDefrag"));
        Assert.False(ScheduledTaskXmlParser.IsWindowsOwned(@"\GoogleUpdateTaskMachineCore"));
    }
}
