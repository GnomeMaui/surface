#!/bin/bash
# gnome-monitor-trigger.sh - GNOME Remote Desktop Monitor Manager
#
# WHAT THIS SCRIPT DOES:
# ----------------------
# When an RDP client connects to a headless GNOME session, two monitors appear:
#   - Meta-0 (created by gnome-shell --headless)
#   - Meta-1 (created by gnome-remote-desktop for the RDP client)
#
# This is a known limitation of gnome-remote-desktop: it requires an active
# monitor to stream, but having two monitors causes issues (the headless one
# wastes resources and can confuse applications).
#
# WHAT THIS SCRIPT FIXES:
# -----------------------
# 1. Automatically detects when a "Virtual remote monitor" (Meta-1) appears
# 2. Disables the headless monitor (Meta-0) by applying a new configuration
# 3. Leaves only the Virtual remote monitor active at position (0,0) as primary
# 4. Runs both at startup and on every monitor change (MonitorsChanged signal)
#
# WHY IT'S NEEDED:
# ---------------
# Without this script, users connecting via RDP would see:
#   - Two monitors in their RDP session (confusing)
#   - The headless monitor wasting GPU/memory resources
#   - Potential application misbehavior (windows opening on wrong monitor)
#
# With this script, the RDP experience is seamless: a single monitor,
# properly configured, exactly as the user expects.
#
# USAGE:
# -----
#   ./gnome-monitor-trigger.sh         # Silent mode (for systemd service)
#   ./gnome-monitor-trigger.sh --log   # Verbose mode (for manual debugging)
#
# INSTALLATION AS SYSTEMD USER SERVICE:
# ------------------------------------
#   cp gnome-monitor-trigger.sh ~/.local/bin/
#   cp gnome-monitor-trigger.service ~/.config/systemd/user/
#   systemctl --user enable --now gnome-monitor-trigger.service

LOG_ENABLED=false

# Argument parsing
for arg in "$@"; do
	case $arg in
	--log)
		LOG_ENABLED=true
		;;
	--help)
		echo "Usage: $0 [--log]"
		echo "  --log    Enable detailed logging for debugging"
		exit 0
		;;
	esac
done

# Log function
log() {
	if [ "$LOG_ENABLED" = true ]; then
		echo "[$(date +%H:%M:%S)] $1"
	fi
}

# Check if jq is available
if ! command -v jq &>/dev/null; then
	echo "ERROR: jq is not installed. Please install jq using your package manager."
	exit 1
fi

# Function: check monitor configuration and disable extra monitors if needed
check_and_fix_monitors() {
	log "=== Checking monitor configuration ==="

	JSON=$(busctl --user --json=short call \
		org.gnome.Mutter.DisplayConfig \
		/org/gnome/Mutter/DisplayConfig \
		org.gnome.Mutter.DisplayConfig \
		GetCurrentState 2>/dev/null)

	SERIAL=$(echo "$JSON" | jq -r '.data[0]')
	LOGICAL_COUNT=$(echo "$JSON" | jq '.data[2] | length')
	VIRTUAL_COUNT=$(echo "$JSON" | jq '[.data[1][] | select(.[0][2] == "Virtual remote monitor")] | length')

	log "Serial: $SERIAL"
	log "Logical monitors: $LOGICAL_COUNT"
	log "Virtual remote monitors: $VIRTUAL_COUNT"

	# If a Virtual remote monitor exists AND there is more than 1 logical monitor
	if [ "$VIRTUAL_COUNT" -gt 0 ] && [ "$LOGICAL_COUNT" -gt 1 ]; then
		log "→ Virtual remote monitor detected, DISABLING others..."

		# Extract Virtual remote monitor data
		VIRTUAL_MONITOR=$(echo "$JSON" | jq -r '.data[1][] | select(.[0][2] == "Virtual remote monitor") | .[0][0]')
		VIRTUAL_MODE=$(echo "$JSON" | jq -r '.data[1][] | select(.[0][2] == "Virtual remote monitor") | .[1][0][0]')

		# Extract logical monitor data associated with the Virtual remote monitor
		LOGICAL_DATA=$(echo "$JSON" | jq -r '.data[2][] | select(.[5][0][2] == "Virtual remote monitor")')

		SCALE=$(echo "$LOGICAL_DATA" | jq -r '.[2]')
		TRANSFORM=$(echo "$LOGICAL_DATA" | jq -r '.[3]')

		log "Virtual monitor: $VIRTUAL_MONITOR"
		log "Virtual mode: $VIRTUAL_MODE"
		log "Scale: $SCALE, Transform: $TRANSFORM"

		# Disable others: keep only this monitor, position (0,0), PRIMARY=true
		if RESULT=$(busctl --user call \
			org.gnome.Mutter.DisplayConfig \
			/org/gnome/Mutter/DisplayConfig \
			org.gnome.Mutter.DisplayConfig \
			ApplyMonitorsConfig \
			'uua(iiduba(ssa{sv}))a{sv}' \
			"$SERIAL" 1 1 0 0 "$SCALE" "$TRANSFORM" true 1 "$VIRTUAL_MONITOR" "$VIRTUAL_MODE" 0 0 2>&1); then
			log "✓ Disable successful"
		else
			log "✗ Disable failed: $RESULT"
		fi
	else
		log "→ No action needed"
	fi
}

# Main program - always log startup, but without --log print only this one line
if [ "$LOG_ENABLED" = true ]; then
	echo "Monitor trigger started (debug mode). Waiting for Virtual remote monitor..."
	echo "PID: $$"
	echo ""
else
	# Quiet mode: only log the PID, output still goes to journal
	echo "Monitor trigger started (PID: $$)"
fi

# First check at startup
check_and_fix_monitors

# Then watch for changes
dbus-monitor --session "type='signal',interface='org.gnome.Mutter.DisplayConfig',member='MonitorsChanged'" |
	while read -r line; do
		if ! echo "$line" | grep -q "MonitorsChanged"; then
			continue
		fi

		log "=== MonitorsChanged signal detected ==="
		check_and_fix_monitors
	done
