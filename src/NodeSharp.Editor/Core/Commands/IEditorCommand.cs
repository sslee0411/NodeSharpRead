namespace NodeSharp.Editor.Core.Commands;

/// <summary>
/// Class명 : 에디터 커맨드
/// 역활 및 기능 : Undo/Redo 가능한 캔버스(및 향후 구조 트리) 편집 동작 하나를 나타내는 인터페이스
///
/// 캔버스에서 실행되는 되돌릴 수 있는 편집 동작(노드 추가, 와이어 연결, 속성 편집 등) 하나를
/// 나타냅니다. <see cref="CommandHistory"/>가 이 인터페이스만으로 커맨드를 실행·되돌리므로,
/// 구현체는 <see cref="Do"/>/<see cref="Undo"/> 호출 한 쌍만으로 상태를 정확히 오갈 수 있어야
/// 합니다(즉 <c>Undo</c>는 <c>Do</c>가 만든 변경을 완전히 상쇄해야 하고, 이후 다시 <c>Do</c>를
/// 호출하면 처음과 동일한 결과가 나와야 함 — <see cref="CommandHistory.Redo"/>가 이 재실행
/// 가능성에 의존합니다).
/// 설계 근거: 02번 문서 8번 탭 카드16(캔버스 커맨드와 구조 트리 커맨드가 같은
/// <see cref="CommandHistory"/> 스택을 공유하도록 미리 열어둔 인터페이스 — 실제 구조 트리 커맨드
/// 구현체는 ED-D13에서 추가되며, 지금은 캔버스 커맨드(<c>NodeSharp.Editor.Views.FlowCanvasView</c>의
/// 중첩 클래스)만 존재합니다).
/// </summary>
/// <example>
/// <code>
/// // 1) 캔버스 커맨드 구현 예시(FlowCanvasView의 중첩 private 클래스 패턴) — Do/Undo가 정확히
/// // 서로를 상쇄한다.
/// private sealed class AddNodeCommand : IEditorCommand
/// {
///     private readonly FlowCanvasView _owner;
///     private readonly NodeConfig _config;
///     public AddNodeCommand(FlowCanvasView owner, NodeConfig config) { _owner = owner; _config = config; }
///     public string Description => $"노드 추가: {_config.Name}";
///     public void Do() { _owner._nodeConfigs[_config.Id] = _config; _owner.RedrawActiveTab(); }
///     public void Undo() { _owner._nodeConfigs.Remove(_config.Id); _owner.RedrawActiveTab(); }
/// }
///
/// // 2) CommandHistory와 함께 쓰는 방법 — 직접 Do()를 호출하지 않고 항상 Execute를 거친다
/// // (Execute가 스택 관리(50단계 제한, Redo 스택 비우기)까지 함께 처리하므로).
/// var history = new CommandHistory();
/// history.Execute(new AddNodeCommand(owner, config)); // Do() 호출 + Undo 스택에 기록
/// history.Undo();                                     // 방금 추가한 노드를 되돌림
/// history.Redo();                                     // 되돌린 것을 다시 실행
/// </code>
/// </example>
public interface IEditorCommand
{
    /// <summary>이 커맨드가 나타내는 편집 동작을 실행합니다(최초 실행과 <see cref="CommandHistory.Redo"/> 양쪽에서 호출됨).</summary>
    void Do();

    /// <summary><see cref="Do"/>가 만든 변경을 정확히 되돌립니다.</summary>
    void Undo();

    /// <summary>Undo/Redo 메뉴 등에 표시할, 사람이 읽을 수 있는 이 커맨드의 설명(예: "노드 추가: function").</summary>
    string Description { get; }
}
