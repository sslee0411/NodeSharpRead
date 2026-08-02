using System.Reflection;

namespace NodeSharp.Registry;

/// <summary>
/// Class명 : 플러그인 로더
/// 역활 및 기능 : nodes 디렉터리를 탐색해 플러그인 dll을 찾고 격리 로드하는 진입점
///
/// <c>nodes/*.dll</c> 디렉터리를 탐색해 플러그인 dll 목록을 찾고, 각 dll을 개별
/// <see cref="PluginLoadContext"/>로 격리 로드하는 진입점입니다. 실제로 로드된 어셈블리에서
/// <c>IFlowNode</c> 구현 타입을 찾아 등록하는 것은 <c>NodeTypeRegistry</c>(<c>CT-06b</c>)의 몫이며,
/// 이 클래스는 "파일을 찾아 로드"까지만 담당합니다.
/// 설계 근거: 02번 문서 1번 탭(Registry 폴더 구조), 3번 탭 시퀀스 다이어그램("PluginLoader로
/// nodes/*.dll 스캔").
/// </summary>
/// <example>
/// <code>
/// var loader = new PluginLoader();
///
/// // 1) 플러그인 dll 탐색
/// IReadOnlyList&lt;string&gt; files = loader.DiscoverPluginFiles("nodes");
///
/// // 2) 발견한 dll마다 격리 로드 — 실패한 dll 1개가 나머지 로드를 막지 않도록 개별 try/catch(NodeTypeRegistry에서 처리)
/// foreach (var file in files)
/// {
///     var (assembly, context) = loader.LoadPlugin(file);
///     // NodeTypeRegistry가 assembly.GetTypes()로 IFlowNode 구현 타입을 찾아 등록
/// }
/// </code>
/// </example>
public sealed class PluginLoader
{
    /// <summary>지정한 디렉터리(하위 폴더 제외)에서 <c>*.dll</c> 파일 목록을 찾습니다. 디렉터리가 없으면 빈 목록을 반환합니다.</summary>
    public IReadOnlyList<string> DiscoverPluginFiles(string pluginsDirectory)
    {
        if (!Directory.Exists(pluginsDirectory))
            return Array.Empty<string>();

        return Directory.GetFiles(pluginsDirectory, "*.dll", SearchOption.TopDirectoryOnly);
    }

    /// <summary>지정한 플러그인 dll을 새 <see cref="PluginLoadContext"/>로 격리 로드합니다. 언로드하려면 반환된 컨텍스트에 <see cref="System.Runtime.Loader.AssemblyLoadContext.Unload"/>를 호출합니다.</summary>
    public (Assembly Assembly, PluginLoadContext Context) LoadPlugin(string pluginDllPath)
    {
        var context = new PluginLoadContext(pluginDllPath);
        var assembly = context.LoadFromAssemblyPath(pluginDllPath);
        return (assembly, context);
    }
}
