using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;

namespace NodeSharp.Nodes.Function;

/// <summary>
/// Class명 : Roslyn C# 코드 실행기
/// 역활 및 기능 : IFunctionExecutor의 Roslyn(CSharpScript) 구현체 — 반복문·조건문 등 제약 없는 완전한 C# 문법으로 msg를 계산·변환
///
/// Roslyn(<c>Microsoft.CodeAnalysis.CSharp.Scripting</c>)으로 사용자가 입력한 C# 코드를 컴파일·실행하는
/// <see cref="IFunctionExecutor"/> 구현체입니다. NCalc 한 줄 수식(<see cref="NCalcFunctionExecutor"/>)과
/// 달리 반복문·조건문·지역 변수 선언 등 제약 없는 완전한 C# 문법을 쓸 수 있어, 복잡한 로직이 필요한
/// 고급 사용자 대상입니다.
/// 설계 근거: 02번 문서 5번 탭 카드7, 03번 개발 Step맵 Phase 7 FN-02.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>컴파일 캐시</b>: Roslyn은 최초 컴파일에 수십~수백ms가 걸려, 사용자 코드 문자열을 키로
/// 컴파일 결과(<see cref="ScriptRunner{T}"/>)를 <see cref="ConcurrentDictionary{TKey,TValue}"/>에
/// 캐시합니다 — 같은 코드를 여러 노드가 쓰거나 재배포해도 최초 1회만 컴파일됩니다(카드7 설계 그대로).</item>
/// <item><b>참조 화이트리스트(02번 문서 v1.31 설계 반영)</b>: 사용자가 임의 어셈블리를 추가하지 못하도록
/// <see cref="AllowedReferences"/>·<see cref="AllowedImports"/> 2개 고정 목록만 허용합니다 — FN-04(위험
/// 네임스페이스 경고, ⏳ 대기)가 아직 없어 목록 밖 네임스페이스(예: <c>System.IO</c>)도 지금은 컴파일
/// 자체를 막지는 않고, 배포 전 경고 표시만 FN-04 몫으로 남습니다.</item>
/// <item><b>발견한 공백 — NodeSharp.Util.Extensions 임포트는 아직 보류</b>: 02번 문서 v1.31 스니펫은
/// <c>NodeSharp.Util.Extensions</c>(<c>ScaleExtensions</c> 등)를 고정 임포트에 포함하지만,
/// 이 시점엔 <c>LL-08a</c>/<c>LL-11a</c>(포팅 대상 Step)가 모두 <c>⏳ 대기</c>라 그 네임스페이스에
/// 타입이 하나도 없습니다 — 이 상태로 임포트하면 모든 Function 노드 C# 코드 컴파일이
/// <c>CS0246</c>(네임스페이스 없음)으로 깨집니다. <see cref="AllowedReferences"/>에 <c>NodeSharp.Util</c>
/// 어셈블리 참조는 지금 추가하되(화이트리스트 확장 자체는 가능), <c>NodeSharp.Util.Extensions</c>
/// 임포트는 <c>LL-08a</c>/<c>LL-11a</c> 완료 후 추가합니다 — 03번 Step맵 FN-02 항목의 "★ 실행 순서
/// 주의"가 이미 예견한 상황이라 저위험 판단으로 처리(개발 지침 5번).</item>
/// <item><b>예외는 그대로 던진다</b>: 컴파일 오류(<see cref="CompilationErrorException"/>)·런타임 예외
/// 모두 잡지 않고 그대로 전파합니다 — <see cref="NCalcFunctionExecutor"/>와 동일하게, 실행 중 예외를
/// 잡는 책임은 호출자 <c>FunctionNode.OnInputAsync</c>에 있습니다. 다만 컴파일 오류는
/// <see cref="Prepare"/>(즉 <c>FunctionNode.OnStartAsync</c>) 시점에 발생하므로, 실제로는
/// <c>FlowEngine.DeployAsync</c>(RT-02b)의 기존 노드별 예외 격리가 잡아 <c>FailedNodeIds</c>에
/// 기록합니다(Inject의 잘못된 cron 표현식·FN-01의 <c>NotSupportedException</c>과 동일한 기존 경로 재사용,
/// 새 인프라 불필요).</item>
/// <item><b>★ 버그 수정(2026-08-12) — Microsoft.CSharp 참조 누락</b>: 사용자가 실제 xUnit 테스트 실행에서
/// <c>CS0656</c>(<c>Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfo.Create' 멤버가 필요한 컴파일러가
/// 없습니다</c>)를 보고 — <see cref="FunctionGlobals.msg"/>가 <c>dynamic</c>이라 사용자 코드의
/// <c>msg.payload = ...</c> 같은 동적 멤버 접근을 컴파일하려면 Roslyn이 런타임 바인더 호출 코드
/// (<c>Microsoft.CSharp.RuntimeBinder.Binder</c>류)를 생성해야 하는데, <see cref="AllowedReferences"/>에
/// <c>Microsoft.CSharp</c> 어셈블리가 빠져 있어 그 생성 코드 자체가 컴파일되지 못한 것이 원인입니다.
/// <see cref="AllowedReferences"/>에 <c>typeof(Microsoft.CSharp.RuntimeBinder.Binder).Assembly</c>를
/// 추가해 해소 — 이 프로젝트가 이미 <see cref="FunctionGlobals"/>에서 <c>dynamic</c>을 쓰고 있어
/// 별도 NuGet 패키지 추가 없이 런타임에 이미 로드되어 있는 어셈블리입니다.</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // 캔버스 "C# 코드" 입력란 예시(반복문·조건문 등 완전한 C# 문법 사용 가능):
/// //   msg.payload = (double)msg.payload * 2;
/// //   if ((double)msg.payload > 100) msg.topic = "고온 경고";
/// //   return msg;                      // Function 노드는 반드시 msg를 반환해야 다음 노드로 전달됨
/// //   // return null; 을 쓰면 이 msg는 버려지고 다음 노드로 전달되지 않음(필터링 용도)
/// var executor = new RoslynFunctionExecutor();
/// executor.Prepare("msg.payload = (double)msg.payload * 2; return msg;");
/// var msg = new Msg { Payload = 21.0 };
/// var result = await executor.ExecuteAsync(msg, CancellationToken.None);
/// // result.Payload == 42.0
/// </code>
/// </example>
public sealed class RoslynFunctionExecutor : IFunctionExecutor
{
    /// <summary>
    /// 사용자가 임의로 추가할 수 없는 고정 참조 어셈블리 목록입니다 — <c>NodeSharp.Contracts</c>
    /// (<see cref="Msg"/>가 속한 어셈블리)와 <c>NodeSharp.Util</c>(<c>LL-00</c>~<c>LL-11</c> 포팅
    /// 클래스가 앞으로 위치할 어셈블리, 위 클래스 remarks의 "발견한 공백" 항목 참고), 그리고
    /// <c>Microsoft.CSharp</c>(<c>FunctionGlobals.msg</c>가 <c>dynamic</c>이라 사용자 코드의 동적
    /// 멤버 접근을 컴파일하는 데 필요 — 위 클래스 remarks의 "★ 버그 수정" 항목 참고)만 허용합니다.
    /// </summary>
    private static readonly Assembly[] AllowedReferences =
    {
        typeof(Msg).Assembly,                                    // NodeSharp.Contracts
        typeof(NodeSharp.Util.SemVer).Assembly,                  // NodeSharp.Util — LL-00~LL-11 포팅 클래스가 이후 이 어셈블리에 추가됨
        typeof(Microsoft.CSharp.RuntimeBinder.Binder).Assembly,  // Microsoft.CSharp — dynamic(FunctionGlobals.msg) 바인딩 컴파일에 필요(CS0656 수정)
    };

    /// <summary>
    /// 고정 임포트 네임스페이스 목록입니다. 목록 밖 네임스페이스(예: <c>System.IO</c>)도 지금은 컴파일
    /// 자체는 막지 않습니다(위 클래스 remarks 참고) — FN-04 Linter가 배포 전 경고를 담당할 예정입니다.
    /// </summary>
    private static readonly string[] AllowedImports =
    {
        "System",
        "System.Dynamic",
        "System.Linq",
        "System.Collections.Generic",
        "NodeSharp.Util",
        // "NodeSharp.Util.Extensions" — LL-08a/LL-11a(ScaleExtensions 등 포팅) 완료 후 추가 예정(위 클래스 remarks 참고)
    };

    /// <summary>코드 문자열별로 컴파일 결과를 캐시합니다 — 같은 코드를 여러 노드가 쓰거나 재배포해도 재컴파일하지 않습니다(위 클래스 remarks 참고).</summary>
    private static readonly ConcurrentDictionary<string, ScriptRunner<object>> CompileCache = new();

    private ScriptRunner<object>? _runner;

    /// <summary>
    /// <paramref name="userCode"/>를 컴파일합니다. 이미 같은 코드 문자열이 캐시에 있으면 재컴파일 없이
    /// 그대로 재사용합니다. 문법 오류가 있으면 <see cref="CompilationErrorException"/>이 여기서
    /// 즉시 던져집니다(잡는 책임은 위 클래스 remarks 참고).
    /// </summary>
    public void Prepare(string userCode)
    {
        _runner = CompileCache.GetOrAdd(userCode, code =>
        {
            var script = CSharpScript.Create<object>(
                code,
                ScriptOptions.Default.WithReferences(AllowedReferences).WithImports(AllowedImports),
                typeof(FunctionGlobals));
            return script.CreateDelegate(); // 컴파일은 여기서 1회만 수행(캐시 미스 시), 문법 오류는 즉시 예외로 표면화
        });
    }

    /// <summary>
    /// <paramref name="msg"/>를 <see cref="FunctionGlobals.msg"/>에 담아 컴파일된 스크립트를 실행합니다.
    /// 사용자 코드가 <c>return null;</c>이면 이 메서드도 <c>null</c>을 반환합니다(다음 노드로 전달되지
    /// 않음, 필터링 용도). 실행 중 예외는 잡지 않고 그대로 전파합니다.
    /// </summary>
    public async Task<Msg?> ExecuteAsync(Msg msg, CancellationToken ct)
    {
        var globals = new FunctionGlobals { msg = msg };
        var result = await _runner!(globals, ct);
        return result as Msg; // 사용자가 null을 return하면 여기서도 null → 필터링
    }
}
