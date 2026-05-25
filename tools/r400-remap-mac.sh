#!/usr/bin/env bash
# Remaps the R400 (VID 0x3151 / PID 0x3020) B button to F13 on macOS
# using the built-in `hidutil` tool. No app install required.
#
# hidutil property remaps live until the device is unplugged or the user
# logs out. For persistence we drop a LaunchAgent that re-applies on login
# and on device-plug events (via WatchPaths on the IOKit registry isn't
# feasible from a LaunchAgent, but RunAtLoad covers reboot/login).
#
# Usage:
#   ./r400-remap-mac.sh apply         # apply remap now (one-shot)
#   ./r400-remap-mac.sh install       # install LaunchAgent + apply
#   ./r400-remap-mac.sh uninstall     # remove LaunchAgent, clear remap
#   ./r400-remap-mac.sh status        # show current remap on the device

set -euo pipefail

VENDOR_ID=12625      # 0x3151
PRODUCT_ID=12320     # 0x3020
# HID Usage Page 0x07 (keyboard), Usage 0x05 (B) -> Usage 0x68 (F13)
SRC=0x700000005
DST=0x700000068

MATCH="{\"VendorID\":$VENDOR_ID,\"ProductID\":$PRODUCT_ID}"
MAPPING="{\"UserKeyMapping\":[{\"HIDKeyboardModifierMappingSrc\":$SRC,\"HIDKeyboardModifierMappingDst\":$DST}]}"

AGENT_LABEL="com.etdofresh.r400-remap"
AGENT_PLIST="$HOME/Library/LaunchAgents/$AGENT_LABEL.plist"

apply_remap() {
    hidutil property --matching "$MATCH" --set "$MAPPING" >/dev/null
    echo "Applied: B -> F13 on VID 0x3151 / PID 0x3020"
}

clear_remap() {
    hidutil property --matching "$MATCH" --set '{"UserKeyMapping":[]}' >/dev/null || true
    echo "Cleared remap on VID 0x3151 / PID 0x3020"
}

show_status() {
    hidutil property --matching "$MATCH" --get UserKeyMapping
}

install_agent() {
    cat > "$AGENT_PLIST" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>$AGENT_LABEL</string>
    <key>ProgramArguments</key>
    <array>
        <string>/usr/bin/hidutil</string>
        <string>property</string>
        <string>--matching</string>
        <string>$MATCH</string>
        <string>--set</string>
        <string>$MAPPING</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
</dict>
</plist>
EOF
    launchctl unload "$AGENT_PLIST" 2>/dev/null || true
    launchctl load "$AGENT_PLIST"
    echo "Installed LaunchAgent: $AGENT_PLIST"
    apply_remap
}

uninstall_agent() {
    if [[ -f "$AGENT_PLIST" ]]; then
        launchctl unload "$AGENT_PLIST" 2>/dev/null || true
        rm "$AGENT_PLIST"
        echo "Removed LaunchAgent: $AGENT_PLIST"
    fi
    clear_remap
}

case "${1:-apply}" in
    apply)      apply_remap ;;
    install)    install_agent ;;
    uninstall)  uninstall_agent ;;
    clear)      clear_remap ;;
    status)     show_status ;;
    *) echo "Usage: $0 {apply|install|uninstall|clear|status}" >&2; exit 2 ;;
esac
