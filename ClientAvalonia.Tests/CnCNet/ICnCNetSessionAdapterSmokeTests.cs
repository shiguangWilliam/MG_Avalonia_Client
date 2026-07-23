using ClientAvalonia.CnCNet;
using ClientAvalonia.CnCNet.Tunnels;
using ClientAvalonia.Services;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

public sealed class ICnCNetSessionAdapterSmokeTests
{
    [Fact]
    public void Adapter_Exposes_TunnelSorter_And_ActiveGameRoom()
    {
        var adapter = new CnCNetSessionServiceAdapter(CnCNetSessionService.Instance);

        adapter.TunnelSorter.Should().BeOfType<TunnelSorter>();
        adapter.ActiveGameRoom.Should().BeNull();
        adapter.GameRoom.Should().BeNull();
        adapter.Tunnels.Should().NotBeNull();
    }
}
