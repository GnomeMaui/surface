#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
TARGET="${1:-}"

print_help() {
	cat <<'EOF'
Usage: gnomesurface-check.sh <target>

Collect a structured diagnostics report from a running GNOME MAUI Docker stack.

Targets:
	arch
	ubuntu2604
	fedora44

Options:
	-h, --help   Show this help message
EOF
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

# shellcheck source=/dev/null
source "$SCRIPT_DIR/shared.env"
# shellcheck source=/dev/null
source "$SCRIPT_DIR/os/$TARGET/$TARGET.env"

COMPOSE_FILE="$SCRIPT_DIR/os/$TARGET/docker-compose.yml"
CONTAINER_NAME="gnomesurface-$TARGET"
RDP_USER="${GM_RDP_USER:-dev}"
RDP_UID="${GM_RDP_USER_UID:-1000}"
TIMESTAMP="$(date +%Y%m%d-%H%M%S)"
REPORT_DIR="$SCRIPT_DIR/.reports"
REPORT_FILE="$REPORT_DIR/gnomesurface-check-${TARGET}-${TIMESTAMP}.log"

mkdir -p "$REPORT_DIR"

compose_cmd=(docker compose --env-file "$SCRIPT_DIR/shared.env" --env-file "$SCRIPT_DIR/os/$TARGET/$TARGET.env" -f "$COMPOSE_FILE")

if ! docker inspect "$CONTAINER_NAME" >/dev/null 2>&1; then
	echo "Error: container '$CONTAINER_NAME' does not exist." >&2
	echo "Tip: start it first with ./gnomesurface-up.sh $TARGET" >&2
	exit 1
fi

if [[ "$(docker inspect --format '{{.State.Running}}' "$CONTAINER_NAME")" != "true" ]]; then
	echo "Error: container '$CONTAINER_NAME' is not running." >&2
	echo "Tip: start it first with ./gnomesurface-up.sh $TARGET" >&2
	exit 1
fi

section() {
	local title="$1"
	printf '\n\n========== %s ==========' "$title"
	printf '\n'
}

run_cmd() {
	local title="$1"
	shift
	echo
	echo "--- $title ---"
	echo "+ $*"
	set +e
	"$@"
	local rc=$?
	set -e
	echo "[exit_code=$rc]"
}

{
	echo "gnomesurface-check report"
	echo "timestamp: $(date --iso-8601=seconds)"
	echo "target: $TARGET"
	echo "container: $CONTAINER_NAME"
	echo "report_file: $REPORT_FILE"

	section "Host Summary"
	run_cmd "Current directory" pwd
	run_cmd "Kernel" uname -a
	run_cmd "Docker version" docker version
	run_cmd "Docker compose version" docker compose version
	run_cmd "Compose ps" "${compose_cmd[@]}" ps
	run_cmd "Container in docker ps" docker ps --no-trunc --filter "name=^/${CONTAINER_NAME}$"

	section "Container State"
	run_cmd "Container inspect (state/health)" docker inspect --format 'name={{.Name}} id={{.Id}} status={{.State.Status}} running={{.State.Running}} health={{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}} started={{.State.StartedAt}} image={{.Config.Image}}' "$CONTAINER_NAME"
	run_cmd "Container ports" docker inspect --format '{{json .NetworkSettings.Ports}}' "$CONTAINER_NAME"
	run_cmd "Container mounts" docker inspect --format '{{range .Mounts}}{{println .Type "->" .Source "=>" .Destination "(" .RW ")"}}{{end}}' "$CONTAINER_NAME"
	run_cmd "Container GM_* env" docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' "$CONTAINER_NAME"

	section "Docker Logs"
	run_cmd "Container stdout/stderr logs" docker logs --timestamps "$CONTAINER_NAME"

	section "Inside Container (Root)"
	run_cmd "OS release" docker exec "$CONTAINER_NAME" sh -lc 'cat /etc/os-release'
	run_cmd "Identity" docker exec "$CONTAINER_NAME" sh -lc 'id; whoami; hostname'
	run_cmd "Runtime environment" docker exec "$CONTAINER_NAME" sh -lc 'printenv | sort'
	run_cmd "Locale" docker exec "$CONTAINER_NAME" sh -lc 'locale; echo; locale -a | sort'
	run_cmd "Network listeners" docker exec "$CONTAINER_NAME" sh -lc 'ss -lntup || true'
	run_cmd "Process list" docker exec "$CONTAINER_NAME" sh -lc 'ps -ef'
	if [[ "$TARGET" == "alpine" ]]; then
		run_cmd "OpenRC service list" docker exec "$CONTAINER_NAME" sh -lc 'rc-status --all 2>/dev/null || true'
		run_cmd "OpenRC run level" docker exec "$CONTAINER_NAME" sh -lc 'rc-status 2>/dev/null || true'
		run_cmd "syslog tail" docker exec "$CONTAINER_NAME" sh -lc 'tail /var/log/messages 2>/dev/null || true'
	else
		run_cmd "Systemd failed units" docker exec "$CONTAINER_NAME" sh -lc 'systemctl --failed --no-pager || true'
		run_cmd "System journal tail" docker exec "$CONTAINER_NAME" sh -lc 'journalctl  --no-pager || true'
	fi
	run_cmd "RDP files (permissions)" docker exec "$CONTAINER_NAME" sh -lc "ls -lah /home/${GM_RDP_USER}/.local/share/gnome-remote-desktop 2>/dev/null || true; ls -lah /home/${GM_RDP_USER}/.local/share/gnome-remote-desktop/certificates 2>/dev/null || true; ls -lah /run/secrets 2>/dev/null || true"
	run_cmd "RDP cert sanity" docker exec "$CONTAINER_NAME" sh -lc "openssl x509 -in /home/${GM_RDP_USER}/.local/share/gnome-remote-desktop/certificates/rdp-tls.crt -noout -subject -issuer -dates 2>/dev/null || true; openssl pkey -in /home/${GM_RDP_USER}/.local/share/gnome-remote-desktop/certificates/rdp-tls.key -noout 2>/dev/null || true"

	section "Inside Container (RDP User)"
	if [[ "$TARGET" == "alpine" ]]; then
		# Alpine uses OpenRC + direct process bootstrap (no user systemd).
		# Diagnose via process list, log files and grdctl.
		run_cmd "RDP / GNOME process list" docker exec "$CONTAINER_NAME" sh -lc "ps -ef | grep -E 'gnome-remote-desktop|gnome-shell|gnome-session|pipewire|wireplumber|dbus-daemon|elogind|polkitd|xdg-desktop-portal' | grep -v grep || true"
		run_cmd "gnome-bootstrap-rdp.sh log" docker exec "$CONTAINER_NAME" sh -lc "cat /tmp/gnome-bootstrap-rdp.log 2>/dev/null || echo '(no log at /tmp/gnome-bootstrap-rdp.log)'"
		run_cmd "gnome-shell log" docker exec "$CONTAINER_NAME" sh -lc "cat /tmp/gnome-shell.log 2>/dev/null || echo '(no log at /tmp/gnome-shell.log)'"
		run_cmd "gnome-remote-desktop log" docker exec "$CONTAINER_NAME" sh -lc "cat /tmp/gnome-remote-desktop.log 2>/dev/null || echo '(no log at /tmp/gnome-remote-desktop.log)'"
		run_cmd "pipewire log" docker exec "$CONTAINER_NAME" sh -lc "cat /tmp/pipewire.log 2>/dev/null || echo '(no log at /tmp/pipewire.log)'"
		run_cmd "wireplumber log" docker exec "$CONTAINER_NAME" sh -lc "cat /tmp/wireplumber.log 2>/dev/null || echo '(no log at /tmp/wireplumber.log)'"
		run_cmd "xdg-desktop-portal log" docker exec "$CONTAINER_NAME" sh -lc "cat /tmp/xdg-desktop-portal.log 2>/dev/null || echo '(no log at /tmp/xdg-desktop-portal.log)'"
		run_cmd "xdg-desktop-portal-gnome log" docker exec "$CONTAINER_NAME" sh -lc "cat /tmp/xdg-desktop-portal-gnome.log 2>/dev/null || echo '(no log at /tmp/xdg-desktop-portal-gnome.log)'"
		run_cmd "dbus-daemon status" docker exec "$CONTAINER_NAME" sh -lc "pgrep -a dbus-daemon || echo '(not running)'"
		run_cmd "elogind status" docker exec "$CONTAINER_NAME" sh -lc "pgrep -a elogind || echo '(not running)'"
		run_cmd "polkitd status" docker exec "$CONTAINER_NAME" sh -lc "pgrep -a polkitd || echo '(not running)'"
		run_cmd "Wayland socket" docker exec "$CONTAINER_NAME" sh -lc "ls -la /run/user/$RDP_UID/wayland-0 2>/dev/null || echo '(no wayland socket)'"
	else
		# systemd-based targets: query user service manager and journals.
		run_cmd "User services status" docker exec "$CONTAINER_NAME" sh -lc "sudo -u '$RDP_USER' env XDG_RUNTIME_DIR=/run/user/$RDP_UID DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/$RDP_UID/bus systemctl --user status gnome-bootstrap-rdp.service gnome-remote-desktop-headless.service gnome-session.service gnome-settings-daemon.target gnome-session-ctl.service org.gnome.Shell@user.service gnome-monitor-trigger.service --no-pager || true"
		run_cmd "User services failed" docker exec "$CONTAINER_NAME" sh -lc "sudo -u '$RDP_USER' env XDG_RUNTIME_DIR=/run/user/$RDP_UID DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/$RDP_UID/bus systemctl --user --failed --no-pager || true"
		run_cmd "User journal for GNOME/RDP units" docker exec "$CONTAINER_NAME" sh -lc "sudo -u '$RDP_USER' journalctl --user -u gnome-bootstrap-rdp.service -u gnome-remote-desktop-headless.service -u gnome-session.service -u gnome-settings-daemon.target -u gnome-session-ctl.service -u org.gnome.Shell@user.service -u gnome-monitor-trigger.service -n 400 --no-pager || true"
	fi
	run_cmd "grdctl help" docker exec "$CONTAINER_NAME" sh -lc "sudo -u '$RDP_USER' env XDG_RUNTIME_DIR=/run/user/$RDP_UID DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/$RDP_UID/bus grdctl --help || true"
	run_cmd "grdctl status (headless)" docker exec "$CONTAINER_NAME" sh -lc "sudo -u '$RDP_USER' env XDG_RUNTIME_DIR=/run/user/$RDP_UID DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/$RDP_UID/bus grdctl --headless status || true"
	run_cmd "RDP gsettings" docker exec "$CONTAINER_NAME" sh -lc "sudo -u '$RDP_USER' env XDG_RUNTIME_DIR=/run/user/$RDP_UID DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/$RDP_UID/bus gsettings list-recursively org.gnome.desktop.remote-desktop.rdp || true"
	run_cmd "Input source gsettings" docker exec "$CONTAINER_NAME" sh -lc "sudo -u '$RDP_USER' env XDG_RUNTIME_DIR=/run/user/$RDP_UID DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/$RDP_UID/bus gsettings get org.gnome.desktop.input-sources sources || true"

	section "Host Connectivity Checks"
	run_cmd "RDP TCP check to published endpoint" bash -lc "timeout 3 bash -lc '</dev/tcp/${GM_IP}/${GM_RDP_HOST_PORT}' && echo open || echo closed"
} | tee "$REPORT_FILE"

echo

echo "Report written to: $REPORT_FILE"
