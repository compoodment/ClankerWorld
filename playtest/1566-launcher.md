# Launcher

- On a Windows 11 x64 PC, download the `clankerworld-launcher-windows-11-x64` CI artifact, unzip it and start `ClankerWorldLauncher.exe`. A small window opens with the game's logo, valley and buttons, not the game's Main Menu. ([#1566](https://github.com/ClankerWorldOrg/ClankerWorld/issues/1566))
- With no published release yet, the launcher says no game versions are published. After a release with its zip and `.sha256` is published, **Play** reads "Install and Play <version>", downloads it with a progress bar, and opens that version's Main Menu. The launcher closes. ([#1566](https://github.com/ClankerWorldOrg/ClankerWorld/issues/1566))
- `%LOCALAPPDATA%\ClankerWorld\versions\<version>` holds the game, and `saves`, `settings` and `logs` are unchanged beside it. ([#1566](https://github.com/ClankerWorldOrg/ClankerWorld/issues/1566))
- On **Versions**, edit a file inside the installed version's `host` folder, then choose **Repair**. It downloads the version again and the file is restored. **Remove** asks first, removes only the version folder and keeps your worlds. ([#1566](https://github.com/ClankerWorldOrg/ClankerWorld/issues/1566))
- Turn off the network and start the launcher. It says it couldn't check for updates, and **Play** still starts an installed version. ([#1566](https://github.com/ClankerWorldOrg/ClankerWorld/issues/1566))
- **Open saves folder** and **Open logs folder** open those folders in Explorer. **Developer mode** stays on after closing and reopening the launcher. ([#1566](https://github.com/ClankerWorldOrg/ClankerWorld/issues/1566))
