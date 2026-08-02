using NodeSharp.Registry;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="NodeTypeRegistry.LoadPlugins"/>(RG-02a+02b)에 대한 테스트입니다. RG-02a(nodes/*.dll
/// 스캔, 로딩은 안 함)는 CT-06a의 <see cref="PluginLoader.DiscoverPluginFiles"/>가 이미 구현·테스트돼
/// 있음을(<see cref="PluginLoadingTests"/>) 설계 검토 중 확인했고, 사용자 확인을 거쳐 RG-02b(격리 로드
/// + Registry 등록)와 합쳐 <see cref="NodeTypeRegistry.LoadPlugins"/> 파이프라인 전체를 이 파일에서
/// 검증합니다. 실제 노드 플러그인 dll이 아직 없으므로(Phase 7 이후), <see cref="NodeTypeDescriptorTests"/>
/// 안의 <c>TestFunctionNodeType.Descriptor</c>가 이미 들어있는 이 테스트 어셈블리 자신의 dll을
/// "더미 플러그인 dll"로 사용합니다(<see cref="PluginLoadingTests"/>와 동일한 방식).
/// </summary>
public class NodeTypeRegistryLoadPluginsTests
{
    private static string ThisTestAssemblyDllPath => typeof(NodeTypeRegistryLoadPluginsTests).Assembly.Location;

    /// <summary>
    /// 임시 폴더를 최선을 다해 지웁니다(실패해도 테스트를 실패시키지 않음). 발견한 공백(사용자 실제
    /// 테스트 실행에서 확인, Windows): dll을 <c>PluginLoadContext</c>로 로드하면 <c>Collectible</c>
    /// 이라도 그 순간 파일이 메모리 매핑되어 잠깁니다 — 잠금은 컨텍스트가 실제로 Unload되고 GC까지
    /// 끝나야 풀리는데(비결정적 타이밍, <see cref="PluginLoadingTests"/>의 GC 반복 테스트와 동일한
    /// 이유), <see cref="NodeTypeRegistry.LoadPlugins"/>는 로드에 쓴 컨텍스트를 밖으로 내보내지 않아
    /// 테스트에서 명시적으로 Unload+GC를 강제할 수 없습니다. 임시 폴더 정리 실패는 OS 임시 디렉터리가
    /// 결국 정리해 주는 문제라 테스트 결과에 영향이 없으므로, 삭제 실패를 조용히 무시합니다.
    /// </summary>
    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 위 XML 주석 참고 — 로드된 dll의 Windows 파일 잠금으로 인한 정리 실패는 무시.
        }
    }

    [Fact]
    public void 완료_기준_직접_검증__RG_02a__존재하지_않는_디렉터리를_스캔하면_파일도_로딩도_0건이다()
    {
        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        var missingDir = Path.Combine(Path.GetTempPath(), "nodesharp-no-such-dir-" + Guid.NewGuid());

        var result = registry.LoadPlugins(missingDir);

        Assert.Equal(0, result.FilesFound);
        Assert.Equal(0, result.LoadedSuccessfully);
        Assert.Equal(0, result.DescriptorsRegistered);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void 완료_기준_직접_검증__RG_02b__실제_dll_1개를_두면_Registry가_인식한다()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "nodesharp-plugins-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        try
        {
            File.Copy(ThisTestAssemblyDllPath, Path.Combine(tempDir, "NodeSharp.Tests.dll"));
            var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");

            var result = registry.LoadPlugins(tempDir);

            Assert.Equal(1, result.FilesFound);          // RG-02a: 스캔으로 dll 1개 발견
            Assert.Equal(1, result.LoadedSuccessfully);  // RG-02b: 격리 로드 성공
            Assert.True(result.DescriptorsRegistered >= 1);
            Assert.Empty(result.Failures);
            Assert.True(registry.Descriptors.ContainsKey("test-function"));   // Registry가 실제로 인식
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void LoadPlugins는_손상된_dll_1개가_있어도_나머지_정상_dll_로딩을_막지_않는다()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "nodesharp-plugins-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        try
        {
            File.Copy(ThisTestAssemblyDllPath, Path.Combine(tempDir, "NodeSharp.Tests.dll"));
            File.WriteAllText(Path.Combine(tempDir, "corrupt.dll"), "이 파일은 유효한 어셈블리가 아닙니다.");
            var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");

            var result = registry.LoadPlugins(tempDir);

            Assert.Equal(2, result.FilesFound);
            Assert.Equal(1, result.LoadedSuccessfully);   // corrupt.dll만 실패
            Assert.Single(result.Failures);
            Assert.Contains("corrupt.dll", result.Failures[0]);
            Assert.True(registry.Descriptors.ContainsKey("test-function"));   // 정상 dll은 그대로 등록됨
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void LoadPlugins를_같은_디렉터리에_다시_호출해도_안전하게_유지된다()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "nodesharp-plugins-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        try
        {
            File.Copy(ThisTestAssemblyDllPath, Path.Combine(tempDir, "NodeSharp.Tests.dll"));
            var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");

            registry.LoadPlugins(tempDir);
            registry.LoadPlugins(tempDir);   // 재호출

            Assert.True(registry.Descriptors.ContainsKey("test-function"));   // 예외 없이 그대로 유지
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void LoadPlugins는_커스텀_PluginLoader_인스턴스를_주입받을_수_있다()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "nodesharp-plugins-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        try
        {
            File.Copy(ThisTestAssemblyDllPath, Path.Combine(tempDir, "NodeSharp.Tests.dll"));
            var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
            var customLoader = new PluginLoader();

            var result = registry.LoadPlugins(tempDir, customLoader);

            Assert.Equal(1, result.LoadedSuccessfully);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }
}
