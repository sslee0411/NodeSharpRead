using System.Runtime.CompilerServices;
using NodeSharp.Registry;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="PluginLoadContext"/>/<see cref="PluginLoader"/>(CT-06a, 02번 설계 문서 1번 탭 — v1.66에서
/// Contracts 소속 오기를 Registry로 정정)에 대한 단위 테스트입니다. 실제 노드 플러그인 dll이 아직
/// 없으므로(Phase 7 이후), 이 테스트 어셈블리 자신의 dll을 "더미 dll"로 사용해 로드/언로드 동작을
/// 검증합니다.
/// </summary>
public class PluginLoadingTests
{
    [Fact]
    public void PluginLoadContext는_Collectible로_생성되고_PluginDllPath를_보관한다()
    {
        var dllPath = typeof(PluginLoadingTests).Assembly.Location;

        var context = new PluginLoadContext(dllPath);

        Assert.True(context.IsCollectible);
        Assert.Equal(dllPath, context.PluginDllPath);
        context.Unload();
    }

    [Fact]
    public void PluginLoadContext_LoadFromAssemblyPath로_더미_dll을_로드하면_유효한_Assembly가_반환된다()
    {
        var dllPath = typeof(PluginLoadingTests).Assembly.Location;
        var context = new PluginLoadContext(dllPath);

        var assembly = context.LoadFromAssemblyPath(dllPath);

        Assert.NotNull(assembly);
        Assert.Equal("NodeSharp.Tests", assembly.GetName().Name);
        context.Unload();
    }

    [Fact]
    public void PluginLoadContext_Unload_후_참조가_모두_끊기면_결국_GC로_수거된다()
    {
        var weakRef = LoadIntoIsolatedContextAndUnload();

        for (int i = 0; i < 10 && weakRef.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(weakRef.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LoadIntoIsolatedContextAndUnload()
    {
        var dllPath = typeof(PluginLoadingTests).Assembly.Location;
        var context = new PluginLoadContext(dllPath);
        context.LoadFromAssemblyPath(dllPath);
        context.Unload();
        return new WeakReference(context, trackResurrection: true);
    }

    [Fact]
    public void PluginLoader_DiscoverPluginFiles는_존재하지_않는_디렉터리면_빈_목록을_반환한다()
    {
        var loader = new PluginLoader();

        var result = loader.DiscoverPluginFiles(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));

        Assert.Empty(result);
    }

    [Fact]
    public void PluginLoader_DiscoverPluginFiles는_디렉터리_안의_dll만_찾는다()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "nodesharp-plugin-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "NodeSharp.Nodes.Dummy1.dll"), "");
            File.WriteAllText(Path.Combine(tempDir, "NodeSharp.Nodes.Dummy2.dll"), "");
            File.WriteAllText(Path.Combine(tempDir, "readme.txt"), "");   // dll이 아닌 파일은 제외돼야 함

            var loader = new PluginLoader();
            var result = loader.DiscoverPluginFiles(tempDir);

            Assert.Equal(2, result.Count);
            Assert.All(result, f => Assert.EndsWith(".dll", f));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void PluginLoader_LoadPlugin은_Assembly와_Context를_함께_반환한다()
    {
        var dllPath = typeof(PluginLoadingTests).Assembly.Location;
        var loader = new PluginLoader();

        var (assembly, context) = loader.LoadPlugin(dllPath);

        Assert.NotNull(assembly);
        Assert.True(context.IsCollectible);
        context.Unload();
    }
}
