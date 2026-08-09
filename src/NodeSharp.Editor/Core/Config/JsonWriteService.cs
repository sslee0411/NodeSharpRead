using System.Text.Json;
using System.IO;

namespace NodeSharp.Editor.Core.Config;

/// <summary>
/// Class명 : JSON 원자적 쓰기 서비스
/// 역활 및 기능 : 설정 파일을 깨지지 않게 저장하고(원자적 저장) 변경을 Runner에 알리는(.signal) 공용 도우미
///
/// (EC-04) flows.json 저장 중 프로세스가 강제 종료돼도 기존 파일이 손상되지 않아야 한다는 완료
/// 기준을 만족하기 위한 범용 헬퍼입니다(02번 문서 "공통 규칙 ④" — .tmp에 먼저 전부 쓰고, 다 쓴
/// 뒤에야 원본과 한 번에 바꿔치기). device.json/sequences.json/credentials.json도 나중에 같은
/// 패턴을 그대로 재사용할 수 있도록 <typeparamref name="T"/> 제네릭으로 만들었습니다(02번 문서
/// 설계도의 <c>NodeSharp.Editor\Core\Config\JsonWriteService</c> 위치 그대로).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>원자적 저장 원리</b>: 실제 데이터를 <c>{파일}.tmp</c>에 전부 쓴 뒤(쓰는 도중 크래시가
/// 나도 원본 <c>{파일}</c>은 그대로 남아있음), 파일이 이미 있으면 <see cref="File.Replace(string, string, string?)"/>
/// 로 "임시 파일 → 원본, 원본 → .bak" 스왑을 한 번에 수행합니다(OS 수준 원자적 연산). 파일이 아직
/// 없으면(최초 저장) 바꿔치기할 원본이 없으므로 <see cref="File.Move(string, string, bool)"/>로 그냥
/// 옮깁니다.</item>
/// <item><b>.signal 파일</b>: 저장이 끝난 뒤 <c>{파일}.signal</c>에 저장 시각(UTC, ISO-8601)을
/// 씁니다. Runner 쪽의 <c>FileSystemWatcher</c>(LK-01, 아직 미착수)가 이 파일의 변경을 감지해
/// 자동 재배포를 트리거할 예정입니다 — 이 Step(EC-04)은 신호를 "보내는" 쪽만 구현하고, "받는" 쪽은
/// LK-01 범위입니다.</item>
/// <item><b>(v2.53 버그 수정) 스키마가 안 맞는 파일도 손상으로 취급</b>: <see cref="ReadAsync{T}"/>는
/// 파일은 있지만 지금 <c>T</c>의 모양과 JSON 내용이 안 맞을 때(예: EC-05 전에 단일 객체로 저장해둔
/// 옛 flows.json을 EC-05 이후 <c>List&lt;FlowDefinition&gt;</c>로 읽으려는 경우) 예외를 밖으로 던지지
/// 않고 "저장된 값 없음"과 동일하게 <c>default</c>를 반환합니다 — <c>StartupSequencer</c>(RN-01a)가
/// flows.json이 손상됐을 때 그 단계만 실패로 기록하고 나머지는 계속 진행하는 것과 같은 "격리"
/// 원칙입니다. 이 보호가 없으면 <c>FlowCanvasView.OnLoaded</c>처럼 <c>async void</c> 이벤트
/// 핸들러에서 처리되지 않은 예외가 그대로 앱 전체를 크래시시킵니다(실제 발견된 버그).</item>
/// </list>
/// </remarks>
public static class JsonWriteService
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// <paramref name="data"/>를 JSON으로 직렬화해 <paramref name="filePath"/>에 원자적으로 저장합니다.
    /// 대상 폴더가 없으면 먼저 만듭니다.
    /// </summary>
    public static async Task WriteAtomicAsync<T>(string filePath, T data, CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = filePath + ".tmp";
        var json = JsonSerializer.Serialize(data, WriteOptions);
        await File.WriteAllTextAsync(tempPath, json, ct);

        if (File.Exists(filePath))
        {
            // 원본이 이미 있으면 .bak으로 백업하며 한 번에 바꿔치기(원자적 스왑).
            File.Replace(tempPath, filePath, filePath + ".bak");
        }
        else
        {
            // 최초 저장 — 바꿔치기할 원본이 없으므로 그냥 이동.
            File.Move(tempPath, filePath, overwrite: true);
        }
    }

    /// <summary>
    /// <paramref name="filePath"/> + <c>.signal</c> 파일에 현재 UTC 시각을 씁니다(Runner가
    /// FileSystemWatcher로 감지할 신호 파일 — 감지·재배포 로직 자체는 LK-01 범위).
    /// </summary>
    public static async Task WriteSignalAsync(string filePath, CancellationToken ct = default)
    {
        var signalPath = filePath + ".signal";
        await File.WriteAllTextAsync(signalPath, DateTime.UtcNow.ToString("O"), ct);
    }

    /// <summary>
    /// <paramref name="filePath"/>를 읽어 <typeparamref name="T"/>로 역직렬화합니다. 파일이 없으면
    /// (아직 한 번도 저장한 적 없는 최초 실행) 예외 없이 <c>default</c>(참조 타입은 <c>null</c>)를
    /// 반환합니다. (v2.53 버그 수정) 파일은 있지만 JSON 내용이 <typeparamref name="T"/>의 모양과
    /// 맞지 않아 역직렬화에 실패해도(예: 스키마가 바뀌기 전 버전으로 저장된 옛 파일) 예외를 밖으로
    /// 던지지 않고 <c>default</c>를 반환합니다 — 클래스 remarks 참고.
    /// </summary>
    public static async Task<T?> ReadAsync<T>(string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
        {
            return default;
        }

        var json = await File.ReadAllTextAsync(filePath, ct);
        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException)
        {
            // 스키마 불일치·손상된 JSON — "저장된 값 없음"과 동일하게 취급(위 remarks의 격리 원칙).
            return default;
        }
    }
}
