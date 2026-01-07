using System.Diagnostics;

namespace Base58Encoding.Tests;

public class BigEndianTests
{
    private readonly ITestOutputHelper _output;

    public BigEndianTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Alternatively can be run manually to verify compatibility on big-endian systems using below command
    /// docker run -it --rm --platform linux/s390x -v ProjectPath/src:/src registry.access.redhat.com/dotnet/sdk:10.0 sh -c "cd /src/Base58Encoding.Tests && dotnet run"
    /// </summary>
    [Fact(Skip = "For local usage only")]
    public void Base58Encoding_Works_On_BigEndian_ViaProcess()
    {
        if (!BitConverter.IsLittleEndian)
        {
            _output.WriteLine("Skipping big-endian verification as already running on big-endian environment.");
            return;
        }

        var path = FindSrcPath();

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"run --rm --platform linux/s390x -v {path}:/src registry.access.redhat.com/dotnet/sdk:10.0 sh -c \"cd /src/Base58Encoding.Tests && dotnet run --no-restore --no-build\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        Assert.Equal(0, process.ExitCode);
        Assert.Contains("Passed!", output);

        _output.Write(output);
    }

    private static string FindSrcPath()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && dir.Name != "src" && !dir.GetFiles("*.slnx").Any())
        {
            dir = dir.Parent;
        }

        if (dir == null) throw new Exception("Src path not found.");
        return Path.GetFullPath(dir.FullName);
    }
}
