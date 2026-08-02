using System.Reflection;
using System.Runtime.Loader;

namespace NodeSharp.Registry;

// 한글명: 플러그인 로드 컨텍스트
/// <summary>
/// 노드 플러그인(<c>nodes/*.dll</c>) 하나를 격리된 <see cref="AssemblyLoadContext"/>로 로드합니다.
/// <c>isCollectible: true</c>로 생성되어, 플러그인을 제거하거나 갱신할 때 프로세스를 재시작하지 않고
/// <see cref="AssemblyLoadContext.Unload"/>로 메모리에서 내릴 수 있습니다(Node-RED의 런타임 노드
/// 설치/제거에 대응).
/// 설계 근거: 02번 문서 1번 탭(v1.66 정정 — 원래 Contracts 소속으로 잘못 표기돼 있었음, 아래 remarks 참고).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>왜 Contracts가 아니라 Registry인가</b>: <c>AssemblyLoadContext</c> 기반 동적 로딩·리플렉션은
/// Contracts의 "외부참조 0개" 원칙과 맞지 않는다. <c>NodeSharp.Registry</c>가 정확히 "노드 타입
/// 스캔/로딩/버전관리"를 목적으로 이미 존재하므로(1번 탭 솔루션 구조), 이 클래스는 처음부터 여기
/// 소속이어야 했다 — <c>CT-03a</c>(NodeContext)에서 발견한 것과 같은 유형의 경로 오기.</item>
/// <item><b>Contracts 타입을 "같은 타입"으로 공유</b>: <see cref="Load"/>를 재정의해, <c>NodeSharp.Contracts</c>
/// 어셈블리만큼은 이 격리된 컨텍스트에서 따로 로드하지 않고 <c>null</c>을 반환해 기본 컨텍스트에
/// 맡깁니다. 이렇게 하지 않으면, 플러그인이 구현한 <c>IFlowNode</c>와 호스트(Registry/Runtime)가 아는
/// <c>IFlowNode</c>가 겉보기엔 같은 타입이어도 서로 다른 로드 컨텍스트에서 온 것이라 C# 런타임이
/// 이를 다른 타입으로 취급해 캐스팅이 실패합니다(.NET 플러그인 방식에서 자주 걸리는 함정).</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // 1) 플러그인 로드
/// var context = new PluginLoadContext("nodes/NodeSharp.Nodes.Inject/NodeSharp.Nodes.Inject.dll");
/// Assembly plugin = context.LoadFromAssemblyPath(context.PluginDllPath);
///
/// // 2) 리플렉션으로 IFlowNode 구현 타입 탐색(NodeTypeRegistry, CT-06b의 몫)
/// var nodeTypes = plugin.GetTypes().Where(t => typeof(IFlowNode).IsAssignableFrom(t) &amp;&amp; !t.IsAbstract);
///
/// // 3) 플러그인 제거/갱신 시 메모리에서 내림 — 참조가 모두 끊긴 뒤에야 실제로 수거됨(Collectible 특성)
/// context.Unload();
/// </code>
/// </example>
public sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    /// <summary>이 컨텍스트가 로드하는 플러그인 진입 dll의 전체 경로.</summary>
    public string PluginDllPath { get; }

    /// <summary>지정한 플러그인 dll을 로드할 격리 컨텍스트를 생성합니다. 컨텍스트 자체는 아직 어셈블리를 로드하지 않습니다.</summary>
    public PluginLoadContext(string pluginDllPath) : base(isCollectible: true)
    {
        PluginDllPath = pluginDllPath;
        _resolver = new AssemblyDependencyResolver(pluginDllPath);
    }

    /// <summary>
    /// 플러그인이 참조하는 의존 어셈블리를 해석합니다. <c>NodeSharp.Contracts</c>는 항상 <c>null</c>을
    /// 반환해 기본 컨텍스트에서 공유하고, 그 외(플러그인 전용 NuGet 의존성 등)는 플러그인 dll 폴더
    /// 기준으로 해석해 이 컨텍스트 안에서 격리 로드합니다.
    /// </summary>
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name == "NodeSharp.Contracts")
            return null;   // 기본 컨텍스트에 위임 — 타입 identity 공유

        string? path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }
}
