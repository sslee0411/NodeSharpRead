using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;

namespace NodeSharp.Nodes.Function;

/// <summary>
/// Class명 : Function 노드
/// 역활 및 기능 : IFunctionExecutor(NCalc 표현식 또는 향후 Roslyn C# 코드)로 msg를 계산·변환하는 범용 처리 노드
///
/// 사용자가 캔버스에서 입력한 표현식/코드로 <see cref="Msg"/>를 계산·변환하는 범용 처리 노드입니다.
/// <see cref="Mode"/>(<see cref="FunctionMode"/>)에 따라 <see cref="IFunctionExecutor"/> 구현체를
/// 선택해 위임합니다 — "표현식이냐 코드냐" 분기를 이 클래스가 직접 하지 않고 전략 패턴으로
/// 뽑아낸 이유는 <see cref="IFunctionExecutor"/> XML 문서를 참고하십시오.
/// 설계 근거: 02번 문서 5번 탭 카드5·카드8, 03번 개발 Step맵 Phase 7 FN-01·FN-02·FN-03.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>카드5 원본 코드와의 차이</b>: 카드5 스니펫은 <c>NodeContext ctx</c>(구체 클래스)와
/// <c>ctx.Engine.RouteAsync(...)</c>를 사용하지만, 실제로 확정된 계약은 <see cref="INodeContext"/>
/// (인터페이스)와 <see cref="INodeContext.RouteAsync"/>(엔진을 거치지 않고 컨텍스트가 직접 노출)입니다
/// — Inject/Switch 노드와 동일한 선례로 재조정했습니다.</item>
/// <item><b>(FN-01) 예외 격리 — 왜 이 클래스가 직접 잡는가</b>: <see cref="IFunctionExecutor.ExecuteAsync"/>가
/// 던지는 예외(NCalc 문법 오류 등)를 잡지 않으면 <c>FlowEngine.RouteAsync</c>(RT-04a)까지 그대로
/// 전파됩니다 — 그 메서드는 대상 노드별 예외 격리가 없어(원본 <c>RouteAsync</c>에 try/catch 없음)
/// Runner 프로세스가 그대로 죽습니다. 착수 전 재검토로 이 공백을 발견해 두 방안을 검토했습니다:
/// (A) <c>FlowEngine</c> 자체에 try/catch + 실제 <c>NodeErrorEvent</c>(CT-05a, 모델만 있고 발행 코드는
/// 어디에도 없음을 grep으로 확인) 발행 — Runtime 핵심 엔진에 영향을 주는 더 큰 판단이라 별도 Step
/// 대상(EC-01c 팔레트 공백과 같은 성격의 미배정 공백, <c>NR-01a</c> Catch 노드는 "소비"만 전담하고
/// "발행"은 어느 Step도 맡고 있지 않음). (B) 이 클래스의 <see cref="OnInputAsync"/> 안에서만 잡아
/// 이미 완성된 <c>INodeContext.SetStatus</c>(RT-07/RT-09b, <c>NodeStatusEvent</c> 발행)로 즉시
/// 표면화 — <c>FlowEngine.cs</c>를 전혀 건드리지 않아 RT-04a 경계를 유지합니다. 개발 지침 #8에 따라
/// 착수 전 Node-RED 원본(10-function.js)을 WebSearch로 확인한 결과, 원본도 사용자 코드 실행을
/// 자체 try/catch로 감싸 동기 예외를 잡은 뒤 <c>node.error(err, msg)</c>를 호출합니다(Catch 노드가
/// 있으면 라우팅, 없으면 런타임 로그로만 남고 크래시하지 않음 — Node-RED가 실제로 크래시하는 경우는
/// "등록하지 않은 비동기 작업의 uncaught exception"뿐). 즉 Function 노드 자신이 자기 실행 오류를
/// 격리하는 것이 원본 설계와 일치해 B안을 채택했습니다(인터페이스 변경 없음·<c>FlowEngine</c> 비수정·
/// 기존 완성 메커니즘 재사용이라는 낮은 리스크로 판단, AskUserQuestion 생략). 완전한 <c>NodeErrorEvent</c>
/// 발행 + Catch 노드 라우팅(A안 성격)은 향후 <c>NR-01a</c> 착수 시 별도로 재검토될 사안으로 남습니다.</item>
/// <item><b>(FN-02) <see cref="FunctionMode.CSharp"/> 처리</b>: <see cref="OnStartAsync"/>가
/// <c>RoslynFunctionExecutor</c>를 만들어 <see cref="ActiveCode"/>를 컴파일합니다. 문법 오류가 있으면
/// <c>Microsoft.CodeAnalysis.Scripting.CompilationErrorException</c>이 여기서 그대로 던져지며, 이는
/// <c>FlowEngine.DeployAsync</c>(RT-02b)의 기존 노드별 예외 격리가 잡아 <c>FailedNodeIds</c>에
/// 기록합니다 — FN-01 시점의 <c>NotSupportedException</c>과 Inject 노드의 잘못된 cron 표현식(NR-03d)이
/// 쓰던 것과 동일한 기존 메커니즘 재사용이라 새 인프라가 필요 없습니다.</item>
/// <item><b>(FN-03) 단일 <c>Code</c>에서 <see cref="ExpressionCode"/>/<see cref="CSharpCode"/> 2개로 분리</b>:
/// 02번 설계 문서 5번 탭 카드8은 처음부터 모드별로 <c>expressionCode</c>/<c>csharpCode</c>를 별도
/// 필드에 저장해, 사용자가 <see cref="Mode"/>를 오가도 각 모드에서 입력했던 내용을 서로 잃지 않게
/// 설계했습니다 — 그런데 FN-01/FN-02 구현 당시엔 이 카드를 착수 전 재확인하지 않고 단일 <c>Code</c>
/// 문자열 하나로 만들어, 모드를 바꾸면 같은 텍스트가 그대로 남아(잃지는 않지만 다른 모드끼리 서로
/// 섞이는) 설계와 다른 상태였습니다. FN-03 착수 전 이 모순을 발견해 사용자에게 "단일 필드 유지(낮은
/// 리스크)" vs "설계 문서대로 분리(카드8 원본 의도 실현)" 중 확인받아, <b>분리</b>를 선택받아 이
/// 클래스의 <c>Code</c> 속성을 이 두 속성으로 교체했습니다. <see cref="OnStartAsync"/>는 <see cref="Mode"/>에
/// 맞는 쪽만(<see cref="ActiveCode"/>) 실행기에 넘기므로 실행 동작 자체는 이전과 동일합니다 — 바뀐
/// 것은 "Editor에서 두 모드의 입력값이 서로 독립적으로 보존되는지" 뿐입니다. 이미 저장된 flows.json이
/// 옛 단일 <c>code</c> 키를 갖고 있어도 깨지지 않도록 <c>FunctionNodeType.Factory</c>가 읽기 시점에
/// 호환 처리합니다(그 파일의 "발견한 공백" 항목에 근거 기록).</item>
/// <item><b>(FN-04) 실행 타임아웃 배선</b>: <see cref="OnStartAsync"/>가 <see cref="FunctionMode.CSharp"/>용
/// 기본 실행기를 만들 때 <see cref="TimeoutSeconds"/>(노드 속성 "timeoutSec", 기본 5초)를
/// <c>RoslynFunctionExecutor.ExecutionTimeoutSeconds</c>에 그대로 전달합니다. NCalc 모드는 반복문 자체가
/// 없어(카드7 설계 근거) 타임아웃을 적용하지 않습니다. 타임아웃 초과 시 <c>RoslynFunctionExecutor</c>가
/// 던지는 <c>FunctionTimeoutException</c>은 일반 <see cref="Exception"/>이라 <see cref="OnInputAsync"/>의
/// 기존 catch(FN-01 항목)가 그대로 잡아 <c>ctx.SetStatus</c>로 표면화합니다 — FN-04 완료 기준이 요구하는
/// "NodeErrorEvent로 알림"도 FN-01 때와 동일한 이유(실제 <c>NodeErrorEvent</c> 발행 인프라가 아직 없음,
/// <c>NR-01a</c> 몫)로 <c>SetStatus</c>(<c>NodeStatusEvent</c> 발행)로 대신합니다 — 새 판단이 아니라
/// FN-01 결정을 그대로 재사용.</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// var node = new FunctionNode { Id = "n1", Mode = FunctionMode.Expression, ExpressionCode = "(pressure1 - pressure2) * 0.0689" };
/// await node.OnStartAsync(ctx, ct);              // NCalcFunctionExecutor.Prepare(ExpressionCode) 호출
/// await node.OnInputAsync(msg, ctx, ct);          // msg.payload에 계산 결과 저장 후 0번 포트로 전달
/// // ExpressionCode에 "(" 같은 문법 오류가 있으면 예외가 여기서 잡혀 ctx.SetStatus(Red, ...)로만
/// // 표면화되고 Runner는 계속 동작합니다.
/// </code>
/// </example>
public sealed class FunctionNode : IFlowNode
{
    /// <inheritdoc />
    public string Id { get; init; } = string.Empty;

    /// <inheritdoc />
    public string Type => "function";

    /// <inheritdoc />
    public string Name { get; set; } = string.Empty;

    /// <summary>Function은 msg 1개를 받아 계산하는 노드라 입력 포트 1개입니다.</summary>
    public IReadOnlyList<NodePort> InputPorts { get; } = new[] { new NodePort(0, "in") };

    /// <summary>계산 결과 msg가 나가는 출력 포트 1개입니다(Node-RED Function 노드의 다중 출력 포트 설정 기능은 이 Step 범위 밖).</summary>
    public IReadOnlyList<NodePort> OutputPorts { get; } = new[] { new NodePort(0, "out") };

    /// <summary>실행 모드 — 기본값은 코드를 몰라도 되는 <see cref="FunctionMode.Expression"/>입니다.</summary>
    public FunctionMode Mode { get; init; } = FunctionMode.Expression;

    /// <summary>
    /// (FN-03) <see cref="Mode"/>가 <see cref="FunctionMode.Expression"/>일 때 쓰는 NCalc 표현식
    /// 문자열입니다. <see cref="CSharpCode"/>와 별도로 저장되므로, Editor에서 모드를 CSharp로 바꿔도
    /// 이 값은 지워지지 않고 그대로 남아 있다가 다시 Expression으로 돌아오면 그대로 쓸 수 있습니다
    /// (위 클래스 remarks의 FN-03 항목 참고 — 02번 문서 5번 탭 카드8 원본 설계 의도).
    /// </summary>
    public string ExpressionCode { get; init; } = string.Empty;

    /// <summary>
    /// (FN-03) <see cref="Mode"/>가 <see cref="FunctionMode.CSharp"/>일 때 쓰는 완전한 C# 코드
    /// 문자열입니다. <see cref="ExpressionCode"/>와 별도로 저장됩니다(위 클래스 remarks의 FN-03 항목
    /// 참고).
    /// </summary>
    public string CSharpCode { get; init; } = string.Empty;

    /// <summary>
    /// (FN-03) <see cref="Mode"/>에 맞는 실제 실행 대상 코드 — <see cref="OnStartAsync"/>가 이 값만
    /// 실행기에 넘깁니다. <see cref="ExpressionCode"/>/<see cref="CSharpCode"/> 중 현재 쓰이지 않는
    /// 쪽은 실행에 전혀 관여하지 않고 Editor 편집 편의를 위해서만 보존됩니다.
    /// </summary>
    private string ActiveCode => Mode == FunctionMode.CSharp ? CSharpCode : ExpressionCode;

    /// <summary>
    /// (FN-04) <see cref="FunctionMode.CSharp"/> 모드에서 <c>RoslynFunctionExecutor</c>에 전달할 실행
    /// 타임아웃(초)입니다. 기본값 5초는 02번 설계 문서 5번 탭 카드7과 동일 — <see cref="FunctionMode.Expression"/>
    /// 모드에서는 쓰이지 않습니다(위 클래스 remarks의 FN-04 항목 참고).
    /// </summary>
    public double TimeoutSeconds { get; init; } = 5.0;

    /// <summary>
    /// 테스트에서 가짜 실행기를 주입할 수 있도록 공개 세터를 둡니다. 지정하지 않으면
    /// <see cref="OnStartAsync"/>가 <see cref="Mode"/>에 따라 기본 실행기를 만듭니다.
    /// </summary>
    public IFunctionExecutor? Executor { get; set; }

    /// <summary>
    /// <see cref="Mode"/>에 따라 <see cref="Executor"/>가 비어 있으면 기본 구현체를 만들고(테스트가
    /// 미리 주입했다면 그대로 유지), <see cref="IFunctionExecutor.Prepare"/>를 1회 호출해
    /// <see cref="ActiveCode"/>(<see cref="Mode"/>에 맞는 쪽)를 준비시킵니다. <see cref="FunctionMode.CSharp"/>은
    /// <c>RoslynFunctionExecutor</c>가 이 시점에 컴파일합니다 — 문법 오류가 있으면 위 클래스 remarks의
    /// FN-02 항목대로 여기서 예외가 던져집니다. (FN-04) 이때 <see cref="TimeoutSeconds"/>를
    /// <c>RoslynFunctionExecutor.ExecutionTimeoutSeconds</c>에도 함께 전달합니다.
    /// </summary>
    public Task OnStartAsync(INodeContext ctx, CancellationToken ct)
    {
        Executor ??= Mode switch
        {
            FunctionMode.Expression => new NCalcFunctionExecutor(),
            FunctionMode.CSharp => new RoslynFunctionExecutor { ExecutionTimeoutSeconds = TimeoutSeconds },
            _ => throw new NotSupportedException($"알 수 없는 FunctionMode: {Mode}"),
        };
        Executor.Prepare(ActiveCode);
        return Task.CompletedTask;
    }

    /// <summary>
    /// <see cref="Executor"/>로 <paramref name="msg"/>를 계산하고, 결과가 <c>null</c>이 아니면 0번
    /// 출력 포트로 전달합니다. 계산 중 예외(NCalc 문법 오류 등)가 발생하면 위 클래스 remarks의 FN-01
    /// 항목대로 이 메서드가 직접 잡아 <see cref="INodeContext.SetStatus"/>로만 표면화하고 다시 던지지
    /// 않습니다 — <c>FlowEngine</c>까지 예외가 전파돼 Runner가 죽는 것을 막습니다.
    /// </summary>
    public async Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct)
    {
        try
        {
            var result = await Executor!.ExecuteAsync(msg, ct);
            if (result is not null)
            {
                await ctx.RouteAsync(Id, outputPort: 0, result, ct);
            }
        }
        catch (Exception ex)
        {
            ctx.SetStatus(NodeStatusLevel.Red, "dot", ex.Message);
        }
    }

    /// <summary>정리할 구독·연결이 없어 아무 일도 하지 않습니다.</summary>
    public Task OnCloseAsync(INodeContext ctx) => Task.CompletedTask;
}
