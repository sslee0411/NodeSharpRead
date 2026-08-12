namespace NodeSharp.Nodes.Function;

/// <summary>
/// Class명 : Roslyn 스크립트 전역 변수 홀더
/// 역활 및 기능 : RoslynFunctionExecutor가 CSharpScript.Create의 globalsType으로 쓰는 최소 컨테이너 — 사용자 C# 코드가 msg를 Node-RED 스타일 소문자 동적 필드로 그대로 쓸 수 있게 함
///
/// Roslyn(CSharpScript)이 사용자 코드를 실행할 때 전역 범위에 노출하는 변수 1개(<see cref="msg"/>)만
/// 담는 컨테이너입니다. <c>CSharpScript.Create&lt;object&gt;(code, options, typeof(FunctionGlobals))</c>로
/// 넘기면, 사용자 코드 안에서 이 클래스의 공개 멤버를 지역 변수처럼(예: <c>msg.payload = 1;</c>) 바로
/// 쓸 수 있습니다.
/// 설계 근거: 02번 문서 5번 탭 카드7(RoslynFunctionExecutor 스니펫이 타입만 참조하고 정식 선언은
/// 없었던 공백 — NodeRef·PlcConnectionConfig와 동일 유형, 이 파일에서 처음 정의합니다), 03번 개발
/// Step맵 Phase 7 FN-02.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b><see cref="msg"/>가 <c>dynamic</c>인 이유</b>: 카드7 예제 코드는 <c>msg.payload</c>처럼
/// 소문자로 접근하지만, <see cref="NodeSharp.Contracts.Models.Msg"/>의 강타입 프로퍼티는
/// <c>Payload</c>(대문자)입니다. <see cref="msg"/>를 <c>Msg</c> 타입으로 선언하면 사용자 코드의
/// <c>msg.payload</c>가 존재하지 않는 멤버로 컴파일 오류(CS1061)가 됩니다 — <c>dynamic</c>으로
/// 선언하면 <c>Msg</c>가 상속하는 <c>DynamicObject.TryGetMember/TrySetMember</c>가 <c>binder.Name
/// ="payload"</c>로 호출되고, 이 키는 강타입 <c>Payload</c> 프로퍼티와 내부적으로 같은 저장소 슬롯을
/// 공유하므로(<c>Msg.cs</c> 참고) 두 표기가 서로 자유롭게 섞여도 항상 같은 값을 가리킵니다.</item>
/// <item><b>실제 런타임 타입</b>: <see cref="msg"/>에 대입되는 값은 항상
/// <see cref="NodeSharp.Contracts.Models.Msg"/> 인스턴스입니다(<c>RoslynFunctionExecutor.ExecuteAsync</c>
/// 참고) — <c>dynamic</c>은 컴파일 타임 타입 검사만 우회할 뿐, 런타임에는 그대로 <c>Msg</c>이므로
/// 사용자 코드가 <c>return msg;</c>로 돌려주면 호출자가 다시 <c>Msg</c>로 캐스팅할 수 있습니다.</item>
/// </list>
/// </remarks>
public sealed class FunctionGlobals
{
    /// <summary>
    /// 사용자 C# 코드가 전역 변수처럼 바로 쓰는 메시지 객체입니다. 실제 런타임 타입은 항상
    /// <see cref="NodeSharp.Contracts.Models.Msg"/>이며, <c>dynamic</c>으로 선언한 이유는 위 클래스
    /// remarks를 참고하십시오.
    /// </summary>
    public dynamic msg { get; set; } = null!;
}
