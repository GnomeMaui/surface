#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
TARGET="${1:-}"

print_help() {
	cat <<'EOF'
Usage: gnomesurface-up.sh <target>

Start the selected Docker stack.

Targets:
	arch
	ubuntu2604
	fedora44

Options:
	-h, --help   Show this help message
EOF
}

print_ready_info() {
	# shellcheck source=/dev/null
	source "$SCRIPT_DIR/shared.env"
	# shellcheck source=/dev/null
	source "$SCRIPT_DIR/os/$TARGET/$TARGET.env"

	GREEN='\033[1;32m'
	YELLOW='\033[1;33m'
	NC='\033[0m'

	printf ' %b✔%b RDP is available.\n' "$GREEN" "$NC"
	printf ' %b✔%b RDP endpoint: %b%s:%s%b\n' "$GREEN" "$NC" "$YELLOW" "$GM_IP" "$GM_RDP_HOST_PORT" "$NC"
	printf ' %b✔%b Hosts entry: %b%s %s%b\n' "$GREEN" "$NC" "$YELLOW" "$GM_IP" "$GM_TARGET" "$NC"
}

case "$TARGET" in
-h | --help)
	print_help
	exit 0
	;;
arch | ubuntu2604 | fedora44)
	;;
"")
	echo "Error: missing target." >&2
	print_help >&2
	exit 1
	;;
*)
	echo "Error: unknown target '$TARGET'." >&2
	print_help >&2
	exit 1
	;;
esac

if [[ $# -ne 1 ]]; then
	echo "Error: exactly one target is required." >&2
	print_help >&2
	exit 1
fi

docker compose --env-file "$SCRIPT_DIR/shared.env" --env-file "$SCRIPT_DIR/os/$TARGET/$TARGET.env" -f "$SCRIPT_DIR/os/$TARGET/docker-compose.yml" up -d --wait --wait-timeout 120 &&
	print_ready_info
