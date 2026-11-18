dotnet build PocketMC.Desktop.sln
dotnet publish PocketMC.Desktop/PocketMC.Desktop.csproj -c Release -r win-x64 --self-contained false -p:Version=1.9.12 -o publish
vpk pack -u PocketMC -v 1.9.15 --packTitle PocketMc --packAuthors "CS Labs" -i PocketMC.Desktop/icon.ico -p publish -e PocketMC.exe --framework net8.0 --runtime win-x64 -o Releases 