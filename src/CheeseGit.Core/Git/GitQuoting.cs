namespace CheeseGit.Core.Git;

/// <summary>
/// Git's C-style path quoting, used in patch headers where <c>-z</c> does not
/// apply. Even with <c>core.quotepath=false</c>, names containing '"', '\',
/// control characters or a leading/trailing space come out quoted.
/// </summary>
public static class GitQuoting
{
    /// <summary>
    /// If <paramref name="text"/> starts with a quoted string, decodes it and
    /// returns the remainder after the closing quote; otherwise returns false.
    /// </summary>
    public static bool TryUnquote(ReadOnlySpan<byte> text, out byte[] value, out ReadOnlySpan<byte> rest)
    {
        value = [];
        rest = text;
        if (text.IsEmpty || text[0] != (byte)'"') return false;

        var output = new List<byte>(text.Length);
        for (var i = 1; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == (byte)'"')
            {
                value = [.. output];
                rest = text[(i + 1)..];
                return true;
            }
            if (ch != (byte)'\\')
            {
                output.Add(ch);
                continue;
            }
            if (++i >= text.Length) return false;
            ch = text[i];
            switch ((char)ch)
            {
                case 'a': output.Add(7); break;
                case 'b': output.Add(8); break;
                case 't': output.Add(9); break;
                case 'n': output.Add(10); break;
                case 'v': output.Add(11); break;
                case 'f': output.Add(12); break;
                case 'r': output.Add(13); break;
                case '"': output.Add((byte)'"'); break;
                case '\\': output.Add((byte)'\\'); break;
                case >= '0' and <= '3':
                    if (i + 2 >= text.Length) return false;
                    output.Add((byte)(((ch - '0') << 6) | ((text[i + 1] - '0') << 3) | (text[i + 2] - '0')));
                    i += 2;
                    break;
                default:
                    return false;
            }
        }
        return false;
    }
}
