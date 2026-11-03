#!/bin/sh
# Runs the engine and app test suites under every git config profile of the
# hostile-environment matrix (see CheeseGit.Core.Tests/TestEnvironment.cs).
set -e
cd "$(dirname "$0")"
dotnet build CheeseGit.Core.Tests -c Release -v q --nologo
dotnet build CheeseGit.App.Tests -c Release -v q --nologo
status=0
for profile in default hostile crlf crlf-input merge status; do
    for suite in Core App; do
        printf '%-12s %-5s ' "$profile" "$suite"
        log="${TMPDIR:-/tmp}/cheesegit-matrix-$profile-$suite.log"
        if CHEESEGIT_TEST_PROFILE=$profile dotnet test "CheeseGit.$suite.Tests" -c Release --no-build --nologo -v q > "$log" 2>&1; then
            grep -o 'Passed:.*Total:[^,]*' "$log" | head -1
        else
            echo "FAILED (see $log)"
            status=1
        fi
    done
done
exit $status
