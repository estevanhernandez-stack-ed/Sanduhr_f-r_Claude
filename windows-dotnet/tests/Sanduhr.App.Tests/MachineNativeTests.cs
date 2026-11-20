using System.Runtime.InteropServices;
using Sanduhr.App.Interop;

namespace Sanduhr.App.Tests;

/// <summary>5.0 spec 7: the machine's real architecture, read through IsWow64Process2. In a native process it must
/// agree with the runtime's own answer: X64 on the x64 CI runner, Arm64 on the Arm runner.</summary>
public sealed class MachineNativeTests
{
    [Fact]
    public void The_machine_read_agrees_with_the_runtime_in_a_native_process()
    {
        if (RuntimeInformation.ProcessArchitecture != RuntimeInformation.OSArchitecture)
            return;   // an emulated test run: the two answers differ on purpose
        Assert.Equal(RuntimeInformation.OSArchitecture, MachineNative.Native());
    }

    [Fact]
    public void The_machine_read_answers_on_every_supported_Windows()
        => Assert.NotNull(MachineNative.Native());
}
