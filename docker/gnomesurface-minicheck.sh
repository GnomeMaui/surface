#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
TARGET="${1:-}"

print_help() {
	cat <<'EOF'
Usage: gnomesurface-minicheck.sh <target>

Collect the minimum diagnostics needed for GNOME Surface shell/RDP debugging.

Targets:
	arch
	ubuntu2604
	fedora44
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

CONTAINER_NAME="gnomesurface-$TARGET"
RDP_USER="${GM_RDP_USER:-dev}"
RDP_UID="${GM_RDP_USER_UID:-1000}"
TIMESTAMP="$(date +%Y%m%d-%H%M%S)"
REPORT_DIR="$SCRIPT_DIR/.reports"
REPORT_FILE="$REPORT_DIR/gnomesurface-minicheck-${TARGET}-${TIMESTAMP}.log"
USER_ENV="XDG_RUNTIME_DIR=/run/user/$RDP_UID DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/$RDP_UID/bus"

mkdir -p "$REPORT_DIR"

if ! docker inspect "$CONTAINER_NAME" >/dev/null 2>&1; then
	echo "Error: container '$CONTAINER_NAME' does not exist." >&2
	exit 1
fi

if [[ "$(docker inspect --format '{{.State.Running}}' "$CONTAINER_NAME")" != "true" ]]; then
	echo "Error: container '$CONTAINER_NAME' is not running." >&2
	exit 1
fi

section() {
	printf '\n\n========== %s ==========\n' "$1"
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

user_sh() {
	local command="$1"
	docker exec "$CONTAINER_NAME" sh -lc "sudo -u '$RDP_USER' env $USER_ENV sh -lc '$command'"
}

{
	echo "gnomesurface-minicheck report"
	echo "timestamp: $(date --iso-8601=seconds)"
	echo "target: $TARGET"
	echo "container: $CONTAINER_NAME"
	echo "report_file: $REPORT_FILE"

	section "Container"
	run_cmd "State" docker inspect --format 'name={{.Name}} status={{.State.Status}} running={{.State.Running}} started={{.State.StartedAt}} image={{.Config.Image}}' "$CONTAINER_NAME"
	run_cmd "Published ports" docker inspect --format '{{json .NetworkSettings.Ports}}' "$CONTAINER_NAME"
	run_cmd "Relevant processes" docker exec "$CONTAINER_NAME" sh -lc "ps -ef | grep -E 'GnomeSurfaceShell|gnome-shell|gnome-session|gnome-remote-desktop|gnome-monitor-trigger|xdg-desktop-portal|pipewire|wireplumber|dbus-daemon|systemd --user' | grep -v grep || true"
	run_cmd "System failed units" docker exec "$CONTAINER_NAME" sh -lc 'systemctl --failed --no-pager || true'

	section "User Services"
	run_cmd "Important user unit status" user_sh "systemctl --user status org.gnome.Shell@user.service gnome-session.service gnome-remote-desktop-headless.service gnome-monitor-trigger.service gnome-bootstrap-rdp.service --no-pager --lines=40 || true"
	run_cmd "Failed user units" user_sh "systemctl --user --failed --no-pager || true"

	section "Targeted Journals"
	run_cmd "GNOME/RDP user journal" user_sh "journalctl --user -u org.gnome.Shell@user.service -u gnome-remote-desktop-headless.service -u gnome-monitor-trigger.service -u gnome-session.service -u gnome-bootstrap-rdp.service -n 260 --no-pager || true"
	run_cmd "System journal filtered" docker exec "$CONTAINER_NAME" sh -lc "journalctl -n 220 --no-pager | grep -Ei 'GnomeSurface|gnome-remote-desktop|gnome-monitor-trigger|xdg-desktop-portal|DisplayConfig|MonitorsChanged|ApplyMonitorsConfig|Virtual remote|MetaVirtual|Window introspection|Window moved|segfault|SEGV|exception|failed|assertion' || true"

	section "DBus State"
	run_cmd "DisplayConfig GetCurrentState raw" user_sh "busctl --user --json=short call org.gnome.Mutter.DisplayConfig /org/gnome/Mutter/DisplayConfig org.gnome.Mutter.DisplayConfig GetCurrentState || true"
	run_cmd "DisplayConfig monitors summary" user_sh "busctl --user --json=short call org.gnome.Mutter.DisplayConfig /org/gnome/Mutter/DisplayConfig org.gnome.Mutter.DisplayConfig GetCurrentState | jq -r '.data[1][] | \"monitor connector=\\(.[0][0]) vendor=\\(.[0][1]) product=\\(.[0][2]) serial=\\(.[0][3]) current_modes=\\([.[1][] | select(.[6][\"is-current\"].data == true) | .[0]] | join(\",\"))\"' || true"
	run_cmd "DisplayConfig logical monitors summary" user_sh "busctl --user --json=short call org.gnome.Mutter.DisplayConfig /org/gnome/Mutter/DisplayConfig org.gnome.Mutter.DisplayConfig GetCurrentState | jq -r '.data[2][] | \"logical x=\\(.[0]) y=\\(.[1]) scale=\\(.[2]) primary=\\(.[4]) monitors=\\([.[5][] | .[0]] | join(\",\"))\"' || true"
	run_cmd "Shell Introspect windows" user_sh "busctl --user --json=short call org.gnome.Shell.Introspect /org/gnome/Shell/Introspect org.gnome.Shell.Introspect GetWindows || true"

	section "RDP"
	run_cmd "grdctl headless status" user_sh "grdctl --headless status || true"
	run_cmd "RDP gsettings" user_sh "gsettings list-recursively org.gnome.desktop.remote-desktop.rdp || true"
	run_cmd "RDP TCP check" bash -lc "timeout 3 bash -lc '</dev/tcp/${GM_IP}/${GM_RDP_HOST_PORT}' && echo open || echo closed"
} | tee "$REPORT_FILE"

echo
echo "Report written to: $REPORT_FILE"
