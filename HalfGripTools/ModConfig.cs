using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace HalfGripTools
{
	/// <summary>One setting of a mod, as bound with <see cref="ModConfig.Bind{T}"/>. <see cref="ModSetting{T}"/> is the typed one.</summary>
	public abstract class ModSetting
	{
		internal ModConfig Owner;

		public string Section { get; internal set; }

		public string Key { get; internal set; }

		public string Description { get; internal set; }

		public Type SettingType { get; internal set; }

		/// <summary>The value the mod bound it with (the one that is used when the file has none).</summary>
		public object DefaultValue { get; internal set; }

		public abstract object BoxedValue { get; set; }

		/// <summary>Raised when <c>Value</c> is set to something different, by the mod or after the file was read again.</summary>
		public event EventHandler SettingChanged;

		internal void RaiseChanged()
		{
			SettingChanged?.Invoke(this, EventArgs.Empty);
		}

		internal abstract string Serialize();

		internal abstract void Deserialize(string text);
	}

	public sealed class ModSetting<T> : ModSetting
	{
		private T value;

		public T Value
		{
			get { return value; }
			set
			{
				if (Equals(this.value, value))
				{
					return;
				}
				this.value = value;
				Owner.OnChanged();
				RaiseChanged();
			}
		}

		public override object BoxedValue
		{
			get { return value; }
			set { Value = (T)ModConfig.Convert(value, typeof(T)); }
		}

		internal void SetQuietly(T newValue)
		{
			value = newValue;
		}

		internal override string Serialize()
		{
			return ModConfig.Format(value);
		}

		internal override void Deserialize(string text)
		{
			value = (T)ModConfig.Parse(text, typeof(T));
		}
	}

	/// <summary>
	/// A mod's settings file (<c>ctx.Config</c>), <c>&lt;data folder&gt;\config\&lt;mod id&gt;.cfg</c>. The text format is the one BepInEx used
	/// (<c>[Section]</c>, a <c>## description</c> line, <c>Key = value</c>), so a 1.x file is read as it is. Entries a mod no longer binds stay in the file.
	/// A new entry is written when the mod finishes loading; a changed value is written at once.
	/// </summary>
	public sealed class ModConfig
	{
		private sealed class FileEntry
		{
			internal string Section;
			internal string Key;
			internal string Value;
		}

		private readonly string path;

		private readonly List<FileEntry> fromFile = new List<FileEntry>();

		private readonly List<ModSetting> settings = new List<ModSetting>();

		private bool dirty;

		public string FilePath => path;

		/// <summary>When true (the default) a changed value is written to the file at once.</summary>
		public bool SaveOnConfigSet { get; set; } = true;

		internal ModConfig(string path)
		{
			this.path = path;
			Read();
		}

		/// <summary>
		/// Adds a setting. If the file has a value for it, that is used; otherwise <paramref name="defaultValue"/>, and the entry is added to the file with
		/// the description. Types that work: numbers, bool, string and enums. A value that cannot be read gives the default, with a line in the log.
		/// </summary>
		public ModSetting<T> Bind<T>(string section, string key, T defaultValue, string description = "")
		{
			section = Clean(section);
			key = Clean(key);
			ModSetting existing = settings.FirstOrDefault(s => s.Section == section && s.Key == key);
			if (existing != null)
			{
				return existing as ModSetting<T> ?? throw new ArgumentException($"'{section}' / '{key}' was already bound as {existing.SettingType.Name}, not {typeof(T).Name}");
			}
			ModSetting<T> setting = new ModSetting<T>
			{
				Owner = this,
				Section = section,
				Key = key,
				Description = description ?? "",
				SettingType = typeof(T),
				DefaultValue = defaultValue
			};
			setting.SetQuietly(defaultValue);
			FileEntry entry = fromFile.FirstOrDefault(e => e.Section == section && e.Key == key);
			if (entry == null)
			{
				dirty = true;
			}
			else
			{
				try
				{
					setting.Deserialize(entry.Value);
				}
				catch (Exception e)
				{
					ModRuntime.Log.LogWarning($"{Path.GetFileName(path)}: '{key}' in [{section}] is '{entry.Value}', which is not a valid {typeof(T).Name} ({e.Message}). Using the default {Format(defaultValue)}.");
					setting.SetQuietly(defaultValue);
				}
			}
			settings.Add(setting);
			return setting;
		}

		/// <summary>Writes the file now.</summary>
		public void Save()
		{
			StringBuilder text = new StringBuilder();
			List<string> sections = settings.Select(s => s.Section).Concat(fromFile.Select(e => e.Section)).Distinct().ToList();
			foreach (string section in sections)
			{
				text.Append('[').Append(section).Append("]\n\n");
				foreach (ModSetting setting in settings.Where(s => s.Section == section))
				{
					if (setting.Description != "")
					{
						text.Append("## ").Append(setting.Description.Replace("\r", "").Replace("\n", "\n## ")).Append('\n');
					}
					text.Append("# Setting type: ").Append(setting.SettingType.Name).Append('\n');
					text.Append("# Default value: ").Append(Format(setting.DefaultValue)).Append('\n');
					if (setting.SettingType.IsEnum)
					{
						text.Append("# Acceptable values: ").Append(string.Join(", ", Enum.GetNames(setting.SettingType))).Append('\n');
					}
					text.Append(setting.Key).Append(" = ").Append(setting.Serialize()).Append("\n\n");
				}
				foreach (FileEntry entry in fromFile.Where(e => e.Section == section && !settings.Any(s => s.Section == e.Section && s.Key == e.Key)))
				{
					text.Append(entry.Key).Append(" = ").Append(entry.Value).Append("\n\n");
				}
			}
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				File.WriteAllText(path, text.ToString().Replace("\n", "\r\n"), new UTF8Encoding(false));
				dirty = false;
			}
			catch (Exception e)
			{
				ModRuntime.Log.LogError($"Could not write {path}: {e.Message}");
			}
		}

		internal void OnChanged()
		{
			dirty = true;
			if (SaveOnConfigSet)
			{
				Save();
			}
		}

		/// <summary>Called by the loader once a mod has loaded, so the entries it bound are in the file without every mod calling Save.</summary>
		internal void SaveIfDirty()
		{
			if (dirty)
			{
				Save();
			}
		}

		private void Read()
		{
			if (!File.Exists(path))
			{
				return;
			}
			try
			{
				string section = "";
				foreach (string raw in File.ReadAllLines(path))
				{
					string line = raw.Trim();
					if (line == "" || line.StartsWith("#"))
					{
						continue;
					}
					if (line.StartsWith("[") && line.EndsWith("]"))
					{
						section = line.Substring(1, line.Length - 2).Trim();
						continue;
					}
					int eq = line.IndexOf('=');
					if (eq > 0)
					{
						fromFile.Add(new FileEntry { Section = section, Key = line.Substring(0, eq).Trim(), Value = line.Substring(eq + 1).Trim() });
					}
				}
			}
			catch (Exception e)
			{
				ModRuntime.Log.LogError($"Could not read {path} ({e.Message}); the defaults are used.");
			}
		}

		/// <summary>Section and key names cannot hold the characters the file format uses.</summary>
		private static string Clean(string name)
		{
			if (name == null)
			{
				return "";
			}
			StringBuilder clean = new StringBuilder();
			foreach (char c in name)
			{
				if ("=\n\r\t\\\"'[]".IndexOf(c) < 0)
				{
					clean.Append(c);
				}
			}
			return clean.ToString().Trim();
		}

		internal static string Format(object value)
		{
			if (value == null)
			{
				return "";
			}
			if (value is float f)
			{
				return f.ToString("R", CultureInfo.InvariantCulture);
			}
			if (value is double d)
			{
				return d.ToString("R", CultureInfo.InvariantCulture);
			}
			if (value is bool b)
			{
				return b ? "true" : "false";
			}
			return System.Convert.ToString(value, CultureInfo.InvariantCulture);
		}

		internal static object Parse(string text, Type type)
		{
			if (type == typeof(string))
			{
				return text;
			}
			if (type.IsEnum)
			{
				return Enum.Parse(type, text.Trim(), true);
			}
			if (type == typeof(bool))
			{
				return bool.Parse(text.Trim());
			}
			return System.Convert.ChangeType(text.Trim(), type, CultureInfo.InvariantCulture);
		}

		internal static object Convert(object value, Type type)
		{
			if (value == null || type.IsInstanceOfType(value))
			{
				return value;
			}
			return type.IsEnum ? Enum.Parse(type, value.ToString(), true) : System.Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
		}
	}
}
