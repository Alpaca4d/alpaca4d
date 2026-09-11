#!/usr/bin/env python3

import argparse
import os
import re
import shutil
import subprocess
import sys
from glob import glob
from pathlib import Path


# Colors
RED = "\033[0;31m"
GREEN = "\033[0;32m"
BLUE = "\033[0;34m"
NC = "\033[0m"


def print_status(message: str) -> None:
    print(f"{BLUE}[INFO]{NC} {message}")


def print_success(message: str) -> None:
    print(f"{GREEN}[SUCCESS]{NC} {message}")


def print_error(message: str) -> None:
    print(f"{RED}[ERROR]{NC} {message}")


def run(cmd, cwd=None, check=True) -> subprocess.CompletedProcess:
    print_status(f"Running: {' '.join(cmd)}")
    return subprocess.run(cmd, cwd=cwd, check=check)


def project_paths() -> dict:
    script_path = Path(__file__).resolve()
    project_root = script_path.parent.parent
    yak_dir = project_root / "YAK"
    manifest_file = yak_dir / "manifest.yml"
    icon_file = yak_dir / "icon.png"
    csproj = project_root / "Alpaca4d.Gh" / "Alpaca4d.Gh.csproj"
    output_dir = project_root / "Alpaca4d.Gh" / "bin" / "Release"
    return {
        "project_root": project_root,
        "yak_dir": yak_dir,
        "manifest_file": manifest_file,
        "icon_file": icon_file,
        "csproj": csproj,
        "output_dir": output_dir,
    }


def get_current_version(manifest_path: Path) -> str:
    if not manifest_path.exists():
        raise FileNotFoundError(f"Manifest not found: {manifest_path}")
    with manifest_path.open("r", encoding="utf-8") as f:
        for line in f:
            if line.startswith("version:"):
                return line.strip().split(":", 1)[1].strip()
    raise RuntimeError("Could not find 'version:' in manifest.yml")


def update_version(manifest_path: Path, new_version: str) -> None:
    content = manifest_path.read_text(encoding="utf-8")
    new_content = re.sub(r"^version:\s*.*$", f"version: {new_version}", content, flags=re.MULTILINE)
    manifest_path.write_text(new_content, encoding="utf-8")
    print_success(f"Version updated to {new_version}")


def detect_output_dir(base_output_dir: Path) -> Path:
    net48_dir = base_output_dir / "net48"
    return net48_dir if net48_dir.is_dir() else base_output_dir


def safe_copy_file(src: Path, dst_dir: Path) -> None:
    if src.exists():
        dst_dir.mkdir(parents=True, exist_ok=True)
        shutil.copy2(str(src), str(dst_dir / src.name))
    else:
        print_status(f"Skipping missing file: {src}")


def safe_copy_tree(src: Path, dst: Path) -> None:
    if src.exists():
        if dst.exists():
            shutil.rmtree(dst)
        shutil.copytree(src, dst)
    else:
        print_status(f"Skipping missing directory: {src}")


def ensure_yak_available() -> None:
    try:
        run(["yak", "--version"], check=True)
    except (subprocess.CalledProcessError, FileNotFoundError):
        print_error("Yak CLI not found. Install from https://developer.rhino3d.com/guides/yak/yak-cli-reference/")
        sys.exit(1)


def build_project(paths: dict) -> None:
    project_root: Path = paths["project_root"]
    csproj: Path = paths["csproj"]
    if sys.platform.startswith("win"):
        cmd = [
            "dotnet",
            "build",
            str(csproj),
            "--configuration",
            "Release",
        ]
    else:
        cmd = [
            "msbuild",
            str(csproj),
            "/p:Configuration=Release",
            "/p:Platform=AnyCPU",
        ]
    print_status("Building project...")
    try:
        run(cmd, cwd=str(project_root), check=True)
    except subprocess.CalledProcessError:
        print_error("Build failed")
        sys.exit(1)
    print_success("Build completed")


def collect_files(paths: dict, version: str) -> Path:
    project_root: Path = paths["project_root"]
    yak_dir: Path = paths["yak_dir"]
    output_dir: Path = detect_output_dir(paths["output_dir"])

    version_dir = yak_dir / version
    version_dir.mkdir(parents=True, exist_ok=True)

    print_status(f"Collecting files from {output_dir} to {version_dir}")

    # Copy DLLs
    dlls = glob(str(output_dir / "*.dll"))
    if not dlls:
        print_status("No DLL files found")
    for dll in dlls:
        shutil.copy2(dll, str(version_dir))

    # Copy GHA
    gha = output_dir / "Alpaca4d.Gh.gha"
    if not gha.exists():
        print_error(f"Missing plugin file: {gha}")
        sys.exit(1)
    shutil.copy2(str(gha), str(version_dir / gha.name))

    # Copy folders and extra files (best-effort)
    #
    # OpenSees-Solvers is deliberately not packaged. The solver is chosen by the user -
    # Alpaca4d > Settings > Set OpenSees Executable - and Application.OpenSees reads that
    # path and nothing else, so a bundled copy was never loaded at runtime. It stays in
    # the repo and in the build output for local development; shipping it only added
    # ~114 MB to every download, and on macOS a bundled unsigned solver arrives
    # quarantined and gets SIGKILLed anyway.
    #
    # Dropped from a version dir an older script already filled: the directory is reused,
    # not rebuilt, so a leftover copy would go on being packaged and quietly undo this.
    stale_solvers = version_dir / "OpenSees-Solvers"
    if stale_solvers.exists():
        shutil.rmtree(stale_solvers)
        print_status(f"Removed stale OpenSees-Solvers from {version_dir.name} (no longer packaged)")

    safe_copy_file(output_dir / "data.bin", version_dir)
    safe_copy_tree(output_dir / "UserObject", version_dir / "UserObject")

    # Copy manifest + icon for yak build
    safe_copy_file(paths["manifest_file"], version_dir)
    safe_copy_file(paths["icon_file"], version_dir)

    print_success(f"Files collected in {version_dir}")
    return version_dir


def rename_yak_to_any_any(yak_path: Path) -> Path:
    new_name = re.sub(r"-rh[0-9_]*-", "-any-", yak_path.name)
    if new_name != yak_path.name:
        new_path = yak_path.with_name(new_name)
        yak_path.rename(new_path)
        print_status(f"Renamed {yak_path.name} to {new_path.name}")
        return new_path
    return yak_path


def build_yak(version_dir: Path) -> Path:
    ensure_yak_available()
    print_status("Creating yak package...")
    try:
        run(["yak", "build", "--platform", "any"], cwd=str(version_dir), check=True)
    except subprocess.CalledProcessError:
        print_error("yak build failed")
        sys.exit(1)

    yak_files = list(version_dir.glob("*.yak"))
    if not yak_files:
        print_error("No .yak file produced")
        sys.exit(1)

    yak_file = yak_files[0]
    yak_file = rename_yak_to_any_any(yak_file)
    print_success(f"Package created: {yak_file.name}")
    return yak_file


def cleanup_version_dir(version_dir: Path) -> None:
    # Remove manifest.yml and icon.png copied in for the build; keep package and binaries
    for name in ("manifest.yml", "icon.png"):
        p = version_dir / name
        if p.exists():
            p.unlink()
    print_success("Cleaned temporary files (kept package and binaries)")


def prompt_yes_no(message: str, default_yes: bool = True) -> bool:
    default = "Y/n" if default_yes else "y/N"
    try:
        answer = input(f"{message} ({default}): ").strip()
    except EOFError:
        return default_yes
    if not answer:
        return default_yes
    return answer.lower().startswith("y")


def push_package(yak_file: Path) -> None:
    print_status("Pushing package...")
    try:
        run(["yak", "push", str(yak_file)], cwd=str(yak_file.parent), check=True)
    except subprocess.CalledProcessError:
        print_error("Failed to push package. Ensure you're logged in: 'yak login'")
        sys.exit(1)
    print_success("Package pushed successfully!")


def main() -> None:
    parser = argparse.ArgumentParser(description="Cross-platform Yak publishing script for Alpaca4d")
    parser.add_argument("--version", dest="version", help="New version to set in manifest.yml")
    parser.add_argument("--push", dest="push", action="store_true", help="Push the built package without prompting")
    parser.add_argument("--yes", dest="assume_yes", action="store_true", help="Assume 'yes' for prompts (non-interactive)")
    args = parser.parse_args()

    paths = project_paths()

    print_status("Starting Alpaca4d plugin publishing process...")

    current_version = get_current_version(paths["manifest_file"])
    print_status(f"Current version: {current_version}")

    new_version = args.version
    if not new_version:
        try:
            new_version = input(f"Enter new version (or press Enter to keep {current_version}): ").strip()
        except EOFError:
            new_version = ""
    if not new_version:
        new_version = current_version

    # Prerelease awareness
    if re.search(r"-", new_version):
        print_status(f"Detected prerelease version: {new_version}")
        if not args.assume_yes:
            if not prompt_yes_no("This is a prerelease. Users will need --prerelease to find it. Continue?", default_yes=True):
                print_status("Publishing cancelled")
                sys.exit(0)

    if new_version != current_version:
        print_status(f"Updating version to {new_version} in manifest.yml")
        update_version(paths["manifest_file"], new_version)
        current_version = new_version

    # Build
    build_project(paths)

    # Collect
    version_dir = collect_files(paths, current_version)

    # Build yak
    yak_file = build_yak(version_dir)

    # Cleanup extras per publish.sh behavior
    cleanup_version_dir(version_dir)

    # Push optionally
    if args.push or (not args.assume_yes and prompt_yes_no("Do you want to push to Package Manager?", default_yes=True)):
        push_package(yak_file)
    else:
        print_status(f"Package ready in {version_dir}")

    print_success("Publishing process completed!")


if __name__ == "__main__":
    main()


