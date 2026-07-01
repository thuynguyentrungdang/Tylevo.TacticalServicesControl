# Building

This repository does not include proprietary EFT or SPT assemblies. Provide local references from your own SPT install.

## Requirements

- .NET SDK compatible with the project.
- SPT 4.0.13 reference assemblies.
- UnityToolkit v2.0.1.
- WTT Client Common Lib and WTT Server Common Lib, installed separately as dependencies.
- Project Fika references if building the Fika plugin.

TSC references WTT Common Lib from the local SPT dependency install. Do not copy WTT Common Lib source or binaries into the TSC source tree or release archive.

## Reference Paths

Create a local `Shared.User.props` or pass MSBuild properties:

- `SptDir`: path to a local SPT-style folder used for post-build output.
- `SptSharedAssembliesDir`: folder containing the versioned SPT reference assemblies, such as `400x/Assembly-CSharp.dll`.
- `SevenZipPath`: optional path to `7z.exe` when creating release archives. Defaults to `C:\Program Files\7-Zip\7z.exe`.

Use forward slashes or quote paths carefully when paths contain spaces.

## Build Commands

Example:

```powershell
dotnet build .\project\SamSWAT.FireSupport\SamSWAT.FireSupport.Core.csproj --configuration "SPT-4.0 Release" "-p:SptDir=C:/Path/To/SPT/" "-p:SptSharedAssembliesDir=C:/Path/To/SPT Assemblies/" "-p:ConfigurationName=Debug"
dotnet build .\project\SamSWAT.FireSupport.Fika\SamSWAT.FireSupport.Fika.csproj --configuration "SPT-4.0 Release" "-p:SptDir=C:/Path/To/SPT/" "-p:SptSharedAssembliesDir=C:/Path/To/SPT Assemblies/" "-p:ConfigurationName=Debug"
dotnet build .\project\SamSWAT.FireSupport.Server\SamSWAT.FireSupport.Server.csproj --configuration "SPT-4.0 Release" "-p:SptDir=C:/Path/To/ServerSPT/" "-p:SptSharedAssembliesDir=C:/Path/To/SPT Assemblies/" "-p:ConfigurationName=Debug"
```

## Packaging

Release archive creation uses `SevenZipPath` and emits a `.zip` file matching the public package format. Override the tool path when needed:

```powershell
dotnet build .\project\SamSWAT.FireSupport\SamSWAT.FireSupport.Core.csproj --configuration "SPT-4.0 Release" "-p:SptDir=C:/Path/To/SPT/" "-p:SptSharedAssembliesDir=C:/Path/To/SPT Assemblies/" "-p:SevenZipPath=C:/Tools/7-Zip/7z.exe"
```

The release ZIP must extract directly into the SPT root and contain:

- `BepInEx/plugins/Tylevo.TacticalServicesControl/`
- `SPT/user/mods/Tylevo.TacticalServicesControl/`
- Root release docs.

Do not include proprietary EFT assemblies, local profiles, logs, build caches, source-only prompt files, or local machine paths.
