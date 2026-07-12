using Xunit;

namespace ClientAvalonia.Tests;

/// <summary>
/// Verifies the test host wires up (xunit discovers, ClientAvalonia references resolve,
/// InternalsVisibleTo works). No DX-aligned logic here — see CnCNet/, Core/, IniUi/ for that.
/// </summary>
public sealed class SmokeTest
{
    [Fact]
    public void TestRunner_Executes_And_FrameworkReferences_Resolve()
    {
        // Reaches an internal static type in ClientAvalonia — only resolves if
        // InternalsVisibleTo("ClientAvalonia.Tests") is configured.
        _ = ClientAvalonia.CnCNet.CnCNetIrcChannelNames.Normalize("#CnCNet");

        Assert.Equal(4, 2 + 2);
    }
}
