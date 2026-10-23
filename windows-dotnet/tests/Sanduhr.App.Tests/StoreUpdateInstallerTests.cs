using Sanduhr.App.Services;
using Sanduhr.Core;
using Windows.Services.Store;

namespace Sanduhr.App.Tests;

/// <summary>
/// The Store's answer to RequestDownloadAndInstallStorePackageUpdatesAsync, as Restart to update reads it.
/// The call itself needs a Store identity and a real update (3.8 to 3.8.x); the mapping does not.
/// </summary>
public class StoreUpdateInstallerTests
{
    [Theory]
    [InlineData(StorePackageUpdateState.Completed, UpdateInstallResult.Completed)]
    [InlineData(StorePackageUpdateState.Canceled, UpdateInstallResult.Canceled)]
    [InlineData(StorePackageUpdateState.Pending, UpdateInstallResult.Installing)]
    [InlineData(StorePackageUpdateState.Downloading, UpdateInstallResult.Installing)]
    [InlineData(StorePackageUpdateState.Deploying, UpdateInstallResult.Installing)]
    [InlineData(StorePackageUpdateState.ErrorLowBattery, UpdateInstallResult.Failed)]
    [InlineData(StorePackageUpdateState.ErrorWiFiRecommended, UpdateInstallResult.Failed)]
    [InlineData(StorePackageUpdateState.ErrorWiFiRequired, UpdateInstallResult.Failed)]
    [InlineData(StorePackageUpdateState.OtherError, UpdateInstallResult.Failed)]
    public void Every_store_state_has_an_answer(StorePackageUpdateState state, UpdateInstallResult expected)
        => Assert.Equal(expected, StoreUpdateInstaller.Map(state));

    [Fact]
    public void Both_channels_name_themselves_for_the_log()
    {
        Assert.Equal("store", StoreUpdateInstaller.ChannelName);
        Assert.Equal("velopack", Updates.VelopackUpdateInstaller.ChannelName);
    }
}
