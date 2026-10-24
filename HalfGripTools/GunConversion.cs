using System;
using System.Reflection;
using UnityEngine;

namespace HalfGripTools
{
	/// <summary>Helpers for mods that put their own gun in the SMG's place (see the RPG and Sniper Rifle mods for the whole recipe).</summary>
	public static class GunConversion
	{
		/// <summary>Copies the authored (serialized) fields one class declares from one component to another.</summary>
		public static void CopyFields(Type declaring, object from, object to)
		{
			foreach (FieldInfo field in declaring.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
			{
				if (field.IsStatic || field.IsInitOnly || field.IsLiteral || field.Name.StartsWith("<"))
				{
					continue;
				}
				bool authored = field.IsPublic ? !field.IsDefined(typeof(NonSerializedAttribute), false) : field.IsDefined(typeof(SerializeField), false);
				if (authored)
				{
					field.SetValue(to, field.GetValue(from));
				}
			}
		}
	}
}
