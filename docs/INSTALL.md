# Install & usage guide

[← Back to README](../README.md) · [Tiếng Việt](INSTALL.vi.md)

- [1. Requirements](#1-requirements)
- [2. Download](#2-download)
- [3. First launch](#3-first-launch)
- [4. Sign in with your Apple ID](#4-sign-in-with-your-apple-id)
- [5. Search and download an IPA](#5-search-and-download-an-ipa)
- [6. Download an older version](#6-download-an-older-version)
- [7. Owned apps](#7-owned-apps)
- [8. Language & theme](#8-language--theme)
- [9. Updates](#9-updates)
- [10. Using the ipatool CLI with the same session](#10-using-the-ipatool-cli-with-the-same-session)
- [11. Uninstall](#11-uninstall)
- [Troubleshooting](#troubleshooting)

---

## 1. Requirements

| | |
|---|---|
| OS | Windows 10 version 1903 or later, or Windows 11 (x64 / ARM64) |
| Software to install | None: .NET Framework 4.8 is part of Windows |
| Account | An Apple ID that has used the App Store (and accepted Apple's latest terms) |
| Network | Access to `github.com`, `api.github.com`, `itunes.apple.com` and Apple's App Store servers |

## 2. Download

1. Open the [Releases](https://github.com/DuyTaDinh/ipatool-gui/releases/latest) page and download **`IpaToolGui.exe`**.
2. Put it anywhere, e.g. `D:\Tools\IpaToolGui.exe`. There's no installer: nothing is written to Program Files or the registry.
3. *(Optional)* Check that the file is genuine:
   - Compare the checksum with `IpaToolGui.exe.sha256` from the release:

     ```powershell
     (Get-FileHash .\IpaToolGui.exe -Algorithm SHA256).Hash
     ```

   - Or verify the build provenance (proves the file was built by this repository's GitHub Actions), using the [GitHub CLI](https://cli.github.com/):

     ```powershell
     gh attestation verify .\IpaToolGui.exe --repo DuyTaDinh/ipatool-gui
     ```

## 3. First launch

1. Run `IpaToolGui.exe`.
2. If Windows shows **"Windows protected your PC"** (SmartScreen), click **More info → Run anyway**.
   The warning appears because the exe isn't code-signed yet (certificates cost money), not because it contains malware. All source code is in this repository and every release is built by GitHub Actions.
   > If *Run anyway* doesn't show up for a file downloaded through a browser: right-click the file → **Properties** → tick **Unblock** → **OK**.
3. The **Preparing ipatool** screen appears. The app downloads the latest ipatool (~27 MB) from GitHub and verifies its SHA-256.
   - If your network blocks GitHub: click **Choose an existing ipatool.exe...** and pick an `ipatool-x.y.z-windows-amd64.exe` you downloaded from [majd/ipatool/releases](https://github.com/majd/ipatool/releases) (extract the `.tar.gz` first).

## 4. Sign in with your Apple ID

1. Click **Sign in** in the bottom-left corner.
2. Enter your Apple ID (email or phone number) and password, then click **Sign in**.
   - The **first** sign-in can take 1–2 minutes while ipatool sets up its device information.
3. If two-factor authentication is on, Apple sends a prompt to your iPhone/iPad/Mac:
   tap **Allow** on the device, enter the **6-digit code** in the app and click **Verify code**.
4. Once signed in, your name and email appear in the bottom-left corner. You stay signed in the next time you open the app.

> **Tip:** lookups by **bundle ID** or **App ID** (info and screenshots) work without signing in.

## 5. Search and download an IPA

1. In **Search & download**, type one of:
   - an app name: `Wikipedia` (requires sign-in)
   - a bundle ID: `org.wikimedia.wikipedia`
   - an App ID (the number in an App Store link such as `apps.apple.com/.../id324715238`): `324715238`
2. Pick a **platform** (iPhone/iPad by default) and the number of **results** if needed, then press **Enter**.
3. Click an app to open its **details**: screenshots, description, what's new and **Open in the App Store**.
4. Click **Download IPA**. The app joins the queue in **Downloads**, with a progress bar.
5. When it's done, click **Show in folder** to find `bundleID_appID_version.ipa`.

The default folder is `%USERPROFILE%\Downloads\IPA`; change it in **Settings → IPA download folder**.

### Apps you don't own yet
- **Free apps:** with **Settings → Automatically get licenses for free apps** on (the default), the app "Gets" them for you, like the App Store's GET button.
- **Paid apps:** buy them on an iPhone/Mac with that Apple ID first. IPA Tool GUI **never** pays for anything.

### Cancel / resume
Click **Cancel** to stop; **Retry** resumes from where it stopped instead of starting over.

## 6. Download an older version

1. Click **Older versions** on an app card (or in the details view).
2. Every version Apple still has is listed, **newest first**. The first few show their version number and release date automatically;
   click **Show version number** or **Get version numbers for the next 10** for more (about 1–3 seconds each).
3. Click **Download this** on the version you need.

## 7. Owned apps

**Owned apps** lists everything this Apple ID has downloaded or bought (50 per page). Use it to re-download IPAs, including apps that were removed from the App Store, as long as Apple still has them.

## 8. Language & theme

Open **Settings → Appearance**:

- **Language:** *Auto (Windows)* (default) follows your Windows display language and falls back to English. Or pick Tiếng Việt, English, 简体中文, 한국어 or Türkçe. The change applies immediately.
- **Theme:** *System* (default) follows Windows' light/dark setting, including the window title bar. Or force **Dark** or **Light**.

## 9. Updates

- **ipatool:** checked and updated every time the app opens. To check manually, use the ⟳ button in the bottom-left corner or **Settings → Check & update**.
- **IPA Tool GUI:** when a new release is published on GitHub, the app shows a notice with a **Download update** button in **Settings → About**. Download the new exe and replace the old one; your settings and session are kept.

To turn off automatic checks, untick **Settings → Check for and install ipatool updates every time the app opens**.

## 10. Using the ipatool CLI with the same session

On Windows, ipatool encrypts its session with a *keychain passphrase*. The app generates a random one and protects it with Windows DPAPI.

- **Use the CLI with the app's session:** **Settings → Copy passphrase**, then:

  ```powershell
  & "$env:LOCALAPPDATA\IpaToolGui\bin\ipatool.exe" search "wikipedia" --keychain-passphrase "<passphrase>"
  ```

- **You signed in with the CLI before, using your own passphrase:** **Settings → Use another passphrase...**, enter it, then **Save & check**.

## 11. Uninstall

1. Delete `IpaToolGui.exe`.
2. Delete `%LOCALAPPDATA%\IpaToolGui` (ipatool and settings).
3. *(Optional)* Sign out in the app first, or delete `%USERPROFILE%\.ipatool` to remove the session.
4. Downloaded IPA files stay in `Downloads\IPA`; keep or delete them as you like.

---

## Troubleshooting

| Symptom | Common cause | What to do |
|---|---|---|
| `HTTP 403` / `unexpected response from Apple` at sign-in | Outdated ipatool, or Apple temporarily blocking | **Settings → Check & update**; wait 15–30 minutes and retry; try another network |
| No 2FA code field appears | Apple didn't require 2FA this time | Normal: sign-in still succeeds |
| `apple did not complete verification` | The 2FA code expired | Click **‹ Change account** and sign in again to get a new code |
| `account is disabled` | The Apple ID is locked | Unlock it at [iforgot.apple.com](https://iforgot.apple.com) |
| "Not signed in" after using the CLI | Different keychain passphrase | Sign in again, or enter the old passphrase (section 10) |
| `license is required` | The Apple ID doesn't own the app | Turn on **Automatically get licenses** (free apps) / buy it on a device (paid apps) |
| `purchasing paid apps is not supported` | Paid app not bought yet | Buy it on an iPhone/Mac with this Apple ID |
| `app not found` | Wrong bundle ID, or the app isn't sold in the Apple ID's country | Check the ID; use an Apple ID from the app's country |
| No icon / screenshots | The app isn't available in the selected country | Change **Settings → App Store country** (e.g. `us`) |
| ipatool can't be downloaded on first launch | Network/proxy blocks GitHub | Use **Choose an existing ipatool.exe...** (section 3) |
| `429 Too Many Requests` | Too many requests | Wait a few minutes |

Still stuck? Open **Log → Copy** and create an [issue](https://github.com/DuyTaDinh/ipatool-gui/issues/new/choose) with the log. Passwords and 2FA codes are hidden, but remove your email if you don't want it public.
