using System.Collections.ObjectModel;
using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Models;

namespace NodeSharp.Editor.Structure;

/// <summary>
/// Class명 : 구조 설정 트리 노드(추상 베이스)
/// 역활 및 기능 : 장비→PLC→디바이스맵→태그→스케일→알람 6단계 고정 트리 공통 베이스
///
/// (ED-D01) 02번 설계문서 8번 탭 카드3을 그대로 포팅했습니다. 6단계를 각각 별도 구체 클래스로 두는
/// 이유: 단계마다 "허용되는 자식 타입"이 다르기 때문에(장비 밑에는 PLC만, 태그 밑에는 스케일/알람만)
/// 이를 컴파일 타임에 강제해 잘못된 트리 구성을 실수로 만들 수 없게 막는 것이 목적입니다.
/// </summary>
/// <remarks>
/// <b>Editor 전용</b>: 이 클래스(<see cref="ObservableCollection{T}"/> 기반)는 WPF Editor에서만 쓰입니다.
/// 헤드리스 Runner는 이 클래스를 직접 참조하지 않고 <c>IStructureService</c>(Runner용 순수 데이터
/// 인터페이스, ED-D03 이후)를 통해 <c>DeviceTreeDto</c>로 변환된 값만 사용합니다 — Runner가 WPF에
/// 의존하지 않게 하기 위함입니다(02번 문서 8번 탭 카드6).
/// </remarks>
public abstract class StructureTreeNode
{
    /// <summary>이 노드의 고유 Id — 저장/참조(TagRef 등)의 기준이 됩니다.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>트리에 표시되는 이름.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>선택 사항 — 사용자가 남기는 설명.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>단계별 아이콘(📁🏭🔧🗺🏷📐🔔).</summary>
    public abstract string IconGlyph { get; }

    /// <summary>이 노드 타입의 속성 편집 폼 정의 — 9번 탭 <see cref="PropertyField"/> 재사용(ED-D02a/b에서 실제 편집 UI가 이 값을 렌더링).</summary>
    public abstract IReadOnlyList<PropertyField> PropertySchema { get; }

    /// <summary>이 노드 아래 허용되는 자식 타입 목록 — 트리 우클릭 "추가" 메뉴가 이 값을 보고 항목을 결정합니다.</summary>
    public abstract IReadOnlyList<Type> AllowedChildTypes { get; }

    /// <summary>이 노드의 자식 목록.</summary>
    public ObservableCollection<StructureTreeNode> Children { get; } = new();
}

/// <summary>장비(1단계) — 6단계 트리의 루트. 자식으로 <see cref="PlcNode"/>만 허용합니다.</summary>
public sealed class DeviceNode : StructureTreeNode
{
    // (ED-D02a 발견·수정) 02번 설계문서 8번 탭 카드3의 DeviceNode 예시는 PropertySchema에
    // "model"/"location" 필드만 정의하고 그 값을 담을 실제 C# 프로퍼티는 정의하지 않은 상태였다
    // — PlcNode(CommType/Host/Port)·DeviceMapNode(StartAddress/LengthBytes) 등 나머지 5개
    // 클래스는 모두 PropertySchema.Key와 이름이 같은 프로퍼티를 갖고 있는 것과 대조적이다.
    // StructureNodePropertyDialog(ED-D02a/b)가 PropertyField.Key로 리플렉션 프로퍼티를 찾아 값을
    // 읽고 쓰므로, 이 프로퍼티가 없으면 "모델명"/"설치 위치" 필드는 편집해도 저장될 곳이 없어
    // 조용히 무시된다 — 다른 5개 클래스와 동일한 관례로 프로퍼티를 추가해 바로잡는다.
    public string Model { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public override string IconGlyph => "🏭";
    public override IReadOnlyList<Type> AllowedChildTypes => new[] { typeof(PlcNode) };
    public override IReadOnlyList<PropertyField> PropertySchema => new[]
    {
        new PropertyField("model", "모델명", PropertyFieldType.Text,
            HelpText: "설비 제조사가 제공하는 모델명입니다. 유지보수 시 부품 검색에 사용됩니다.",
            Example: "예: XT-3000"),
        new PropertyField("location", "설치 위치", PropertyFieldType.Text,
            HelpText: "공장 내 물리적 위치입니다.", Example: "예: A동 1층 라인1"),
    };
}

/// <summary>PLC(2단계) — 통신 대상 1개. 자식으로 <see cref="DeviceMapNode"/>만 허용합니다.</summary>
public sealed class PlcNode : StructureTreeNode
{
    /// <summary>통신 프로토콜 — <see cref="ProtocolDriverType"/> 상수와 동일한 값을 사용(11번 탭 카드8 IProtocolDriver, NetTransportType 아님).</summary>
    public string CommType { get; set; } = ProtocolDriverType.ModbusTcp;

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public override string IconGlyph => "🔧";
    public override IReadOnlyList<Type> AllowedChildTypes => new[] { typeof(DeviceMapNode) };
    public override IReadOnlyList<PropertyField> PropertySchema => new[]
    {
        new PropertyField("commType", "프로토콜", PropertyFieldType.ComboBox,
            // Options: null = "정적 목록 없음, 호출 측(Editor)이 ProtocolDriverRegistry.RegisteredDrivers.Keys로 동적으로 채움" 관례.
            Options: null,
            HelpText: "이 PLC와 통신할 프로토콜입니다. 11번 탭 카드 8의 IProtocolDriver 구현체가 이 값을 보고 선택됩니다. 목록은 로드된 프로토콜 드라이버 플러그인에 따라 달라집니다.",
            Example: "예: Modbus.Tcp"),
        new PropertyField("host", "IP/주소", PropertyFieldType.Text, Example: "예: 192.168.1.10"),
        new PropertyField("port", "포트", PropertyFieldType.Number, DefaultValue: "502",
            HelpText: "Modbus TCP는 보통 502번을 사용합니다."),
    };
}

/// <summary>디바이스맵(3단계) — PLC 레지스터의 연속된 블록 1개. 자식으로 <see cref="TagNode"/>만 허용합니다.</summary>
public sealed class DeviceMapNode : StructureTreeNode
{
    public int StartAddress { get; set; }
    public int LengthBytes { get; set; }
    public override string IconGlyph => "🗺";
    public override IReadOnlyList<Type> AllowedChildTypes => new[] { typeof(TagNode) };
    public override IReadOnlyList<PropertyField> PropertySchema => new[]
    {
        new PropertyField("startAddress", "시작 주소", PropertyFieldType.Number, Required: true,
            HelpText: "PLC 레지스터에서 이 블록이 시작하는 주소입니다. 이 블록 안의 태그들은 이 주소 " +
                      "기준 상대 오프셋(offset)으로 위치가 계산됩니다.", Example: "예: 40001"),
        new PropertyField("lengthBytes", "블록 길이(byte)", PropertyFieldType.Number,
            HelpText: "이 블록을 한 번에 몇 바이트 읽을지입니다. 태그 offset들의 최댓값보다 커야 합니다.",
            Example: "예: 16 (태그 4개, FloatLE 4바이트씩)"),
    };
}

/// <summary>태그(4단계) — 디바이스맵 안의 값 1개. 자식으로 <see cref="ScaleNode"/>/<see cref="AlarmNode"/>를 허용합니다(둘 다 선택 사항).</summary>
public sealed class TagNode : StructureTreeNode
{
    public int Offset { get; set; }

    /// <summary>PLC가 이 값을 저장하는 형식 — 11번 탭 lssLib.Serialization.BufType 재사용.</summary>
    public string BufType { get; set; } = "FloatLE";

    public string Unit { get; set; } = string.Empty;
    public override string IconGlyph => "🏷";
    public override IReadOnlyList<Type> AllowedChildTypes => new[] { typeof(ScaleNode), typeof(AlarmNode) };
    public override IReadOnlyList<PropertyField> PropertySchema => new[]
    {
        new PropertyField("offset", "오프셋(byte)", PropertyFieldType.Number, Required: true,
            HelpText: "디바이스맵 시작 주소로부터 몇 바이트 떨어진 곳에 이 값이 있는지입니다.",
            Example: "예: 2 (블록 시작에서 2바이트 뒤)"),
        new PropertyField("bufType", "데이터 타입", PropertyFieldType.ComboBox,
            Options: new[] { "UInt16", "Int16", "UInt32", "Int32", "FloatLE", "FloatBE", "Bit" },
            HelpText: "PLC가 이 값을 몇 바이트로, 어떤 형식(정수/실수/비트)으로 저장하는지입니다. " +
                      "PLC 매뉴얼의 레지스터 맵을 참고하세요."),
        new PropertyField("unit", "단위", PropertyFieldType.Text, Example: "예: bar, °C, m³/h"),
    };
}

/// <summary>스케일(5단계) — 태그의 Raw 값을 공학단위로 변환하는 선형 변환 규칙(잎 노드, 선택 사항).</summary>
public sealed class ScaleNode : StructureTreeNode
{
    public double RawMin { get; set; }
    public double RawMax { get; set; }
    public double EngMin { get; set; }
    public double EngMax { get; set; }
    public override string IconGlyph => "📐";
    public override IReadOnlyList<Type> AllowedChildTypes => Array.Empty<Type>();
    public override IReadOnlyList<PropertyField> PropertySchema => new[]
    {
        new PropertyField("rawMin", "Raw 최솟값", PropertyFieldType.Number,
            HelpText: "PLC에서 그대로 읽히는 값(스케일 변환 전)의 최소치입니다.", Example: "예: 0"),
        new PropertyField("rawMax", "Raw 최댓값", PropertyFieldType.Number, Example: "예: 4095 (12bit ADC 최댓값)"),
        new PropertyField("engMin", "공학단위 최솟값", PropertyFieldType.Number,
            HelpText: "Raw 최솟값이 변환되어야 할 실제 물리량입니다.", Example: "예: 0"),
        new PropertyField("engMax", "공학단위 최댓값", PropertyFieldType.Number, Example: "예: 10 (bar)"),
    };
}

/// <summary>알람(5단계, ScaleNode와 같은 층위) — 태그 값에 대한 임계값/특정값 알람 규칙(잎 노드, 선택 사항).</summary>
public sealed class AlarmNode : StructureTreeNode
{
    public double? HH { get; set; }
    public double? H { get; set; }
    public double? L { get; set; }
    public double? LL { get; set; }

    /// <summary>이산/상태 태그(설비 상태 코드 등)의 특정값 일치(EQ)/불일치(NE) 알람 — HH/H/L/LL(&gt;=/&lt;= 비교)과 성격이 다릅니다.</summary>
    public double? EQ { get; set; }
    public double? NE { get; set; }
    public override string IconGlyph => "🔔";
    public override IReadOnlyList<Type> AllowedChildTypes => Array.Empty<Type>();
    public override IReadOnlyList<PropertyField> PropertySchema => new[]
    {
        new PropertyField("hh", "HH(위험 상한)", PropertyFieldType.Number,
            HelpText: "이 값을 넘으면 가장 심각한 경보가 발생합니다(빨강). 비워두면 이 등급은 검사하지 않습니다."),
        new PropertyField("h", "H(주의 상한)", PropertyFieldType.Number, HelpText: "HH보다 낮은 1차 경고 임계값입니다(주황)."),
        new PropertyField("l", "L(주의 하한)", PropertyFieldType.Number, HelpText: "값이 이보다 낮으면 1차 경고입니다(파랑)."),
        new PropertyField("ll", "LL(위험 하한)", PropertyFieldType.Number, HelpText: "값이 이보다 낮으면 가장 심각한 경보입니다(보라)."),
        new PropertyField("eq", "EQ(특정값 일치)", PropertyFieldType.Number,
            HelpText: "태그 값이 이 값과 정확히 같으면 알람이 발생합니다. 이산/상태 태그(예: 상태코드=고장)에 사용합니다. 비워두면 이 등급은 검사하지 않습니다.", Example: "예: 3 (고장 상태코드)"),
        new PropertyField("ne", "NE(특정값 불일치)", PropertyFieldType.Number,
            HelpText: "태그 값이 이 값과 다르면(그 값 이외의 모든 값) 알람이 발생합니다. 이산/상태 태그(예: 상태코드≠정상)에 사용합니다. 비워두면 이 등급은 검사하지 않습니다.", Example: "예: 1 (정상 가동 상태코드)"),
    };
}
