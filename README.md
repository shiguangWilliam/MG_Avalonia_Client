# Important Notice

This project is modified from the source code of the [CnCNet xna-cncnet-client](https://github.com/CnCNet/xna-cncnet-client.git) v2.12.18 release.

# CnCNet Client

The MonoGame / XNA CnCNet client, a platform for playing classic Command & Conquer games and their mods both online and offline. Supports setting up and launching both singleplayer and multiplayer games with [a CnCNet game spawner](https://github.com/CnCNet/ts-patches). Includes an IRC-based chat client with advanced features like private messaging, a friend list, a configurable game lobby, flexible and moddable UI graphics, and extras like game setting configuration and keeping track of match statistics. And much more!


You can find the [dedicated project development chat](https://discord.gg/M5gGdBYG5m) at C&C Mod Haven Discord server.

## Targets

The primary targets of the client project are
* [Dawn of the Tiberium Age](https://www.moddb.com/mods/the-dawn-of-the-tiberium-age)
* [Twisted Insurrection](https://www.moddb.com/mods/twisted-insurrection)
* [Mental Omega](https://www.moddb.com/mods/mental-omega)
* [CnCNet Yuri's Revenge](https://cncnet.org/yuris-revenge)

However, there is no limitation in the client that would prevent incorporating it into other projects. Any game or mod project that utilizes the CnCNet spawner for Tiberian Sun and Red Alert 2 can be supported. Several other projects also use the client or an unofficial fork of it, including [Tiberian Sun Client](https://www.moddb.com/mods/tiberian-sun-client), [Project Phantom](https://www.moddb.com/mods/project-phantom), [YR Red-Resurrection](https://www.moddb.com/mods/yr-red-resurrection), [The Second Tiberium War](https://www.moddb.com/mods/the-second-tiberium-war) and [CnC: Final War](https://www.moddb.com/mods/cncfinalwar).

## Development requirements

The client has 2 variants: .NET 4.8 and .NET 8.0.
* Both variants have 3 builds: Windows DirectX11, Windows OpenGL and Windows XNA.
* .NET 8.0 in addition has a cross-platform Universal OpenGL build.
* The DirectX11 and OpenGL builds rely on MonoGame.
* The XNA build relies on Microsoft's XNA Framework 4.0 Refresh.

To build this project, you must use Git to clone the repository, instead of downloading a ZIP archive. After cloning, make sure to initialize and update the submodules using the following command:
```shell
git submodule update --init --recursive
```

Building the solution for **any** platform requires the .NET SDK 10.0.100. Editing the source code requires Visual Studio 2026 or newer, or Rider 2025.3 or newer. A modern version of Visual Studio Code also works, but is not officially supported.
To debug WindowsXNA builds the .NET SDK 10.0 x86 is additionally required.
When using the included build scripts PowerShell 7.2 or newer is required.[^install-powershell]

## Compiling and debugging

* Compiling itself is simple: assuming you have the .NET SDK 10.0 installed, you can just open the solution with Visual Studio and compile it right away.
* When built as a debug build, the client executable expects to reside in the same directory with the target project's main game executable. Resources should exist in a "Resources" sub-directory in the same directory. The repository contains sample resources and post-build commands for copying them so that you can immediately run the client in debug mode by just hitting the Debug button in Visual Studio.
* When built in release mode, the client executables expect to reside in the `Resources` sub-directory itself for .NET 4.8, named `clientdx.exe`, `clientogl.exe` and `clientxna.exe`. Each `.exe` file or `.dll` file expects a `.pdb` file for diagnostics purpose. It's advised not to delete these `.pdb` files. Keep all `.pdb` files even for end users.
* The `Scripts` directory has automated build scripts that build the client for all platforms and copy the output files to a folder named `Compiled` in the project root. You can then copy the contents of this `Compiled` directory into the `Resources` sub-directory of any target project.

<details>
  <summary>.NET 8 builds</summary>

* For .NET 8, When built in release mode, the client executables expect to reside in `Resources/BinariesNET8/{Windows, OpenGL, UniversalGL, XNA}` folders, named `client{dx, ogl, ogl, xna}.dll`, respectively. Note that `client{dx, ogl, ogl, xna}.runtimeconfig.json` files are required for the corresponding .NET 8 dlls.
* When built on an OS other than Windows, only the Universal OpenGL build is available.
</details>

<details>
  <summary>Development workarounds</summary>

* If you switch among different solution configurations in Visual Studio (e.g. switch to `UniversalGLRelease` from `WindowsDXDebug`), especially switching between .NET 4.8 and .NET 8.0 variants, it is recommended to restart Visual Studio after switching configurations to prevent unexpected error messages. If restarting Visual Studio do not work as intended, try deleting all `obj` folders in each project. Due to the same reason, it is highly advised to close Visual Studio when building the client using the scripts in `Scripts` folder.
* Some dependencies are stored in `References` folder instead of the official NuGet source. This folder is also useful if you are working on modifying a dependency and debugging in your local machine without publishing the modification to NuGet. However, if you have replaced the `.(s)nupkg` files of a package, without altering the package version, be sure to remove the corresponding package from `%USERPROFILE%\.nuget\packages` folder (Windows) to purge the old version. 

Refer to [Docs/Build.md](/Docs/Build.md) for more information about building the client.

</details>

## ClientAvalonia (Avalonia UI)

This fork adds **ClientAvalonia**, a .NET 8 Avalonia-based launcher for MG and other CnCNet mods. It publishes a **single-file** `ClientAvalonia.exe` (self-contained, Windows x64).

> Before running scripts in `Scripts/`, close Visual Studio to avoid file locks on `obj/` / output folders.

### Requirements

* [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (build)
* PowerShell 5.1 or newer (`powershell.exe`; `pwsh` also works)
* Windows x64 (runtime target)
* An installed MG (or mod) game root with `Resources/ThemeMG/`, `gamemd.exe`, and existing CnCNet config

### Compile

From the repository root:

```powershell
.\Scripts\build-clientavalonia.ps1
```

This will:

1. Build `ClientCore` and `ClientUpdater` (Release, net8.0)
2. Publish `ClientAvalonia` as a single-file exe to **`CompiledAvalonia/`**
3. Stage fallback `Resources/DTA/` INI into `CompiledAvalonia/Resources/`
4. Mirror the same bundle to **`ClientAvalonia/publish/`**
5. Run a headless `MainMenu.ini` validation (unless skipped)

Useful flags:

| Flag | Purpose |
|------|---------|
| `-IsDebug` | Local dev only: multi-file publish (not for deploy) |
| `-NoClean` | Incremental rebuild, keep previous output |
| `-SkipValidate` | Skip INI smoke test after publish |
| `-SkipWorkspaceMirror` | Do not copy to `ClientAvalonia/publish/` |

Quick compile without cleaning:

```powershell
.\Scripts\build-clientavalonia.ps1 -SkipValidate -NoClean
```

Output to run locally:

```powershell
cd CompiledAvalonia
.\ClientAvalonia.exe
```

Set the game root with `--game-root` if the exe is not inside the mod folder.

### Deploy (test folder)

To copy **only** `ClientAvalonia.exe` into an existing MG install (does **not** overwrite `Resources/`, maps, or INI):

```powershell
.\Scripts\build-clientavalonia.ps1 -DeployTo "D:\MG\MG-Avalonia测试区2" -SkipValidate
```

Or copy manually after a normal build:

```powershell
Copy-Item -Force .\CompiledAvalonia\ClientAvalonia.exe "D:\MG\MG-Avalonia测试区2\"
```

**Important:** close `ClientAvalonia.exe` before copying, or Windows will lock the file.

The deploy step does **not** update `ClientDefinitions.ini`. For MG, merge or replace that file separately (see packaging below).

### Package (patch zip)

Two packaging scripts write to **`Dist/`**:

#### MG patch (recommended for Moment of Genesis)

Includes `ClientAvalonia.exe` + `Resources\ClientDefinitions.ini` (MG / YR / CnCNet R10).

```powershell
.\Scripts\package-mg-avalonia-patch.ps1
```

Optional:

```powershell
# Reuse an existing build
.\Scripts\package-mg-avalonia-patch.ps1 -SkipBuild

# Custom ClientDefinitions.ini (e.g. from your test install)
.\Scripts\package-mg-avalonia-patch.ps1 -ClientDefinitionsIni "D:\MG\my-test\Resources\ClientDefinitions.ini"
```

Default INI template: `Packaging/MG-Avalonia/ClientDefinitions.ini`

Output:

* `Dist/MG-Avalonia-Patch-<yyyyMMdd-HHmm>/` — folder
* `Dist/MG-Avalonia-Patch-<yyyyMMdd-HHmm>.zip` — distribute this

Install: extract the zip into the **MG game root** (same folder as `gamemd.exe`). See `PATCH_README.txt` inside the zip.

#### Generic DTA lobby patch

Includes exe + `Resources/DTA/` lobby INI fallbacks (does not touch `ClientDefinitions.ini` or ThemeMG):

```powershell
.\Scripts\package-clientavalonia-patch.ps1
```

Output: `Dist/ClientAvalonia-Patch-<timestamp>.zip`

### Patch contents (what gets overwritten)

| Package | Overwrites | Does not overwrite |
|---------|------------|-------------------|
| MG-Avalonia-Patch | `ClientAvalonia.exe`, `Resources\ClientDefinitions.ini` | `ThemeMG/`, `DTA/`, `GameCollectionConfig.ini`, game INI/MIX |
| ClientAvalonia-Patch | `ClientAvalonia.exe`, `Resources/DTA/*.ini` (listed lobby INIs) | `ThemeMG/`, `ClientDefinitions.ini`, `GameCollectionConfig.ini` |

### Troubleshooting

* **Copy failed / file in use** — exit ClientAvalonia and MGLauncher, then retry deploy or extract the zip again.
* **Empty lobby / wrong game** — check `Resources\ClientDefinitions.ini` (`LocalGame`, `CnCNetProtocolRevision`) and `Resources/GameCollectionConfig.ini`.
* **Missing UI windows** — ensure `Resources/ThemeMG/` exists; optionally add `package-clientavalonia-patch` DTA INIs as fallback.

See also [Scripts/README.md](/Scripts/README.md) for the legacy XNA client build scripts.

## End-user usage

* Windows: Windows 7 SP1 or higher is required. The preferred build is DirectX11 (.NET 4.8), i.e., `clientdx.exe`. If your GPU does not support DX11, consider using the OpenGL or XNA build instead. Advanced users may experiment with the .NET 8 builds at their discretion.
* Other OS: Use the Universal OpenGL build.

## End-user requirements

### Windows .NET 4.8 requirements:

* The [.NET Framework 4.8 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet-framework/thank-you/net48-web-installer)

(Optional) The XNA build requires:
* [Microsoft XNA Framework Redistributable 4.0 Refresh](https://www.microsoft.com/en-us/download/details.aspx?id=27598).

### Linux requirements:

* The [.NET 8.0 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0/runtime?initial-os=linux) for your specific platform.

### macOS requirements:

* The [.NET 8.0 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0/runtime?initial-os=macos) for your specific platform.

### Windows .NET 8.0 requirements:

<details>
  <summary>Windows .NET 8.0 requirements</summary>

* The [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0/runtime?initial-os=windows) for your specific platform.

(Optional) The XNA build requires:
* [Microsoft XNA Framework Redistributable 4.0 Refresh](https://www.microsoft.com/en-us/download/details.aspx?id=27598).
* [.NET 8.0 Desktop Runtime x86](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-8.0.0-windows-x86-installer).

Windows 7 SP1 and Windows 8.x additionally require:
* Microsoft Visual C++ 2015-2019 Redistributable [64-bit](https://aka.ms/vs/16/release/vc_redist.x64.exe) / [32-bit](https://aka.ms/vs/16/release/vc_redist.x86.exe). Note: the latest version of this redistributable is named "Microsoft Visual C++ 2015-2026 Redistributable", available [here](https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist). We recommend using the latest version instead of the 2015-2019 version.

Windows 7 SP1 additionally requires:
* KB3063858 [64-bit](https://www.microsoft.com/download/details.aspx?id=47442) / [32-bit](https://www.microsoft.com/download/details.aspx?id=47409).
</details>

## Client launcher

This repository does not contain the client launcher (for example, `DTA.exe` in Dawn of the Tiberium Age) that selects which platform's client executable is most suitable for each user's system.
See [xna-cncnet-client-launcher](https://github.com/CnCNet/xna-cncnet-client-launcher).

## Branches

Currently there are only two major active branches. `develop` is where development happens, and while things should be fairly stable, occasionally there can also be bugs. If you want stability and reliability, the `master` branch is recommended.

## Screenshots

![Screenshot](cncnetchatlobby.png?raw=true "CnCNet IRC Chat Lobby")
![Screenshot](cncnetgamelobby.png?raw=true "CnCNet Game Lobby")

## License

CnCNet Client
Copyright (C) 2013-2026 CnCNet, Rampastring

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.

### Additional permission under GNU GPL version 3 section 7

If you modify this program, or any covered work, by linking or combining it with the Steamworks SDK (or a modified version of that library), containing parts covered by the terms of the Steamworks SDK's license, the licensors of this program grant you additional permission to convey the resulting work.

Sponsored by
------------
<a href="https://www.digitalocean.com/?refcode=337544e2ec7b&utm_campaign=Referral_Invite&utm_medium=opensource&utm_source=CnCNet" title="Powered by Digital Ocean" target="_blank">
    <img src="https://opensource.nyc3.cdn.digitaloceanspaces.com/attribution/assets/PoweredByDO/DO_Powered_by_Badge_blue.svg" width="201px" alt="Powered By Digital Ocean" />
</a>


[^install-powershell]: [How To Install PowerShell Core](https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-windows)
