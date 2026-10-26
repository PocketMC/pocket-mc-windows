using System.Globalization;
using System.Text;
using PocketMC.Domain.Storage;

namespace PocketMC.Application.Services.Instances;

/// <summary>Java-properties escaping and logical lines, also compatible with simple BDS properties.</summary>
public static class ServerPropertiesParser
{
    public static Dictionary<string, string> Read(string filePath)
    {
        var properties = new Dictionary<string, string>();
        if (!File.Exists(filePath)) return properties;
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            string logical = line.TrimStart();
            if (logical.Length == 0 || logical[0] is '#' or '!') continue;
            while (HasContinuation(logical))
            {
                string? next = reader.ReadLine();
                logical = logical[..^1] + (next?.TrimStart() ?? "");
                if (next == null) break;
            }
            int split = 0;
            bool escaped = false;
            for (; split < logical.Length; split++)
            {
                char c = logical[split];
                if (!escaped && (c is '=' or ':' || char.IsWhiteSpace(c))) break;
                escaped = !escaped && c == '\\';
            }
            string key = Decode(logical[..split]);
            int valueStart = split;
            while (valueStart < logical.Length && char.IsWhiteSpace(logical[valueStart])) valueStart++;
            if (valueStart < logical.Length && logical[valueStart] is '=' or ':') valueStart++;
            while (valueStart < logical.Length && char.IsWhiteSpace(logical[valueStart])) valueStart++;
            properties[key] = Decode(logical[valueStart..]);
        }
        return properties;
    }

    public static void Write(string filePath, Dictionary<string, string> properties)
    {
        var comments = new List<string>();
        if (File.Exists(filePath))
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            string? line;
            while ((line = reader.ReadLine()) != null)
                if (line.TrimStart().StartsWith('#') || line.TrimStart().StartsWith('!')) comments.Add(line);
        }
        comments.AddRange(properties.Select(p => $"{Encode(p.Key, true)}={Encode(p.Value, false)}"));
        FileUtils.AtomicWriteAllText(filePath, string.Join(Environment.NewLine, comments) + Environment.NewLine, new UTF8Encoding(false));
    }

    private static bool HasContinuation(string value) => (value.Reverse().TakeWhile(c => c == '\\').Count() & 1) == 1;
    private static string Decode(string value)
    {
        var result = new StringBuilder();
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c != '\\' || ++i >= value.Length) { result.Append(c); continue; }
            c = value[i];
            if (c == 'u')
            {
                if (i + 4 >= value.Length || !ushort.TryParse(value.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort code))
                    throw new InvalidDataException("Malformed Unicode escape in server.properties.");
                result.Append((char)code); i += 4;
            }
            else result.Append(c switch { 't' => '\t', 'r' => '\r', 'n' => '\n', 'f' => '\f', _ => c });
        }
        return result.ToString();
    }
    private static string Encode(string value, bool key)
    {
        var result = new StringBuilder();
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            result.Append(c switch
            {
                '\\' => "\\\\",
                '\r' => "\\r",
                '\n' => "\\n",
                '\t' => "\\t",
                '\f' => "\\f",
                ' ' when key || i == 0 || i == value.Length - 1 => "\\ ",
                '=' or ':' or '#' or '!' when key => "\\" + c,
                _ when char.IsControl(c) => "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture),
                _ => c.ToString()
            });
        }
        return result.ToString();
    }
}
