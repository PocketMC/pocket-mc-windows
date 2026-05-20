namespace PocketMC.Desktop.Tests;

public sealed class JavaRuntimeValidatorTests
{
    [Fact]
    public void ValidateRuntimeAsync_KillsJavaProcessTreeWhenValidationIsCanceled()
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Java",
            "JavaRuntimeValidator.cs"));

        Assert.Contains("catch (OperationCanceledException", source, StringComparison.Ordinal);
        Assert.Contains("process.Kill(entireProcessTree: true)", source, StringComparison.Ordinal);
        Assert.Contains("throw new TimeoutException", source, StringComparison.Ordinal);
    }
}
