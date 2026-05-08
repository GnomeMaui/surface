#!/bin/bash
# Customize the desktop environment

gnome-extensions enable light-style@gnome-shell-extensions.gcampax.github.com || true
gsettings set org.gnome.desktop.interface color-scheme 'prefer-dark'

gsettings set org.gnome.shell favorite-apps "['org.gnome.Software.desktop', 'org.gnome.Nautilus.desktop', 'org.gnome.Ptyxis.desktop', 'org.gnome.Epiphany.desktop', 'org.gnome.Settings.desktop', 'org.gnome.seahorse.Application.desktop']"

gsettings set org.gnome.desktop.interface icon-theme "Papirus"

gsettings set org.gnome.mutter dynamic-workspaces false
gsettings set org.gnome.desktop.wm.preferences num-workspaces 1
gsettings set org.gnome.desktop.wm.preferences button-layout ':minimize,maximize,close'

busctl call org.freedesktop.Accounts "/org/freedesktop/Accounts/User$(id -u)" org.freedesktop.Accounts.User SetLanguages as 1 "$LANG" || true

gsettings set org.gnome.settings-daemon.plugins.power sleep-inactive-ac-type 'nothing'
gsettings set org.gnome.settings-daemon.plugins.power sleep-inactive-battery-type 'nothing'
gsettings set org.gnome.desktop.session idle-delay 0
gsettings set org.gnome.settings-daemon.plugins.power idle-dim false

if [ -x "$HOME/.local/bin/desktop-ext.sh" ]; then
	# shellcheck source=/dev/null
	source "$HOME/.local/bin/desktop-ext.sh"
fi
