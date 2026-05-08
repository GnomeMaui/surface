#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ENV_FILE="${SCRIPT_DIR}/shared.env"
SECRET_DIR="${SCRIPT_DIR}/.secret"
KEY_FILE="${SECRET_DIR}/rdp-tls.key"
CRT_FILE="${SECRET_DIR}/rdp-tls.crt"
NETWORK_NAME="gnomesurface-network"

RUN_CERT=0
RUN_NET=0

print_help() {
	cat <<'EOF'
Usage: gnomesurface-install.sh [options]

Configure shared Docker prerequisites for all stacks.

Actions:
    --cert    Generate persistent RDP TLS certificate
    --net     Create the shared Docker network from .env values
    --all     Run both actions

Options:
    --force   Overwrite existing certificate
    -h, --help  Show this help message
EOF
}

load_env() {
	# shellcheck source=./shared.env
	source "${ENV_FILE}"
}

create_certificate() {
	mkdir -p "${SECRET_DIR}"
	chmod 700 "${SECRET_DIR}"

	if [[ -f "${KEY_FILE}" && -f "${CRT_FILE}" && "${FORCE}" -eq 0 ]]; then
		echo "RDP TLS certificate already exists, skipping generation."
		echo "Use --force to regenerate."
		return 0
	fi

	echo "Generating RDP TLS certificate..."
	openssl req -x509 -newkey rsa:2048 -nodes \
		-keyout "${KEY_FILE}" \
		-out "${CRT_FILE}" \
		-days 3650 \
		-subj "/CN=gnomesurface-rdp"

	chmod 600 "$KEY_FILE"
	chmod 644 "$CRT_FILE"

	echo "Certificate generated:"
	echo "  $KEY_FILE"
	echo "  $CRT_FILE"
}

create_network() {
	load_env

	if docker network inspect "$NETWORK_NAME" >/dev/null 2>&1; then
		echo "Docker network '$NETWORK_NAME' already exists, skipping creation."
		return 0
	fi

	echo "Creating Docker network '$NETWORK_NAME'..."
	docker network create \
		--subnet="$GM_SUBNET" \
		--gateway="$GM_GATEWAY" \
		"$NETWORK_NAME"
}

FORCE=0

while [[ $# -gt 0 ]]; do
	case "$1" in
	--cert)
		RUN_CERT=1
		shift
		;;
	--net)
		RUN_NET=1
		shift
		;;
	--all)
		RUN_CERT=1
		RUN_NET=1
		shift
		;;
	-h | --help)
		print_help
		exit 0
		;;
	--force)
		FORCE=1
		shift
		;;
	*)
		echo "Error: unknown argument '$1'." >&2
		print_help >&2
		exit 1
		;;
	esac
done

if [[ $RUN_CERT -eq 0 && $RUN_NET -eq 0 ]]; then
	print_help
	exit 0
fi

if [[ $RUN_CERT -eq 1 ]]; then
	create_certificate
fi

if [[ $RUN_NET -eq 1 ]]; then
	create_network
fi
