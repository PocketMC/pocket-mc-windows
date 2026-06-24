using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PocketMC.Application.Interfaces.AI;
using PocketMC.Domain.Models;
using PocketMC.Infrastructure.AI;
using PocketMC.Infrastructure.AI.Providers;
using Xunit;

namespace PocketMC.Infrastructure.Tests.AI;

public class LlmProviderFactoryTests
{
    [Fact]
    public void GetProvider_ResolvesCorrectProvider()
    {
        // Arrange
        var services = new ServiceCollection();

        var mockGemini = new Mock<ILlmProvider>();
        mockGemini.Setup(p => p.ProviderType).Returns(AiProviderType.Gemini);

        var mockOpenAi = new Mock<ILlmProvider>();
        mockOpenAi.Setup(p => p.ProviderType).Returns(AiProviderType.OpenAI);

        services.AddSingleton(mockGemini.Object);
        services.AddSingleton(mockOpenAi.Object);

        var serviceProvider = services.BuildServiceProvider();
        var factory = new LlmProviderFactory(serviceProvider);

        // Act
        var result = factory.GetProvider(AiProviderType.OpenAI);

        // Assert
        Assert.Equal(AiProviderType.OpenAI, result.ProviderType);
    }
}
