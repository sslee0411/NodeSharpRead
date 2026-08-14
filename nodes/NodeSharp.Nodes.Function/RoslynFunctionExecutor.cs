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
/// <see cref="AllowedReferences"/>·<see cref="AllowedImports"/> 2개 고정 목록만 허용합니다 — FN-04는
/// 실행 타임아웃(<see cref="ExecutionTimeoutSeconds"/>)만 구현했고, 위험 네임스페이스 경고는
/// <c>OP-04</c>(FlowLinter, ⏳ 대기)가 먼저 있어야 해 아직 없습니다 — 목록 밖 네임스페이스(예:
/// <c>System.IO</c>)도 지금은 컴파일 자체를 막지는 않고, 배포 전 경고 표시는 <c>OP-04</c> 몫으로 남습니다.</item>
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
/// <item><b>(FN-04) 실행 타임아웃 — watchdog 방식, 진짜 강제 종료 아님</b>: <see cref="ExecuteAsync"/>는
/// <see cref="ExecutionTimeoutSeconds"/>(기본 5초, <c>FunctionNode.TimeoutSeconds</c>에서 전달)를
/// 넘기면 <see cref="FunctionTimeoutException"/>을 던집니다. 02번 설계 문서 5번 탭 카드7의
/// <c>CancellationTokenSource.CreateLinkedTokenSource</c>+<c>CancelAfter</c> 스니펫을 그대로 따르되,
/// 그 스니펫만으로는 <c>while(true){}</c>처럼 <c>await</c>/토큰 검사 지점이 전혀 없는 사용자 코드를
/// 실제로 멈추지 못한다는 것(<see cref="FunctionTimeoutException"/> XML 문서에 근거 상세 기록 — .NET
/// Core 이후 <c>Thread.Abort</c>/<c>AppDomain</c> 언로드가 모두 제거돼 관리형 코드로 다른 스레드를
/// 강제 정지시킬 방법이 없음)을 착수 전 검토로 발견 — 그래서 사용자 코드를 <c>Task.Run</c>으로 별도
/// 스레드 풀 스레드에 맡기고, <c>Task.WhenAny</c>로 "그 작업이 먼저 끝나는지, 타임아웃이 먼저 오는지"만
/// 지켜보는 watchdog 방식을 채택 — <c>FunctionNode</c> 입장에서는 타임아웃 시간 안에 반드시 응답이
/// 돌아오지만(완료 기준 충족), 끝나지 않은 사용자 코드의 스레드 자체는 백그라운드에서 계속 실행될 수
/// 있다는 한계를 <see cref="FunctionTimeoutException"/> 문서에 명시(허위로 "완전한 강제 종료"라고
/// 과장하지 않음). 진짜 OS 프로세스 수준 강제 종료가 필요하면 Roslyn 실행을 별도 프로세스로 분리하는
/// 더 큰 아키텍처 변경이 필요해 이 Step 범위 밖으로 남김. FN-04의 "위험 네임스페이스 사용 경고" 부분은
/// <c>OP-04</c>(FlowLinter, ⏳ 대기)가 먼저 있어야 해 이 클래스가 아니라 <c>OP-04</c> 착수 시 별도로
/// 구현됩니다(03번 Step맵 FN-04 항목의 "★ 실행 순서 주의"가 이미 예견한 순서).</item>
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
    /// 자체는 막지 않습니다(위 클래스 remarks 참고) — <c>OP-04</c> FlowLinter가 배포 전 경고를 담당할 예정입니다.
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

    /// <summary>
    /// (FN-04) <see cref="ExecuteAsync"/>가 이 시간(초)을 넘기면 <see cref="FunctionTimeoutException"/>을
    /// 던집니다. 기본값 5초는 02번 설계 문서 5번 탭 카드7과 동일 — <c>FunctionNode.OnStartAsync</c>가
    /// <c>FunctionNode.TimeoutSeconds</c>(노드 속성 "timeoutSec")로 덮어씁니다. "진짜 강제 종료"가
    /// 아닌 watchdog 방식의 한계는 위 클래스 remarks의 FN-04 항목·<see cref="FunctionTimeoutException"/>
    /// XML 문서를 참고하십시오.
    /// </summary>
    public double ExecutionTimeoutSeconds { get; set; } = 5.0;

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
    /// 않음, 필터링 용도). <see cref="ExecutionTimeoutSeconds"/>를 넘기면 <see cref="FunctionTimeoutException"/>을
    /// 던집니다(watchdog 방식 — 실제 스레드가 종료된다는 보장은 없음, 위 클래스 remarks의 FN-04 항목·
    /// <see cref="FunctionTimeoutException"/> XML 문서 참고). 그 외 실행 중 예외는 잡지 않고 그대로 전파합니다.
    /// </summary>
    public async Task<Msg?> ExecuteAsync(Msg msg, CancellationToken ct)
    {
        var globals = new FunctionGlobals { msg = msg };

        // (FN-04) 사용자 코드를 별도 스레드 풀 스레드(Task.Run)에 맡기고, 그 작업이 끝나는 것과
        // 타임아웃 중 먼저 오는 쪽만 기다린다(Task.WhenAny) — await 지점이 있는 코드는 timeoutCts.Token이
        // 곧바로 취소를 전파해 정상적으로 즉시 중단되고, await 지점이 전혀 없는 코드(예: while(true){})는
        // 스레드 자체가 계속 실행되더라도 이 메서드는 타임아웃 시점에 FunctionTimeoutException으로
        // 즉시 반환한다 — "노드가 멈추지 않는다"는 완료 기준을 충족하되 "스레드가 실제로 죽는다"는
        // 것까지는 보장하지 않는다(근거: FunctionTimeoutException XML 문서).
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(ExecutionTimeoutSeconds));

        var scriptTask = Task.Run(() => _runner!(globals, timeoutCts.Token), CancellationToken.None);
        var watchdogTask = Task.Delay(Timeout.InfiniteTimeSpan, timeoutCts.Token);

        var finished = await Task.WhenAny(scriptTask, watchdogTask).ConfigureAwait(false);
        if (finished != scriptTask)
        {
            // timeoutCts가 취소됨 — 원인이 외부 ct(플로우 중지 등)인지, 이 메서드 자체의 타임아웃인지 구분
            ct.ThrowIfCancellationRequested();
            throw new FunctionTimeoutException($"코드 실행이 {ExecutionTimeoutSeconds}초를 초과해 타임아웃 처리되었습니다.");
        }

        var result = await scriptTask.ConfigureAwait(false); // 정상 완료 — 스크립트가 던진 예외는 여기서 그대로 전파됨
        return result as Msg; // 사용자가 null을 return하면 여기서도 null → 필터링
    }
}
