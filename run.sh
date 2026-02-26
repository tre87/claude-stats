#!/bin/bash
# claude-stats launcher — builds in Release if source is newer than the binary, then runs.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$SCRIPT_DIR/ClaudeStats.Console/ClaudeStats.Console.csproj"
BINARY="$SCRIPT_DIR/ClaudeStats.Console/bin/Release/net10.0/ClaudeStats.Console"
SOURCE_DIR="$SCRIPT_DIR/ClaudeStats.Console"

needs_build() {
    # No binary yet
    [ ! -f "$BINARY" ] && return 0

    # Any .cs or .csproj file newer than the binary triggers a rebuild
    while IFS= read -r -d '' file; do
        if [ "$file" -nt "$BINARY" ]; then
            return 0
        fi
    done < <(find "$SOURCE_DIR" \( -name "*.cs" -o -name "*.csproj" \) -not -path "*/obj/*" -not -path "*/bin/*" -print0)

    return 1
}

if needs_build; then
    echo "Building..."
    dotnet build "$PROJECT" -c Release --nologo -v quiet
    if [ $? -ne 0 ]; then
        echo "Build failed." >&2
        exit 1
    fi
fi

exec "$BINARY" "$@"
