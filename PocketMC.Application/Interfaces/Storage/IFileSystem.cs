using System.IO;
using System.Threading.Tasks;

using PocketMC.Domain.Models;

namespace PocketMC.Application.Interfaces
{
    public interface IFileSystem
    {
        bool DirectoryExists(string path);
        void CreateDirectory(string path);
        bool FileExists(string path);
        Task WriteAllTextAsync(string path, string contents);
        Task<string> ReadAllTextAsync(string path);
        Task WriteAllBytesAsync(string path, byte[] bytes);
        Task DeleteFileAsync(string path);
        string CombinePath(params string[] paths);
    }
}
