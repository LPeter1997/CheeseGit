/**
 * Small helper for persisting transient UI state per repository path.
 * Keeps the same semantics as a plain Map while making intent explicit.
 */
export class PerRepoStateCache<T> {
  private readonly entries = new Map<string, T>();

  save(repoPath: string | null, state: T): void {
    if (!repoPath) return;
    this.entries.set(repoPath, state);
  }

  load(repoPath: string): T | undefined {
    return this.entries.get(repoPath);
  }

  delete(repoPath: string): boolean {
    return this.entries.delete(repoPath);
  }

  clear(): void {
    this.entries.clear();
  }
}