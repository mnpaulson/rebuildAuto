cd RoRebuildServer\GameConfig
dotnet build -c Release --property WarningLevel=0
cd ../..
cd RoRebuildServer\RebuildSharedData
dotnet build -c Release --property WarningLevel=0
cd ../..
if not exist "RebuildClient\Assets\Data\" mkdir "RebuildClient\Assets\Data\"
copy /b/v/y "RoRebuildServer\GameConfig\bin\Release\netstandard2.0\GameConfig.dll" "RebuildClient\Assets\Data\GameConfig.dll"
copy /b/v/y "RoRebuildServer\RebuildSharedData\bin\Release\netstandard2.1\RebuildSharedData.dll" "RebuildClient\Assets\Data\RebuildSharedData.dll"
if not exist "C:\games\RagnarokRebuild\BepInEx\plugins\" mkdir "C:\games\RagnarokRebuild\BepInEx\plugins\"
if exist "RebuildBotPlugin\bin\Release\RebuildBotPlugin.dll" copy /b/v/y "RebuildBotPlugin\bin\Release\RebuildBotPlugin.dll" "C:\games\RagnarokRebuild\BepInEx\plugins\RebuildBotPlugin.dll"
cd RoRebuildServer\DataToClientUtility
dotnet build -c Release --property WarningLevel=0
cd "bin\Release\net9.0\"
DataToClientUtility.exe
pause