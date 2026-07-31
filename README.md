# PC Build Companion

A Windows desktop app that combines PCPartPicker and BuildCores in one window,
with a toggle button to switch between them.

## How to run it

1. Make sure you have Visual Studio 2022 installed with the
   ".NET desktop development" workload (this is a checkbox in the VS Installer).
2. Double-click `PCBuildCompanion.csproj` — it opens directly in Visual Studio,
   no `.sln` file needed.
3. Press F5 (or click the green "Run" arrow). On first build, Visual Studio /
   NuGet will automatically download the WebView2 package it needs — this
   requires an internet connection the first time only.

That's it — the app window should open with PCPartPicker loaded by default.

## What it does

- Two buttons at the top ("PCPartPicker" / "BuildCores") swap the whole window
  between the two sites.
- Back / forward / reload buttons.
- Remembers the last page you were on for each site when you switch back.
- If a site ever fails to load (some sites try to block embedded browsers),
  you'll see a clear error screen with a button to open that page in your
  normal default browser instead.

## Parts List helper

Click the "Parts List" button in the top bar to open a side panel that helps
you move your build from PCPartPicker into BuildCores (e.g. to look at it in
BuildCores' 3D viewer) without retyping every part name.

How to use it:

1. On PCPartPicker, open your saved build/list.
2. Above the parts table, click the **"Markup"** button (this is a built-in
   PCPartPicker feature for exporting your own list as plain text).
3. Copy that text.
4. Switch to the app's Parts List panel and paste it into the box, then click
   **"Parse Parts List"**.
5. You'll get a checklist of every part, grouped by category, each with its
   own **Copy** button (or use **"Copy All Remaining"** to copy every
   not-yet-copied part at once, one per line).
6. Switch to BuildCores, paste into its search box, and click BuildCores' own
   "add" button yourself for each part.

This is intentionally a copy/paste helper, not an auto-import: you still
click "add" on BuildCores yourself for every part. Nothing in this app reads
PCPartPicker's page automatically or clicks anything on BuildCores' site —
it only reformats text you copied yourself from PCPartPicker's own export
feature, since automating either site isn't something either site allows.

## AI Assistant

Click **"AI Assistant"** in the top bar to open a chat panel for build advice
(compatibility, bottlenecks, upgrade suggestions, general questions).

- The first time you open it, click the **⚙** icon and paste in your own
  Google AI (Gemini) API key. Get a free one at
  [aistudio.google.com/apikey](https://aistudio.google.com/apikey), then
  click **Save**.
- The key is stored in a small local settings file at
  `%AppData%\PCBuildCompanion\ai-settings.json` on your PC — in plain text,
  not encrypted — and is sent directly from this app to Google's API over
  HTTPS. It never passes through any server of ours, since this app doesn't
  have one.
- If you've parsed a parts list in the Parts List panel, the assistant is
  given that list as context automatically, so you can ask things like "will
  this PSU be enough?" without retyping your build.
- The model box defaults to `gemini-flash-latest` (Google's alias for its
  current fast/cheap model). You can change it to a different Gemini model
  name if you prefer.
- Usage is billed by Google to your own API key according to their pricing;
  this app doesn't add any markup or handle payment.

## Files

- `Program.cs` — all the app code (window, buttons, browser control, logic).
- `PCBuildCompanion.csproj` — project file your IDE uses to know what to
  build and which packages to install. You shouldn't need to touch this,
  aside from the publish settings used below.
- `installer.iss` — Inno Setup script for building a distributable installer
  (see "Sharing this with other people" below).

## Requirements

- Windows 10 or 11
- An IDE with .NET support — Visual Studio 2022 (Community edition is free)
  with the ".NET desktop development" workload, or JetBrains Rider
- Microsoft Edge WebView2 Runtime — this comes pre-installed on almost all
  Windows 10/11 machines already, so you likely don't need to do anything here.

## Sharing this with other people

If you want other people to be able to install and run this without setting
up an IDE or having .NET installed, build a proper installer:

**Step 1 — Publish a self-contained .exe**

The `.csproj` is already configured to produce a self-contained single-file
executable (it bundles the .NET runtime inside the .exe, so users don't need
to install .NET separately). To build it:

- In Rider: open a terminal in the project folder (View > Tool Windows >
  Terminal) and run:
  ```
  dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
  ```
- In Visual Studio: right-click the project in Solution Explorer > Publish,
  or use the same command above in Developer PowerShell.

This creates `PCBuildCompanion.exe` inside
`bin\Release\net8.0-windows\win-x64\publish\`.

**Step 2 — Build the installer with Inno Setup**

1. Download and install Inno Setup (free): https://jrsoftware.org/isinfo.php
2. Open `installer.iss` in Inno Setup.
3. Click **Build > Compile** (or press F9).
4. `PCBuildCompanion-Setup.exe` will appear in a new `installer_output` folder.

That `PCBuildCompanion-Setup.exe` is the file you share. When someone runs
it, they get a normal Windows installer: a Start Menu entry, an optional
desktop shortcut, and a proper uninstaller listed in "Add or Remove
Programs."

**Note on WebView2:** almost all Windows 10/11 PCs already have the WebView2
Runtime installed (Microsoft ships it as part of Windows updates), so most
people won't need to do anything extra. If someone runs the installer and
the app shows a "WebView2 Runtime not found" screen, they just need to grab
it from Microsoft's WebView2 Runtime download page — a one-time, few-second
install.

**Note on SmartScreen:** since this isn't code-signed with a paid certificate,
Windows may show a "Windows protected your PC" SmartScreen warning the first
time someone runs the installer. This is normal for small/free apps without
a paid code-signing certificate — clicking "More info" > "Run anyway" gets
past it. It's not a sign anything is wrong with the app.
