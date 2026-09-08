using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NodeSharp.Contracts.Models;
using NodeSharp.Editor.Core.Commands;
using NodeSharp.Editor.Core.Config;

namespace NodeSharp.Editor.Views;

/// <summary>
/// Class명 : 시퀀스 에디터 창
/// 역활 및 기능 : 캔버스(FlowCanvasView)와 별개의 독립 WPF Window로 뜨는 Sequence Editor — 단계 목록
/// 추가/삭제 편집 + sequences.json 저장/로드 + 역방향 내비게이션 진입점
///
/// (SQ-02) 02번 설계 문서 11번 탭 카드6이 요구하는 "캔버스와 별개의 독립 Window"입니다. 실제 중복
/// 방지·위치 기억 로직은 <see cref="Core.SequenceWindowManager"/>(별도 클래스, 창 인스턴스를 감싸는
/// 책임 분리 — MainWindow가 직접 <c>new SequenceEditorWindow()</c>를 호출하지 않고 매니저를 거치게 함)에
/// 있습니다.
/// (SQ-03, ★ 보강) "호출하는 Flow 노드 보기" 버튼을 누르면 <see cref="FindCallersRequested"/> 이벤트를
/// 발행합니다 — 이 창 자체는 <c>IFlowNodeIndex</c>/캔버스를 몰라도 되고(창이 아는 것은 "지금 텍스트
/// 상자에 어떤 문자열이 있는가"뿐), 실제 조회·하이라이트는 이 이벤트를 구독하는
/// <see cref="Core.SequenceWindowManager"/>가 담당합니다(책임 분리는 SQ-02와 동일한 원칙).
/// (SQ-04, ★ 보강) AskUserQuestion 확인("최소 단계 추가/삭제 UI까지 지금 구현") 결과에 따라 실제
/// 단계 목록 편집을 추가했습니다. <see cref="_steps"/>(현재 편집 중인 시퀀스의 단계 목록)에 대한
/// 추가/삭제는 <see cref="AddSequenceStepCommand"/>/<see cref="RemoveSequenceStepCommand"/>
/// (<see cref="IEditorCommand"/> 구현, <see cref="StructureView"/>의 <c>AddStructureNodeCommand</c>/
/// <c>DeleteStructureNodeCommand</c>와 동일한 중첩 private 클래스 패턴)로 실행되어,
/// <see cref="History"/>(<see cref="SequenceWindowManager"/>가 <c>FlowCanvas.History</c>를 그대로
/// 연결해주는 공유 <see cref="CommandHistory"/>, ED-D13과 동일한 배선)에 편입됩니다.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>(★ 범위 축소) 단계 상세 편집 없음</b>: 이 Step은 "단계를 추가/삭제할 수 있고, 그 동작이
/// Undo/Redo 되는지"만 완료 기준으로 삼습니다(AskUserQuestion 확인 답변 그대로). 추가된 단계는
/// <see cref="MakeDefaultStep"/>이 채우는 기본값(이름 "새 단계 N", TriggerExpression "true", 빈
/// ActionType)을 그대로 갖고, 이름 변경·조건식/동작 편집·순서 드래그 변경 UI는 여전히 없습니다 —
/// 02번 설계 문서 11번 탭 카드5가 나열하는 <see cref="SequenceStepDto"/> 필드 전체를 다루는 상세
/// 편집기는 후속 Step에서 별도로 판단합니다(SQ-02가 이미 예고해둔 "단계 목록 편집 화면" 공백의
/// 나머지 부분).</item>
/// <item><b>단계 목록 렌더링 방식</b>: XAML <c>ItemsControl</c> 데이터 바인딩 대신
/// <see cref="StructureView.RenderTree"/>와 동일하게 <see cref="RenderSteps"/>가 코드비하인드에서
/// <c>StepsPanel.Children</c>을 직접 갈아끼웁니다(이 프로젝트 전체의 기존 관례 — <c>StructureView</c>/
/// <c>FlowCanvasView</c> 모두 데이터 바인딩이 아니라 수동 렌더링을 씁니다).</item>
/// <item><b>저장 스키마</b>: sequences.json은 <see cref="SequenceDefinition"/> 목록이라(다른
/// 시퀀스도 같은 파일에 함께 저장), "저장"을 누르면 <see cref="_allSequences"/>(마지막으로 불러온
/// 전체 목록) 중 현재 편집 중인 Id만 교체(또는 신규 추가)하고 전체를 다시 씁니다 — 다른 시퀀스를
/// 안 불러온 상태에서 저장해도 그 시퀀스가 사라지지 않도록 하기 위함입니다. <see cref="OnLoadClick"/>을
/// 한 번도 누르지 않고 저장하면 파일 전체를 아직 못 읽은 것이므로, <see cref="OnSaveClick"/>이 먼저
/// 조용히 <see cref="OnLoadClick"/>과 동일한 로드를 수행합니다(다른 시퀀스 유실 방지).</item>
/// <item><b>(SQ-04, ★ 범위 축소) ED-D13 더티 비교·자동저장 미적용</b>: <see cref="SequenceStore"/>
/// 클래스 자체 주석 참고 — 이 Step은 "저장" 버튼을 눌렀을 때 항상 실제로 쓰는 가장 단순한 형태로
/// 시작합니다(EC-04 최초 <c>FlowStore</c>/<c>SaveFlowAsync</c>와 동일한 순서).</item>
/// </list>
/// </remarks>
public partial class SequenceEditorWindow : Window
{
    private readonly List<SequenceDefinition> _allSequences = new();
    private readonly List<SequenceStepDto> _steps = new();
    private readonly SequenceStore _sequenceStore = new();
    private int? _selectedStepIndex;

    /// <summary>"호출하는 Flow 노드 보기" 버튼을 눌렀을 때 발행되며, 인자는 <see cref="SequenceIdBox"/>에 입력된(공백 제거된) 시퀀스 Id입니다.</summary>
    public event Action<string>? FindCallersRequested;

    /// <summary>
    /// (SQ-04) sequences.json이 위치한 데이터 폴더 — <see cref="Core.SequenceWindowManager.ShowOrActivate"/>가
    /// <c>MainWindow.FlowCanvas.DataDirectory</c>를 그대로 채워줍니다(flows.json/device.json과 같은 폴더).
    /// </summary>
    public string DataDirectory { get; set; } = AppContext.BaseDirectory;

    /// <summary>
    /// (SQ-04) <see cref="Core.SequenceWindowManager.ShowOrActivate"/>가 <c>FlowCanvas.History</c>를
    /// 그대로 채워주는, <see cref="FlowCanvasView"/>/<see cref="StructureView"/>와 <b>공유하는</b>
    /// Undo/Redo 스택입니다(<see cref="IEditorCommand"/> 문서가 처음부터 예고한 설계 — ED-D13에서
    /// 구조 트리 커맨드가 편입된 것과 동일하게, 이번엔 시퀀스 단계 커맨드를 편입합니다). 아직
    /// 연결되지 않았으면(<c>null</c>, 예: 단위 테스트) <see cref="ExecuteOrDirect"/>가 커맨드를
    /// 스택에 올리지 않고 즉시 직접 실행합니다.
    /// </summary>
    public CommandHistory? History { get; set; }

    /// <summary>XAML에서 정의한 컨트롤을 초기화합니다(WPF 표준 패턴).</summary>
    public SequenceEditorWindow()
    {
        InitializeComponent();
        RenderSteps();
    }

    /// <summary>
    /// (SQ-03) <c>SequenceTriggerNode</c>를 더블클릭해 이 창을 열 때(<c>FlowCanvasView.OnCardMouseLeftButtonDown</c>)
    /// 그 노드의 <c>SequenceId</c>를 미리 채워둡니다 — "편도"로 들어온 시퀀스를 굳이 다시 타이핑하지
    /// 않고 바로 "호출하는 Flow 노드 보기"(역방향)를 눌러볼 수 있게 합니다. (SQ-04) 단계 목록은 별도로
    /// 자동 로드하지 않습니다 — "불러오기" 버튼을 명시적으로 눌러야 합니다(더블클릭마다 조용히
    /// sequences.json을 다시 읽는 것보다, 사용자가 편집 중인 단계 목록을 실수로 덮어쓰지 않는 쪽이
    /// 안전하다고 판단 — ★ 범위 축소).
    /// </summary>
    public void SetSequenceId(string sequenceId) => SequenceIdBox.Text = sequenceId;

    private void OnFindCallersClick(object sender, RoutedEventArgs e) =>
        FindCallersRequested?.Invoke(SequenceIdBox.Text.Trim());

    /// <summary>
    /// (SQ-04) <see cref="DataDirectory"/>\sequences.json 전체를 다시 읽어 <see cref="_allSequences"/>에
    /// 담고, <see cref="SequenceIdBox"/>에 입력된 Id와 일치하는 <see cref="SequenceDefinition"/>이
    /// 있으면 그 <see cref="SequenceDefinition.Steps"/>로 <see cref="_steps"/>를 채웁니다(없으면 빈
    /// 목록 — "새 시퀀스"로 시작). 되돌릴 수 있는 편집 동작이 아니므로(파일을 그대로 읽어오는
    /// 것뿐) <see cref="IEditorCommand"/>를 쓰지 않습니다.
    /// </summary>
    private async void OnLoadClick(object sender, RoutedEventArgs e)
    {
        var id = SequenceIdBox.Text.Trim();
        if (string.IsNullOrEmpty(id))
        {
            MessageBox.Show(this, "시퀀스 Id를 먼저 입력하세요.", "불러오기", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await LoadAllSequencesAsync();

        var found = _allSequences.FirstOrDefault(s => s.Id == id);
        _steps.Clear();
        if (found is not null)
        {
            _steps.AddRange(found.Steps);
        }

        _selectedStepIndex = null;
        RenderSteps();

        if (found is null)
        {
            MessageBox.Show(this, $"시퀀스 '{id}'가 아직 없습니다 — 단계를 추가한 뒤 저장하면 새로 만들어집니다.",
                "불러오기", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    /// <summary>
    /// (SQ-04) <see cref="DataDirectory"/>\sequences.json에서 <see cref="_allSequences"/>를 다시 읽어옵니다
    /// (파일이 없으면 빈 목록 — 최초 실행). <see cref="OnLoadClick"/>과 <see cref="OnSaveClick"/> 둘 다
    /// 이 메서드를 거쳐, 다른 세션/창에서 먼저 저장해둔 다른 시퀀스를 덮어쓰지 않게 합니다.
    /// </summary>
    private async Task LoadAllSequencesAsync()
    {
        var loaded = await _sequenceStore.LoadAsync(DataDirectory);
        _allSequences.Clear();
        if (loaded is not null)
        {
            _allSequences.AddRange(loaded);
        }
    }

    /// <summary>
    /// (SQ-04) 현재 편집 중인 <see cref="_steps"/>를 <see cref="SequenceIdBox"/>의 Id로
    /// <see cref="SequenceDefinition"/>을 만들어 <see cref="_allSequences"/> 중 같은 Id를 교체(없으면
    /// 새로 추가)한 뒤, 전체 목록을 <see cref="DataDirectory"/>\sequences.json에 원자적으로 저장합니다.
    /// 다른 시퀀스를 안 불러온 채로 저장해도 유실되지 않도록, 저장 직전 항상 파일 전체를
    /// <see cref="LoadAllSequencesAsync"/>로 다시 읽은 뒤 병합합니다(클래스 자체 주석 "저장 스키마"
    /// 참고).
    /// </summary>
    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var id = SequenceIdBox.Text.Trim();
        if (string.IsNullOrEmpty(id))
        {
            MessageBox.Show(this, "시퀀스 Id를 먼저 입력하세요.", "저장", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await LoadAllSequencesAsync();

        var existing = _allSequences.FirstOrDefault(s => s.Id == id);
        var definition = new SequenceDefinition(
            Id: id,
            Name: existing?.Name ?? id,
            Steps: _steps.OrderBy(s => s.Order).ToList(),
            WatchedTagIds: existing?.WatchedTagIds ?? Array.Empty<string>());

        var index = _allSequences.FindIndex(s => s.Id == id);
        if (index >= 0)
        {
            _allSequences[index] = definition;
        }
        else
        {
            _allSequences.Add(definition);
        }

        await _sequenceStore.SaveAsync(_allSequences, DataDirectory);

        MessageBox.Show(this, $"시퀀스 '{id}' 저장 완료 (단계 {_steps.Count}개).", "저장",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// (SQ-04) 다음 순번(<see cref="_steps"/>.Count)으로 기본값 단계를 만들어
    /// <see cref="AddSequenceStepCommand"/>로 실행합니다 — <see cref="ExecuteOrDirect"/>를 거치므로
    /// <see cref="History"/>가 연결돼 있으면 공유 CommandHistory에 편입됩니다.
    /// </summary>
    private void OnAddStepClick(object sender, RoutedEventArgs e)
    {
        var step = MakeDefaultStep(_steps.Count);
        ExecuteOrDirect(new AddSequenceStepCommand(this, step));
    }

    /// <summary>
    /// (SQ-04) <see cref="_selectedStepIndex"/>(단계 목록에서 클릭해 선택한 행)가 없으면 안내만 하고,
    /// 있으면 <see cref="RemoveSequenceStepCommand"/>로 실행합니다.
    /// </summary>
    private void OnRemoveStepClick(object sender, RoutedEventArgs e)
    {
        if (_selectedStepIndex is not { } index || index < 0 || index >= _steps.Count)
        {
            MessageBox.Show(this, "삭제할 단계를 목록에서 먼저 선택하세요.", "단계 삭제", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ExecuteOrDirect(new RemoveSequenceStepCommand(this, _steps[index], index));
    }

    /// <summary>(NR-04 스타일 기본값) 새 단계의 초기 내용 — 이름만 순번대로 구분되고, 나머지는 상세 편집 UI가 생기기 전까지의 자리표시자 값입니다.</summary>
    private static SequenceStepDto MakeDefaultStep(int order) => new(
        Order: order,
        Name: $"새 단계 {order + 1}",
        TriggerExpression: "true",
        ActionType: string.Empty,
        ActionParams: new Dictionary<string, object?>());

    /// <summary>
    /// (ED-D13의 <c>StructureView.ExecuteOrDirect</c>와 동일한 패턴) <see cref="History"/>가 연결돼
    /// 있으면 <see cref="CommandHistory.Execute"/>(Undo 스택에 기록)로, 아니면
    /// <paramref name="command"/>의 <see cref="IEditorCommand.Do"/>만 직접 호출합니다(하위 호환 —
    /// <see cref="History"/> 미연결 시나리오, 예: 단위 테스트에서 이 창만 단독 생성한 경우).
    /// </summary>
    private void ExecuteOrDirect(IEditorCommand command)
    {
        if (History is not null)
        {
            History.Execute(command);
        }
        else
        {
            command.Do();
        }
    }

    /// <summary>
    /// (SQ-04) <see cref="StructureView.RenderTree"/>와 동일한 방식 — <c>StepsPanel.Children</c>을
    /// 비우고 <see cref="_steps"/>(Order 기준 정렬) 순서대로 한 줄씩 다시 그립니다. 각 행을 클릭하면
    /// <see cref="_selectedStepIndex"/>가 그 행의 인덱스로 바뀌고(강조 배경으로 표시), 다시 그려집니다.
    /// </summary>
    private void RenderSteps()
    {
        StepsPanel.Children.Clear();

        if (_steps.Count == 0)
        {
            StepsPanel.Children.Add(new TextBlock
            {
                Text = "단계가 없습니다 — + 단계 추가 버튼을 눌러 시작하세요.",
                Foreground = (Brush)FindResource("PrimaryTextBrush"),
                Opacity = 0.6,
                Margin = new Thickness(8),
            });
            return;
        }

        var ordered = _steps.OrderBy(s => s.Order).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var step = ordered[i];
            var actualIndex = _steps.IndexOf(step);
            var isSelected = _selectedStepIndex == actualIndex;

            var row = new Border
            {
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 2),
                Background = isSelected
                    ? (Brush)FindResource("AccentBrush")
                    : Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = actualIndex,
            };
            row.MouseLeftButtonDown += (_, _) =>
            {
                _selectedStepIndex = actualIndex;
                RenderSteps();
            };

            row.Child = new TextBlock
            {
                Text = $"{step.Order}. {step.Name}",
                Foreground = (Brush)FindResource("PrimaryTextBrush"),
            };

            StepsPanel.Children.Add(row);
        }
    }

    /// <summary>
    /// (SQ-04) <see cref="OnAddStepClick"/>이 만드는, 단계 1개를 <see cref="_steps"/> 끝에 추가하는
    /// 커맨드입니다 — <see cref="StructureView"/>의 <c>AddStructureNodeCommand</c>와 동일한 패턴을
    /// 시퀀스 단계 목록에 그대로 옮긴 것입니다.
    /// </summary>
    private sealed class AddSequenceStepCommand : IEditorCommand
    {
        private readonly SequenceEditorWindow _owner;
        private readonly SequenceStepDto _step;

        public AddSequenceStepCommand(SequenceEditorWindow owner, SequenceStepDto step)
        {
            _owner = owner;
            _step = step;
        }

        public string Description => $"시퀀스 단계 추가: {_step.Name}";

        public void Do()
        {
            _owner._steps.Add(_step);
            _owner.RenderSteps();
        }

        public void Undo()
        {
            _owner._steps.Remove(_step);
            _owner._selectedStepIndex = null;
            _owner.RenderSteps();
        }
    }

    /// <summary>
    /// (SQ-04) <see cref="OnRemoveStepClick"/>이 만드는, 단계 1개를 <see cref="_steps"/>에서 제거하는
    /// 커맨드입니다. <see cref="Undo"/>는 <see cref="_index"/>(삭제 직전 인덱스)에 정확히 다시 끼워
    /// 넣습니다(<see cref="StructureView"/>의 <c>DeleteStructureNodeCommand</c>와 동일한 패턴).
    /// </summary>
    private sealed class RemoveSequenceStepCommand : IEditorCommand
    {
        private readonly SequenceEditorWindow _owner;
        private readonly SequenceStepDto _step;
        private readonly int _index;

        public RemoveSequenceStepCommand(SequenceEditorWindow owner, SequenceStepDto step, int index)
        {
            _owner = owner;
            _step = step;
            _index = index;
        }

        public string Description => $"시퀀스 단계 삭제: {_step.Name}";

        public void Do()
        {
            _owner._steps.RemoveAt(_index);
            _owner._selectedStepIndex = null;
            _owner.RenderSteps();
        }

        public void Undo()
        {
            _owner._steps.Insert(_index, _step);
            _owner.RenderSteps();
        }
    }
}
