using System.Text;
using PocketMC.Desktop.Services;

namespace PocketMC.Desktop.Tests;

public sealed class ServerPropertiesParserTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "PocketMC.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Read_IgnoresInlineCommentsInPropertyValues()
    {
        Directory.CreateDirectory(_tempDirectory);
        string filePath = Path.Combine(_tempDirectory, "server.properties");
        File.WriteAllText(filePath, "motd=Hello world # keep this comment" + Environment.NewLine, new UTF8Encoding(false));

        var properties = ServerPropertiesParser.Read(filePath);

        Assert.Equal("Hello world", properties["motd"]);
    }

    [Fact]
    public void Write_PreservesInlineCommentsAndUntouchedLines()
    {
        Directory.CreateDirectory(_tempDirectory);
        string filePath = Path.Combine(_tempDirectory, "server.properties");
        File.WriteAllLines(
            filePath,
            new[]
            {
                "# Minecraft server properties",
                "motd=Old name # visible comment",
                "difficulty=normal"
            },
            new UTF8Encoding(false));

        ServerPropertiesParser.Write(
            filePath,
            new Dictionary<string, string>
            {
                ["motd"] = "New name",
                ["difficulty"] = "hard"
            });

        string[] lines = File.ReadAllLines(filePath);
        Assert.Equal("# Minecraft server properties", lines[0]);
        Assert.Equal("motd=New name # visible comment", lines[1]);
        Assert.Equal("difficulty=hard", lines[2]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
