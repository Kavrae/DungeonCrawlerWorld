using System.Text;

namespace Workshop;

/// <summary>Joins arguments into one command-line string and splits one back, by the rules a Windows program reads its command line with.</summary>
/// <remarks>
/// An argument with no space, tab or quote is written as it is; any other is quoted, its quotes escaped with a backslash
/// and the backslashes before a quote or the closing quote doubled. Split reads the same rules back: whitespace outside
/// quotes separates, 2n backslashes before a quote are n backslashes and a quote that toggles quoting, 2n+1 are n and a
/// literal quote, and a backslash anywhere else is itself.
/// </remarks>
public static class CommandLineArguments
{
    public static string Join(IEnumerable<string> arguments) => string.Join(' ', arguments.Select(Quote));

    public static string Quote(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '"']) < 0)
        {
            return argument;
        }

        var quoted = new StringBuilder(argument.Length + 2).Append('"');
        var backslashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            quoted.Append('\\', character == '"' ? backslashes * 2 + 1 : backslashes);
            backslashes = 0;
            quoted.Append(character);
        }

        return quoted.Append('\\', backslashes * 2).Append('"').ToString();
    }

    public static IReadOnlyList<string> Split(string commandLine)
    {
        var arguments = new List<string>();
        var current = new StringBuilder();
        var hasArgument = false;
        var isQuoted = false;
        var backslashes = 0;
        foreach (var character in commandLine)
        {
            if (character == '\\')
            {
                backslashes++;
                hasArgument = true;
                continue;
            }

            if (character == '"')
            {
                current.Append('\\', backslashes / 2);
                if (backslashes % 2 == 1)
                {
                    current.Append('"');
                }
                else
                {
                    isQuoted = !isQuoted;
                }

                backslashes = 0;
                hasArgument = true;
                continue;
            }

            current.Append('\\', backslashes);
            backslashes = 0;
            if (!isQuoted && character is ' ' or '\t')
            {
                if (hasArgument)
                {
                    arguments.Add(current.ToString());
                    current.Clear();
                    hasArgument = false;
                }

                continue;
            }

            current.Append(character);
            hasArgument = true;
        }

        current.Append('\\', backslashes);
        if (hasArgument)
        {
            arguments.Add(current.ToString());
        }

        return arguments;
    }
}
