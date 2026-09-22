"""Install one checksum-pinned official executable; never run a remote installer."""

import argparse
import hashlib
import io
import json
import platform
import tarfile
import urllib.request
import zipfile
from pathlib import Path


def binary_from_archive(data, asset, executable):
    if hashlib.sha256(data).hexdigest() != asset["sha256"]:
        raise ValueError("Tool archive checksum mismatch")
    if asset["asset"].endswith(".zip"):
        with zipfile.ZipFile(io.BytesIO(data)) as archive:
            return archive.read(executable)
    with tarfile.open(fileobj=io.BytesIO(data), mode="r:gz") as archive:
        member = archive.getmember(executable)
        if not member.isfile():
            raise ValueError("Tool archive entry is not a regular file")
        stream = archive.extractfile(member)
        if stream is None:
            raise ValueError("Tool archive executable cannot be read")
        return stream.read()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("tool", choices=("actionlint", "trivy"))
    parser.add_argument("--directory", type=Path, required=True)
    args = parser.parse_args()
    system = platform.system().lower()
    if system not in ("linux", "windows") or platform.machine().lower() not in ("amd64", "x86_64"):
        parser.error("Only Linux/Windows x64 are supported")
    tools = json.loads(Path(__file__).with_name("tool-versions.json").read_text(encoding="utf-8"))
    config = tools[args.tool]
    asset = config[system]
    url = f'https://github.com/{config["repository"]}/releases/download/v{config["version"]}/{asset["asset"]}'
    request = urllib.request.Request(url, headers={"User-Agent": "hospital-quality-tools"})
    # URL is constructed from a fixed HTTPS GitHub origin and the reviewed manifest.
    with urllib.request.urlopen(request, timeout=90) as response:  # nosec B310
        data = response.read(256 * 1024 * 1024 + 1)
    if len(data) > 256 * 1024 * 1024:
        raise ValueError("Tool archive exceeds size limit")
    executable = args.tool + (".exe" if system == "windows" else "")
    binary = binary_from_archive(data, asset, executable)
    args.directory.mkdir(parents=True, exist_ok=True)
    destination = args.directory / executable
    destination.write_bytes(binary)
    destination.chmod(0o755)
    print(f'Installed {args.tool} {config["version"]}; archive SHA-256 verified.')


if __name__ == "__main__":
    main()
