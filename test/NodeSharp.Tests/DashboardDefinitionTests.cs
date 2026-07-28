using System.Text.Json;
using NodeSharp.Contracts.Models;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="DashboardDefinition"/>/<see cref="DashboardTabDto"/>/<see cref="DashboardGroupDto"/>
/// (CT-03c, 02번 설계 문서 9번 탭 카드 11)에 대한 단위 테스트입니다.
/// 완료 기준: Tab &gt; Group &gt; Widget 3단계 계층이 dashboard.json(System.Text.Json)으로
/// 직렬화/역직렬화 가능한지 확인.
/// </summary>
public class DashboardDefinitionTests
{
    private static DashboardDefinition CreateSample() => new(
        Tabs: new List<DashboardTabDto>
        {
            new(Id: "tab-1", Name: "1호기 현황",
                Groups: new List<DashboardGroupDto>
                {
                    new(Id: "group-1", Name: "압력/온도", Width: 6,
                        WidgetNodeIds: new List<string> { "ui-gauge-1", "ui-gauge-2" }),
                    new(Id: "group-2", Name: "제어", Width: 3,
                        WidgetNodeIds: new List<string> { "ui-button-1" }),
                }),
            new(Id: "tab-2", Name: "2호기 현황",
                Groups: new List<DashboardGroupDto>()),
        });

    [Fact]
    public void Tab_Group_Widget_3단계_계층이_그대로_보관된다()
    {
        var dashboard = CreateSample();

        Assert.Equal(2, dashboard.Tabs.Count);
        Assert.Equal("1호기 현황", dashboard.Tabs[0].Name);
        Assert.Equal(2, dashboard.Tabs[0].Groups.Count);
        Assert.Equal("압력/온도", dashboard.Tabs[0].Groups[0].Name);
        Assert.Equal(2, dashboard.Tabs[0].Groups[0].WidgetNodeIds.Count);
        Assert.Contains("ui-gauge-1", dashboard.Tabs[0].Groups[0].WidgetNodeIds);
        Assert.Empty(dashboard.Tabs[1].Groups);
    }

    [Fact]
    public void SystemTextJson_왕복_시_Tab_Group_Widget_3단계_계층이_모두_보존된다()
    {
        // dashboard.json은 flows.json 계열과 동일하게 System.Text.Json을 사용한다
        // (02번 문서 2번 탭 카드 3 방침 — 정적 스키마를 가진 설정 파일).
        var original = CreateSample();

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<DashboardDefinition>(json);

        Assert.NotNull(restored);
        Assert.Equal(original.Tabs.Count, restored!.Tabs.Count);

        var tab0 = restored.Tabs[0];
        Assert.Equal("tab-1", tab0.Id);
        Assert.Equal("1호기 현황", tab0.Name);
        Assert.Equal(2, tab0.Groups.Count);

        var group0 = tab0.Groups[0];
        Assert.Equal("group-1", group0.Id);
        Assert.Equal("압력/온도", group0.Name);
        Assert.Equal(6, group0.Width);
        Assert.Equal(2, group0.WidgetNodeIds.Count);
        Assert.Equal("ui-gauge-1", group0.WidgetNodeIds[0]);
        Assert.Equal("ui-gauge-2", group0.WidgetNodeIds[1]);

        Assert.Empty(restored.Tabs[1].Groups);
    }

    [Fact]
    public void 빈_Tabs로도_인스턴스를_생성할_수_있다()
    {
        var dashboard = new DashboardDefinition(new List<DashboardTabDto>());

        Assert.Empty(dashboard.Tabs);
    }
}
