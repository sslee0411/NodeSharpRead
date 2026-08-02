using NodeSharp.Contracts.Models;

namespace NodeSharp.Contracts.Interfaces;

/// <summary>
/// Class명 : 프로토콜 드라이버 계약
/// 역활 및 기능 : raw Transport 위에서 PLC 프로토콜 규약(주소 체계·함수 코드)을 처리하는 드라이버 계약
///
/// raw Transport(<c>NetTransportType</c>, 11번 탭 카드1) 위에서 PLC 프로토콜 규약(주소 체계·함수
/// 코드·오류 응답)을 처리하는 드라이버 계약입니다. <see cref="IFlowNode"/>와는 별개 축입니다 —
/// 노드는 "Flow 그래프의 실행 단위", 드라이버는 "PLC와 바이트를 주고받는 방법"으로 관심사가 다릅니다.
/// 설계 근거: 02번 문서 11번 탭 카드 8(v1.12 신설, v1.33 확정 — 8번 탭 PlcNode 예시의 "ModbusTcp"가
/// NetTransportType에 없어 구현 불가능했던 공백을 메우기 위해 도입).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>1차 구현체는 <c>ModbusDriver</c>(<c>PD-01a/b</c>)이며, <see cref="ReadAsync"/>가 반환하는
/// 원시 바이트는 8번 탭 <c>DeviceMapNode</c>가 <c>TagNode.Offset</c>/<c>BufType</c> 기준으로
/// 재해석합니다.</item>
/// <item><see cref="WriteAsync"/>는 <c>ED-D06a</c>의 PLC Write 안전장치(범위 검사 등, <c>PlcTagWriteNode</c>
/// 참고)가 이 메서드를 감싸 호출합니다 — 드라이버 자체는 안전장치를 갖지 않습니다.</item>
/// <item>(★ v1.71 정정) <see cref="Type"/>은 애초 고정 <c>enum ProtocolDriverType</c>이었으나, LS산전
/// XGT·미쯔비시 A/QnA·CIMON HD 등 새 PLC 프로토콜을 Contracts 재컴파일 없이 추가할 수 있도록 <c>string</c>
/// 식별자로 전환했습니다. 잘 알려진 값은 <c>Enums.ProtocolDriverType</c> 상수를 참조하고, 새 프로토콜은
/// <c>Registry.ProtocolDriverRegistry.TryRegister</c>로 런타임에 등록합니다(<c>PluginLoadContext</c>/
/// <c>PluginLoader</c>·<c>NodeTypeRegistry</c>, CT-06a/b와 동일한 플러그인 패턴).</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// IProtocolDriver driver = new ModbusDriver(IsRtu: false);
/// await driver.ConnectAsync(new PlcConnectionConfig(Host: "192.168.1.10", Port: 502), ct);
///
/// // DeviceMapNode.StartAddress/LengthBytes(8번 탭)와 직결
/// byte[] raw = await driver.ReadAsync(startAddress: 40001, lengthBytes: 4, ct);
///
/// // ED-D06a 안전장치가 범위 검사 후 이 메서드를 호출
/// await driver.WriteAsync(address: 40001, data: raw, ct);
/// </code>
/// </example>
public interface IProtocolDriver
{
    /// <summary>
    /// 이 드라이버가 구현하는 프로토콜 식별 문자열(예: <c>Enums.ProtocolDriverType.ModbusTcp</c> 또는
    /// 플러그인이 새로 등록한 임의 문자열). ★ v1.71 정정 이전에는 <c>ProtocolDriverType</c> Enum이었음.
    /// </summary>
    string Type { get; }

    /// <summary>PLC와 연결을 수립합니다. TCP/RTU 등 실제 전송 방식은 구현체가 <paramref name="config"/>를 보고 결정합니다.</summary>
    Task ConnectAsync(PlcConnectionConfig config, CancellationToken ct);

    /// <summary>지정한 시작 주소부터 원시 바이트를 읽습니다. <c>DeviceMapNode.StartAddress</c>/<c>LengthBytes</c>(8번 탭)와 직결됩니다.</summary>
    Task<byte[]> ReadAsync(int startAddress, int lengthBytes, CancellationToken ct);

    /// <summary>지정한 주소에 원시 바이트를 씁니다. 범위 검사 등 안전장치는 호출 측(<c>ED-D06a</c>)의 책임입니다.</summary>
    Task WriteAsync(int address, byte[] data, CancellationToken ct);
}
