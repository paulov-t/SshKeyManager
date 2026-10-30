# SSH Key Manager

A small desktop app for managing the keys in your local `~/.ssh` folder. It reads, creates,
tests and backs up SSH keys through the OpenSSH tools you already have installed, so the files
it produces work with `ssh`, `git`, VS Code and everything else exactly as if you had run
`ssh-keygen` yourself.

Built with .NET 10 and [Avalonia](https://avaloniaui.net/) 12, so it runs as a native desktop
app on Windows, Linux and macOS.

## Features

| Area | What you can do |
| --- | --- |
| **Keys** | See every keypair in `~/.ssh` with algorithm, size, comment, SHA256 and MD5 fingerprints, file size and modification date. Orphan `.pub` files are listed too. |
| **Create** | Generate Ed25519 (default), RSA (2048/3072/4096) or ECDSA (256/384/521) keys with a comment and an optional passphrase. Optionally load the new key into `ssh-agent`. |
| **Public keys** | Copy the public key or fingerprint to the clipboard, or export the `.pub` file anywhere. |
| **Test** | Run `ssh -T` against GitHub, GitLab, Bitbucket, Codeberg or a custom target using only the selected key, and see whether the remote accepted it. |
| **ssh-agent** | See which keys are loaded, add the selected key (passphrase is sent over stdin), remove it again, and start the Windows agent service with guidance if it is disabled. |
| **SSH config** | Visual editor for `~/.ssh/config` host blocks: alias, host name, user, port, identity file and `IdentitiesOnly`. Unknown directives and comments are preserved, and every save keeps a timestamped backup. A raw text editor handles anything the form does not cover. |
| **Backup / delete** | Create a timestamped zip of the whole `.ssh` folder on demand, and every deletion writes such a backup first. |
| **Quality of life** | Search, light/dark/system theme (remembered), activity log, and a graceful warning when the OpenSSH client or `ssh-agent` is missing. |

## Requirements

- **.NET 10 SDK** to build, or a published build to run.
- **OpenSSH client** (`ssh-keygen`, `ssh-add`, `ssh`) on `PATH`.
  - Windows: Settings → System → Optional features → *OpenSSH Client*.
- **ssh-agent** is optional. On Windows it is the *OpenSSH Authentication Agent* service:

  ```powershell
  # Run PowerShell as administrator once:
  Set-Service ssh-agent -StartupType Automatic
  Start-Service ssh-agent
  ssh-add -l
  ```

  Linux/macOS: start one with `eval "$(ssh-agent -s)"` or use your desktop keyring.

## Build and run

```powershell
dotnet run --project src/SshKeyManager
```

Run the tests (they create throwaway keys in your temp folder, never in `~/.ssh`):

```powershell
dotnet test
```

## Publish

Framework-dependent build:

```powershell
dotnet publish src/SshKeyManager -c Release -o artifacts/publish
```

Self-contained single file for Windows:

```powershell
dotnet publish src/SshKeyManager -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o artifacts/win-x64
```

Or use the helper script:

```powershell
./publish.ps1                 # framework-dependent -> artifacts/publish
./publish.ps1 -SelfContained  # single-file win-x64  -> artifacts/win-x64
```

## Safety notes

- **Nothing is written to `~/.ssh` without an explicit action.** The app only reads the folder
  when it starts or refreshes.
- **Deleting a key** first writes a zip of the whole `.ssh` folder to
  `%USERPROFILE%\.ssh-backups\ssh-backup-<timestamp>.zip`, then removes the private key, its
  `.pub` file and any matching `ssh-agent` identity.
- **Saving the config** keeps the previous version as `config.bak-<timestamp>` next to the file.
  Those backups are excluded from the key list.
- **Passphrases** for key generation are passed to `ssh-keygen` with `-N`, which means they are
  briefly visible in the local process list. If that matters, generate the key with an empty
  passphrase and add one afterwards with `ssh-keygen -p -f ~/.ssh/<name>`. When adding a key to
  `ssh-agent`, the passphrase is written to `ssh-add`'s stdin and is never stored.
- **Testing a key** uses `BatchMode=yes`, so it never blocks on a passphrase prompt. If the key
  is passphrase protected, load it into `ssh-agent` first.
- **ssh-agent passphrase forwarding** relies on `ssh-add` reading a redirected stdin, which it
  does when no terminal is attached. If your build of `ssh-add` refuses, the app shows the raw
  `ssh-add` error; run `ssh-add path\to\key` in a terminal instead.
- **Match blocks** cannot be represented by the form, so the app refuses to rewrite such a file
  and points you at the raw editor instead.

## Project layout

```
SshKeyManager.slnx
src/SshKeyManager
  Models/        SshKeyInfo, SshHostEntry, key-generation options
  Services/      ssh-keygen/ssh-add/ssh process wrappers, key + config services,
                 backup, connection tester, dialogs, settings, crash log
  ViewModels/    Main window (keys + config) and the create-key dialog
  Views/         Avalonia windows: main, create key, prompt, message, raw config
tests/SshKeyManager.Tests
                 xUnit tests that exercise real ssh-keygen output in temp folders
```

Unexpected errors are appended to `%APPDATA%\SshKeyManager\error.log`.

## Notes and limitations

- The key scanner treats anything `ssh-keygen` can read as a key; unrelated files are ignored.
- Comments that sit directly above a `Host` line are preserved but may move inside the previous
  host block when the form saves the file. Use the raw editor if exact formatting matters.
- Key generation always writes into `~/.ssh` (no custom folder picker) to keep the list coherent.
- The app does not modify `authorized_keys` or `known_hosts`.
