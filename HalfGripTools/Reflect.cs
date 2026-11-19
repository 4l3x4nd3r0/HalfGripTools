using System;
using System.Reflection;

namespace HalfGripTools
{
	/// <summary>
	/// Reading and writing a private field of a game object, for the few places the loader needs it. Plain reflection (this is what Harmony's
	/// Traverse did), so HalfGripTools does not need Harmony for it. Each use names the game class and field it reaches: these are the lines that
	/// break first when the game changes.
	/// </summary>
	internal static class Reflect
	{
		private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

		private static FieldInfo Find(Type type, string name)
		{
			for (; type != null; type = type.BaseType)
			{
				FieldInfo field = type.GetField(name, Any);
				if (field != null)
				{
					return field;
				}
			}
			return null;
		}

		/// <summary>The value of the field, or the default (null) if the object or the field is not there.</summary>
		internal static T GetField<T>(object target, string name)
		{
			FieldInfo field = target == null ? null : Find(target.GetType(), name);
			return field != null && field.GetValue(target) is T value ? value : default(T);
		}

		/// <summary>Sets the field. Returns false (and does nothing) if the object or the field is not there.</summary>
		internal static bool SetField(object target, string name, object value)
		{
			FieldInfo field = target == null ? null : Find(target.GetType(), name);
			if (field == null)
			{
				return false;
			}
			field.SetValue(target, value);
			return true;
		}
	}
}
