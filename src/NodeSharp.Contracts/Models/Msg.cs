using System.Dynamic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace NodeSharp.Contracts.Models;

/// <summary>
/// Class명 : 메시지
/// 역활 및 기능 : Node-RED의 msg 객체에 대응하는 동적 필드 확장 가능한 메시지 컨테이너
///
/// Node-RED의 <c>msg</c> 객체에 대응하는 NodeSharpRead의 메시지 컨테이너입니다. 노드가
/// 런타임에 임의의 필드를 자유롭게 추가/삭제할 수 있는 동적 객체이며, 내부적으로
/// <see cref="ExpandoObject"/>를 데이터 저장소로 사용합니다. 고정 스키마 클래스(예:
/// <c>Dictionary&lt;string, object&gt; Extra</c> 방식)로는 <c>msg.foo</c> 같은 동적 접근이
/// 안 되고 항상 <c>Extra["foo"]</c>로 우회해야 하는 문제가 있어 이 방식을 택했습니다.
/// 설계 근거: 02번 문서 2번 탭 카드 2·3.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>_msgid</c>는 생성자에서 항상 보장되고, <see cref="Payload"/>/<see cref="Topic"/>은
/// 강타입 프로퍼티로 노출됩니다 — 동적 객체라도 핵심 필드는 오타·타입 실수를 컴파일 타임에 방지합니다.</item>
/// <item><see cref="Clone"/>으로 깊은 복제 후 다음 노드에 전달합니다 — <c>FlowEngine.RouteAsync</c>가
/// N개 와이어로 분기(Fan-out)할 때 한 분기가 <c>Payload</c>를 바꿔도 다른 분기에 영향을 주지 않습니다.</item>
/// <item>JSON 직렬화는 System.Text.Json이 아니라 Newtonsoft.Json을 사용합니다 — <see cref="ExpandoObjectConverter"/>가
/// <see cref="ExpandoObject"/>↔JSON 왕복 변환을 기본 지원해 커스텀 컨버터가 필요 없습니다.</item>
/// <item><see cref="Clone"/>의 한계: 값이 <see cref="ICloneable"/>일 때만 실제로 깊은 복제를 하고,
/// 그 외 참조 타입(커스텀 클래스, 배열 등)은 참조를 그대로 복사(얕은 복사)합니다. <c>string</c>/<c>int</c> 같은
/// 값 타입·불변 타입은 이 한계와 무관하게 항상 안전합니다.</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // 1) 새 메시지 생성(Inject 노드 등) — Payload/Topic은 강타입 프로퍼티
/// var msg = new Msg { Payload = 42, Topic = "sensor/temp" };
///
/// // 2) 동적 필드 추가(Function 노드 등) — dynamic 캐스팅 후 임의 이름으로 접근
/// dynamic dyn = msg;
/// dyn.customField = "hello";
/// dyn.unit = "C";
///
/// // 3) 타입 안전 접근자 — 필드 존재/타입 불일치를 구분해야 할 때
/// if (msg.TryGet&lt;int&gt;("payload", out var temp))
/// {
///     Console.WriteLine($"온도: {temp}");
/// }
/// int fallback = msg.Get&lt;int&gt;("missingField"); // 필드 없음 → default(int) = 0
///
/// // 4) Fan-out 분기 시 데이터 격리를 위한 깊은 복제 — Id는 두 분기 모두 원본과 동일하게 유지
/// var branch1 = msg.Clone();
/// var branch2 = msg.Clone();
/// branch1.Payload = 100;   // branch2.Payload는 영향받지 않음(여전히 42)
/// bool sameOrigin = branch1.Id == branch2.Id &amp;&amp; branch1.Id == msg.Id; // true
///
/// // 5) Debug 노드 출력 등에서의 JSON 왕복(중첩 객체/배열도 그대로 복원)
/// string json = msg.ToJson();
/// Msg restored = Msg.FromJson(json);
///
/// // 6) 순환 구조 안전장치(RT-05) — FlowEngine.RouteAsync가 매 홉마다 1씩 증가시킴, 직접 조작할 일은 거의 없음
/// var looped = new Msg();
/// looped.HopCount = 999;   // FlowEngine.MaxHopCount(기본 1000) 직전까지 도달한 상태를 시뮬레이션
/// </code>
/// </example>
public sealed class Msg : DynamicObject
{
    /// <summary>실제 데이터가 저장되는 내부 컨테이너입니다. 인덱서(<c>_data["key"]</c>)로 다루면 타입 정보를 검사하는 과정(리플렉션) 없이 빠르게 필드를 읽고 씁니다.</summary>
    private readonly IDictionary<string, object?> _data = new ExpandoObject();

    /// <summary>새 <see cref="Msg"/>를 생성하고, Node-RED의 <c>msg._msgid</c>와 동일한 역할을 하는 고유 식별자를 자동으로 부여합니다.</summary>
    public Msg() => _data["_msgid"] = Guid.NewGuid().ToString("N");

    /// <summary>이 메시지의 고유 식별자(Node-RED의 <c>msg._msgid</c>에 대응). 생성 시 자동 부여되며 이후 변경되지 않습니다.</summary>
    public string Id => (string)_data["_msgid"]!;

    /// <summary>메시지의 본문 데이터(Node-RED의 <c>msg.payload</c>에 대응). 실제 타입은 노드마다 다릅니다(문자열, 숫자, byte[], 커스텀 객체 등).</summary>
    public object? Payload
    {
        get => _data.TryGetValue("payload", out var v) ? v : null;
        set => _data["payload"] = value;
    }

    /// <summary>메시지의 주제/경로(Node-RED의 <c>msg.topic</c>에 대응). MQTT 노드의 Topic, HTTP 노드의 경로 등에 흔히 쓰입니다.</summary>
    public string? Topic
    {
        get => _data.TryGetValue("topic", out var v) ? v as string : null;
        set => _data["topic"] = value;
    }

    /// <summary>
    /// (★ RT-05) 이 메시지가 <c>FlowEngine.RouteAsync</c>를 거쳐 지금까지 몇 번 전달됐는지(02번 문서
    /// 5번 탭 카드2 <c>_hopCount</c> 동적 필드). 순환 구조(A→B→A)에서 무한 루프에 빠지지 않도록 매
    /// <c>RouteAsync</c> 호출마다 1씩 증가하고, <c>FlowEngine.MaxHopCount</c>를 넘으면 라우팅이 중단됩니다.
    /// 값이 없으면(아직 한 번도 라우팅되지 않은 새 메시지) 0을 반환합니다.
    /// </summary>
    public int HopCount
    {
        get => _data.TryGetValue("_hopCount", out var v) && v is int h ? h : 0;
        set => _data["_hopCount"] = value;
    }

    /// <summary>
    /// 동적 필드 읽기 — <c>dynamic</c>으로 캐스팅한 뒤 <c>msg.customField</c> 형태로 접근할 때 C#
    /// 런타임이 호출합니다. 존재하지 않는 필드를 읽으면 <c>null</c>을 반환하고 예외를 던지지
    /// 않습니다(Node-RED의 <c>msg.foo</c>가 <c>undefined</c>를 반환하는 것과 동일한 관용).
    /// </summary>
    /// <remarks>
    /// 반드시 항상 <c>true</c>를 반환해야 합니다. C#의 <c>dynamic</c> 규칙상 <c>false</c>를 반환하면
    /// "이 필드를 찾지 못했다"는 뜻이 되어, 실행 중 <c>RuntimeBinderException</c> 예외가 발생합니다.
    /// </remarks>
    public override bool TryGetMember(GetMemberBinder binder, out object? result)
    {
        _data.TryGetValue(binder.Name, out result);
        return true;
    }

    /// <summary>동적 필드 쓰기 — <c>dynamic</c>으로 캐스팅한 뒤 <c>msg.customField = 1;</c> 형태로 대입할 때 호출됩니다. Function 노드 등에서 사용합니다.</summary>
    public override bool TrySetMember(SetMemberBinder binder, object? value)
    {
        _data[binder.Name] = value;
        return true;
    }

    /// <summary>
    /// 타입 안전 접근자 — <paramref name="key"/> 필드가 존재하고 실제로 <typeparamref name="T"/>
    /// 타입이면 그 값을, 그렇지 않으면(필드 없음 또는 타입 불일치) <c>default(T)</c>를 반환합니다.
    /// 값이 없는 것과 타입이 다른 것을 구분해야 한다면 <see cref="TryGet{T}"/>를 사용하세요.
    /// </summary>
    public T? Get<T>(string key) =>
        _data.TryGetValue(key, out var v) && v is T t ? t : default;

    /// <summary>
    /// 타입 안전 접근자(TryGet 패턴) — 필드가 존재하고 <typeparamref name="T"/> 타입이면
    /// <paramref name="value"/>에 값을 담아 <c>true</c>를, 아니면 <c>false</c>를 반환합니다.
    /// </summary>
    public bool TryGet<T>(string key, out T? value)
    {
        if (_data.TryGetValue(key, out var v) && v is T t)
        {
            value = t;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// 깊은 복제 — N개 와이어로 분기(Fan-out)할 때 각 분기마다 별도의 <see cref="Msg"/> 인스턴스를
    /// 만들어, 한 분기에서 필드를 변경해도 다른 분기나 원본에 영향을 주지 않도록 합니다.
    /// <see cref="Id"/>는 원본과 동일하게 유지됩니다 — 같은 입력 메시지에서 갈라져 나온 분기들이
    /// 모두 같은 <see cref="Id"/>를 공유해, Complete 노드·추적(Trace)에서 서로 연관된 메시지임을
    /// 알 수 있게 합니다(Node-RED의 fan-out 동작과 동일).
    /// </summary>
    public Msg Clone()
    {
        var clone = new Msg();
        foreach (var kv in _data)
        {
            clone._data[kv.Key] = kv.Value is ICloneable c ? c.Clone() : kv.Value;
        }

        return clone;
    }

    /// <summary>이 메시지를 JSON 문자열로 직렬화합니다. Debug 노드 출력, 로그 등에서 그대로 사용됩니다.</summary>
    public string ToJson() => JsonConvert.SerializeObject(_data);

    /// <summary>
    /// JSON 문자열로부터 <see cref="Msg"/>를 복원합니다. <see cref="ExpandoObjectConverter"/>를 사용해
    /// 임의의 중첩 JSON 구조(중첩 객체는 <see cref="ExpandoObject"/>, 배열은 <c>List&lt;object&gt;</c>)를
    /// 그대로 동적 필드로 복원합니다.
    /// </summary>
    /// <remarks>
    /// <see cref="ExpandoObjectConverter"/>는 역직렬화 대상 타입이 정확히 <see cref="ExpandoObject"/>일
    /// 때만 동작합니다. 대상 타입을 <c>IDictionary&lt;string, object?&gt;</c> 등으로 지정하면 컨버터가
    /// 조용히 무시되어 중첩 객체/배열이 Newtonsoft의 기본 처리(<c>JObject</c>/<c>JArray</c>)로 남으니
    /// 주의하세요.
    /// </remarks>
    /// <param name="json"><see cref="ToJson"/>으로 만든(또는 그와 동일한 형식의) JSON 문자열.</param>
    /// <returns>역직렬화된 필드를 모두 포함하는 새 <see cref="Msg"/> 인스턴스.</returns>
    public static Msg FromJson(string json)
    {
        var expando = JsonConvert.DeserializeObject<ExpandoObject>(
            json, new ExpandoObjectConverter())!;
        var dict = (IDictionary<string, object?>)expando;

        var msg = new Msg();
        foreach (var kv in dict)
        {
            msg._data[kv.Key] = kv.Value;
        }

        return msg;
    }
}
