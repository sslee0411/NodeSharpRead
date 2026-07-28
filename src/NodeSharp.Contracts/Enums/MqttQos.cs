namespace NodeSharp.Contracts.Enums;

/// <summary>
/// MQTT 프로토콜의 QoS(Quality of Service, 전달 보증 수준) 3단계입니다. 값은 MQTT 스펙의
/// QoS 코드(0/1/2)와 정확히 일치시켜, <c>(byte)</c> 캐스팅 결과를 PUBLISH 패킷의 QoS 필드에
/// 그대로 실을 수 있게 했습니다. <c>MqttInNode</c>/<c>MqttOutNode</c>가 Topic 와일드카드·
/// Retain·LWT 같은 다른 MQTT 시맨틱과 함께 이 값을 사용합니다.
/// 설계 근거: 02번 문서 11번 탭.
/// </summary>
/// <example>
/// <code>
/// // MqttOutNode에서 발행 — 대부분의 산업 현장 기본값(중복 수신 가능하지만 전달은 보장)
/// var qos = MqttQos.AtLeastOnce;
/// await mqttClient.PublishAsync(topic, payload, qosLevel: (byte)qos, retain: false);
///
/// // MqttInNode에서 구독 — 비상정지 같은 중요 신호는 정확히 1회 전달로 구독
/// await mqttClient.SubscribeAsync("plc/1/estop", qosLevel: (byte)MqttQos.ExactlyOnce);
///
/// // 센서 텔레메트리처럼 유실돼도 다음 주기 값으로 충분한 경우는 AtMostOnce로 오버헤드를 줄임
/// await mqttClient.PublishAsync("sensor/temp", payload, qosLevel: (byte)MqttQos.AtMostOnce, retain: false);
/// </code>
/// </example>
public enum MqttQos
{
    /// <summary>최대 1회 전달(Fire-and-forget). 전달 보증이 없어 네트워크 상황에 따라 메시지가 사라질 수 있습니다.</summary>
    AtMostOnce = 0,

    /// <summary>최소 1회 전달. 전달은 보장되지만 중복 수신이 발생할 수 있습니다. 대부분의 산업 현장에서 기본값으로 사용합니다.</summary>
    AtLeastOnce = 1,

    /// <summary>정확히 1회 전달. 중복·손실이 모두 없지만 가장 무거운 프로토콜 핸드셰이크가 필요합니다.</summary>
    ExactlyOnce = 2
}
