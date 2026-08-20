using PocketMC.Domain.Models;
using PocketMC.Desktop.Features.Players;

namespace PocketMC.Desktop.Tests.Features.Players;

public sealed class PlayerViewModelTests
{
    [Fact]
    public void SetGameModeFromServer_DoesNotDispatchGamemodeCommand()
    {
        int dispatchCount = 0;
        var player = CreatePlayer((_, _) =>
        {
            dispatchCount++;
            return Task.CompletedTask;
        });

        player.SetGameModeFromServer("creative");

        Assert.Equal("creative", player.SelectedGameMode);
        Assert.Equal("creative", player.ConfirmedGameMode);
        Assert.Equal(0, dispatchCount);
    }

    [Fact]
    public void RevertGameModeFromPendingChange_RestoresConfirmedModeSilently()
    {
        int dispatchCount = 0;
        var player = CreatePlayer((_, _) =>
        {
            dispatchCount++;
            return Task.CompletedTask;
        });
        player.IsServerOnline = true;
        player.SetGameModeFromServer("survival");

        player.SelectedGameMode = "adventure";
        player.RevertGameModeFromPendingChange("survival");

        Assert.Equal("survival", player.SelectedGameMode);
        Assert.Equal("survival", player.ConfirmedGameMode);
        Assert.Equal(1, dispatchCount);
    }

    [Fact]
    public void IsOpBusyCombinesInitialLoadAndCommandUpdate()
    {
        var player = CreatePlayer((_, _) => Task.CompletedTask);

        player.IsOpLoading = true;

        Assert.True(player.IsOpBusy);

        player.IsOpLoading = false;
        player.IsOpUpdating = true;

        Assert.True(player.IsOpBusy);
    }

    [Theory]
    [InlineData("survival")]
    [InlineData("creative")]
    [InlineData("adventure")]
    [InlineData("spectator")]
    public void ConfirmGameModeChange_UpdatesBothSelectedAndConfirmed(string mode)
    {
        var player = CreatePlayer((_, _) => Task.CompletedTask);
        player.ConfirmGameModeChange(mode);

        Assert.Equal(mode, player.SelectedGameMode);
        Assert.Equal(mode, player.ConfirmedGameMode);
        Assert.False(player.IsGamemodeLoading);
    }

    private static PlayerViewModel CreatePlayer(Func<PlayerViewModel, string, Task> changeGamemodeAsync)
    {
        return new PlayerViewModel(
            "Sahaj33",
            _ => Task.CompletedTask,
            changeGamemodeAsync,
            (_, _, _) => Task.FromResult(true));
    }
}
