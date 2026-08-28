using System.Text.Json;
using NodeSharp.Contracts.Models;

namespace NodeSharp.Runner.Core;

/// <summary>
/// Class명 : 구조 재로더
/// 역활 및 기능 : device.json.signal 변경 감지 시 FlowEngine을 전혀 건드리지 않고 device.json만
/// 다시 읽어 CurrentDeviceTreeHolder에 반영하는 "가벼운" 재로드 진입점
///
/// (ED-D13, ★ 완료 기준 — "구조만 바뀌면 ReloadStructureOnlyAsync로 가볍게, Flow도 바뀌면 기존
/// DeployAsync까지 수행") 착수 전 조사 결과: 기존에는 Ctrl+S 한 번에 flows.json/device.json이
/// 항상 함께 저장됐고(<c>MainWindow.OnSaveFlowClick</c>, EC-04/ED-D03), 재배포는 오직
/// <c>flows.json.signal</c> 하나만 감시하는 <see cref="FlowFileWatcher"/>(LK-01)가 트리거했습니다 —
/// 구조만 바뀌어도 결과적으로 매번 <c>flows.json.signal</c>이 함께 갱신돼(내용이 그대로였어도) 매번
/// <see cref="FlowDeployer.RedeployAsync"/>(전체 재배포)가 불필요하게 실행됐고, 반대로
/// <c>device.json.signal</c>은 아무도 감시하지 않아 완전히 버려지고 있었습니다.
/// 이번 Step에서 (1) Editor 쪽(<c>NodeSharp.Editor.Views.FlowCanvasView.SaveFlowAsync</c>/
/// <c>NodeSharp.Editor.Views.StructureView.SaveDeviceTreeAsync</c>)이 실제로 내용이 달라진 파일만
/// 저장·신호를 남기도록 고치고, (2) <c>Worker</c>가 <see cref="FlowFileWatcher"/>를
/// <c>"device.json.signal"</c> 감시용으로 하나 더 만들어 이 클래스의 <see cref="ReloadStructureOnlyAsync"/>를
/// 콜백으로 연결했습니다 — 그 결과 "구조만 저장" 시나리오에서는 <c>FlowEngine.DeployAsync</c>가
/// 전혀 호출되지 않습니다.
/// <see cref="IStructureService"/>(CT-04b가 선언만 해둔 Contracts 인터페이스)의 실제 구현체가 아직
/// 없어(ED-D12 조사에서 재확인) 지금은 device.json을 다시 읽어 <see cref="CurrentDeviceTreeHolder"/>에
/// 반영하는 것 이상은 하지 않지만, "구조가 바뀌었을 때 FlowEngine을 건드리지 않는 가벼운 경로"라는
/// 이 Step의 핵심 요구는 이미 충족합니다 — 실제 PLC 연결·태그 조회 등 <c>IStructureService</c> 소비
/// 로직은 그 구현체가 생기는 후속 Step(PD-01x 계열 이후)에서 이 홀더를 읽어가는 방식으로 이어질
/// 예정입니다.
/// </summary>
/// <example>
/// <code>
/// var deviceTreeHolder = new CurrentDeviceTreeHolder();
/// using var deviceFileWatcher = new FlowFileWatcher(
///     baseDirectory,
///     ct =&gt; StructureReloader.ReloadStructureOnlyAsync(baseDirectory, deviceTreeHolder, ct),
///     signalFileName: "device.json.signal");
/// // 이후 Editor가 device.json만 저장(Flow는 그대로)할 때마다 FlowEngine 재배포 없이
/// // deviceTreeHolder.DeviceTree만 최신 내용으로 갱신된다.
/// </code>
/// </example>
public static class StructureReloader
{
    /// <summary>
    /// <paramref name="baseDirectory"/>\device.json을 다시 읽어 <paramref name="holder"/>에 반영합니다.
    /// 파일이 없거나 JSON 파싱에 실패하면(저장 도중 신호가 먼저 도착하는 극단적 경합 등,
    /// <see cref="FlowDeployer.RedeployAsync"/>와 동일한 방어) 홀더를 건드리지 않고 조용히 반환합니다
    /// — 있던 값을 잘못 지우지 않습니다. <see cref="FlowEngine"/>은 이 메서드 어디에서도 참조하지
    /// 않습니다(위 클래스 문서의 "가볍게" 요구 그대로).
    /// </summary>
    public static async Task ReloadStructureOnlyAsync(string baseDirectory, CurrentDeviceTreeHolder holder, CancellationToken ct = default)
    {
        var path = Path.Combine(baseDirectory, "device.json");
        if (!File.Exists(path))
        {
            return;
        }

        DeviceTreeDto? tree;
        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            tree = JsonSerializer.Deserialize<DeviceTreeDto>(json);
        }
        catch (JsonException)
        {
            return;
        }

        if (tree is null)
        {
            return;
        }

        holder.DeviceTree = tree;
        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] 구조 설정(device.json) 재로드 완료 — 장비 {tree.Devices.Count}개(FlowEngine은 재배포하지 않음).");
    }
}
