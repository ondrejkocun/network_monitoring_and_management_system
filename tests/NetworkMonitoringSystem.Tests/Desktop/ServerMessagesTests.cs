namespace NetworkMonitoringSystem.Tests.Desktop;

using NetworkMonitoringSystem.Desktop.Services;
using NetworkMonitoringSystem.Domain.Commands;
using NetworkMonitoringSystem.Domain.Monitoring;

public class ServerMessagesTests
{
    [Theory]
    [InlineData("You do not have permission for this operation.", "Na túto operáciu nemáte oprávnenie.")]
    [InlineData("The device is not online.", "Zariadenie nie je online.")]
    [InlineData("Device name must not be empty. (Parameter 'name')", "Názov zariadenia nesmie byť prázdny.")]
    [InlineData("A device with IP address '192.0.2.1' already exists.", "Zariadenie s IP adresou 192.0.2.1 už existuje.")]
    [InlineData("The password must have 8 to 128 characters.", "Heslo musí mať 8 až 128 znakov.")]
    public void KnownMessage_IsShownInSlovak(string english, string slovak)
    {
        Assert.Equal(slovak, ServerMessages.ToSlovak(english));
    }

    [Fact]
    public void UnknownMessage_IsShownAsTheServerWroteIt()
    {
        Assert.Equal("Something new went wrong.", ServerMessages.ToSlovak("Something new went wrong."));
        Assert.Equal(string.Empty, ServerMessages.ToSlovak(null));
    }

    /// <summary>The real messages of the rules, so a reworded one is noticed here and not by the user.</summary>
    [Fact]
    public void MessagesOfTheRules_AreTranslated()
    {
        var settings = new MonitoringSettings(60, 3, 30, 10);
        var failures = new Action[]
        {
            () => settings.ChangeSyncInterval(1),
            () => settings.ChangeOfflineThreshold(0),
            () => settings.ChangeRetention(0),
            () => FileTransfer.NormalizeFileName("a/b.txt"),
            () => FileTransfer.NormalizeFileName("notes."),
            () => FileTransfer.NormalizeFileName("CON.txt"),
        };

        foreach (var failure in failures)
        {
            var message = Assert.ThrowsAny<ArgumentException>(failure).Message;

            Assert.NotEqual(message, ServerMessages.ToSlovak(message));
            Assert.DoesNotContain("Parameter", ServerMessages.ToSlovak(message));
        }
    }
}
