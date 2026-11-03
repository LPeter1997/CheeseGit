namespace CheeseGit.Core.Graph;

/// <summary>
/// Checks that a <see cref="GraphLayout"/> draws every parent edge of its
/// <see cref="CommitDag"/> as one continuous line, and that no lane ever
/// carries two different edges at once.
/// </summary>
public static class LayoutValidator
{
    /// <summary>Returns null when valid, otherwise a description of the first problem.</summary>
    public static string? Validate(CommitDag dag, GraphLayout layout)
    {
        if (dag.Count != layout.RowCount) return $"row count {layout.RowCount} != commit count {dag.Count}";

        // lane (at the top edge of the current row) -> row of the commit the line is heading to
        var carry = new Dictionary<int, int>();
        // parent row -> number of broken edges that must arrive there
        var stubsInto = new Dictionary<int, int>();
        for (var row = 0; row < dag.Count; row++)
        {
            var node = layout.NodeLane(row);
            if (node < 0 || node >= layout.LaneCount) return $"row {row}: node lane {node} out of range";
            var segments = layout.SegmentsOf(row).ToArray();

            // Upper half: exactly the lines heading here converge into the dot.
            var arriving = carry.Where(kv => kv.Value == row).Select(kv => kv.Key).ToHashSet();
            var tops = segments.Where(s => s.Kind == SegmentKind.Top).ToList();
            if (tops.Any(s => s.ToLane != node)) return $"row {row}: a converging line misses the commit";
            if (!arriving.SetEquals(tops.Select(s => (int)s.FromLane)) || tops.Count != arriving.Count)
            {
                return $"row {row}: {arriving.Count} edges arrive but {tops.Count} converge";
            }

            stubsInto.Remove(row, out var expectedStubs);
            var stubsUp = segments.Count(s => s.Kind == SegmentKind.StubUp);
            if (stubsUp != expectedStubs) return $"row {row}: {expectedStubs} broken edges arrive but {stubsUp} stubs are drawn";

            // Every other line passes by exactly once, possibly sliding one way.
            var next = new Dictionary<int, int>();
            var passing = carry.Where(kv => kv.Value != row).Select(kv => kv.Key).ToHashSet();
            var throughs = segments.Where(s => s.Kind == SegmentKind.Through).ToList();
            if (!passing.SetEquals(throughs.Select(s => (int)s.FromLane)) || throughs.Count != passing.Count)
            {
                return $"row {row}: {passing.Count} lanes pass by but {throughs.Count} are drawn";
            }
            foreach (var s in throughs)
            {
                if (s.FromLane == node) return $"row {row}: a passing line crosses the commit dot";
                if (!next.TryAdd(s.ToLane, carry[s.FromLane])) return $"row {row}: two passing lines end in lane {s.ToLane}";
            }

            // Lower half: one edge per parent inside the DAG, in parent order.
            var bottoms = segments.Where(s => s.Kind is SegmentKind.Bottom or SegmentKind.StubDown).ToList();
            var parents = dag.ParentsOf(row).ToArray().Where(p => p >= 0).ToArray();
            if (bottoms.Count != parents.Length) return $"row {row}: {parents.Length} parents but {bottoms.Count} edges";
            for (var i = 0; i < parents.Length; i++)
            {
                var b = bottoms[i];
                if (b.FromLane != node) return $"row {row}: edge does not start at the commit";
                if (parents[i] <= row) return $"row {row}: parent {parents[i]} is not below the commit";
                if (b.Kind == SegmentKind.StubDown)
                {
                    stubsInto[parents[i]] = stubsInto.GetValueOrDefault(parents[i]) + 1;
                    continue;
                }
                if (next.TryGetValue(b.ToLane, out var existing) && existing != parents[i])
                {
                    return $"row {row}: lane {b.ToLane} would carry two edges ({existing} and {parents[i]})";
                }
                next[b.ToLane] = parents[i];
            }
            carry = next;
        }
        if (stubsInto.Count > 0) return $"{stubsInto.Count} broken edges never reach their parent";
        return carry.Count == 0 ? null : $"{carry.Count} edges never reach their parent";
    }
}
