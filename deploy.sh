#!/usr/bin/env bash
# VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-23 @ 16:47 UTC
#
# Jotanator deploy script: publishes the bot and runs it forever as a systemd *user* service
# (starts at boot, restarts on crash, survives logout).
#
# Put this file next to Jotanator.csproj and run:
#   ./deploy.sh            publish + install + (re)start   (run again after every code change)
#   ./deploy.sh status     show service status
#   ./deploy.sh logs       follow live logs
#   ./deploy.sh stop       stop the bot
#   ./deploy.sh uninstall  stop, disable and remove the service (keeps the published folder)
#
# Optional: INSTALL_DIR=/some/path ./deploy.sh   (default: ~/jotanator)

set -euo pipefail

SERVICE="jotanator"
SRC_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
INSTALL_DIR="${INSTALL_DIR:-$HOME/jotanator}"
UNIT_DIR="$HOME/.config/systemd/user"
UNIT_FILE="$UNIT_DIR/$SERVICE.service"

say() { printf '\033[1;36m==>\033[0m %s\n' "$*"; }
die() { printf '\033[1;31mError:\033[0m %s\n' "$*" >&2; exit 1; }

cmd="${1:-deploy}"

case "$cmd" in
  status)
    systemctl --user status "$SERVICE" --no-pager || true
    exit 0 ;;
  logs)
    exec journalctl --user -u "$SERVICE" -f ;;
  stop)
    systemctl --user stop "$SERVICE"
    say "Stopped."
    exit 0 ;;
  uninstall)
    systemctl --user disable --now "$SERVICE" 2>/dev/null || true
    rm -f "$UNIT_FILE"
    systemctl --user daemon-reload
    say "Service removed. Files in $INSTALL_DIR were left alone."
    exit 0 ;;
  deploy) ;;
  *) die "Unknown command '$cmd' (use: deploy | status | logs | stop | uninstall)" ;;
esac

# ---- checks -----------------------------------------------------------------
command -v dotnet >/dev/null || die "dotnet SDK not found (on Arch/CachyOS: sudo pacman -S dotnet-sdk)"
command -v systemctl >/dev/null || die "systemd not found"
[[ -f "$SRC_DIR/Jotanator.csproj" ]] || die "Jotanator.csproj not found next to this script"

# ---- publish (to a temp dir first so the running bot isn't disturbed while building) ----
BUILD_DIR="$(mktemp -d)"
trap 'rm -rf "$BUILD_DIR"' EXIT

say "Publishing Release build..."
dotnet publish "$SRC_DIR/Jotanator.csproj" -c Release -o "$BUILD_DIR" --nologo -v quiet

# ---- install ----------------------------------------------------------------
mkdir -p "$INSTALL_DIR"

if systemctl --user is-active --quiet "$SERVICE"; then
  say "Stopping running bot..."
  systemctl --user stop "$SERVICE"
fi

say "Installing to $INSTALL_DIR"
cp -a "$BUILD_DIR"/. "$INSTALL_DIR"/
chmod +x "$INSTALL_DIR/Jotanator"

# .env holds the token: copy it from the project if present, otherwise it must already be installed
if [[ -f "$SRC_DIR/.env" ]]; then
  cp "$SRC_DIR/.env" "$INSTALL_DIR/.env"
fi
[[ -f "$INSTALL_DIR/.env" ]] || die "No .env found in $SRC_DIR or $INSTALL_DIR (needs DISCORD_BOT_TOKEN)"
chmod 600 "$INSTALL_DIR/.env"

# ---- systemd user service ------------------------------------------------------
say "Writing $UNIT_FILE"
mkdir -p "$UNIT_DIR"
cat > "$UNIT_FILE" <<UNIT
[Unit]
Description=Jotanator Discord bot
After=network-online.target
Wants=network-online.target

[Service]
WorkingDirectory=$INSTALL_DIR
ExecStart=$INSTALL_DIR/Jotanator
Restart=always
RestartSec=5

[Install]
WantedBy=default.target
UNIT

systemctl --user daemon-reload
systemctl --user enable "$SERVICE" >/dev/null 2>&1
systemctl --user restart "$SERVICE"

# lets the service start at boot and keep running after you log out
if ! loginctl enable-linger "$USER" 2>/dev/null; then
  say "Could not enable lingering. Run manually: sudo loginctl enable-linger $USER"
fi

sleep 2
say "Done. Current status:"
systemctl --user status "$SERVICE" --no-pager || true

cat <<MSG

Useful:
  ./deploy.sh logs      live logs
  ./deploy.sh status    status
  ./deploy.sh           redeploy after changing the code
MSG