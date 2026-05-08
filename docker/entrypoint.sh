#!/bin/bash
set -euo pipefail

RDP_PASS="$(cat /run/secrets/rdp_pass)"
echo "${GM_RDP_USER}:${RDP_PASS}" | chpasswd

mkdir -p "/run/user/${GM_RDP_USER_UID}"
chown "${GM_RDP_USER}:${GM_RDP_USER}" "/run/user/${GM_RDP_USER_UID}"
chmod 700 "/run/user/${GM_RDP_USER_UID}"

mkdir -p "/home/${GM_RDP_USER}/.config/environment.d"
sed -i "s|%GM_RDP_USER%|${GM_RDP_USER_UID}|g" "/home/${GM_RDP_USER}/.config/environment.d/gnome.conf"

BASE_DIR="/home/${GM_RDP_USER}/.local/share/gnome-remote-desktop"
CERT_DIR="${BASE_DIR}/certificates"
CRED_FILE="${BASE_DIR}/credentials.ini"

mkdir -p "${CERT_DIR}"
chmod 700 "${BASE_DIR}" "${CERT_DIR}"

cp /run/secrets/rdp_tls_key "${CERT_DIR}/rdp-tls.key"
cp /run/secrets/rdp_tls_crt "${CERT_DIR}/rdp-tls.crt"

chmod 600 "${CERT_DIR}/rdp-tls.key"
chmod 644 "${CERT_DIR}/rdp-tls.crt"

cat >"${CRED_FILE}" <<EOF
[RDP]
credentials={'username': '${GM_RDP_USER}', 'password': '${RDP_PASS}'}
EOF

chmod 600 "${CRED_FILE}"

case "${GM_TARGET}" in
arch | ubuntu2604)
	GM_GNOME_SHELL_LIBRARY_PATH="/usr/lib/gnome-shell"
	;;
fedora44)
	GM_GNOME_SHELL_LIBRARY_PATH="/usr/lib64/gnome-shell"
	;;
*)
	echo "Unsupported GM_TARGET for GNOME Shell library path: ${GM_TARGET}" >&2
	exit 1
	;;
esac

if [ -n "${LD_LIBRARY_PATH:-}" ]; then
	export LD_LIBRARY_PATH="${GM_GNOME_SHELL_LIBRARY_PATH}:${LD_LIBRARY_PATH}"
else
	export LD_LIBRARY_PATH="${GM_GNOME_SHELL_LIBRARY_PATH}"
fi

INIT_FLAG="/home/${GM_RDP_USER}/.gnomesurface-initialized"
if [ ! -f "$INIT_FLAG" ]; then
	# Only adjust ownership on writable runtime paths. Some files are mounted read-only.
	mkdir -p \
		"/home/${GM_RDP_USER}/.local/share/gnome-remote-desktop" \
		"/home/${GM_RDP_USER}/.local/share/gnome-shell" \
		"/home/${GM_RDP_USER}/.local/share/gnome-settings-daemon" \
		"/home/${GM_RDP_USER}/.local/share/icc" \
		"/home/${GM_RDP_USER}/.local/share/keyrings" \
		"/home/${GM_RDP_USER}/.local/state" \
		"/home/${GM_RDP_USER}/.cache"

	chown -R "${GM_RDP_USER}:${GM_RDP_USER}" "/home/${GM_RDP_USER}" 2>/dev/null || true
	chown -R "${GM_RDP_USER}:${GM_RDP_USER}" "/home/${GM_RDP_USER}/.local/share/gnome-surface/" 2>/dev/null || true
	chown -R "${GM_RDP_USER}:${GM_RDP_USER}" "/home/${GM_RDP_USER}/.local/share/backgrounds/" 2>/dev/null || true

	chown root:audio /dev/snd/* 2>/dev/null || true
	chown root:video /dev/video* 2>/dev/null || true

	echo "Initializing system Flatpak"
	flatpak --system remote-add --if-not-exists flathub https://dl.flathub.org/repo/flathub.flatpakrepo || true
	echo "Initializing Flatpak for user ${GM_RDP_USER}..."
	sudo -u "${GM_RDP_USER}" flatpak remote-add --if-not-exists --user flathub https://dl.flathub.org/repo/flathub.flatpakrepo || true
	sudo -u "${GM_RDP_USER}" flatpak update --appstream --user || true

	sudo -u "${GM_RDP_USER}" touch "${INIT_FLAG}"
	echo "Flatpak initialized."
fi

sudo -u "${GM_RDP_USER}" dotnet nuget add source "/home/${GM_RDP_USER}/.local/share/gnome-surface/nuget/" --name "GNOME Surface" || true
sudo -u "${GM_RDP_USER}" dotnet tool install gnomesurface.session
sudo -u "${GM_RDP_USER}" dotnet tool install gnomesurface.shell
sudo -u "${GM_RDP_USER}" dotnet tool install gnomesurfaceplugin.default
sudo -u "${GM_RDP_USER}" dotnet tool install gnomesurfaceplugin.demo

exec %GM_SYSTEMD_BINARY%
