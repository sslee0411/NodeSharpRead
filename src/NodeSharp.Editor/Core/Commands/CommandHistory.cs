namespace NodeSharp.Editor.Core.Commands;

/// <summary>
/// Class명 : 커맨드 히스토리
/// 역활 및 기능 : 최대 50단계까지 IEditorCommand를 기억해 Undo/Redo를 제공하는 스택
///
/// 실행된 <see cref="IEditorCommand"/>를 최대 <see cref="MaxDepth"/>(50)단계까지 기억하는 Undo
/// 스택과, <see cref="Undo"/>로 되돌린 커맨드를 다시 실행할 수 있게 담아두는 Redo 스택을
/// 관리합니다. <see cref="Execute"/>로 새 커맨드를 실행하면 Redo 스택은 비워집니다(새로운 편집이
/// 이전에 되돌렸던 "미래"를 덮어쓰는 것 — 대부분의 에디터가 따르는 일반적인 Undo/Redo 관례).
/// Undo 스택이 <see cref="MaxDepth"/>를 넘으면 가장 오래된 커맨드부터 버립니다(그 이상은 더 이상
/// 되돌릴 수 없음 — EC-07 완료 기준의 "51번째 작업 후 Undo 51번을 눌러도 가장 오래된 1건은
/// 복구되지 않음"이 이 동작입니다).
/// 설계 근거: 02번 문서 9번 탭(<c>CommandHistory</c> 최대 50단계, iiot-system-arch S-29 패턴 재사용).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>스레드 안전성 없음</b>: WPF UI 스레드에서만 호출되는 것을 전제로 합니다(캔버스 편집은
/// 항상 UI 스레드 이벤트 핸들러에서 일어나므로) — 락(lock)을 두지 않았습니다.</item>
/// <item><b>재실행 가능성 전제</b>: <see cref="Redo"/>는 커맨드의 <see cref="IEditorCommand.Do"/>를
/// 그대로 다시 호출합니다. 커맨드 구현체가 내부 상태를 <c>Do</c> 호출마다 새로 계산하지 않고 생성자
/// 시점에 고정된 값만 쓴다면(<c>IEditorCommand</c> 예제 참고) 몇 번을 오가도 항상 같은 결과가
/// 나옵니다.</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // 1) 기본 사용 — Execute/Undo/Redo
/// var history = new CommandHistory();
/// history.Execute(command1); // Do() 호출, Undo 스택 = [command1]
/// history.Execute(command2); // Do() 호출, Undo 스택 = [command1, command2]
/// history.Undo();            // command2.Undo() 호출, Undo 스택 = [command1], Redo 스택 = [command2]
/// history.Redo();            // command2.Do() 재호출, Undo 스택 = [command1, command2]
///
/// // 2) 새 커맨드 실행은 Redo 스택을 비운다
/// history.Undo();            // Redo 스택 = [command2]
/// history.Execute(command3); // command3 실행 — 이제 command2는 다시 실행할 수 없음(Redo 스택 비워짐)
/// bool canRedoCommand2 = history.CanRedo; // false
///
/// // 3) 50단계 제한 — 51번째 실행 시 가장 오래된 1건이 버려진다
/// for (var i = 0; i &lt; 51; i++)
/// {
///     history.Execute(new SomeCommand(i));
/// }
/// // 51번 Undo를 눌러도 맨 처음(0번째) 커맨드는 복구되지 않는다(Undo 스택 최대 50개).
/// </code>
/// </example>
public sealed class CommandHistory
{
    /// <summary>Undo 스택이 기억하는 최대 커맨드 개수. 이 개수를 넘으면 가장 오래된 것부터 버려집니다.</summary>
    public const int MaxDepth = 50;

    private readonly LinkedList<IEditorCommand> _undoStack = new();
    private readonly Stack<IEditorCommand> _redoStack = new();

    /// <summary>Undo할 수 있는 커맨드가 하나 이상 있는지.</summary>
    public bool CanUndo => _undoStack.Count > 0;

    /// <summary>Redo할 수 있는 커맨드가 하나 이상 있는지.</summary>
    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>
    /// <paramref name="command"/>.<see cref="IEditorCommand.Do"/>를 실행하고 Undo 스택 맨 뒤에
    /// 추가합니다. Redo 스택은 비웁니다(새 편집이 발생하면 이전에 되돌렸던 "미래"는 더 이상 다시
    /// 실행할 수 없는 것이 일반적인 Undo/Redo 관례입니다). Undo 스택이 <see cref="MaxDepth"/>를
    /// 넘으면 맨 앞(가장 오래된 것)을 제거합니다.
    /// </summary>
    /// <param name="command">실행하고 히스토리에 기록할 커맨드.</param>
    public void Execute(IEditorCommand command)
    {
        command.Do();

        _undoStack.AddLast(command);
        if (_undoStack.Count > MaxDepth)
        {
            _undoStack.RemoveFirst();
        }

        _redoStack.Clear();
    }

    /// <summary>
    /// Undo 스택 맨 뒤(가장 최근 실행된) 커맨드를 꺼내 <see cref="IEditorCommand.Undo"/>를 호출하고
    /// Redo 스택으로 옮겨 담습니다. Undo 스택이 비어있으면(<see cref="CanUndo"/>가 <c>false</c>)
    /// 아무 것도 하지 않습니다.
    /// </summary>
    public void Undo()
    {
        if (_undoStack.Last is not { } lastNode)
        {
            return;
        }

        var command = lastNode.Value;
        _undoStack.RemoveLast();
        command.Undo();
        _redoStack.Push(command);
    }

    /// <summary>
    /// Redo 스택에서 커맨드를 하나 꺼내 <see cref="IEditorCommand.Do"/>를 다시 호출하고 Undo
    /// 스택으로 되돌립니다(<see cref="MaxDepth"/> 초과 시 가장 오래된 것을 버리는 규칙도 동일하게
    /// 적용). Redo 스택이 비어있으면(<see cref="CanRedo"/>가 <c>false</c>) 아무 것도 하지 않습니다.
    /// </summary>
    public void Redo()
    {
        if (_redoStack.Count == 0)
        {
            return;
        }

        var command = _redoStack.Pop();
        command.Do();

        _undoStack.AddLast(command);
        if (_undoStack.Count > MaxDepth)
        {
            _undoStack.RemoveFirst();
        }
    }
}
