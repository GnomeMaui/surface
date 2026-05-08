#!/bin/bash
set -euo pipefail

# shellcheck source=/dev/null
source "${HOME}/shared.env"
# shellcheck source=/dev/null
source "${HOME}/shared.override.env"

export XDG_RUNTIME_DIR="/run/user/${GM_RDP_USER_UID}"
export DBUS_SESSION_BUS_ADDRESS="unix:path=/run/user/${GM_RDP_USER_UID}/bus"
export WAYLAND_DISPLAY="wayland-0"
export XDG_CURRENT_DESKTOP="GNOME"
export XDG_SESSION_TYPE="wayland"
export XDG_SESSION_DESKTOP="gnome"
export XDG_MENU_PREFIX="gnome-"
export DESKTOP_SESSION="gnome"
export GNOME_DESKTOP_SESSION_ID="this-is-deprecated"

CERT_DIR="$HOME/.local/share/gnome-remote-desktop/certificates"
RDP_PASS="$(cat /run/secrets/rdp_pass)"
GNOME_SHELL_MAJOR="$(gnome-shell --version | sed -nE 's/^GNOME Shell ([0-9]+)(\..*)?$/\1/p')"

if [ "${GNOME_SHELL_MAJOR:-0}" -ge 50 ]; then
	grdctl --headless rdp disable-port-negotiation
	grdctl --headless rdp set-port "${GM_RDP_DOCKER_PORT}"
	grdctl --headless rdp set-auth-methods credentials
	grdctl --headless rdp set-tls-cert "${CERT_DIR}/rdp-tls.crt"
	grdctl --headless rdp set-tls-key "${CERT_DIR}/rdp-tls.key"
	grdctl --headless rdp set-credentials "${GM_RDP_USER}" "${RDP_PASS}"
	grdctl --headless rdp disable-view-only
	grdctl --headless rdp enable
	if grdctl --help | grep -q "vnc"; then
		grdctl --headless vnc disable
	fi

	grdctl --headless status --show-credentials || true
else
	gsettings set org.gnome.desktop.remote-desktop.rdp enable true
	gsettings set org.gnome.desktop.remote-desktop.rdp view-only false
	gsettings set org.gnome.desktop.remote-desktop.rdp negotiate-port false
	gsettings set org.gnome.desktop.remote-desktop.rdp port "${GM_RDP_DOCKER_PORT}"
	gsettings set org.gnome.desktop.remote-desktop.rdp screen-share-mode 'mirror-primary'
	gsettings set org.gnome.desktop.remote-desktop.rdp tls-cert "${CERT_DIR}/rdp-tls.crt"
	gsettings set org.gnome.desktop.remote-desktop.rdp tls-key "${CERT_DIR}/rdp-tls.key"
	gsettings set org.gnome.desktop.remote-desktop.vnc enable false
	gsettings set org.gnome.mutter check-alive-timeout 0
	gsettings list-recursively org.gnome.desktop.remote-desktop.rdp
fi

gsettings set org.gnome.desktop.input-sources sources "[('xkb', '${GM_KEYMAP}')]"

# shellcheck source=/dev/null
source "${HOME}/.local/bin/desktop.sh"
