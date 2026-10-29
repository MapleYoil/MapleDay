namespace MapleDay.Core;

public readonly record struct FormattedIntegerInput(string Text, int Caret);

public static class IntegerInputFormatter
{
    public static FormattedIntegerInput Format(string source, int caret)
    {
        caret = Math.Clamp(caret, 0, source.Length);
        var digitsAfterCaret = source[caret..].Count(char.IsAsciiDigit);
        var digits = string.Concat(source.Where(char.IsAsciiDigit)).TrimStart('0');
        if (digits.Length == 0 && source.Any(char.IsAsciiDigit)) digits = "0";
        var first = digits.Length % 3;
        if (first == 0 && digits.Length > 0) first = 3;
        var formatted = digits.Length == 0 ? "" : digits[..first]
            + string.Concat(Enumerable.Range(0, (digits.Length - first) / 3)
                .Select(index => "," + digits.Substring(first + index * 3, 3)));
        var nextCaret = formatted.Length;
        var remaining = digitsAfterCaret;
        while (nextCaret > 0 && remaining > 0)
        {
            nextCaret--;
            if (char.IsAsciiDigit(formatted[nextCaret])) remaining--;
        }
        while (nextCaret > 0 && !char.IsAsciiDigit(formatted[nextCaret - 1])) nextCaret--;
        return new(formatted, nextCaret);
    }
}
