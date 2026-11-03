#!/usr/bin/env bash
# CI: runs the app tests, and if they still run after STACK_AFTER seconds, prints the managed stacks of every
# thread in the test host (dotnet-stack). The app tests share one UI thread, so one blocked test stalls them all;
# --blame-hang only names the tests in flight, the stacks show where it blocks. Works on Windows (Git Bash) too.
set -u
cd "$(dirname "$0")"
tools="${RUNNER_TEMP:-${TMPDIR:-/tmp}}/cheesegit-ci-tools"

dotnet test CheeseGit.App.Tests --logger "console;verbosity=minimal" --blame-hang-timeout 10m --blame-hang-dump-type none &
test_pid=$!

(
    # One-second sleeps: killing this subshell must not leave a long sleep holding the step's output open.
    for _ in $(seq "${STACK_AFTER:-300}"); do sleep 1; done
    echo "::group::App tests still running after ${STACK_AFTER:-300} s: managed stacks of the test host"
    # The tool targets an older runtime than the SDK on the runner.
    export DOTNET_ROLL_FORWARD=Major
    dotnet tool install --tool-path "$tools" dotnet-stack > /dev/null 2>&1 || dotnet tool update --tool-path "$tools" dotnet-stack > /dev/null 2>&1
    # xUnit v3 runs the tests in their own process (CheeseGit.App.Tests), not in testhost.
    pids=$("$tools/dotnet-stack" ps 2>/dev/null | tr -d '\r' | grep -E "CheeseGit\.App\.Tests|testhost" | awk '{print $1}')
    for pid in $pids; do
        echo "--- process $pid"
        "$tools/dotnet-stack" report -p "$pid" 2>&1
    done
    echo "::endgroup::"
) &
watch_pid=$!

wait "$test_pid"
status=$?
kill "$watch_pid" 2> /dev/null
exit "$status"
