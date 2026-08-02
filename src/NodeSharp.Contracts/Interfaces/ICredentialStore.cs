namespace NodeSharp.Contracts.Interfaces;

/// <summary>
/// Class명 : 자격 증명 저장소 계약
/// 역활 및 기능 : 비밀값을 별도 파일에 암호화해 저장·조회하는 자격 증명 저장소 계약
///
/// 노드 설정(<c>flows.json</c>)에는 절대 평문으로 남지 않아야 하는 비밀값(API 키, MQTT 비밀번호 등)을
/// 별도 파일(<c>credentials.json</c>)에 암호화해 저장·조회하는 계약입니다. <see cref="Models.NodeConfig.CredentialRefId"/>는
/// 이 저장소의 키만 들고 있고 실제 값은 갖지 않습니다.
/// 설계 근거: 02번 문서 9번 탭 "Credential 암호화 저장" 카드.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>1차 구현(<c>DpapiCredentialStore</c>, NodeSharp.Runtime)은 Windows DPAPI(사용자/머신 스코프 키)를
/// 사용해 별도 키 관리 없이 암호화합니다.</item>
/// <item><see cref="Save"/>/<see cref="Load"/>가 가리키는 <c>credentials.json</c>은 <see cref="Models.ProjectBundle"/>의
/// 저장 파일 목록(CT-03c)에서 의도적으로 제외되어 있습니다 — 프로젝트를 통째로 내보낼 때 비밀값이
/// 함께 유출되지 않도록 하기 위함입니다.</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // 1) MQTT Publish 노드 설정 화면에서 브로커 비밀번호 저장
/// credentialStore.Set(nodeId: "mqtt-1", field: "password", plainValue: "s3cr3t");
/// // NodeConfig.CredentialRefId = "mqtt-1" 로 참조만 flows.json에 남김(평문 저장 금지)
///
/// // 2) 노드 시작 시 저장된 비밀값 조회
/// string? password = credentialStore.Get(nodeId: "mqtt-1", field: "password");
///
/// // 3) 애플리케이션 시작/종료 시 credentials.json 왕복
/// credentialStore.Load(path: "credentials.json");
/// credentialStore.Save(path: "credentials.json");
/// </code>
/// </example>
public interface ICredentialStore
{
    /// <summary>지정한 노드의 지정한 필드에 대한 비밀값을 암호화해 저장(또는 갱신)합니다.</summary>
    void Set(string nodeId, string field, string plainValue);

    /// <summary>지정한 노드의 지정한 필드에 저장된 비밀값을 복호화해 반환합니다. 저장된 값이 없으면 <c>null</c>.</summary>
    string? Get(string nodeId, string field);

    /// <summary>현재 저장된 모든 암호화 값을 <paramref name="path"/>(<c>credentials.json</c>)에 저장합니다. 저장 도중 오류가 나도 파일이 절반만 쓰인 채로 남지 않도록 안전하게 처리합니다.</summary>
    void Save(string path);

    /// <summary><paramref name="path"/>(<c>credentials.json</c>)에서 암호화된 값을 불러와 메모리에 복원합니다.</summary>
    void Load(string path);
}
