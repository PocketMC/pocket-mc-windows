mkdir -p ./PocketMC.Application/Instances
mv ./PocketMC.Desktop/Features/Instances/Services ./PocketMC.Application/Instances/
mv ./PocketMC.Desktop/Features/Instances/Providers ./PocketMC.Application/Instances/
mv ./PocketMC.Desktop/Features/Instances/Backups ./PocketMC.Application/Instances/
mv ./PocketMC.Desktop/Features/Instances/Updates ./PocketMC.Application/Instances/

cat << 'PYTHON' > patch_application_namespaces.py
import os
import re

replacements = [
    (r'PocketMC\.Desktop\.Features\.Instances\.Services', r'PocketMC.Application.Instances.Services'),
    (r'PocketMC\.Desktop\.Features\.Instances\.Providers', r'PocketMC.Application.Instances.Providers'),
    (r'PocketMC\.Desktop\.Features\.Instances\.Backups', r'PocketMC.Application.Instances.Backups'),
    (r'PocketMC\.Desktop\.Features\.Instances\.Updates', r'PocketMC.Application.Instances.Updates'),
]

moved_files = []
for root, dirs, files in os.walk('./PocketMC.Application/Instances'):
    for f in files:
        if f.endswith('.cs'):
            moved_files.append(os.path.join(root, f))

for file_path in moved_files:
    with open(file_path, 'r') as f:
        content = f.read()

    content = re.sub(r'namespace PocketMC\.Desktop\.Features\.Instances', 'namespace PocketMC.Application.Instances', content)

    with open(file_path, 'w') as f:
        f.write(content)

for root, dirs, files in os.walk('.'):
    for f in files:
        if f.endswith('.cs'):
            file_path = os.path.join(root, f)
            with open(file_path, 'r') as file:
                content = file.read()

            new_content = content
            for old, new in replacements:
                new_content = re.sub(old, new, new_content)

            if new_content != content:
                with open(file_path, 'w') as file:
                    file.write(new_content)
PYTHON
python3 patch_application_namespaces.py
