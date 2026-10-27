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

    [Theory]
    [InlineData("Device 'ONDREJPC' opened UDP port 5353 on 0.0.0.0 (chrome).", "Zariadenie „ONDREJPC“ otvorilo UDP port 5353 na 0.0.0.0 (chrome).")]
    [InlineData("Device 'Router izba' is offline.", "Zariadenie „Router izba“ je offline.")]
    [InlineData("User 'admin' signed in.", "Používateľ „admin“ sa prihlásil.")]
    [InlineData("User 'admin' sent the command 'Ping' (8.8.8.8) to device 'PC'.", "Používateľ „admin“ poslal príkaz „Ping zo zariadenia“ (8.8.8.8) na zariadenie „PC“.")]
    [InlineData("The command 'RestartDevice' sent by 'admin' failed on device 'PC'.", "Príkaz „Reštart zariadenia“, ktorý poslal „admin“, na zariadení „PC“ zlyhal.")]
    [InlineData("User 'admin' allowed 'Ping' on all devices to role 'Operátor'.", "Používateľ „admin“ povolil Ping na všetkých zariadeniach role „Operátor“.")]
    [InlineData("Synchronization interval changed from 60 s to 30 s.", "Interval synchronizácie sa zmenil z 60 s na 30 s.")]
    [InlineData("The process 'chrome' (20852) was ended.", "Proces „chrome“ (20852) bol ukončený.")]
    [InlineData("The agent did not pick the command up in time.", "Agent si príkaz včas neprevzal.")]
    [InlineData("Agent on device 'PC' registered with an enrollment code ('Učebňa 3').", "Agent na zariadení „PC“ sa zaregistroval registračným kódom ('Učebňa 3').")]
    [InlineData("User 'admin' created an enrollment code for a new computer, valid until 2026-10-28 10:00 UTC.", "Používateľ „admin“ vytvoril registračný kód pre nový počítač s platnosťou do 2026-10-28 10:00 UTC.")]
    [InlineData("User 'admin' created an enrollment code for device 'PC' ('reinstall'), valid until 2026-10-28 10:00 UTC.", "Používateľ „admin“ vytvoril registračný kód pre zariadenie „PC“ ('reinstall') s platnosťou do 2026-10-28 10:00 UTC.")]
    [InlineData("User 'admin' revoked an enrollment code.", "Používateľ „admin“ zrušil registračný kód.")]
    [InlineData("An enrollment code must be valid for 10 minutes to 7 days.", "Registračný kód musí platiť od 10 minút do 7 dní.")]
    public void EventsAndResults_AreShownInSlovak(string english, string slovak)
    {
        Assert.Equal(slovak, ServerMessages.ToSlovakText(english));
    }

    [Fact]
    public void ResultOfPing_KeepsItsLines_AndTranslatesEachOfThem()
    {
        var english = "Ping 8.8.8.8:\r\n  reply in 8 ms\r\n  no reply (TimedOut)\r\n";

        Assert.Equal(
            string.Join(Environment.NewLine, "Ping na 8.8.8.8:", "  odpoveď za 8 ms", "  bez odpovede (TimedOut)", string.Empty),
            ServerMessages.ToSlovakText(english));
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
