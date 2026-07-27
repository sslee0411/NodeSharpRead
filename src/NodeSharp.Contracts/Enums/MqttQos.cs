namespace NodeSharp.Contracts.Enums;

/// <summary>
/// MQTT 프로토콜의 QoS(Quality of Service, 전달 보증 수준) 3단계입니다. 값은 MQTT 스펙의
/// QoS 코드(0/1/2)와 정확히 일치시켰습니다.
/// </summary>
/// <remarks>
/// <para>
/// 설계 근거: 02번 설계 문서 11번 탭(lssLib 노드 모듈화) — MQTT 프로토콜 전용 노드
/// (<c>MqttInNode</c>/<c>MqttOutNode</c>)가 범용 <see cref="NetTransportType.Mqtt"/> 전송
/// 위에서 Topic 와일드카드·QoS·Retain·LWT(Last Will and Testament) 같은 MQTT 고유 시맨틱을
/// 처리할 때 이 Enum을 사용합니다(v1.8에서 발견한 공백 — 범용 <c>NetIoNode</c>는 QoS 개념이 없음).
/// </para>
/// <para>
/// 값을 명시적으로 지정한 이유: MQTT 프로토콜 자체가 QoS를 0/1/2 숫자로 정의하고
/// PUBLISH 패킷의 QoS 필드에 그대로 실려 나가므로, <c>(byte)</c> 캐스팅 결과가 프로토콜 값과
/// 항상 일치하도록 값을 고정합니다(향후 MqttInNode/MqttOutNode 구현 시 별도 매핑 테이블 불필요).
/// </para>
/// </remarks>
/// <example>
/// MQTT 발행 노드(NR-07b, 향후 구현)에서 QoS를 지정하는 예:
/// <code>
/// var qos = MqttQos.AtLeastOnce;   // 최소 1회 전달 보장(중복 수신 가능) — 대부분의 산업 현장 기본값
///
/// // MQTT 클라이언트 라이브러리 호출 시 프로토콜 값 그대로 사용
/// await mqttClient.PublishAsync(topic, payload, qosLevel: (byte)qos, retain: false);
/// </code>
/// </example>
public enum MqttQos
{
    /// <summary>최대 1회 전달(전달 보증 없음, Fire-and-forget). 네트워크 상황에 따라 메시지가 아예 사라질 수 있습니다.</summary>
    AtMostOnce = 0,

    /// <summary>최소 1회 전달(전달은 보장되지만 중복 수신 가능). 대부분의 산업 현장에서 기본값으로 사용합니다.</summary>
    AtLeastOnce = 1,

    /// <summary>정확히 1회 전달(중복·손실 모두 없음, 가장 무거운 프로토콜 핸드셰이크가 필요).</summary>
    ExactlyOnce = 2
}
