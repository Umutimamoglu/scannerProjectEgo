#!/usr/bin/env sh
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
WRAPPER_DIR="$SCRIPT_DIR/.mvn/wrapper"
JAR="$WRAPPER_DIR/maven-wrapper.jar"
WRAPPER_URL="https://repo1.maven.org/maven2/io/takari/maven-wrapper/0.5.6/maven-wrapper-0.5.6.jar"

if [ ! -f "$JAR" ]; then
  echo "Downloading Maven Wrapper..."
  if command -v curl >/dev/null 2>&1; then
    curl -L -o "$JAR" "$WRAPPER_URL"
  elif command -v wget >/dev/null 2>&1; then
    wget -O "$JAR" "$WRAPPER_URL"
  else
    echo "Error: curl or wget is required to download Maven Wrapper."
    exit 1
  fi
fi

exec java -jar "$JAR" "$@"
