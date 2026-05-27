/**
 * Coordinates async request ordering and mutation invalidation.
 *
 * - `nextRequest` issues a monotonically increasing request id.
 * - `isLatest` ensures only the newest in-flight request can apply.
 * - `markMutation` bumps a separate mutation counter.
 * - `snapshotMutation` + `isMutationUnchanged` guards request results that
 *   started before a local optimistic mutation.
 */
export class RequestSequencer {
  private requestSeq = 0;
  private mutationSeq = 0;

  nextRequest(): number {
    this.requestSeq += 1;
    return this.requestSeq;
  }

  isLatest(requestId: number): boolean {
    return requestId === this.requestSeq;
  }

  cancelInflight(): void {
    this.requestSeq += 1;
  }

  markMutation(): void {
    this.mutationSeq += 1;
  }

  snapshotMutation(): number {
    return this.mutationSeq;
  }

  isMutationUnchanged(snapshot: number): boolean {
    return snapshot === this.mutationSeq;
  }
}