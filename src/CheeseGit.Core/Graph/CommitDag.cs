using System.Collections;
using CheeseGit.Core.Git;

namespace CheeseGit.Core.Graph;

/// <summary>
/// The commit topology of a repository (or of the part reachable from some
/// refs), in git's topological order: every commit comes before its parents.
/// Commits are addressed by their row index; parents are stored in a
/// compressed sparse row layout.
/// </summary>
public sealed class CommitDag
{
    private readonly Oid[] oids;
    private readonly long[] commitTimes;
    private readonly int[] parentStart;
    private readonly int[] parents;
    private readonly Dictionary<Oid, int> indexOf;

    internal CommitDag(Oid[] oids, long[] commitTimes, int[] parentStart, int[] parents, Dictionary<Oid, int> indexOf)
    {
        this.oids = oids;
        this.commitTimes = commitTimes;
        this.parentStart = parentStart;
        this.parents = parents;
        this.indexOf = indexOf;
    }

    public int Count => this.oids.Length;

    public Oid OidAt(int index) => this.oids[index];

    public long CommitTimeAt(int index) => this.commitTimes[index];

    /// <summary>Parent row indices; -1 marks a parent outside this DAG (e.g. shallow boundary).</summary>
    public ReadOnlySpan<int> ParentsOf(int index) =>
        this.parents.AsSpan(this.parentStart[index], this.parentStart[index + 1] - this.parentStart[index]);

    public int IndexOf(Oid oid) => this.indexOf.TryGetValue(oid, out var i) ? i : -1;

    /// <summary>Marks every commit reachable from <paramref name="tips"/>.</summary>
    public BitArray ReachableFrom(IEnumerable<int> tips)
    {
        var seen = new BitArray(this.Count);
        var stack = new Stack<int>();
        foreach (var tip in tips)
        {
            if (tip >= 0 && !seen[tip])
            {
                seen[tip] = true;
                stack.Push(tip);
            }
        }
        while (stack.Count > 0)
        {
            foreach (var p in this.ParentsOf(stack.Pop()))
            {
                if (p < 0 || seen[p]) continue;
                seen[p] = true;
                stack.Push(p);
            }
        }
        return seen;
    }

    /// <summary>
    /// The sub-DAG of the selected commits, keeping their relative order (a
    /// subsequence of a topological order is still topological). Edges to
    /// unselected parents become -1. Returns the mapping back to this DAG.
    /// </summary>
    public (CommitDag Dag, int[] OriginalIndex) Filter(BitArray keep)
    {
        var newIndex = new int[this.Count];
        var original = new List<int>();
        for (var i = 0; i < this.Count; i++)
        {
            newIndex[i] = keep[i] ? original.Count : -1;
            if (keep[i]) original.Add(i);
        }

        var oids = new Oid[original.Count];
        var times = new long[original.Count];
        var parentStart = new int[original.Count + 1];
        var parents = new List<int>(original.Count + original.Count / 4);
        var indexOf = new Dictionary<Oid, int>(original.Count);
        for (var n = 0; n < original.Count; n++)
        {
            var i = original[n];
            oids[n] = this.oids[i];
            times[n] = this.commitTimes[i];
            indexOf[oids[n]] = n;
            parentStart[n] = parents.Count;
            foreach (var p in this.ParentsOf(i)) parents.Add(p < 0 ? -1 : newIndex[p]);
        }
        parentStart[original.Count] = parents.Count;
        return (new CommitDag(oids, times, parentStart, parents.ToArray(), indexOf), original.ToArray());
    }

    /// <summary>Incrementally builds a DAG from commits arriving in topological order.</summary>
    public sealed class Builder(int capacity = 1024)
    {
        private readonly List<Oid> oids = new(capacity);
        private readonly List<long> times = new(capacity);
        private readonly List<int> parentStart = new(capacity + 1);
        // Parents usually arrive before they are loaded themselves (children
        // come first), so they are kept as ids until Build resolves them.
        private readonly List<Oid> parents = new(capacity + capacity / 4);

        public int Count => this.oids.Count;

        public void BeginCommit(Oid oid, long commitTime)
        {
            this.oids.Add(oid);
            this.times.Add(commitTime);
            this.parentStart.Add(this.parents.Count);
        }

        public void AddParent(Oid parent) => this.parents.Add(parent);

        public CommitDag Build()
        {
            var oidArray = this.oids.ToArray();
            var indexOf = new Dictionary<Oid, int>(oidArray.Length);
            for (var i = 0; i < oidArray.Length; i++) indexOf[oidArray[i]] = i;

            var parentArray = new int[this.parents.Count];
            for (var i = 0; i < parentArray.Length; i++)
            {
                parentArray[i] = indexOf.TryGetValue(this.parents[i], out var p) ? p : -1;
            }
            var starts = new int[oidArray.Length + 1];
            this.parentStart.CopyTo(starts);
            starts[oidArray.Length] = parentArray.Length;
            return new CommitDag(oidArray, this.times.ToArray(), starts, parentArray, indexOf);
        }
    }
}
