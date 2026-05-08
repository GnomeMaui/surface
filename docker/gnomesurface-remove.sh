#!/bin/bash
set -euo pipefail

mapfile -t CONTAINER_ITEMS < <(docker ps -a --format '{{.ID}} {{.Names}}' | awk '$2 ~ /^gnomesurface/ {print}')
mapfile -t IMAGE_ITEMS < <(docker images --format '{{.Repository}}:{{.Tag}} {{.ID}}' | awk '$1 ~ /^gnomesurface/ {print}')
mapfile -t VOLUME_ITEMS < <(docker volume ls --format '{{.Name}}' | grep '^gnomesurface' || true)
mapfile -t NETWORK_ITEMS < <(docker network ls --format '{{.Name}}' | grep '^gnomesurface' || true)

print_section() {
	local title="$1"
	shift
	local items=("$@")

	echo "$title"
	if ((${#items[@]} == 0)); then
		echo "  - none"
		return
	fi

	for item in "${items[@]}"; do
		echo "  - $item"
	done
}

echo "WARNING: This is a destructive Docker cleanup operation."
echo "It can permanently remove local gnomesurface Docker artifacts."
echo ""
echo "This script will remove only resources whose name/repository starts with 'gnomesurface'."
echo ""
echo "Planned deletions:"
print_section "Containers:" "${CONTAINER_ITEMS[@]}"
print_section "Images:" "${IMAGE_ITEMS[@]}"
print_section "Volumes:" "${VOLUME_ITEMS[@]}"
print_section "Networks:" "${NETWORK_ITEMS[@]}"
echo ""
echo "Choose what to delete:"
echo "  - ALL: Containers + Images + Volumes + Networks"
echo "  - C: Containers"
echo "  - I: Images"
echo "  - V: Volumes"
echo "  - N: Networks"
echo "Type command (examples: ALL, C, IV, CVN):"
read -r CONFIRM

CONFIRM_CLEANED="$(echo "$CONFIRM" | tr '[:lower:]' '[:upper:]' | tr -d '[:space:],')"

DELETE_CONTAINERS=false
DELETE_IMAGES=false
DELETE_VOLUMES=false
DELETE_NETWORKS=false

if [[ "$CONFIRM_CLEANED" == "ALL" ]]; then
	DELETE_CONTAINERS=true
	DELETE_IMAGES=true
	DELETE_VOLUMES=true
	DELETE_NETWORKS=true
elif [[ "$CONFIRM_CLEANED" =~ ^[CIVN]+$ ]]; then
	[[ "$CONFIRM_CLEANED" == *C* ]] && DELETE_CONTAINERS=true
	[[ "$CONFIRM_CLEANED" == *I* ]] && DELETE_IMAGES=true
	[[ "$CONFIRM_CLEANED" == *V* ]] && DELETE_VOLUMES=true
	[[ "$CONFIRM_CLEANED" == *N* ]] && DELETE_NETWORKS=true
else
	echo "Aborted. Invalid command: '$CONFIRM'. No changes were made."
	exit 1
fi

if [[ "$DELETE_CONTAINERS" == true ]]; then
	echo "=== Stopping and removing gnomesurface* containers ==="
	if ((${#CONTAINER_ITEMS[@]} > 0)); then
		printf '%s\n' "${CONTAINER_ITEMS[@]}" | awk '{print $1}' | xargs -r docker rm -f
	fi
fi

if [[ "$DELETE_IMAGES" == true ]]; then
	echo "=== Removing gnomesurface* images ==="
	if ((${#IMAGE_ITEMS[@]} > 0)); then
		printf '%s\n' "${IMAGE_ITEMS[@]}" | awk '{print $2}' | xargs -r docker rmi -f
	fi
fi

if [[ "$DELETE_VOLUMES" == true ]]; then
	echo "=== Removing gnomesurface* volumes ==="
	if ((${#VOLUME_ITEMS[@]} > 0)); then
		printf '%s\n' "${VOLUME_ITEMS[@]}" | xargs -r docker volume rm -f
	fi
fi

if [[ "$DELETE_NETWORKS" == true ]]; then
	echo "=== Removing gnomesurface* networks ==="
	if ((${#NETWORK_ITEMS[@]} > 0)); then
		printf '%s\n' "${NETWORK_ITEMS[@]}" | xargs -r docker network rm
	fi
fi

echo ""
echo "=== Remaining disk usage ==="
docker system df
