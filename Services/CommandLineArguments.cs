using System.Text;

namespace vs2026_plugin.Services
{
    /// <summary>
    /// Builds <see cref="System.Diagnostics.ProcessStartInfo.Arguments"/> on net472, which has no ArgumentList. Each
    /// argument is quoted so CommandLineToArgvW reads it back intact: a quote or a trailing backslash inside a value
    /// (an exclude "build\", a value from .xygeni\ide.json) must not end the argument and merge or inject the next ones.
    /// </summary>
    public static class CommandLineArguments
    {
        /// <summary>
        /// One argument, always quoted: backslashes before a quote are doubled and the quote escaped, trailing
        /// backslashes doubled so they do not escape the closing quote.
        /// </summary>
        public static string Quote(string arg)
        {
            var quoted = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char c in arg ?? string.Empty)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (c == '"')
                {
                    quoted.Append('\\', backslashes * 2 + 1);
                }
                else
                {
                    quoted.Append('\\', backslashes);
                }
                quoted.Append(c);
                backslashes = 0;
            }
            quoted.Append('\\', backslashes * 2);
            quoted.Append('"');
            return quoted.ToString();
        }
    }
}
