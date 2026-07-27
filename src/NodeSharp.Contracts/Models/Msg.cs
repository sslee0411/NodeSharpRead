using System.Dynamic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace NodeSharp.Contracts.Models;

/// <summary>
/// Node-RED의 <c>msg</c> 객체에 대응하는 NodeSharpRead의 메시지 컨테이너입니다.
/// 노드가 런타임에 임의의 필드를 자유롭게 추가/삭제할 수 있는 <b>동적 객체</b>이며,
/// 내부적으로 <see cref="ExpandoObject"/>를 데이터 저장소로 사용합니다.
/// </summary>
/// <remarks>
/// <para>
/// 설계 근거: 02번 설계 문서(<c>docs/02_CSharp_구조설계.html</c>) 2번 탭(핵심 클래스 설계)
/// 카드 2·3. 고정 스키마 클래스(예: <c>Dictionary&lt;string, object&gt; Extra</c> 방식)로
/// 흉내내면 노드 개발자가 <c>msg.foo</c> 같은 동적 접근을 못 하고 항상 <c>Extra["foo"]</c>로
/// 우회해야 하는 문제가 있어, <see cref="ExpandoObject"/>를 채택해 Node-RED와 동일한 동적
/// 필드 확장성을 확보했습니다.
/// </para>
/// <para>
/// <b>데이터 안정성 3종 장치</b>(02번 문서 2번 탭 카드 3):
/// </para>
/// <list type="number">
/// <item><description><c>_msgid</c>는 생성자에서 항상 보장, <see cref="Payload"/>/<see cref="Topic"/>은
/// 강타입 프로퍼티로 노출 — 동적 객체라도 핵심 필드는 오타·타입 실수를 컴파일 타임에 방지합니다.</description></item>
/// <item><description><see cref="Clone"/>으로 깊은 복제 후 다음 노드에 전달 —
/// <c>FlowEngine.RouteAsync</c>가 N개 와이어로 분기(1:N)할 때 한 노드가 <c>msg.payload</c>를
/// 변경해도 다른 분기에 영향을 주지 않도록 격리합니다.</description></item>
/// <item><description>JSON 직렬화는 <b>Newtonsoft.Json</b>을 채택(System.Text.Json이 아님) —
/// <see cref="ExpandoObjectConverter"/>를 기본 제공해 <see cref="ExpandoObject"/>↔JSON 왕복
/// 변환에 커스텀 컨버터 작성이 불필요합니다.</description></item>
/// </list>
/// <para>
/// <b>Clone()의 한계(설계상 알려진 제약)</b>: 값이 <see cref="ICloneable"/>을 구현하는 경우에만
/// 실제로 깊은 복제를 수행하고, 그렇지 않은 참조 타입(예: 커스텀 클래스, 배열)은 참조를 그대로
/// 복사합니다(얕은 복사). <c>string</c>·<c>int</c> 등 값 타입/불변 타입은 이 한계와 무관하게
/// 항상 안전합니다. 커스텀 참조 타입을 payload로 쓰는 노드는 그 타입이 <see cref="ICloneable"/>을
/// 구현하도록 해야 완전한 격리가 보장됩니다 — 이는 02번 문서 원본 코드의 설계 그대로이며,
/// 이 Step(CT-02a)에서 임의로 변경하지 않았습니다.
/// </para>
/// </remarks>
/// <example>
/// 노드 구현 코드에서의 일반적인 사용 패턴:
/// <code>
/// // 새 메시지 생성(Inject 노드 등)
/// var msg = new Msg { Payload = 42, Topic = "sensor/temp" };
///
/// // 동적 필드 추가(Function 노드 등) — 컴파일 타임에 정의되지 않은 필드도 자유롭게 사용
/// dynamic dyn = msg;
/// dyn.customField = "hello";
///
/// // 타입 안전 접근자로 값 꺼내기
/// if (msg.TryGet<string>("customField", out var s))
/// {
///     Console.WriteLine(s);   // "hello"
/// }
///
/// // Fan-out 분기 시 데이터 격리를 위한 깊은 복제
/// var branch1 = msg.Clone();
/// var branch2 = msg.Clone();
/// branch1.Payload = 100;   // branch2.Payload는 영향받지 않음(여전히 42)
///
/// // Debug 노드 출력 / flows.json 로그 — JSON 왕복 변환
/// string json = msg.ToJson();
/// Msg restored = Msg.FromJson(json);
/// </code>
/// </example>
public sealed class Msg : DynamicObject
{
    /// <summary>
    /// 실제 데이터가 저장되는 내부 컨테이너입니다. <see cref="ExpandoObject"/>를
    /// <see cref="IDictionary{TKey,TValue}"/>로 캐스팅해 인덱서로 다루면 리플렉션 없이
    /// 빠르게 필드를 읽고 쓸 수 있습니다.
    /// </summary>
    private readonly IDictionary<string, object?> _data = new ExpandoObject();

    /// <summary>
    /// 새 <see cref="Msg"/>를 생성하고, Node-RED의 <c>msg._msgid</c>와 동일한 역할을 하는
    /// 고유 식별자를 자동으로 부여합니다.
    /// </summary>
    public Msg() => _data["_msgid"] = Guid.NewGuid().ToString("N");

    /// <summary>이 메시지의 고유 식별자(Node-RED의 <c>msg._msgid</c>에 대응). 생성 시 자동 부여되며 이후 변경되지 않습니다.</summary>
    public string Id => (string)_data["_msgid"]!;

    /// <summary>
    /// 메시지의 본문 데이터(Node-RED의 <c>msg.payload</c>에 대응). 어떤 타입도 담을 수 있는
    /// <see cref="object"/>이며, 실제 타입은 노드마다 다릅니다(문자열, 숫자, byte[], 커스텀 객체 등).
    /// </summary>
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
    /// 동적 필드 읽기 — <c>dynamic</c>으로 캐스팅한 뒤 <c>msg.customField</c> 형태로 접근할 때
    /// C# 런타임이 내부적으로 호출합니다. 존재하지 않는 필드를 읽으면 <paramref name="result"/>가
    /// <c>null</c>이 되고 <c>true</c>를 반환합니다(존재하지 않는 필드 접근 시 예외를 던지지 않음 —
    /// Node-RED의 <c>msg.foo</c>가 <c>undefined</c>를 반환하는 것과 동일한 관용).
    /// </summary>
    /// <remarks>
    /// 이 메서드는 반드시 <b>항상 <c>true</c></b>를 반환해야 합니다 — C#의 동적 바인딩 규약상
    /// <c>TryGetMember</c>가 <c>false</c>를 반환하면 "이 멤버를 찾지 못했다"는 뜻이 되어 런타임이
    /// <c>RuntimeBinderException</c>을 던집니다. <c>_data.TryGetValue(...)</c>의 결과를 그대로
    /// 반환하면(필드가 없을 때 <c>false</c>) 바로 이 예외가 발생하는 실제 버그가 있었습니다
    /// (CT-02a 로컬 단위 테스트 실행으로 발견, README Ver History v1.47 / 02번 문서 v1.39 참고).
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
    /// 타입 안전 접근자(TryGet 패턴) — <paramref name="key"/> 필드가 존재하고 실제로
    /// <typeparamref name="T"/> 타입이면 <paramref name="value"/>에 값을 담아 <c>true</c>를,
    /// 그렇지 않으면 <paramref name="value"/>를 <c>default(T)</c>로 두고 <c>false</c>를 반환합니다.
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
    /// <see cref="Id"/>(<c>_msgid</c>)는 <b>원본과 동일하게 유지</b>됩니다 — 생성자가 임시로 새
    /// 값을 부여하지만 아래 루프가 원본의 <c>_msgid</c>로 덮어써, 같은 입력 메시지에서 갈라져 나온
    /// 분기들이 모두 같은 <see cref="Id"/>를 공유하게 됩니다(Node-RED에서 한 번의 fan-out으로 갈라진
    /// 메시지들이 같은 <c>_msgid</c>를 유지해 Complete 노드·추적(Trace)에서 서로 연관된 메시지임을
    /// 알 수 있는 것과 동일한 동작 — 02번 문서 2번 탭 카드 2 원본 코드를 그대로 따름).
    /// 값이 <see cref="ICloneable"/>이면 그 값도 복제하고, 그렇지 않으면 참조를 그대로 복사합니다
    /// (클래스 상단 <b>Remarks</b>의 "Clone()의 한계" 참고).
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

    /// <summary>
    /// 이 메시지를 JSON 문자열로 직렬화합니다. Debug 노드의 출력, flows.json 관련 로그 등에서
    /// 그대로 사용됩니다(Newtonsoft.Json이 <see cref="ExpandoObject"/>를 네이티브로 지원).
    /// </summary>
    public string ToJson() => JsonConvert.SerializeObject(_data);

    /// <summary>
    /// JSON 문자열로부터 <see cref="Msg"/>를 복원합니다. <see cref="ExpandoObjectConverter"/>를
    /// 사용해 JSON 객체를 <see cref="ExpandoObject"/> 트리로 역직렬화하므로, 커스텀 컨버터 없이도
    /// 임의의 중첩 JSON 구조(중첩 객체는 <see cref="ExpandoObject"/>, 배열은
    /// <see cref="List{T}">List&lt;object&gt;</see>)를 그대로 동적 필드로 복원할 수 있습니다.
    /// </summary>
    /// <remarks>
    /// <see cref="ExpandoObjectConverter.CanConvert"/>는 대상 타입이 <b>정확히</b>
    /// <see cref="ExpandoObject"/>일 때만 <c>true</c>를 반환합니다. 이전에는 대상 타입을
    /// <c>IDictionary&lt;string, object?&gt;</c>로 지정해서 컨버터가 전혀 동작하지 않고,
    /// 중첩 객체/배열이 Newtonsoft의 기본 처리인 <c>JObject</c>/<c>JArray</c>로 남는 실제 버그가
    /// 있었습니다(중첩 객체는 <c>JObject</c>도 동적 속성 접근을 지원해 우연히 동작하는 것처럼
    /// 보였지만, 엄밀한 타입을 기대하는 코드에서는 실패). 대상 타입을 <see cref="ExpandoObject"/>로
    /// 직접 지정해야 컨버터가 실제로 재귀 변환을 수행합니다(CT-02a 로컬 단위 테스트 실행으로
    /// 발견, README Ver History v1.47 / 02번 문서 v1.39 참고).
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
