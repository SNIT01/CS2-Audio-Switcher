using System;
using System.Linq;
using System.Reflection;
using Game.Prefabs;
using Unity.Entities;

namespace SirenChanger;

internal static class UIToolSoundSettingsFields
{
	internal static readonly FieldInfo[] EntityFields = typeof(ToolUXSoundSettingsData)
		.GetFields(BindingFlags.Instance | BindingFlags.Public)
		.Where(static field => field.FieldType == typeof(Entity))
		.OrderBy(static field => field.Name, StringComparer.OrdinalIgnoreCase)
		.ToArray();
}
