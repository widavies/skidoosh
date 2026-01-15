#!/bin/bash
set -e

REPO="widavies/skidoosh"
SERVICE="skidoosh.service"
INSTALL_PATH="/home/cci/skidoosh"
ASSET_NAME="skidoosh"

if [ "$EUID" -ne 0 ]; then
    echo "Please run as root: curl -sSL <url> | sudo bash"
    exit 1
fi

echo "Fetching latest release..."
TAG=$(curl -s "https://api.github.com/repos/$REPO/releases/latest" | grep '"tag_name"' | cut -d'"' -f4)
echo "Updating to version: $TAG"

DOWNLOAD_URL="https://github.com/$REPO/releases/download/$TAG/$ASSET_NAME"
echo "Downloading from: $DOWNLOAD_URL"
curl -L -o /tmp/skidoosh-new "$DOWNLOAD_URL"

echo "Stopping $SERVICE..."
systemctl stop "$SERVICE"

echo "Installing new binary..."
mv /tmp/skidoosh-new "$INSTALL_PATH"
chmod +x "$INSTALL_PATH"
chown cci:cci "$INSTALL_PATH"

echo "Starting $SERVICE..."
systemctl start "$SERVICE"

echo "Update complete!"
systemctl status "$SERVICE" --no-pager
