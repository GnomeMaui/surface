#!/bin/bash

set -euo pipefail

ssh rhea@titan "sudo systemctl stop gdm"

FILE="Versions.props"
current=$(grep -oP '(?<=<GnomeSurfaceVersion>)[^<]+' "$FILE")
IFS='.' read -r major minor patch <<<"$current"
new_version="$major.$minor.$((patch + 1))"
sed -i "s|<GnomeSurfaceVersion>$current</GnomeSurfaceVersion>|<GnomeSurfaceVersion>$new_version</GnomeSurfaceVersion>|" "$FILE"

echo "GNOME Surface Version: $current → $new_version"
dotnet build -c Release

library_pack_dir="../.vscode/.linux/.dotnet/library-packs"
packages_to_prune_native_skia=(
	"gnomesurface.shell"
	"gnomesurfaceplugin.demo"
	"gnomesurfaceplugin.default"
)

native_runtime_globs_to_prune=(
	"tools/net10.0/any/runtimes/linux-arm/*"
	"tools/net10.0/any/runtimes/linux-loongarch64/*"
	"tools/net10.0/any/runtimes/linux-musl-arm/*"
	"tools/net10.0/any/runtimes/linux-musl-arm64/*"
	"tools/net10.0/any/runtimes/linux-musl-loongarch64/*"
	"tools/net10.0/any/runtimes/linux-musl-riscv64/*"
	"tools/net10.0/any/runtimes/linux-musl-x64/*"
	"tools/net10.0/any/runtimes/linux-riscv64/*"
	"tools/net10.0/any/runtimes/linux-x86/*"
	"tools/net10.0/any/runtimes/osx/*"
	"tools/net10.0/any/runtimes/win/*"
	"tools/net10.0/any/runtimes/win-arm64/*"
	"tools/net10.0/any/runtimes/win-x64/*"
	"tools/net10.0/any/runtimes/win-x86/*"
)

prune_native_skia_libs() {
	local package_id="$1"
	local package_path="$library_pack_dir/$package_id.$new_version.nupkg"

	if [[ ! -f "$package_path" ]]; then
		echo "Missing nupkg for native cleanup: $package_path" >&2
		return 1
	fi

	echo "Pruning extra native Skia runtimes: $package_path"
	zip -d "$package_path" "${native_runtime_globs_to_prune[@]}" || true
}

for package_id in "${packages_to_prune_native_skia[@]}"; do
	prune_native_skia_libs "$package_id"
done

ssh rhea@titan "rm -rf /home/rhea/.nuget/packages/*"
ssh rhea@titan "rm -rf /home/rhea/.local/share/gnome-surface/nuget/*"

scp -r ../.vscode/.linux/.dotnet/library-packs/*.$new_version.nupkg rhea@titan:/home/rhea/.local/share/gnome-surface/nuget/

ssh rhea@titan "dotnet tool install gnomesurface.session"
ssh rhea@titan "dotnet tool install gnomesurface.shell"
ssh rhea@titan "dotnet tool install gnomesurfaceplugin.default"
ssh rhea@titan "dotnet tool install gnomesurfaceplugin.demo"

ssh rhea@titan "sudo systemctl start gdm"
