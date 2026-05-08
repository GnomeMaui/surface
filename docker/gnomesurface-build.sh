#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
TARGET=""
NO_CACHE=0

print_help() {
	cat <<'EOF'
Usage: gnomesurface-build.sh [options] <target>

Build Docker image for the selected target.

Targets:
    arch       Build Arch Linux GNOME headless image
    ubuntu2604 Build Ubuntu 26.04 GNOME headless image
    fedora44   Build Fedora 44 GNOME headless image

Options:
    -nc, --no-cache   Build without Docker cache
    -h, --help        Show this help message
EOF
}

while [[ $# -gt 0 ]]; do
	case "$1" in
	-h | --help)
		print_help
		exit 0
		;;
	-nc | --no-cache)
		NO_CACHE=1
		shift
		;;
	arch | ubuntu2604 | fedora44)
		if [[ -n "${TARGET}" ]]; then
			echo "Error: target already set to '${TARGET}'." >&2
			print_help >&2
			exit 1
		fi
		TARGET="$1"
		shift
		;;
	*)
		echo "Error: unknown argument '$1'." >&2
		print_help >&2
		exit 1
		;;
	esac
done

if [[ -z "${TARGET}" ]]; then
	echo "Error: missing target." >&2
	print_help >&2
	exit 1
fi

source "${SCRIPT_DIR}/shared.env"
# shellcheck source=/dev/null
source "${SCRIPT_DIR}/os/${TARGET}/${TARGET}.env"

NO_CACHE_ARG=()
if [[ ${NO_CACHE} -eq 1 ]]; then
	NO_CACHE_ARG+=(--no-cache)
fi

CONTEXT_DIR="${SCRIPT_DIR}"

create_volume_if_not_exists() {
	local name="$1"
	local device="$2"

	mkdir -p "$device"

	if docker volume inspect "$name" >/dev/null 2>&1; then
		echo "Volume '$name' exists."
	else
		docker volume create \
			--driver local \
			--opt type=none \
			--opt device="$device" \
			--opt o=bind \
			"$name"
		echo "Volume '$name' created."
	fi
}

create_volume_if_not_exists "gnomesurface_home_local_share_backgrounds" "${SCRIPT_DIR}/volumes/shared/home_local_share_backgrounds"
create_volume_if_not_exists "gnomesurface_home_local_share_gnomesurface" "${SCRIPT_DIR}/volumes/shared/home_local_share_gnomesurface"
create_volume_if_not_exists "gnomesurface_home_local_share_flatpak" "${SCRIPT_DIR}/volumes/shared/home_local_share_flatpak"
create_volume_if_not_exists "gnomesurface_home_var_app" "${SCRIPT_DIR}/volumes/shared/home_var_app"
create_volume_if_not_exists "gnomesurface_var_lib_flatpak" "${SCRIPT_DIR}/volumes/shared/var_lib_flatpak"

IMAGE_TAG="gnomesurface/${TARGET}:${GM_TARGETARCH}"
DOCKERFILE="${SCRIPT_DIR}/os/${TARGET}/Dockerfile"

docker build \
	"${NO_CACHE_ARG[@]}" \
	-f "${DOCKERFILE}" \
	--build-arg GM_TARGETARCH="${GM_TARGETARCH}" \
	--build-arg GM_HOSTNAME="${GM_HOSTNAME}" \
	--build-arg GM_RDP_DOCKER_PORT="${GM_RDP_DOCKER_PORT}" \
	--build-arg GM_RDP_USER_UID="${GM_RDP_USER_UID}" \
	--build-arg GM_RDP_USER_GID="${GM_RDP_USER_GID}" \
	--build-arg GM_RDP_USER="${GM_RDP_USER}" \
	--build-arg GM_TIMEZONE="${GM_TIMEZONE}" \
	--build-arg GM_LOCALES="${GM_LOCALES}" \
	--build-arg GM_LOCALE="${GM_LOCALE}" \
	--build-arg GM_LANGUAGE="${GM_LANGUAGE}" \
	--build-arg GM_KEYMAP="${GM_KEYMAP}" \
	--build-arg GM_KEYMODEL="${GM_KEYMODEL}" \
	-t "${IMAGE_TAG}" "${CONTEXT_DIR}"
