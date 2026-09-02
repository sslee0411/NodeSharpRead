using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NodeSharp.Editor.Core;
using NodeSharp.Editor.Structure;

namespace NodeSharp.Editor.Views;

/// <summary>
/// Class명 : 시뮬레이터 패널
/// 역활 및 기능 : SimulationMode=true인 PlcNode를 골라, Runner가 소유한 VirtualModbusSlave(PD-01c)의
/// 레지스터 값을 SignalR로 원격 기입하는 Editor 다섯 번째 사이드바 탭
///
/// (PD-01d, ★ 신규) SimulatorPanelView.xaml 상단 주석 참고 — 최초 범위는 "체크박스+패널 UI만"(사용자
/// 확인, 2026-09-02)이라, 당시에는 이 클래스가 VirtualModbusSlave를 Editor 프로세스 안에 직접
/// 만들어 들고 있었습니다.
/// (PD-01e, ★ 재설계) 캔버스 PlcTagReadNode 실시간 반영을 구현하려고 보니, Editor와 Runner는 별도
/// OS 프로세스라 Editor가 만든 VirtualModbusSlave를 Runner의 DeviceMapPoller가 읽을 방법이 없다는
/// 설계 공백을 발견했습니다(PD-01e 착수 전 조사) — 사용자 확인("Runner로 이전 + SignalR 원격제어",
/// 2026-09-02)에 따라 VirtualModbusSlave 소유권을 Runner(<c>SimulationDeviceBinder</c>/
/// <c>SimulationSlaveHolder</c>)로 옮기고, 이 클래스는 이제 값을 "직접 들고" 있지 않습니다 — 사용자가
/// 표를 편집하면 <see cref="EditorMonitorClient.SetSimulatedRegisterAsync"/>(신규 SignalR 클라이언트→
/// 서버 채널, LK-02b <c>TriggerInjectAsync</c>와 동일한 패턴)로 Runner에 그대로 원격 기입만 합니다.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>(PD-01e) 표시값은 "이 세션에서 입력한 값" — Runner의 실제 값을 되읽지 않음</b>: Runner→Editor
/// 방향으로 "지금 레지스터 값이 뭔지"를 조회하는 채널이 아직 없습니다(<c>MonitorHub</c>는 지금까지
/// 항상 push 또는 명령형 채널만 가졌지, "값 조회" 요청-응답은 <c>GetMsgTrace</c> 하나뿐이고 레지스터
/// 조회 목적이 아님). 이 Step의 범위(사용자 확인 "전체 4단계 범위 그대로 구현")는 "쓰기"까지만
/// 요구하므로, 표에 보이는 값은 여전히 이 Editor 세션에서 마지막으로 입력한 값이고, 다른 경로(예:
/// PlcTagReadNode가 캔버스에서 값을 바꾸는 시나리오는 이 Step 범위 밖)로 Runner 쪽 레지스터가
/// 바뀌어도 이 표에는 반영되지 않습니다 — 조회 채널 추가는 후속 Step으로 남깁니다.</item>
/// <item><b>연결 안 됨이면 조용히 무시</b>: <see cref="EditorMonitorClient.IsConnected"/>가
/// <c>false</c>면(Runner 미실행 등) <see cref="OnAddRegisterClick"/>이 SignalR 호출 자체를 시도하지
/// 않습니다 — <c>TriggerInjectAsync</c> 호출부(<c>FlowCanvasView</c>)와 동일한 원칙으로, 예외를
/// 던지는 API를 굳이 try/catch로 감싸기보다 호출 전에 확인합니다.</item>
/// <item><b>PLC 목록 새로고침 시점</b>: 구조 설정 탭에서 PlcNode를 추가/삭제하거나 SimulationMode를
/// 켜고 끄는 것을 실시간으로 구독하지 않고, "새로고침" 버튼(및 <see cref="SetDeviceTree"/> 최초 호출)
/// 시점에만 트리를 다시 훑습니다 — PD-01d 당시 판단을 그대로 유지합니다.</item>
/// </list>
/// </remarks>
public partial class SimulatorPanelView : UserControl
{
    private readonly Dictionary<string, ObservableCollection<RegisterRowViewModel>> _registerRows = new();
    private ObservableCollection<StructureTreeNode>? _devices;
    private PlcNode? _selectedPlc;
    private EditorMonitorClient? _monitorClient;

    public SimulatorPanelView()
    {
        InitializeComponent();
        RefreshButton.MouseLeftButtonDown += (_, _) => RefreshPlcList();
        AddRegisterButton.MouseLeftButtonDown += (_, _) => OnAddRegisterClick();
    }

    /// <summary>MainWindow.xaml.cs 생성자가 StructureTab.Devices(구조 설정 트리 루트)를 넘겨줍니다 — 이 시점에는 아직 비어 있을 수 있으므로(StructureTab의 비동기 로드가 나중에 채움) 실제 PLC 목록은 "새로고침"을 누를 때(또는 이 메서드가 처음 불릴 때) 다시 훑습니다.</summary>
    public void SetDeviceTree(ObservableCollection<StructureTreeNode> devices)
    {
        _devices = devices;
        RefreshPlcList();
    }

    /// <summary>
    /// (PD-01e, ★ 신규) MainWindow.xaml.cs 생성자가 자신의 <c>_monitorClient</c>(Runner "/hubs/monitor"
    /// SignalR 연결)를 넘겨줍니다 — <see cref="OnAddRegisterClick"/>이 이 인스턴스를 통해
    /// <see cref="EditorMonitorClient.SetSimulatedRegisterAsync"/>를 호출합니다. 호출되지 않았으면
    /// (테스트 등) <c>_monitorClient</c>가 <c>null</c>인 채로 남아 <see cref="OnAddRegisterClick"/>이
    /// 아무 SignalR 호출도 하지 않습니다(조용히 무시 — 클래스 remarks "연결 안 됨" 항목과 동일한 원칙).
    /// </summary>
    public void SetMonitorClient(EditorMonitorClient monitorClient) => _monitorClient = monitorClient;

    /// <summary><see cref="_devices"/>를 장비→PLC 2단계만 훑어 SimulationMode=true인 PlcNode 목록을 다시 그립니다. 선택돼 있던 PLC가 목록에서 사라졌으면(SimulationMode를 끔·삭제됨) 선택을 해제합니다.</summary>
    private void RefreshPlcList()
    {
        PlcListPanel.Children.Clear();

        var simPlcs = new List<(DeviceNode Device, PlcNode Plc)>();
        if (_devices is not null)
        {
            foreach (var deviceNode in _devices.OfType<DeviceNode>())
            {
                foreach (var plc in deviceNode.Children.OfType<PlcNode>().Where(p => p.SimulationMode))
                {
                    simPlcs.Add((deviceNode, plc));
                }
            }
        }

        EmptyPlcHint.Visibility = simPlcs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var (device, plc) in simPlcs)
        {
            var row = new Border
            {
                Background = ReferenceEquals(_selectedPlc, plc) ? (Brush)FindResource("AccentBrush") : Brushes.Transparent,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 2),
                Cursor = Cursors.Hand,
            };
            row.Child = new TextBlock
            {
                Text = $"{device.Name} › {plc.Name}",
                Foreground = (Brush)FindResource("PrimaryTextBrush"),
                FontSize = 12,
            };
            row.MouseLeftButtonDown += (_, _) => SelectPlc(plc);
            PlcListPanel.Children.Add(row);
        }

        if (_selectedPlc is not null && !simPlcs.Any(t => ReferenceEquals(t.Plc, _selectedPlc)))
        {
            SelectPlc(null); // 선택 해제 — 이 안에서 다시 RefreshPlcList를 부르지만 _selectedPlc가 이미 null이라 재귀는 여기서 끝난다.
        }
    }

    /// <summary><paramref name="plc"/>를 선택 상태로 만들고, 그 PLC의 레지스터 표(이 세션에서 입력한 값 — 클래스 remarks 참고)를 DataGrid에 연결합니다. null이면 표를 감추고 선택을 해제합니다.</summary>
    private void SelectPlc(PlcNode? plc)
    {
        _selectedPlc = plc;

        if (plc is null)
        {
            RegisterGrid.ItemsSource = null;
            RegisterGrid.Visibility = Visibility.Collapsed;
            AddRegisterRow.Visibility = Visibility.Collapsed;
            NoPlcSelectedHint.Visibility = Visibility.Visible;
            RefreshPlcList();
            return;
        }

        if (!_registerRows.TryGetValue(plc.Id, out var rows))
        {
            rows = new ObservableCollection<RegisterRowViewModel>();
            _registerRows[plc.Id] = rows;
        }

        RegisterGrid.ItemsSource = rows;
        RegisterGrid.Visibility = Visibility.Visible;
        AddRegisterRow.Visibility = Visibility.Visible;
        NoPlcSelectedHint.Visibility = Visibility.Collapsed;

        RefreshPlcList(); // 목록의 선택 강조(배경색)를 새로 그리기 위해.
    }

    /// <summary>
    /// "추가/수정" 클릭 — 입력한 주소·값을 검증(0~65535)한 뒤, 이미 표에 있는 주소면 값만 갱신하고,
    /// 없으면 새 행을 추가합니다. 둘 다 <see cref="RegisterRowViewModel"/>을 거쳐
    /// (PD-01e) <see cref="EditorMonitorClient.SetSimulatedRegisterAsync"/>로 Runner에 원격 기입됩니다
    /// — <see cref="_monitorClient"/>가 아직 연결돼 있지 않으면(클래스 remarks "연결 안 됨" 항목) 표
    /// 갱신만 하고 원격 호출은 건너뜁니다.
    /// </summary>
    private void OnAddRegisterClick()
    {
        if (_selectedPlc is null)
        {
            return;
        }

        if (!int.TryParse(AddressInput.Text, out var address) || address is < 0 or > 65535)
        {
            MessageBox.Show("주소는 0~65535 사이의 정수여야 합니다.", "시뮬레이터", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(ValueInput.Text, out var value) || value is < 0 or > 65535)
        {
            MessageBox.Show("값은 0~65535 사이의 정수여야 합니다.", "시뮬레이터", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var plcId = _selectedPlc.Id;
        var rows = _registerRows[plcId];
        var existingRow = rows.FirstOrDefault(r => r.Address == address);
        if (existingRow is not null)
        {
            existingRow.Value = value; // setter가 SendSimulatedRegister까지 반영.
        }
        else
        {
            rows.Add(new RegisterRowViewModel(address, value, plcId, SendSimulatedRegister));
        }

        AddressInput.Text = string.Empty;
        ValueInput.Text = string.Empty;
    }

    /// <summary>
    /// (PD-01e, ★ 신규) <see cref="RegisterRowViewModel"/>이 값이 바뀔 때마다 호출하는 콜백 — 연결돼
    /// 있으면 <see cref="EditorMonitorClient.SetSimulatedRegisterAsync"/>를 실행하고, 그 Task는
    /// 기다리지 않습니다(<c>StatusBroadcaster.Broadcast</c>의 "전송 예외 격리"와 동일한 정신 — UI
    /// 편집 자체가 네트워크 호출 실패로 멈추면 안 됨). 실패해도 콘솔에 한 줄만 남깁니다.
    /// </summary>
    private void SendSimulatedRegister(string plcId, int address, int value)
    {
        if (_monitorClient is not { IsConnected: true } client)
        {
            return;
        }

        _ = client.SetSimulatedRegisterAsync(plcId, address, value).ContinueWith(
            t => Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] 시뮬레이터 레지스터 원격 기입 실패 — {t.Exception?.GetBaseException().Message}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>
    /// Class명 : 레지스터 행 뷰모델
    /// 역활 및 기능 : RegisterGrid(DataGrid) 한 행 — 주소는 고정, 값은 편집하면 즉시 Runner로 원격 기입
    ///
    /// (PD-01d, ★ 신규) <see cref="Value"/> setter가 값을 반영하던 대상은 (PD-01e, ★ 갱신) Editor
    /// 프로세스 안의 <c>VirtualModbusSlave</c>가 아니라, 생성자로 받은 <paramref name="onChanged"/>
    /// 콜백(<see cref="SendSimulatedRegister"/>)을 통해 Runner로 원격 기입하는 방식으로 바뀌었습니다 —
    /// DataGrid 셀 편집이 끝나는 순간(바인딩의 <c>UpdateSourceTrigger=PropertyChanged</c>) 곧바로
    /// 호출됩니다. 별도의 "저장" 버튼은 여전히 없습니다.
    /// </summary>
    private sealed class RegisterRowViewModel : INotifyPropertyChanged
    {
        private readonly string _plcId;
        private readonly Action<string, int, int> _onChanged;
        private int _value;

        public RegisterRowViewModel(int address, int value, string plcId, Action<string, int, int> onChanged)
        {
            Address = address;
            _plcId = plcId;
            _onChanged = onChanged;
            _value = Math.Clamp(value, 0, 65535);
            _onChanged(_plcId, Address, _value);
        }

        /// <summary>Modbus 레지스터 주소(0~65535) — 생성 후 변경되지 않습니다(주소를 바꾸고 싶으면 새 행을 추가).</summary>
        public int Address { get; }

        /// <summary>현재 레지스터 값(0~65535). 설정하면 즉시 <see cref="_onChanged"/>(Runner 원격 기입)로 반영됩니다.</summary>
        public int Value
        {
            get => _value;
            set
            {
                var clamped = Math.Clamp(value, 0, 65535);
                if (_value == clamped)
                {
                    return;
                }

                _value = clamped;
                _onChanged(_plcId, Address, _value);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
