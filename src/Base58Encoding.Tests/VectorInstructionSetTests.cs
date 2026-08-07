using System.Diagnostics;

namespace Base58Encoding.Tests;

// The 32/64-byte fast paths (and CountLeadingZeros) select a code path from the CPU's available
// vector instruction sets:
//   AVX2 present            -> Vector256 branch
//   AVX2 off, SSE present   -> Vector128 branch
//   no hardware intrinsics  -> scalar fallback
//
// Whether Vector256/Vector128 are "hardware accelerated" is decided by the JIT when a method is
// compiled, from the instruction sets the runtime enabled at startup -- it cannot be toggled inside
// a running process. So to prove the Vector128 and scalar paths are correct we launch a CHILD
// process with an instruction set disabled via an environment variable and run the whole test suite
// there (Base58EncodeFast / Base58DecodeFast already cover the 32/64 fast paths thoroughly). Knobs
// (x86/x64):
//   DOTNET_EnableAVX2=0        disables AVX2 (and AVX-512), leaving SSE  -> forces the Vector128 path
//   DOTNET_EnableHWIntrinsic=0 disables all hardware intrinsics          -> forces the scalar path
//
// Runs by default: these are the only tests covering the Vector128 and scalar paths. ChildMarker is
// what stops the child spawning its own child.
public class VectorInstructionSetTests
{
    private const string ChildMarker = "BASE58_VECTOR_CHILD";

    private readonly ITestOutputHelper _output;

    public VectorInstructionSetTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData("DOTNET_EnableAVX2", "0")]        // disable AVX2 -> forces the Vector128 path
    [InlineData("DOTNET_EnableHWIntrinsic", "0")] // disable all hardware intrinsics -> forces the scalar path
    public void AllTests_Pass_WithVectorInstructionSetDisabled(string environmentVariable, string value)
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable(ChildMarker) == "1", "already the child run");

#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        string testProjectDir = Path.Combine(FindSrcDir(), "Base58Encoding.Tests");

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
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--no-restore");
        startInfo.Environment[environmentVariable] = value;
        startInfo.Environment[ChildMarker] = "1";

        using var process = Process.Start(startInfo)!;
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
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
