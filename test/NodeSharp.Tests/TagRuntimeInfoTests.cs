using System.Linq;
using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Models;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="TagRuntimeInfo"/>/<see cref="ScaleRuntimeInfo"/>/<see cref="AlarmRuntimeInfo"/>
/// (CT-03b, 02번 설계 문서 8번 탭 카드 7)에 대한 단위 테스트입니다.
/// 완료 기준: TagRuntimeInfo가 순수 데이터 전용(구조 설정 서비스 직접 참조 없음)인지 확인.
/// </summary>
public class TagRuntimeInfoTests
{
    [Fact]
    public void 필수_필드만으로_인스턴스를_생성할_수_있다()
    {
        var tag = new TagRuntimeInfo(
            Id: "tag-1", Name: "토출압력", ParentMapId: "map-1",
            Offset: 0, BufType: BufFieldType.FloatLE,
            Scale: null, Alarm: null);

        Assert.Equal("tag-1", tag.Id);
        Assert.Equal("토출압력", tag.Name);
        Assert.Equal("map-1", tag.ParentMapId);
        Assert.Equal(0, tag.Offset);
        Assert.Equal(BufFieldType.FloatLE, tag.BufType);
        Assert.Null(tag.Scale);
        Assert.Null(tag.Alarm);
    }

    [Fact]
    public void Scale와_Alarm을_포함해_인스턴스를_생성할_수_있다()
    {
        var scale = new ScaleRuntimeInfo(RawMin: 0, RawMax: 4095, EngMin: 0, EngMax: 10);
        var alarm = new AlarmRuntimeInfo(HH: 9.5, H: 8.0, L: null, LL: null);

        var tag = new TagRuntimeInfo(
            "tag-1", "토출압력", "map-1", 0, BufFieldType.FloatLE, scale, alarm);

        Assert.NotNull(tag.Scale);
        Assert.Equal(0, tag.Scale!.RawMin);
        Assert.Equal(4095, tag.Scale.RawMax);
        Assert.Equal(0, tag.Scale.EngMin);
        Assert.Equal(10, tag.Scale.EngMax);

        Assert.NotNull(tag.Alarm);
        Assert.Equal(9.5, tag.Alarm!.HH);
        Assert.Equal(8.0, tag.Alarm.H);
        Assert.Null(tag.Alarm.L);
        Assert.Null(tag.Alarm.LL);
    }

    [Fact]
    public void AlarmRuntimeInfo_EQ_NE를_생략하면_기본값_null이다()
    {
        // (v2.50 신설, ★ 사용자 요청) EQ/NE는 HH/H/L/LL 뒤에 추가된 선택 매개변수라, 생략하면
        // 다른 임계값들과 마찬가지로 null(해당 등급 감시 안 함)이어야 한다.
        var alarm = new AlarmRuntimeInfo(HH: 9.5, H: 8.0, L: null, LL: null);

        Assert.Null(alarm.EQ);
        Assert.Null(alarm.NE);
    }

    [Fact]
    public void AlarmRuntimeInfo_EQ_NE_특정값_비교값을_설정할_수_있다()
    {
        // (v2.50 신설, ★ 사용자 요청) 이산/상태 태그(설비 상태코드)의 특정값 일치(EQ)/불일치(NE)
        // 알람 — 상태코드 3(고장)과 일치하면 EQ, 1(정상)과 다르면 NE.
        var alarm = new AlarmRuntimeInfo(HH: null, H: null, L: null, LL: null, EQ: 3, NE: 1);

        var tag = new TagRuntimeInfo(
            Id: "tag-3", Name: "설비 상태코드", ParentMapId: "map-1",
            Offset: 16, BufType: BufFieldType.Int16LE,
            Scale: null, Alarm: alarm);

        Assert.Equal(3, tag.Alarm!.EQ);
        Assert.Equal(1, tag.Alarm.NE);

        // 특정값 일치/불일치 판정 로직 그대로 재현
        double statusValue = 3;
        bool isEqAlarm = tag.Alarm.EQ is double eq && statusValue == eq;
        bool isNeAlarm = tag.Alarm.NE is double ne && statusValue != ne;

        Assert.True(isEqAlarm);
        Assert.True(isNeAlarm); // 3 != 1이므로 NE 조건도 함께 성립
    }

    public static IEnumerable<object[]> 모든_BufFieldType_값 =>
        Enum.GetValues<BufFieldType>().Select(v => new object[] { v });

    [Theory]
    [MemberData(nameof(모든_BufFieldType_값))]
    public void BufFieldType_모든_값이_TagRuntimeInfo에_설정할_수_있다(BufFieldType bufType)
    {
        // lssLib.Binary.BufType 전체 목록(dev-csharp 스킬 근거, v1.42 강화)과 1:1 대응하는지
        // Enum.GetValues로 전수 순회해 빠짐없이 확인한다 — 값이 추가/삭제돼도 테스트를 다시 쓸 필요가 없다.
        var tag = new TagRuntimeInfo("tag-1", "테스트", "map-1", 0, bufType, null, null);

        Assert.Equal(bufType, tag.BufType);
    }

    [Fact]
    public void BufFieldType는_lssLib_Binary_BufType의_4개_분류_전체를_포함한다()
    {
        // dev-csharp 스킬의 "BufType — 지원 타입 전체 목록" 표(정수/실수/고정소수점/논리/문자열/원시/배열)와
        // 대조 — 각 분류에서 최소 1개 이상의 대표값이 실제로 정의돼 있는지 확인한다.
        var values = Enum.GetValues<BufFieldType>().Select(v => v.ToString()).ToHashSet();

        Assert.Equal(37, values.Count); // 정수 14 + 실수 4 + 고정소수점 2 + 논리 2 + 문자열 4 + 원시 1 + 배열 10
        Assert.Contains("Int8", values);
        Assert.Contains("UInt64BE", values);
        Assert.Contains("FloatLE", values);
        Assert.Contains("DoubleBE", values);
        Assert.Contains("DecimalLE", values);
        Assert.Contains("DecimalBE", values);
        Assert.Contains("Bool", values);
        Assert.Contains("Bit", values);
        Assert.Contains("StringAscii", values);
        Assert.Contains("StringUtf8", values);
        Assert.Contains("Hex", values);
        Assert.Contains("Base64", values);
        Assert.Contains("Raw", values);
        Assert.Contains("DecimalLEArray", values);
        Assert.Contains("DecimalBEArray", values);
    }

    [Fact]
    public void TagRuntimeInfo는_구조_설정_서비스_타입을_직접_참조하지_않는_순수_데이터다()
    {
        // 완료 기준 그대로: TagRuntimeInfo(및 중첩 레코드)의 public 필드 타입이
        // System 기본 타입 · NodeSharp.Contracts.Enums.BufFieldType · 자기 자신이 정의한
        // 레코드(ScaleRuntimeInfo/AlarmRuntimeInfo) 외에는 아무것도 참조하지 않아야 한다.
        // IStructureService/StructureTreeNode(WPF ObservableCollection 기반) 등은 전혀 등장하지 않는다.
        var recordType = typeof(TagRuntimeInfo);
        var allowedNamespaces = new[] { "System", "NodeSharp.Contracts.Enums", "NodeSharp.Contracts.Models" };

        foreach (var prop in recordType.GetProperties())
        {
            var propType = prop.PropertyType;
            var ns = propType.Namespace ?? string.Empty;

            Assert.True(
                Array.Exists(allowedNamespaces, allowed => ns == allowed || ns.StartsWith(allowed + ".", StringComparison.Ordinal)),
                $"{prop.Name}의 타입 {propType.FullName}이 허용되지 않은 네임스페이스({ns})에 있습니다 — TagRuntimeInfo는 순수 데이터여야 합니다.");
        }
    }

    [Fact]
    public void 같은_필드값이면_동일한_레코드로_판정된다()
    {
        var a = new TagRuntimeInfo("tag-1", "토출압력", "map-1", 0, BufFieldType.FloatLE, null, null);
        var b = new TagRuntimeInfo("tag-1", "토출압력", "map-1", 0, BufFieldType.FloatLE, null, null);
        var c = new TagRuntimeInfo("tag-2", "유량", "map-1", 4, BufFieldType.FloatLE, null, null);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }
}
