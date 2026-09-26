using PocketMC.Domain.Models;
using Xunit;

namespace PocketMC.Domain.Tests.Models;

public class OllamaModelsTests
{
    // ── FormattedSize ───────────────────────────────────────────────────

    [Fact]
    public void FormattedSize_LargeFile_ReturnsGB()
    {
        var model = new OllamaModelInfo { SizeBytes = 5225388164L };
        Assert.Contains("GB", model.FormattedSize);
        Assert.StartsWith("4.8", model.FormattedSize);
    }

    [Fact]
    public void FormattedSize_MediumFile_ReturnsMB()
    {
        var model = new OllamaModelInfo { SizeBytes = 274302450L };
        Assert.Contains("MB", model.FormattedSize);
    }

    [Fact]
    public void FormattedSize_Zero_ReturnsUnknown()
    {
        var model = new OllamaModelInfo { SizeBytes = 0 };
        Assert.Equal("Unknown", model.FormattedSize);
    }

    [Fact]
    public void FormattedSize_Negative_ReturnsUnknown()
    {
        var model = new OllamaModelInfo { SizeBytes = -1 };
        Assert.Equal("Unknown", model.FormattedSize);
    }

    // ── SupportsCompletion ──────────────────────────────────────────────

    [Fact]
    public void SupportsCompletion_WithCompletionCapability_ReturnsTrue()
    {
        var model = new OllamaModelInfo
        {
            Capabilities = new[] { "completion", "tools" }
        };
        Assert.True(model.SupportsCompletion);
    }

    [Fact]
    public void SupportsCompletion_EmbeddingOnly_ReturnsFalse()
    {
        var model = new OllamaModelInfo
        {
            Capabilities = new[] { "embedding" }
        };
        Assert.False(model.SupportsCompletion);
    }

    [Fact]
    public void SupportsCompletion_EmptyCapabilities_DefaultsToTrue()
    {
        var model = new OllamaModelInfo
        {
            Capabilities = System.Array.Empty<string>()
        };
        Assert.True(model.SupportsCompletion);
    }

    // ── OllamaPullProgress ──────────────────────────────────────────────

    [Fact]
    public void Percent_WithValidBytes_CalculatesCorrectly()
    {
        var progress = new OllamaPullProgress
        {
            TotalBytes = 1000,
            CompletedBytes = 500
        };
        Assert.NotNull(progress.Percent);
        Assert.Equal(50.0, progress.Percent!.Value, precision: 1);
    }

    [Fact]
    public void Percent_ZeroTotal_ReturnsNull()
    {
        var progress = new OllamaPullProgress
        {
            TotalBytes = 0,
            CompletedBytes = 0
        };
        Assert.Null(progress.Percent);
    }

    [Fact]
    public void Percent_NullTotal_ReturnsNull()
    {
        var progress = new OllamaPullProgress
        {
            TotalBytes = null,
            CompletedBytes = 500
        };
        Assert.Null(progress.Percent);
    }

    [Fact]
    public void Success_Factory_SetsCorrectState()
    {
        var p = OllamaPullProgress.Success();
        Assert.True(p.IsComplete);
        Assert.Equal("success", p.Status);
        Assert.Null(p.ErrorMessage);
    }

    [Fact]
    public void Error_Factory_SetsCorrectState()
    {
        var p = OllamaPullProgress.Error("test error");
        Assert.True(p.IsComplete);
        Assert.Equal("test error", p.ErrorMessage);
    }

    // ── OllamaDaemonStatus ──────────────────────────────────────────────

    [Fact]
    public void Online_Factory_SetsCorrectState()
    {
        var s = OllamaDaemonStatus.Online("0.5.1");
        Assert.True(s.IsRunning);
        Assert.Equal("0.5.1", s.Version);
        Assert.Null(s.ErrorMessage);
    }

    [Fact]
    public void Offline_Factory_SetsCorrectState()
    {
        var s = OllamaDaemonStatus.Offline("Not running");
        Assert.False(s.IsRunning);
        Assert.Equal("Not running", s.ErrorMessage);
        Assert.Null(s.Version);
    }

    // ── AppSettings.IsAiConfigured Ollama Edge Cases ────────────────────

    [Fact]
    public void IsAiConfigured_OllamaLocalWithoutKey_ReturnsTrue()
    {
        var settings = new AppSettings
        {
            AiProvider = "Ollama",
            OllamaMode = "Local"
        };
        Assert.True(settings.IsAiConfigured());
    }

    [Fact]
    public void IsAiConfigured_OllamaCloudWithoutKey_ReturnsFalse()
    {
        var settings = new AppSettings
        {
            AiProvider = "Ollama",
            OllamaMode = "Cloud"
        };
        Assert.False(settings.IsAiConfigured());
    }

    [Fact]
    public void IsAiConfigured_OllamaCloudWithKey_ReturnsTrue()
    {
        var settings = new AppSettings
        {
            AiProvider = "Ollama",
            OllamaMode = "Cloud"
        };
        settings.AiApiKeys["Ollama"] = "ollama-cloud-api-key-12345";
        Assert.True(settings.IsAiConfigured());
    }

    [Fact]
    public void IsAiConfigured_OtherProviderWithoutKey_ReturnsFalse()
    {
        var settings = new AppSettings
        {
            AiProvider = "Gemini"
        };
        Assert.False(settings.IsAiConfigured());
    }
}
