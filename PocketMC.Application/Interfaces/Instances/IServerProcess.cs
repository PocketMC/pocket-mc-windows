using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PocketMC.Domain.Models;

namespace PocketMC.Application.Interfaces
{
    public interface IServerProcess
    {
        ServerState State { get; }
        int PlayerCount { get; }
        IReadOnlyList<string> OnlinePlayerNames { get; }
        IEnumerable<string> OutputBuffer { get; }
        event Action<string>? OnOutputLine;
        event Action<string>? OnErrorLine;
        Task WriteInputAsync(string input);
    }
}
