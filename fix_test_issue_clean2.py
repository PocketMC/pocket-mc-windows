import re

file_path = './PocketMC.Desktop/Features/Intelligence/SummaryStorageService.cs'
with open(file_path, 'r') as f:
    content = f.read()

old_lock = 'private static readonly System.Threading.SemaphoreSlim _lock = new System.Threading.SemaphoreSlim(1, 1);'
new_lock = 'private static readonly object _syncRoot = new object();'
content = content.replace(old_lock, new_lock)

content = content.replace('_lock.Wait();\n        try\n        {', 'lock (_syncRoot)\n        {')
content = content.replace('        finally\n        {\n            _lock.Release();\n        }', '')

with open(file_path, 'w') as f:
    f.write(content)

print("SummaryStorageService patched with _syncRoot")

# Remove CORS
file_path = './PocketMC.Desktop/Features/RemoteControl/Hosting/RemoteDashboardHost.cs'
with open(file_path, 'r') as f:
    content = f.read()

cors_services_pattern = r'\s*builder\.Services\.AddCors\(options =>\s*\{\s*options\.AddPolicy\("RemoteDashboardCors", policy =>\s*\{\s*policy\.AllowAnyOrigin\(\)\s*\.AllowAnyHeader\(\)\s*\.AllowAnyMethod\(\);\s*\}\);\s*\}\);'
content = re.sub(cors_services_pattern, '', content)

cors_app_pattern = r'\s*app\.UseCors\("RemoteDashboardCors"\);'
content = re.sub(cors_app_pattern, '', content)

with open(file_path, 'w') as f:
    f.write(content)
