/** Simple LRU cache with a fixed maximum size. */
export class LruCache<K, V> {
  private entries = new Map<K, V>();

  constructor(private maxSize: number) {}

  get(key: K): V | undefined {
    const entry = this.entries.get(key);
    if (entry !== undefined) {
      // Move to end (most recent).
      this.entries.delete(key);
      this.entries.set(key, entry);
    }
    return entry;
  }

  set(key: K, value: V): void {
    this.entries.delete(key);
    this.entries.set(key, value);
    if (this.entries.size > this.maxSize) {
      const first = this.entries.keys().next().value!;
      this.entries.delete(first);
    }
  }

  delete(key: K): boolean {
    return this.entries.delete(key);
  }

  clear(): void {
    this.entries.clear();
  }
}
