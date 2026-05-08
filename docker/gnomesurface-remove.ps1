Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Write-Output "WARNING: This is a destructive Docker cleanup operation."
Write-Output "It can permanently remove local development data and cached artifacts."
Write-Output ""
Write-Output "This script will remove:"
Write-Output "  - ALL containers"
Write-Output "  - ALL gnomesurface/* images"
Write-Output "  - ALL dangling images"
Write-Output "  - ALL Docker volumes"
Write-Output "  - Docker build cache"
Write-Output "  - Unused Docker networks"
Write-Output "  - Additional resources via 'docker system prune -af --volumes'"
Write-Output ""
$confirm = Read-Host "Type YES (uppercase) to continue"

if ($confirm -cne "YES") {
	Write-Output "Aborted. No changes were made."
	exit 1
}

Write-Output "=== Stopping and removing containers ==="
$containers = docker ps -aq
if ($containers) {
	docker rm -f $containers
}

Write-Output "=== Removing gnomesurface/* images ==="
$gnomesurfaceImages = docker images --format "{{.ID}} {{.Repository}}:{{.Tag}}" | Where-Object { $_ -match "^(\S+)\s+gnomesurface/" } | ForEach-Object { $Matches[1] }
if ($gnomesurfaceImages) {
	docker rmi -f $gnomesurfaceImages
}

Write-Output "=== Removing <none> images ==="
$danglingImages = docker images -f "dangling=true" -q
if ($danglingImages) {
	docker rmi -f $danglingImages
}

Write-Output "=== Removing all volumes ==="
$volumes = docker volume ls -q
if ($volumes) {
	docker volume rm -f $volumes
}

Write-Output "=== Removing build cache ==="
docker builder prune -af

Write-Output "=== Removing networks ==="
docker network prune -f

Write-Output "=== Running full cleanup ==="
docker system prune -af --volumes

Write-Output ""
Write-Output "=== Remaining disk usage ==="
docker system df
