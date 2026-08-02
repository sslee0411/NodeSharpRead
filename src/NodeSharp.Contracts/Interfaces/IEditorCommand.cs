namespace NodeSharp.Contracts.Interfaces;

/// <summary>
/// Class명 : 에디터 명령 계약
/// 역활 및 기능 : Editor Undo/Redo 스택에 들어가는 실행 취소 가능한 작업 하나를 나타내는 계약
///
/// Editor의 Undo/Redo 스택(<c>CommandHistory</c>)에 들어가는 실행 취소 가능한 작업 하나를
/// 나타내는 계약입니다. 캔버스 커맨드(노드 추가/삭제/이동)와 구조 트리 커맨드(태그 추가,
/// 스케일 변경 등)가 이 인터페이스 하나를 공유해, Ctrl+Z가 도메인 구분 없이 동작합니다.
/// 설계 근거: 02번 문서 2번 탭 카드 9(통합 저장/배포 ②).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><see cref="Do"/>는 커맨드 객체 생성 시점이 아니라 <c>CommandHistory</c>에 실제로 등록되는
/// 시점에 호출됩니다 — 커맨드 객체를 만드는 것만으로는 작업이 실행되지 않습니다.</item>
/// <item>50단계 제한(<c>EC-07</c>)은 이 인터페이스가 아니라 <c>CommandHistory</c> 쪽에서
/// 스택 크기로 관리합니다.</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // 1) 캔버스 커맨드 — 노드 이동
/// public sealed class NodeMoveCommand : IEditorCommand
/// {
///     private readonly string _nodeName;
///     private readonly (double X, double Y) _from, _to;
///     public string Description =&gt; $"{_nodeName} 이동";
///     public void Do()   { /* 노드의 캔버스 좌표를 _to로 갱신 */ }
///     public void Undo() { /* 노드의 캔버스 좌표를 _from으로 복원 */ }
/// }
///
/// // 2) 구조 트리 커맨드 — 알람 임계값 변경(캔버스 커맨드와 같은 스택을 공유)
/// public sealed class AlarmThresholdChangeCommand : IEditorCommand
/// {
///     public string Description =&gt; "알람 임계값 변경";
///     public void Do()   { /* 새 임계값 적용 */ }
///     public void Undo() { /* 이전 임계값으로 복원 */ }
/// }
///
/// // Ctrl+Z는 두 커맨드가 어떤 도메인인지 구분하지 않고 "가장 최근 작업"을 되돌린다
/// // commandHistory.Execute(new NodeMoveCommand(...));
/// // commandHistory.Execute(new AlarmThresholdChangeCommand(...));
/// // commandHistory.Undo();   // 알람 임계값 변경이 먼저 되돌아감
/// </code>
/// </example>
public interface IEditorCommand
{
    /// <summary>이 커맨드가 실제로 수행하는 작업을 실행합니다. <c>CommandHistory</c>에 등록되는 시점에 호출됩니다.</summary>
    void Do();

    /// <summary><see cref="Do"/>가 수행한 작업을 되돌립니다.</summary>
    void Undo();

    /// <summary>Undo/Redo 히스토리 UI에 표시되는 짧은 설명(예: "노드 이동", "알람 임계값 변경").</summary>
    string Description { get; }
}
