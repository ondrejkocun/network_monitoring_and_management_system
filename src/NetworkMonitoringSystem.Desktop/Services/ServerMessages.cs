using System.Text.RegularExpressions;
using NetworkMonitoringSystem.Desktop.ViewModels;

namespace NetworkMonitoringSystem.Desktop.Services;

/// <summary>
/// Translates the error messages of the server into Slovak. The server, its rules and its logs speak English; only
/// what the user reads is translated. A message this version does not know is shown as the server wrote it.
/// </summary>
public static partial class ServerMessages
{
    private static readonly Dictionary<string, string> Fixed = new(StringComparer.Ordinal)
    {
        // Access and sign-in.
        ["You do not have permission for this operation."] = "Na túto operáciu nemáte oprávnenie.",
        ["Sign in to use the administration API."] = "Prihláste sa.",
        ["The current password is not correct."] = "Súčasné heslo nie je správne.",
        ["The user does not exist."] = "Používateľ neexistuje.",
        ["You cannot delete your own account."] = "Vlastný účet nemôžete odstrániť.",
        ["The role does not exist."] = "Rola neexistuje.",
        ["Unknown permission."] = "Neznáme oprávnenie.",

        // Devices.
        ["The device does not exist."] = "Zariadenie neexistuje.",
        ["The item does not exist."] = "Položka neexistuje.",
        ["Device name must not be empty."] = "Názov zariadenia nesmie byť prázdny.",
        ["Device without an agent must have an IP address."] = "Zariadenie bez agenta musí mať IP adresu.",
        ["Device with an agent must have a host name."] = "Zariadenie s agentom musí mať názov počítača.",
        ["Only a device with an agent can capture network traffic."] = "Komunikáciu možno zachytávať len na zariadení s agentom.",
        ["Only a device without an agent is checked on a TCP port."] = "TCP port sa kontroluje len pri zariadení bez agenta.",

        // Remote commands and files.
        ["Commands can be sent only to a device with an agent."] = "Príkazy možno posielať len zariadeniu s agentom.",
        ["The device is disabled."] = "Zariadenie je zakázané.",
        ["The device is not online."] = "Zariadenie nie je online.",
        ["Too many commands are already waiting for this device."] = "Na toto zariadenie už čaká príliš veľa príkazov.",
        ["Unknown command."] = "Neznámy príkaz.",
        ["Unknown command type."] = "Neznámy príkaz.",
        ["This command takes no parameter."] = "Tento príkaz nemá parameter.",
        ["The process identifier must be a positive whole number."] = "PID procesu musí byť kladné celé číslo.",
        ["The service name must have 1 to 80 characters: letters, digits, spaces and . _ - $ only."] =
            "Názov služby musí mať 1 až 80 znakov: len písmená, číslice, medzery a . _ - $.",
        ["The target must be an IP address or a host name."] = "Cieľ musí byť IP adresa alebo názov počítača.",
        ["A file is sent together with its contents, not as a plain command."] = "Súbor sa posiela spolu s obsahom, nie ako bežný príkaz.",
        ["The file name is reserved by Windows for a device."] = "Takýto názov súboru si Windows vyhradzuje pre zariadenie.",
        ["The file name must not end with a dot."] = "Názov súboru nesmie končiť bodkou.",
        ["The contents do not match the size in the header."] = "Obsah súboru nezodpovedá jeho veľkosti.",
        ["The upload must start with the header of the file."] = "Odosielanie súboru musí začať jeho popisom.",

        // History.
        ["The period must have a start and an end."] = "Obdobie musí mať začiatok aj koniec.",
        ["The period must end after it starts."] = "Obdobie musí skončiť po svojom začiatku.",
        ["The period must not end before it starts."] = "Obdobie nesmie skončiť pred svojím začiatkom.",
    };

    private static readonly (Regex Pattern, string Slovak)[] Patterns =
    [
        (new(@"^'(?<a>.*)' is not a valid IP address\.$"), "„{0}“ nie je platná IP adresa."),
        (new(@"^A device with IP address '(?<a>.*)' already exists\.$"), "Zariadenie s IP adresou {0} už existuje."),
        (new(@"^Device name must have at most (?<a>\d+) characters\.$"), "Názov zariadenia môže mať najviac {0} znakov."),
        (new(@"^A user named '(?<a>.*)' already exists\.$"), "Používateľ s menom „{0}“ už existuje."),
        (new(@"^A role named '(?<a>.*)' already exists\.$"), "Rola s názvom „{0}“ už existuje."),
        (new(@"^The role '(?<a>.*)' is built in and cannot be changed\.$"), "Rola „{0}“ je vstavaná a nedá sa meniť."),
        (new(@"^User '(?<a>.*)' is the last active administrator\. Make another user an administrator first\.$"),
            "Používateľ „{0}“ je posledný aktívny správca. Najprv urobte správcom iného používateľa."),
        (new(@"^User name must have 1 to (?<a>\d+) characters\.$"), "Meno používateľa musí mať 1 až {0} znakov."),
        (new(@"^Role name must have 1 to (?<a>\d+) characters\.$"), "Názov roly musí mať 1 až {0} znakov."),
        (new(@"^The password must have (?<a>\d+) to (?<b>\d+) characters\.$"), "Heslo musí mať {0} až {1} znakov."),
        (new(@"^The file name must have 1 to (?<a>\d+) characters\.$"), "Názov súboru musí mať 1 až {0} znakov."),
        (new(@"^The file name must not contain a folder or any of the characters"), "Názov súboru nesmie obsahovať priečinok ani znaky \\ / : * ? \" < > |."),
        (new(@"^A file can have at most (?<a>\d+) MB\.$"), "Súbor môže mať najviac {0} MB."),
        (new(@"^The period must not be longer than (?<a>\d+) days\.$"), "Obdobie nesmie byť dlhšie ako {0} dní."),
        (new(@"^Synchronization interval must be between (?<a>\d+) and (?<b>\d+) seconds\.$"), "Interval synchronizácie musí byť od {0} do {1} sekúnd."),
        (new(@"^A device must be allowed to miss between (?<a>\d+) and (?<b>\d+) synchronizations\.$"),
            "Povolený počet vynechaných synchronizácií musí byť od {0} do {1}."),
        (new(@"^History must be kept between (?<a>\d+) and (?<b>\d+) days\.$"), "História sa musí uchovávať od {0} do {1} dní."),
    ];

    /// <summary>Returns the message in Slovak, or as it is when it is not known.</summary>
    public static string ToSlovak(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        // .NET appends the name of the parameter and the refused value; they mean nothing to the user.
        var text = ParameterSuffix().Replace(message.Split('\n')[0].Trim(), string.Empty);

        if (Fixed.TryGetValue(text, out var slovak))
        {
            return slovak;
        }

        foreach (var (pattern, template) in Patterns)
        {
            var match = pattern.Match(text);

            if (match.Success)
            {
                return string.Format(template, match.Groups["a"].Value, match.Groups["b"].Value);
            }
        }

        return text;
    }

    /// <summary>
    /// Translates a text the server or an agent wrote for people: an event in the log or the result of a command.
    /// Each line is translated on its own, so the output of a ping or a traceroute keeps its lines and indentation.
    /// </summary>
    public static string ToSlovakText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var trimmed = line.Trim();
            var indent = line[..(line.Length - line.TrimStart().Length)];

            if (trimmed.Length > 0)
            {
                lines[index] = indent + TranslateLine(trimmed);
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string TranslateLine(string line)
    {
        if (TextFixed.TryGetValue(line, out var fixedText) || Fixed.TryGetValue(line, out fixedText))
        {
            return fixedText;
        }

        foreach (var (pattern, template) in TextPatterns)
        {
            var match = pattern.Match(line);

            if (match.Success)
            {
                return string.Format(
                    template,
                    match.Groups["a"].Value,
                    match.Groups["b"].Value,
                    match.Groups["c"].Value,
                    match.Groups["d"].Value,
                    match.Groups["e"].Value,
                    Command(match));
            }
        }

        return ToSlovak(line);
    }

    /// <summary>A command named in a text, as "„Ping zo zariadenia“ (8.8.8.8)".</summary>
    private static string Command(Match match)
    {
        if (!match.Groups["cmd"].Success)
        {
            return string.Empty;
        }

        var name = $"„{CommandOption.Describe(match.Groups["cmd"].Value)}“";

        return match.Groups["param"].Success ? $"{name} ({match.Groups["param"].Value})" : name;
    }

    private static readonly Dictionary<string, string> TextFixed = new(StringComparer.Ordinal)
    {
        ["Sign-in failed: unknown user name."] = "Neúspešné prihlásenie: neznáme meno používateľa.",
        ["The DNS cache was flushed."] = "DNS cache bola vyprázdnená.",
        ["The agent does not end itself."] = "Agent neukončí sám seba.",
        ["The agent does not know this command."] = "Agent tento príkaz nepozná.",
        ["The agent failed to carry the command out."] = "Agentovi sa príkaz nepodarilo vykonať.",
        ["The agent reports its state now."] = "Agent hneď pošle svoj stav.",
        ["The command did not finish in time."] = "Príkaz sa nestihol dokončiť.",
        ["The agent did not pick the command up in time."] = "Agent si príkaz včas neprevzal.",
        ["The agent took the command over but did not report a result."] = "Agent príkaz prevzal, ale neohlásil výsledok.",
        ["The process identifier is not valid."] = "PID procesu nie je platný.",
        ["The service name is not valid."] = "Názov služby nie je platný.",
        ["The file name is not acceptable; the file was not downloaded."] = "Názov súboru nie je prípustný; súbor sa nestiahol.",
        ["The file arrived damaged (its SHA-256 hash does not match); it was not kept."] = "Súbor prišiel poškodený (nesedí odtlačok SHA-256); neuložil sa.",
        ["The file arrived incomplete; it was not kept."] = "Súbor neprišiel celý; neuložil sa.",
        ["The server described a different file than the command; it was not kept."] = "Server poslal iný súbor, než aký uvádza príkaz; neuložil sa.",
        ["The server sent more data than the file has; it was not kept."] = "Server poslal viac dát, než má súbor; neuložil sa.",
    };

    private static readonly (Regex Pattern, string Slovak)[] TextPatterns =
    [
        // Events of devices.
        (new(@"^Agent on device '(?<a>.*)' registered\.$"), "Agent na zariadení „{0}“ sa zaregistroval."),
        (new(@"^Device '(?<a>.*)' is online\.$"), "Zariadenie „{0}“ je online."),
        (new(@"^Device '(?<a>.*)' is offline\.$"), "Zariadenie „{0}“ je offline."),
        (new(@"^Device '(?<a>.*)' was disabled\.$"), "Zariadenie „{0}“ bolo zakázané."),
        (new(@"^Device '(?<a>.*)' was enabled\.$"), "Zariadenie „{0}“ bolo povolené."),
        (new(@"^Device '(?<a>.*)' was removed\.$"), "Zariadenie „{0}“ bolo odstránené."),
        (new(@"^Device '(?<a>.*)' was renamed to '(?<b>.*)'\.$"), "Zariadenie „{0}“ bolo premenované na „{1}“."),
        (new(@"^Device '(?<a>.*)' opened (?<b>\w+) port (?<c>\d+) on (?<d>\S+) \((?<e>.*)\)\.$"), "Zariadenie „{0}“ otvorilo {1} port {2} na {3} ({4})."),
        (new(@"^Capture of network traffic on device '(?<a>.*)' was switched on\.$"), "Zachytávanie komunikácie na zariadení „{0}“ bolo zapnuté."),
        (new(@"^Capture of network traffic on device '(?<a>.*)' was switched off\.$"), "Zachytávanie komunikácie na zariadení „{0}“ bolo vypnuté."),

        // Settings.
        (new(@"^Synchronization interval changed from (?<a>\d+) s to (?<b>\d+) s\.$"), "Interval synchronizácie sa zmenil z {0} s na {1} s."),
        (new(@"^Devices are now considered offline after (?<a>\d+) missed synchronizations instead of (?<b>\d+)\.$"),
            "Zariadenie sa teraz považuje za offline po {0} vynechaných synchronizáciách namiesto {1}."),
        (new(@"^History is now kept for (?<a>\d+) days instead of (?<b>\d+)\.$"), "História sa teraz uchováva {0} dní namiesto {1}."),

        // Sign-in and access.
        (new(@"^User '(?<a>.*)' signed in\.$"), "Používateľ „{0}“ sa prihlásil."),
        (new(@"^Sign-in of user '(?<a>.*)' failed: wrong password\. The account is locked for (?<b>\d+) minutes\.$"),
            "Neúspešné prihlásenie používateľa „{0}“: nesprávne heslo. Účet je zamknutý na {1} minút."),
        (new(@"^Sign-in of user '(?<a>.*)' failed: wrong password\.$"), "Neúspešné prihlásenie používateľa „{0}“: nesprávne heslo."),
        (new(@"^Sign-in of user '(?<a>.*)' was refused: the account is deactivated\.$"), "Prihlásenie používateľa „{0}“ bolo odmietnuté: účet je deaktivovaný."),
        (new(@"^Sign-in of user '(?<a>.*)' was refused: the account is locked\.$"), "Prihlásenie používateľa „{0}“ bolo odmietnuté: účet je zamknutý."),
        (new(@"^User '(?<a>.*)' was refused the operation '(?<b>.*)': missing permission\.$"), "Používateľovi „{0}“ bola odmietnutá operácia {1}: chýba oprávnenie."),
        (new(@"^User '(?<a>.*)' changed their password\.$"), "Používateľ „{0}“ si zmenil heslo."),
        (new(@"^User '(?<a>.*)' set a new password for user '(?<b>.*)'\.$"), "Používateľ „{0}“ nastavil nové heslo používateľovi „{1}“."),
        (new(@"^User '(?<a>.*)' created user '(?<b>.*)' with roles: (?<c>.*)\.$"), "Používateľ „{0}“ vytvoril používateľa „{1}“ s rolami: {2}."),
        (new(@"^User '(?<a>.*)' set the roles of user '(?<b>.*)' to: (?<c>.*)\.$"), "Používateľ „{0}“ nastavil roly používateľa „{1}“ na: {2}."),
        (new(@"^User '(?<a>.*)' activated user '(?<b>.*)'\.$"), "Používateľ „{0}“ aktivoval používateľa „{1}“."),
        (new(@"^User '(?<a>.*)' deactivated user '(?<b>.*)'\.$"), "Používateľ „{0}“ deaktivoval používateľa „{1}“."),
        (new(@"^User '(?<a>.*)' deleted user '(?<b>.*)'\.$"), "Používateľ „{0}“ odstránil používateľa „{1}“."),
        (new(@"^User '(?<a>.*)' created role '(?<b>.*)'\.$"), "Používateľ „{0}“ vytvoril rolu „{1}“."),
        (new(@"^User '(?<a>.*)' deleted role '(?<b>.*)'\.$"), "Používateľ „{0}“ odstránil rolu „{1}“."),
        (new(@"^User '(?<a>.*)' allowed '(?<b>.*)' on all devices to role '(?<d>.*)'\.$"), "Používateľ „{0}“ povolil {1} na všetkých zariadeniach role „{3}“."),
        (new(@"^User '(?<a>.*)' allowed '(?<b>.*)' on device '(?<c>.*)' to role '(?<d>.*)'\.$"), "Používateľ „{0}“ povolil {1} na zariadení „{2}“ role „{3}“."),
        (new(@"^User '(?<a>.*)' revoked '(?<b>.*)' on all devices from role '(?<d>.*)'\.$"), "Používateľ „{0}“ odobral role „{3}“ {1} na všetkých zariadeniach."),
        (new(@"^User '(?<a>.*)' revoked '(?<b>.*)' on one device from role '(?<d>.*)'\.$"), "Používateľ „{0}“ odobral role „{3}“ {1} na jednom zariadení."),

        // Remote commands and files.
        (new(@"^User '(?<a>.*)' sent the command '(?<cmd>\w+)'( \((?<param>.*)\))? to device '(?<d>.*)'\.$"), "Používateľ „{0}“ poslal príkaz {5} na zariadenie „{3}“."),
        (new(@"^The command '(?<cmd>\w+)'( \((?<param>.*)\))? sent by '(?<a>.*)' succeeded on device '(?<d>.*)'\.$"), "Príkaz {5}, ktorý poslal „{0}“, na zariadení „{3}“ uspel."),
        (new(@"^The command '(?<cmd>\w+)'( \((?<param>.*)\))? sent by '(?<a>.*)' failed on device '(?<d>.*)'\.$"), "Príkaz {5}, ktorý poslal „{0}“, na zariadení „{3}“ zlyhal."),
        (new(@"^User '(?<a>.*)' sent the file '(?<b>.*)' \((?<c>\d+) bytes, SHA-256 (?<d>\w+)\) to device '(?<e>.*)'\.$"),
            "Používateľ „{0}“ poslal súbor „{1}“ ({2} bajtov, SHA-256 {3}) na zariadenie „{4}“."),

        // Results reported by agents.
        (new(@"^Ping (?<a>.+):$"), "Ping na {0}:"),
        (new(@"^reply in (?<a>\d+) ms$"), "odpoveď za {0} ms"),
        (new(@"^no reply \((?<a>.*)\)$"), "bez odpovede ({0})"),
        (new(@"^Route to (?<a>.+):$"), "Trasa k {0}:"),
        (new(@"^Reached in (?<a>\d+) hop\(s\)\.$"), "Cieľ dosiahnutý po {0} skokoch."),
        (new(@"^Saved as (?<a>.+) \((?<b>\d+) bytes, SHA-256 (?<c>\w+)\)\.$"), "Uložené ako {0} ({1} bajtov, SHA-256 {2})."),
        (new(@"^The device will restart in (?<a>\d+) seconds\.$"), "Zariadenie sa o {0} sekúnd reštartuje."),
        (new(@"^The device will shut down in (?<a>\d+) seconds\.$"), "Zariadenie sa o {0} sekúnd vypne."),
        (new(@"^The service '(?<a>.*)' is running\.$"), "Služba „{0}“ beží."),
        (new(@"^The service '(?<a>.*)' is stopped\.$"), "Služba „{0}“ je zastavená."),
        (new(@"^The service '(?<a>.*)' was already running\.$"), "Služba „{0}“ už bežala."),
        (new(@"^The service '(?<a>.*)' was already stopped\.$"), "Služba „{0}“ už bola zastavená."),
        (new(@"^The service '(?<a>.*)' does not exist on this device\.$"), "Služba „{0}“ na tomto zariadení neexistuje."),
        (new(@"^The process '(?<a>.*)' \((?<b>\d+)\) was ended\.$"), "Proces „{0}“ ({1}) bol ukončený."),
        (new(@"^The process '(?<a>.*)' \((?<b>\d+)\) was asked to end but is still running\.$"), "Proces „{0}“ ({1}) dostal pokyn skončiť, ale stále beží."),
        (new(@"^The process '(?<a>.*)' \((?<b>\d+)\) is part of the system and is not ended\.$"), "Proces „{0}“ ({1}) je súčasťou systému a neukončí sa."),
        (new(@"^No process with the identifier (?<a>\d+) is running\.$"), "Nebeží žiadny proces s PID {0}."),
        (new(@"^The address '(?<a>.*)' could not be resolved\.$"), "Adresu „{0}“ sa nepodarilo preložiť."),
        (new(@"^The program '(?<a>.*)' could not be started\.$"), "Program „{0}“ sa nepodarilo spustiť."),
        (new(@"^The system refused \(code (?<a>-?\d+)\)\.\s*(?<b>.*)$"), "Systém odmietol (kód {0}). {1}"),
        (new(@"^The file could not be downloaded: (?<a>.*)$"), "Súbor sa nepodarilo stiahnuť: {0}"),
        (new(@"^The file could not be saved: (?<a>.*)$"), "Súbor sa nepodarilo uložiť: {0}"),
    ];

    [GeneratedRegex(@"\s*\(Parameter '[^']*'\)$")]
    private static partial Regex ParameterSuffix();
}
