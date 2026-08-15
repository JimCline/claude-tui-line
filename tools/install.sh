#!/usr/bin/env bash
# One-shot dev build+confidence script. Mechanizes README.md's "By hand" install steps
# (dotnet publish + settings.json wiring) and adds a freshness proof, so it is always
# possible to tell from the script's own output whether the running binary matches the
# checked-out commit. Safe to rerun: publish output directories are overwritten in place,
# and an existing symlink/settings.json entry from a previous run is updated in place.
#
# Only touches ~/.claude/settings.json if the interactive settings.json prompt (step 7) is
# accepted — and then only the statusLine key, with a timestamped backup taken first. Never
# runs `git pull` or touches git state — it reports what is checked out and builds it,
# nothing more.

set -euo pipefail

cd "$(dirname "$0")/.." || exit 2

pass() { printf '  \033[32m\xe2\x9c\x93\033[0m %s\n' "$1"; }
fail() { printf '  \033[31m\xe2\x9c\x97\033[0m %s\n' "$1"; }
warn() { printf '  \033[33m!\033[0m %s\n' "$1"; }
info() { printf '  %s\n' "$1"; }

epoch_to_date() {
    date -r "$1" 2>/dev/null || date -d "@$1"
}

mtime_epoch() {
    stat -f %m "$1" 2>/dev/null || stat -c %Y "$1"
}

echo
echo "claude-tui-line — dev install"
echo

# 1. Toolchain check
echo "1. Toolchain"
if ! dotnet_version=$(dotnet --version 2>/dev/null); then
    fail "dotnet not found on PATH"
    info "Install the .NET 10 SDK: https://dotnet.microsoft.com/download"
    exit 1
fi

dotnet_major="${dotnet_version%%.*}"
if [[ ! "$dotnet_major" =~ ^[0-9]+$ ]] || (( dotnet_major < 10 )); then
    fail "dotnet $dotnet_version found, but .NET 10+ is required"
    info "Install the .NET 10 SDK: https://dotnet.microsoft.com/download"
    exit 1
fi
pass "dotnet $dotnet_version"
echo

# 2. Repo state — reported, never mutated: no `git pull`, no touching the remote.
echo "2. Repo state"
commit=$(git rev-parse --short HEAD)
commit_epoch=$(git log -1 --format=%ct)
pass "HEAD is $commit ($(epoch_to_date "$commit_epoch"))"

dirty=$(git status --porcelain)
if [[ -n "$dirty" ]]; then
    warn "working tree is dirty — uncommitted changes present:"
    while IFS= read -r line; do
        info "    $line"
    done <<< "$dirty"
else
    pass "working tree clean"
fi
echo

# 3. Build: claude-tui-line
echo "3. Build: claude-tui-line"
log_dir=$(mktemp -d)
cli_log="$log_dir/cli-publish.log"
if dotnet publish src/ClaudeTuiLine/ClaudeTuiLine.csproj -c Release -o publish > "$cli_log" 2>&1; then
    cli_bin="$(pwd)/publish/claude-tui-line"
    pass "built -> $cli_bin"
else
    fail "dotnet publish failed — last 30 lines of $cli_log:"
    tail -n 30 "$cli_log" | sed 's/^/    /'
    exit 1
fi
echo

# 4. Symlink into the user's bin path, so settings.json doesn't need the full repo path.
# Never creates a bin directory and never touches $PATH — only uses a candidate that's
# already a directory on $PATH. A previous run's symlink (pointing at any clone) is
# updated in place; a real file already at that path is left alone.
echo "4. Symlink"
bin_dir=""
for candidate in "$HOME/.local/bin" "$HOME/bin"; do
    if [[ -d "$candidate" ]] && [[ ":$PATH:" == *":$candidate:"* ]]; then
        bin_dir="$candidate"
        break
    fi
done

symlink_path=""
if [[ -z "$bin_dir" ]]; then
    warn "no bin dir on PATH (checked \$HOME/.local/bin, \$HOME/bin) — skipping symlink, settings.json wire-up will use the full path"
else
    link_target="$bin_dir/claude-tui-line"
    if [[ -e "$link_target" && ! -L "$link_target" ]]; then
        warn "$link_target already exists and is not a symlink — skipping, settings.json wire-up will use the full path"
    else
        ln -sf "$cli_bin" "$link_target"
        symlink_path="$link_target"
        pass "symlinked -> $symlink_path"
    fi
fi
echo

# 5. Build: claude-tui-line-mcp
#
# ClaudeTuiLineMcp.csproj is OutputType=Exe with its own Program.cs and Microsoft.Extensions
# .Hosting — a real standalone MCP server process, not a library referenced only by other
# projects. It has no PublishAot and no publish profile of its own, so this publishes it
# framework-dependent (needs the .NET 10 runtime present at run time, unlike the AOT CLI).
#
# This step is allowed to fail without aborting the script, so an MCP publish problem never
# blocks the CLI install above.
echo "5. Build: claude-tui-line-mcp"
mcp_log="$log_dir/mcp-publish.log"
if dotnet publish src/ClaudeTuiLineMcp/ClaudeTuiLineMcp.csproj -c Release -o publish-mcp > "$mcp_log" 2>&1; then
    mcp_bin="$(pwd)/publish-mcp/claude-tui-line-mcp"
    pass "built -> $mcp_bin"
else
    warn "MCP publish currently fails (NETSDK1151: self-contained/non-self-contained ProjectReference conflict), tracked separately, CLI build above is unaffected"
fi
echo

# 6. Freshness proof
echo "6. Freshness"
bin_version=$("$cli_bin" --version)
bin_mtime_epoch=$(mtime_epoch "$cli_bin")
pass "binary version: $bin_version"
info "binary built:   $(epoch_to_date "$bin_mtime_epoch")"
info "commit $commit: $(epoch_to_date "$commit_epoch")"
if (( bin_mtime_epoch < commit_epoch )); then
    warn "binary mtime predates HEAD's commit time — that shouldn't happen right after a publish; rerun this script if you see it"
else
    pass "binary is newer than HEAD's commit"
fi
echo

# 7. Wire-up (~/.claude/settings.json) — interactive when run from a real terminal: on
# acceptance, updates ONLY the statusLine key (via jq, never hand-rolled JSON surgery) and
# backs up the existing file first. Non-interactive (CI, piped stdin) always falls back to
# print-only, so this step is safe to run unattended.
echo "7. Wire-up (~/.claude/settings.json)"
wire_command="$cli_bin"
[[ -n "$symlink_path" ]] && wire_command="$symlink_path"
settings_file="$HOME/.claude/settings.json"

print_snippet() {
    info "Add or update the statusLine block, with the path just built already substituted in:"
    echo
    cat <<EOF
    {
      "statusLine": {
        "type": "command",
        "command": "$wire_command",
        "refreshInterval": 1
      }
    }
EOF
    echo
    info "This script does not modify ~/.claude/settings.json — paste the block above by hand."
}

if [[ ! -t 0 ]]; then
    print_snippet
else
    printf "  Update %s's statusLine to point at %s? [y/N] " "$settings_file" "$wire_command"
    read -r reply || reply=""
    if [[ "$reply" =~ ^[Yy]$ ]]; then
        if ! command -v jq >/dev/null 2>&1; then
            warn "jq not found on PATH — required to edit settings.json safely, skipping the write"
            print_snippet
        else
            tmp_settings=$(mktemp)
            if [[ -f "$settings_file" ]]; then
                backup="$settings_file.bak-$(date +%Y%m%d%H%M%S)"
                cp "$settings_file" "$backup"
                jq --arg cmd "$wire_command" \
                    '.statusLine = {"type": "command", "command": $cmd, "refreshInterval": 1}' \
                    "$settings_file" > "$tmp_settings"
                mv "$tmp_settings" "$settings_file"
                pass "updated $settings_file (backup: $backup)"
            else
                mkdir -p "$(dirname "$settings_file")"
                jq -n --arg cmd "$wire_command" \
                    '{"statusLine": {"type": "command", "command": $cmd, "refreshInterval": 1}}' \
                    > "$tmp_settings"
                mv "$tmp_settings" "$settings_file"
                pass "created $settings_file (no prior file to back up)"
            fi
        fi
    else
        print_snippet
    fi
fi
echo

# 8. Preview — visual proof the new build works, using the freshly built binary.
echo "8. Preview (--preview)"
"$cli_bin" --preview
echo

pass "done"
