#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
TARGET="${1:-}"

print_help() {
	cat <<'EOF'
Usage: gnomesurface-down.sh <target>

Stop the selected Docker stack.

Targets:
	arch
	ubuntu2604
	fedora44

Options:
	-h, --help   Show this help message
EOF
}

case "${TARGET}" in
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
	echo "Error: unknown target '${TARGET}'." >&2
	print_help >&2
	exit 1
	;;
esac

if [[ $# -ne 1 ]]; then
	echo "Error: exactly one target is required." >&2
	print_help >&2
	exit 1
fi

docker compose --env-file "${SCRIPT_DIR}/shared.env" --env-file "${SCRIPT_DIR}/os/${TARGET}/${TARGET}.env" -f "${SCRIPT_DIR}/os/${TARGET}/docker-compose.yml" down
