// Compares two BenchmarkDotNet artifact directories and reports the per-benchmark delta.
//
//   dotnet run .github/scripts/compare-bench.cs -- <baseline-dir> <head-dir> [options]
//
//     --threshold N          percent change treated as meaningful once significant (default 10)
//     --label TEXT           prefix for the report heading and warning annotations
//     --fail-on-regression   exit non-zero when a benchmark regresses beyond the threshold
//
// Significance is decided by confidence-interval overlap, not by a bare percentage. Shared CI
// runners drift enough that a percentage on means alone invents regressions. If the two intervals
// overlap, the runs are statistically indistinguishable and the row is reported as noise however
// large the difference in means looks. The percentage threshold then applies on top of a result that
// is already significant, filtering out real-but-trivial movement.

using System.Globalization;
using System.Text;
using System.Text.Json;

var positional = new List<string>();
var threshold = 10.0;
var label = "";
var failOnRegression = false;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--threshold":
            threshold = double.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
        case "--label":
            label = args[++i];
            break;
        case "--fail-on-regression":
            failOnRegression = true;
            break;
        default:
            positional.Add(args[i]);
            break;
    }
}

if (positional.Count < 2)
{
    Console.Error.WriteLine("usage: compare-bench.cs <baseline-dir> <head-dir> [--threshold N] [--label TEXT] [--fail-on-regression]");
    return 2;
}

var (baseline, baselineFiles) = Load(positional[0]);
var (head, headFiles) = Load(positional[1]);

if (baseline.Count == 0 || head.Count == 0)
{
    Console.WriteLine($"::error::No benchmark results found (baseline: {baseline.Count} records from " +
                      $"{baselineFiles} files, head: {head.Count} records from {headFiles} files)");
    return 1;
}

var rows = new List<string[]>();
var regressions = new List<(string Name, double Delta)>();
var improvements = new List<(string Name, double Delta)>();

foreach (var name in baseline.Keys.Union(head.Keys).OrderBy(k => k, StringComparer.Ordinal))
{
    var shortName = ShortName(name);
    var hasBefore = baseline.TryGetValue(name, out var before);
    var hasAfter = head.TryGetValue(name, out var after);

    if (!hasBefore)
    {
        rows.Add([shortName, "-", after.Mean.ToString("F1", CultureInfo.InvariantCulture), "new", ""]);
        continue;
    }

    if (!hasAfter)
    {
        rows.Add([shortName, before.Mean.ToString("F1", CultureInfo.InvariantCulture), "-", "removed", ""]);
        continue;
    }

    var delta = (after.Mean - before.Mean) / before.Mean * 100.0;
    // Non-overlapping intervals mean the difference exceeds the measured noise.
    var overlap = !(after.Lower > before.Upper || after.Upper < before.Lower);

    string verdict;
    if (overlap)
    {
        verdict = "noise";
    }
    else if (delta > threshold)
    {
        verdict = "SLOWER";
        regressions.Add((shortName, delta));
    }
    else if (delta < -threshold)
    {
        verdict = "faster";
        improvements.Add((shortName, delta));
    }
    else
    {
        verdict = "same";
    }

    rows.Add([
        shortName,
        before.Mean.ToString("F1", CultureInfo.InvariantCulture),
        after.Mean.ToString("F1", CultureInfo.InvariantCulture),
        verdict,
        delta.ToString("+0.0;-0.0", CultureInfo.InvariantCulture) + "%",
    ]);
}

var report = new StringBuilder();
report.AppendLine($"## Benchmark{(label.Length > 0 ? ": " + label : "")}");
report.AppendLine();
report.AppendLine("Mean nanoseconds. `noise` means the confidence intervals overlap, so the two runs are "
                + "statistically indistinguishable regardless of the percentage shown.");
report.AppendLine();
report.AppendLine("| Benchmark | Base | Head | Verdict | Delta |");
report.AppendLine("| --- | ---: | ---: | --- | ---: |");
foreach (var row in rows)
{
    report.AppendLine("| " + string.Join(" | ", row) + " |");
}

report.AppendLine();
if (regressions.Count > 0)
{
    report.AppendLine($"**{regressions.Count} significant regression(s) over {threshold}%:** "
                    + string.Join(", ", regressions.Select(r => $"{r.Name} ({r.Delta:+0.0;-0.0}%)")));
}

if (improvements.Count > 0)
{
    report.AppendLine($"**{improvements.Count} significant improvement(s):** "
                    + string.Join(", ", improvements.Select(r => $"{r.Name} ({r.Delta:+0.0;-0.0}%)")));
}

if (regressions.Count == 0 && improvements.Count == 0)
{
    report.AppendLine("No statistically significant change.");
}

Console.WriteLine(report.ToString());

var summary = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
if (!string.IsNullOrEmpty(summary))
{
    File.AppendAllText(summary, report.ToString() + Environment.NewLine);
}

foreach (var (name, delta) in regressions)
{
    Console.WriteLine($"::warning::{label} {name} is {delta:+0.0;-0.0}% slower than baseline");
}

if (regressions.Count > 0 && failOnRegression)
{
    Console.WriteLine($"::error::{regressions.Count} benchmark(s) regressed beyond {threshold}%");
    return 1;
}

return 0;

static (Dictionary<string, Stat> Results, int FileCount) Load(string directory)
{
    var results = new Dictionary<string, Stat>(StringComparer.Ordinal);
    var files = Directory.GetFiles(directory, "*-report-full-compressed.json", SearchOption.AllDirectories);
    if (files.Length == 0)
    {
        // Older BenchmarkDotNet versions emit the uncompressed name instead.
        files = Directory.GetFiles(directory, "*-report-full.json", SearchOption.AllDirectories);
    }

    foreach (var path in files)
    {
        // ReadAllText strips the UTF-8 BOM that BenchmarkDotNet writes; JsonDocument would choke on it.
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("Benchmarks", out var benchmarks))
        {
            continue;
        }

        foreach (var bench in benchmarks.EnumerateArray())
        {
            if (!bench.TryGetProperty("Statistics", out var stats) || stats.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var mean = stats.GetProperty("Mean").GetDouble();
            var lower = mean;
            var upper = mean;
            if (stats.TryGetProperty("ConfidenceInterval", out var ci) && ci.ValueKind == JsonValueKind.Object)
            {
                lower = ci.GetProperty("Lower").GetDouble();
                upper = ci.GetProperty("Upper").GetDouble();
            }

            // Key on DisplayInfo, not FullName. FullName omits the job, so a class carrying two
            // [SimpleJob] attributes (several here pair Net90 with Net10_0) or a --job argument on the
            // command line produces multiple records sharing one FullName. Keying on FullName silently
            // keeps whichever was parsed last, and can pair a ShortRun baseline against a default-job
            // head -- a plausible-looking number that means nothing.
            var key = bench.GetProperty("DisplayInfo").GetString()!;
            results[key] = new Stat(mean, lower, upper);
        }
    }

    return (results, files.Length);
}

// "Class.Method: .NET 10.0(Runtime=.NET 10.0) [Size=32]" -> "Class.Method: .NET 10.0 [Size=32]"
static string ShortName(string display)
{
    var trimmed = System.Text.RegularExpressions.Regex.Replace(display, @"\([^)]*\)", "");
    return trimmed.Replace("Base58Encoding.Benchmarks.", "").Trim();
}

readonly record struct Stat(double Mean, double Lower, double Upper);
