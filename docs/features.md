# Features

How each part of the app behaves, and why where it is not obvious. Tests named here cover the behaviour.

## Repositories and tabs

- Open, init and clone; one tab per repository, kept across restarts with the window's size and position. A saved
  position on no screen (an unplugged monitor) is dropped; Wayland reports no positions, so only the size is kept.
- Opening a folder inside a repository opens the repository's root; opening another folder of an open repository
  selects its tab.
- Tabs reorder by dragging (the dragged tab follows the pointer, the others slide aside) and shrink evenly when many
  are open.
- When git is missing or too old, the tab says so: "Install git 2.34 or newer and make sure it is on PATH."

## Staging and committing

- Unstaged and staged lists with a name filter, per-file +/− totals, and stage, unstage or discard per file, for
  all, or per line in the diff. Holding Shift turns the arrows into discard. Up/Down move within a list.
  Right-clicking a file copies its path or name.
- Staging a conflicted file that still has markers asks first ("Stage anyway").
- Commit with a summary and description (Ctrl+Enter), an empty commit, and undo of the last commit when it is not
  pushed. Undoing a merge commit resets to the first parent: the merge becomes ordinary staged changes.
- During a merge, revert, cherry-pick or rebase the commit button reads "Merge in progress" and is disabled; the
  banner finishes the operation.
- Commit alerts replace each other and leave after 15 s; their Undo checks that the commit is still the tip.

## Diffs

- Unified or split, syntax highlighting, inline word highlights, search (Ctrl+F). Clicking the path in the header
  copies it.
- A diff whose changed lines mix line endings labels each line `LF` or `CRLF`, so an LF ↔ CRLF change does not look
  like identical lines removed and added.
- Conflicted files show the working tree against HEAD, so the markers are visible; their lines cannot be staged.
- **Images** (png, apng, jpg, gif, bmp, webp, ico, wbmp: what Skia decodes): Before and After cards side by side,
  each with dimensions and size, and a line saying what changed. Transparency sits on a checkerboard. Small images
  scale up by a whole factor to about 128 px with nearest-neighbour sampling; large ones shrink to fit. Versions over
  32 MB are not read. Animated images show their first frame.
- **Git LFS files** show their content, not their pointer:
  - Detection is by content (`LfsPointer.TryParse`, per the spec), not attributes.
  - A version is read from the local LFS store (`lfs.storage`, else `lfs/objects/` in the common git dir). Missing
    objects download with `git lfs smudge`, through the askpass bridge: images by themselves up to
    *Settings → LFS previews* (default 10 MB), larger ones with a Download button. Failures are remembered per object
    for the tab.
  - Non-image LFS files say "Stored in Git LFS (2.9 KB → 4.9 KB)". Lines of a pointer diff cannot be staged (a mix of
    two pointers names no object); the engine refuses too.
  - Rows of LFS files show an LFS tag and no line counts. Status runs `check-attr` only when the repository may use
    LFS; a commit's files use that commit's attributes (git 2.40+).
  - A banner offers to install or set up Git LFS when the repository uses it and it is missing or not configured.
  - A large binary file that is added and not in LFS gets an offer to track its extension with Git LFS.
  - In conflicts, LFS and binary files offer only Current and Incoming: a union would be a broken pointer or garbage.
- Other binary files say so in one line (see `roadmap.md` for the planned info card).

## History

- Every commit in a virtualized list with the graph (`architecture.md`), subject, author, date, line totals and short
  id. Text loads lazily for the rows on screen, 32 rows ahead, and line totals follow separately so a big commit does
  not hold the text back.
- Selecting a commit lists its files and shows their diffs.
- Clicking a dot checks the commit out ("browsing history"), or switches to the branch whose tip it is.
  "Jump back to present" returns to the branch that was being browsed. Syncing is disabled while browsing.
- The branch visibility dropdown has an eye per branch and Show all / Hide all. It starts with only the required
  branches. The graph holds the local branches plus the remote's default branch (for fork points).
- Search covers the whole history: the first search after each load streams one `git log`, then each keystroke
  filters in memory by id, subject, author and ref names. The graph is hidden while searching.
- Revert from a row's button; cherry-pick by selecting commits (Ctrl toggles, Shift selects a range) and choosing a
  branch, or typing a new one to create at HEAD.

## Branches

The branch name in the branch bar opens the dropdown:
- "Find or create a branch…" filters local and remote branches. Enter switches to an exact match or creates it.
- Local branches, most recently committed first, with visibility, merge and delete buttons. A "Remote" section lists
  remote branches with no local branch of their name; clicking one creates a tracking branch
  (`switch --track -c`, naming the remote, so two remotes with the same branch are not ambiguous).
- Creating takes a **base**: the current branch (or the browsed commit) by default, or any branch. A branch made from
  another branch does not track it.
- Delete is forced, with a dialog as the confirmation. It warns when the branch has commits on no other branch, and
  offers to delete the remote branch too when its upstream still exists (asked with `ls-remote`, since the local
  remote-tracking ref may be stale). The current branch cannot be deleted.
- Merge's tooltip predicts the result ("No conflicts expected", "Conflicts expected in 2 files: …", "Already up to
  date"; git 2.38+), computed when the tooltip opens.

**Changes on switch.** When a switch finds local changes, a dialog asks: "Bring to <new>" (git's behaviour) or
"Leave on <old>". The answer can be remembered (Settings → Changes on switch).
- Leaving stashes everything, untracked files too, as "CheeseGit: changes left on <branch>"; a failed switch pops it.
- Switching back to a branch with such a stash restores it (`stash apply --index`, then drop) when the tree is
  clean, so staged changes stay staged. Otherwise it waits in the stash and an alert says so.
- Creating a branch from the current one takes the changes along without asking.

## Remotes

The remote button in the branch bar does one thing against the chosen remote, in this order: "Publish to X" when the
branch is not there, "Pull N" when behind, "Push N" when ahead, else "Fetch X"; "Add remote" with no remotes.
Ahead/behind is its tooltip.
- **Progress:** the label follows git's progress meter ("Receiving objects 45%") with a thin bar; the command log keeps
  only each meter's last state. Calls run with `COLUMNS=1000`, because git splits a meter wider than 80 columns into
  two lines when stderr is a pipe. Small fetches (under `fetch.unpackLimit`) show only the server's phases: git's
  `unpack-objects` draws its meter only on a terminal.
- **Cancel:** clicking while it runs kills git and ssh, so a remote that hangs can always be stopped.
- **Pull** without `pull.rebase`/`pull.ff` configured passes `--no-rebase` (git 2.27+ otherwise refuses diverged
  branches). A configured choice is left to git. A pull that merges with conflicts opens the conflict dialog.
- **Push rejected because the remote moved:** the engine fetches and counts the commits HEAD lacks, and the app says
  "Not pushed: origin has 2 commits that this branch does not have" with a Pull action. Other refusals show git's
  error.
- **Fetch on a case-insensitive file system:** when the remote deleted branches differing only in case, git prunes
  both in one transaction, both take the same lock file, and every fetch fails from then on. When `core.ignorecase`
  is set and a fetch fails, `FetchAsync` lists the stale refs (`fetch --prune --dry-run --porcelain`, git 2.41+),
  deletes them in rounds that never hold two case-equal names, and fetches again.
- The chevron opens the remote picker: remotes with URLs, remove, and Add remote. The first remote is named
  "origin", later ones after the host (`git@github.com:…` → "github"). Adding checks that the remote answers
  (`ls-remote`) and removes it again if not, keeping the dialog open to fix the URL.

## Merge, conflicts, revert, cherry-pick

- **Merge** from the branch dropdown: "Merged N commits from X", "Already up to date", or the conflict dialog. Merge
  messages come from `fmt-merge-msg`, so they name the branch, not `refs/heads/…`.
- **Revert** makes a new commit, or opens the conflict dialog.
- **Cherry-pick** runs the selected commits oldest first in one `git cherry-pick -x`. A conflict pauses the sequence
  and opens the dialog; completing continues it, and a later conflict reuses the dialog. Picks that become empty are
  skipped and counted. If git refuses outright (local changes), `cherry-pick --quit` clears the sequencer.
- **The conflict dialog** (merges, reverts, cherry-picks): one row per file with its conflict count and Current /
  Incoming / Both. Complete is enabled once every file is chosen or resolved elsewhere.
  - The message is git's prepared one, editable. Continue goes through `MERGE_MSG`, so the sequencer state stays
    git's.
  - A file whose markers were removed in an editor shows "Resolved" and is staged as it is on disk
    (`ConflictResolution.KeepWorkingTree`), so the user's edit is kept. A file staged in a terminal needs nothing.
    The dialog re-reads on every status refresh.
  - "Open in <tool>" runs an external merge tool with the three versions (from the index stages, with the file's
    extension) and the working-tree file as output, with the arguments `git mergetool` uses: VS Code, NeoVim, Vim (in
    a terminal), Rider, IntelliJ, KDiff3, Meld, Beyond Compare, Visual Studio. Tools are found on PATH; the choice is
    remembered.
  - Abort, or Later to leave the operation paused. Finished in a terminal, the dialog closes by itself.
- **The banner** shows a paused merge, revert, cherry-pick or rebase, also one started outside the app. A rebase can
  be continued or aborted from it; the branch bar names the branch being rebased.

## Stash

- Entries with their message (git's "On branch:" prefix split off), author, date, totals and short hash. Apply, pop
  and drop ask first. Selecting an entry shows its files and diffs.
- Untracked files stashed with `-u` are listed and diffed (they live in the stash's third parent, which
  `git stash show` skips).
- Entries are followed by commit id, so a stash made in a terminal (which renumbers every entry) does not make an
  action hit the wrong one.
- A pop that conflicts keeps the entry, as git does, and says so.

## Settings and the rest

- Settings: theme, diff layout, changes on switch, merge tool, native Wayland, LFS previews, update checks.
- Updates and What's new: see `release.md`.
- AppImage: asks once to add itself to the applications menu; the options menu keeps "Add to Applications Menu".
- On first start without settings, the old app's `state.json` gives the open tabs, theme and merge tool.
