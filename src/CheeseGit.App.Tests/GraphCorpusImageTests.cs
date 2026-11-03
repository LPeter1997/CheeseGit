using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CheeseGit.App.Controls;
using CheeseGit.Core.Git;
using CheeseGit.Core.Repository;
using CheeseGit.Core.Tests;
using CheeseGit.Fixtures;

namespace CheeseGit.App.Tests;

/// <summary>
/// The graph corpus (docs/testing.md, Graph corpus) drawn with <see cref="GraphCell.DrawRow"/> and compared with the
/// reviewed images in <c>GraphCorpus/</c>. The images hold the graph only, no text, so fonts
/// cannot make them differ between machines.
/// <para>
/// <c>CHEESEGIT_UPDATE_GRAPH_CORPUS=1</c> rewrites the images (review them before committing);
/// <c>CHEESEGIT_GRAPH_REVIEW=&lt;dir&gt;</c> also writes copies labelled with each row's subject and refs.
/// </para>
/// </summary>
public class GraphCorpusImageTests
{
    private const double RowHeight = 32;

    /// <summary>Pixels may differ this much per channel (anti-aliasing across Skia builds)...</summary>
    private const int ChannelTolerance = 24;

    /// <summary>...and this share of pixels may differ more.</summary>
    private const double PixelTolerance = 0.002;

    private static readonly Color Background = Color.Parse("#1c1c1f");
    private static readonly IPen HeadRing = new Pen(new SolidColorBrush(Color.Parse("#e5e5e5")), 2);

    /// <summary>Every case, plus hover variants: the named commit's dot is under the pointer, so the other chains fade.</summary>
    public static TheoryData<string> Cases => [.. GraphCorpus.Cases.Select(c => c.Name), .. Hovered.Keys];

    private static readonly Dictionary<string, (string Case, string Subject)> Hovered = new()
    {
        ["wide@hover-f7"] = ("wide", "f7 1"),
        ["repeated-merge-backs@hover-develop"] = ("repeated-merge-backs", "develop 2"),
    };

    private static string CorpusDirectory([CallerFilePath] string source = "") =>
        Path.Combine(Path.GetDirectoryName(source)!, "GraphCorpus");

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public async Task Corpus_graph_matches_its_reviewed_image(string name)
    {
        var (caseName, hoveredSubject) = Hovered.TryGetValue(name, out var hovered) ? hovered : (name, null);
        using var dir = TempRepo.EmptyDirectory();
        var fixture = await GraphCorpus.CreateAsync(GraphCorpus.Cases.Single(c => c.Name == caseName), dir.Path);
        var view = await fixture.LoadViewAsync(dir.Git);
        Oid? head;
        var highlight = -1;
        using (var repo = await GitRepository.OpenAsync(fixture.Path, dir.Git))
        {
            head = (await repo.HeadAsync()).Commit;
            if (hoveredSubject is not null)
            {
                var row = Enumerable.Range(0, view.Count).Single(r => repo.GetCommit(view.Dag.OidAt(r))?.Summary == hoveredSubject);
                highlight = view.Layout.NodeColor(row);
            }
        }

        var actual = Render(view, head, labels: null, highlight);
        var expectedPath = Path.Combine(CorpusDirectory(), name + ".png");

        if (Environment.GetEnvironmentVariable("CHEESEGIT_GRAPH_REVIEW") is { Length: > 0 } review)
        {
            Directory.CreateDirectory(review);
            using var repo = await GitRepository.OpenAsync(fixture.Path, dir.Git);
            var labels = Enumerable.Range(0, view.Count).Select(row =>
            {
                var oid = view.Dag.OidAt(row);
                var refs = string.Join(", ", view.Graph.RefsOf(oid).Select(r => r.ShortName));
                var text = repo.GetCommit(oid)?.Summary ?? oid.ToShortHex();
                return refs.Length > 0 ? $"{text}  [{refs}]" : text;
            }).ToList();
            await File.WriteAllBytesAsync(Path.Combine(review, name + ".png"), Render(view, head, labels, highlight));
        }

        if (Environment.GetEnvironmentVariable("CHEESEGIT_UPDATE_GRAPH_CORPUS") == "1")
        {
            Directory.CreateDirectory(CorpusDirectory());
            await File.WriteAllBytesAsync(expectedPath, actual);
            return;
        }

        var actualPath = Path.Combine(AppContext.BaseDirectory, "graph-corpus-actual", name + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(actualPath)!);
        await File.WriteAllBytesAsync(actualPath, actual);
        Assert.True(File.Exists(expectedPath), $"No reviewed image for '{name}'. The render is at {actualPath}; review it and run with CHEESEGIT_UPDATE_GRAPH_CORPUS=1.");
        var difference = Compare(await File.ReadAllBytesAsync(expectedPath), actual);
        Assert.True(difference is null, $"'{name}' differs from {expectedPath}: {difference}. The render is at {actualPath}.");
    }

    /// <summary>The whole graph as a PNG, one <see cref="RowHeight"/> row per commit, optionally labelled.</summary>
    private static byte[] Render(HistoryView view, Oid? head, IReadOnlyList<string>? labels, int highlight = -1)
    {
        var graphWidth = GraphCell.ColumnWidth(view.Layout.LaneCount) + 8;
        var width = labels is null ? graphWidth : graphWidth + 460;
        var size = new PixelSize((int)Math.Ceiling(width), (int)Math.Ceiling(view.Count * RowHeight));
        using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
        var background = new SolidColorBrush(Background);
        var text = new SolidColorBrush(Color.Parse("#d4d4d8"));
        using (var context = bitmap.CreateDrawingContext())
        {
            context.FillRectangle(background, new Rect(0, 0, size.Width, size.Height));
            for (var row = 0; row < view.Count; row++)
            {
                using (context.PushTransform(Matrix.CreateTranslation(0, row * RowHeight)))
                {
                    var ring = view.Dag.OidAt(row) == head ? HeadRing : null;
                    GraphCell.DrawRow(context, view.Layout, row, RowHeight, view.IsLocalOnly(row), background, ring, highlight, highlight >= 0 ? 1 : 0);
                    if (labels is not null)
                    {
                        var label = new FormattedText(labels[row], CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 12, text);
                        context.DrawText(label, new Point(graphWidth, (RowHeight - label.Height) / 2));
                    }
                }
            }
        }
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    /// <summary>Null when the images match within the tolerances, else what differs.</summary>
    private static string? Compare(byte[] expectedPng, byte[] actualPng)
    {
        using var expected = new Bitmap(new MemoryStream(expectedPng));
        using var actual = new Bitmap(new MemoryStream(actualPng));
        if (expected.PixelSize != actual.PixelSize) return $"size {actual.PixelSize}, expected {expected.PixelSize}";
        var a = Pixels(expected);
        var b = Pixels(actual);
        var differing = 0;
        for (var i = 0; i < a.Length; i += 4)
        {
            for (var c = 0; c < 4; c++)
            {
                if (Math.Abs(a[i + c] - b[i + c]) > ChannelTolerance)
                {
                    differing++;
                    break;
                }
            }
        }
        var share = (double)differing / (a.Length / 4);
        return share > PixelTolerance ? $"{differing} pixels ({share:P2}) differ" : null;
    }

    private static byte[] Pixels(Bitmap bitmap)
    {
        var stride = bitmap.PixelSize.Width * 4;
        var buffer = new byte[stride * bitmap.PixelSize.Height];
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), handle.AddrOfPinnedObject(), buffer.Length, stride);
        }
        finally
        {
            handle.Free();
        }
        return buffer;
    }
}
