using System.Text.RegularExpressions;

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

    [GeneratedRegex(@"\s*\(Parameter '[^']*'\)$")]
    private static partial Regex ParameterSuffix();
}
