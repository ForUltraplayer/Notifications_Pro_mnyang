// @file TestAppDataIsolationTests.cs
// @author: rudals252
// @brief 테스트 AppData 격리가 실제 %AppData% 대신 임시 폴더를 가리키는지 검증한다.

using System.IO;
using NotificationsPro.Helpers;

namespace NotificationsPro.Tests;

public class TestAppDataIsolationTests
{
    [Fact]
    public void AppDataRoot_IsRedirectedUnderTempDuringTests()
    {
        var tempRoot = Path.GetFullPath(Path.GetTempPath());
        var realAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        Assert.StartsWith(tempRoot, ForkIdentity.AppDataRoot, StringComparison.OrdinalIgnoreCase);
        Assert.False(ForkIdentity.AppDataRoot.StartsWith(realAppData, StringComparison.OrdinalIgnoreCase));
        Assert.StartsWith(tempRoot, ManagedAssetPathHelper.GetRoot(ManagedAssetPathHelper.IconsFolderName), StringComparison.OrdinalIgnoreCase);
    }
}
