using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Base58Encoding.Tests;

// JIT feature switches require a fresh process. Native AOT selects its vector width during publishing.
public class VectorInstructionSetTests
{
    private const string ChildMarker = "BASE58_VECTOR_CHILD";

    private readonly ITestOutputHelper _output;

    public VectorInstructionSetTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void NativeAot_UsesExpectedVectorWidth()
    {
        var expectedWidth = Environment.GetEnvironmentVariable("BASE58_EXPECT_VECTOR_WIDTH");
        Assert.SkipWhen(expectedWidth is null, "no Native AOT baseline selected");
        Assert.False(RuntimeFeature.IsDynamicCodeSupported);

        var actualWidth = Vector256.IsHardwareAccelerated ? "256" :
            Vector128.IsHardwareAccelerated ? "128" : "0";
        Assert.Equal(expectedWidth, actualWidth);
    }

    [Theory]
    [InlineData("DOTNET_EnableAVX2", "0")]
    [InlineData("DOTNET_EnableHWIntrinsic", "0")]
    public void AllTests_Pass_WithVectorInstructionSetDisabled(string environmentVariable, string value)
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable(ChildMarker) == "1", "already the child run");
        Assert.SkipUnless(
            RuntimeFeature.IsDynamicCodeSupported,
            "native AOT: instruction sets are baked in by ILC, so the env knob would do nothing");

#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
#if NET11_0_OR_GREATER
        const string targetFramework = "net11.0";
#else
        const string targetFramework = "net10.0";
#endif
        var testProjectDir = Path.Combine(FindSrcDir(), "Base58Encoding.Tests");

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = testProjectDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(configuration);
        startInfo.ArgumentList.Add("--framework");
        startInfo.ArgumentList.Add(targetFramework);
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--no-restore");
        startInfo.Environment[environmentVariable] = value;
        startInfo.Environment[ChildMarker] = "1";

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        _output.WriteLine($"child with {environmentVariable}={value}:");
        _output.WriteLine(stdout);
        if (stderr.Length > 0)
        {
            _output.WriteLine("stderr: " + stderr);
        }

        Assert.Equal(0, process.ExitCode);
    }

    private static string FindSrcDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.Name != "src")
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the 'src' directory.");
    }
}
