using System.Collections.ObjectModel;
using System.Reflection;
using NodeSharp.Contracts.Models;

namespace NodeSharp.Editor.Structure;

/// <summary>
/// Class명 : 구조 설정 트리 ↔ DTO 매퍼
/// 역활 및 기능 : <see cref="StructureTreeNode"/>(6종 구체 클래스, Editor 메모리 상태)와
/// <see cref="StructureTreeNodeDto"/>/<see cref="DeviceTreeDto"/>(device.json 저장 포맷) 사이를
/// 왕복 변환하는 정적 도우미
///
/// (ED-D03) <see cref="StructureNodePropertyDialog"/>가 이미 "PropertyField.Key == C# 프로퍼티
/// 이름(대소문자 무시)"이라는 규칙으로 리플렉션 기반 범용 편집기를 만든 것과 동일한 규칙을 재사용해,
/// 6종 클래스마다 별도 매핑 코드를 두지 않고 <see cref="StructureTreeNode.PropertySchema"/>를 순회하는
/// 리플렉션 하나로 저장/복원을 모두 처리합니다.
/// </summary>
public static class StructureTreeMapper
{
    /// <summary>노드 타입 → <see cref="StructureTreeNodeDto.NodeType"/> 문자열 — <see cref="StructureView.TypeLabels"/>(한글 표시용)와는 별개로, 저장 파일에 그대로 남는 값이라 영문 타입명을 씁니다.</summary>
    private static readonly IReadOnlyDictionary<Type, string> TypeNames = new Dictionary<Type, string>
    {
        [typeof(DeviceNode)] = "Device",
        [typeof(PlcNode)] = "Plc",
        [typeof(DeviceMapNode)] = "DeviceMap",
        [typeof(TagNode)] = "Tag",
        [typeof(ScaleNode)] = "Scale",
        [typeof(AlarmNode)] = "Alarm",
    };

    /// <summary><see cref="TypeNames"/>의 역방향 — 저장 파일의 <see cref="StructureTreeNodeDto.NodeType"/> 문자열로 새 인스턴스를 만드는 팩토리.</summary>
    private static readonly IReadOnlyDictionary<string, Func<StructureTreeNode>> Factories = new Dictionary<string, Func<StructureTreeNode>>
    {
        ["Device"] = () => new DeviceNode(),
        ["Plc"] = () => new PlcNode(),
        ["DeviceMap"] = () => new DeviceMapNode(),
        ["Tag"] = () => new TagNode(),
        ["Scale"] = () => new ScaleNode(),
        ["Alarm"] = () => new AlarmNode(),
    };

    /// <summary><paramref name="roots"/>(트리 루트 목록, <see cref="StructureView.Devices"/>가 그대로 전달)를 device.json에 쓸 <see cref="DeviceTreeDto"/>로 변환합니다.</summary>
    public static DeviceTreeDto ToDto(IEnumerable<StructureTreeNode> roots)
    {
        return new DeviceTreeDto(roots.Select(ToNodeDto).ToList());
    }

    /// <summary><paramref name="node"/> 1개(및 그 자손 전체)를 <see cref="StructureTreeNodeDto"/>로 변환합니다. <see cref="StructureTreeNode.PropertySchema"/>의 각 필드를 리플렉션으로 읽어 <see cref="StructureTreeNodeDto.Properties"/>에 담습니다.</summary>
    private static StructureTreeNodeDto ToNodeDto(StructureTreeNode node)
    {
        var type = node.GetType();
        var properties = new Dictionary<string, string?>();

        foreach (var field in node.PropertySchema)
        {
            var property = type.GetProperty(field.Key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            properties[field.Key] = property?.GetValue(node)?.ToString();
        }

        return new StructureTreeNodeDto(
            node.Id,
            TypeNames.TryGetValue(type, out var typeName) ? typeName : type.Name,
            node.Name,
            node.Description,
            properties,
            node.Children.Select(ToNodeDto).ToList());
    }

    /// <summary>device.json에서 읽은 <paramref name="dto"/>를 <see cref="StructureView.Devices"/>에 그대로 대입할 수 있는 <see cref="StructureTreeNode"/> 루트 목록으로 복원합니다.</summary>
    public static ObservableCollection<StructureTreeNode> FromDto(DeviceTreeDto dto)
    {
        var result = new ObservableCollection<StructureTreeNode>();
        foreach (var nodeDto in dto.Devices)
        {
            result.Add(FromNodeDto(nodeDto));
        }

        return result;
    }

    /// <summary>
    /// <paramref name="dto"/> 1개(및 그 자손 전체)를 <see cref="StructureTreeNodeDto.NodeType"/>에 맞는
    /// 구체 <see cref="StructureTreeNode"/>로 복원합니다. <see cref="StructureTreeNode.Id"/>는 <c>init</c>
    /// 접근자지만 리플렉션 <see cref="PropertyInfo.SetValue"/>는 컴파일 타임 <c>init</c> 제약을 우회할 수
    /// 있어(CLR 수준에서는 일반 setter 메서드 호출과 동일) 원래 Id를 그대로 복원합니다 — 클래스 상단
    /// remarks의 "Id 보존" 이유 참고. 저장 파일의 <see cref="StructureTreeNodeDto.NodeType"/>이 알 수
    /// 없는 값이면(수동으로 손상시킨 파일 등) <see cref="KeyNotFoundException"/>이 발생합니다 — 이 Step은
    /// 파일이 아예 없는 경우(최초 실행)만 정상 처리 범위이고, 스키마가 깨진 저장 파일에 대한 복구
    /// 로직은 <c>JsonWriteService.ReadAsync</c>의 역직렬화 실패 격리(v2.53) 이상으로는 다루지 않습니다.
    /// </summary>
    private static StructureTreeNode FromNodeDto(StructureTreeNodeDto dto)
    {
        var node = Factories[dto.NodeType]();

        var idProperty = typeof(StructureTreeNode).GetProperty(nameof(StructureTreeNode.Id));
        idProperty?.SetValue(node, dto.Id);

        node.Name = dto.Name;
        node.Description = dto.Description;

        var type = node.GetType();
        foreach (var (key, raw) in dto.Properties)
        {
            var property = type.GetProperty(key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property is null || !property.CanWrite)
            {
                continue;
            }

            TrySetTypedValue(node, property, raw);
        }

        foreach (var childDto in dto.Children)
        {
            node.Children.Add(FromNodeDto(childDto));
        }

        return node;
    }

    /// <summary>
    /// <paramref name="property"/>의 실제 타입(<c>string</c>/<c>int</c>/<c>double</c>/<c>double?</c>/<c>bool</c>)에
    /// 맞게 <paramref name="raw"/>를 변환해 <paramref name="node"/>에 씁니다 —
    /// <c>StructureNodePropertyDialog.TrySetTypedValue</c>(ED-D02a)와 동일한 변환 규칙입니다(널 가능
    /// 숫자 필드는 <paramref name="raw"/>가 null/빈 문자열이면 null로, 그 외 변환 실패는 조용히 건너뜀).
    /// </summary>
    private static void TrySetTypedValue(StructureTreeNode node, PropertyInfo property, string? raw)
    {
        var underlyingType = Nullable.GetUnderlyingType(property.PropertyType);
        var isNullable = underlyingType is not null;
        var targetType = underlyingType ?? property.PropertyType;

        if (isNullable && string.IsNullOrWhiteSpace(raw))
        {
            property.SetValue(node, null);
            return;
        }

        if (raw is null)
        {
            return;
        }

        if (targetType == typeof(string))
        {
            property.SetValue(node, raw);
        }
        else if (targetType == typeof(int))
        {
            if (int.TryParse(raw, out var i))
            {
                property.SetValue(node, i);
            }
        }
        else if (targetType == typeof(double))
        {
            if (double.TryParse(raw, out var d))
            {
                property.SetValue(node, d);
            }
        }
        else if (targetType == typeof(bool))
        {
            if (bool.TryParse(raw, out var b))
            {
                property.SetValue(node, b);
            }
        }
    }
}
