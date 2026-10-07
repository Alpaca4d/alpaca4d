# Alpaca4d Package Manager Publishing

This directory contains the necessary files and scripts to publish the Alpaca4d plugin to the McNeel Package Manager using the Yak CLI tool.

## Prerequisites

Before publishing, ensure you have:

1. **Yak CLI Tool**: Install the Yak command line tool
   - Download from: https://developer.rhino3d.com/guides/yak/yak-cli-reference/
   - On macOS: Available at `/Applications/Rhino 8.app/Contents/Resources/bin/yak`
   - On Windows: Available at `C:\Program Files\Rhino 8\System\yak.exe`

2. **Authentication**: Login to your Rhino account
   ```bash
   yak login
   ```
   This will create a token file at:
   - macOS: `~/.mcneel/yak.yml`
   - Windows: `%appdata%\McNeel\yak.yml`

3. **Build Environment**: Ensure you can build the Alpaca4d.Gh project
   
   **On Windows:**
   ```bash
   dotnet build Alpaca4d.Gh/Alpaca4d.Gh.csproj --configuration Release
   ```
   
   **On macOS:** Since the project targets .NET Framework 4.8, you need Mono with MSBuild:
   ```bash
   brew install mono
   msbuild Alpaca4d.sln /p:Configuration=Release /p:Platform="Any CPU"
   ```

## Files Structure

```
YAK/
├── manifest.yml          # Package manifest with metadata
├── icon.png             # Package icon
├── publish.sh           # Publishing script for macOS/Linux
├── publish.bat          # Publishing script for Windows
├── README.md            # This file
└── [version]/           # Version-specific directories (created during publishing)
    ├── *.dll            # Plugin dependencies
    ├── Alpaca4d.Gh.gha  # Main Grasshopper assembly
    ├── data.bin         # License data
    └── UserObject/      # User objects
```

## Publishing Process

The publishing scripts automate the following steps:

1. **Version Management**: Updates the version number in `manifest.yml`
2. **Project Build**: Builds the Alpaca4d.Gh project in Release configuration
3. **File Collection**: Copies all necessary files to a version-specific directory
4. **Package Creation**: Uses `yak build` to create the package
5. **Package Upload**: Uses `yak push` to upload to the Package Manager
6. **Cleanup**: Removes temporary files

## Usage

### macOS/Linux
```bash
cd YAK
./publish.sh
```

### Windows
```cmd
cd YAK
publish.bat
```

## Manual Publishing (Alternative)

If you prefer to publish manually:

1. **Update version** in `manifest.yml`:
   ```yaml
   version: 1.2.3
   ```

2. **Build the project**:
   ```bash
   dotnet build Alpaca4d.Gh/Alpaca4d.Gh.csproj --configuration Release
   ```

3. **Copy files** to a version directory:
   ```bash
   mkdir YAK/1.2.3
   cp Alpaca4d.Gh/bin/Release/*.dll YAK/1.2.3/
   cp Alpaca4d.Gh/bin/Release/Alpaca4d.Gh.gha YAK/1.2.3/
   cp Alpaca4d.Gh/bin/Release/data.bin YAK/1.2.3/
   cp -r Alpaca4d.Gh/bin/Release/UserObject YAK/1.2.3/
   ```

4. **Build package**:
   ```bash
   cd YAK
   yak build --platform any
   ```

5. **Push package**:
   ```bash
   yak push Alpaca4d-1.2.3-any-any.yak
   ```

## Files Included in Package

The following files are automatically collected and included in the package:

- **DLLs**: All dependency libraries
- **Alpaca4d.Gh.gha**: Main Grasshopper assembly
- **data.bin**: License and configuration data
- **UserObject/**: Grasshopper user objects

OpenSees is **not** included. Users install it themselves and point Alpaca4d at it with
**Alpaca4d → Settings → Set OpenSees Executable**, which is the only solver Alpaca4d runs. The
project file keeps `OpenSees-Solvers` out of the build output, and `publish.py` removes it from
a version folder an older script filled.

## Version Management

The scripts will prompt you for a new version number. If you press Enter without entering a version, it will keep the current version from the manifest.

Version format should follow semantic versioning: `major.minor.patch` (e.g., `1.2.3`)

## Troubleshooting

### Common Issues

1. **"Yak command not found"**
   - Install the Yak CLI tool from the official download page
   - Ensure it's in your system PATH

2. **"Authentication token not found"**
   - Run `yak login` to authenticate with your Rhino account
   - Check that the token file exists in the expected location

3. **"Project build failed"**
   - Ensure all dependencies are installed
   - Check that the project builds successfully with `dotnet build`

4. **"Failed to push package"**
   - Verify your authentication is still valid
   - Check that you have permission to publish to the package name
   - Ensure the package name in manifest.yml is unique

### Getting Help

- Yak CLI Reference: https://developer.rhino3d.com/guides/yak/yak-cli-reference/
- Package Manager Documentation: https://developer.rhino3d.com/guides/yak/

## Package Manifest

The `manifest.yml` file contains the package metadata:

```yaml
---
name: Alpaca4d
version: 0.0.6
authors:
- Marco Pellegrino
description: Alpaca4d is a Grasshopper plugin which has been developed on top of OpenSees. It lets you analyse beam, shell and brick elements through Static, Dynamic, Linear and Not Linear Analysis.
url: https://alpaca4d.github.io/
icon: icon.png
```

Update this file as needed before publishing new versions.
