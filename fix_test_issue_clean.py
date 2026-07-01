import re

file_path = './PocketMC.Desktop/Features/Players/Services/ServerStateFileService.cs'
with open(file_path, 'r') as f:
    content = f.read()

# Replace the single SemaphoreSlim with ConcurrentDictionary per the previous critique
old_lock = 'private static readonly System.Threading.SemaphoreSlim _lock = new System.Threading.SemaphoreSlim(1, 1);'
new_lock = 'private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Threading.SemaphoreSlim> _fileLocks = new(StringComparer.OrdinalIgnoreCase);'

content = content.replace(old_lock, new_lock)

old_read = """    private static async Task<string?> ReadTextWithRetriesAsync(string path)
    {
        await _lock.WaitAsync();
        try
        {
            return await ReadTextWithRetriesInternalAsync(path);
        }
        finally
        {
            _lock.Release();
        }
    }"""

new_read = """    private static async Task<string?> ReadTextWithRetriesAsync(string path)
    {
        var fileLock = _fileLocks.GetOrAdd(path, _ => new System.Threading.SemaphoreSlim(1, 1));
        await fileLock.WaitAsync();
        try
        {
            return await ReadTextWithRetriesInternalAsync(path);
        }
        finally
        {
            fileLock.Release();
        }
    }"""

if old_read in content:
    content = content.replace(old_read, new_read)

with open(file_path, 'w') as f:
    f.write(content)

print("ServerStateFileService patched with ConcurrentDictionary")
